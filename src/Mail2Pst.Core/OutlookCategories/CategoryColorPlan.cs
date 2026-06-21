// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Msf;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// Builds the colour-import candidate list from a profile's prefs.js tag names + colours. Candidate set =
/// the five built-in $labelN keys plus every key seen in names/colours. Name resolution matches the E4
/// resolver (prefs name -> built-in $labelN default -> key). Colour = prefs .color -> built-in default for
/// $labelN -> none. Names are validated against Outlook rules. Pure; Outlook-free.
/// </summary>
public static class CategoryColorPlan
{
    public static IReadOnlyList<CategoryCandidate> Build(
        IReadOnlyDictionary<string, string> tagNames, IReadOnlyDictionary<string, string> tagColors)
    {
        ArgumentNullException.ThrowIfNull(tagNames);
        ArgumentNullException.ThrowIfNull(tagColors);

        // Deterministic order: the five built-ins first, then any other key (ordinal-sorted), distinct.
        var keys = new List<string>();
        var seenKey = new HashSet<string>(StringComparer.Ordinal);
        foreach (string k in MsfTagDefaults.BuiltinLabels.Keys)
            if (!MsfTagDefaults.Filtered.Contains(k) && seenKey.Add(k)) keys.Add(k);
        foreach (string k in tagNames.Keys.Concat(tagColors.Keys).OrderBy(k => k, StringComparer.Ordinal))
            if (!MsfTagDefaults.Filtered.Contains(k) && seenKey.Add(k)) keys.Add(k);

        var result = new List<CategoryCandidate>();
        // OrdinalIgnoreCase (not E4's Ordinal): Outlook category names are case-insensitive.
        var seenName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in keys)
        {
            string name = tagNames.TryGetValue(key, out string? pref) ? pref
                : MsfTagDefaults.BuiltinLabels.TryGetValue(key, out string? builtin) ? builtin
                : key;
            if (!seenName.Add(name)) continue; // ordinal-ignore-case de-dupe across keys

            string? hex = tagColors.TryGetValue(key, out string? c) ? c
                : MsfTagDefaults.BuiltinColors.TryGetValue(key, out string? bc) ? bc
                : null;

            if (name.Length == 0 || name.Length > 255 || name.Contains(','))
            { result.Add(new CategoryCandidate(name, hex, null, "skipped-invalid-name")); continue; }

            if (hex is null || !OlCategoryColorMap.TryNearestIndex(hex, out int color))
            { result.Add(new CategoryCandidate(name, hex, null, "skipped-no-colour")); continue; }

            result.Add(new CategoryCandidate(name, hex, color, "would-add"));
        }
        return result;
    }
}
