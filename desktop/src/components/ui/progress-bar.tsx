// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

interface ProgressBarProps {
  value: number; // 0..1
}

export function ProgressBar({ value }: ProgressBarProps) {
  const pct = Math.max(0, Math.min(1, value)) * 100;
  // No CSS width transition: `width` triggers layout and would animate on the
  // (conversion-saturated) main thread, so the animated fill lags the instantly
  // committed percentage text. Instead the caller feeds an already-eased float
  // that advances in fine sub-pixel steps, so the fill is smooth AND always
  // agrees with the number in the same render.
  return (
    <div className="h-3 w-full overflow-hidden rounded-full bg-dark-cream">
      <div className="h-full rounded-full bg-primary" style={{ width: `${pct}%` }} />
    </div>
  );
}
