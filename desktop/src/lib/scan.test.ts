// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { parseScanLine, scanningTitle } from "./scan";

const SCAN_LINE =
  '{"type":"scan","totals":{"messages":2,"bytes":167,"sourceBytes":735,"sources":1},"sources":[{"id":"sample","path":"C:/x/sample.mbox","displayName":"sample","messages":2,"bytes":167,"sourceBytes":735,"dateFrom":"2024-01-01T10:00:00Z","dateTo":"2024-01-02T11:30:00Z","warnings":0,"skipped":0}],"skipped":[],"warnings":[]}';

describe("parseScanLine", () => {
  it("parses a scanProgress advisory line", () => {
    expect(parseScanLine('{"type":"scanProgress","bytes":1048576,"totalBytes":4194304}')).toEqual({
      type: "scanProgress",
      bytes: 1048576,
      totalBytes: 4194304,
    });
  });

  it("parses the final single-line scan result", () => {
    const r = parseScanLine(SCAN_LINE);
    expect(r?.type).toBe("scan");
    if (r?.type === "scan") {
      expect(r.result.kind).toBe("scan");
      expect(r.result.totals.messages).toBe(2);
      expect(r.result.sources[0].id).toBe("sample");
    }
  });

  it("returns null for blank, non-JSON, unknown-type, and partial pretty-printed lines", () => {
    expect(parseScanLine("")).toBeNull();
    expect(parseScanLine("   ")).toBeNull();
    expect(parseScanLine("Building...")).toBeNull();
    expect(parseScanLine('{"type":"started"}')).toBeNull();
    expect(parseScanLine('  "type": "scan",')).toBeNull(); // one line of a pretty-printed object
  });

  it("returns null for scanProgress with non-numeric fields", () => {
    expect(parseScanLine('{"type":"scanProgress","bytes":"x","totalBytes":1}')).toBeNull();
  });
});

describe("scanningTitle", () => {
  it("omits the count when it is not yet known (0 or negative)", () => {
    // Profile mode shows the Scanning view during discovery, before the
    // discovered-source count exists — don't claim "0 files".
    expect(scanningTitle(0)).toBe("Scanning…");
    expect(scanningTitle(-1)).toBe("Scanning…");
  });

  it("uses the singular for one source", () => {
    expect(scanningTitle(1)).toBe("Scanning 1 file…");
  });

  it("uses the plural for multiple sources", () => {
    expect(scanningTitle(3)).toBe("Scanning 3 files…");
  });
});
