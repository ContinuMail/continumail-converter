// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>Reads user-overridden calendar category colours from prefs.js —
/// <c>calendar.category.color.&lt;mangledKey&gt;</c> with a #RRGGBB value. Line-oriented; does NOT
/// execute JS. Default category colours are NOT stored (Thunderbird computes them), so this is empty
/// for a profile that never changed a category colour.</summary>
public static class CalendarCategoryOverrideReader
{
    private static readonly Regex Line = new(
        "^\\s*user_pref\\s*\\(\\s*\"calendar\\.category\\.color\\.(?<key>[^\"]+)\"\\s*,\\s*\"(?<val>#?[0-9A-Fa-f]{6})\"\\s*\\)\\s*;?\\s*$",
        RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string> Read(string prefsJsPath)
    {
        try { return ParseText(File.ReadAllText(prefsJsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new Dictionary<string, string>(StringComparer.Ordinal); }
    }

    public static IReadOnlyDictionary<string, string> ParseText(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            Match m = Line.Match(line);
            if (!m.Success) continue;
            string hex = m.Groups["val"].Value;
            if (hex[0] != '#') hex = "#" + hex;
            map[m.Groups["key"].Value] = hex; // key is already the mangled token; later duplicate wins
        }
        return map;
    }
}
