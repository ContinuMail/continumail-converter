// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { FolderOpen, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { groupByAccount } from "@/lib/accounts";
import { formatBytes } from "@/lib/format";
import { pickOutputFolder } from "@/lib/engine";
import type { ProfileSourceRow, OutputTarget, Account } from "@/lib/types";

interface AccountsViewProps {
  rows: ProfileSourceRow[];
  accounts: Account[];
  selectedAccountKeys: Set<string>;
  pstNames: Record<string, string>;
  outputTarget: OutputTarget | null;
  onOutputTargetChange: (target: OutputTarget | null) => void;
  onToggleAccount: (key: string) => void;
  onSetPstName: (key: string, name: string) => void;
  onBack: () => void;
  onContinue: () => void;
}

export function AccountsView({
  rows,
  accounts,
  selectedAccountKeys,
  pstNames,
  outputTarget,
  onOutputTargetChange,
  onToggleAccount,
  onSetPstName,
  onBack,
  onContinue,
}: AccountsViewProps) {
  const groups = groupByAccount(rows, accounts);
  const selectedCount = selectedAccountKeys.size;
  const folderDir = outputTarget?.kind === "folder" ? outputTarget.dir : null;
  const canContinue = selectedCount >= 1 && folderDir !== null;

  async function onPickFolder() {
    const dir = await pickOutputFolder();
    if (dir) onOutputTargetChange({ kind: "folder", dir });
  }

  function onClearFolder() {
    onOutputTargetChange(null);
  }

  return (
    <div className="flex flex-1 flex-col min-h-0">
      <h1 className="text-xl font-semibold text-foreground">Accounts found</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Select the accounts to convert. Each account produces one PST file.
      </p>

      <div className="mt-3 min-h-0 flex-1 overflow-auto flex flex-col gap-2">
        {groups.map((group) => {
          const checked = selectedAccountKeys.has(group.key);
          const label = group.account?.email ?? group.account?.folderSegment ?? group.key;
          const host = group.account?.host;
          const notFound = group.account?.addressResolution === "not-found";
          const currentName = pstNames[group.key] ?? group.defaultPstName;

          return (
            <div
              key={group.key}
              className="rounded-lg border border-border bg-card px-4 py-3"
            >
              <div className="flex items-start gap-3">
                <input
                  type="checkbox"
                  checked={checked}
                  onChange={() => onToggleAccount(group.key)}
                  aria-label={`Include account ${label}`}
                  className="mt-0.5 accent-primary"
                />
                <div className="flex-1 min-w-0">
                  <div className="font-medium text-sm text-foreground truncate">{label}</div>
                  <div className="mt-0.5 text-xs text-light-gray">
                    {[
                      host,
                      `${group.folderCount} folder${group.folderCount === 1 ? "" : "s"}`,
                      `${group.messageCount.toLocaleString()} messages`,
                      formatBytes(group.estimatedBytes),
                    ]
                      .filter(Boolean)
                      .join(" · ")}
                  </div>
                  {notFound && (
                    <div className="mt-1 text-xs text-muted-foreground italic">
                      address not found in profile
                    </div>
                  )}
                  <div className="mt-2 flex items-center gap-2">
                    <label className="text-xs text-light-gray shrink-0">PST name:</label>
                    <input
                      type="text"
                      value={currentName}
                      onChange={(e) => onSetPstName(group.key, e.target.value)}
                      aria-label={`PST name for ${label}`}
                      className="flex-1 min-w-0 rounded border border-border bg-background px-2 py-0.5 text-xs text-foreground focus:outline-none focus:ring-1 focus:ring-primary"
                    />
                    <span className="text-xs text-light-gray">.pst</span>
                  </div>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      <div className="mt-3">
        <div className="mb-1 text-sm font-medium text-foreground">Output folder</div>
        <div className="flex items-center gap-2">
          <Button variant="outline" onClick={onPickFolder}>
            <FolderOpen /> {folderDir ? folderDir.split(/[\\/]/).pop() : "Choose output folder…"}
          </Button>
          {folderDir && (
            <button
              type="button"
              onClick={onClearFolder}
              aria-label="Clear output folder"
              title="Clear"
              className="shrink-0 rounded p-0.5 text-light-gray transition-colors hover:text-destructive"
            >
              <X className="size-4" />
            </button>
          )}
        </div>
        {folderDir && (
          <div className="mt-1 text-xs text-light-gray">{folderDir}</div>
        )}
      </div>

      <div className="mt-3 text-sm text-muted-foreground">
        {selectedCount} account{selectedCount === 1 ? "" : "s"} selected &rarr; {selectedCount} PST file{selectedCount === 1 ? "" : "s"}
      </div>

      <div className="mt-4 flex items-center justify-end">
        <div className="flex gap-3">
          <Button variant="outline" onClick={onBack}>&#8249; Back</Button>
          <Button disabled={!canContinue} onClick={onContinue}>Review folders &#8250;</Button>
        </div>
      </div>
    </div>
  );
}
