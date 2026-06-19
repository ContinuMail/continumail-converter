// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.Config;

/// <summary>How .msf junk (junkscore >= 50) is emitted into the PST. Folder is modeled but
/// not accepted until SP4 (folder routing is a pipeline concern).</summary>
public enum JunkHandlingMode { Off, Category, Folder }
