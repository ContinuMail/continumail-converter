// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { reduceConvert, initialConvertState } from "./useConvert";
import type { ConvertEvent, EnrichmentSummary, ColourPlanEntry } from "./types";

const enr: EnrichmentSummary = {
  matched: 10, skippedMissingId: 0, skippedDuplicateId: 0, noMsfMatch: 0,
  expungedMatched: 2, expungedDropped: 2, sourcesAttempted: 3, sourcesEnriched: 3, sourcesDegraded: 0,
};

const running = { ...initialConvertState, phase: "running" as const };

describe("reduceConvert enrichment capture", () => {
  it("stores enrichment from a done event", () => {
    const done: ConvertEvent = { type: "done", converted: 10, skipped: 0, warnings: 0, outputs: ["x.pst"], elapsedMs: 5, enrichment: enr };
    expect(reduceConvert(running, done).enrichment).toEqual(enr);
  });

  it("defaults enrichment to null when the done event omits it", () => {
    const done: ConvertEvent = { type: "done", converted: 1, skipped: 0, warnings: 0, outputs: [], elapsedMs: 1 };
    expect(reduceConvert(running, done).enrichment).toBeNull();
  });

  it("initial state has null enrichment", () => {
    expect(initialConvertState.enrichment).toBeNull();
  });
});

describe("reduceConvert colourPlan capture", () => {
  it("captures colourPlan from the done event", () => {
    const plan: ColourPlanEntry[] = [{ name: "Important", hex: "#FF0000", outlookColor: 6, action: "would-add" }];
    const next = reduceConvert(running, {
      type: "done", converted: 1, skipped: 0, warnings: 0, outputs: ["a.pst"],
      elapsedMs: 5, colourPlan: plan,
    } as ConvertEvent);
    expect(next.colourPlan).toEqual(plan);
  });

  it("defaults colourPlan to null when the done event omits it", () => {
    const done: ConvertEvent = { type: "done", converted: 1, skipped: 0, warnings: 0, outputs: [], elapsedMs: 1 };
    expect(reduceConvert(running, done).colourPlan).toBeNull();
  });

  it("initial state has null colourPlan", () => {
    expect(initialConvertState.colourPlan).toBeNull();
  });
});

describe("reduceConvert per-type", () => {
  it("copies progress per-type counts + currentPhase", () => {
    const ev = { type: "progress", converted: 10, total: 20, warnings: 0, skipped: 0, bytes: 0,
      phase: "contacts", contactsConverted: 3, contactsTotal: 4 } as ConvertEvent;
    const s = reduceConvert(running, ev);
    expect(s.currentPhase).toBe("contacts");
    expect(s.contacts.converted).toBe(3);
    expect(s.contacts.total).toBe(4);
  });
  it("copies done per-type counts under exact wire names", () => {
    const ev = { type: "done", converted: 100, skipped: 0, warnings: 40, outputs: [], elapsedMs: 1,
      appointmentsConverted: 368, appointmentsSkipped: 0, appointmentWarnings: 38,
      tasksConverted: 7, tasksSkipped: 0, taskWarnings: 1,
      contactsConverted: 4, contactsSkipped: 0, contactWarnings: 0 } as ConvertEvent;
    const s = reduceConvert(running, ev);
    expect(s.appointments).toEqual({ converted: 368, total: 0, skipped: 0, warnings: 38 });
    expect(s.tasks.warnings).toBe(1);
    expect(s.contacts.converted).toBe(4);
  });
});
