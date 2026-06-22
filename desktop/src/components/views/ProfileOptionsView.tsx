// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { useMemo, useState } from "react";
import { Save, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { HelpTip } from "@/components/ui/help-tip";
import { SplitSizeControl } from "@/components/ui/split-size-control";
import { buildProfileConfig } from "@/lib/profileConfig";
import { effectiveRows } from "@/lib/review";
import { ConvertConfigError, deriveOutputTarget } from "@/lib/convert";
import { splitPath } from "@/lib/convert";
import { formatBytes } from "@/lib/format";
import { openJunkHelp, pickOutputPst } from "@/lib/engine";
import type { OptionsState } from "@/lib/options";
import type { ConversionConfig, ProfileSourceRow, OutputTarget } from "@/lib/types";

interface ProfileOptionsViewProps {
  rows: ProfileSourceRow[];
  outputTarget: OutputTarget | null;
  onOutputTargetChange: (target: OutputTarget | null) => void;
  profileRoot: string;
  checkedIds: Set<string>;
  skipEmpty: boolean;
  options: OptionsState;
  onSetOptions: (patch: Partial<OptionsState>) => void;
  onStart: (config: ConversionConfig, outputDir: string) => void;
  onBack: () => void;
}

export function ProfileOptionsView({
  rows, outputTarget, onOutputTargetChange, profileRoot, checkedIds, skipEmpty, options, onSetOptions, onStart, onBack,
}: ProfileOptionsViewProps) {
  const [startError, setStartError] = useState<string | null>(null);
  const [splitOk, setSplitOk] = useState(true);

  const eff = useMemo(
    () => effectiveRows(rows, checkedIds, skipEmpty) as ProfileSourceRow[],
    [rows, checkedIds, skipEmpty],
  );

  const outputPath = outputTarget?.kind === "pstFile" ? outputTarget.path : null;

  const pstName = useMemo(() => {
    if (!outputPath) return "Output";
    try { return deriveOutputTarget(outputPath).pstName; } catch { return "Output"; }
  }, [outputPath]);
  const totalBytes = eff.reduce((n, r) => n + r.bytes, 0);
  const canStart = eff.length > 0 && splitOk && outputTarget !== null;

  async function onChooseOutput() {
    const path = await pickOutputPst();
    if (path) onOutputTargetChange({ kind: "pstFile", path });
  }

  function onClearOutput() {
    onOutputTargetChange(null);
  }

  function onStartClick() {
    if (!outputPath) {
      setStartError("Choose an output .pst file first.");
      return;
    }
    try {
      const { config, outputDir } = buildProfileConfig(
        rows, checkedIds, skipEmpty, options, outputPath, profileRoot,
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
        Choose how the discovered folders are written and how large each PST may get.
      </p>

      <div className="mt-4 flex flex-1 gap-6 overflow-hidden">
        <div className="flex w-64 shrink-0 flex-col gap-5">
          <div>
            <div className="mb-1 text-sm font-medium text-foreground">Folder structure</div>
            <label className="flex items-center gap-2 text-sm text-foreground">
              <input type="radio" name="mapping" checked={options.folderMapping === "mirror"}
                onChange={() => onSetOptions({ folderMapping: "mirror" })} />
              Mirror the Thunderbird tree
            </label>
            <label className="mt-1 flex items-center gap-2 text-sm text-foreground">
              <input type="radio" name="mapping" checked={options.folderMapping === "flatten"}
                onChange={() => onSetOptions({ folderMapping: "flatten" })} />
              Flatten into one folder
            </label>
          </div>
          <div>
            <div className="mb-1 flex items-center gap-1.5 text-sm font-medium text-foreground">
              Junk handling
              <HelpTip>
                <strong className="text-foreground">Thunderbird junk scores.</strong> Thunderbird's own junk filter
                scores each message for how spam-like it is, based on what you've marked as junk. That score is
                separate from which folder a message sits in, so it's <strong className="text-foreground">not</strong>{" "}
                the same as the contents of your Spam/Junk folder. This option acts on messages Thunderbird scored
                as junk, wherever they are.{" "}
                <strong className="text-foreground">"Leave in place" is recommended if unsure.</strong>
                <button
                  type="button"
                  onClick={() => void openJunkHelp()}
                  className="mt-2 block text-primary underline underline-offset-2 hover:opacity-80"
                >
                  Learn more on Mozilla Support
                </button>
              </HelpTip>
            </div>
            <label className="flex items-center gap-2 text-sm text-foreground">
              <input type="radio" name="junk" checked={options.junkHandling === "Off"}
                onChange={() => onSetOptions({ junkHandling: "Off" })} />
              Leave in place
            </label>
            <label className="mt-1 flex items-center gap-2 text-sm text-foreground">
              <input type="radio" name="junk" checked={options.junkHandling === "Category"}
                onChange={() => onSetOptions({ junkHandling: "Category" })} />
              Tag as Junk category
            </label>
            <label className="mt-1 flex items-center gap-2 text-sm text-foreground">
              <input type="radio" name="junk" checked={options.junkHandling === "Folder"}
                onChange={() => onSetOptions({ junkHandling: "Folder" })} />
              Move to Junk Email folder
            </label>
          </div>

          <div>
            <label className="flex items-center gap-2 text-sm text-foreground">
              <input type="checkbox" checked={options.dropExpunged}
                onChange={(e) => onSetOptions({ dropExpunged: e.target.checked })} />
              Skip deleted (expunged) messages
            </label>
            <p className="ml-6 mt-0.5 text-xs text-light-gray">
              Permanently leave out messages Thunderbird marked as deleted.
            </p>
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

        <div className="flex flex-1 flex-col overflow-hidden">
          <div className="mb-1 text-sm font-medium text-foreground">Preview</div>
          <div className="flex-1 overflow-auto rounded-lg border border-border p-3 text-sm">
            <div className="font-semibold text-foreground">{pstName}.pst</div>
            <div className="mt-1 flex flex-col gap-1">
              {options.folderMapping === "flatten" ? (
                <div className="pl-3 text-foreground">└ Imported Mail
                  <span className="ml-2 text-light-gray">{eff.reduce((n, r) => n + r.messages, 0).toLocaleString()} · {formatBytes(totalBytes)}</span>
                </div>
              ) : eff.map((r) => (
                <div key={r.id} className="pl-3 text-foreground">
                  └ {r.displayName}
                  <span className="ml-2 text-light-gray">{r.messages.toLocaleString()} · {formatBytes(r.bytes)}</span>
                </div>
              ))}
              {eff.length === 0 && <div className="pl-3 text-xs text-light-gray">No folders selected.</div>}
            </div>
          </div>
          <div className="mt-2 rounded-lg border border-border border-l-[3px] border-l-primary bg-card px-3 py-2 text-xs text-muted-foreground">
            Carried over from Thunderbird automatically: <span className="text-foreground">read/unread, replied, forwarded, starred</span> flags and <span className="text-foreground">tags → Outlook categories</span> (folders with an <span className="text-primary">.msf</span> badge).
          </div>
        </div>
      </div>

      <div className="mt-4">
        <div className="mb-1 text-sm font-medium text-foreground">Output PST file</div>
        <div className="flex items-center gap-2">
          <Button variant="outline" onClick={onChooseOutput}>
            <Save /> {outputPath ? splitPath(outputPath).base : "Choose output…"}
          </Button>
          {outputPath && (
            <button
              type="button"
              onClick={onClearOutput}
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
