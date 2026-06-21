// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from "react";
import { Palette, TriangleAlert, Check } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { previewColours, applyColours } from "@/lib/engine";
import { summarizeColourApply } from "@/lib/colourImport";
import type { ColourCategory } from "@/lib/types";

type Phase =
  | { k: "loading" }
  | { k: "ready"; outlookAvailable: boolean; categories: ColourCategory[] }
  | { k: "applying" }
  | { k: "applied"; added: number; existing: number }
  | { k: "error"; message: string; retry: "preview" | "apply" }
  | { k: "dismissed" };

const ACTION_LABEL: Record<string, string> = {
  "would-add": "will add",
  "added": "added",
  "skipped-existing": "already in Outlook",
  "skipped-no-colour": "no colour",
  "skipped-invalid-name": "invalid name",
};

// Map a raw engine error message to friendly text for the card.
function errorText(message: string): string {
  if (/running/i.test(message)) return "Outlook is open — close it completely, then retry.";
  if (/did not respond|timed out|timeout/i.test(message)) return "Outlook didn't respond — dismiss any Outlook prompt and retry.";
  return message;
}

export function ColourImportCard({ profileRoot }: { profileRoot: string }) {
  const [phase, setPhase] = useState<Phase>({ k: "loading" });
  const [consent, setConsent] = useState(false);
  // Guard against setting state after unmount (e.g. user clicks "Convert another" while a one-shot
  // preview/apply is still running — there is no cancellation, so just drop the late result).
  const mounted = useRef(true);
  useEffect(() => () => { mounted.current = false; }, []);

  const runPreview = useCallback(() => {
    setPhase({ k: "loading" });
    previewColours(profileRoot)
      .then((r) => {
        if (!mounted.current) return;
        setPhase(
          r.kind === "error"
            ? { k: "error", message: r.message, retry: "preview" }
            : { k: "ready", outlookAvailable: r.outlookAvailable, categories: r.categories },
        );
      })
      .catch((e) => { if (mounted.current) setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), retry: "preview" }); });
  }, [profileRoot]);

  const runApply = useCallback(() => {
    setPhase({ k: "applying" });
    applyColours(profileRoot)
      .then((r) => {
        if (!mounted.current) return;
        if (r.kind === "error") { setPhase({ k: "error", message: r.message, retry: "apply" }); return; }
        const s = summarizeColourApply(r.categories);
        setPhase({ k: "applied", added: s.added, existing: s.existing });
      })
      .catch((e) => { if (mounted.current) setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), retry: "apply" }); });
  }, [profileRoot]);

  useEffect(() => { runPreview(); }, [runPreview]);

  if (phase.k === "dismissed") return null;

  const dismiss = () => setPhase({ k: "dismissed" });

  return (
    <div className="mt-4 overflow-hidden rounded-[11px] border border-border">
      <div className="flex items-center justify-between border-b border-border bg-card px-3.5 py-2.5">
        <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
          <Palette className="size-4 text-primary" /> Outlook category colours
        </div>
        <span className="rounded-full bg-primary/12 px-2 py-0.5 text-[10px] text-primary">Optional · Windows + Outlook</span>
      </div>

      <div className="px-3.5 py-3 text-sm">
        {phase.k === "loading" && (
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2 text-muted-foreground"><Spinner size="sm" /> Checking your tag colours…</div>
            <Button variant="outline" onClick={dismiss}>Skip</Button>
          </div>
        )}

        {phase.k === "applying" && (
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2 text-muted-foreground"><Spinner size="sm" /> Importing… Outlook opens briefly to save the categories.</div>
            <Button variant="outline" onClick={dismiss}>Hide</Button>
          </div>
        )}

        {phase.k === "applied" && (
          <div>
            <div className="flex items-start gap-2 text-foreground">
              <Check className="mt-0.5 size-4 shrink-0 text-primary" />
              <span>Colours imported. Added {phase.added}, {phase.existing} already existed. Reopen Outlook to see them.</span>
            </div>
            <div className="mt-3"><Button variant="outline" onClick={dismiss}>Done</Button></div>
          </div>
        )}

        {phase.k === "error" && (
          <div>
            <div className="flex items-start gap-2 text-destructive">
              <TriangleAlert className="mt-0.5 size-4 shrink-0" /> <span>{errorText(phase.message)}</span>
            </div>
            <div className="mt-3 flex gap-2.5">
              <Button onClick={phase.retry === "apply" ? runApply : runPreview}>Retry</Button>
              <Button variant="outline" onClick={dismiss}>Skip</Button>
            </div>
          </div>
        )}

        {phase.k === "ready" && (() => {
          const wouldAdd = phase.categories.filter((c) => c.action === "would-add");
          return (
            <div>
              <p className="mb-2 text-xs text-muted-foreground">
                Your tags became Outlook categories, but Outlook colours them from its own master list. Import your Thunderbird tag colours so they match.
              </p>
              <div className="flex flex-col gap-1">
                {phase.categories.map((c) => (
                  <div key={c.name} className="flex items-center gap-2.5 text-[13px]">
                    <span className="size-3.5 shrink-0 rounded border border-black/10" style={{ background: c.hex ?? "transparent" }} />
                    <span className="flex-1 text-foreground">{c.name}</span>
                    <span className="rounded-full bg-muted px-2 py-0.5 text-[10px] text-muted-foreground">{ACTION_LABEL[c.action] ?? c.action}</span>
                  </div>
                ))}
                {phase.categories.length === 0 && <div className="text-xs text-light-gray">No tag colours found in this profile.</div>}
              </div>

              {!phase.outlookAvailable ? (
                <div className="mt-3 text-xs text-light-gray">Outlook not detected — install Outlook to import colours.</div>
              ) : wouldAdd.length === 0 ? (
                <div className="mt-3 text-xs text-light-gray">Nothing to import — no new Outlook colours are available from this profile.</div>
              ) : (
                <>
                  <div className="mt-3 flex items-start gap-2 rounded-lg border border-[#ecd9a8] bg-[#fbf2dd] px-3 py-2 text-[11.5px] text-[#8a6516]">
                    <TriangleAlert className="mt-0.5 size-4 shrink-0" />
                    <span>This adds these categories to Outlook's master list for <strong>all</strong> your Outlook accounts — not just this PST. <strong>Close Outlook before importing.</strong></span>
                  </div>
                  <label className="mt-2.5 flex items-center gap-2 text-xs text-foreground">
                    <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
                    I understand this changes my Outlook categories
                  </label>
                </>
              )}

              <div className="mt-3 flex items-center gap-2.5">
                {phase.outlookAvailable && wouldAdd.length > 0 && (
                  <Button disabled={!consent} onClick={runApply}>Import colours to Outlook</Button>
                )}
                <Button variant="outline" onClick={dismiss}>Skip</Button>
              </div>
            </div>
          );
        })()}
      </div>
    </div>
  );
}
