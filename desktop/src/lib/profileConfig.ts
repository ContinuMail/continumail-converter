// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type {
  DiscoveredSource, ProfileSourceRow, ConversionConfig, SourceConfigEntry, OutputTarget, OutputGroupConfig,
  DiscoveredCalendar, DiscoveredAddressBook, CalendarSourceConfigTS, ContactSourceConfigTS, Account,
} from "./types";
import type { ScanResult } from "./parse";
import type { OptionsState } from "./options";
import { effectiveRows } from "./review";
import { deriveOutputTarget, ConvertConfigError } from "./convert";
import { sanitizePstName, dedupePstNames } from "./accounts";
import { alsoConvertInfo } from "./alsoConvert";
import { routeAlsoConvert, SYNTHETIC_LOCAL_FOLDERS_KEY } from "./routeAlsoConvert";

/** Build the calendars[]/contacts[] arrays for the PRIMARY output group from the
 * discovered data + toggles. Applies the SAME disable logic the UI uses, so a
 * zero-count type (which the UI shows disabled/unchecked) never emits config even
 * though options.* defaults ON. Both include flags are always set explicitly; an
 * array is omitted entirely (undefined) when its type is effectively off OR the
 * discovered list is empty. */
export function buildAlsoConvert(
  calendars: DiscoveredCalendar[],
  addressBooks: DiscoveredAddressBook[],
  options: OptionsState,
): { calendars?: CalendarSourceConfigTS[]; contacts?: ContactSourceConfigTS[] } {
  const info = alsoConvertInfo(calendars, addressBooks);
  // "Effective" = the user's toggle AND the type is not UI-disabled (zero count).
  const effAppointments = options.includeAppointments && !info.appointments.disabled;
  const effTasks = options.includeTasks && !info.tasks.disabled;
  const effContacts = options.includeContacts && !info.contacts.disabled;

  const out: { calendars?: CalendarSourceConfigTS[]; contacts?: ContactSourceConfigTS[] } = {};
  if ((effAppointments || effTasks) && calendars.length > 0) {
    out.calendars = calendars.map((c) => {
      const entry: CalendarSourceConfigTS = {
        storePath: c.storePath, calId: c.calId,
        includeAppointments: effAppointments,
        includeTasks: effTasks,
      };
      if (c.defaultCalendarFolderPath.length > 0) entry.appointmentFolderPath = c.defaultCalendarFolderPath;
      if (c.defaultTaskFolderPath.length > 0) entry.taskFolderPath = c.defaultTaskFolderPath;
      return entry;
    });
  }
  if (effContacts) {
    // Skip books we KNOW are empty; keep unknown-count books (they may hold contacts).
    const booksToInclude = addressBooks.filter((b) => b.contactCount == null || b.contactCount > 0);
    if (booksToInclude.length > 0) {
      out.contacts = booksToInclude.map((b) => ({ path: b.path, format: b.format }));
    }
  }
  return out;
}

/** Merge discovery sources with scan counts. Identity = discovered `path`; the
 * scan row's id is ignored (it is scan-local). displayName = joined nested path. */
export function mergeProfileSources(
  discovered: DiscoveredSource[], scan: ScanResult,
): ProfileSourceRow[] {
  const byPath = new Map(scan.sources.map((s) => [s.path, s]));
  return discovered.map((d) => {
    const s = byPath.get(d.path);
    return {
      id: d.path,
      path: d.path,
      displayName: d.targetFolderPath.join(" / "),
      messages: s?.messages ?? 0,
      bytes: s?.bytes ?? 0,
      sourceBytes: s?.sourceBytes ?? d.sourceBytes,
      dateFrom: s?.dateFrom ?? null,
      dateTo: s?.dateTo ?? null,
      warnings: s?.warnings ?? 0,
      skipped: s?.skipped ?? 0,
      targetFolderPath: d.targetFolderPath,
      msfPath: d.msfPath,
      accountId: d.accountId,
    };
  });
}

/** Build a full profile-mode ConversionConfig. Mirror emits each source's
 * targetFolderPath + msfPath verbatim (NO leaf-name dedupe — same leaf under
 * different parents is valid). Flatten omits targetFolderPath and sets
 * folderMapping:"flatten" so the engine routes to "Imported Mail"; msfPath and
 * top-level profilePath are kept in both modes so enrichment + tag names apply.
 * junkHandling + dropExpunged are always written at the top-level config. */
export function buildProfileConfig(
  rows: ProfileSourceRow[],
  checkedIds: Set<string>,
  skipEmpty: boolean,
  options: OptionsState,
  outputPstPath: string,
  profileRoot: string,
  calendars: DiscoveredCalendar[] = [],
  addressBooks: DiscoveredAddressBook[] = [],
): { config: ConversionConfig; outputDir: string; pstName: string } {
  const { outputDir, pstName } = deriveOutputTarget(outputPstPath);
  const folderMapping = options.folderMapping;
  const eff = effectiveRows(rows, checkedIds, skipEmpty) as ProfileSourceRow[];
  if (eff.length === 0) {
    throw new ConvertConfigError("Select at least one folder to convert.");
  }

  const sources: SourceConfigEntry[] = eff.map((r) => {
    const entry: SourceConfigEntry = { path: r.path, type: "mbox" };
    if (folderMapping === "mirror") entry.targetFolderPath = r.targetFolderPath;
    if (r.msfPath != null) entry.msfPath = r.msfPath;
    return entry;
  });

  return {
    outputDir,
    pstName,
    config: {
      profilePath: profileRoot,
      junkHandling: options.junkHandling,
      dropExpunged: options.dropExpunged,
      outputs: [{
        name: pstName,
        maxSizeMB: options.maxSizeMB,
        folderMapping,
        includeEmptyFolders: !skipEmpty,
        sources,
        ...buildAlsoConvert(calendars, addressBooks, options),
      }],
    },
  };
}

