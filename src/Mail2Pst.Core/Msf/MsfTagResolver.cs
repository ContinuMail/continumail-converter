// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Msf;

public interface IMsfTagResolver
{
    IReadOnlyList<string> Resolve(IReadOnlyList<string> keywords);
}

/// <summary>
/// SP3 default: drop internal junk tokens, map the five built-in $labelN to Thunderbird's
/// default tag names, pass custom tokens through, dedupe (ordinal, first wins). SP4 replaces
/// this with prefs.js-backed resolution. (English defaults are a best guess; TB lets users
/// rename/localize them — prefs.js is authoritative.)
/// </summary>
public sealed class DefaultMsfTagResolver : IMsfTagResolver
{
    public IReadOnlyList<string> Resolve(IReadOnlyList<string> keywords)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string kw in keywords)
        {
            if (MsfTagDefaults.Filtered.Contains(kw)) continue;
            string name = MsfTagDefaults.BuiltinLabels.TryGetValue(kw, out string? mapped) ? mapped : kw;
            if (seen.Add(name)) result.Add(name);
        }
        return result;
    }
}
