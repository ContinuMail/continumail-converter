import { describe, it, expect } from "vitest";
import { shouldShowEnrichment, formatEnrichmentLine } from "./doneEnrichment";
import type { EnrichmentSummary } from "./types";

const base: EnrichmentSummary = {
  matched: 0, skippedMissingId: 0, skippedDuplicateId: 0, noMsfMatch: 0,
  expungedMatched: 0, expungedDropped: 0, sourcesAttempted: 0, sourcesEnriched: 0, sourcesDegraded: 0,
};

describe("shouldShowEnrichment", () => {
  it("false for null", () => expect(shouldShowEnrichment(null)).toBe(false));
  it("false when nothing enriched or degraded", () => expect(shouldShowEnrichment(base)).toBe(false));
  it("true when a folder enriched", () => expect(shouldShowEnrichment({ ...base, sourcesEnriched: 1 })).toBe(true));
  it("true when a folder degraded (none enriched)", () => expect(shouldShowEnrichment({ ...base, sourcesDegraded: 2 })).toBe(true));
});

describe("formatEnrichmentLine", () => {
  it("formats folders + matched", () => {
    expect(formatEnrichmentLine({ ...base, sourcesAttempted: 3, sourcesEnriched: 3, matched: 10 }))
      .toBe("Thunderbird data applied to 3 of 3 folders · 10 messages matched");
  });
  it("appends expunged clause only when >0", () => {
    expect(formatEnrichmentLine({ ...base, sourcesAttempted: 1, sourcesEnriched: 1, matched: 4, expungedDropped: 2 }))
      .toBe("Thunderbird data applied to 1 of 1 folders · 4 messages matched · 2 expunged dropped");
  });
  it("degraded-only: formats honestly without implying enrichment", () => {
    expect(formatEnrichmentLine({ ...base, sourcesAttempted: 2, sourcesEnriched: 0, sourcesDegraded: 2, matched: 0 }))
      .toBe("Thunderbird data applied to 0 of 2 folders · 0 messages matched");
  });
});
