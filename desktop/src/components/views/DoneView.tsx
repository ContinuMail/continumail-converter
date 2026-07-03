// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { Check, Package, FolderOpen, TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { openFolder } from "@/lib/engine";
import { splitPath } from "@/lib/convert";
import type { ConvertState } from "@/lib/useConvert";
import { formatElapsed } from "@/lib/progressStats";
import { shouldShowEnrichment, formatEnrichmentLine } from "@/lib/doneEnrichment";
import { perTypeRows, anySkipped, doneSubtitle } from "@/lib/perTypeRows";
import { ColourImportCard } from "@/components/views/ColourImportCard";

export function DoneView({ state, profileRoot, onConvertAnother }: { state: ConvertState; profileRoot?: string | null; onConvertAnother: () => void }) {
  const first = state.outputs[0];
  const folder = first ? splitPath(first).dir : state.outputDir;
  const elapsed = state.elapsedMs != null ? formatElapsed(state.elapsedMs) : "";
  const rows = perTypeRows(state);
  const showSkipped = anySkipped(rows);
  const title =
    state.outputs.length <= 1
      ? first
        ? splitPath(first).base
        : "Output"
      : `${state.outputs.length} PST files`;

  return (
    <div className="flex flex-1 flex-col">
      <div className="flex items-center gap-3.5">
        <div className="flex size-11 items-center justify-center rounded-full bg-primary">
          <Check className="size-6 text-primary-foreground" />
        </div>
        <div>
          <h1 className="text-xl font-semibold text-foreground">Conversion complete</h1>
          <p className="text-sm text-muted-foreground">{doneSubtitle(state, elapsed)}</p>
        </div>
      </div>

      <div className="mt-5 flex items-center justify-between rounded-[11px] border border-border bg-card px-4 py-3">
        <div className="flex items-center gap-3">
          <Package className="size-5 text-primary" />
          <div>
            <div className="text-sm font-semibold text-foreground">{title}</div>
            <div className="text-xs text-light-gray">{folder}</div>
          </div>
        </div>
        {(first || folder) && (
          <Button onClick={() => void openFolder(first ?? folder)}>
            <FolderOpen /> Open folder
          </Button>
        )}
      </div>

      <div className="mt-4 overflow-hidden rounded-[10px] border border-border bg-card text-sm">
        <div className="flex border-b border-border px-3.5 py-1.5 text-[11px] uppercase tracking-wide text-light-gray">
          <div className="flex-1">Type</div>
          <div className="w-20 text-right">Converted</div>
          {showSkipped && <div className="w-20 text-right">Skipped</div>}
          <div className="w-20 text-right">Warnings</div>
        </div>
        {rows.map((r) => (
          <div key={r.label} className="flex border-t border-border px-3.5 py-2 first:border-t-0 text-foreground">
            <div className="flex-1 font-medium">{r.label}</div>
            <div className="w-20 text-right tabular-nums">{r.converted.toLocaleString()}</div>
            {showSkipped && <div className="w-20 text-right tabular-nums text-light-gray">{r.skipped.toLocaleString()}</div>}
            <div className="w-20 text-right tabular-nums text-light-gray">{r.warnings.toLocaleString()}</div>
          </div>
        ))}
      </div>

      {state.enrichment && shouldShowEnrichment(state.enrichment) && (
        <div className="mt-4 rounded-[10px] border border-border bg-card px-3.5 py-3 text-xs">
          <div className="flex items-center gap-2 text-foreground">
            <span className="text-data-present font-bold">✓</span>
            {formatEnrichmentLine(state.enrichment)}
          </div>
          {state.enrichment.sourcesDegraded > 0 && (
            <div className="mt-1.5 flex items-start gap-2 text-muted-foreground">
              <TriangleAlert className="mt-0.5 size-4 shrink-0 text-primary" />
              <span>
                {state.enrichment.sourcesDegraded} folder{state.enrichment.sourcesDegraded === 1 ? "" : "s"} couldn't read their .msf — flags/tags not applied there.
              </span>
            </div>
          )}
        </div>
      )}

      {profileRoot && state.colourPlan && state.colourPlan.length > 0 && (
        <ColourImportCard plan={state.colourPlan} />
      )}

      <div className="mt-4 rounded-[10px] border border-dashed border-border bg-card px-3.5 py-3 text-xs text-muted-foreground">
        Keep your original .mbox files until you've opened the PST in Outlook and confirmed everything looks right.
      </div>

      {state.warningList.length > 0 && (
        <div className="mt-4 flex max-h-40 flex-col overflow-hidden rounded-[10px] border border-border bg-card">
          <div className="border-b border-border px-3.5 py-2 text-xs font-semibold text-foreground">
            Warnings ({state.warnings.toLocaleString()})
          </div>
          <div className="min-h-0 flex-1 overflow-auto px-3.5 py-2 text-xs text-light-gray">
            {state.warningList.map((w, i) => (
              <div key={i} className="py-0.5">
                <span className="text-foreground">{w.source}</span>{w.identifier ? ` ${w.identifier}` : ""} — {w.reason}
              </div>
            ))}
            {state.warnings > state.warningList.length && (
              <div className="py-1 italic">
                …and {Math.max(0, state.warnings - state.warningList.length).toLocaleString()} more — see conversion-report.txt
              </div>
            )}
          </div>
        </div>
      )}

      <div className="mt-auto pt-5">
        <Button variant="outline" onClick={onConvertAnother}>
          Convert another
        </Button>
      </div>
    </div>
  );
}
