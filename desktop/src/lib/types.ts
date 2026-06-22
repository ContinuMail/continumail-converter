// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

export interface ScanTotals {
  messages: number;
  bytes: number;
  sourceBytes: number;
  sources: number;
}

export interface SourceRow {
  id: string;
  path: string;
  displayName: string;
  messages: number;
  bytes: number;
  sourceBytes: number;
  dateFrom: string | null;
  dateTo: string | null;
  warnings: number;
  skipped: number;
}

export interface SourceConfigEntry {
  path: string;
  type: "mbox";
  targetFolder?: string;
  targetFolderPath?: string[];
  msfPath?: string;
}

export interface OutputGroupConfig {
  name: string;
  maxSizeMB: number;
  folderMapping: "mirror" | "flatten";
  includeEmptyFolders: boolean;
  sources: SourceConfigEntry[];
}

export interface ConversionConfig {
  outputs: OutputGroupConfig[];
  profilePath?: string;
  junkHandling?: "Off" | "Category" | "Folder";
  dropExpunged?: boolean;
}

export interface FileStat {
  path: string;
  size: number;
}

export interface EnrichmentSummary {
  matched: number;
  skippedMissingId: number;
  skippedDuplicateId: number;
  noMsfMatch: number;
  expungedMatched: number;
  expungedDropped: number;
  sourcesAttempted: number;
  sourcesEnriched: number;
  sourcesDegraded: number;
}

export type Versioned = { schemaVersion?: number };

export type ConvertEvent = (
  | { type: "started"; input?: string; outputDirectory?: string }
  | { type: "scan"; totalMessages: number }
  | {
      type: "progress";
      converted: number;
      total: number;
      warnings: number;
      skipped: number;
      bytes: number;
      currentSource?: string | null;
      currentFolder?: string | null;
    }
  | { type: "warning"; source: string; identifier: string; reason: string }
  | {
      type: "done";
      converted: number;
      skipped: number;
      warnings: number;
      outputs: string[];
      outputDirectory?: string;
      report?: string;
      elapsedMs: number;
      enrichment?: EnrichmentSummary;
    }
  | { type: "error"; stage?: string; message: string; fatal: boolean }
  | {
      type: "cancelled";
      deleted: string[];
      outputs: string[];
      converted: number;
      skipped: number;
      warnings: number;
      elapsedMs?: number;
    }
) & Versioned;

export interface DiscoveredSource {
  path: string;
  type: string;
  targetFolderPath: string[];
  displayName: string;
  sourceBytes: number;
  msfPath: string | null;
}

export interface DiscoverWarning {
  code: string;
  path: string;
  targetFolderPath: string[] | null;
  segment: string | null;
  segmentIndex: number | null;
  relatedPaths: string[] | null;
  message: string;
}

export interface DiscoverSkipped {
  code: string;
  path: string;
  reason: string;
}

export interface DiscoverPairing {
  pairedMsfCount: number;
  unpairedMboxCount: number;
  orphanMsfCount: number;
}

export interface DiscoverResult {
  root: string;
  layout: string;
  sources: DiscoveredSource[];
  warnings: DiscoverWarning[];
  skipped: DiscoverSkipped[];
  pairing: DiscoverPairing;
  schemaVersion?: number;
}

/** A discovery source merged with its scan counts. `id` is the source `path`
 * (profile-mode identity); `displayName` is the joined nested target path. */
export interface ProfileSourceRow extends SourceRow {
  targetFolderPath: string[];
  msfPath: string | null;
}

export interface ColourCategory {
  name: string;
  hex: string | null;
  outlookColor: number | null;
  action: string;
}

export interface ProfileEntry {
  name: string;
  path: string;
  isDefault: boolean;
}
