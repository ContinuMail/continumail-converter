// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useMemo } from "react";
import { TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { calculateReviewTotals, nextSort, type SortField, type SortDir, type SortableField } from "@/lib/review";
import { formatBytes, formatDateRange } from "@/lib/format";
import type { DiscoverWarning, SourceRow } from "@/lib/types";

interface ReviewViewProps {
  sources: SourceRow[];
  checkedIds: Set<string>;
  skipEmpty: boolean;
  sortBy: SortField;
  sortDir: SortDir;
  onSortChange: (field: SortField, dir: SortDir) => void;
  onToggle: (id: string) => void;
  onSkipEmptyChange: (v: boolean) => void;
  onContinue: () => void;
  onBack: () => void;
  pairedIds?: Set<string>;
  warnings?: DiscoverWarning[];
}

export function ReviewView({
  sources, checkedIds, skipEmpty, sortBy, sortDir, onSortChange,
  onToggle, onSkipEmptyChange, onContinue, onBack, pairedIds, warnings,
}: ReviewViewProps) {
  const totals = useMemo(
    () => calculateReviewTotals(sources, checkedIds, skipEmpty),
    [sources, checkedIds, skipEmpty],
  );
  const canContinue = totals.folders > 0;

  const onHeaderClick = (clicked: SortableField) => {
    const n = nextSort(sortBy, sortDir, clicked);
    onSortChange(n.field, n.dir);
  };
  const arrow = (field: SortField) =>
    sortBy === field ? <span aria-hidden="true">{sortDir === "asc" ? "▲" : "▼"}</span> : null;
  const ariaSort = (field: SortField): "ascending" | "descending" | "none" =>
    sortBy === field ? (sortDir === "asc" ? "ascending" : "descending") : "none";

  return (
    <div className="flex flex-1 flex-col min-h-0">
      <h1 className="text-xl font-semibold text-foreground">Mail folders found</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        {totals.messages.toLocaleString()} messages · ~{formatBytes(totals.bytes)} estimated PST ·{" "}
        {totals.folders} folder{totals.folders === 1 ? "" : "s"} ·{" "}
        {formatDateRange(totals.dateFrom, totals.dateTo)}
      </p>

      <p className="mt-2 text-xs text-light-gray">
        Click a column header to sort. Sorting changes this view only — it doesn't affect the converted file.
      </p>

      {warnings && warnings.length > 0 && (
        <div className="mt-2 flex items-start gap-2 rounded-lg border border-border bg-card px-3 py-2 text-xs text-muted-foreground">
          <TriangleAlert className="mt-0.5 size-4 shrink-0 text-primary" />
          <div>
            {warnings.length} discovery warning{warnings.length === 1 ? "" : "s"}
            <ul className="mt-1 list-disc pl-4">
              {warnings.slice(0, 5).map((w, i) => <li key={i}>{w.message}</li>)}
            </ul>
            {warnings.length > 5 && <div className="mt-0.5">…and {warnings.length - 5} more.</div>}
          </div>
        </div>
      )}

      <div className="mt-2 min-h-0 flex-1 overflow-auto rounded-lg border border-border">
        <table className="w-full text-sm">
          <thead className="sticky top-0 bg-card text-left text-xs text-light-gray">
            <tr>
              <th className="w-8 px-3 py-2"></th>
              <th className="px-2 py-2" aria-sort={ariaSort("name")}>
                <button type="button" className="flex items-center gap-1 hover:text-foreground" onClick={() => onHeaderClick("name")}>
                  Folder{arrow("name")}
                </button>
              </th>
              <th className="px-2 py-2 text-right" aria-sort={ariaSort("messages")}>
                <button type="button" className="flex w-full items-center justify-end gap-1 hover:text-foreground" onClick={() => onHeaderClick("messages")}>
                  Messages{arrow("messages")}
                </button>
              </th>
              <th className="px-2 py-2" aria-sort={ariaSort("date")}>
                <button type="button" className="flex items-center gap-1 hover:text-foreground" onClick={() => onHeaderClick("date")} title="▲ oldest first · ▼ newest first">
                  Dates{arrow("date")}
                </button>
              </th>
              <th className="px-3 py-2 text-right" aria-sort={ariaSort("size")}>
                <button type="button" className="flex w-full items-center justify-end gap-1 hover:text-foreground" onClick={() => onHeaderClick("size")}>
                  Estimated PST size{arrow("size")}
                </button>
              </th>
            </tr>
          </thead>
          <tbody>
            {sources.map((s) => {
              const isEmpty = s.messages === 0;
              const hiddenBySkip = isEmpty && skipEmpty;
              return (
                <tr key={s.id} className={hiddenBySkip ? "text-light-gray/60 line-through" : "text-foreground"}>
                  <td className="px-3 py-1.5">
                    <input
                      type="checkbox"
                      checked={checkedIds.has(s.id)}
                      onChange={() => onToggle(s.id)}
                      aria-label={`Include ${s.displayName}`}
                    />
                  </td>
                  <td className="px-2 py-1.5">
                    {s.displayName}
                    {isEmpty && (
                      <span className="ml-2 rounded bg-muted px-1.5 py-0.5 text-[10px] uppercase text-muted-foreground">empty</span>
                    )}
                    {pairedIds && (
                      pairedIds.has(s.id)
                        ? <span className="ml-2 rounded bg-data-present/15 px-1.5 py-0.5 text-[10px] uppercase text-data-present">✓ .msf</span>
                        : <span className="ml-2 rounded bg-muted px-1.5 py-0.5 text-[10px] uppercase text-muted-foreground">no .msf</span>
                    )}
                  </td>
                  <td className="px-2 py-1.5 text-right">{s.messages.toLocaleString()}</td>
                  <td className="px-2 py-1.5">{formatDateRange(s.dateFrom, s.dateTo)}</td>
                  <td className="px-3 py-1.5 text-right">{formatBytes(s.bytes)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div className="mt-3">
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input type="checkbox" checked={skipEmpty} onChange={(e) => onSkipEmptyChange(e.target.checked)} />
          Skip empty folders
        </label>
        <p className="ml-6 text-xs text-light-gray">Empty folders are skipped if checked.</p>
      </div>

      {!canContinue && (
        <p className="mt-3 text-sm text-muted-foreground">No messages found in the selected files.</p>
      )}

      <div className="mt-4 flex items-center justify-end">
        <div className="flex gap-3">
          <Button variant="outline" onClick={onBack}>‹ Back</Button>
          <Button disabled={!canContinue} onClick={onContinue}>Continue to Options ›</Button>
        </div>
      </div>
    </div>
  );
}
