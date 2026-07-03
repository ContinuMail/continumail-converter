// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import type { ConvertState } from "./useConvert";

export interface PerTypeRow { label: string; converted: number; skipped: number; warnings: number; }

/** Mail-only warning count. The engine's top-level `warnings` is a GRAND TOTAL —
 * its warning list also receives every per-type warning AND every per-type skip
 * (see ConversionReport.AddWarning). So mail-only warnings = total minus the
 * per-type warning and skip counts, clamped >= 0. `converted` and `skipped` are
 * already mail-only on the wire, so only warnings needs this correction. */
export function mailWarnings(s: ConvertState): number {
  const perType =
    s.appointments.warnings + s.tasks.warnings + s.contacts.warnings +
    s.appointments.skipped + s.tasks.skipped + s.contacts.skipped;
  return Math.max(0, s.warnings - perType);
}

/** One row per data type that produced anything (non-zero converted/skipped/warnings).
 * Mail uses top-level converted/skipped but the DERIVED mail-only warning count; the
 * other three use their own per-type slices. */
export function perTypeRows(s: ConvertState): PerTypeRow[] {
  const all: PerTypeRow[] = [
    { label: "Mail", converted: s.converted, skipped: s.skipped, warnings: mailWarnings(s) },
    { label: "Calendar", converted: s.appointments.converted, skipped: s.appointments.skipped, warnings: s.appointments.warnings },
    { label: "Tasks", converted: s.tasks.converted, skipped: s.tasks.skipped, warnings: s.tasks.warnings },
    { label: "Contacts", converted: s.contacts.converted, skipped: s.contacts.skipped, warnings: s.contacts.warnings },
  ];
  return all.filter((r) => r.converted > 0 || r.skipped > 0 || r.warnings > 0);
}

export function anySkipped(rows: PerTypeRow[]): boolean {
  return rows.some((r) => r.skipped > 0);
}

export function totalItems(s: ConvertState): number {
  return s.converted + s.appointments.converted + s.tasks.converted + s.contacts.converted;
}

/** DoneView subtitle. A run with more than the Mail row reads "N items converted";
 * a mail-only run keeps the historical "N messages converted" wording. `elapsed` is
 * the already-formatted duration (e.g. "29s") or "" for none. */
export function doneSubtitle(s: ConvertState, elapsed: string): string {
  const mixed = perTypeRows(s).length > 1;
  const n = mixed ? totalItems(s) : s.converted;
  const noun = mixed ? "items" : "messages";
  return `${n.toLocaleString()} ${noun} converted${elapsed ? ` in ${elapsed}` : ""}.`;
}
