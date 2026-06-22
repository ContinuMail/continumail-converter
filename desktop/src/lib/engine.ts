// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { invoke } from "@tauri-apps/api/core";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import { open, save } from "@tauri-apps/plugin-dialog";
import { parseEngineOutput, type VersionResult, type ScanResult } from "./parse";
import { parseScanLine } from "./scan";
import { parseDiscover } from "./discover";
import { parseColourImport, type ColourImportParse } from "./colourImport";
import type { ConversionConfig, FileStat, DiscoverResult } from "./types";

export async function checkEngineVersion(): Promise<VersionResult> {
  const stdout = await invoke<string>("check_engine_version");
  const result = parseEngineOutput(stdout);
  if (result.kind !== "version") throw new Error("Expected a version response from the engine");
  return result;
}

export async function scanSample(): Promise<ScanResult> {
  const stdout = await invoke<string>("scan_sample");
  const result = parseEngineOutput(stdout);
  if (result.kind !== "scan") throw new Error("Expected a scan response from the engine");
  return result;
}

/** Run engine discovery on a Thunderbird profile / mail folder (one-shot). */
export async function discoverProfile(dir: string): Promise<DiscoverResult> {
  const stdout = await invoke<string>("discover_profile", { dir });
  return parseDiscover(stdout);
}

/** Scan arbitrary user-selected mbox paths. Spawns the sidecar via `start_scan`
 * and resolves from streamed events (NOT the Tauri command return). `onProgress`
 * receives advisory byte-progress; the Promise resolves with the final result.
 * Rejects (with the engine's stderr text when present) on nonzero exit, a
 * missing final result, or an invoke failure.
 *
 * Scan is not cancellable: if the user navigates away mid-scan the Promise stays
 * pending and its three listeners remain registered until the sidecar exits, at
 * which point cleanup() runs. (The useScan stage-guard drops any late progress
 * update, so a stale bar never leaks into Review/ScanError.) */
export async function scan(
  paths: string[],
  onProgress?: (p: { bytes: number; totalBytes: number }) => void,
): Promise<ScanResult> {
  let result: ScanResult | null = null;
  let stderr = "";
  let unlistenLine: UnlistenFn | null = null;
  let unlistenStderr: UnlistenFn | null = null;
  let unlistenExit: UnlistenFn | null = null;
  const cleanup = () => {
    unlistenLine?.();
    unlistenStderr?.();
    unlistenExit?.();
    unlistenLine = null;
    unlistenStderr = null;
    unlistenExit = null;
  };

  return new Promise<ScanResult>((resolve, reject) => {
    Promise.all([
      listen<string>("scan://line", (e) => {
        const parsed = parseScanLine(e.payload);
        if (!parsed) return;
        if (parsed.type === "scanProgress") {
          onProgress?.({ bytes: parsed.bytes, totalBytes: parsed.totalBytes });
        } else {
          result = parsed.result;
        }
      }),
      listen<string>("scan://stderr", (e) => {
        stderr += e.payload;
      }),
      listen<number | null>("scan://exit", async (e) => {
        // Best-effort: yield one microtask so a `scan://line` handler already
        // queued ahead of this exit runs first. line/exit are separate Tauri
        // channels, so this is not a hard ordering guarantee — if no final result
        // was captured we fall back to the stderr / "no result" reject below
        // (a clean scan always emits its final line before exit).
        await Promise.resolve();
        cleanup();
        if (e.payload && e.payload !== 0) {
          reject(new Error(stderr.trim() || `Scan failed (exit code ${e.payload}).`));
        } else if (result) {
          resolve(result);
        } else {
          reject(new Error(stderr.trim() || "Scan ended without a result."));
        }
      }),
    ])
      .then(([ul, us, ue]) => {
        // Register listeners BEFORE the sidecar starts so no early line is missed.
        unlistenLine = ul;
        unlistenStderr = us;
        unlistenExit = ue;
        return invoke<void>("start_scan", { paths });
      })
      .catch((err) => {
        cleanup();
        reject(err instanceof Error ? err : new Error(String(err)));
      });
  });
}

const MBOX_FILTER = [{ name: "mbox archives", extensions: ["mbox"] }];

/** Returns selected .mbox file paths (multi-select), or [] if cancelled. */
export async function pickMboxFiles(): Promise<string[]> {
  const result = await open({ multiple: true, directory: false, filters: MBOX_FILTER });
  if (result === null) return [];
  return Array.isArray(result) ? result : [result];
}

/** Returns a chosen folder path, or null if cancelled. */
export async function pickFolder(): Promise<string | null> {
  let defaultPath: string | undefined;
  try {
    const d = await invoke<string | null>("default_thunderbird_profiles_dir");
    defaultPath = d ?? undefined;
  } catch {
    defaultPath = undefined; // never block the picker on this
  }
  const result = await open({ multiple: false, directory: true, defaultPath });
  return typeof result === "string" ? result : null;
}

export function listMboxInDir(dir: string): Promise<string[]> {
  return invoke<string[]>("list_mbox_in_dir", { dir });
}

export function statFiles(paths: string[]): Promise<FileStat[]> {
  return invoke<FileStat[]>("stat_files", { paths });
}

/** Save dialog for the output .pst path, or null if cancelled. */
export async function pickOutputPst(): Promise<string | null> {
  const result = await save({ filters: [{ name: "Outlook PST", extensions: ["pst"] }] });
  return result ?? null;
}

export function startConvert(config: ConversionConfig, outputDir: string): Promise<void> {
  // Tauri v2 auto-converts camelCase JS arg keys to snake_case Rust params, so
  // `outputDir` here maps to the Rust `output_dir` parameter.
  return invoke<void>("start_convert", { config, outputDir });
}

export function cancelConvert(): Promise<void> {
  return invoke<void>("cancel_convert");
}

export function openFolder(path: string): Promise<void> {
  return invoke<void>("open_folder", { path });
}

export function openJunkHelp(): Promise<void> {
  return invoke<void>("open_junk_help");
}

/** Preview the Thunderbird→Outlook colour import (Outlook-free). */
export async function previewColours(dir: string): Promise<ColourImportParse> {
  return parseColourImport(await invoke<string>("preview_colours", { dir }));
}

/** Apply the colour import to Outlook's master list (Windows + Outlook, Outlook must be closed). */
export async function applyColours(dir: string): Promise<ColourImportParse> {
  return parseColourImport(await invoke<string>("apply_colours", { dir }));
}
