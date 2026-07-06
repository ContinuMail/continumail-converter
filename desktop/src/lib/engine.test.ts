// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect, vi, beforeEach } from "vitest";

// engine.ts imports Tauri runtime modules at load; stub them so this
// test needs no Tauri host.
vi.mock("@tauri-apps/api/core", () => ({ invoke: vi.fn() }));
vi.mock("@tauri-apps/api/event", () => ({ listen: vi.fn() }));
vi.mock("@tauri-apps/plugin-dialog", () => ({ open: vi.fn(), save: vi.fn() }));

import { invoke } from "@tauri-apps/api/core";
import { buildStartConvertPayload, startConvert } from "./engine";
import type { ConversionConfig } from "./types";

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
