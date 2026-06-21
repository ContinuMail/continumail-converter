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
    /// <summary>Add a category with the given OlCategoryColor integer (1-25).</summary>
    void Add(string name, int outlookColorIndex);
}
