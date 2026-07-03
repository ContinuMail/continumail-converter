import { describe, it, expect } from "vitest";
import { alsoConvertInfo } from "./alsoConvert";
import type { DiscoveredCalendar, DiscoveredAddressBook } from "./types";

const cal = (eventCount: number, taskCount: number): DiscoveredCalendar => ({
  calId: "c", displayName: "d", storeKind: "local", storePath: "/s", calendarType: "both",
  isVisibleInThunderbird: true, eventCount, taskCount,
  defaultCalendarFolderPath: [], defaultTaskFolderPath: [],
});
const book = (contactCount: number | null): DiscoveredAddressBook =>
  ({ displayName: "b", path: `/b${contactCount}`, format: "thunderbird-sqlite", contactCount });

describe("alsoConvertInfo", () => {
  it("sums counts across calendars/books", () => {
    const i = alsoConvertInfo([cal(10, 2), cal(0, 5)], [book(4), book(3)]);
    expect(i.appointments.count).toBe(10);
    expect(i.tasks.count).toBe(7);
    expect(i.contacts.count).toBe(7);
  });
  it("disables appointments independently of tasks", () => {
    const i = alsoConvertInfo([cal(0, 7)], []);
    expect(i.appointments.disabled).toBe(true);
    expect(i.tasks.disabled).toBe(false);
  });
  it("keeps contacts enabled when a count is unknown", () => {
    const i = alsoConvertInfo([], [book(null)]);
    expect(i.contacts.disabled).toBe(false);
    expect(i.contacts.unknown).toBe(true);
    expect(i.contacts.count).toBe(0);
  });
  it("disables contacts only when no books or all known-zero", () => {
    expect(alsoConvertInfo([], []).contacts.disabled).toBe(true);
    expect(alsoConvertInfo([], [book(0), book(0)]).contacts.disabled).toBe(true);
    expect(alsoConvertInfo([], [book(0), book(null)]).contacts.disabled).toBe(false);
  });
});
