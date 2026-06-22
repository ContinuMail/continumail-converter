// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useCallback, useMemo, useState } from "react";
import { scan as runScan, discoverProfile } from "./engine";
import { mergeProfileSources } from "./profileConfig";
import { checkSchemaVersion } from "./schema";
import { defaultOptions, type OptionsState, FLATTEN_SOURCE_ID } from "./options";
import { sortSources, type SortField, type SortDir } from "./review";
import { groupByAccount, accountKeyForRow } from "./accounts";
import type { FileStat, SourceRow, ProfileSourceRow, DiscoverWarning, DiscoverResult, OutputTarget, Account } from "./types";
import type { ScanResult } from "./parse";

export type FlowStage = "select" | "scanning" | "review" | "options" | "scanError" | "accounts";

// ── Pure helpers (exported for unit-testing) ─────────────────────────────────

/**
 * Given the rows + discovered accounts after a profile scan, compute:
 *   - which stage to navigate to ("accounts" if ≥2 accounts, else "review")
 *   - initial `selectedAccountKeys` (all keys when multi-account, empty otherwise)
 *   - initial `pstNames` (keyed by account key, seeded from groupByAccount defaultPstName)
 */
export function computeAccountRouting(
  rows: ProfileSourceRow[],
  accounts: Account[],
): { stage: "accounts" | "review"; selectedAccountKeys: Set<string>; pstNames: Record<string, string> } {
  if (accounts.length >= 2) {
    const groups = groupByAccount(rows, accounts);
    const selectedAccountKeys = new Set(groups.map((g) => g.key));
    const pstNames: Record<string, string> = {};
    for (const g of groups) pstNames[g.key] = g.defaultPstName;
    return { stage: "accounts", selectedAccountKeys, pstNames };
  }
  return { stage: "review", selectedAccountKeys: new Set(), pstNames: {} };
}

/**
 * Filter profile rows to those belonging to selected accounts.
 * Only active in multi-account profile mode (inputMode === "profile" AND accounts.length >= 2).
 * Files mode and single-account mode pass through all rows unchanged.
 */
export function filterSourcesBySelection(
  rows: ProfileSourceRow[],
  selectedAccountKeys: Set<string>,
  inputMode: "files" | "profile",
  accounts: Account[],
): ProfileSourceRow[] {
  if (inputMode !== "profile" || accounts.length < 2) return rows;
  return rows.filter((r) => selectedAccountKeys.has(accountKeyForRow(r)));
}

// ─────────────────────────────────────────────────────────────────────────────

export interface PreConvertState {
  inputFiles: FileStat[];
  outputTarget: OutputTarget | null;
  stage: FlowStage;
  scan: ScanResult | null;
  errorMessage: string | null;
  checkedIds: Set<string>;
  skipEmpty: boolean;
  options: OptionsState;
  scanProgress: { bytes: number; totalBytes: number } | null;
  scanFileCount: number; // sources being scanned: files (.mbox mode) or discovered folders (profile mode); 0 until known
  sortBy: SortField;
  sortDir: SortDir;
  inputMode: "files" | "profile";
  profileRoot: string | null;
  profileRows: ProfileSourceRow[];
  discoverWarnings: DiscoverWarning[];
  sourceError: string | null; // parent-owned discover-time error, shown on the Source screen
  // Account selection (multi-account profile mode)
  accounts: Account[];
  selectedAccountKeys: Set<string>;
  pstNames: Record<string, string>; // per-account edited PST name, keyed by account key
}

function initialState(): PreConvertState {
  return {
    inputFiles: [],
    outputTarget: null,
    stage: "select",
    scan: null,
    errorMessage: null,
    checkedIds: new Set(),
    skipEmpty: true,
    options: defaultOptions(),
    scanProgress: null,
    scanFileCount: 0,
    sortBy: "default",
    sortDir: "desc",
    inputMode: "files",
    profileRoot: null,
    profileRows: [],
    discoverWarnings: [],
    sourceError: null,
    accounts: [],
    selectedAccountKeys: new Set(),
    pstNames: {},
  };
}

