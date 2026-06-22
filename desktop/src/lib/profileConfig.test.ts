// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { mergeProfileSources, buildProfileConfig, buildProfileConfigMulti } from "./profileConfig";
import type { DiscoveredSource, ProfileSourceRow } from "./types";
import type { OutputTarget } from "./types";
import type { ScanResult } from "./parse";
import { ConvertConfigError } from "./convert";
import { defaultOptions } from "./options";

const disc = (path: string, tfp: string[], msf: string | null): DiscoveredSource => ({
  path, type: "mbox", targetFolderPath: tfp, displayName: tfp[tfp.length - 1], sourceBytes: 1, msfPath: msf, accountId: null,
});

const scan: ScanResult = {
  kind: "scan",
  totals: { messages: 0, bytes: 0, sourceBytes: 0, sources: 0 },
  sources: [
    { id: "scan-1", path: "C:/p/A", displayName: "A", messages: 5, bytes: 500, sourceBytes: 50, dateFrom: null, dateTo: null, warnings: 0, skipped: 0 },
    { id: "scan-2", path: "C:/p/B", displayName: "B", messages: 3, bytes: 300, sourceBytes: 30, dateFrom: null, dateTo: null, warnings: 0, skipped: 0 },
  ],
};

const opts = (over: Partial<import("./options").OptionsState> = {}) => ({ ...defaultOptions(), ...over });

describe("mergeProfileSources", () => {
  it("mergeProfileSources preserves accountId from discovery", () => {
    const rows = mergeProfileSources(
      [{ path: "a", type: "mbox", targetFolderPath: ["imap.example.com", "Inbox"],
         displayName: "Inbox", sourceBytes: 1, msfPath: null, accountId: "/p/ImapMail/imap.example.com" }],
      { sources: [{ path: "a", messages: 1, bytes: 2, sourceBytes: 1, dateFrom: null, dateTo: null, warnings: 0, skipped: 0 }] } as any);
    expect(rows[0].accountId).toBe("/p/ImapMail/imap.example.com");
  });

  it("joins by path, sets id=path and displayName=joined targetFolderPath, takes counts from scan", () => {
    const rows = mergeProfileSources(
      [disc("C:/p/A", ["Account", "Inbox"], "C:/p/A.msf"), disc("C:/p/B", ["Work", "Inbox"], null)],
      scan,
    );
    expect(rows[0].id).toBe("C:/p/A");
    expect(rows[0].displayName).toBe("Account / Inbox");
    expect(rows[0].messages).toBe(5);
    expect(rows[0].msfPath).toBe("C:/p/A.msf");
    expect(rows[1].msfPath).toBeNull();
  });

  it("uses zero counts for a discovered source with no scan row", () => {
    const rows = mergeProfileSources([disc("C:/p/Z", ["Z"], null)], scan);
    expect(rows[0].messages).toBe(0);
    expect(rows[0].id).toBe("C:/p/Z");
  });

  it("ignores scan rows absent from discovery (discovery is the source of truth)", () => {
    // scan has C:/p/A and C:/p/B; discovery only has A → B must not appear.
    const rows = mergeProfileSources([disc("C:/p/A", ["Account", "Inbox"], null)], scan);
    expect(rows.map((r) => r.path)).toEqual(["C:/p/A"]);
  });
});

