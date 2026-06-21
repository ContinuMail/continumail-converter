// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { useState, type ReactNode } from "react";
import { CircleHelp } from "lucide-react";

export function HelpTip({ children, label = "Help" }: { children: ReactNode; label?: string }) {
  const [clickedOpen, setClickedOpen] = useState(false);
  const [hovered, setHovered] = useState(false);
  const open = clickedOpen || hovered;

  return (
    <span
      className="relative inline-flex"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onKeyDown={(e) => { if (e.key === "Escape") setClickedOpen(false); }}
      onBlur={(e) => {
        // Close the click-pinned popover only when focus leaves the whole wrapper —
        // not merely when the trigger button loses focus to content inside it.
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setClickedOpen(false);
      }}
    >
      <button
        type="button"
        aria-label={label}
        aria-haspopup="dialog"
        aria-expanded={open}
        className="inline-flex text-light-gray transition-colors hover:text-foreground"
        onClick={() => setClickedOpen((v) => !v)}
      >
        <CircleHelp className="size-4" />
      </button>
      {open && (
        <span
          role="dialog"
          className="absolute left-0 top-6 z-10 w-72 rounded-lg border border-border bg-popover px-3 py-2 text-xs font-normal text-popover-foreground shadow-lg"
        >
          {children}
        </span>
      )}
    </span>
  );
}
