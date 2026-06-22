// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

export type StepList = string[];

export function buildSteps(inputMode: "files" | "profile", accountCount: number): StepList {
  const multi = inputMode === "profile" && accountCount >= 2;
  return multi
    ? ["Source", "Accounts", "Review", "Options", "Convert", "Done"]
    : ["Source", "Review", "Options", "Convert", "Done"];
}

// stage names used by useScan; "accounts" only present in multi-account lists
const STAGE_TO_LABEL: Record<string, string> = {
  select: "Source",
  scanning: "Review",
  review: "Review",
  scanError: "Review",
  accounts: "Accounts",
  options: "Options",
};
const PHASE_TO_LABEL: Record<string, string> = {
  running: "Convert",
  done: "Done",
  error: "Done",
  cancelled: "Done",
};

export function stepIndexForStage(steps: StepList, stage: string): number {
  return Math.max(0, steps.indexOf(STAGE_TO_LABEL[stage] ?? "Source"));
}

export function stepIndexForPhase(steps: StepList, phase: string): number {
  return Math.max(0, steps.indexOf(PHASE_TO_LABEL[phase] ?? "Convert"));
}
