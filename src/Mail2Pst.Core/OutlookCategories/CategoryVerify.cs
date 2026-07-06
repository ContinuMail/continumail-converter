// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>Pure verification helpers for the add-only colour apply: which categories we expected to
/// add, and which of those are missing (absent OR wrong colour) from a read-back. No COM/Outlook types.</summary>
public static class CategoryVerify
{
    public static IReadOnlyDictionary<string, int> ExpectedAdded(IReadOnlyList<CategoryCandidate> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (CategoryCandidate c in results)
            if (c.Action == "added" && c.OutlookColor is int colour)
                map[c.Name] = colour;
        return map;
    }

    public static void MergeAdded(IDictionary<string, int> accumulator, IReadOnlyList<CategoryCandidate> results)
    {
        ArgumentNullException.ThrowIfNull(accumulator);
        foreach ((string name, int colour) in ExpectedAdded(results))
            accumulator[name] = colour;
    }

    public static IReadOnlyList<string> Missing(
        IReadOnlyDictionary<string, int> expected, IReadOnlyDictionary<string, int> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        // Normalize actual to case-insensitive regardless of its incoming comparer (a plain
        // Dictionary<string,int> from a caller would otherwise miss "WORK" vs "Work").
        var actualCi = new Dictionary<string, int>(actual, StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach ((string name, int colour) in expected)
            if (!actualCi.TryGetValue(name, out int got) || got != colour)
                missing.Add(name);
        return missing;
    }
}
