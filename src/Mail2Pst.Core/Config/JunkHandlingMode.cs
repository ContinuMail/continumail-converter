// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.Config;

/// <summary>How .msf junk (junkscore &gt;= 50) is emitted into the PST:
/// Off = derive IsJunk but emit nothing; Category = add a synthetic "Junk" category;
/// Folder = route junk messages into a top-level "Junk Email" folder.</summary>
public enum JunkHandlingMode { Off, Category, Folder }
