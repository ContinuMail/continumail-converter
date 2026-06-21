// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { mergeProfileSources, buildProfileConfig } from "./profileConfig";
import type { DiscoveredSource, ProfileSourceRow } from "./types";
import type { ScanResult } from "./parse";
import { ConvertConfigError } from "./convert";

const disc = (path: string, tfp: string[], msf: string | null): DiscoveredSource => ({
  path, type: "mbox", targetFolderPath: tfp, displayName: tfp[tfp.length - 1], sourceBytes: 1, msfPath: msf,
});

const scan: ScanResult = {
  kind: "scan",
  totals: { messages: 0, bytes: 0, sourceBytes: 0, sources: 0 },
  sources: [
    { id: "scan-1", path: "C:/p/A", displayName: "A", messages: 5, bytes: 500, sourceBytes: 50, dateFrom: null, dateTo: null, warnings: 0, skipped: 0 },
    { id: "scan-2", path: "C:/p/B", displayName: "B", messages: 3, bytes: 300, sourceBytes: 30, dateFrom: null, dateTo: null, warnings: 0, skipped: 0 },
  ],
};

describe("mergeProfileSources", () => {
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
    { id: "C:/p/A", path: "C:/p/A", displayName: "Account / Inbox", messages: 5, bytes: 500, sourceBytes: 50, dateFrom: null, dateTo: null, warnings: 0, skipped: 0, targetFolderPath: ["Account", "Inbox"], msfPath: "C:/p/A.msf" },
    { id: "C:/p/B", path: "C:/p/B", displayName: "Work / Inbox", messages: 3, bytes: 300, sourceBytes: 30, dateFrom: null, dateTo: null, warnings: 0, skipped: 0, targetFolderPath: ["Work", "Inbox"], msfPath: null },
  ];
  const checked = new Set(["C:/p/A", "C:/p/B"]);

  it("mirror: emits targetFolderPath + msfPath verbatim, profilePath set", () => {
    const { config } = buildProfileConfig(rows, checked, true, "mirror", 5120, "C:/out/Gmail.pst", "C:/p");
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
    const { config } = buildProfileConfig(dupRows, new Set(["C:/x/A/Inbox", "C:/x/B/Inbox"]), true, "mirror", 5120, "C:/out/X.pst", "C:/x");
    expect(config.outputs[0].sources).toHaveLength(2);
    expect(config.outputs[0].sources.map((s) => s.targetFolderPath)).toEqual([["A", "Inbox"], ["B", "Inbox"]]);
  });

  it("flatten: folderMapping flatten, no targetFolderPath, no targetFolder, msfPath + profilePath kept", () => {
    const { config } = buildProfileConfig(rows, checked, true, "flatten", 5120, "C:/out/Gmail.pst", "C:/p");
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
    const { config } = buildProfileConfig(withEmpty, new Set(["C:/p/A", "C:/p/E"]), true, "mirror", 5120, "C:/out/G.pst", "C:/p");
    // A is checked+non-empty; E is checked but empty (skipped); B not checked.
    expect(config.outputs[0].sources.map((s) => s.path)).toEqual(["C:/p/A"]);
  });

  it("throws ConvertConfigError when no folders are effective", () => {
    expect(() => buildProfileConfig(rows, new Set(), true, "mirror", 5120, "C:/out/G.pst", "C:/p")).toThrow(ConvertConfigError);
  });

  it("derives outputDir + pstName from the .pst path", () => {
    const { outputDir, pstName } = buildProfileConfig(rows, checked, true, "mirror", 5120, "C:/out/Gmail.pst", "C:/p");
    expect(pstName).toBe("Gmail");
    expect(outputDir).toBe("C:/out");
  });
});
