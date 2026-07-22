// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

/** The source selection paths supported by the desktop flow. */
export type InputMode = "files" | "profile" | "folderTree";

export function isDiscoveryBackedMode(mode: InputMode): boolean {
  return mode === "profile" || mode === "folderTree";
}

export function isProfileTabMode(mode: InputMode): boolean {
  return mode === "profile";
}

export function isFilesTabMode(mode: InputMode): boolean {
  return mode === "files" || mode === "folderTree";
}

/** The highlighted file tab represents both ordinary files and a folder tree. */
export function fileTabClickMode(mode: InputMode): InputMode {
  return mode === "profile" ? "files" : mode;
}
