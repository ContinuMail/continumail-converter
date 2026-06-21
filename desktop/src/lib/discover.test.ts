// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { parseDiscover } from "./discover";

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
    expect(() => parseDiscover("not json at all")).toThrow();
  });
});
