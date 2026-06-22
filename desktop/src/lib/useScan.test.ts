// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

/**
 * Tests for the pure helpers extracted from useScan:
 *   - computeAccountRouting  (stage + seed selection/pstNames)
 *   - filterSourcesBySelection (sortedSources filtering)
 *
 * useScan itself has no exported reducer — these helpers are the test seam.
 */

import { describe, it, expect } from "vitest";
import { computeAccountRouting, filterSourcesBySelection } from "./useScan";
import type { Account, ProfileSourceRow } from "./types";

const acct = (id: string, email: string | null, seg: string): Account => ({
  id,
  folderSegment: seg,
  accountPath: id,
  store: "ImapMail",
  email,
  host: seg,
  addressResolution: email ? "identity" : "not-found",
});

const row = (
  id: string,
  accountId: string | null,
  tfp: string[],
): ProfileSourceRow => ({
  id,
  path: id,
  accountId,
  targetFolderPath: tfp,
  displayName: tfp.join(" / "),
  messages: 1,
  bytes: 100,
  sourceBytes: 200,
  msfPath: null,
  warnings: 0,
  skipped: 0,
  dateFrom: null,
  dateTo: null,
});

const accounts2 = [
  acct("A", "alice@example.com", "imap.example.com"),
  acct("B", "bob@example.com", "imap2.example.com"),
];

const rows2 = [
  row("a1", "A", ["imap.example.com", "Inbox"]),
  row("a2", "A", ["imap.example.com", "Sent"]),
  row("b1", "B", ["imap2.example.com", "Inbox"]),
];

// ── computeAccountRouting ─────────────────────────────────────────────────────

describe("computeAccountRouting", () => {
  it("routes to 'accounts' stage when there are ≥2 discovered accounts", () => {
    const result = computeAccountRouting(rows2, accounts2);
    expect(result.stage).toBe("accounts");
  });

  it("seeds selectedAccountKeys with ALL account keys on multi-account", () => {
    const { selectedAccountKeys } = computeAccountRouting(rows2, accounts2);
    expect(selectedAccountKeys.has("A")).toBe(true);
    expect(selectedAccountKeys.has("B")).toBe(true);
    expect(selectedAccountKeys.size).toBe(2);
  });

  it("seeds pstNames from each group's defaultPstName", () => {
    const { pstNames } = computeAccountRouting(rows2, accounts2);
    // email label used → sanitizePstName("alice@example.com") = "alice@example.com"
    expect(pstNames["A"]).toBe("alice@example.com");
    expect(pstNames["B"]).toBe("bob@example.com");
  });

  it("routes to 'review' when there is exactly 1 account", () => {
    const rows1 = [row("a1", "A", ["imap.example.com", "Inbox"])];
    const accounts1 = [acct("A", "alice@example.com", "imap.example.com")];
    const result = computeAccountRouting(rows1, accounts1);
    expect(result.stage).toBe("review");
  });

  it("routes to 'review' when there are 0 accounts (files mode fallback)", () => {
    const result = computeAccountRouting([], []);
    expect(result.stage).toBe("review");
  });

  it("returns empty selectedAccountKeys and pstNames for single-account", () => {
    const rows1 = [row("a1", "A", ["imap.example.com", "Inbox"])];
    const accounts1 = [acct("A", null, "imap.example.com")];
    const { selectedAccountKeys, pstNames } = computeAccountRouting(rows1, accounts1);
    expect(selectedAccountKeys.size).toBe(0);
    expect(Object.keys(pstNames)).toHaveLength(0);
  });
});

// ── filterSourcesBySelection ──────────────────────────────────────────────────

describe("filterSourcesBySelection", () => {
  it("returns only rows for selected accounts in multi-account profile mode", () => {
    const selected = new Set(["A"]);
    const filtered = filterSourcesBySelection(rows2, selected, "profile", accounts2);
    expect(filtered.map((r) => r.id).sort()).toEqual(["a1", "a2"]);
  });

  it("returns all rows when both accounts are selected", () => {
    const selected = new Set(["A", "B"]);
    const filtered = filterSourcesBySelection(rows2, selected, "profile", accounts2);
    expect(filtered).toHaveLength(3);
  });

  it("does NOT filter in files mode (single-account path)", () => {
    const selected = new Set(["A"]); // irrelevant for files mode
    // In files mode rows have no accountId
    const fileRows = [
      row("f1", null, ["SomeFolder"]),
      row("f2", null, ["OtherFolder"]),
    ];
    const filtered = filterSourcesBySelection(fileRows, selected, "files", []);
    expect(filtered).toHaveLength(2);
  });

  it("does NOT filter when only 1 account exists (single-account profile)", () => {
    const rows1 = [row("a1", "A", ["imap.example.com", "Inbox"])];
    const accounts1 = [acct("A", "alice@example.com", "imap.example.com")];
    const selected = new Set(["A"]);
    const filtered = filterSourcesBySelection(rows1, selected, "profile", accounts1);
    expect(filtered).toHaveLength(1);
  });
});
