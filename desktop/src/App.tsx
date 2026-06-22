// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useCallback, useState } from "react";
import { Shell } from "@/components/Shell";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { SelectView } from "@/components/views/SelectView";
import { ScanningView } from "@/components/views/ScanningView";
import { ReviewView } from "@/components/views/ReviewView";
import { OptionsView } from "@/components/views/OptionsView";
import { ProfileOptionsView } from "@/components/views/ProfileOptionsView";
import { AccountsView } from "@/components/views/AccountsView";
import type { ProfileSourceRow } from "@/lib/types";
import { ScanErrorView } from "@/components/views/ScanErrorView";
import { ConvertView } from "@/components/views/ConvertView";
import { DoneView } from "@/components/views/DoneView";
import { ErrorView } from "@/components/views/ErrorView";
import { CancelledView } from "@/components/views/CancelledView";
import { useConvert } from "@/lib/useConvert";
import { useScan } from "@/lib/useScan";
import { buildSteps, stepIndexForStage, stepIndexForPhase } from "@/lib/steps";

export default function App() {
  const { state: conv, start, reset } = useConvert();
  const flow = useScan();
  const { state: f } = flow;
  const [confirmSource, setConfirmSource] = useState(false);

  const steps = buildSteps(f.inputMode, flow.discoveredAccountCount);

  // Memoized so ConfirmDialog's Esc-key effect (deps on onCancel) doesn't
  // re-register its keydown listener on every App render.
  const dismissConfirm = useCallback(() => setConfirmSource(false), []);
  const confirmGoToSource = useCallback(() => {
    setConfirmSource(false);
    flow.back();
  }, [flow]);

  if (conv.phase !== "idle") {
    const onConvertAnother = () => {
      reset();
      flow.resetFlow();
    };
    return (
      <Shell steps={steps} currentStep={stepIndexForPhase(steps, conv.phase)}>
        {conv.phase === "running" && <ConvertView state={conv} />}
        {conv.phase === "done" && (
          <DoneView
            state={conv}
            profileRoot={f.inputMode === "profile" ? f.profileRoot : null}
            onConvertAnother={onConvertAnother}
          />
        )}
        {conv.phase === "error" && <ErrorView state={conv} onConvertAnother={onConvertAnother} />}
        {conv.phase === "cancelled" && <CancelledView state={conv} onConvertAnother={onConvertAnother} />}
      </Shell>
    );
  }

  // Going back to Source discards the scan (forces a rescan), so confirm first
  // whenever a scan exists. Review/Options jumps keep the scan and stay silent.
  const requestGoToSource = () => {
    if (f.scan) setConfirmSource(true);
    else flow.back();
  };

  // Clickable stepper (backward only, pre-convert stable stages). Step 0 → Source
  // (guarded), step 1 → Accounts (multi) or Review (single), step 2 → Review
  // (multi only). Not offered during scanning/scanError (transient) so the stepper
  // can't bail an in-flight scan.
  const isMultiAccount = flow.discoveredAccountCount >= 2 && f.inputMode === "profile";
  const onStepSelect =
    f.stage === "accounts" || f.stage === "review" || f.stage === "options"
      ? (step: number) => {
          if (step === 0) requestGoToSource();
          else if (isMultiAccount && step === 1 && f.stage !== "accounts") {
            flow.backToAccounts();
          } else if (step === (isMultiAccount ? 2 : 1)) flow.backToReview();
        }
      : undefined;

  return (
    <>
    <Shell steps={steps} currentStep={stepIndexForStage(steps, f.stage)} onStepSelect={onStepSelect}>
      {f.stage === "select" && (
        <SelectView
          files={f.inputFiles}
          outputTarget={f.outputTarget}
          inputMode={f.inputMode}
          profileRoot={f.profileRoot}
          sourceError={f.sourceError}
          onFilesChange={flow.setInputFiles}
          onOutputTargetChange={flow.setOutputTarget}
          onInputModeChange={flow.setInputMode}
          onProfileRootChange={flow.setProfileRoot}
          onContinue={flow.continueToScan}
        />
      )}
      {f.stage === "scanning" && <ScanningView fileCount={f.inputFiles.length} progress={f.scanProgress} />}
      {f.stage === "accounts" && f.inputMode === "profile" && (
        <AccountsView
          rows={f.profileRows}
          accounts={f.accounts}
          selectedAccountKeys={f.selectedAccountKeys}
          pstNames={f.pstNames}
          outputTarget={f.outputTarget}
          onOutputTargetChange={flow.setOutputTarget}
          onToggleAccount={flow.toggleAccount}
          onSetPstName={flow.setPstName}
          onBack={requestGoToSource}
          onContinue={flow.continueToReviewFromAccounts}
        />
      )}
      {f.stage === "review" && f.scan && (
        <ReviewView
          sources={flow.sortedSources}
          checkedIds={f.checkedIds}
          skipEmpty={f.skipEmpty}
          sortBy={f.sortBy}
          sortDir={f.sortDir}
          onSortChange={flow.setSort}
          onToggle={flow.toggleChecked}
          onSkipEmptyChange={flow.setSkipEmpty}
          onContinue={flow.continueToOptions}
          onBack={isMultiAccount ? flow.backToAccounts : requestGoToSource}
          pairedIds={f.inputMode === "profile" ? flow.pairedIds : undefined}
          warnings={f.inputMode === "profile" ? f.discoverWarnings : undefined}
        />
      )}
      {f.stage === "options" && (
        f.inputMode === "profile" && f.profileRoot ? (
          <ProfileOptionsView
            rows={flow.sortedSources as ProfileSourceRow[]}
            outputTarget={f.outputTarget}
            onOutputTargetChange={flow.setOutputTarget}
            profileRoot={f.profileRoot}
            checkedIds={f.checkedIds}
            skipEmpty={f.skipEmpty}
            options={f.options}
            onSetOptions={flow.setOptions}
            onStart={start}
            onBack={flow.backToReview}
            accounts={f.accounts}
            selectedAccountKeys={f.selectedAccountKeys}
            pstNames={f.pstNames}
          />
        ) : f.scan && f.outputTarget?.kind === "pstFile" ? (
          <OptionsView
            scan={f.scan}
            previewSources={flow.sortedSources}
            outputPath={f.outputTarget.path}
            checkedIds={f.checkedIds}
            skipEmpty={f.skipEmpty}
            options={f.options}
            onSetOptions={flow.setOptions}
            onSetRename={flow.setRename}
            onStart={start}
            onBack={flow.backToReview}
          />
        ) : null
      )}
      {f.stage === "scanError" && (
        <ScanErrorView message={f.errorMessage ?? "Unknown error"} onBack={flow.back} />
      )}
    </Shell>
    <ConfirmDialog
      open={confirmSource}
      title="Rescan needed"
      message="Going back to Source will discard this scan — you'll have to rescan the source."
      confirmLabel="Go back"
      cancelLabel="Stay"
      onConfirm={confirmGoToSource}
      onCancel={dismissConfirm}
    />
    </>
  );
}
