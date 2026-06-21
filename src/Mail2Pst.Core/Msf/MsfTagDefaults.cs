// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Shared tag-resolution defaults used by both DefaultMsfTagResolver and PrefsJsTagResolver:
/// internal junk tokens to drop, and the five built-in $labelN English fallback names.
/// (Thunderbird lets users rename/localize these; prefs.js is authoritative when present.)
/// </summary>
internal static class MsfTagDefaults
{
    internal static readonly IReadOnlySet<string> Filtered =
        new HashSet<string>(StringComparer.Ordinal) { "NonJunk" };

    internal static readonly IReadOnlyDictionary<string, string> BuiltinLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["$label1"] = "Important", ["$label2"] = "Work", ["$label3"] = "Personal",
            ["$label4"] = "To Do",     ["$label5"] = "Later",
        };

    internal static readonly IReadOnlyDictionary<string, string> BuiltinColors =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["$label1"] = "#FF0000", ["$label2"] = "#FF9900", ["$label3"] = "#009900",
            ["$label4"] = "#3333FF", ["$label5"] = "#993399",
        };
}
