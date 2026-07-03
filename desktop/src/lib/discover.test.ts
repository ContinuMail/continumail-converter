// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { parseDiscover } from "./discover";
import { EngineParseError } from "./parse";

const SAMPLE = JSON.stringify({
  type: "discovery",
  schemaVersion: 1,
  root: "C:/profile",
  layout: "thunderbird",
  sources: [
    { path: "C:/p/Inbox", type: "mbox", targetFolderPath: ["Account", "Inbox"],
      displayName: "Inbox", sourceBytes: 100, msfPath: "C:/p/Inbox.msf" },
    { path: "C:/p/Work", type: "mbox", targetFolderPath: ["Work", "Inbox"],
      displayName: "Inbox", sourceBytes: 50, msfPath: null },
  ],
  warnings: [
    { code: "duplicate-target-folder-path", path: "C:/p/Inbox", targetFolderPath: ["Account", "Inbox"],
      segment: null, segmentIndex: null, relatedPaths: ["C:/p/Inbox"], message: "dup" },
  ],
  skipped: [{ code: "symlink-skipped", path: "C:/p/link", reason: "symlink" }],
  pairing: { pairedMsfCount: 1, unpairedMboxCount: 1, orphanMsfCount: 0 },
});

describe("parseDiscover", () => {
  it("parses a discovery object with nested sources, pairing, warnings, skipped", () => {
    const r = parseDiscover(SAMPLE);
    expect(r.root).toBe("C:/profile");
    expect(r.layout).toBe("thunderbird");
    expect(r.sources).toHaveLength(2);
    expect(r.sources[0].targetFolderPath).toEqual(["Account", "Inbox"]);
    expect(r.sources[0].msfPath).toBe("C:/p/Inbox.msf");
    expect(r.sources[1].msfPath).toBeNull();
    expect(r.warnings[0].code).toBe("duplicate-target-folder-path");
    expect(r.skipped[0].code).toBe("symlink-skipped");
    expect(r.pairing.pairedMsfCount).toBe(1);
  });

  it("tolerates surrounding noise (dev build) around the object", () => {
    const r = parseDiscover("warning: dev build\n" + SAMPLE + "\n");
    expect(r.sources).toHaveLength(2);
  });

  it("defaults missing arrays to empty", () => {
    const r = parseDiscover(JSON.stringify({ type: "discovery", root: "x", layout: "empty" }));
    expect(r.sources).toEqual([]);
    expect(r.warnings).toEqual([]);
    expect(r.skipped).toEqual([]);
    expect(r.pairing).toEqual({ pairedMsfCount: 0, unpairedMboxCount: 0, orphanMsfCount: 0 });
  });

  it("throws when no discovery object is present", () => {
    expect(() => parseDiscover("not json at all")).toThrow(EngineParseError);
  });

  it("parses accounts[] and accountId", () => {
    const json = JSON.stringify({
      type: "discovery", root: "P", layout: "profile",
      sources: [{ path: "a", type: "mbox", targetFolderPath: ["imap.example.com", "Inbox"],
        displayName: "Inbox", sourceBytes: 1, msfPath: null, accountId: "/p/ImapMail/imap.example.com" }],
      warnings: [], skipped: [], pairing: { pairedMsfCount: 0, unpairedMboxCount: 1, orphanMsfCount: 0 },
      accounts: [{ id: "/p/ImapMail/imap.example.com", folderSegment: "imap.example.com",
        accountPath: "/p/ImapMail/imap.example.com", store: "ImapMail",
        email: "alice@example.com", host: "imap.example.com", addressResolution: "identity" }],
    });
    const r = parseDiscover(json);
    expect(r.accounts[0].email).toBe("alice@example.com");
    expect(r.sources[0].accountId).toBe("/p/ImapMail/imap.example.com");
  });

  it("defaults accounts to [] and accountId to null when absent", () => {
    const json = JSON.stringify({
      type: "discovery", root: "P", layout: "single", sources: [
        { path: "a", type: "mbox", targetFolderPath: ["Inbox"], displayName: "Inbox", sourceBytes: 1, msfPath: null }],
      warnings: [], skipped: [], pairing: { pairedMsfCount: 0, unpairedMboxCount: 1, orphanMsfCount: 0 },
    });
    const r = parseDiscover(json);
    expect(r.accounts).toEqual([]);
    expect(r.sources[0].accountId).toBeNull();
  });

  it("clamps an invalid addressResolution to not-found", () => {
    const json = JSON.stringify({
      type: "discovery", root: "P", layout: "profile", sources: [],
      warnings: [], skipped: [], pairing: { pairedMsfCount: 0, unpairedMboxCount: 0, orphanMsfCount: 0 },
      accounts: [{ id: "x", folderSegment: "x", accountPath: "x", store: null, email: null, host: null, addressResolution: "bogus" }],
    });
    expect(parseDiscover(json).accounts[0].addressResolution).toBe("not-found");
  });

  it("parses calendars and addressBooks", () => {
    const stdout = JSON.stringify({
      type: "discovery", root: "/p", layout: "thunderbird-profile",
      sources: [], warnings: [], skipped: [],
      pairing: { pairedMsfCount: 0, unpairedMboxCount: 0, orphanMsfCount: 0 },
      accounts: [],
      calendars: [{ calId: "c1", displayName: "Home", storeKind: "local", storePath: "/p/local.sqlite",
        calendarType: "both", isVisibleInThunderbird: true, eventCount: 12, taskCount: 3,
        defaultCalendarFolderPath: ["Calendar"], defaultTaskFolderPath: ["Tasks"] }],
      addressBooks: [{ displayName: "Personal", path: "/p/abook.sqlite", format: "thunderbird-sqlite", contactCount: 4 },
        { displayName: "Collected", path: "/p/history.mab", format: "thunderbird-mab", contactCount: null }],
    });
    const r = parseDiscover(stdout);
    expect(r.calendars).toHaveLength(1);
    expect(r.calendars[0].eventCount).toBe(12);
    expect(r.addressBooks).toHaveLength(2);
    expect(r.addressBooks[1].contactCount).toBeNull();
  });

  it("defaults calendars/addressBooks to [] when absent", () => {
    const stdout = JSON.stringify({
      type: "discovery", root: "/p", layout: "x", sources: [], warnings: [], skipped: [],
      pairing: { pairedMsfCount: 0, unpairedMboxCount: 0, orphanMsfCount: 0 }, accounts: [],
    });
    const r = parseDiscover(stdout);
    expect(r.calendars).toEqual([]);
    expect(r.addressBooks).toEqual([]);
  });

  it("coerces a malformed contactCount to null (never NaN)", () => {
    const stdout = JSON.stringify({
      type: "discovery", root: "/p", layout: "x", sources: [], warnings: [], skipped: [],
      pairing: { pairedMsfCount: 0, unpairedMboxCount: 0, orphanMsfCount: 0 }, accounts: [],
      addressBooks: [{ displayName: "B", path: "/b", format: "thunderbird-sqlite", contactCount: "oops" }],
    });
    expect(parseDiscover(stdout).addressBooks[0].contactCount).toBeNull();
  });

  it("parses accountId on calendars and address books (present, null, absent)", () => {
    const stdout = JSON.stringify({
      type: "discovery", root: "/p", layout: "x", sources: [], warnings: [], skipped: [],
      pairing: { pairedMsfCount: 0, unpairedMboxCount: 0, orphanMsfCount: 0 }, accounts: [],
      calendars: [{ calId: "c1", displayName: "Home", storeKind: "local", storePath: "/s",
        calendarType: "both", isVisibleInThunderbird: true, eventCount: 1, taskCount: 0,
        defaultCalendarFolderPath: [], defaultTaskFolderPath: [], accountId: "/p/acc" }],
      addressBooks: [
        { displayName: "P", path: "/p/abook.sqlite", format: "thunderbird-sqlite", contactCount: 1, accountId: null },
        { displayName: "G", path: "/p/abook-1.sqlite", format: "thunderbird-sqlite", contactCount: 2 }, // absent
      ],
    });
    const r = parseDiscover(stdout);
    expect(r.calendars[0].accountId).toBe("/p/acc");
    expect(r.addressBooks[0].accountId).toBeNull();
    expect(r.addressBooks[1].accountId).toBeNull(); // absent -> null
  });
});