export interface MultiAccountGroup {
  key: string;
  pstName: string;
  rows: ProfileSourceRow[];
}

/** Build a full profile-mode ConversionConfig with one OutputGroup per kept
 * account. Mirror strips the first path segment (the account prefix) from each
 * source's targetFolderPath and throws if stripping leaves an empty path.
 * Flatten omits targetFolderPath entirely. Groups with no effective rows (after
 * checkedIds + skipEmpty filtering) are silently dropped; a ConvertConfigError
 * is thrown when none survive. PST names are de-duplicated by appending -2, -3,
 * … in order, skipping suffixes already taken by other groups. */
export function buildProfileConfigMulti({
  groups,
  checkedIds,
  skipEmpty,
  options,
  target,
  profileRoot,
  calendars,
  addressBooks,
  accounts,
}: {
  groups: MultiAccountGroup[];
  checkedIds: Set<string>;
  skipEmpty: boolean;
  options: OptionsState;
  target: OutputTarget;
  profileRoot: string;
  calendars?: DiscoveredCalendar[];
  addressBooks?: DiscoveredAddressBook[];
  accounts?: Account[];
}): { config: ConversionConfig; outputDir: string } {
  const folderMapping = options.folderMapping;
  const cals = calendars ?? [];
  const books = addressBooks ?? [];
  const accts = accounts ?? [];
  const outputDir = target.kind === "folder" ? target.dir : deriveOutputTarget(target.path).outputDir;

  // Build surviving output groups
  const outputGroups: OutputGroupConfig[] = [];
  const keptKeys: string[] = [];
  for (const group of groups) {
    const eff = effectiveRows(group.rows, checkedIds, skipEmpty) as ProfileSourceRow[];
    if (eff.length === 0) continue; // drop group with no effective sources

    const sources: SourceConfigEntry[] = eff.map((r) => {
      const entry: SourceConfigEntry = { path: r.path, type: "mbox" };
      if (folderMapping === "mirror") {
        const stripped = r.targetFolderPath.slice(1);
        if (stripped.length === 0) {
          throw new ConvertConfigError(
            `Discovered source ${r.path} has no folder below its account.`,
          );
        }
        entry.targetFolderPath = stripped;
      }
      if (r.msfPath != null) entry.msfPath = r.msfPath;
      return entry;
    });

    outputGroups.push({
      name: sanitizePstName(group.pstName), // placeholder — de-duped below
      maxSizeMB: options.maxSizeMB,
      folderMapping,
      includeEmptyFolders: !skipEmpty,
      sources,
    });
    keptKeys.push(group.key);
  }

  if (outputGroups.length === 0) {
    throw new ConvertConfigError("Select at least one folder to convert.");
  }

  // De-duplicate names (shared helper — keeps parity with the Options preview).
  const dedup = dedupePstNames(outputGroups.map((g) => g.name));
  outputGroups.forEach((g, i) => { g.name = dedup[i]; });

  // Route each calendar/address book into its account's group (split mode).
  const isLocalFolders = (key: string): boolean =>
    accts.find((a) => a.id === key)?.addressResolution === "local-folders";
  const routeGroups = keptKeys.map((key) => ({ key, accountId: key, isLocalFolders: isLocalFolders(key) }));
  const routed = routeAlsoConvert(cals, books, routeGroups, options);

  keptKeys.forEach((key, i) => {
    const pim = routed.perGroup.get(key);
    if (pim?.calendars) outputGroups[i].calendars = pim.calendars;
    if (pim?.contacts) outputGroups[i].contacts = pim.contacts;
  });

  if (routed.needsLocalFoldersGroup) {
    const synthetic = routed.perGroup.get(SYNTHETIC_LOCAL_FOLDERS_KEY);
    const names = dedupePstNames([...outputGroups.map((g) => g.name), sanitizePstName("Local Folders")]);
    outputGroups.push({
      name: names[names.length - 1],
      maxSizeMB: options.maxSizeMB,
      folderMapping,
      includeEmptyFolders: !skipEmpty,
      sources: [],
      ...(synthetic?.calendars ? { calendars: synthetic.calendars } : {}),
      ...(synthetic?.contacts ? { contacts: synthetic.contacts } : {}),
    });
  }

  return {
    outputDir,
    config: {
      profilePath: profileRoot,
      junkHandling: options.junkHandling,
      dropExpunged: options.dropExpunged,
      outputs: outputGroups,
    },
  };
}
