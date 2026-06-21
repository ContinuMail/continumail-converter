// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { reduceConvert, initialConvertState } from "./useConvert";
import type { ConvertEvent, EnrichmentSummary } from "./types";

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
