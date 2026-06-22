// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { buildSteps, stepIndexForStage } from "./steps";

describe("buildSteps", () => {
  it("files mode = 5 steps, no Accounts", () => {
    expect(buildSteps("files", 0)).toEqual(["Source", "Review", "Options", "Convert", "Done"]);
  });
  it("profile single-account = 5 steps, no Accounts", () => {
    expect(buildSteps("profile", 1)).toEqual(["Source", "Review", "Options", "Convert", "Done"]);
  });
  it("profile multi-account inserts Accounts after Source", () => {
    expect(buildSteps("profile", 3)).toEqual(["Source", "Accounts", "Review", "Options", "Convert", "Done"]);
  });
});

describe("stepIndexForStage", () => {
  it("maps review to its index in each list", () => {
    expect(stepIndexForStage(buildSteps("files", 0), "review")).toBe(1);
    expect(stepIndexForStage(buildSteps("profile", 3), "review")).toBe(2);
    expect(stepIndexForStage(buildSteps("profile", 3), "accounts")).toBe(1);
  });
});
