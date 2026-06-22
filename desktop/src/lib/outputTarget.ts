// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { OutputTarget } from "./types";

export function canScan(
  inputMode: "files" | "profile",
  files: string[],
  profileRoot: string | null,
  outputTarget: OutputTarget | null,
): boolean {
  if (inputMode === "profile") return profileRoot !== null; // output chosen after discovery
  return files.length > 0 && outputTarget?.kind === "pstFile";
}
