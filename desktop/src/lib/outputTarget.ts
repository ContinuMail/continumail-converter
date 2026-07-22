// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { OutputTarget } from "./types";
import type { InputMode } from "./inputMode";

export function canScan(
  inputMode: InputMode,
  files: string[],
  profileRoot: string | null,
  outputTarget: OutputTarget | null,
  folderTreeDiscovery: { sources: unknown[] } | null = null,
  folderTreeDiscovering = false,
): boolean {
  if (inputMode === "profile") return profileRoot !== null; // output chosen after discovery
  if (inputMode === "folderTree") {
    return profileRoot !== null && !folderTreeDiscovering && (folderTreeDiscovery?.sources.length ?? 0) > 0 && outputTarget?.kind === "pstFile";
  }
  return files.length > 0 && outputTarget?.kind === "pstFile";
}
