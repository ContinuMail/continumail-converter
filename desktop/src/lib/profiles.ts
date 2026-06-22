// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import type { ProfileEntry } from "./types";

export const visibleProfiles = (entries: ProfileEntry[]): ProfileEntry[] => entries.filter((e) => e.convertible);
export const hiddenProfiles = (entries: ProfileEntry[]): ProfileEntry[] => entries.filter((e) => !e.convertible);

export function hiddenNote(entries: ProfileEntry[]): string | null {
  const hidden = hiddenProfiles(entries);
  if (hidden.length === 0) return null;
  if (hidden.length === 1) return `“${hidden[0].name}” was hidden — no convertible mail found in it.`;
  return `${hidden.length} profiles were hidden — no convertible mail found.`;
}

export function profilePrimaryLabel(e: ProfileEntry): string {
  if (e.accounts.length === 0) return e.name;
  const extra = e.accounts.length - 1;
  return extra > 0 ? `${e.accounts[0]} +${extra} more` : e.accounts[0];
}

export const profileSubtext = (e: ProfileEntry): string => `${e.name} · ${e.path}`;

/** Path to preselect, or null to change nothing. Never overrides an existing selection. */
export function pickDefaultProfile(entries: ProfileEntry[], currentProfileRoot: string | null): string | null {
  if (currentProfileRoot !== null) return null;
  const conv = visibleProfiles(entries);
  if (conv.length === 0) return null;
  if (conv.length === 1) return conv[0].path;
  return conv.find((e) => e.isDefault)?.path ?? null;
}
