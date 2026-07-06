// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from "react";
import { Palette, TriangleAlert, Check } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { applyColoursPlan, outlookProfilesList, outlookProfilesCreate, openInOutlook } from "@/lib/engine";
import { summarizeColourApply, validateProfileName, ProfileStageError, type OutlookProfiles } from "@/lib/colourImport";
import type { ColourPlanEntry } from "@/lib/types";

// Default name offered when creating a fresh viewing profile (user can rename it).
const DEFAULT_NEW_PROFILE = "ContinuMail";

type Phase =
  | { k: "ready" }
  | { k: "creating"; name: string }
  | { k: "applying"; viaCreate?: boolean }
  // `profileName` (create path only) is the registry-cased profile we applied to, so the
  // success "Open in Outlook" button opens exactly that profile.
  | { k: "applied"; added: number; existing: number; viaCreate?: boolean; profileName?: string }
  // `retry` is bound to the exact step that failed so retrying never re-runs a succeeded step.
  | { k: "error"; message: string; stage?: string; retry: () => void }
  | { k: "dismissed" };

const ACTION_LABEL: Record<string, string> = {
  "would-add": "will add",
  "added": "added",
  "skipped-existing": "already in Outlook",
  "skipped-no-colour": "no colour",
  "skipped-invalid-name": "invalid name",
};

