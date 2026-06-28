// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { SourceRow } from "./types";

export interface ReviewTotals {
  messages: number;
  bytes: number;
  folders: number;
  dateFrom: string | null;
  dateTo: string | null;
}

/** The rows that will actually become PST folders: checked, and (when
 * skipEmpty) non-empty. This one predicate backs both the totals and the
 * generated config so they cannot disagree. */
export function effectiveRows(
  sources: SourceRow[],
  checkedIds: Set<string>,
  skipEmpty: boolean,
): SourceRow[] {
  return sources.filter(
    (r) => checkedIds.has(r.id) && !(skipEmpty && r.messages === 0),
  );
}

/** Sum of messages over the rows that will actually be converted (checked, and
 * non-empty when skipEmpty). Same `effectiveRows` predicate that backs the config,
 * so this equals the engine's boundary count for those sources — the value passed
 * as --expected-total. Callers pass the EXACT row set feeding their config (e.g. a
 * single account's rows in multi-account mode), never the all-accounts row set. */
export function expectedTotalMessages(
  sources: SourceRow[],
  checkedIds: Set<string>,
  skipEmpty: boolean,
): number {
  return effectiveRows(sources, checkedIds, skipEmpty).reduce((n, r) => n + r.messages, 0);
}

export function calculateReviewTotals(
  sources: SourceRow[],
  checkedIds: Set<string>,
  skipEmpty: boolean,
): ReviewTotals {
  const rows = effectiveRows(sources, checkedIds, skipEmpty);
  let messages = 0;
  let bytes = 0;
  let dateFrom: string | null = null;
  let dateTo: string | null = null;
  for (const r of rows) {
    messages += r.messages;
    bytes += r.bytes;
    // ISO-8601 UTC strings compare correctly lexicographically.
    if (r.dateFrom && (dateFrom === null || r.dateFrom < dateFrom)) dateFrom = r.dateFrom;
    if (r.dateTo && (dateTo === null || r.dateTo > dateTo)) dateTo = r.dateTo;
  }
  return { messages, bytes, folders: rows.length, dateFrom, dateTo };
}

export type SortField = "default" | "name" | "messages" | "date" | "size";
export type SortDir = "asc" | "desc";

/** Returns a NEW array of sources ordered by `field`/`dir` (never mutates input).
 * `field === "default"` keeps the original (scan) order. Stable for equal keys.
 * For `date`, ascending = oldest-first by range start (dateFrom); rows whose
 * dateFrom is null always sort LAST regardless of direction. `size` orders by
 * estimated PST bytes (SourceRow.bytes); `name` uses fixed English numeric
 * collation (locale-stable). View-only: callers use this for display, never for
 * the conversion write path. */
export function sortSources(sources: SourceRow[], field: SortField, dir: SortDir): SourceRow[] {
  const copy = [...sources];
  if (field === "default") return copy;
  const factor = dir === "asc" ? 1 : -1;
  copy.sort((a, b) => {
    switch (field) {
      case "name":
        return factor * a.displayName.localeCompare(b.displayName, "en", { sensitivity: "base", numeric: true });
      case "messages":
        return factor * (a.messages - b.messages);
      case "size":
        return factor * (a.bytes - b.bytes);
      case "date":
        // Null dateFrom sorts last in BOTH directions (factor not applied to nulls).
        if (a.dateFrom === null && b.dateFrom === null) return 0;
        if (a.dateFrom === null) return 1;
        if (b.dateFrom === null) return -1;
        return factor * a.dateFrom.localeCompare(b.dateFrom);
    }
  });
  return copy;
}

/** A real, sortable column field — excludes the "default" (scan-order) state,
 * which is never a header the user can click. */
export type SortableField = Exclude<SortField, "default">;

/** Computes the next {field, dir} when a sortable column header is clicked.
 * `clicked` is a real column field (never "default" — encoded in the type).
 * Clicking the active column flips the direction; clicking any other column
 * starts it ascending. */
export function nextSort(field: SortField, dir: SortDir, clicked: SortableField): { field: SortableField; dir: SortDir } {
  if (field === clicked) {
    return { field: clicked, dir: dir === "asc" ? "desc" : "asc" };
  }
  return { field: clicked, dir: "asc" };
}
