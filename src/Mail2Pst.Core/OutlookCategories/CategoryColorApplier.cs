// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
namespace Mail2Pst.Core.OutlookCategories;

/// <summary>Applies a candidate plan to an IOutlookCategoryStore: would-add becomes added (store.Add) or
/// skipped-existing (name already present, case-insensitive, never overwritten); already-skipped candidates
/// pass through unchanged. Pure orchestration — no Outlook types.</summary>
public static class CategoryColorApplier
{
    public static IReadOnlyList<CategoryCandidate> Apply(
        IReadOnlyList<CategoryCandidate> plan, IOutlookCategoryStore store)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(store);

        IReadOnlySet<string> existing = store.ExistingNames();
        var results = new List<CategoryCandidate>(plan.Count);
        foreach (CategoryCandidate c in plan)
        {
            if (c.Action != "would-add") { results.Add(c); continue; }
            if (existing.Contains(c.Name)) { results.Add(c with { Action = "skipped-existing" }); continue; }
            store.Add(c.Name, c.OutlookColor!.Value);
            results.Add(c with { Action = "added" });
        }
        return results;
    }
}
