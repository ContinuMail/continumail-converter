// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, it, expect } from "vitest";
import { canScan } from "./outputTarget";

describe("canScan", () => {
  it("files mode requires a pstFile target + files", () => {
    expect(canScan("files", ["a.mbox"], null, { kind: "pstFile", path: "o.pst" })).toBe(true);
    expect(canScan("files", ["a.mbox"], null, null)).toBe(false);
    expect(canScan("files", [], null, { kind: "pstFile", path: "o.pst" })).toBe(false);
  });
  it("profile mode requires only a profile root (output chosen later)", () => {
    expect(canScan("profile", [], "/p", null)).toBe(true);
    expect(canScan("profile", [], null, null)).toBe(false);
  });
  it("folder-tree mode requires a completed nonempty discovery and a pstFile target", () => {
    const discovered = { sources: [{ path: "C:/export/Inbox", targetFolderPath: ["Inbox"] }] };
    expect(canScan("folderTree", [], "C:/export", { kind: "pstFile", path: "C:/out/Mail.pst" }, discovered, false)).toBe(true);
    expect(canScan("folderTree", [], "C:/export", { kind: "pstFile", path: "C:/out/Mail.pst" }, discovered, true)).toBe(false);
    expect(canScan("folderTree", [], "C:/export", { kind: "pstFile", path: "C:/out/Mail.pst" }, { sources: [] }, false)).toBe(false);
    expect(canScan("folderTree", [], "C:/export", null, discovered, false)).toBe(false);
  });
});
