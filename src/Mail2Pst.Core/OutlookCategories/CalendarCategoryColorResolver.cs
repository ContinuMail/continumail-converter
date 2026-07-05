// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>Resolves each calendar/task category name to its Thunderbird colour: a prefs.js override
/// (keyed by the mangled name) if present, else the computed hashColor default. Insertion-ordered,
/// case-insensitively de-duplicated (first occurrence casing kept).</summary>
public static class CalendarCategoryColorResolver
{
    public static IReadOnlyDictionary<string, string> Resolve(
        IEnumerable<string> categoryNames,
        IReadOnlyDictionary<string, string> overridesByMangledKey)
    {
        ArgumentNullException.ThrowIfNull(categoryNames);
        ArgumentNullException.ThrowIfNull(overridesByMangledKey);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in categoryNames)
        {
            if (string.IsNullOrEmpty(name) || result.ContainsKey(name)) continue;
            string key = CategoryColorHasher.FormatStringForCSSRule(name);
            string hex = overridesByMangledKey.TryGetValue(key, out string? o) ? o
                : CategoryColorHasher.HashColor(name);
            result[name] = hex;
        }
        return result;
    }
}
