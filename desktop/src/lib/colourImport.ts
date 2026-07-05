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

export interface OutlookProfiles {
  classicOutlook: boolean;
  defaultProfile: string | null;
  profiles: string[];
}

/** Parse the `outlook-profiles list` result. Unlike parseColourImport there is
 * no user-facing error variant for a plain listing, so unrecognized/malformed
 * output throws rather than returning a typed error. */
export function parseOutlookProfiles(stdout: string): OutlookProfiles {
  const obj = extractJsonObjects(stdout).find(
    (o) => isRecord(o) && o.type === "outlookProfiles",
  ) as Record<string, unknown> | undefined;

  if (!obj) throw new Error("Could not read Outlook profiles.");

  return {
    classicOutlook: obj.classicOutlook === true,
    defaultProfile: typeof obj.defaultProfile === "string" ? obj.defaultProfile : null,
    profiles: Array.isArray(obj.profiles) ? (obj.profiles as string[]) : [],
  };
}

/** 0 / 1 / many profiles → which colour-card state to show. */
export function cardProfileState(p: OutlookProfiles): "none" | "single" | "multiple" {
  if (p.profiles.length === 0) return "none";
  if (p.profiles.length === 1) return "single";
  return "multiple";
}

/** A stage-tagged failure from `outlook-profiles create`/`open` (e.g. `pim-unsupported`
 * when the /PIM switch is unavailable). Carries `stage` so the card can pick the right copy. */
export class ProfileStageError extends Error {
  stage: string;
  constructor(stage: string, message: string) {
    super(message);
    this.name = "ProfileStageError";
    this.stage = stage;
  }
}

/** Parse the `outlook-profiles create` result. A `type:"error"` object throws a
 * ProfileStageError carrying its stage; unrecognized/malformed output throws generically. */
export function parseProfileCreate(stdout: string): { name: string; created: boolean; reused: boolean } {
  const obj = extractJsonObjects(stdout).find(
    (o) => isRecord(o) && (o.type === "outlookProfileCreate" || o.type === "error"),
  ) as Record<string, unknown> | undefined;

  if (!obj) throw new Error("Could not read profile-create result.");

  if (obj.type === "error") {
    const stage = typeof obj.stage === "string" && obj.stage.length > 0 ? obj.stage : "outlook-profiles";
    const message = typeof obj.message === "string" && obj.message.length > 0 ? obj.message : "Could not create the viewing profile.";
    throw new ProfileStageError(stage, message);
  }

  return {
    name: typeof obj.name === "string" ? obj.name : "",
    created: obj.created === true,
    reused: obj.reused === true,
  };
}

/** Parse the `outlook-profiles open` result. A `type:"error"` object throws a
 * ProfileStageError carrying its stage (e.g. an unknown-profile name); unrecognized/malformed
 * output throws generically. Mirrors parseProfileCreate's shape. */
export function parseProfileOpen(stdout: string): { name: string; launched: boolean } {
  const obj = extractJsonObjects(stdout).find(
    (o) => isRecord(o) && (o.type === "outlookProfileOpen" || o.type === "error"),
  ) as Record<string, unknown> | undefined;

  if (!obj) throw new Error("Could not read profile-open result.");

  if (obj.type === "error") {
    const stage = typeof obj.stage === "string" && obj.stage.length > 0 ? obj.stage : "outlook-profiles";
    const message = typeof obj.message === "string" && obj.message.length > 0 ? obj.message : "Could not open Outlook with this profile.";
    throw new ProfileStageError(stage, message);
  }

  return {
    name: typeof obj.name === "string" ? obj.name : "",
    launched: obj.launched === true,
  };
}
