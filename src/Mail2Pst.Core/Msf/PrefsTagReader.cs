// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Reads Thunderbird tag display-names from a profile's prefs.js — the `mailnews.tags.&lt;key&gt;.tag`
/// prefs only (colour is out of scope). Line-oriented recognition of user_pref(...) text; does NOT
/// execute JavaScript. ParseText is pure; Read adds file I/O and returns an empty map when the file is
/// missing/unreadable.
/// </summary>
public static class PrefsTagReader
{
    // Anchored per line so a "// user_pref(...)" comment line does not match. Value allows escaped
    // chars (\\ and \") inside; key is the lazy segment between "mailnews.tags." and the ".tag" suffix.
    private static readonly Regex TagPrefRegex = new(
        "^\\s*user_pref\\s*\\(\\s*\"mailnews\\.tags\\.(?<key>.+?)\\.tag\"\\s*,\\s*\"(?<val>(?:\\\\.|[^\"\\\\])*)\"\\s*\\)\\s*;?\\s*$",
        RegexOptions.CultureInvariant);

    // Like TagPrefRegex but for `.color` with a strict #RRGGBB value (6 hex digits, optional leading #).
    private static readonly Regex ColorPrefRegex = new(
        "^\\s*user_pref\\s*\\(\\s*\"mailnews\\.tags\\.(?<key>.+?)\\.color\"\\s*,\\s*\"(?<val>#?[0-9A-Fa-f]{6})\"\\s*\\)\\s*;?\\s*$",
        RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string> Read(string prefsJsPath)
    {
        try
        {
            return ParseText(File.ReadAllText(prefsJsPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public static IReadOnlyDictionary<string, string> ParseText(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            Match m = TagPrefRegex.Match(line);
            if (!m.Success) continue;
            if (!PrefsJsEscape.TryUnescape(m.Groups["val"].Value, out string name)) continue; // bad escape -> skip line
            if (name.Length == 0) continue;                                       // empty name -> skip line
            map[m.Groups["key"].Value] = name;                                    // later duplicate wins
        }
        return map;
    }

    public static IReadOnlyDictionary<string, string> ParseColors(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            Match m = ColorPrefRegex.Match(line);
            if (!m.Success) continue;
            string hex = m.Groups["val"].Value;
            if (hex[0] != '#') hex = "#" + hex;     // normalize to leading #
            map[m.Groups["key"].Value] = hex;       // later duplicate wins
        }
        return map;
    }

}
