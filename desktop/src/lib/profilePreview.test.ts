// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { buildAccountPreview } from "./profilePreview";
import type { Account, ProfileSourceRow } from "./types";

const acct = (id: string, email: string | null, seg: string): Account => ({
  id, folderSegment: seg, accountPath: id, store: "ImapMail", email, host: seg,
  addressResolution: email ? "identity" : "not-found",
});
const row = (id: string, accountId: string, tfp: string[], messages = 1, bytes = 100): ProfileSourceRow => ({
  id, path: id, accountId, targetFolderPath: tfp, displayName: tfp.join(" / "),
  messages, bytes, sourceBytes: bytes * 2, dateFrom: null, dateTo: null, warnings: 0, skipped: 0, msfPath: null,
});

const accounts = [acct("a", "alice@example.com", "a"), acct("b", "bob@example.test", "b")];
const rows = [
  row("a/Inbox", "a", ["a", "Inbox"], 10, 1000),
  row("a/Sent", "a", ["a", "Sent"], 5, 500),
  row("b/Inbox", "b", ["b", "Inbox"], 3, 300),
];
const all = new Set(["a", "b"]);

describe("buildAccountPreview", () => {
  it("one entry per selected account; mirror shows the account-stripped folder path", () => {
    const out = buildAccountPreview(rows, accounts, all, {}, "mirror");
    expect(out.map((e) => e.pstName)).toEqual(["alice@example.com", "bob@example.test"]);
    // account segment "a" is stripped (it becomes the PST), matching buildProfileConfigMulti
    expect(out[0].folders.map((f) => f.displayName)).toEqual(["Inbox", "Sent"]);
    expect(out[0].folders[0]).toEqual({ displayName: "Inbox", messages: 10, bytes: 1000 });
  });

  it("flatten aggregates each account into one Imported Mail row", () => {
    const out = buildAccountPreview(rows, accounts, all, {}, "flatten");
    expect(out[0].folders).toEqual([{ displayName: "Imported Mail", messages: 15, bytes: 1500 }]);
    expect(out[1].folders).toEqual([{ displayName: "Imported Mail", messages: 3, bytes: 300 }]);
  });

  it("uses edited pstNames (sanitized) and falls back to the account default", () => {
    const out = buildAccountPreview(rows, accounts, all, { a: "My Mail" }, "mirror");
    expect(out[0].pstName).toBe("My Mail");
    expect(out[1].pstName).toBe("bob@example.test");
  });

  it("de-duplicates colliding pst names with -2, matching the builder", () => {
    const out = buildAccountPreview(rows, accounts, all, { a: "Same", b: "Same" }, "mirror");
    expect(out.map((e) => e.pstName)).toEqual(["Same", "Same-2"]);
  });

  it("omits accounts with no effective rows and respects selection", () => {
    const out = buildAccountPreview(rows, accounts, new Set(["a"]), {}, "mirror");
    expect(out.map((e) => e.key)).toEqual(["a"]);
  });
});
