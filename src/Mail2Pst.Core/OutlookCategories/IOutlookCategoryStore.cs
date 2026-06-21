// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
namespace Mail2Pst.Core.OutlookCategories;

/// <summary>Abstraction over the Outlook master category list. No MS/COM types — pure string/int — so Core
/// stays Outlook-free; the only implementation is the CLI's late-bound COM shim.</summary>
public interface IOutlookCategoryStore
{
    /// <summary>Existing master-list category names, compared case-insensitively (OrdinalIgnoreCase).</summary>
    IReadOnlySet<string> ExistingNames();
    /// <summary>Buffer a category with the given OlCategoryColor integer (1-25) for addition. Not persisted
    /// until <see cref="Commit"/> is called — the master list must be written in a single atomic operation
    /// (Outlook commits per-add writes lazily and racily, losing all but the first).</summary>
    void Add(string name, int outlookColorIndex);
    /// <summary>Atomically persist all buffered <see cref="Add"/>s. No-op when nothing was buffered.</summary>
    void Commit();
}
