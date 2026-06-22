// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { ConvertEvent } from "./types";

export class ConvertConfigError extends Error {}

/** Split a path into { dir, base, stem } handling both `/` and `\` separators. */
export function splitPath(p: string): { dir: string; base: string; stem: string } {
  const norm = p.replace(/[/\\]+$/, "");
  const idx = Math.max(norm.lastIndexOf("/"), norm.lastIndexOf("\\"));
  const dir = idx >= 0 ? norm.slice(0, idx) : "";
  const base = idx >= 0 ? norm.slice(idx + 1) : norm;
  const dot = base.lastIndexOf(".");
  const stem = dot > 0 ? base.slice(0, dot) : base;
  return { dir, base, stem };
}

/** Validate an output .pst path and split it into { outputDir, pstName }.
 * Used by buildConfigFromOptions. */
export function deriveOutputTarget(outputPstPath: string): { outputDir: string; pstName: string } {
  const { dir, base, stem } = splitPath(outputPstPath);
  if (!/\.pst$/i.test(base) || base.length <= 4) {
    throw new ConvertConfigError("Choose an output file ending in .pst.");
  }
  return { outputDir: dir, pstName: stem };
}

/** Join an output directory and an already-sanitized PST stem into a full
 * `<dir><sep><pstName>.pst` path. Preserves the dir's separator style (the
 * Windows folder picker returns `\`-style paths); collapses a trailing
 * separator. `pstName` must already be sanitized (no separators survive
 * sanitizePstName), so no path-injection re-guard is needed here. */
export function joinOutputPstPath(dir: string, pstName: string): string {
  const sep = dir.includes("\\") ? "\\" : "/";
  const cleanDir = dir.replace(/[/\\]+$/, "");
  return `${cleanDir}${sep}${pstName}.pst`;
}

const KNOWN_TYPES = new Set([
  "started",
  "scan",
  "progress",
  "warning",
  "done",
  "error",
  "cancelled",
]);

export interface WarningItem {
  source: string;
  identifier: string;
  reason: string;
}

/** Append a warning unless the list is already at `cap` (returns the same array
 * when full, so we never grow unbounded on a pathological archive). */
export function appendWarningCapped(list: WarningItem[], w: WarningItem, cap = 200): WarningItem[] {
  if (list.length >= cap) return list;
  return [...list, w];
}

/** Decide how a `convert://exit` should affect a still-running UI. Returns an
 * error message when the process exited while no terminal event had been
 * processed — a nonzero exit, or a zero/null exit with no done/cancelled (which
 * would otherwise hang the progress screen) — or null when no transition is
 * needed (a terminal event already moved the UI out of "running"). */
export function convertExitError(exitCode: number | null, isRunning: boolean): string | null {
  if (!isRunning) return null;
  if (exitCode && exitCode !== 0) return `Conversion failed (exit code ${exitCode}).`;
  return "Conversion ended without a terminal event.";
}

/** Parse one JSON-Lines stdout line into a typed ConvertEvent, or null for
 * blank / non-JSON / unrecognized-type lines (e.g. dev build noise). */
export function parseConvertLine(line: string): ConvertEvent | null {
  const trimmed = line.trim();
  if (trimmed.length === 0 || trimmed[0] !== "{") return null;
  let obj: unknown;
  try {
    obj = JSON.parse(trimmed);
  } catch {
    return null;
  }
  if (typeof obj !== "object" || obj === null) return null;
  const type = (obj as { type?: unknown }).type;
  if (typeof type !== "string" || !KNOWN_TYPES.has(type)) return null;
  return obj as ConvertEvent;
}
