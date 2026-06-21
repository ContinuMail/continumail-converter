// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Resolves .msf tag keys to category names using prefs.js display-names layered over the built-in
/// defaults. Per keyword: drop NonJunk; else prefs name; else built-in $labelN; else the key verbatim.
/// Dedupe ordinal, first occurrence wins (same contract as DefaultMsfTagResolver when prefs is empty).
/// </summary>
public sealed class PrefsJsTagResolver : IMsfTagResolver
{
    private readonly Dictionary<string, string> _prefs;

    public PrefsJsTagResolver(IReadOnlyDictionary<string, string> prefsNames)
    {
        ArgumentNullException.ThrowIfNull(prefsNames);
        _prefs = new Dictionary<string, string>(prefsNames, StringComparer.Ordinal); // defensive ordinal copy
    }

    public IReadOnlyList<string> Resolve(IReadOnlyList<string> keywords)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string kw in keywords)
        {
            if (MsfTagDefaults.Filtered.Contains(kw)) continue;
            string name = _prefs.TryGetValue(kw, out string? pref) ? pref
                : MsfTagDefaults.BuiltinLabels.TryGetValue(kw, out string? builtin) ? builtin
                : kw;
            if (seen.Add(name)) result.Add(name);
        }
        return result;
    }
}
