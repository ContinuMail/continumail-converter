// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from "react";
import { Palette, TriangleAlert, Check } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { applyColoursPlan, outlookProfilesList, outlookProfilesCreate, openInOutlook } from "@/lib/engine";
import { summarizeColourApply, cardProfileState, ProfileStageError, type OutlookProfiles } from "@/lib/colourImport";
import type { ColourPlanEntry } from "@/lib/types";

// The mail-less viewing profile we offer to create when the user has no classic-Outlook
// profile at all (auto-mount was dropped — see OutlookProfilesCommand.cs — so this only
// creates the profile; the user adds the converted .pst manually afterwards).
const CONTINUMAIL_PROFILE = "ContinuMail";

type Phase =
  | { k: "ready" }
  | { k: "creating" }
  | { k: "applying"; viaCreate?: boolean }
  | { k: "applied"; added: number; existing: number; viaCreate?: boolean }
  // `retry` is a closure bound to the exact step that failed (plain apply, or the
  // create→apply chain), so retrying never re-runs a step that already succeeded.
  | { k: "error"; message: string; stage?: string; retry: () => void }
  | { k: "dismissed" };

const ACTION_LABEL: Record<string, string> = {
  "would-add": "will add",
  "added": "added",
  "skipped-existing": "already in Outlook",
  "skipped-no-colour": "no colour",
  "skipped-invalid-name": "invalid name",
};

// Map a raw engine error message to friendly text for the card. Stage-tagged errors (from
// the create/open flow, e.g. `pim-unsupported`) already carry curated, actionable copy from
// the CLI — shown as-is rather than run through the plain-apply heuristics below.
function errorText(message: string, stage?: string): string {
  if (stage) return message;
  if (/running/i.test(message)) return "Outlook is open — close it completely, then retry.";
  if (/did not respond|timed out|timeout/i.test(message)) return "Outlook didn't respond — dismiss any Outlook prompt and retry.";
  return message;
}

