// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useState } from "react";
import { FileText, FolderOpen, Save, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { pickMboxFiles, pickFolder, statFiles, pickOutputPst, listThunderbirdProfiles } from "@/lib/engine";
import { visibleProfiles, hiddenNote, profileAccountLabels, profileSubtext, pickDefaultProfile } from "@/lib/profiles";
import { splitPath } from "@/lib/convert";
import { formatBytes } from "@/lib/format";
import { canScan } from "@/lib/outputTarget";
import { fileTabClickMode, isFilesTabMode, isProfileTabMode } from "@/lib/inputMode";
import type { InputMode } from "@/lib/inputMode";
import type { DiscoverResult, FileStat, ProfileEntry, OutputTarget } from "@/lib/types";

interface SelectViewProps {
  files: FileStat[];
  outputTarget: OutputTarget | null;
  inputMode: InputMode;
  profileRoot: string | null;
  folderTreeDiscovery: DiscoverResult | null;
  folderTreeDiscovering: boolean;
  sourceError?: string | null;
  onFilesChange: (files: FileStat[]) => void;
  onOutputTargetChange: (target: OutputTarget | null) => void;
  onInputModeChange: (m: InputMode) => void;
  onProfileRootChange: (path: string | null) => void;
  onFolderTreeRootChange: (path: string) => void;
  onAutomaticProfileRootChange: (path: string) => void;
  onContinue: () => void;
}

export function SelectView({
  files, outputTarget, inputMode, profileRoot, folderTreeDiscovery, folderTreeDiscovering, sourceError,
  onFilesChange, onOutputTargetChange, onInputModeChange, onProfileRootChange,
  onFolderTreeRootChange, onAutomaticProfileRootChange, onContinue,
}: SelectViewProps) {
  // Picker errors are transient and screen-local. Discovery errors are owned by
  // the parent flow and returned through sourceError.
  const [pickerError, setPickerError] = useState<string | null>(null);

  const [scan, setScan] = useState<
    | { k: "idle" }
    | { k: "scanning" }
    | { k: "done"; entries: ProfileEntry[] }
    | { k: "error" }
  >({ k: "idle" });

  async function runScan() {
    setScan({ k: "scanning" });
    try {
      const entries = await listThunderbirdProfiles();
      setScan({ k: "done", entries });
      const pick = pickDefaultProfile(entries, profileRoot);
      if (pick) onAutomaticProfileRootChange(pick);
    } catch {
      setScan({ k: "error" });
    }
  }

  async function addFiles(paths: string[]) {
    if (paths.length === 0) return;
    const stats = await statFiles(paths);
    const seen = new Set(files.map((f) => f.path));
    const merged = [...files, ...stats.filter((f) => !seen.has(f.path))];
    merged.sort((a, b) => a.path.toLowerCase().localeCompare(b.path.toLowerCase()));
    onFilesChange(merged);
  }

  async function onChooseFiles() {
    setPickerError(null);
    await addFiles(await pickMboxFiles());
  }

  async function onChooseFolder() {
    const dir = await pickFolder();
    if (!dir) return;
    setPickerError(null);
    onFolderTreeRootChange(dir);
  }

  async function onChooseProfile() {
    setPickerError(null);
    const dir = await pickFolder();
    if (dir) onProfileRootChange(dir);
  }

  async function onChooseOutput() {
    setPickerError(null);
    const path = await pickOutputPst();
    if (path) onOutputTargetChange({ kind: "pstFile", path });
  }

  function removeFile(path: string) {
    setPickerError(null);
    onFilesChange(files.filter((f) => f.path !== path));
  }

  function clearOutput() {
    setPickerError(null);
    onOutputTargetChange(null);
  }

  function onFilesTabClick() {
    const nextMode = fileTabClickMode(inputMode);
    if (nextMode !== inputMode) onInputModeChange(nextMode);
  }

  const totalBytes = files.reduce((sum, f) => sum + f.size, 0);
  const outputPath = outputTarget?.kind === "pstFile" ? outputTarget.path : null;
  const canContinue = canScan(inputMode, files.map((f) => f.path), profileRoot, outputTarget, folderTreeDiscovery, folderTreeDiscovering);

  // For the "manual path shown when not a scanned profile" check.
  const scannedEntries = scan.k === "done" ? scan.entries : [];

  return (
    <div className="flex flex-1 flex-col">
      <h1 className="text-xl font-semibold text-foreground">Choose what to convert</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Select a Thunderbird folder/profile or pick individual <code>.mbox</code> files.
      </p>

      <div className="mt-4 inline-flex overflow-hidden rounded-md border border-border">
        <button
          type="button"
          aria-pressed={isProfileTabMode(inputMode)}
          onClick={() => onInputModeChange("profile")}
          className={"px-4 py-1.5 text-sm " + (isProfileTabMode(inputMode) ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted")}
        >
          Thunderbird folder / profile
        </button>
        <button
          type="button"
          aria-pressed={isFilesTabMode(inputMode)}
          onClick={onFilesTabClick}
          className={"px-4 py-1.5 text-sm " + (isFilesTabMode(inputMode) ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted")}
        >
          .mbox files
        </button>
      </div>

      {isProfileTabMode(inputMode) && (
        <div className="mt-4">
          {scan.k === "scanning" && (
            <div className="mb-3 flex items-center gap-2 text-sm text-muted-foreground">
              <Spinner size="sm" />
              Scanning for Thunderbird profiles…
            </div>
          )}

          {scan.k === "error" && (
            <p className="mb-3 text-sm text-destructive">
              Could not scan Thunderbird profiles — use Browse manually…
            </p>
          )}

          {scan.k === "done" && (() => {
            const vis = visibleProfiles(scan.entries);
            const note = hiddenNote(scan.entries);
            if (vis.length === 0) {
              return (
                <p className="mb-3 text-sm text-muted-foreground">
                  No Thunderbird profiles with mail found — use Browse manually… to point at a profile or folder.
                </p>
              );
            }
            return (
              <div className="mb-3">
                <div className="flex flex-col gap-1.5">
                  {vis.map((e) => (
                    <label
                      key={e.path}
                      className={
                        "flex cursor-pointer items-start gap-3 rounded-lg border px-3 py-2 text-sm transition-colors " +
                        (profileRoot === e.path
                          ? "border-primary bg-primary/10 text-foreground"
                          : "border-border bg-card text-foreground hover:bg-muted")
                      }
                    >
                      <input
                        type="radio"
                        name="profile"
                        value={e.path}
                        checked={profileRoot === e.path}
                        onChange={() => { setPickerError(null); onProfileRootChange(e.path); }}
                        className="mt-0.5 accent-primary shrink-0"
                      />
                      <div className="min-w-0 flex-1">
                        <div className="flex items-start justify-between gap-2">
                          {/* One box per account found in the profile (each its own
                              email) — the radio still selects the whole profile. */}
                          <div className="flex min-w-0 flex-col gap-1">
                            {profileAccountLabels(e).map((label) => (
                              <span
                                key={label}
                                className="w-fit max-w-full truncate rounded-md border border-border bg-background px-2 py-0.5 text-sm font-medium"
                              >
                                {label}
                              </span>
                            ))}
                          </div>
                          {e.isDefault && (
                            <span className="shrink-0 rounded bg-primary/15 px-1.5 py-0.5 text-xs text-primary">default</span>
                          )}
                        </div>
                        <div className="mt-1.5 truncate text-xs text-light-gray">{profileSubtext(e)}</div>
                      </div>
                    </label>
                  ))}
                </div>
                {note && (
                  <p className="mt-1 text-xs italic text-light-gray">{note}</p>
                )}
              </div>
            );
          })()}

          <div className="flex gap-3">
            <Button variant="outline" onClick={onChooseProfile}>
              <FolderOpen /> Browse manually…
            </Button>
            <Button
              variant="outline"
              onClick={runScan}
              disabled={scan.k === "scanning"}
            >
              <Search /> {scan.k === "idle" ? "Scan for Thunderbird profiles" : scan.k === "scanning" ? "Scanning…" : "Rescan"}
            </Button>
          </div>

          {profileRoot && scannedEntries.every((e) => e.path !== profileRoot) && (
            <div className="mt-1 text-xs text-light-gray">{profileRoot}</div>
          )}
          <p className="mt-2 text-xs text-light-gray">
            Point at a Thunderbird profile, Mail/ImapMail store, account folder, or an ImportExportTools NG folder export. Extensionless mbox files, nested .sbd folders, and available tags/flags are detected automatically.
          </p>
        </div>
      )}

      {isFilesTabMode(inputMode) && (
        <>
          <div className="mt-4 flex gap-3">
            <Button onClick={onChooseFiles}>
              <FileText /> Choose .mbox files…
            </Button>
            <Button variant="outline" onClick={onChooseFolder}>
              <FolderOpen /> Select folder tree…
            </Button>
          </div>

          {inputMode === "folderTree" && profileRoot && (
            <div className="mt-4 rounded-lg border border-border bg-card p-3">
              <div className="text-sm font-medium text-foreground">Selected folder tree</div>
              <div className="mt-1 break-all text-xs text-light-gray" title={profileRoot}>{profileRoot}</div>
              <div className="mt-3" role="status" aria-live="polite">
                {folderTreeDiscovering && (
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Spinner size="sm" /> Discovering mail folders…
                  </div>
                )}
                {!folderTreeDiscovering && folderTreeDiscovery && folderTreeDiscovery.sources.length > 0 && (
                  <div className="text-sm font-medium text-foreground">
                    {folderTreeDiscovery.sources.length} mail folder{folderTreeDiscovery.sources.length === 1 ? "" : "s"} found
                  </div>
                )}
                {sourceError && (
                  <p className="mt-3 text-sm text-destructive">{sourceError}</p>
                )}
              </div>
              {!folderTreeDiscovering && folderTreeDiscovery && folderTreeDiscovery.sources.length > 0 && (
                <>
                  <div className="mt-2 flex max-h-40 flex-col gap-1 overflow-auto">
                    {folderTreeDiscovery.sources.map((source) => (
                      <div key={source.path} className="rounded border border-border bg-background px-2 py-1 text-sm text-foreground">
                        {source.targetFolderPath.join(" / ")}
                      </div>
                    ))}
                  </div>
                  {(folderTreeDiscovery.warnings.length > 0 || folderTreeDiscovery.skipped.length > 0) && (
                    <div className="mt-2 text-xs text-light-gray">
                      {folderTreeDiscovery.warnings.length > 0 && `${folderTreeDiscovery.warnings.length} warning${folderTreeDiscovery.warnings.length === 1 ? "" : "s"}`}
                      {folderTreeDiscovery.warnings.length > 0 && folderTreeDiscovery.skipped.length > 0 && " · "}
                      {folderTreeDiscovery.skipped.length > 0 && `${folderTreeDiscovery.skipped.length} item${folderTreeDiscovery.skipped.length === 1 ? "" : "s"} skipped`}
                    </div>
                  )}
                  {folderTreeDiscovery.skipped.length > 0 && (
                    <details className="mt-2 text-xs text-light-gray">
                      <summary className="cursor-pointer">Show skipped folder details</summary>
                      <dl className="mt-1 max-h-28 space-y-1 overflow-auto">
                        {folderTreeDiscovery.skipped.map((skipped) => (
                          <div key={`${skipped.path}:${skipped.code}`}>
                            <dt className="break-all text-foreground">{skipped.path}</dt>
                            <dd>{skipped.reason}</dd>
                          </div>
                        ))}
                      </dl>
                    </details>
                  )}
                </>
              )}
            </div>
          )}

          {inputMode === "files" && files.length > 0 && (
            <div className="mt-4">
              <div className="text-xs text-light-gray">
                {files.length} mbox file{files.length === 1 ? "" : "s"} · {formatBytes(totalBytes)}
              </div>
              <div className="mt-2 flex max-h-40 flex-col gap-1.5 overflow-auto">
                {files.map((f) => (
                  <div
                    key={f.path}
                    className="flex items-center gap-3 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground"
                  >
                    <span className="truncate">{splitPath(f.path).base}</span>
                    <span className="ml-auto shrink-0 text-light-gray">{formatBytes(f.size)}</span>
                    <button
                      type="button"
                      onClick={() => removeFile(f.path)}
                      aria-label={`Remove ${splitPath(f.path).base}`}
                      title="Remove"
                      className="shrink-0 rounded p-0.5 text-light-gray transition-colors hover:text-destructive"
                    >
                      <X className="size-4" />
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}
        </>
      )}

      {isFilesTabMode(inputMode) && (
        <div className="mt-5">
          <div className="mb-1 text-sm font-medium text-foreground">Output location and PST name</div>
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={onChooseOutput}>
              <Save /> {outputPath ? splitPath(outputPath).base : "Choose output…"}
            </Button>
            {outputPath && (
              <button
                type="button"
                onClick={clearOutput}
                aria-label="Clear output location"
                title="Clear"
                className="shrink-0 rounded p-0.5 text-light-gray transition-colors hover:text-destructive"
              >
                <X className="size-4" />
              </button>
            )}
          </div>
          {outputPath && <div className="mt-1 text-xs text-light-gray">{splitPath(outputPath).dir}</div>}
        </div>
      )}

      {(pickerError || (sourceError && inputMode !== "folderTree")) && (
        <p className="mt-4 text-sm text-destructive">{pickerError ?? sourceError}</p>
      )}

      <div className="mt-auto flex items-center justify-end pt-5">
        <Button disabled={!canContinue} onClick={onContinue}>
          Continue ›
        </Button>
      </div>
    </div>
  );
}
