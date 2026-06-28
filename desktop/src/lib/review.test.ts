// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { effectiveRows, calculateReviewTotals, sortSources, nextSort, expectedTotalMessages } from "./review";
import type { SourceRow } from "./types";

function row(over: Partial<SourceRow>): SourceRow {
  return {
    id: "x", path: "x.mbox", displayName: "x", messages: 0, bytes: 0,
    sourceBytes: 0, dateFrom: null, dateTo: null, warnings: 0, skipped: 0,
    ...over,
  };
}

const inbox = row({ id: "inbox", path: "inbox.mbox", messages: 100, bytes: 1000, dateFrom: "2012-01-01T00:00:00Z", dateTo: "2020-01-01T00:00:00Z" });
const sent = row({ id: "sent", path: "sent.mbox", messages: 50, bytes: 500, dateFrom: "2015-01-01T00:00:00Z", dateTo: "2024-01-01T00:00:00Z" });
const empty = row({ id: "empty", path: "empty.mbox", messages: 0, bytes: 0 });
const all = [inbox, sent, empty];

describe("effectiveRows", () => {
  it("keeps checked, non-empty rows when skipEmpty is on", () => {
    const ids = new Set(["inbox", "sent", "empty"]);
    expect(effectiveRows(all, ids, true).map((r) => r.id)).toEqual(["inbox", "sent"]);
  });
  it("keeps a checked empty row when skipEmpty is off", () => {
    const ids = new Set(["inbox", "empty"]);
    expect(effectiveRows(all, ids, false).map((r) => r.id)).toEqual(["inbox", "empty"]);
  });
  it("drops unchecked rows regardless of skipEmpty", () => {
    const ids = new Set(["inbox"]);
    expect(effectiveRows(all, ids, false).map((r) => r.id)).toEqual(["inbox"]);
  });
});

describe("calculateReviewTotals", () => {
  it("sums messages/bytes/folders over effective rows and spans the date range", () => {
    const ids = new Set(["inbox", "sent", "empty"]);
    expect(calculateReviewTotals(all, ids, true)).toEqual({
      messages: 150, bytes: 1500, folders: 2,
      dateFrom: "2012-01-01T00:00:00Z", dateTo: "2024-01-01T00:00:00Z",
    });
  });
  it("returns null dates when all effective rows have null dates", () => {
    const ids = new Set(["empty"]);
    expect(calculateReviewTotals(all, ids, false)).toEqual({
      messages: 0, bytes: 0, folders: 1, dateFrom: null, dateTo: null,
    });
  });
});

describe("sortSources", () => {
  const f10 = row({ id: "f10", displayName: "Folder 10", messages: 5, bytes: 50, dateFrom: "2019-01-01T00:00:00Z" });
  const f2 = row({ id: "f2", displayName: "folder 2", messages: 20, bytes: 200, dateFrom: "2010-01-01T00:00:00Z" });
  const f1 = row({ id: "f1", displayName: "Folder 1", messages: 20, bytes: 200, dateFrom: null });
  const base = [f10, f2, f1];

  it("default returns original order, a new array, and does not mutate input", () => {
    const out = sortSources(base, "default", "desc");
    expect(out.map((r) => r.id)).toEqual(["f10", "f2", "f1"]);
    expect(out).not.toBe(base);
    expect(base.map((r) => r.id)).toEqual(["f10", "f2", "f1"]);
  });

  it("name asc uses numeric, case-insensitive collation (Folder 2 before Folder 10)", () => {
    expect(sortSources(base, "name", "asc").map((r) => r.id)).toEqual(["f1", "f2", "f10"]);
  });
  it("name desc reverses", () => {
    expect(sortSources(base, "name", "desc").map((r) => r.id)).toEqual(["f10", "f2", "f1"]);
  });

  it("messages asc then desc (ties keep input order)", () => {
    expect(sortSources(base, "messages", "asc").map((r) => r.id)).toEqual(["f10", "f2", "f1"]);
    expect(sortSources(base, "messages", "desc").map((r) => r.id)).toEqual(["f2", "f1", "f10"]);
  });

  it("size asc then desc", () => {
    expect(sortSources(base, "size", "asc").map((r) => r.id)).toEqual(["f10", "f2", "f1"]);
    expect(sortSources(base, "size", "desc").map((r) => r.id)).toEqual(["f2", "f1", "f10"]);
  });

  it("date asc = oldest first, null dateFrom always last", () => {
    expect(sortSources(base, "date", "asc").map((r) => r.id)).toEqual(["f2", "f10", "f1"]);
  });
  it("date desc = newest first, null dateFrom STILL last", () => {
    expect(sortSources(base, "date", "desc").map((r) => r.id)).toEqual(["f10", "f2", "f1"]);
  });

  it("is stable for equal keys (messages tie keeps input order, both directions of input)", () => {
    expect(sortSources([f2, f1], "messages", "asc").map((r) => r.id)).toEqual(["f2", "f1"]);
    expect(sortSources([f1, f2], "messages", "asc").map((r) => r.id)).toEqual(["f1", "f2"]);
  });
});

describe("nextSort", () => {
  it("clicking an inactive column sorts it ascending", () => {
    expect(nextSort("default", "desc", "size")).toEqual({ field: "size", dir: "asc" });
    expect(nextSort("name", "asc", "messages")).toEqual({ field: "messages", dir: "asc" });
  });
  it("clicking the active ascending column flips to descending", () => {
    expect(nextSort("size", "asc", "size")).toEqual({ field: "size", dir: "desc" });
  });
  it("clicking the active descending column flips to ascending", () => {
    expect(nextSort("size", "desc", "size")).toEqual({ field: "size", dir: "asc" });
  });
  it("clicking a different active column resets to ascending", () => {
    expect(nextSort("date", "desc", "name")).toEqual({ field: "name", dir: "asc" });
  });
});

describe("expectedTotalMessages", () => {
  const rows = [
    row({ id: "a", path: "a.mbox", displayName: "a", messages: 5 }),
    row({ id: "b", path: "b.mbox", displayName: "b", messages: 0 }),
    row({ id: "c", path: "c.mbox", displayName: "c", messages: 7 }),
  ];

  it("sums messages over checked rows", () => {
    expect(expectedTotalMessages(rows, new Set(["a", "c"]), false)).toBe(12);
  });

  it("excludes unchecked rows (the multi-account split case)", () => {
    // Only account 'a' selected → 'c' must NOT inflate the denominator.
    expect(expectedTotalMessages(rows, new Set(["a"]), false)).toBe(5);
  });

  it("skipEmpty does not change the sum (empty rows contribute 0)", () => {
    expect(expectedTotalMessages(rows, new Set(["a", "b", "c"]), true)).toBe(12);
    expect(expectedTotalMessages(rows, new Set(["a", "b", "c"]), false)).toBe(12);
  });

  it("is 0 when nothing is checked", () => {
    expect(expectedTotalMessages(rows, new Set(), false)).toBe(0);
  });
});