export function ColourImportCard({ plan }: { plan: ColourPlanEntry[] }) {
  const wouldAdd = plan.filter((c) => c.action === "would-add");
  const [phase, setPhase] = useState<Phase>({ k: "ready" });
  const [consent, setConsent] = useState(false);
  // Outlook profile listing: null while loading (or if the listing call itself failed) —
  // the ready phase falls back to legacy no-profile-arg behaviour in that case, so a slow
  // or failed listing never blocks the existing single-profile flow.
  const [profiles, setProfiles] = useState<OutlookProfiles | null>(null);
  const [selectedProfile, setSelectedProfile] = useState(""); // session-only, "multiple" state
  const [opening, setOpening] = useState(false);
  const [opened, setOpened] = useState(false);
  // Guard against setting state after unmount (e.g. user clicks "Convert another" while
  // apply is still running — there is no cancellation, so just drop the late result).
  const mounted = useRef(true);
  useEffect(() => () => { mounted.current = false; }, []);

  useEffect(() => {
    outlookProfilesList()
      .then((p) => {
        if (!mounted.current) return;
        setProfiles(p);
        setSelectedProfile(p.defaultProfile ?? p.profiles[0] ?? "");
      })
      .catch(() => { /* listing failed — ready phase falls back to legacy behaviour */ });
  }, []);

  const state = profiles ? cardProfileState(profiles) : null;

  const runApply = useCallback((profileName?: string) => {
    setPhase({ k: "applying" });
    applyColoursPlan(plan, profileName)
      .then((r) => {
        if (!mounted.current) return;
        if (r.kind === "error") { setPhase({ k: "error", message: r.message, retry: () => runApply(profileName) }); return; }
        const s = summarizeColourApply(r.categories);
        setPhase({ k: "applied", added: s.added, existing: s.existing });
      })
      .catch((e) => {
        if (!mounted.current) return;
        setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), retry: () => runApply(profileName) });
      });
  }, [plan]);

  // Second half of the "none" flow: apply the plan to the just-created ContinuMail
  // profile. Factored out so a failure here can retry just this step, not the whole
  // create→list-refresh chain.
  const applyForContinuMail = useCallback(() => {
    setPhase({ k: "applying", viaCreate: true });
    applyColoursPlan(plan, CONTINUMAIL_PROFILE)
      .then((r) => {
        if (!mounted.current) return;
        if (r.kind === "error") { setPhase({ k: "error", message: r.message, retry: applyForContinuMail }); return; }
        const s = summarizeColourApply(r.categories);
        setPhase({ k: "applied", added: s.added, existing: s.existing, viaCreate: true });
      })
      .catch((e) => {
        if (!mounted.current) return;
        setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), retry: applyForContinuMail });
      });
  }, [plan]);

  // "none" state (and the secondary link in single/multiple): create → refresh the
  // profile list → only then apply. If the refresh doesn't show ContinuMail, stop with
  // a state error rather than blindly applying to a profile we can't confirm exists.
  const runCreateFlow = useCallback(() => {
    setPhase({ k: "creating" });
    outlookProfilesCreate(CONTINUMAIL_PROFILE)
      .then(() => outlookProfilesList())
      .then((refreshed) => {
        if (!mounted.current) return;
        if (!refreshed.profiles.includes(CONTINUMAIL_PROFILE)) {
          setPhase({
            k: "error",
            message: "Couldn't confirm the ContinuMail profile was created. Try again, or create one manually in Outlook.",
            retry: runCreateFlow,
          });
          return;
        }
        setProfiles(refreshed);
        applyForContinuMail();
      })
      .catch((e) => {
        if (!mounted.current) return;
        const stage = e instanceof ProfileStageError ? e.stage : undefined;
        setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), stage, retry: runCreateFlow });
      });
  }, [applyForContinuMail]);

  const handleOpen = useCallback(() => {
    setOpening(true);
    openInOutlook(CONTINUMAIL_PROFILE)
      .catch(() => { /* best-effort: opening Outlook doesn't gate anything past this point */ })
      .then(() => { if (mounted.current) { setOpening(false); setOpened(true); } });
  }, []);

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
        {phase.k === "creating" && (
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2 text-muted-foreground"><Spinner size="sm" /> Creating your ContinuMail viewing profile…</div>
            <Button variant="outline" onClick={dismiss}>Hide</Button>
          </div>
        )}

        {phase.k === "applying" && (
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2 text-muted-foreground"><Spinner size="sm" /> Importing… Outlook opens briefly to save the categories.</div>
            <Button variant="outline" onClick={dismiss}>Hide</Button>
          </div>
        )}

        {phase.k === "applied" && phase.viaCreate && (
          <div>
            <div className="flex items-start gap-2 text-foreground">
              <Check className="mt-0.5 size-4 shrink-0 text-primary" />
              <span>Colours imported to your new ContinuMail profile. Added {phase.added}, {phase.existing} already existed.</span>
            </div>
            <div className="mt-2 text-xs text-muted-foreground">
              In Outlook, choose File → Open &amp; Export → Open Outlook Data File and pick your converted .pst to see the coloured folders.
            </div>
            <div className="mt-3 flex gap-2.5">
              <Button onClick={handleOpen} disabled={opening}>{opening ? "Opening…" : opened ? "Open in Outlook again" : "Open in Outlook"}</Button>
              <Button variant="outline" onClick={dismiss}>Done</Button>
            </div>
          </div>
        )}

        {phase.k === "applied" && !phase.viaCreate && (
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
              <TriangleAlert className="mt-0.5 size-4 shrink-0" /> <span>{errorText(phase.message, phase.stage)}</span>
            </div>
            <div className="mt-3 flex gap-2.5">
              <Button onClick={phase.retry}>Retry</Button>
              <Button variant="outline" onClick={dismiss}>Skip</Button>
            </div>
          </div>
        )}

        {phase.k === "ready" && (
          <div>
            <p className="mb-2 text-xs text-muted-foreground">
              Your tags became Outlook categories, but Outlook colours them from its own master list. Import your Thunderbird tag colours so they match.
            </p>
            <div className="flex flex-col gap-1">
              {plan.map((c) => (
                <div key={c.name} className="flex items-center gap-2.5 text-[13px]">
                  <span className="size-3.5 shrink-0 rounded border border-black/10" style={{ background: c.hex ?? "transparent" }} />
                  <span className="flex-1 text-foreground">{c.name}</span>
                  <span className="rounded-full bg-muted px-2 py-0.5 text-[10px] text-muted-foreground">{ACTION_LABEL[c.action] ?? c.action}</span>
                </div>
              ))}
            </div>

            {wouldAdd.length === 0 ? (
              <div className="mt-3 text-xs text-light-gray">Nothing to import — no new Outlook colours are available from this profile.</div>
            ) : (
              <>
                <div className="mt-3 flex items-start gap-2 rounded-lg border border-[#ecd9a8] bg-[#fbf2dd] px-3 py-2 text-[11.5px] text-[#8a6516]">
                  <TriangleAlert className="mt-0.5 size-4 shrink-0" />
                  <span>This adds these categories to Outlook's master list for <strong>all</strong> your Outlook accounts — not just this PST. <strong>Close Outlook before importing.</strong></span>
                </div>

                {state === "multiple" && (
                  <div className="mt-2.5 flex items-center gap-2 text-xs text-foreground">
                    <label htmlFor="colour-import-profile">Outlook profile:</label>
                    <select
                      id="colour-import-profile"
                      className="rounded-md border border-border bg-background px-1.5 py-1 text-xs"
                      value={selectedProfile}
                      onChange={(e) => setSelectedProfile(e.target.value)}
                    >
                      {profiles?.profiles.map((name) => <option key={name} value={name}>{name}</option>)}
                    </select>
                  </div>
                )}

                <label className="mt-2.5 flex items-center gap-2 text-xs text-foreground">
                  <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
                  I understand this changes my Outlook categories
                </label>
              </>
            )}

            <div className="mt-3 flex items-center gap-2.5">
              {wouldAdd.length > 0 && state === "none" && (
                <Button disabled={!consent} onClick={runCreateFlow}>Create ContinuMail viewing profile</Button>
              )}
              {wouldAdd.length > 0 && state !== "none" && (
                <Button
                  disabled={!consent}
                  onClick={() => runApply(state === "multiple" ? selectedProfile : profiles?.profiles[0])}
                >
                  Import colours to Outlook
                </Button>
              )}
              <Button variant="outline" onClick={dismiss}>Skip</Button>
            </div>

            {wouldAdd.length > 0 && (state === "single" || state === "multiple") && (
              <Button variant="link" size="sm" className="mt-1 h-auto p-0 text-[11.5px]" onClick={runCreateFlow}>
                Or create a separate ContinuMail viewing profile
              </Button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
