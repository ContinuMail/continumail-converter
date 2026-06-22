// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { groupByAccount, sanitizePstName, dedupePstNames } from "./accounts";
import type { Account, ProfileSourceRow } from "./types";

export interface AccountPreviewFolder { displayName: string; messages: number; bytes: number }
export interface AccountPreviewEntry { key: string; pstName: string; folders: AccountPreviewFolder[] }

/** One preview entry per selected, non-empty account. `effRows` must already be
 * effectiveRows(...)-filtered so the preview matches what Start converts. Mirror
 * strips the account segment from each folder path (matching buildProfileConfigMulti);
 * flatten aggregates to one "Imported Mail" row per account. PST names are sanitized
 * and de-duplicated exactly like the builder via the shared dedupePstNames. */
export function buildAccountPreview(
  effRows: ProfileSourceRow[],
  accounts: Account[],
  selectedKeys: Set<string>,
  pstNames: Record<string, string>,
  folderMapping: "mirror" | "flatten",
): AccountPreviewEntry[] {
  const groups = groupByAccount(effRows, accounts).filter(
    (g) => selectedKeys.has(g.key) && g.rows.length > 0,
  );

  const entries: AccountPreviewEntry[] = groups.map((g) => {
    const pstName = sanitizePstName(pstNames[g.key] ?? g.defaultPstName);
    const folders: AccountPreviewFolder[] =
      folderMapping === "flatten"
        ? [{
            displayName: "Imported Mail",
            messages: g.rows.reduce((n, r) => n + (r.messages ?? 0), 0),
            bytes: g.rows.reduce((n, r) => n + (r.bytes ?? 0), 0),
          }]
        : g.rows.map((r) => {
            const stripped = r.targetFolderPath.slice(1);
            const displayName = stripped.length > 0 ? stripped.join(" / ") : r.displayName;
            return { displayName, messages: r.messages, bytes: r.bytes };
          });
    return { key: g.key, pstName, folders };
  });

  const deduped = dedupePstNames(entries.map((e) => e.pstName));
  entries.forEach((e, i) => { e.pstName = deduped[i]; });
  return entries;
}
