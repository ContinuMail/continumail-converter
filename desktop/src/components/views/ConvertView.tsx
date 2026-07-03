// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useState } from "react";
import { FolderInput, Square } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ProgressBar } from "@/components/ui/progress-bar";
import { StatChip } from "@/components/ui/stat-chip";
import { cancelConvert } from "@/lib/engine";
import { formatRate, formatDuration } from "@/lib/progressStats";
import { useSmoothedProgress } from "@/lib/useSmoothedProgress";
import type { ConvertState } from "@/lib/useConvert";

const PHASE_HEADING: Record<string, string> = {
  mail: "Converting your mail…",
  appointments: "Converting appointments…",
  tasks: "Converting tasks…",
  contacts: "Converting contacts…",
};

export function ConvertView({ state }: { state: ConvertState }) {
  const [cancelling, setCancelling] = useState(false);
  const { displayConverted, mbPerSec, etaSec } = useSmoothedProgress(state);
  const shown = Math.min(Math.floor(displayConverted), state.converted);
  const ratio = state.total > 0 ? shown / state.total : 0;
  const pct = Math.round(ratio * 100);
  return (
    <div className="flex flex-1 flex-col">
      <h1 className="text-xl font-semibold text-foreground">{PHASE_HEADING[state.currentPhase ?? "mail"] ?? "Converting your mail…"}</h1>
      <p className="mt-1 flex items-center gap-2 text-sm text-muted-foreground">
        <FolderInput className="size-4 text-primary" />
        {state.currentFolder ? (
          <>Writing folder <strong className="text-foreground">{state.currentFolder}</strong></>
        ) : (
          "Preparing…"
        )}
      </p>

      <div className="mt-4 flex justify-between text-sm text-foreground">
        <span className="font-semibold">
          {shown.toLocaleString()} / {state.total.toLocaleString()} messages
        </span>
        <span className="text-light-gray">{pct}%</span>
      </div>
      {state.currentPhase && state.currentPhase !== "mail" && (() => {
        const t = state.currentPhase === "appointments" ? state.appointments
          : state.currentPhase === "tasks" ? state.tasks : state.contacts;
        return t.total > 0 ? (
          <div className="text-sm text-light-gray">
            {t.converted.toLocaleString()} of {t.total.toLocaleString()}
          </div>
        ) : null;
      })()}
      <div className="mt-1.5">
        <ProgressBar value={ratio} />
      </div>
      <div className="mt-1.5 flex justify-between text-xs text-light-gray">
        <span>{formatRate(mbPerSec)}</span>
        <span>{etaSec != null ? `~${formatDuration(etaSec)} left` : "—"}</span>
      </div>

      <div className="mt-5 flex gap-2.5">
        <StatChip label="Converted" value={state.converted} />
        <StatChip label="Skipped" value={state.skipped} />
        <StatChip label="Warnings" value={state.warnings} />
      </div>

      <div className="mt-auto pt-5">
        <Button
          variant="outline"
          disabled={cancelling}
          onClick={() => {
            setCancelling(true);
            void cancelConvert();
          }}
        >
          <Square /> {cancelling ? "Cancelling…" : "Cancel"}
        </Button>
      </div>
    </div>
  );
}
