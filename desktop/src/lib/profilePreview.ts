// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { groupByAccount, sanitizePstName, dedupePstNames } from "./accounts";
import { routeAlsoConvert, SYNTHETIC_LOCAL_FOLDERS_KEY } from "./routeAlsoConvert";
import type { Account, ProfileSourceRow, DiscoveredCalendar, DiscoveredAddressBook } from "./types";
import type { OptionsState } from "./options";

export interface AccountPreviewFolder { displayName: string; messages: number; bytes: number }
export interface AccountPreviewPim { calendars: string[]; contacts: string[] }
export interface AccountPreviewEntry {
  key: string;
  pstName: string;
  folders: AccountPreviewFolder[];
  pim: AccountPreviewPim;
  isSynthetic?: boolean;
}
export interface AccountPreviewResult { entries: AccountPreviewEntry[]; warnings: string[] }

/** One preview entry per selected, non-empty account (+ a synthetic "Local Folders"
 * entry when routing needs one). `effRows` must already be effectiveRows(...)-filtered
 * so the preview matches what Start converts. Mirror strips the account segment from
 * each folder path (matching buildProfileConfigMulti); flatten aggregates to one
 * "Imported Mail" row per account. PST names are sanitized and de-duplicated exactly
 * like the builder via the shared dedupePstNames. When `calendars`/`addressBooks`/
 * `options` are supplied, PIM routing is driven through the SAME `routeAlsoConvert`
 * used by `buildProfileConfigMulti`, so the preview and the actual conversion always
 * agree on where each calendar/address book lands. */
export function buildAccountPreview(
  effRows: ProfileSourceRow[],
  accounts: Account[],
  selectedKeys: Set<string>,
  pstNames: Record<string, string>,
  folderMapping: "mirror" | "flatten",
  calendars: DiscoveredCalendar[] = [],
  addressBooks: DiscoveredAddressBook[] = [],
  options?: OptionsState,
): AccountPreviewResult {
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
    return { key: g.key, pstName, folders, pim: { calendars: [], contacts: [] } };
  });

  const deduped = dedupePstNames(entries.map((e) => e.pstName));
  entries.forEach((e, i) => { e.pstName = deduped[i]; });

  let warnings: string[] = [];
  if (options) {
    const isLF = (key: string) => accounts.find((a) => a.id === key)?.addressResolution === "local-folders";
    const routeGroups = groups.map((g) => ({ key: g.key, accountId: g.key, isLocalFolders: isLF(g.key) }));
    const routed = routeAlsoConvert(calendars, addressBooks, routeGroups, options);
    warnings = routed.warnings;

    // Friendly labels: map the routed config entries back to the ORIGINAL discovered items
    // (which carry displayName) by calId / path — the config shapes drop displayName.
    const calName = new Map(calendars.map((c) => [c.calId, c.displayName]));
    const bookName = new Map(addressBooks.map((b) => [b.path, b.displayName]));
    const calLabels = (pim: { calendars?: { calId: string }[] } | undefined) =>
      (pim?.calendars ?? []).map((c) => calName.get(c.calId) ?? c.calId);
    const bookLabels = (pim: { contacts?: { path: string }[] } | undefined) =>
      (pim?.contacts ?? []).map((c) => bookName.get(c.path) ?? (c.path.split(/[\\/]/).pop() ?? c.path));

    const byKey = new Map(entries.map((e) => [e.key, e]));
    for (const [key, pim] of routed.perGroup) {
      if (key === SYNTHETIC_LOCAL_FOLDERS_KEY) continue;
      const e = byKey.get(key);
      if (!e) continue;
      e.pim.calendars = calLabels(pim);
      e.pim.contacts = bookLabels(pim);
    }

    if (routed.needsLocalFoldersGroup) {
      const synthetic = routed.perGroup.get(SYNTHETIC_LOCAL_FOLDERS_KEY);
      const names = dedupePstNames([...entries.map((e) => e.pstName), sanitizePstName("Local Folders")]);
      entries.push({
        key: SYNTHETIC_LOCAL_FOLDERS_KEY,
        pstName: names[names.length - 1],
        folders: [],
        isSynthetic: true,
        pim: { calendars: calLabels(synthetic), contacts: bookLabels(synthetic) },
      });
    }
  }

  return { entries, warnings };
}