export function useScan() {
  const [state, setState] = useState<PreConvertState>(() => initialState());

  const setInputFiles = useCallback(
    (inputFiles: FileStat[]) => setState((s) => ({ ...s, inputFiles })),
    [],
  );
  const setOutputTarget = useCallback(
    (outputTarget: OutputTarget | null) => setState((s) => ({ ...s, outputTarget })),
    [],
  );

  const setInputMode = useCallback(
    (inputMode: "files" | "profile") => setState((s) => ({ ...s, inputMode, sourceError: null })),
    [],
  );
  const setProfileRoot = useCallback(
    (profileRoot: string | null) => setState((s) => ({ ...s, profileRoot, sourceError: null })),
    [],
  );

  const continueToScan = useCallback(async () => {
    if (state.inputMode === "profile") {
      if (!state.profileRoot) {
        setState((s) => ({ ...s, sourceError: "Choose a Thunderbird profile or mail folder." }));
        return;
      }
      setState((s) => ({ ...s, stage: "scanning", sourceError: null, errorMessage: null, scanProgress: null, scanFileCount: 0 }));

      // Discovery failure / empty discovery is a SOURCE-selection problem → return to Source with
      // sourceError. Only a scan failure AFTER successful discovery goes to the ScanError view.
      let disc: DiscoverResult;
      try {
        disc = await discoverProfile(state.profileRoot);
      } catch (e) {
        const sourceError = e instanceof Error ? e.message : String(e);
        setState((s) => ({ ...s, stage: "select", sourceError, scanProgress: null }));
        return;
      }
      if (disc.sources.length === 0) {
        setState((s) => ({ ...s, stage: "select", sourceError: "No mail folders found in that location.", scanProgress: null }));
        return;
      }

      // Discovery done: the source count is now known — surface it on the Scanning view.
      setState((s) => (s.stage === "scanning" ? { ...s, scanFileCount: disc.sources.length } : s));

      try {
        const paths = disc.sources.map((d) => d.path);
        const result = await runScan(paths, (p) =>
          setState((s) => (s.stage === "scanning" ? { ...s, scanProgress: p } : s)),
        );
        const schemaMsg = checkSchemaVersion(result.schemaVersion);
        if (schemaMsg) console.warn(`[mail2pst] ${schemaMsg}`);
        const profileRows = mergeProfileSources(disc.sources, result);
        const { stage: nextStage, selectedAccountKeys, pstNames } = computeAccountRouting(profileRows, disc.accounts);
        setState((s) => ({
          ...s,
          stage: nextStage,
          scan: result,
          profileRows,
          discoverWarnings: disc.warnings,
          checkedIds: new Set(profileRows.map((r) => r.id)),
          skipEmpty: true,
          options: defaultOptions(),
          scanProgress: null,
          sortBy: "default",
          sortDir: "desc",
          accounts: disc.accounts,
          selectedAccountKeys,
          pstNames,
        }));
      } catch (e) {
        const errorMessage = e instanceof Error ? e.message : String(e);
        setState((s) => ({ ...s, stage: "scanError", errorMessage, scanProgress: null }));
      }
      return;
    }

    // --- file mode (unchanged) ---
    const paths = state.inputFiles.map((f) => f.path);
    if (paths.length === 0) {
      setState((s) => ({ ...s, errorMessage: "Select at least one .mbox file." }));
      return;
    }
    setState((s) => ({ ...s, stage: "scanning", sourceError: null, errorMessage: null, scanProgress: null, scanFileCount: paths.length }));
    try {
      // Guard the progress update on stage so a late/queued event after the scan
      // resolves can't leak a bar into Review or ScanError.
      const result = await runScan(paths, (p) =>
        setState((s) => (s.stage === "scanning" ? { ...s, scanProgress: p } : s)),
      );
      const schemaMsg = checkSchemaVersion(result.schemaVersion);
      if (schemaMsg) console.warn(`[mail2pst] ${schemaMsg}`);
      // Fresh selection + default options each scan.
      setState((s) => ({
        ...s,
        stage: "review",
        scan: result,
        profileRows: [],
        discoverWarnings: [],
        checkedIds: new Set(result.sources.map((x) => x.id)),
        skipEmpty: true,
        options: defaultOptions(),
        scanProgress: null,
        sortBy: "default",
        sortDir: "desc",
      }));
    } catch (e) {
      const errorMessage = e instanceof Error ? e.message : String(e);
      setState((s) => ({ ...s, stage: "scanError", errorMessage, scanProgress: null }));
    }
  }, [state.inputMode, state.profileRoot, state.inputFiles]);

  const toggleChecked = useCallback((id: string) => {
    setState((s) => {
      const next = new Set(s.checkedIds);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return { ...s, checkedIds: next };
    });
  }, []);

  const setSkipEmpty = useCallback(
    (skipEmpty: boolean) => setState((s) => ({ ...s, skipEmpty })),
    [],
  );

  const setSort = useCallback(
    (sortBy: SortField, sortDir: SortDir) => setState((s) => ({ ...s, sortBy, sortDir })),
    [],
  );

  const setOptions = useCallback(
    (patch: Partial<OptionsState>) => setState((s) => ({ ...s, options: { ...s.options, ...patch } })),
    [],
  );

  // Routes the flatten pseudo-folder rename to flattenFolderName; everything
  // else updates the per-source renames map.
  const setRename = useCallback((sourceId: string, name: string) => {
    setState((s) => {
      if (sourceId === FLATTEN_SOURCE_ID) {
        return { ...s, options: { ...s.options, flattenFolderName: name } };
      }
      return { ...s, options: { ...s.options, renames: { ...s.options.renames, [sourceId]: name } } };
    });
  }, []);

  const continueToOptions = useCallback(() => setState((s) => ({ ...s, stage: "options" })), []);
  const backToReview = useCallback(() => setState((s) => ({ ...s, stage: "review" })), []);
  const continueToReviewFromAccounts = useCallback(() => setState((s) => ({ ...s, stage: "review" })), []);
  const backToAccounts = useCallback(() => setState((s) => ({ ...s, stage: "accounts" })), []);

  const toggleAccount = useCallback((key: string) => {
    setState((s) => {
      const next = new Set(s.selectedAccountKeys);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return { ...s, selectedAccountKeys: next };
    });
  }, []);

  const setPstName = useCallback((key: string, name: string) => {
    setState((s) => ({ ...s, pstNames: { ...s.pstNames, [key]: name } }));
  }, []);

  const back = useCallback(
    () => setState((s) => ({ ...s, stage: "select", scan: null, profileRows: [], discoverWarnings: [], errorMessage: null, sourceError: null, scanProgress: null })),
    [],
  );
  const resetFlow = useCallback(() => setState(initialState()), []);

  const sortedSources: SourceRow[] = useMemo(() => {
    if (state.inputMode === "profile") {
      const filtered = filterSourcesBySelection(state.profileRows, state.selectedAccountKeys, state.inputMode, state.accounts);
      return sortSources(filtered, state.sortBy, state.sortDir);
    }
    return state.scan ? sortSources(state.scan.sources, state.sortBy, state.sortDir) : [];
  }, [state.inputMode, state.profileRows, state.selectedAccountKeys, state.accounts, state.scan, state.sortBy, state.sortDir]);

  const pairedIds: Set<string> = useMemo(
    () => new Set(state.profileRows.filter((r) => r.msfPath).map((r) => r.id)),
    [state.profileRows],
  );

  const discoveredAccountCount = state.accounts.length;

  return {
    state,
    setInputFiles,
    setOutputTarget,
    setInputMode,
    setProfileRoot,
    continueToScan,
    toggleChecked,
    setSkipEmpty,
    setSort,
    sortedSources,
    pairedIds,
    setOptions,
    setRename,
    continueToOptions,
    backToReview,
    backToAccounts,
    continueToReviewFromAccounts,
    toggleAccount,
    setPstName,
    discoveredAccountCount,
    back,
    resetFlow,
  };
}
