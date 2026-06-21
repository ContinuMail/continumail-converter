// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { describe, it, expect } from "vitest";
import { parseColourImport, summarizeColourApply } from "./colourImport";
import type { ColourCategory } from "./types";

const preview = JSON.stringify({
  type: "importColours", mode: "preview", outlookAvailable: true, schemaVersion: 1,
  categories: [
    { name: "Work", hex: "#FF9900", outlookColor: 2, action: "would-add" },
    { name: "Important", hex: "#FF0000", outlookColor: 1, action: "skipped-existing" },
  ],
});

describe("parseColourImport", () => {
  it("parses a preview object", () => {
    const r = parseColourImport(preview);
    expect(r.kind).toBe("result");
    if (r.kind !== "result") return;
    expect(r.mode).toBe("preview");
    expect(r.outlookAvailable).toBe(true);
    expect(r.categories).toHaveLength(2);
    expect(r.categories[0]).toEqual({ name: "Work", hex: "#FF9900", outlookColor: 2, action: "would-add" });
  });

  it("parses an apply object", () => {
    const r = parseColourImport(JSON.stringify({ type: "importColours", mode: "apply", outlookAvailable: true, categories: [] }));
    expect(r.kind === "result" && r.mode === "apply").toBe(true);
  });

  it("maps a handled error object (nonzero exit) to kind:error with its message", () => {
    const r = parseColourImport(JSON.stringify({ type: "error", stage: "import-colours", message: "Outlook is running. Close Outlook completely, then re-run import-colours --apply.", fatal: true }));
    expect(r.kind).toBe("error");
    if (r.kind !== "error") return;
    expect(r.message).toContain("Outlook is running");
  });

  it("returns empty categories result when none", () => {
    const r = parseColourImport(JSON.stringify({ type: "importColours", mode: "preview", outlookAvailable: false, categories: [] }));
    expect(r.kind === "result" && r.categories.length === 0 && r.outlookAvailable === false).toBe(true);
  });

  it("returns all rows for a skipped-only preview (none would-add → drives the 'nothing to import' card state)", () => {
    const r = parseColourImport(JSON.stringify({ type: "importColours", mode: "preview", outlookAvailable: true, categories: [
      { name: "Important", hex: "#FF0000", outlookColor: 1, action: "skipped-existing" },
      { name: "NoColour", hex: null, outlookColor: null, action: "skipped-no-colour" },
    ] }));
    expect(r.kind).toBe("result");
    if (r.kind !== "result") return;
    expect(r.categories).toHaveLength(2);
    expect(r.categories.some((c) => c.action === "would-add")).toBe(false);
  });

  it("returns a generic error for unrecognized/non-JSON output (never throws)", () => {
    expect(parseColourImport("not json at all")).toEqual({ kind: "error", message: "Could not read colour-import result." });
    expect(parseColourImport("")).toEqual({ kind: "error", message: "Could not read colour-import result." });
  });
});

describe("summarizeColourApply", () => {
  it("counts added vs already-existing", () => {
    const cats: ColourCategory[] = [
      { name: "a", hex: "#1", outlookColor: 1, action: "added" },
      { name: "b", hex: "#2", outlookColor: 2, action: "added" },
      { name: "c", hex: "#3", outlookColor: 3, action: "skipped-existing" },
      { name: "d", hex: null, outlookColor: null, action: "skipped-no-colour" },
    ];
    expect(summarizeColourApply(cats)).toEqual({ added: 2, existing: 1 });
  });
});
