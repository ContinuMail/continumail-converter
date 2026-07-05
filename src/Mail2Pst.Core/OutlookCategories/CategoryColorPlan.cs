// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Msf;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// Builds the colour-import candidate list from a profile's prefs.js tag names + colours, optionally
/// merged with calendar category colours. Candidate set = the five built-in $labelN keys plus every key
/// seen in names/colours. Name resolution matches the E4 resolver (prefs name -> built-in $labelN default
/// -> key). Colour = prefs .color -> built-in default for $labelN -> none. Names are validated against
/// Outlook rules. Merge precedence: a coloured mail tag wins; an uncoloured mail tag is upgraded in place
/// (list position kept) by a coloured calendar category of the same name; a calendar-only name is
/// appended. Names de-dupe case-insensitively. Pure; Outlook-free.
/// </summary>
public static class CategoryColorPlan
{
    // Back-compat: mail tags only.
    public static IReadOnlyList<CategoryCandidate> Build(
        IReadOnlyDictionary<string, string> tagNames, IReadOnlyDictionary<string, string> tagColors)
        => Build(tagNames, tagColors, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<CategoryCandidate> Build(
        IReadOnlyDictionary<string, string> tagNames,
        IReadOnlyDictionary<string, string> tagColors,
        IReadOnlyDictionary<string, string> calendarCategoryColors)
    {
        ArgumentNullException.ThrowIfNull(tagNames);
        ArgumentNullException.ThrowIfNull(tagColors);
        ArgumentNullException.ThrowIfNull(calendarCategoryColors);

        // Deterministic mail-tag order: the five built-ins first, then any other key (ordinal-sorted).
        var keys = new List<string>();
        var seenKey = new HashSet<string>(StringComparer.Ordinal);
        foreach (string k in MsfTagDefaults.BuiltinLabels.Keys)
            if (!MsfTagDefaults.Filtered.Contains(k) && seenKey.Add(k)) keys.Add(k);
        foreach (string k in tagNames.Keys.Concat(tagColors.Keys).OrderBy(k => k, StringComparer.Ordinal))
            if (!MsfTagDefaults.Filtered.Contains(k) && seenKey.Add(k)) keys.Add(k);

        var result = new List<CategoryCandidate>();
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pass 1: mail tags (behaviour identical to the old 2-arg path).
        foreach (string key in keys)
        {
            string name = tagNames.TryGetValue(key, out string? pref) ? pref
                : MsfTagDefaults.BuiltinLabels.TryGetValue(key, out string? builtin) ? builtin
                : key;
            if (indexByName.ContainsKey(name)) continue;
            string? hex = tagColors.TryGetValue(key, out string? c) ? c
                : MsfTagDefaults.BuiltinColors.TryGetValue(key, out string? bc) ? bc
                : null;
            indexByName[name] = result.Count;
            result.Add(MakeCandidate(name, hex));
        }

        // Pass 2: calendar categories. Coloured mail wins; an uncoloured mail tag is upgraded; new names append.
        foreach (var kv in calendarCategoryColors)
        {
            CategoryCandidate cand = MakeCandidate(kv.Key, kv.Value);
            if (indexByName.TryGetValue(kv.Key, out int idx))
            {
                if (result[idx].Action == "skipped-no-colour" && cand.Action == "would-add")
                    result[idx] = cand; // upgrade in place, keeping list position
                // else: coloured / invalid mail candidate wins -> skip the calendar entry
            }
            else
            {
                indexByName[kv.Key] = result.Count;
                result.Add(cand);
            }
        }
        return result;
    }

    private static CategoryCandidate MakeCandidate(string name, string? hex)
    {
        if (name.Length == 0 || name.Length > 255 || name.Contains(','))
            return new CategoryCandidate(name, hex, null, "skipped-invalid-name");
        if (hex is null || !OlCategoryColorMap.TryNearestIndex(hex, out int color))
            return new CategoryCandidate(name, hex, null, "skipped-no-colour");
        return new CategoryCandidate(name, hex, color, "would-add");
    }
}
