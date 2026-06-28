// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useCallback, useEffect, useRef, useState } from "react";
import { listen } from "@tauri-apps/api/event";
import { parseConvertLine, appendWarningCapped, convertExitError, type WarningItem } from "./convert";
import { startConvert } from "./engine";
import { checkSchemaVersion } from "./schema";
import type { ConversionConfig, ConvertEvent, EnrichmentSummary, ColourPlanEntry } from "./types";

export type ConvertPhase = "idle" | "running" | "done" | "error" | "cancelled";

export interface ConvertState {
  phase: ConvertPhase;
  total: number;
  converted: number;
  skipped: number;
  warnings: number;
  bytes: number;
  startedAtMs: number | null;
  warningList: WarningItem[];
  currentFolder: string | null;
  outputs: string[];
  deleted: string[];
  outputDir: string;
  errorMessage: string | null;
  elapsedMs: number | null;
  enrichment: EnrichmentSummary | null;
  colourPlan: ColourPlanEntry[] | null;
}

export const initialConvertState: ConvertState = {
  phase: "idle",
  total: 0,
  converted: 0,
  skipped: 0,
  warnings: 0,
  bytes: 0,
  startedAtMs: null,
  warningList: [],
  currentFolder: null,
  outputs: [],
  deleted: [],
  outputDir: "",
  errorMessage: null,
  elapsedMs: null,
  enrichment: null,
  colourPlan: null,
};

export function reduceConvert(state: ConvertState, ev: ConvertEvent): ConvertState {
  // Only process events during an active run. This blocks both already-terminal
  // states AND a stray late event arriving after `reset()` (idle) — which would
  // otherwise wrongly flip idle → running.
  if (state.phase !== "running") return state;
  switch (ev.type) {
    case "started":
      return { ...state, phase: "running" };
    case "scan":
      return { ...state, total: ev.totalMessages };
    case "progress": {
      // Defensive: tolerate a missing/malformed bytes field (co-versioned CLI
      // makes this unlikely, but never poison state with NaN).
      const bytes = Number.isFinite(ev.bytes) ? ev.bytes : state.bytes;
      return {
        ...state,
        phase: "running",
        converted: ev.converted,
        total: ev.total,
        warnings: ev.warnings,
        skipped: ev.skipped,
        bytes,
        currentFolder: ev.currentFolder ?? state.currentFolder,
      };
    }
    case "warning":
      return {
        ...state,
        warningList: appendWarningCapped(state.warningList, {
          source: ev.source,
          identifier: ev.identifier,
          reason: ev.reason,
        }),
      };
    case "done":
      return {
        ...state,
        phase: "done",
        converted: ev.converted,
        skipped: ev.skipped,
        warnings: ev.warnings,
        outputs: ev.outputs,
        elapsedMs: ev.elapsedMs,
        enrichment: ev.enrichment ?? null,
        colourPlan: ev.colourPlan ?? null,
      };
    case "error":
      return { ...state, phase: "error", errorMessage: ev.message };
    case "cancelled":
      return {
        ...state,
        phase: "cancelled",
        converted: ev.converted,
        skipped: ev.skipped,
        warnings: ev.warnings,
        deleted: ev.deleted,
        outputs: ev.outputs,
      };
    default:
      return state;
  }
}

export function useConvert() {
  const [state, setState] = useState<ConvertState>(initialConvertState);
  const phaseRef = useRef<ConvertPhase>("idle");
  phaseRef.current = state.phase;
  const schemaWarnedRef = useRef(false);

  useEffect(() => {
    const unlistenLine = listen<string>("convert://line", (e) => {
      const ev = parseConvertLine(e.payload);
      if (!ev) return;
      if (!schemaWarnedRef.current) {
        schemaWarnedRef.current = true;
        const msg = checkSchemaVersion(ev.schemaVersion);
        if (msg) console.warn(`[mail2pst] ${msg}`);
      }
      setState((s) => reduceConvert(s, ev));
    });
    const unlistenExit = listen<number | null>("convert://exit", async (e) => {
      // Let a convert://line handler already queued ahead of this exit run first
      // (line/exit are separate channels, so ordering is best-effort). If the run
      // is still "running" at process exit, no terminal event was processed —
      // surface an error instead of hanging on the progress screen.
      await Promise.resolve();
      setState((s) => {
        const msg = convertExitError(e.payload, s.phase === "running");
        return msg ? { ...s, phase: "error", errorMessage: msg } : s;
      });
    });
    return () => {
      unlistenLine.then((f) => f());
      unlistenExit.then((f) => f());
    };
  }, []);

  const start = useCallback(async (config: ConversionConfig, outputDir: string, expectedTotal?: number) => {
    schemaWarnedRef.current = false;
    setState({ ...initialConvertState, phase: "running", outputDir, startedAtMs: Date.now() });
    try {
      await startConvert(config, outputDir, expectedTotal);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, phase: "error", errorMessage: message }));
    }
  }, []);

  const reset = useCallback(() => setState(initialConvertState), []);

  return { state, start, reset };
}
