// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { describe, expect, it } from "vitest";
import { fileTabClickMode, isDiscoveryBackedMode, isFilesTabMode, isProfileTabMode } from "./inputMode";

describe("input-mode predicates", () => {
  it("keeps folder trees on the files tab while treating them as discovery-backed", () => {
    expect(isFilesTabMode("folderTree")).toBe(true);
    expect(isProfileTabMode("folderTree")).toBe(false);
    expect(isDiscoveryBackedMode("folderTree")).toBe(true);
  });

  it("keeps installed profiles as the only profile-tab mode", () => {
    expect(isProfileTabMode("profile")).toBe(true);
    expect(isFilesTabMode("profile")).toBe(false);
    expect(isDiscoveryBackedMode("profile")).toBe(true);
    expect(isDiscoveryBackedMode("files")).toBe(false);
  });

  it("does not silently leave folder-tree mode when the highlighted files tab is clicked", () => {
    expect(fileTabClickMode("profile")).toBe("files");
    expect(fileTabClickMode("files")).toBe("files");
    expect(fileTabClickMode("folderTree")).toBe("folderTree");
  });
});
