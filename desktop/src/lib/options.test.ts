// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import {
  validateFolderName,
  normalizeFolderName,
  findDuplicateFolderIds,
  defaultOptions,
  SPLIT_PRESETS,
  OVERSIZE_MAX_GB,
  resolveCustomGbToMb,
  buildPstPreview,
  buildConfigFromOptions,
} from "./options";
import type { SourceRow } from "./types";

describe("validateFolderName", () => {
  it("accepts a normal name", () => {
    expect(validateFolderName("Inbox")).toBeNull();
  });
  it("rejects empty / whitespace-only", () => {
    expect(validateFolderName("")).toMatch(/empty/);
    expect(validateFolderName("   ")).toMatch(/empty/);
  });
  it("rejects path separators", () => {
    expect(validateFolderName("a/b")).toMatch(/\\|\//);
    expect(validateFolderName("a\\b")).toMatch(/\\|\//);
  });
  it("rejects control characters", () => {
    expect(validateFolderName("a" + String.fromCharCode(1) + "b")).toMatch(/control/);
  });
  it("rejects leading/trailing spaces", () => {
    expect(validateFolderName(" Inbox")).toMatch(/space/);
    expect(validateFolderName("Inbox ")).toMatch(/space/);
  });
  it("accepts leading/trailing dots (folder names are MAPI-internal, not filesystem names)", () => {
    expect(validateFolderName(".Inbox")).toBeNull();
    expect(validateFolderName("Inbox.")).toBeNull();
  });
  it("rejects reserved Windows device names (with or without an extension)", () => {
    expect(validateFolderName("CON")).toMatch(/reserved/i);
    expect(validateFolderName("com1")).toMatch(/reserved/i);
    expect(validateFolderName("LPT9")).toMatch(/reserved/i);
    expect(validateFolderName("CON.txt")).toMatch(/reserved/i);
    expect(validateFolderName("NUL.anything")).toMatch(/reserved/i);
  });
});

describe("normalizeFolderName", () => {
  it("trims and lowercases", () => {
    expect(normalizeFolderName(" Inbox ")).toBe("inbox");
    expect(normalizeFolderName("INBOX")).toBe("inbox");
  });
});

describe("findDuplicateFolderIds", () => {
  it("flags case/space-insensitive duplicates", () => {
    const dups = findDuplicateFolderIds([
      { sourceId: "a", name: "Inbox", messages: 1, bytes: 1 },
      { sourceId: "b", name: " inbox ", messages: 1, bytes: 1 },
      { sourceId: "c", name: "Sent", messages: 1, bytes: 1 },
    ]);
    expect([...dups].sort()).toEqual(["a", "b"]);
  });
  it("returns empty when all unique", () => {
    const dups = findDuplicateFolderIds([
      { sourceId: "a", name: "Inbox", messages: 1, bytes: 1 },
      { sourceId: "b", name: "Sent", messages: 1, bytes: 1 },
    ]);
    expect(dups.size).toBe(0);
  });
});

describe("defaultOptions", () => {
  it("returns fresh defaults: mirror, 50 GB cap, no oversize", () => {
    const a = defaultOptions();
    expect(a.folderMapping).toBe("mirror");
    expect(a.maxSizeMB).toBe(51200);
    expect(a.allowOversize).toBe(false);
    expect(a.flattenFolderName).toBe("Imported Mail");
    expect(a.renames).toEqual({});
    expect(defaultOptions()).not.toBe(a); // fresh object each call
  });
});

describe("resolveCustomGbToMb", () => {
  it("converts whole GB to MB", () => {
    expect(resolveCustomGbToMb("2", false)).toEqual({ mb: 2048 });
    expect(resolveCustomGbToMb("50", false)).toEqual({ mb: 51200 });
  });
  it("rounds fractional GB", () => {
    expect(resolveCustomGbToMb("1.5", false)).toEqual({ mb: 1536 });
  });
  it("rejects empty / non-numeric", () => {
    expect(resolveCustomGbToMb("", false)).toHaveProperty("error");
    expect(resolveCustomGbToMb("abc", false)).toHaveProperty("error");
  });
  it("rejects below 1 GB", () => {
    expect(resolveCustomGbToMb("0.5", false)).toHaveProperty("error");
  });
  it("caps at 50 GB without oversize", () => {
    expect(resolveCustomGbToMb("60", false)).toHaveProperty("error");
  });
  it("allows up to OVERSIZE_MAX_GB with oversize", () => {
    expect(resolveCustomGbToMb("60", true)).toEqual({ mb: 60 * 1024 });
    expect(resolveCustomGbToMb(String(OVERSIZE_MAX_GB + 1), true)).toHaveProperty("error");
  });
});

describe("SPLIT_PRESETS", () => {
  it("starts with the 50 GB cap labelled honestly", () => {
    expect(SPLIT_PRESETS[0]).toEqual({ label: "Up to 50 GB", mb: 51200 });
    expect(SPLIT_PRESETS.map((p) => p.mb)).toEqual([51200, 2048, 5120, 10240, 20480]);
  });
});

function srow(over: Partial<SourceRow>): SourceRow {
  return {
    id: "x", path: "x.mbox", displayName: "x", messages: 0, bytes: 0,
    sourceBytes: 0, dateFrom: null, dateTo: null, warnings: 0, skipped: 0,
    ...over,
  };
}

const inbox = srow({ id: "inbox", displayName: "Inbox", path: "Inbox.mbox", messages: 100, bytes: 1000 });
const sent = srow({ id: "sent", displayName: "Sent", path: "Sent.mbox", messages: 50, bytes: 500 });
const empty = srow({ id: "empty", displayName: "Empty", path: "Empty.mbox", messages: 0, bytes: 0 });
const srcs = [inbox, sent, empty];
const allIds = new Set(["inbox", "sent", "empty"]);

describe("buildPstPreview", () => {
  it("Mirror: one folder per effective source, using rename or displayName", () => {
    const opts = { ...defaultOptions(), renames: { sent: "Sent Mail" } };
    const p = buildPstPreview(srcs, allIds, true, opts, "Personal");
    expect(p.pstName).toBe("Personal");
    expect(p.folders.map((f) => [f.name, f.messages])).toEqual([
      ["Inbox", 100],
      ["Sent Mail", 50],
    ]); // empty dropped by skipEmpty
    expect(p.totalBytes).toBe(1500);
  });

  it("Mirror: shows an edited-to-empty rename verbatim (no silent fallback)", () => {
    const opts = { ...defaultOptions(), renames: { inbox: "   " } };
    const p = buildPstPreview(srcs, allIds, true, opts, "Personal");
    expect(p.folders[0]).toEqual({ sourceId: "inbox", name: "   ", messages: 100, bytes: 1000 });
  });

  it("Flatten: a single folder with summed totals and the flatten name", () => {
    const opts = { ...defaultOptions(), folderMapping: "flatten" as const, flattenFolderName: "Archive" };
    const p = buildPstPreview(srcs, allIds, true, opts, "Personal");
    expect(p.folders).toEqual([{ sourceId: "__flatten__", name: "Archive", messages: 150, bytes: 1500 }]);
  });

  it("estimatedParts boundaries", () => {
    const mb = (n: number) => ({ ...defaultOptions(), maxSizeMB: n });
    const oneByteOver = srow({ id: "a", messages: 1, bytes: 1024 * 1024 + 1 });
    const exact = srow({ id: "a", messages: 1, bytes: 1024 * 1024 });
    const ids = new Set(["a"]);
    expect(buildPstPreview([exact], ids, false, mb(1), "P").estimatedParts).toBe(1); // == cap
    expect(buildPstPreview([oneByteOver], ids, false, mb(1), "P").estimatedParts).toBe(2); // cap+1
    expect(buildPstPreview([empty], new Set(["empty"]), false, mb(1), "P").estimatedParts).toBe(1); // 0 bytes
  });
});

describe("buildConfigFromOptions", () => {
  it("Mirror: targetFolder only when rename differs from displayName", () => {
    const opts = { ...defaultOptions(), renames: { inbox: "Inbox", sent: "Sent Mail" } };
    const { config, outputDir, pstName } = buildConfigFromOptions(srcs, allIds, true, opts, "D:\\out\\Personal.pst");
    expect(outputDir).toBe("D:\\out");
    expect(pstName).toBe("Personal");
    const out = config.outputs[0];
    expect(out.name).toBe("Personal");
    expect(out.folderMapping).toBe("mirror");
    expect(out.maxSizeMB).toBe(51200);
    expect(out.includeEmptyFolders).toBe(false); // skipEmpty true
    expect(out.sources).toEqual([
      { path: "Inbox.mbox", type: "mbox" },
      { path: "Sent.mbox", type: "mbox", targetFolder: "Sent Mail" },
    ]);
  });

  it("Flatten: every source gets the flatten folder as targetFolder", () => {
    const opts = { ...defaultOptions(), folderMapping: "flatten" as const, flattenFolderName: "Archive" };
    const { config } = buildConfigFromOptions(srcs, allIds, false, opts, "D:\\out\\Personal.pst");
    expect(config.outputs[0].includeEmptyFolders).toBe(true); // skipEmpty false
    expect(config.outputs[0].sources).toEqual([
      { path: "Inbox.mbox", type: "mbox", targetFolder: "Archive" },
      { path: "Sent.mbox", type: "mbox", targetFolder: "Archive" },
      { path: "Empty.mbox", type: "mbox", targetFolder: "Archive" },
    ]);
  });

  it("throws when no effective rows", () => {
    expect(() => buildConfigFromOptions(srcs, new Set<string>(), false, defaultOptions(), "D:\\out\\P.pst"))
      .toThrow(/at least one folder/);
  });

  it("rejects a non-.pst output path", () => {
    expect(() => buildConfigFromOptions(srcs, allIds, true, defaultOptions(), "D:\\out\\P.txt"))
      .toThrow(/\.pst/);
  });

  it("throws on an invalid mirror rename (engine never sees a bad targetFolder)", () => {
    const opts = { ...defaultOptions(), renames: { inbox: "a/b" } };
    expect(() => buildConfigFromOptions(srcs, allIds, true, opts, "D:\\out\\P.pst")).toThrow(/contain/);
  });

  it("throws on an invalid flatten folder name", () => {
    const opts = { ...defaultOptions(), folderMapping: "flatten" as const, flattenFolderName: "  " };
    expect(() => buildConfigFromOptions(srcs, allIds, true, opts, "D:\\out\\P.pst")).toThrow(/empty/);
  });

  it("throws on duplicate mirror folder names", () => {
    const opts = { ...defaultOptions(), renames: { inbox: "Mail", sent: "Mail" } };
    expect(() => buildConfigFromOptions(srcs, allIds, true, opts, "D:\\out\\P.pst")).toThrow(/same name/i);
  });
});

describe("source order propagation (guardrails)", () => {
  function mkRow(over: Partial<SourceRow>): SourceRow {
    return {
      id: "x", path: "x.mbox", displayName: "x", messages: 1, bytes: 1,
      sourceBytes: 1, dateFrom: null, dateTo: null, warnings: 0, skipped: 0, ...over,
    };
  }
  const a = mkRow({ id: "a", path: "a.mbox", displayName: "Apple" });
  const b = mkRow({ id: "b", path: "b.mbox", displayName: "Banana" });
  const ids = new Set(["a", "b"]);

  it("buildPstPreview folders follow the given source array order", () => {
    expect(buildPstPreview([a, b], ids, false, defaultOptions(), "P").folders.map((f) => f.sourceId))
      .toEqual(["a", "b"]);
    expect(buildPstPreview([b, a], ids, false, defaultOptions(), "P").folders.map((f) => f.sourceId))
      .toEqual(["b", "a"]);
  });

  it("buildConfigFromOptions sources follow the given INPUT order (write path is input order, not a sort)", () => {
    const r1 = buildConfigFromOptions([a, b], ids, false, defaultOptions(), "D:\\out\\P.pst");
    expect(r1.config.outputs[0].sources.map((s) => s.path)).toEqual(["a.mbox", "b.mbox"]);
    const r2 = buildConfigFromOptions([b, a], ids, false, defaultOptions(), "D:\\out\\P.pst");
    expect(r2.config.outputs[0].sources.map((s) => s.path)).toEqual(["b.mbox", "a.mbox"]);
  });
});

describe("validateFolderName parity table (mirror of FolderNameValidatorTests.cs)", () => {
  // Keep these two arrays identical (case-for-case) to the C# parity table in
  // tests/Mail2Pst.Core.Tests/Config/FolderNameValidatorTests.cs. The engine validator
  // (src/Mail2Pst.Core/Config/FolderNameValidator.cs) must agree on every case.
  const valid = ["Imported Mail", "Work", "2024 Archive", "a.b", "Folder.name.with.dots", "café", ".hidden", "trailing."];
  const invalid = ["", "   ", "a/b", "a\\b", "tab\there", " leading", "trailing ", "CON", "con.txt", "COM1", "LPT9.log"];

  it.each(valid)("accepts %j", (name) => {
    expect(validateFolderName(name)).toBeNull();
  });
  it.each(invalid)("rejects %j", (name) => {
    expect(validateFolderName(name)).not.toBeNull();
  });
});

describe("defaultOptions enrichment defaults", () => {
  it("defaults junkHandling to Off and dropExpunged to false", () => {
    const o = defaultOptions();
    expect(o.junkHandling).toBe("Off");
    expect(o.dropExpunged).toBe(false);
  });
});

describe("defaultOptions", () => {
  it("defaults the three data-type toggles ON", () => {
    const o = defaultOptions();
    expect(o.includeAppointments).toBe(true);
    expect(o.includeTasks).toBe(true);
    expect(o.includeContacts).toBe(true);
  });
});
