// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { describe, it, expect } from "vitest";
import {
  parseColourImport,
  summarizeColourApply,
  parseOutlookProfiles,
  cardProfileState,
  parseProfileCreate,
  parseProfileOpen,
  ProfileStageError,
} from "./colourImport";
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

describe("parseOutlookProfiles", () => {
  it("parses a profiles listing", () => {
    const json = JSON.stringify({
      type: "outlookProfiles", classicOutlook: true, defaultProfile: "Outlook", profiles: ["Outlook", "ContinuMail"],
    });
    expect(parseOutlookProfiles(json)).toEqual({
      classicOutlook: true, defaultProfile: "Outlook", profiles: ["Outlook", "ContinuMail"],
    });
  });

  it("parses a listing with no profiles and a null default", () => {
    const json = JSON.stringify({ type: "outlookProfiles", classicOutlook: false, defaultProfile: null, profiles: [] });
    expect(parseOutlookProfiles(json)).toEqual({ classicOutlook: false, defaultProfile: null, profiles: [] });
  });

  it("throws on the wrong type", () => {
    expect(() => parseOutlookProfiles(JSON.stringify({ type: "error", message: "boom" }))).toThrow();
  });

  it("throws on unrecognized/non-JSON output", () => {
    expect(() => parseOutlookProfiles("not json at all")).toThrow();
    expect(() => parseOutlookProfiles("")).toThrow();
  });
});

describe("cardProfileState", () => {
  it("is 'none' with zero profiles", () => {
    expect(cardProfileState({ classicOutlook: true, defaultProfile: null, profiles: [] })).toBe("none");
  });

  it("is 'single' with exactly one profile", () => {
    expect(cardProfileState({ classicOutlook: true, defaultProfile: "Outlook", profiles: ["Outlook"] })).toBe("single");
  });

  it("is 'multiple' with two profiles", () => {
    expect(cardProfileState({ classicOutlook: true, defaultProfile: "Outlook", profiles: ["Outlook", "ContinuMail"] })).toBe("multiple");
  });

  it("is 'multiple' with more than two profiles", () => {
    expect(cardProfileState({ classicOutlook: true, defaultProfile: "A", profiles: ["A", "B", "C"] })).toBe("multiple");
  });
});

describe("parseProfileCreate", () => {
  it("passes through created:true, reused:false for a freshly created profile", () => {
    const json = JSON.stringify({ type: "outlookProfileCreate", name: "ContinuMail", created: true, reused: false });
    expect(parseProfileCreate(json)).toEqual({ name: "ContinuMail", created: true, reused: false });
  });

  it("passes through created:false, reused:true when the profile already existed", () => {
    const json = JSON.stringify({ type: "outlookProfileCreate", name: "ContinuMail", created: false, reused: true });
    expect(parseProfileCreate(json)).toEqual({ name: "ContinuMail", created: false, reused: true });
  });

  it("throws a ProfileStageError carrying the stage for a stage-tagged failure", () => {
    const json = JSON.stringify({
      type: "error", stage: "pim-unsupported",
      message: "Outlook did not create the profile. Create a profile manually in Outlook, then retry.",
      fatal: true,
    });
    try {
      parseProfileCreate(json);
      expect.unreachable("expected parseProfileCreate to throw");
    } catch (e) {
      expect(e).toBeInstanceOf(ProfileStageError);
      expect((e as ProfileStageError).stage).toBe("pim-unsupported");
      expect((e as ProfileStageError).message).toContain("Create a profile manually");
    }
  });

  it("throws on unrecognized/non-JSON output", () => {
    expect(() => parseProfileCreate("not json at all")).toThrow();
    expect(() => parseProfileCreate("")).toThrow();
  });
});

describe("parseProfileOpen", () => {
  it("passes through name and launched:true on success", () => {
    const json = JSON.stringify({ type: "outlookProfileOpen", name: "ContinuMail", launched: true });
    expect(parseProfileOpen(json)).toEqual({ name: "ContinuMail", launched: true });
  });

  it("throws a ProfileStageError carrying the stage for a stage-tagged failure (e.g. an unknown profile)", () => {
    const json = JSON.stringify({
      type: "error", stage: "unknown-outlook-profile",
      message: "Outlook profile 'Stale' not found. Available: Outlook, ContinuMail.",
      fatal: true,
    });
    try {
      parseProfileOpen(json);
      expect.unreachable("expected parseProfileOpen to throw");
    } catch (e) {
      expect(e).toBeInstanceOf(ProfileStageError);
      expect((e as ProfileStageError).stage).toBe("unknown-outlook-profile");
      expect((e as ProfileStageError).message).toContain("not found");
    }
  });

  it("throws on unrecognized/non-JSON output", () => {
    expect(() => parseProfileOpen("not json at all")).toThrow();
    expect(() => parseProfileOpen("")).toThrow();
  });
});