describe("buildProfileConfig", () => {
  const rows: ProfileSourceRow[] = [
    { id: "C:/p/A", path: "C:/p/A", displayName: "Account / Inbox", messages: 5, bytes: 500, sourceBytes: 50, dateFrom: null, dateTo: null, warnings: 0, skipped: 0, targetFolderPath: ["Account", "Inbox"], msfPath: "C:/p/A.msf", accountId: null },
    { id: "C:/p/B", path: "C:/p/B", displayName: "Work / Inbox", messages: 3, bytes: 300, sourceBytes: 30, dateFrom: null, dateTo: null, warnings: 0, skipped: 0, targetFolderPath: ["Work", "Inbox"], msfPath: null, accountId: null },
  ];
  const checked = new Set(["C:/p/A", "C:/p/B"]);

  it("mirror: emits targetFolderPath + msfPath verbatim, profilePath set", () => {
    const { config } = buildProfileConfig(rows, checked, true, opts({ folderMapping: "mirror", maxSizeMB: 5120 }), "C:/out/Gmail.pst", "C:/p");
    expect(config.profilePath).toBe("C:/p");
    expect(config.outputs[0].folderMapping).toBe("mirror");
    expect(config.outputs[0].maxSizeMB).toBe(5120);
    expect(config.outputs[0].sources[0]).toEqual({ path: "C:/p/A", type: "mbox", targetFolderPath: ["Account", "Inbox"], msfPath: "C:/p/A.msf" });
    expect(config.outputs[0].sources[1]).toEqual({ path: "C:/p/B", type: "mbox", targetFolderPath: ["Work", "Inbox"] }); // unpaired → no msfPath
  });

  it("mirror: allows duplicate leaf names under different parents (no error)", () => {
    const dupRows: ProfileSourceRow[] = [
      { ...rows[0], id: "C:/x/A/Inbox", path: "C:/x/A/Inbox", targetFolderPath: ["A", "Inbox"], displayName: "A / Inbox" },
      { ...rows[1], id: "C:/x/B/Inbox", path: "C:/x/B/Inbox", targetFolderPath: ["B", "Inbox"], displayName: "B / Inbox", msfPath: null },
    ];
    const { config } = buildProfileConfig(dupRows, new Set(["C:/x/A/Inbox", "C:/x/B/Inbox"]), true, opts({ folderMapping: "mirror", maxSizeMB: 5120 }), "C:/out/X.pst", "C:/x");
    expect(config.outputs[0].sources).toHaveLength(2);
    expect(config.outputs[0].sources.map((s) => s.targetFolderPath)).toEqual([["A", "Inbox"], ["B", "Inbox"]]);
  });

  it("flatten: folderMapping flatten, no targetFolderPath, no targetFolder, msfPath + profilePath kept", () => {
    const { config } = buildProfileConfig(rows, checked, true, opts({ folderMapping: "flatten", maxSizeMB: 5120 }), "C:/out/Gmail.pst", "C:/p");
    expect(config.outputs[0].folderMapping).toBe("flatten");
    expect(config.profilePath).toBe("C:/p");
    for (const s of config.outputs[0].sources) {
      expect(s.targetFolderPath).toBeUndefined();
      expect(s.targetFolder).toBeUndefined();
    }
    expect(config.outputs[0].sources[0].msfPath).toBe("C:/p/A.msf");
  });

  it("honors skipEmpty and checked selection", () => {
    const withEmpty: ProfileSourceRow[] = [...rows, { ...rows[0], id: "C:/p/E", path: "C:/p/E", messages: 0, targetFolderPath: ["E"], displayName: "E", msfPath: null }];
    const { config } = buildProfileConfig(withEmpty, new Set(["C:/p/A", "C:/p/E"]), true, opts({ folderMapping: "mirror", maxSizeMB: 5120 }), "C:/out/G.pst", "C:/p");
    // A is checked+non-empty; E is checked but empty (skipped); B not checked.
    expect(config.outputs[0].sources.map((s) => s.path)).toEqual(["C:/p/A"]);
  });

  it("throws ConvertConfigError when no folders are effective", () => {
    expect(() => buildProfileConfig(rows, new Set(), true, opts({ folderMapping: "mirror", maxSizeMB: 5120 }), "C:/out/G.pst", "C:/p")).toThrow(ConvertConfigError);
  });

  it("derives outputDir + pstName from the .pst path", () => {
    const { outputDir, pstName } = buildProfileConfig(rows, checked, true, opts({ folderMapping: "mirror", maxSizeMB: 5120 }), "C:/out/Gmail.pst", "C:/p");
    expect(pstName).toBe("Gmail");
    expect(outputDir).toBe("C:/out");
  });

  it("writes junkHandling + dropExpunged top-level (defaults Off/false) in mirror", () => {
    const { config } = buildProfileConfig(rows, checked, true, opts({ folderMapping: "mirror" }), "C:/out/G.pst", "C:/p");
    expect(config.junkHandling).toBe("Off");
    expect(config.dropExpunged).toBe(false);
  });

  it("carries non-default junkHandling + dropExpunged in mirror AND flatten", () => {
    for (const folderMapping of ["mirror", "flatten"] as const) {
      const { config } = buildProfileConfig(
        rows, checked, true, opts({ folderMapping, junkHandling: "Folder", dropExpunged: true }), "C:/out/G.pst", "C:/p",
      );
      expect(config.outputs[0].folderMapping).toBe(folderMapping); // folderMapping stays on the output group
      expect(config).not.toHaveProperty("folderMapping");           // not duplicated at top level
      expect(config.junkHandling).toBe("Folder");                   // junk/expunged ARE top-level
      expect(config.dropExpunged).toBe(true);
    }
  });
});

// ---------------------------------------------------------------------------
// buildProfileConfigMulti
// ---------------------------------------------------------------------------

const srcRow = (
  id: string,
  accountId: string,
  tfp: string[],
  msfPath: string | null,
  messages = 1,
): ProfileSourceRow => ({
  id,
  path: id,
  displayName: tfp.join(" / "),
  messages,
  bytes: messages * 100,
  sourceBytes: messages * 10,
  dateFrom: null,
  dateTo: null,
  warnings: 0,
  skipped: 0,
  targetFolderPath: tfp,
  msfPath,
  accountId,
});

type MultiGroup = { key: string; pstName: string; rows: ProfileSourceRow[] };

