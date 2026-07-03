// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import type {
  DiscoveredCalendar, DiscoveredAddressBook, CalendarSourceConfigTS, ContactSourceConfigTS,
} from "./types";
import type { OptionsState } from "./options";
import { alsoConvertInfo } from "./alsoConvert";

export const SYNTHETIC_LOCAL_FOLDERS_KEY = "__synthetic_local_folders__";

export interface RouteGroup { key: string; accountId: string | null; isLocalFolders: boolean }
export interface RoutedPim { calendars?: CalendarSourceConfigTS[]; contacts?: ContactSourceConfigTS[] }
export interface RouteResult {
  perGroup: Map<string, RoutedPim>;
  needsLocalFoldersGroup: boolean;
  warnings: string[];
}

/** Split-mode routing: each calendar/book → its account group; local/fallback → Local Folders
 * (existing group, else the synthetic key). Applies the same effective-enable logic as
 * alsoConvertInfo (zero-count type omitted; unknown contact count kept). Single source of truth
 * for both buildProfileConfigMulti and the Preview. */
export function routeAlsoConvert(
  calendars: DiscoveredCalendar[],
  addressBooks: DiscoveredAddressBook[],
  groups: RouteGroup[],
  options: OptionsState,
): RouteResult {
  const info = alsoConvertInfo(calendars, addressBooks);
  const effAppointments = options.includeAppointments && !info.appointments.disabled;
  const effTasks = options.includeTasks && !info.tasks.disabled;
  const effContacts = options.includeContacts && !info.contacts.disabled;

  const perGroup = new Map<string, RoutedPim>();
  const warnings: string[] = [];
  let needsLocalFoldersGroup = false;

  const lfGroup = groups.find((g) => g.isLocalFolders) ?? null;
  // Route by the group's own accountId (not by assuming key === accountId), so a future preview
  // whose group key differs from the account id still routes correctly.
  const byAccountId = new Map(
    groups.filter((g) => g.accountId != null).map((g) => [g.accountId as string, g]),
  );

  const bucket = (key: string): RoutedPim => {
    let b = perGroup.get(key);
    if (!b) { b = {}; perGroup.set(key, b); }
    return b;
  };

  // Resolve where an item with this accountId goes; may push a warning / flag synthetic need.
  const targetKey = (accountId: string | null, label: string): string => {
    if (accountId != null) {
      const g = byAccountId.get(accountId);
      if (g) return g.key;                                               // matched, selected
      warnings.push(`${label} belongs to an account that isn't part of this split; it will be written to Local Folders.`);
    }
    if (lfGroup) return lfGroup.key;                                     // local → existing LF
    needsLocalFoldersGroup = true;
    return SYNTHETIC_LOCAL_FOLDERS_KEY;                                  // local/fallback → synthetic LF
  };

  if (effAppointments || effTasks) {
    for (const c of calendars) {
      // A zero-appt calendar with tasks still routes; a wholly-empty calendar contributes nothing.
      const wantsAppt = effAppointments && c.eventCount > 0;
      const wantsTask = effTasks && c.taskCount > 0;
      if (!wantsAppt && !wantsTask) continue;
      const entry: CalendarSourceConfigTS = {
        storePath: c.storePath, calId: c.calId,
        includeAppointments: wantsAppt,
        includeTasks: wantsTask,
      };
      if (c.defaultCalendarFolderPath.length > 0) entry.appointmentFolderPath = c.defaultCalendarFolderPath;
      if (c.defaultTaskFolderPath.length > 0) entry.taskFolderPath = c.defaultTaskFolderPath;
      const key = targetKey(c.accountId, `Calendar "${c.displayName}"`);
      (bucket(key).calendars ??= []).push(entry);
    }
  }

  if (effContacts) {
    for (const b of addressBooks) {
      if (!(b.contactCount == null || b.contactCount > 0)) continue;      // skip known-empty
      const key = targetKey(b.accountId, `Address book "${b.displayName}"`);
      (bucket(key).contacts ??= []).push({ path: b.path, format: b.format });
    }
  }

  return { perGroup, needsLocalFoldersGroup, warnings };
}
