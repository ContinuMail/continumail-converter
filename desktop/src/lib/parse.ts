// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { ScanTotals, SourceRow } from "./types";

export class EngineParseError extends Error {}

export type VersionResult = { kind: "version"; version: string; schemaVersion?: number };
export type ScanResult = { kind: "scan"; totals: ScanTotals; sources: SourceRow[]; schemaVersion?: number };
export type EngineResult = VersionResult | ScanResult;

export function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

// Extract every top-level {...} JSON object from arbitrary text (tolerates
// leading/trailing noise, multi-line objects, and multiple objects). Strings are
// respected so braces inside string values don't confuse the depth counter.
export function extractJsonObjects(text: string): unknown[] {
  const objects: unknown[] = [];
  let depth = 0;
  let start = -1;
  let inString = false;
  let escaped = false;

  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (inString) {
      if (escaped) escaped = false;
      else if (ch === "\\") escaped = true;
      else if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') {
      inString = true;
    } else if (ch === "{") {
      if (depth === 0) start = i;
      depth++;
    } else if (ch === "}") {
      if (depth > 0) {
        depth--;
        if (depth === 0 && start >= 0) {
          try {
            objects.push(JSON.parse(text.slice(start, i + 1)));
          } catch {
            // not valid JSON — ignore this candidate
          }
          start = -1;
        }
      }
    }
  }
  return objects;
}

/** Build a ScanResult from a raw `{type:"scan",...}` object. Shared by
 * parseEngineOutput (one-shot) and parseScanLine (streaming). */
export function toScanResult(obj: Record<string, unknown>): ScanResult {
  return {
    kind: "scan",
    totals: obj.totals as ScanTotals,
    sources: (obj.sources ?? []) as SourceRow[],
    schemaVersion: obj.schemaVersion as number | undefined,
  };
}

export function parseEngineOutput(stdout: string): EngineResult {
  const objects = extractJsonObjects(stdout);
  const recognized = objects.find(
    (o) => isRecord(o) && (o.type === "version" || o.type === "scan"),
  ) as Record<string, unknown> | undefined;

  if (!recognized) {
    throw new EngineParseError("No recognized engine JSON object in output");
  }

  if (recognized.type === "version") {
    return {
      kind: "version",
      version: String(recognized.version ?? ""),
      schemaVersion: recognized.schemaVersion as number | undefined,
    };
  }

  return toScanResult(recognized);
}
