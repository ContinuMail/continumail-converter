// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { SourceRow, SourceConfigEntry, ConversionConfig } from "./types";
import { effectiveRows } from "./review";
import { deriveOutputTarget, ConvertConfigError } from "./convert";

export type FolderMapping = "mirror" | "flatten";

export const FLATTEN_DEFAULT_NAME = "Imported Mail";
export const FLATTEN_SOURCE_ID = "__flatten__";
export const PST_SAFE_CAP_MB = 51200; // 50 GB Unicode PST limit

export interface OptionsState {
  folderMapping: FolderMapping;
  maxSizeMB: number;
  allowOversize: boolean;
  renames: Record<string, string>; // sourceId -> folder name (Mirror)
  flattenFolderName: string;
  junkHandling: "Off" | "Category" | "Folder";
  dropExpunged: boolean;
}

/** Fresh default options (factory, so callers never share a mutable object). */
export function defaultOptions(): OptionsState {
  return {
    folderMapping: "mirror",
    maxSizeMB: PST_SAFE_CAP_MB,
    allowOversize: false,
    renames: {},
    flattenFolderName: FLATTEN_DEFAULT_NAME,
    junkHandling: "Off",
    dropExpunged: false,
  };
}

export interface PstPreviewFolder {
  sourceId: string;
  name: string;
  messages: number;
  bytes: number;
}

export interface PstPreview {
  pstName: string;
  folders: PstPreviewFolder[];
  totalBytes: number;
  estimatedParts: number;
}

/** Lower-cased, trimmed form used for duplicate detection. */
export function normalizeFolderName(name: string): string {
  return name.trim().toLocaleLowerCase();
}

/** Returns an error message for an unsafe folder name, or null if valid. */
export function validateFolderName(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length === 0) return "Folder name can't be empty.";
  if (/[/\\]/.test(name)) return "Folder name can't contain \\ or /.";
  if ([...name].some((ch) => ch.charCodeAt(0) < 0x20)) {
    return "Folder name can't contain control characters.";
  }
  if (name !== trimmed) return "Folder name can't start or end with a space.";
  if (trimmed.startsWith(".") || trimmed.endsWith(".")) {
    return "Folder name can't start or end with a dot.";
  }
  if (/^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?$/i.test(trimmed)) {
    return "Folder name is reserved on Windows.";
  }
  return null;
}

/** Source ids whose (normalized) folder name collides with another row. */
export function findDuplicateFolderIds(folders: PstPreviewFolder[]): Set<string> {
  const byKey = new Map<string, string[]>();
  for (const f of folders) {
    const key = normalizeFolderName(f.name);
    const ids = byKey.get(key) ?? [];
    ids.push(f.sourceId);
    byKey.set(key, ids);
  }
  const dups = new Set<string>();
  for (const ids of byKey.values()) {
    if (ids.length > 1) ids.forEach((id) => dups.add(id));
  }
  return dups;
}

export interface SplitPreset { label: string; mb: number; }

export const SPLIT_PRESETS: SplitPreset[] = [
  { label: "Up to 50 GB", mb: PST_SAFE_CAP_MB },
  { label: "2 GB", mb: 2048 },
  { label: "5 GB", mb: 5120 },
  { label: "10 GB", mb: 10240 },
  { label: "20 GB", mb: 20480 },
];

export const OVERSIZE_MAX_GB = 1024; // 1 TB ceiling for the experimental opt-in

/** Parse a custom split size typed in GB into MB, or return an error message.
 * Without oversize the cap is 50 GB; with it, OVERSIZE_MAX_GB. */
export function resolveCustomGbToMb(
  text: string,
  allowOversize: boolean,
): { mb: number } | { error: string } {
  const gb = Number(text);
  if (text.trim().length === 0 || !Number.isFinite(gb)) return { error: "Enter a size in GB." };
  if (gb < 1) return { error: "Minimum split size is 1 GB." };
  const maxGb = allowOversize ? OVERSIZE_MAX_GB : 50;
  if (gb > maxGb) return { error: `Maximum is ${maxGb} GB.` };
  return { mb: Math.round(gb * 1024) };
}

