// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { DiscoveredSource, ProfileSourceRow, ConversionConfig, SourceConfigEntry } from "./types";
import type { ScanResult } from "./parse";
import type { OptionsState } from "./options";
import { effectiveRows } from "./review";
import { deriveOutputTarget, ConvertConfigError } from "./convert";

/** Merge discovery sources with scan counts. Identity = discovered `path`; the
 * scan row's id is ignored (it is scan-local). displayName = joined nested path. */
export function mergeProfileSources(
  discovered: DiscoveredSource[], scan: ScanResult,
): ProfileSourceRow[] {
  const byPath = new Map(scan.sources.map((s) => [s.path, s]));
  return discovered.map((d) => {
    const s = byPath.get(d.path);
    return {
      id: d.path,
      path: d.path,
      displayName: d.targetFolderPath.join(" / "),
      messages: s?.messages ?? 0,
      bytes: s?.bytes ?? 0,
      sourceBytes: s?.sourceBytes ?? d.sourceBytes,
      dateFrom: s?.dateFrom ?? null,
      dateTo: s?.dateTo ?? null,
      warnings: s?.warnings ?? 0,
      skipped: s?.skipped ?? 0,
      targetFolderPath: d.targetFolderPath,
      msfPath: d.msfPath,
    };
  });
}

/** Build a full profile-mode ConversionConfig. Mirror emits each source's
 * targetFolderPath + msfPath verbatim (NO leaf-name dedupe — same leaf under
 * different parents is valid). Flatten omits targetFolderPath and sets
 * folderMapping:"flatten" so the engine routes to "Imported Mail"; msfPath and
 * top-level profilePath are kept in both modes so enrichment + tag names apply.
 * junkHandling + dropExpunged are always written at the top-level config. */
export function buildProfileConfig(
  rows: ProfileSourceRow[],
  checkedIds: Set<string>,
  skipEmpty: boolean,
  options: OptionsState,
  outputPstPath: string,
  profileRoot: string,
): { config: ConversionConfig; outputDir: string; pstName: string } {
  const { outputDir, pstName } = deriveOutputTarget(outputPstPath);
  const folderMapping = options.folderMapping;
  const eff = effectiveRows(rows, checkedIds, skipEmpty) as ProfileSourceRow[];
  if (eff.length === 0) {
    throw new ConvertConfigError("Select at least one folder to convert.");
  }

  const sources: SourceConfigEntry[] = eff.map((r) => {
    const entry: SourceConfigEntry = { path: r.path, type: "mbox" };
    if (folderMapping === "mirror") entry.targetFolderPath = r.targetFolderPath;
    if (r.msfPath != null) entry.msfPath = r.msfPath;
    return entry;
  });

  return {
    outputDir,
    pstName,
    config: {
      profilePath: profileRoot,
      junkHandling: options.junkHandling,
      dropExpunged: options.dropExpunged,
      outputs: [{
        name: pstName,
        maxSizeMB: options.maxSizeMB,
        folderMapping,
        includeEmptyFolders: !skipEmpty,
        sources,
      }],
    },
  };
}
