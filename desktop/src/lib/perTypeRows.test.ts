import { describe, it, expect } from "vitest";
import { perTypeRows, anySkipped, totalItems, mailWarnings, doneSubtitle } from "./perTypeRows";
import { initialConvertState } from "./useConvert";

// Real numbers from a full run: done.warnings=40 is the GRAND TOTAL (mail 1 + appt 38 + task 1).
const base = {
  ...initialConvertState, converted: 16330, skipped: 0, warnings: 40,
  appointments: { converted: 368, total: 0, skipped: 0, warnings: 38 },
  tasks: { converted: 7, total: 0, skipped: 0, warnings: 1 },
  contacts: { converted: 4, total: 0, skipped: 0, warnings: 0 },
};

describe("perTypeRows", () => {
  it("emits a row per non-zero type", () => {
    const rows = perTypeRows(base);
    expect(rows.map((r) => r.label)).toEqual(["Mail", "Calendar", "Tasks", "Contacts"]);
  });
  it("mail row shows MAIL-ONLY warnings, not the grand total", () => {
    expect(mailWarnings(base)).toBe(1); // 40 - 38 - 1 - 0
    const rows = perTypeRows(base);
    expect(rows.find((r) => r.label === "Mail")!.warnings).toBe(1);
    expect(rows.find((r) => r.label === "Calendar")!.warnings).toBe(38);
  });
  it("subtracts per-type skips from the grand total too", () => {
    // total warnings list includes per-type skips (RecordAppointmentSkipped -> AddWarning)
    const s = { ...base, warnings: 42, tasks: { converted: 7, total: 0, skipped: 2, warnings: 1 } };
    expect(mailWarnings(s)).toBe(1); // 42 - 38 - 1 - 0(appt skip) - 2(task skip) - 0
  });
  it("clamps to zero on an inconsistent event", () => {
    expect(mailWarnings({ ...base, warnings: 1 })).toBe(0); // 1 - 39 -> clamped
  });
  it("omits zero-total types", () => {
    const rows = perTypeRows({ ...base, appointments: { converted: 0, total: 0, skipped: 0, warnings: 0 } });
    expect(rows.map((r) => r.label)).toEqual(["Mail", "Tasks", "Contacts"]);
  });
  it("totalItems sums all types", () => {
    expect(totalItems(base)).toBe(16709);
  });
  it("anySkipped reflects any type skipped>0", () => {
    expect(anySkipped(perTypeRows(base))).toBe(false);
    expect(anySkipped(perTypeRows({ ...base, tasks: { converted: 7, total: 0, skipped: 2, warnings: 0 } }))).toBe(true);
  });
  it("doneSubtitle: 'messages' for mail-only, 'items' for mixed", () => {
    const mailOnly = { ...initialConvertState, converted: 100 };
    expect(doneSubtitle(mailOnly, "")).toBe("100 messages converted.");
    // Locale-agnostic: build the expected count the same way the impl does, so the
    // thousands separator matches whatever locale the test host uses.
    expect(doneSubtitle(base, "29s")).toBe(`${(16709).toLocaleString()} items converted in 29s.`);
  });
});
