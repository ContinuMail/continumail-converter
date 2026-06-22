// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import type { Account, ProfileSourceRow } from "./types";

export interface AccountGroup {
  account: Account | null;
  key: string;
  rows: ProfileSourceRow[];
  folderCount: number;
  messageCount: number;
  estimatedBytes: number;            // sum of row.bytes (estimated PST size — matches Review)
  defaultPstName: string;
}

// Windows reserved device names (stem only), mirroring Core OutputNameValidator.
const RESERVED = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;

/** Match the engine's OutputNameValidator policy so a per-account PST name never fails validation. */
export function sanitizePstName(raw: string): string {
  let s = raw.replace(/[\\/:*?"<>|]/g, "");   // 1. invalid filename chars
  s = s.replace(/[\x00-\x1f]/g, "");           //    + control chars
  s = s.trim();                                // 2. whitespace
  s = s.replace(/^\.+|\.+$/g, "").trim();       // 3. leading/trailing periods
  if (s.length === 0) return "Account";         // 4. empty fallback
  const stem = s.split(".")[0];                 // 5. reserved device-name stem -> suffix
  if (RESERVED.test(stem)) s = `${s}-mail`;
  return s;
}

/** Single source of truth for "which account does this row belong to". Reused by grouping, Review
 * filtering, and config building so the key never drifts. */
export function accountKeyForRow(row: ProfileSourceRow): string {
  return row.accountId ?? row.targetFolderPath[0] ?? "(unknown)";
}

export function groupByAccount(rows: ProfileSourceRow[], accounts: Account[]): AccountGroup[] {
  const byId = new Map(accounts.map((a) => [a.id, a]));
  const order: string[] = [];
  const buckets = new Map<string, ProfileSourceRow[]>();
  for (const r of rows) {
    const key = accountKeyForRow(r);
    if (!buckets.has(key)) { buckets.set(key, []); order.push(key); }
    buckets.get(key)!.push(r);
  }
  return order.map((key) => {
    const groupRows = buckets.get(key)!;
    const account = byId.get(key) ?? null;
    const label = account?.email ?? account?.folderSegment ?? groupRows[0].targetFolderPath[0] ?? key;
    return {
      account, key, rows: groupRows,
      folderCount: groupRows.length,
      messageCount: groupRows.reduce((n, r) => n + (r.messages ?? 0), 0),
      estimatedBytes: groupRows.reduce((n, r) => n + (r.bytes ?? 0), 0),
      defaultPstName: sanitizePstName(label),
    };
  });
}
