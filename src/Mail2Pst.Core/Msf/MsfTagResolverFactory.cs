// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using System.IO;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Selects the tag resolver for a conversion. With a profile directory containing a prefs.js that has
/// tag-name prefs, returns a PrefsJsTagResolver; otherwise (null/blank path, no prefs.js, unreadable, or
/// no tag prefs) silently falls back to DefaultMsfTagResolver. Best-effort category naming — never warns
/// and never affects .msf enrichment counts.
/// </summary>
public static class MsfTagResolverFactory
{
    public static IMsfTagResolver Create(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            return new DefaultMsfTagResolver();

        IReadOnlyDictionary<string, string> prefs = PrefsTagReader.Read(Path.Combine(profilePath, "prefs.js"));
        return prefs.Count > 0 ? new PrefsJsTagResolver(prefs) : new DefaultMsfTagResolver();
    }
}
