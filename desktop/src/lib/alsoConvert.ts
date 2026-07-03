// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import type { DiscoveredCalendar, DiscoveredAddressBook } from "./types";

export interface AlsoConvertInfo {
  appointments: { count: number; disabled: boolean };
  tasks: { count: number; disabled: boolean };
  contacts: { count: number; unknown: boolean; disabled: boolean };
}

/** Counts + per-type disable flags for the "Also convert" toggles.
 * Appointments/tasks disable independently on their own count. Contacts disable
 * only when there are no address books or every KNOWN count is 0 — an unknown
 * (null) count keeps contacts enabled (a count failure must not block conversion). */
export function alsoConvertInfo(
  calendars: DiscoveredCalendar[],
  addressBooks: DiscoveredAddressBook[],
): AlsoConvertInfo {
  const appt = calendars.reduce((n, c) => n + c.eventCount, 0);
  const task = calendars.reduce((n, c) => n + c.taskCount, 0);
  const known = addressBooks.filter((b) => b.contactCount != null);
  const contactCount = known.reduce((n, b) => n + (b.contactCount ?? 0), 0);
  const unknown = addressBooks.some((b) => b.contactCount == null);
  const contactsDisabled = addressBooks.length === 0 || addressBooks.every((b) => b.contactCount === 0);
  return {
    appointments: { count: appt, disabled: appt === 0 },
    tasks: { count: task, disabled: task === 0 },
    contacts: { count: contactCount, unknown, disabled: contactsDisabled },
  };
}
