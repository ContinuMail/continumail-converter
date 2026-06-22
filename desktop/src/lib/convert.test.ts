// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { parseConvertLine, deriveOutputTarget, appendWarningCapped, convertExitError, joinOutputPstPath } from "./convert";
import type { WarningItem } from "./convert";

describe("parseConvertLine", () => {
  it("parses each known event type", () => {
    expect(parseConvertLine('{"type":"started","input":"c.json","outputDirectory":"out/"}')?.type).toBe("started");
    expect(parseConvertLine('{"type":"scan","totalMessages":17989}')).toEqual({ type: "scan", totalMessages: 17989 });
    const p = parseConvertLine('{"type":"progress","converted":10,"total":20,"warnings":1,"skipped":0,"currentFolder":"Inbox"}');
    expect(p).toMatchObject({ type: "progress", converted: 10, total: 20, currentFolder: "Inbox" });
    expect(parseConvertLine('{"type":"warning","source":"a","identifier":"#1","reason":"x"}')?.type).toBe("warning");
    const d = parseConvertLine('{"type":"done","converted":2,"skipped":0,"warnings":0,"outputs":["out/Personal.pst"],"elapsedMs":120}');
    expect(d).toMatchObject({ type: "done", outputs: ["out/Personal.pst"], elapsedMs: 120 });
    expect(parseConvertLine('{"type":"error","message":"boom","fatal":true}')?.type).toBe("error");
    const c = parseConvertLine('{"type":"cancelled","deleted":["out/A-2.pst"],"outputs":["out/A-1.pst"],"converted":5,"skipped":0,"warnings":0}');
    expect(c).toMatchObject({ type: "cancelled", deleted: ["out/A-2.pst"], outputs: ["out/A-1.pst"] });
  });

  it("returns null for blank, non-JSON, and unknown-type lines", () => {
    expect(parseConvertLine("")).toBeNull();
    expect(parseConvertLine("   ")).toBeNull();
    expect(parseConvertLine("Building...")).toBeNull();
    expect(parseConvertLine("not json")).toBeNull();
    expect(parseConvertLine('{"type":"debug","message":"hi"}')).toBeNull();
  });
});

describe("deriveOutputTarget", () => {
  it("splits a .pst path into outputDir and pstName", () => {
    expect(deriveOutputTarget("D:\\out\\Personal.pst")).toEqual({
      outputDir: "D:\\out",
      pstName: "Personal",
    });
  });
  it("rejects a non-.pst path", () => {
    expect(() => deriveOutputTarget("D:\\out\\Personal.txt")).toThrow(/\.pst/);
  });
  it("rejects an empty path", () => {
    expect(() => deriveOutputTarget("")).toThrow();
  });
});

describe("appendWarningCapped", () => {
  const w = (n: number): WarningItem => ({ source: "s", identifier: `#${n}`, reason: `r${n}` });
  it("appends until the cap then stops", () => {
    let list: WarningItem[] = [];
    for (let i = 0; i < 250; i++) list = appendWarningCapped(list, w(i), 200);
    expect(list.length).toBe(200);
    expect(list[0].identifier).toBe("#0");
    expect(list[199].identifier).toBe("#199");
  });
  it("returns the same array reference once full (no-op)", () => {
    const full: WarningItem[] = Array.from({ length: 200 }, (_, i) => w(i));
    expect(appendWarningCapped(full, w(999), 200)).toBe(full);
  });
});

describe("parseConvertLine (progress carries bytes)", () => {
  it("parses a progress line including bytes", () => {
    const p = parseConvertLine(
      '{"type":"progress","converted":10,"total":20,"warnings":1,"skipped":0,"bytes":12345,"currentFolder":"Inbox"}',
    );
    expect(p).toMatchObject({ type: "progress", converted: 10, total: 20, bytes: 12345, currentFolder: "Inbox" });
  });
});

describe("convertExitError", () => {
  it("returns null when not running (a terminal event was already handled)", () => {
    expect(convertExitError(0, false)).toBeNull();
    expect(convertExitError(1, false)).toBeNull();
  });
  it("errors on a nonzero exit while still running", () => {
    expect(convertExitError(2, true)).toMatch(/exit code 2/);
  });
  it("errors on a zero/null exit while still running (no terminal event)", () => {
    expect(convertExitError(0, true)).toMatch(/without a terminal event/);
    expect(convertExitError(null, true)).toMatch(/without a terminal event/);
  });
});

describe("joinOutputPstPath", () => {
  it("joins a Windows dir with backslash separator and appends .pst", () => {
    expect(joinOutputPstPath("C:\\Mail\\out", "Mail")).toBe("C:\\Mail\\out\\Mail.pst");
  });
  it("joins a POSIX dir with forward slash", () => {
    expect(joinOutputPstPath("/srv/out", "Mail")).toBe("/srv/out/Mail.pst");
  });
  it("collapses a trailing separator on the dir", () => {
    expect(joinOutputPstPath("C:\\Mail\\out\\", "Mail")).toBe("C:\\Mail\\out\\Mail.pst");
    expect(joinOutputPstPath("/srv/out/", "Mail")).toBe("/srv/out/Mail.pst");
  });
  it("handles a drive-root dir", () => {
    expect(joinOutputPstPath("C:\\", "Mail")).toBe("C:\\Mail.pst");
  });
});
