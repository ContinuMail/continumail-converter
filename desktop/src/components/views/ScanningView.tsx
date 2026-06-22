// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useEffect, useRef, useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { ProgressBar } from "@/components/ui/progress-bar";
import { easeToward } from "@/lib/progressStats";
import { formatBytes } from "@/lib/format";
import { scanningTitle } from "@/lib/scan";

export interface ScanProgress {
  bytes: number;
  totalBytes: number;
}

/** Scanning step. Determinate byte-% bar once the first scanProgress arrives
 * (eased via easeToward so it climbs smoothly between ~64 MB updates); falls
 * back to the indeterminate spinner until then or when totalBytes is 0. */
export function ScanningView({
  fileCount,
  progress,
}: {
  fileCount: number;
  progress?: ScanProgress | null;
}) {
  const determinate = progress != null && progress.totalBytes > 0;
  const target = determinate ? Math.min(1, progress!.bytes / progress!.totalBytes) : 0;

  const targetRef = useRef(target);
  targetRef.current = target;
  const fracRef = useRef(0);
  const pushedPctRef = useRef(-1);
  const [frac, setFrac] = useState(0);

  useEffect(() => {
    if (!determinate) return;
    let id = 0;
    const tick = () => {
      // Ease toward the target, clamped so the bar never overshoots truth.
      fracRef.current = Math.min(easeToward(fracRef.current, targetRef.current, 0.15, 0.001), targetRef.current);
      const pct = Math.round(fracRef.current * 100);
      if (pct !== pushedPctRef.current) {
        pushedPctRef.current = pct;
        setFrac(fracRef.current);
      }
      id = requestAnimationFrame(tick);
    };
    id = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(id);
  }, [determinate]);

  if (!determinate) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center text-center">
        <Spinner className="size-8 text-primary" />
        <h1 className="mt-4 text-lg font-semibold text-foreground">
          {scanningTitle(fileCount)}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">Large archives can take a while.</p>
      </div>
    );
  }

  const pct = Math.round(frac * 100);
  return (
    <div className="flex flex-1 flex-col items-center justify-center text-center">
      <h1 className="text-lg font-semibold text-foreground">
        {scanningTitle(fileCount)}
      </h1>
      <div className="mt-4 w-full max-w-md">
        <div className="flex justify-between text-sm text-foreground">
          <span className="font-semibold">
            {formatBytes(progress!.bytes)} / {formatBytes(progress!.totalBytes)}
          </span>
          <span className="text-light-gray">{pct}%</span>
        </div>
        <div className="mt-1.5">
          <ProgressBar value={frac} />
        </div>
      </div>
      <p className="mt-3 text-sm text-muted-foreground">Large archives can take a while.</p>
    </div>
  );
}