const buildProfileConfigMultiWrapper = ({
  groups,
  mapping,
  target,
  skipEmpty = false,
  checkedIds,
  profileRoot = "/profile",
}: {
  groups: MultiGroup[];
  mapping: "mirror" | "flatten";
  target: OutputTarget;
  skipEmpty?: boolean;
  checkedIds?: Set<string>;
  profileRoot?: string;
}) => {
  const allIds = new Set(groups.flatMap((g) => g.rows.map((r) => r.id)));
  return buildProfileConfigMulti({
    groups,
    checkedIds: checkedIds ?? allIds,
    skipEmpty,
    options: opts({ folderMapping: mapping }),
    target,
    profileRoot,
  });
};

describe("buildProfileConfigMulti", () => {
  it("emits one output group per kept account with stripped paths", () => {
    const { config } = buildProfileConfigMultiWrapper({
      groups: [
        { key: "A", pstName: "alice@example.com", rows: [
          srcRow("a", "A", ["imap.example.com", "Inbox"], "a.msf"),
          srcRow("b", "A", ["imap.example.com", "Inbox", "Archive"], null)] },
        { key: "B", pstName: "Office365", rows: [srcRow("c", "B", ["Office365", "Inbox"], null)] },
      ],
      mapping: "mirror", target: { kind: "folder", dir: "/out" },
    });
    expect(config.outputs).toHaveLength(2);
    const a = config.outputs[0];
    expect(a.name).toBe("alice@example.com");
    expect(a.sources[0].targetFolderPath).toEqual(["Inbox"]);          // account prefix stripped
    expect(a.sources[1].targetFolderPath).toEqual(["Inbox", "Archive"]);
    expect(a.sources[0].msfPath).toBe("a.msf");                         // preserved
  });

  it("preserves profilePath and honours checkedIds (folder selection)", () => {
    const { config } = buildProfileConfigMultiWrapper({
      groups: [{ key: "A", pstName: "A", rows: [
        srcRow("a", "A", ["imap.example.com", "Inbox"], null),
        srcRow("b", "A", ["imap.example.com", "Spam"], null)] }],
      mapping: "mirror", target: { kind: "folder", dir: "/out" },
      checkedIds: new Set(["a"]),            // only Inbox selected in Review
      profileRoot: "/p",
    });
    expect((config as any).profilePath).toBe("/p");
    expect(config.outputs[0].sources).toHaveLength(1);
    expect(config.outputs[0].sources[0].targetFolderPath).toEqual(["Inbox"]);
  });

  it("THROWS when stripping the account prefix leaves no folder (mirror)", () => {
    expect(() => buildProfileConfigMultiWrapper({
      groups: [{ key: "A", pstName: "A", rows: [srcRow("a", "A", ["imap.example.com"], null)] }],
      mapping: "mirror", target: { kind: "folder", dir: "/out" },
    })).toThrow(/no folder below its account/i);
  });

  it("excludes an account with no effective sources after skip-empty", () => {
    const { config } = buildProfileConfigMultiWrapper({
      groups: [
        { key: "A", pstName: "A", rows: [srcRow("a", "A", ["imap.example.com", "Inbox"], null, 5)] },
        { key: "B", pstName: "B", rows: [srcRow("c", "B", ["Office365", "Empty"], null, 0)] }],
      mapping: "mirror", target: { kind: "folder", dir: "/out" }, skipEmpty: true,
    });
    expect(config.outputs.map((o) => o.name)).toEqual(["A"]);
  });

  it("suffixes colliding PST names deterministically", () => {
    const { config } = buildProfileConfigMultiWrapper({
      groups: [
        { key: "A", pstName: "Mail", rows: [srcRow("a", "A", ["x", "Inbox"], null, 1)] },
        { key: "B", pstName: "Mail", rows: [srcRow("c", "B", ["y", "Inbox"], null, 1)] }],
      mapping: "mirror", target: { kind: "folder", dir: "/out" },
    });
    expect(config.outputs.map((o) => o.name)).toEqual(["Mail", "Mail-2"]);
  });

  it("skips an already-taken suffix (Mail-2 exists → next is Mail-3)", () => {
    const { config } = buildProfileConfigMultiWrapper({
      groups: [
        { key: "A", pstName: "Mail", rows: [srcRow("a", "A", ["x", "Inbox"], null, 1)] },
        { key: "B", pstName: "Mail-2", rows: [srcRow("c", "B", ["y", "Inbox"], null, 1)] },
        { key: "C", pstName: "Mail", rows: [srcRow("d", "C", ["z", "Inbox"], null, 1)] }],
      mapping: "mirror", target: { kind: "folder", dir: "/out" },
    });
    expect(config.outputs.map((o) => o.name)).toEqual(["Mail", "Mail-2", "Mail-3"]);
  });

  it("throws when all groups are empty after skip-empty", () => {
    expect(() => buildProfileConfigMultiWrapper({
      groups: [{ key: "A", pstName: "A", rows: [srcRow("a", "A", ["x", "Empty"], null, 0)] }],
      mapping: "mirror", target: { kind: "folder", dir: "/out" }, skipEmpty: true,
    })).toThrow(/at least one folder/i);
  });
});