// Stage-tagged errors carry curated CLI copy — shown as-is; plain-apply errors get heuristics.
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
  // Outlook profile listing: null while loading (or if the listing call failed) — in that case
  // the card shows the create-new path only (the safe default; we don't know existing profiles).
  const [profiles, setProfiles] = useState<OutlookProfiles | null>(null);
  const [mode, setMode] = useState<"new" | "existing">("new"); // recommended default: create new
  const [newName, setNewName] = useState(DEFAULT_NEW_PROFILE);
  const [selectedProfile, setSelectedProfile] = useState(""); // "existing" + multiple profiles
  const [opening, setOpening] = useState(false);
  const [opened, setOpened] = useState(false);
  // Guard against setState after unmount (user clicks "Convert another" mid-apply).
  // Reset to true on (re)mount: in React 18 StrictMode the effect runs setup→cleanup→setup on the
  // same instance, so a cleanup-only version would leave mounted.current=false forever and silently
  // swallow every async result (froze the create flow on "Creating…").
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    outlookProfilesList()
      .then((p) => {
        if (!mounted.current) return;
        setProfiles(p);
        setSelectedProfile(p.defaultProfile ?? p.profiles[0] ?? "");
      })
      .catch(() => { /* listing failed — the create-new path still works */ });
  }, []);

  const noClassicOutlook = profiles?.classicOutlook === false;
  const hasExisting = (profiles?.profiles.length ?? 0) > 0;
  const effectiveMode: "new" | "existing" = hasExisting ? mode : "new";
  const nameError = validateProfileName(newName);

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

  // Apply the plan to a just-created profile (registry-cased name). Split out so a failure here
  // retries just this step, not the whole create→refresh chain.
  const applyToCreated = useCallback((profileName: string) => {
    setPhase({ k: "applying", viaCreate: true });
    applyColoursPlan(plan, profileName)
      .then((r) => {
        if (!mounted.current) return;
        if (r.kind === "error") { setPhase({ k: "error", message: r.message, retry: () => applyToCreated(profileName) }); return; }
        const s = summarizeColourApply(r.categories);
        setPhase({ k: "applied", added: s.added, existing: s.existing, viaCreate: true, profileName });
      })
      .catch((e) => {
        if (!mounted.current) return;
        setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), retry: () => applyToCreated(profileName) });
      });
  }, [plan]);

  // Create-new flow: create <newName> → refresh list → case-insensitive containment guard →
  // apply to the matched (registry-cased) name. The registry/resolver match case-insensitively,
  // so the guard must too (a case-sensitive check would loop forever on a differently-cased match).
  const runCreateFlow = useCallback(() => {
    const name = newName.trim();
    setPhase({ k: "creating", name });
    outlookProfilesCreate(name)
      .then(() => outlookProfilesList())
      .then((refreshed) => {
        if (!mounted.current) return;
        const matched = refreshed.profiles.find((p) => p.toLowerCase() === name.toLowerCase());
        if (!matched) {
          setPhase({ k: "error", message: `Couldn't confirm the "${name}" profile was created. Try again, or create one manually in Outlook.`, retry: runCreateFlow });
          return;
        }
        setProfiles(refreshed);
        applyToCreated(matched);
      })
      .catch((e) => {
        if (!mounted.current) return;
        const stage = e instanceof ProfileStageError ? e.stage : undefined;
        setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), stage, retry: runCreateFlow });
      });
  }, [newName, applyToCreated]);

  // open goes through run_sidecar_capture too, so a handled failure throws a ProfileStageError —
  // surface it instead of silently claiming "opened".
  const handleOpen = useCallback((profileName: string) => {
    setOpening(true);
    openInOutlook(profileName)
      .then(() => { if (mounted.current) { setOpening(false); setOpened(true); } })
      .catch((e) => {
        if (!mounted.current) return;
        setOpening(false);
        const stage = e instanceof ProfileStageError ? e.stage : undefined;
        setPhase({ k: "error", message: e instanceof Error ? e.message : String(e), stage, retry: () => handleOpen(profileName) });
      });
  }, []);

  if (phase.k === "dismissed") return null;
  const dismiss = () => setPhase({ k: "dismissed" });

  const startImport = () => {
    if (effectiveMode === "new") runCreateFlow();
    else runApply(profiles && profiles.profiles.length === 1 ? profiles.profiles[0] : selectedProfile);
  };
  const importDisabled = effectiveMode === "new" ? Boolean(nameError) : !consent;

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
            <div className="flex items-center gap-2 text-muted-foreground"><Spinner size="sm" /> Creating your “{phase.name}” profile…</div>
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
              <span>Colours imported to your new “{phase.profileName}” profile. Added {phase.added}, {phase.existing} already existed.</span>
            </div>
            <div className="mt-2 text-xs text-muted-foreground">
              In Outlook, choose File → Open &amp; Export → Open Outlook Data File and pick your converted .pst to see the coloured folders.
            </div>
            <div className="mt-3 flex gap-2.5">
              <Button onClick={() => handleOpen(phase.profileName ?? DEFAULT_NEW_PROFILE)} disabled={opening}>{opening ? "Opening…" : opened ? "Open in Outlook again" : "Open in Outlook"}</Button>
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
            ) : noClassicOutlook ? (
              <div className="mt-3 text-xs text-light-gray">Classic Outlook isn't installed on this machine, so colours can't be applied here.</div>
            ) : (
              <>
                <div className="mt-3 text-[10.5px] font-medium uppercase tracking-wide text-muted-foreground">Where should the colours go?</div>

                {hasExisting && (
                  <div className="mt-1.5 flex gap-0.5 rounded-[9px] border border-border bg-muted/60 p-0.5">
                    <button type="button" onClick={() => setMode("new")}
                      className={`flex-1 rounded-[7px] px-2 py-1.5 text-xs font-medium transition ${effectiveMode === "new" ? "bg-primary text-primary-foreground" : "text-muted-foreground"}`}>
                      Create new profile
                    </button>
                    <button type="button" onClick={() => setMode("existing")}
                      className={`flex-1 rounded-[7px] px-2 py-1.5 text-xs font-medium transition ${effectiveMode === "existing" ? "bg-primary text-primary-foreground" : "text-muted-foreground"}`}>
                      Use existing
                    </button>
                  </div>
                )}

                {effectiveMode === "new" ? (
                  <div>
                    <input type="text" value={newName} onChange={(e) => setNewName(e.target.value)} spellCheck={false}
                      aria-label="New profile name" placeholder="Profile name"
                      className="mt-2 w-full rounded-lg border border-border bg-background px-2.5 py-2 text-[13px] text-foreground" />
                    {nameError && <div className="mt-1 text-[11px] text-destructive">{nameError}</div>}
                    <div className="mt-2.5 flex items-start gap-2 rounded-lg border border-[#cfe6d0] bg-[#f2f9f2] px-3 py-2 text-[11.5px] text-[#3f6b45]">
                      <Check className="mt-0.5 size-4 shrink-0" />
                      <span>A separate profile keeps your everyday Outlook categories untouched. Outlook opens briefly to save the colours.</span>
                    </div>
                  </div>
                ) : (
                  <div>
                    {profiles && profiles.profiles.length > 1 ? (
                      <div className="mt-2 flex items-center gap-2 text-xs text-foreground">
                        <label htmlFor="colour-import-profile">Profile:</label>
                        <select id="colour-import-profile" value={selectedProfile} onChange={(e) => setSelectedProfile(e.target.value)}
                          className="rounded-md border border-border bg-background px-1.5 py-1 text-xs">
                          {profiles.profiles.map((name) => <option key={name} value={name}>{name}</option>)}
                        </select>
                      </div>
                    ) : (
                      <div className="mt-2 text-xs text-foreground">Profile: <strong>{profiles?.profiles[0]}</strong></div>
                    )}
                    <div className="mt-2.5 flex items-start gap-2 rounded-lg border border-[#ecd9a8] bg-[#fbf2dd] px-3 py-2 text-[11.5px] text-[#8a6516]">
                      <TriangleAlert className="mt-0.5 size-4 shrink-0" />
                      <span>This adds the colours to Outlook's master category list for <strong>every account</strong> in this profile — not just this PST. <strong>Close Outlook before importing.</strong></span>
                    </div>
                    <label className="mt-2.5 flex items-center gap-2 text-xs text-foreground">
                      <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
                      I understand this changes my Outlook categories
                    </label>
                  </div>
                )}

                <div className="mt-3 flex items-center gap-2.5">
                  <Button disabled={importDisabled} onClick={startImport}>
                    {effectiveMode === "new" ? "Create & import colours" : "Import colours"}
                  </Button>
                  <Button variant="outline" onClick={dismiss}>Skip</Button>
                </div>
              </>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
