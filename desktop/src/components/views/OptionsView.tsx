// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useMemo, useState } from "react";
import { Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import { SplitSizeControl } from "@/components/ui/split-size-control";
import { deriveOutputTarget, ConvertConfigError } from "@/lib/convert";
import { formatBytes } from "@/lib/format";
import {
  buildPstPreview,
  buildConfigFromOptions,
  validateFolderName,
  findDuplicateFolderIds,
  type OptionsState,
} from "@/lib/options";
import type { ConversionConfig, SourceRow } from "@/lib/types";
import type { ScanResult } from "@/lib/parse";

interface OptionsViewProps {
  scan: ScanResult;
  previewSources: SourceRow[];
  outputPath: string;
  checkedIds: Set<string>;
  skipEmpty: boolean;
  options: OptionsState;
  onSetOptions: (patch: Partial<OptionsState>) => void;
  onSetRename: (sourceId: string, name: string) => void;
  onStart: (config: ConversionConfig, outputDir: string) => void;
  onBack: () => void;
}

export function OptionsView({
  scan, previewSources, outputPath, checkedIds, skipEmpty, options, onSetOptions, onSetRename, onStart, onBack,
}: OptionsViewProps) {
  const [startError, setStartError] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [splitOk, setSplitOk] = useState(true);

  const pstName = useMemo(() => {
    try { return deriveOutputTarget(outputPath).pstName; } catch { return "Output"; }
  }, [outputPath]);

  const preview = useMemo(
    () => buildPstPreview(previewSources, checkedIds, skipEmpty, options, pstName),
    [previewSources, checkedIds, skipEmpty, options, pstName],
  );

  const duplicateIds = useMemo(() => findDuplicateFolderIds(preview.folders), [preview.folders]);
  const nameErrors = useMemo(() => {
    const m = new Map<string, string>();
    for (const f of preview.folders) {
      const err = validateFolderName(f.name);
      if (err) m.set(f.sourceId, err);
      else if (duplicateIds.has(f.sourceId)) m.set(f.sourceId, "Duplicate folder name.");
    }
    return m;
  }, [preview.folders, duplicateIds]);

  const canStart = preview.folders.length > 0 && nameErrors.size === 0 && splitOk;

  function onStartClick() {
    try {
      const { config, outputDir } = buildConfigFromOptions(
        scan.sources, checkedIds, skipEmpty, options, outputPath,
      );
      onStart(config, outputDir);
    } catch (e) {
      setStartError(e instanceof ConvertConfigError ? e.message : "Could not start the conversion.");
    }
  }

  return (
    <div className="flex flex-1 flex-col min-h-0">
      <h1 className="text-xl font-semibold text-foreground">Options</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Choose how folders are organised and how large each PST may get. The preview shows what will be written.
      </p>

      <div className="mt-4 flex flex-1 gap-6 overflow-hidden">
        {/* left: controls */}
        <div className="flex w-64 shrink-0 flex-col gap-5">
          <div>
            <div className="mb-1 text-sm font-medium text-foreground">Folder mapping</div>
            <label className="flex items-center gap-2 text-sm text-foreground">
              <input type="radio" name="mapping" checked={options.folderMapping === "mirror"}
                onChange={() => onSetOptions({ folderMapping: "mirror" })} />
              Mirror — one folder per file
            </label>
            <label className="mt-1 flex items-center gap-2 text-sm text-foreground">
              <input type="radio" name="mapping" checked={options.folderMapping === "flatten"}
                onChange={() => onSetOptions({ folderMapping: "flatten" })} />
              Flatten — everything in one folder
            </label>
          </div>

          <div>
            <div className="mb-1 text-sm font-medium text-foreground">Split size</div>
            <SplitSizeControl
              maxSizeMB={options.maxSizeMB}
              allowOversize={options.allowOversize}
              onChange={(mb) => onSetOptions({ maxSizeMB: mb })}
              onValidityChange={setSplitOk}
            />
          </div>
        </div>

        {/* right: live preview tree */}
        <div className="flex flex-1 flex-col overflow-hidden">
          <div className="mb-1 text-sm font-medium text-foreground">Preview</div>
          <p className="mb-2 text-xs text-light-gray">
            Renaming changes a folder's name, not its display order — Outlook lists PST folders alphabetically when the archive is opened.
          </p>
          <div className="flex-1 overflow-auto rounded-lg border border-border p-3 text-sm">
            <div className="font-semibold text-foreground">{preview.pstName}.pst</div>
            <div className="mt-1 flex flex-col gap-1">
              {preview.folders.map((f) => {
                const err = nameErrors.get(f.sourceId);
                return (
                  <div key={f.sourceId} className="pl-3">
                    <div className="flex items-center gap-2">
                      <span className="text-light-gray">└</span>
                      {editing === f.sourceId ? (
                        <input
                          autoFocus
                          className="flex-1 rounded border border-border bg-card px-1.5 py-0.5 text-sm text-foreground"
                          defaultValue={f.name}
                          onBlur={(e) => { onSetRename(f.sourceId, e.target.value); setEditing(null); }}
                          onKeyDown={(e) => { if (e.key === "Enter") (e.target as HTMLInputElement).blur(); }}
                          aria-label={`Rename ${f.name}`}
                        />
                      ) : (
                        <button
                          type="button"
                          className={"flex items-center gap-1 text-left " + (err ? "text-destructive" : "text-foreground")}
                          onClick={() => setEditing(f.sourceId)}
                          title="Rename folder"
                        >
                          {f.name}<Pencil className="size-3 text-light-gray" />
                        </button>
                      )}
                      <span className="ml-auto shrink-0 text-light-gray">
                        {f.messages.toLocaleString()} · {formatBytes(f.bytes)}
                      </span>
                    </div>
                    {err && <div className="pl-5 text-xs text-destructive">{err}</div>}
                  </div>
                );
              })}
              {preview.folders.length === 0 && (
                <div className="pl-3 text-xs text-light-gray">No folders selected.</div>
              )}
            </div>
          </div>
          <div className="mt-2 text-xs text-light-gray">
            {preview.estimatedParts === 1
              ? `~${formatBytes(preview.totalBytes)} · one file`
              : `≈ ${preview.estimatedParts} files (${preview.pstName}-1.pst … ${preview.pstName}-${preview.estimatedParts}.pst) · approximate`}
          </div>
        </div>
      </div>

      {startError && <p className="mt-3 text-sm text-destructive">{startError}</p>}

      <div className="mt-4 flex items-center justify-end">
        <div className="flex gap-3">
          <Button variant="outline" onClick={onBack}>‹ Back</Button>
          <Button disabled={!canStart} onClick={onStartClick}>Start conversion ›</Button>
        </div>
      </div>
    </div>
  );
}
