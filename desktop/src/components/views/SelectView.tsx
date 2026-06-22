// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useState, useEffect } from "react";
import { FileText, FolderOpen, Save, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { pickMboxFiles, pickFolder, listMboxInDir, statFiles, pickOutputPst, listThunderbirdProfiles } from "@/lib/engine";
import { splitPath } from "@/lib/convert";
import { formatBytes } from "@/lib/format";
import type { FileStat, ProfileEntry } from "@/lib/types";

interface SelectViewProps {
  files: FileStat[];
  outputPath: string | null;
  inputMode: "files" | "profile";
  profileRoot: string | null;
  sourceError?: string | null;
  onFilesChange: (files: FileStat[]) => void;
  onOutputPathChange: (path: string | null) => void;
  onInputModeChange: (m: "files" | "profile") => void;
  onProfileRootChange: (path: string | null) => void;
  onContinue: () => void;
}

export function SelectView({
  files, outputPath, inputMode, profileRoot, sourceError,
  onFilesChange, onOutputPathChange, onInputModeChange, onProfileRootChange, onContinue,
}: SelectViewProps) {
  // Picker errors are transient and screen-local (e.g. "no .mbox in folder").
  // Parent owns the durable file/output state; this does NOT belong there.
  const [pickerError, setPickerError] = useState<string | null>(null);
  const [detectedProfiles, setDetectedProfiles] = useState<ProfileEntry[]>([]);

  // Load profiles.ini entries on mount (or when switching to profile mode).
  useEffect(() => {
    if (inputMode !== "profile") return;
    let cancelled = false;
    listThunderbirdProfiles().then((profiles) => {
      if (cancelled) return;
      setDetectedProfiles(profiles);
      // Only preselect the default when the user hasn't already chosen a path.
      if (profileRoot === null) {
        const def = profiles.find((p) => p.isDefault) ?? profiles[0];
        if (def) onProfileRootChange(def.path);
      }
    });
    return () => { cancelled = true; };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inputMode]);

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
    const found = await listMboxInDir(dir);
    if (found.length === 0) {
      setPickerError("No .mbox files found in that folder.");
      return;
    }
    await addFiles(found);
  }

  async function onChooseProfile() {
    setPickerError(null);
    const dir = await pickFolder();
    if (dir) onProfileRootChange(dir);
  }

  async function onChooseOutput() {
    setPickerError(null);
    const path = await pickOutputPst();
    if (path) onOutputPathChange(path);
  }

  function removeFile(path: string) {
    setPickerError(null);
    onFilesChange(files.filter((f) => f.path !== path));
  }

  function clearOutput() {
    setPickerError(null);
    onOutputPathChange(null);
  }

  const totalBytes = files.reduce((sum, f) => sum + f.size, 0);
  const canContinue =
    outputPath !== null &&
    (inputMode === "files" ? files.length > 0 : profileRoot !== null);

  return (
    <div className="flex flex-1 flex-col">
      <h1 className="text-xl font-semibold text-foreground">Choose what to convert</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Select a Thunderbird profile or pick individual <code>.mbox</code> files.
      </p>

      <div className="mt-4 inline-flex overflow-hidden rounded-md border border-border">
        <button
          type="button"
          aria-pressed={inputMode === "profile"}
          onClick={() => onInputModeChange("profile")}
          className={"px-4 py-1.5 text-sm " + (inputMode === "profile" ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted")}
        >
          Thunderbird profile
        </button>
        <button
          type="button"
          aria-pressed={inputMode === "files"}
          onClick={() => onInputModeChange("files")}
          className={"px-4 py-1.5 text-sm " + (inputMode === "files" ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted")}
        >
          .mbox files
        </button>
      </div>

      {inputMode === "profile" && (
        <div className="mt-4">
          {detectedProfiles.length > 0 && (
            <div className="mb-3 flex flex-col gap-1.5">
              {detectedProfiles.map((p) => (
                <label
                  key={p.path}
                  className={
                    "flex cursor-pointer items-start gap-3 rounded-lg border px-3 py-2 text-sm transition-colors " +
                    (profileRoot === p.path
                      ? "border-primary bg-primary/10 text-foreground"
                      : "border-border bg-card text-foreground hover:bg-muted")
                  }
                >
                  <input
                    type="radio"
                    name="profile"
                    value={p.path}
                    checked={profileRoot === p.path}
                    onChange={() => { setPickerError(null); onProfileRootChange(p.path); }}
                    className="mt-0.5 accent-primary shrink-0"
                  />
                  <div className="min-w-0">
                    <span className="font-medium">{p.name}</span>
                    {p.isDefault && (
                      <span className="ml-2 rounded bg-primary/15 px-1.5 py-0.5 text-xs text-primary">default</span>
                    )}
                    <div className="truncate text-xs text-light-gray">{p.path}</div>
                  </div>
                </label>
              ))}
            </div>
          )}
          <Button variant="outline" onClick={onChooseProfile}>
            <FolderOpen /> Browse manually…
          </Button>
          {profileRoot && detectedProfiles.every((p) => p.path !== profileRoot) && (
            <div className="mt-1 text-xs text-light-gray">{profileRoot}</div>
          )}
          <p className="mt-2 text-xs text-light-gray">
            Point at a Thunderbird profile, a Mail/ImapMail store, or a single account folder. Nested folders and tags/flags are detected automatically.
          </p>
        </div>
      )}

      {inputMode === "files" && (
        <>
          <div className="mt-4 flex gap-3">
            <Button onClick={onChooseFiles}>
              <FileText /> Choose .mbox files…
            </Button>
            <Button variant="outline" onClick={onChooseFolder}>
              <FolderOpen /> Select folder…
            </Button>
          </div>

          {files.length > 0 && (
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

      {(pickerError || sourceError) && (
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
