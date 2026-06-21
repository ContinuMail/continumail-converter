// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { extractJsonObjects, isRecord, EngineParseError } from "./parse";
import type {
  DiscoverResult, DiscoveredSource, DiscoverWarning, DiscoverSkipped, DiscoverPairing,
} from "./types";

/** Parse the engine's single pretty-printed `discovery` object (tolerates dev-build
 * noise around it). Throws EngineParseError when no discovery object is present. */
export function parseDiscover(stdout: string): DiscoverResult {
  const obj = extractJsonObjects(stdout).find(
    (o) => isRecord(o) && o.type === "discovery",
  ) as Record<string, unknown> | undefined;
  if (!obj) throw new EngineParseError("No discovery object in engine output");

  const sources = (Array.isArray(obj.sources) ? obj.sources : []).map(
    (s) => s as DiscoveredSource,
  );
  const warnings = (Array.isArray(obj.warnings) ? obj.warnings : []).map(
    (w) => w as DiscoverWarning,
  );
  const skipped = (Array.isArray(obj.skipped) ? obj.skipped : []).map(
    (s) => s as DiscoverSkipped,
  );
  const p = isRecord(obj.pairing) ? obj.pairing : {};
  const pairing: DiscoverPairing = {
    pairedMsfCount: Number(p.pairedMsfCount ?? 0),
    unpairedMboxCount: Number(p.unpairedMboxCount ?? 0),
    orphanMsfCount: Number(p.orphanMsfCount ?? 0),
  };

  return {
    root: String(obj.root ?? ""),
    layout: String(obj.layout ?? ""),
    sources,
    warnings,
    skipped,
    pairing,
    schemaVersion: obj.schemaVersion as number | undefined,
  };
}
