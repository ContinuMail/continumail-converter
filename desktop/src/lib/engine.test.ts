// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect, vi, beforeEach } from "vitest";

// engine.ts imports Tauri runtime modules at load; stub them so this
// test needs no Tauri host.
vi.mock("@tauri-apps/api/core", () => ({ invoke: vi.fn() }));
vi.mock("@tauri-apps/api/event", () => ({ listen: vi.fn() }));
vi.mock("@tauri-apps/plugin-dialog", () => ({ open: vi.fn(), save: vi.fn() }));

import { invoke } from "@tauri-apps/api/core";
import {
  buildStartConvertPayload,
  startConvert,
  applyColoursPlan,
  outlookProfilesList,
  outlookProfilesCreate,
  openInOutlook,
} from "./engine";
import type { ConversionConfig, ColourPlanEntry } from "./types";

const config = { outputs: [] } as unknown as ConversionConfig;
const invokeMock = vi.mocked(invoke);

describe("buildStartConvertPayload", () => {
  it("omits expectedTotal when undefined", () => {
    const p = buildStartConvertPayload(config, "C:/out", undefined);
    expect(p).toEqual({ config, outputDir: "C:/out" });
    expect("expectedTotal" in p).toBe(false);
  });

  it("includes expectedTotal when provided (including 0)", () => {
    expect(buildStartConvertPayload(config, "C:/out", 123)).toEqual({ config, outputDir: "C:/out", expectedTotal: 123 });
    expect(buildStartConvertPayload(config, "C:/out", 0)).toEqual({ config, outputDir: "C:/out", expectedTotal: 0 });
  });
});

// Guard the actual seam: startConvert must hand the conditional payload to invoke.
describe("startConvert", () => {
  beforeEach(() => invokeMock.mockReset());

  it("invokes start_convert with expectedTotal when provided", async () => {
    invokeMock.mockResolvedValueOnce(undefined);
    await startConvert(config, "C:/out", 123);
    expect(invokeMock).toHaveBeenCalledWith("start_convert", { config, outputDir: "C:/out", expectedTotal: 123 });
  });

  it("omits expectedTotal from the invoke payload when undefined", async () => {
    invokeMock.mockResolvedValueOnce(undefined);
    await startConvert(config, "C:/out");
    expect(invokeMock).toHaveBeenCalledWith("start_convert", { config, outputDir: "C:/out" });
  });
});

describe("applyColoursPlan", () => {
  beforeEach(() => invokeMock.mockReset());
  const plan: ColourPlanEntry[] = [{ name: "Work", hex: "#FF9900", outlookColor: 2, action: "would-add" }];
  const okStdout = JSON.stringify({ type: "importColours", mode: "apply", outlookAvailable: true, categories: [] });

  it("omits outlookProfile from the invoke payload when undefined", async () => {
    invokeMock.mockResolvedValueOnce(okStdout);
    await applyColoursPlan(plan);
    expect(invokeMock).toHaveBeenCalledWith("apply_colours_plan", { plan });
  });

  it("passes outlookProfile through when provided", async () => {
    invokeMock.mockResolvedValueOnce(okStdout);
    await applyColoursPlan(plan, "ContinuMail");
    expect(invokeMock).toHaveBeenCalledWith("apply_colours_plan", { plan, outlookProfile: "ContinuMail" });
  });
});

describe("outlookProfilesList", () => {
  beforeEach(() => invokeMock.mockReset());

  it("invokes outlook_profiles_list and parses the result", async () => {
    invokeMock.mockResolvedValueOnce(JSON.stringify({
      type: "outlookProfiles", classicOutlook: true, defaultProfile: "Outlook", profiles: ["Outlook"],
    }));
    const r = await outlookProfilesList();
    expect(invokeMock).toHaveBeenCalledWith("outlook_profiles_list");
    expect(r).toEqual({ classicOutlook: true, defaultProfile: "Outlook", profiles: ["Outlook"] });
  });
});

describe("outlookProfilesCreate", () => {
  beforeEach(() => invokeMock.mockReset());

  it("invokes outlook_profiles_create with the name and parses the result", async () => {
    invokeMock.mockResolvedValueOnce(JSON.stringify({ type: "outlookProfileCreate", name: "ContinuMail", created: true, reused: false }));
    const r = await outlookProfilesCreate("ContinuMail");
    expect(invokeMock).toHaveBeenCalledWith("outlook_profiles_create", { name: "ContinuMail" });
    expect(r).toEqual({ name: "ContinuMail", created: true, reused: false });
  });

  it("rejects with a ProfileStageError on a stage-tagged failure", async () => {
    invokeMock.mockResolvedValueOnce(JSON.stringify({ type: "error", stage: "pim-unsupported", message: "nope", fatal: true }));
    await expect(outlookProfilesCreate("ContinuMail")).rejects.toMatchObject({ stage: "pim-unsupported" });
  });
});

describe("openInOutlook", () => {
  beforeEach(() => invokeMock.mockReset());

  it("invokes open_in_outlook with the name", async () => {
    invokeMock.mockResolvedValueOnce(JSON.stringify({ type: "outlookProfileOpen", name: "ContinuMail", launched: true }));
    await openInOutlook("ContinuMail");
    expect(invokeMock).toHaveBeenCalledWith("open_in_outlook", { name: "ContinuMail" });
  });

  // run_sidecar_capture surfaces a handled (nonzero-exit) open failure as `Ok(stdout)` carrying
  // a structured `{type:"error"}` object — invoke() itself resolves, so openInOutlook must inspect
  // the payload and reject rather than silently treating it as a launch.
  it("rejects with a ProfileStageError on a stage-tagged failure", async () => {
    invokeMock.mockResolvedValueOnce(JSON.stringify({
      type: "error", stage: "unknown-outlook-profile", message: "Outlook profile 'Stale' not found.", fatal: true,
    }));
    await expect(openInOutlook("Stale")).rejects.toMatchObject({ stage: "unknown-outlook-profile" });
  });
});
