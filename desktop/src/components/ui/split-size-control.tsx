// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useState } from "react";
import { SPLIT_PRESETS, resolveCustomGbToMb } from "@/lib/options";

interface SplitSizeControlProps {
  maxSizeMB: number;
  allowOversize: boolean;
  onChange: (maxSizeMB: number) => void;
  onValidityChange: (ok: boolean) => void;
}

export function SplitSizeControl({ maxSizeMB, allowOversize, onChange, onValidityChange }: SplitSizeControlProps) {
  const presetMatch = SPLIT_PRESETS.find((p) => p.mb === maxSizeMB);
  const [splitMode, setSplitMode] = useState<"preset" | "custom">(presetMatch ? "preset" : "custom");
  const [customText, setCustomText] = useState<string>(
    presetMatch ? "" : String(Math.round(maxSizeMB / 1024)),
  );
  const [customErr, setCustomErr] = useState<string | null>(null);

  function onSplitSelect(value: string) {
    if (value === "custom") {
      setSplitMode("custom");
      setCustomText(String(Math.round(maxSizeMB / 1024)));
      setCustomErr(null);
    } else {
      setSplitMode("preset");
      setCustomErr(null);
      onValidityChange(true);
      onChange(Number(value));
    }
  }

  function onCustomChange(text: string) {
    setCustomText(text);
    const r = resolveCustomGbToMb(text, allowOversize);
    if ("mb" in r) {
      setCustomErr(null);
      onValidityChange(true);
      onChange(r.mb);
    } else {
      setCustomErr(r.error);
      onValidityChange(false);
    }
  }

  return (
    <div>
      <select
        className="w-full rounded-lg border border-border bg-card px-2 py-1.5 text-sm text-foreground"
        value={splitMode === "custom" ? "custom" : String(maxSizeMB)}
        onChange={(e) => onSplitSelect(e.target.value)}
      >
        {SPLIT_PRESETS.map((p) => (
          <option key={p.mb} value={String(p.mb)}>{p.label}</option>
        ))}
        <option value="custom">Custom…</option>
      </select>
      {splitMode === "custom" && (
        <div className="mt-2">
          <input
            type="number" min={1} step="0.1"
            className="w-full rounded-lg border border-border bg-card px-2 py-1.5 text-sm text-foreground"
            value={customText}
            onChange={(e) => onCustomChange(e.target.value)}
            aria-label="Custom split size in GB"
          />
          <div className="mt-0.5 text-xs text-light-gray">Size in GB</div>
          {customErr && <div className="mt-0.5 text-xs text-destructive">{customErr}</div>}
        </div>
      )}
      <p className="mt-1 text-xs text-light-gray">
        Uses the PST safety limit of 50&nbsp;GB. Very large archives may still be split.
      </p>
    </div>
  );
}
