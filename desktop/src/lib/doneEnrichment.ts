// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { EnrichmentSummary } from "./types";

/** Show the Done enrichment block only when there is something meaningful to
 * report — at least one folder was enriched from .msf, or at least one degraded
 * (could not read its .msf). Hidden for file-mode / no-.msf conversions. */
export function shouldShowEnrichment(e: EnrichmentSummary | null): boolean {
  return !!e && (e.sourcesEnriched > 0 || e.sourcesDegraded > 0);
}

/** Compact one-line summary. Reports enriched/attempted folders + matched
 * messages honestly (a degraded-only run shows "0 of N folders"); appends the
 * expunged-dropped clause only when any were dropped. */
export function formatEnrichmentLine(e: EnrichmentSummary): string {
  let line =
    `Thunderbird data applied to ${e.sourcesEnriched} of ${e.sourcesAttempted} folders` +
    ` · ${e.matched.toLocaleString()} messages matched`;
  if (e.expungedDropped > 0) line += ` · ${e.expungedDropped.toLocaleString()} expunged dropped`;
  return line;
}
