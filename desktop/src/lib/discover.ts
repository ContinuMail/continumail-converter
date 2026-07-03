// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { extractJsonObjects, isRecord, EngineParseError } from "./parse";
import type {
  DiscoverResult, DiscoveredSource, DiscoverWarning, DiscoverSkipped, DiscoverPairing,
  Account, AddressResolution, DiscoveredCalendar, DiscoveredAddressBook,
} from "./types";

/** Parse the engine's single pretty-printed `discovery` object (tolerates dev-build
 * noise around it). Throws EngineParseError when no discovery object is present. */
export function parseDiscover(stdout: string): DiscoverResult {
  const obj = extractJsonObjects(stdout).find(
    (o) => isRecord(o) && o.type === "discovery",
  ) as Record<string, unknown> | undefined;
  if (!obj) throw new EngineParseError("No discovery object in engine output");

  const VALID_RES = new Set<string>(["identity", "server", "local-folders", "not-found"]);

  const sources: DiscoveredSource[] = (Array.isArray(obj.sources) ? obj.sources : []).map((s) => ({
    ...(s as DiscoveredSource),
    accountId: (s as Record<string, unknown>).accountId != null
      ? String((s as Record<string, unknown>).accountId)
      : null,
  }));
  const warnings = (Array.isArray(obj.warnings) ? obj.warnings : []).map(
    (w) => w as DiscoverWarning,
  );
  const skipped = (Array.isArray(obj.skipped) ? obj.skipped : []).map(
    (s) => s as DiscoverSkipped,
  );
  const p = isRecord(obj.pairing) ? obj.pairing : {};
  const pairing: DiscoverPairing = {
    pairedMsfCount: Number(p.pairedMsfCount ?? 0),
    unpairedMboxCount: Number(p.unpairedMboxCount ?? 0),
    orphanMsfCount: Number(p.orphanMsfCount ?? 0),
  };

  const accounts: Account[] = (Array.isArray(obj.accounts) ? obj.accounts : []).map((a) => {
    const ar = a as Record<string, unknown>;
    const addressResolution: AddressResolution =
      VALID_RES.has(String(ar.addressResolution)) ? (ar.addressResolution as AddressResolution) : "not-found";
    return {
      id: String(ar.id ?? ""),
      folderSegment: String(ar.folderSegment ?? ""),
      accountPath: String(ar.accountPath ?? ""),
      store: ar.store != null ? String(ar.store) : null,
      email: ar.email != null ? String(ar.email) : null,
      host: ar.host != null ? String(ar.host) : null,
      addressResolution,
    };
  });

  const calendars: DiscoveredCalendar[] = (Array.isArray(obj.calendars) ? obj.calendars : []).map((c) => {
    const cc = c as Record<string, unknown>;
    return {
      calId: String(cc.calId ?? ""),
      displayName: String(cc.displayName ?? ""),
      storeKind: String(cc.storeKind ?? ""),
      storePath: String(cc.storePath ?? ""),
      calendarType: String(cc.calendarType ?? ""),
      isVisibleInThunderbird: Boolean(cc.isVisibleInThunderbird),
      eventCount: Number(cc.eventCount ?? 0),
      taskCount: Number(cc.taskCount ?? 0),
      defaultCalendarFolderPath: Array.isArray(cc.defaultCalendarFolderPath) ? cc.defaultCalendarFolderPath.map(String) : [],
      defaultTaskFolderPath: Array.isArray(cc.defaultTaskFolderPath) ? cc.defaultTaskFolderPath.map(String) : [],
    };
  });
  const addressBooks: DiscoveredAddressBook[] = (Array.isArray(obj.addressBooks) ? obj.addressBooks : []).map((b) => {
    const bb = b as Record<string, unknown>;
    const rawCount = bb.contactCount == null ? null : Number(bb.contactCount);
    return {
      displayName: String(bb.displayName ?? ""),
      path: String(bb.path ?? ""),
      format: String(bb.format ?? ""),
      // Guard: a malformed count must become unknown (null), never NaN, or it would
      // poison alsoConvertInfo's sums.
      contactCount: rawCount == null || Number.isNaN(rawCount) ? null : rawCount,
    };
  });

  return {
    root: String(obj.root ?? ""),
    layout: String(obj.layout ?? ""),
    sources,
    warnings,
    skipped,
    pairing,
    accounts,
    calendars,
    addressBooks,
    schemaVersion: obj.schemaVersion as number | undefined,
  };
}
