// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { buildAccountPreview } from "./profilePreview";
import { defaultOptions } from "./options";
import type { Account, ProfileSourceRow, DiscoveredCalendar, DiscoveredAddressBook } from "./types";

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
    const { entries: out } = buildAccountPreview(rows, accounts, all, {}, "mirror");
    expect(out.map((e) => e.pstName)).toEqual(["alice@example.com", "bob@example.test"]);
    // account segment "a" is stripped (it becomes the PST), matching buildProfileConfigMulti
    expect(out[0].folders.map((f) => f.displayName)).toEqual(["Inbox", "Sent"]);
    expect(out[0].folders[0]).toEqual({ displayName: "Inbox", messages: 10, bytes: 1000 });
  });

  it("flatten aggregates each account into one Imported Mail row", () => {
    const { entries: out } = buildAccountPreview(rows, accounts, all, {}, "flatten");
    expect(out[0].folders).toEqual([{ displayName: "Imported Mail", messages: 15, bytes: 1500 }]);
    expect(out[1].folders).toEqual([{ displayName: "Imported Mail", messages: 3, bytes: 300 }]);
  });

  it("uses edited pstNames (sanitized) and falls back to the account default", () => {
    const { entries: out } = buildAccountPreview(rows, accounts, all, { a: "My Mail" }, "mirror");
    expect(out[0].pstName).toBe("My Mail");
    expect(out[1].pstName).toBe("bob@example.test");
  });

  it("de-duplicates colliding pst names with -2, matching the builder", () => {
    const { entries: out } = buildAccountPreview(rows, accounts, all, { a: "Same", b: "Same" }, "mirror");
    expect(out.map((e) => e.pstName)).toEqual(["Same", "Same-2"]);
  });

  it("omits accounts with no effective rows and respects selection", () => {
    const { entries: out } = buildAccountPreview(rows, accounts, new Set(["a"]), {}, "mirror");
    expect(out.map((e) => e.key)).toEqual(["a"]);
  });
});

const acc = (id: string, r: Account["addressResolution"]): Account =>
  ({ id, folderSegment: id, accountPath: id, store: null, email: null, host: null, addressResolution: r });
const row2 = (accountId: string, leaf: string): ProfileSourceRow => ({
  id: `${accountId}/${leaf}`, path: `${accountId}/${leaf}`, displayName: leaf, messages: 1, bytes: 10,
  sourceBytes: 10, dateFrom: null, dateTo: null, warnings: 0, skipped: 0,
  targetFolderPath: [accountId, leaf], msfPath: null, accountId,
});
const cal = (accountId: string | null): DiscoveredCalendar => ({
  calId: "c" + accountId, displayName: "Home", storeKind: "local", storePath: "/s", calendarType: "both",
  isVisibleInThunderbird: true, eventCount: 2, taskCount: 0,
  defaultCalendarFolderPath: ["Calendars", "Home"], defaultTaskFolderPath: [], accountId,
});
const book = (accountId: string | null): DiscoveredAddressBook =>
  ({ displayName: "Personal", path: "/p/abook.sqlite", format: "thunderbird-sqlite", contactCount: 3, accountId });

describe("buildAccountPreview routing", () => {
  it("shows calendars/contacts under their account and a synthetic Local Folders entry", () => {
    const gmail = row2("/acc/gmail", "Inbox");
    const { entries, warnings } = buildAccountPreview(
      [gmail], [acc("/acc/gmail", "server")], new Set(["/acc/gmail"]), {},
      "mirror", [cal("/acc/gmail"), cal(null)], [book(null)], defaultOptions(),
    );
    const gmailEntry = entries.find((e) => e.key === "/acc/gmail")!;
    expect(gmailEntry.pim.calendars.length).toBe(1);
    const synthetic = entries.find((e) => e.pstName === "Local Folders")!;
    expect(synthetic.pim.calendars.length).toBe(1);
    expect(synthetic.pim.contacts.length).toBe(1);
    expect(synthetic.isSynthetic).toBe(true);
    expect(warnings).toEqual([]);
  });

  it("uses the discovered display names as labels (not raw filenames)", () => {
    const gmail = row2("/acc/gmail", "Inbox");
    const { entries } = buildAccountPreview(
      [gmail], [acc("/acc/gmail", "server")], new Set(["/acc/gmail"]), {},
      "mirror", [], [book(null)], defaultOptions(),
    );
    const synthetic = entries.find((e) => e.isSynthetic)!;
    expect(synthetic.pim.contacts).toEqual(["Personal"]); // displayName, not "abook.sqlite"
  });

  it("synthetic Local Folders name matches buildProfileConfigMulti", async () => {
    const { buildProfileConfigMulti } = await import("./profileConfig");
    const gmail = row2("/acc/gmail", "Inbox");
    const args = {
      accounts: [acc("/acc/gmail", "server")], selectedKeys: new Set(["/acc/gmail"]),
      calendars: [cal(null)], addressBooks: [book(null)], options: defaultOptions(),
    };
    const { entries } = buildAccountPreview([gmail], args.accounts, args.selectedKeys, {},
      "mirror", args.calendars, args.addressBooks, args.options);
    const { config } = buildProfileConfigMulti({
      groups: [{ key: "/acc/gmail", pstName: "Gmail", rows: [gmail] }],
      checkedIds: new Set([gmail.id]), skipEmpty: false, options: args.options,
      target: { kind: "folder", dir: "C:/out" }, profileRoot: "/p", accounts: args.accounts,
      calendars: args.calendars, addressBooks: args.addressBooks,
    });
    const previewSynthetic = entries.find((e) => e.isSynthetic)!.pstName;
    const configSynthetic = config.outputs.find((o) => o.sources.length === 0)!.name;
    expect(previewSynthetic).toBe(configSynthetic);
  });
});
