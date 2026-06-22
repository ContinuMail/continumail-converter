// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import type { ReactNode } from "react";
import { ArrowRightLeft, Clock, SlidersHorizontal } from "lucide-react";
import { cn } from "@/lib/utils";

export function Shell({
  children,
  steps,
  currentStep = 0,
  onStepSelect,
}: {
  children: ReactNode;
  steps: string[];
  currentStep?: number;
  // When provided, already-completed steps (index < currentStep) become
  // clickable to navigate back. Forward/current steps are never clickable.
  onStepSelect?: (step: number) => void;
}) {
  return (
    <div className="flex h-screen overflow-hidden bg-background text-foreground">
      {/* icon rail */}
      <nav className="flex w-16 flex-col items-center gap-5 bg-midnight pt-4 text-cream">
        <BrandMark />
        <RailItem active>
          <ArrowRightLeft className="size-[18px]" />
        </RailItem>
        <RailItem>
          <Clock className="size-[18px]" />
        </RailItem>
        <div className="mt-auto mb-4">
          <RailItem>
            <SlidersHorizontal className="size-[18px]" />
          </RailItem>
        </div>
      </nav>

      {/* main area — the single scroll region: vertical scrolls, horizontal is
          clipped so the app never scrolls sideways. Keeping scroll here (not on
          the document) leaves the icon rail pinned full-height. */}
      <main className="flex min-w-0 flex-1 flex-col overflow-y-auto overflow-x-hidden p-6">
        <ol className="mb-5 flex flex-wrap items-center gap-2 text-xs text-light-gray">
          {steps.map((label, i) => {
            const navigable = onStepSelect != null && i < currentStep;
            return (
              <li key={label} className="flex items-center gap-2">
                {navigable ? (
                  <button
                    type="button"
                    onClick={() => onStepSelect(i)}
                    aria-label={`Go back to ${label}`}
                    className="cursor-pointer rounded-full px-2.5 py-0.5 transition-colors hover:bg-muted hover:text-foreground"
                  >
                    {i + 1} {label}
                  </button>
                ) : (
                  <span
                    className={cn(
                      "rounded-full px-2.5 py-0.5",
                      i === currentStep ? "bg-primary text-primary-foreground" : "",
                    )}
                  >
                    {i + 1} {label}
                  </span>
                )}
                {i < steps.length - 1 && <span aria-hidden>›</span>}
              </li>
            );
          })}
        </ol>
        {children}
      </main>
    </div>
  );
}

// ContinuMail "CM" brand mark — mirrors the app icon (terracotta squircle, cream CM).
// Brand asset: proprietary, not covered by the GPL (see TRADEMARKS.md).
function BrandMark() {
  return (
    <svg
      width="30"
      height="30"
      viewBox="0 0 512 512"
      role="img"
      aria-label="ContinuMail"
    >
      <rect width="512" height="512" rx="115" fill="#b05a36" />
      <text
        x="256"
        y="250"
        textAnchor="middle"
        dominantBaseline="central"
        fontFamily="Inter, system-ui, -apple-system, sans-serif"
        fontSize="238"
        fontWeight="800"
        letterSpacing="-12"
        fill="#fef9ef"
      >
        CM
      </text>
    </svg>
  );
}

function RailItem({ children, active = false }: { children: ReactNode; active?: boolean }) {
  return (
    <div
      className={cn(
        "flex size-9 items-center justify-center rounded-[9px]",
        active ? "bg-primary text-primary-foreground" : "text-cream/55",
      )}
    >
      {children}
    </div>
  );
}