/** The folder tree that will be written, given the current selection + options.
 * Mirror = one folder per effective source; Flatten = a single merged folder. */
export function buildPstPreview(
  sources: SourceRow[],
  checkedIds: Set<string>,
  skipEmpty: boolean,
  options: OptionsState,
  pstName: string,
): PstPreview {
  const rows = effectiveRows(sources, checkedIds, skipEmpty);
  const totalBytes = rows.reduce((n, r) => n + r.bytes, 0);

  // No silent fallback for user-edited names: if a rename key exists we show it
  // verbatim (even if empty/whitespace) so validateFolderName can flag it. Only
  // an ABSENT rename falls back to the file stem.
  let folders: PstPreviewFolder[];
  if (options.folderMapping === "flatten") {
    if (rows.length === 0) {
      folders = [];
    } else {
      folders = [{
        sourceId: FLATTEN_SOURCE_ID,
        name: options.flattenFolderName,
        messages: rows.reduce((n, r) => n + r.messages, 0),
        bytes: totalBytes,
      }];
    }
  } else {
    folders = rows.map((r) => {
      const hasRename = Object.prototype.hasOwnProperty.call(options.renames, r.id);
      return {
        sourceId: r.id,
        name: hasRename ? options.renames[r.id] : r.displayName,
        messages: r.messages,
        bytes: r.bytes,
      };
    });
  }

  const maxBytes = options.maxSizeMB * 1024 * 1024;
  const estimatedParts = totalBytes === 0 ? 1 : Math.max(1, Math.ceil(totalBytes / maxBytes));
  return { pstName, folders, totalBytes, estimatedParts };
}

/** Build the final ConversionConfig from the selection + options. Validates the
 * final folder names + uniqueness up front (fast feedback before invoking the CLI;
 * the engine's ConfigValidator re-validates as a backstop), reusing buildPstPreview
 * so it checks exactly what's shown. */
export function buildConfigFromOptions(
  sources: SourceRow[],
  checkedIds: Set<string>,
  skipEmpty: boolean,
  options: OptionsState,
  outputPstPath: string,
): { config: ConversionConfig; outputDir: string; pstName: string } {
  const { outputDir, pstName } = deriveOutputTarget(outputPstPath);
  const rows = effectiveRows(sources, checkedIds, skipEmpty);
  if (rows.length === 0) {
    throw new ConvertConfigError("Select at least one folder to convert.");
  }

  const preview = buildPstPreview(sources, checkedIds, skipEmpty, options, pstName);
  for (const f of preview.folders) {
    const err = validateFolderName(f.name);
    if (err) throw new ConvertConfigError(err);
  }
  if (findDuplicateFolderIds(preview.folders).size > 0) {
    throw new ConvertConfigError("Two folders have the same name. Rename one so each is unique.");
  }

  let configSources: SourceConfigEntry[];
  if (options.folderMapping === "flatten") {
    const folder = preview.folders[0].name; // validated above
    configSources = rows.map((r) => ({ path: r.path, type: "mbox" as const, targetFolder: folder }));
  } else {
    configSources = rows.map((r) => {
      const hasRename = Object.prototype.hasOwnProperty.call(options.renames, r.id);
      const name = hasRename ? options.renames[r.id] : r.displayName;
      const entry: SourceConfigEntry = { path: r.path, type: "mbox" as const };
      // Only send targetFolder when the name differs from the default stem.
      if (name !== r.displayName) entry.targetFolder = name;
      return entry;
    });
  }

  return {
    outputDir,
    pstName,
    config: {
      outputs: [{
        name: pstName,
        maxSizeMB: options.maxSizeMB,
        folderMapping: options.folderMapping,
        includeEmptyFolders: !skipEmpty,
        sources: configSources,
      }],
    },
  };
}
