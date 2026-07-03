// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { describe, it, expect } from "vitest";
import { routeAlsoConvert, SYNTHETIC_LOCAL_FOLDERS_KEY } from "./routeAlsoConvert";
import { defaultOptions } from "./options";
import type { DiscoveredCalendar, DiscoveredAddressBook } from "./types";

const cal = (accountId: string | null, ev = 2, tk = 1): DiscoveredCalendar => ({
  calId: "c" + accountId, displayName: "d", storeKind: "local", storePath: "/s" + accountId,
  calendarType: "both", isVisibleInThunderbird: true, eventCount: ev, taskCount: tk,
  defaultCalendarFolderPath: ["Calendars", "d"], defaultTaskFolderPath: ["Tasks", "d"], accountId,
});
const book = (accountId: string | null, count: number | null = 3): DiscoveredAddressBook =>
  ({ displayName: "b", path: "/b" + accountId, format: "thunderbird-sqlite", contactCount: count, accountId });

const G = (key: string, isLocalFolders = false) => ({ key, accountId: key, isLocalFolders });
const opts = defaultOptions();

describe("routeAlsoConvert", () => {
  it("routes a matched calendar/book to its account group", () => {
    const r = routeAlsoConvert([cal("/acc/gmail")], [book("/acc/gmail")], [G("/acc/gmail"), G("/acc/lf", true)], opts);
    expect(r.perGroup.get("/acc/gmail")!.calendars).toHaveLength(1);
    expect(r.perGroup.get("/acc/gmail")!.contacts).toHaveLength(1);
    expect(r.perGroup.has("/acc/lf")).toBe(false);
    expect(r.needsLocalFoldersGroup).toBe(false);
    expect(r.warnings).toEqual([]);
  });

  it("routes a local item to an existing Local Folders group (no synthetic)", () => {
    const r = routeAlsoConvert([cal(null)], [book(null)], [G("/acc/gmail"), G("/acc/lf", true)], opts);
    expect(r.perGroup.get("/acc/lf")!.calendars).toHaveLength(1);
    expect(r.perGroup.get("/acc/lf")!.contacts).toHaveLength(1);
    expect(r.needsLocalFoldersGroup).toBe(false);
  });

  it("creates a synthetic LF bucket when no LF group exists", () => {
    const r = routeAlsoConvert([cal(null)], [book(null)], [G("/acc/gmail")], opts);
    expect(r.perGroup.get(SYNTHETIC_LOCAL_FOLDERS_KEY)!.calendars).toHaveLength(1);
    expect(r.needsLocalFoldersGroup).toBe(true);
  });

  it("deselected/unmatched account falls back to LF and warns", () => {
    const r = routeAlsoConvert([cal("/acc/deleted")], [], [G("/acc/gmail"), G("/acc/lf", true)], opts);
    expect(r.perGroup.get("/acc/lf")!.calendars).toHaveLength(1);
    expect(r.warnings.length).toBe(1);
    expect(r.warnings[0]).toMatch(/isn't part of this split|Local Folders/i);
  });

  it("respects effective-enable: zero-count calendar emits nothing", () => {
    const r = routeAlsoConvert([cal("/acc/gmail", 0, 0)], [], [G("/acc/gmail")], opts);
    expect(r.perGroup.get("/acc/gmail")).toBeUndefined();
  });

  it("appointments off, tasks on -> includeAppointments=false, includeTasks=true", () => {
    const r = routeAlsoConvert([cal("/acc/gmail")], [], [G("/acc/gmail")],
      { ...opts, includeAppointments: false, includeTasks: true });
    const c = r.perGroup.get("/acc/gmail")!.calendars![0];
    expect(c.includeAppointments).toBe(false);
    expect(c.includeTasks).toBe(true);
  });

  it("unknown contact count still routes; .mab-style null count is included", () => {
    const r = routeAlsoConvert([], [book(null, null)], [G("/acc/lf", true)], opts);
    expect(r.perGroup.get("/acc/lf")!.contacts).toHaveLength(1);
  });

  it("contacts toggled off emits no contacts", () => {
    const r = routeAlsoConvert([], [book("/acc/gmail")], [G("/acc/gmail")], { ...opts, includeContacts: false });
    expect(r.perGroup.get("/acc/gmail")).toBeUndefined();
  });
});
