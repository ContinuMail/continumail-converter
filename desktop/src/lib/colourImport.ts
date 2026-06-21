// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { extractJsonObjects, isRecord } from "./parse";
import type { ColourCategory } from "./types";

export type ColourImportParse =
  | { kind: "result"; mode: "preview" | "apply"; outlookAvailable: boolean; categories: ColourCategory[] }
  | { kind: "error"; message: string };

/** Parse the one-shot import-colours stdout into a result or a typed error.
 * Never throws on engine/user output: unrecognized/malformed → a generic error. */
export function parseColourImport(stdout: string): ColourImportParse {
  const obj = extractJsonObjects(stdout).find(
    (o) => isRecord(o) && (o.type === "importColours" || o.type === "error"),
  ) as Record<string, unknown> | undefined;

  if (!obj) return { kind: "error", message: "Could not read colour-import result." };

  if (obj.type === "error") {
    const message = typeof obj.message === "string" && obj.message.length > 0 ? obj.message : "Colour import failed.";
    return { kind: "error", message };
  }

  return {
    kind: "result",
    mode: obj.mode === "apply" ? "apply" : "preview",
    outlookAvailable: obj.outlookAvailable === true,
    categories: Array.isArray(obj.categories) ? (obj.categories as ColourCategory[]) : [],
  };
}

/** Count an apply result's outcomes for the success line. */
export function summarizeColourApply(categories: ColourCategory[]): { added: number; existing: number } {
  let added = 0;
  let existing = 0;
  for (const c of categories) {
    if (c.action === "added") added++;
    else if (c.action === "skipped-existing") existing++;
  }
  return { added, existing };
}
