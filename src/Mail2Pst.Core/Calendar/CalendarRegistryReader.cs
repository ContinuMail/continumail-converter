// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mail2Pst.Core.Msf;   // PrefsJsEscape

namespace Mail2Pst.Core.Calendar;

public sealed record CalendarRegistryEntry(
    string CalId, string DisplayName, string CalendarType, string Uri, bool VisibleInThunderbird);

/// <summary>
/// Reads <c>calendar.registry.&lt;id&gt;.{name,type,uri,calendar-main-in-composite}</c> from prefs.js.
/// Line-oriented user_pref recognition (does NOT execute JS); mirrors PrefsTagReader and reuses
/// PrefsJsEscape for \uXXXX / \" / \\ etc. One entry per cal_id that has a name OR type. Missing
/// file -> empty list.
/// </summary>
public static class CalendarRegistryReader
{
    private static readonly Regex Line = new(
        "^\\s*user_pref\\s*\\(\\s*\"calendar\\.registry\\.(?<id>[^.\"]+)\\.(?<key>[^\"]+)\"\\s*,\\s*(?:\"(?<sval>(?:\\\\.|[^\"\\\\])*)\"|(?<bval>true|false|-?\\d+))\\s*\\)\\s*;?\\s*$",
        RegexOptions.CultureInvariant);

    private sealed class Builder { public string Name = ""; public string Type = ""; public string Uri = ""; public bool Visible; }

    public static IReadOnlyList<CalendarRegistryEntry> Read(string prefsJsPath)
    {
        try { return ParseText(File.ReadAllText(prefsJsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Array.Empty<CalendarRegistryEntry>(); }
    }

    public static IReadOnlyList<CalendarRegistryEntry> ParseText(string content)
    {
        var byId = new Dictionary<string, Builder>(StringComparer.Ordinal);
        foreach (string raw in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            Match m = Line.Match(raw);
            if (!m.Success) continue;
            string id = m.Groups["id"].Value, key = m.Groups["key"].Value;
            string val;
            if (m.Groups["sval"].Success)
            {
                if (!PrefsJsEscape.TryUnescape(m.Groups["sval"].Value, out val)) continue; // bad escape -> skip line
            }
            else val = m.Groups["bval"].Value;

            if (!byId.TryGetValue(id, out Builder? b)) byId[id] = b = new Builder();
            switch (key)
            {
                case "name": b.Name = val; break;
                case "type": b.Type = val; break;
                case "uri":  b.Uri = val; break;
                case "calendar-main-in-composite": b.Visible = string.Equals(val, "true", StringComparison.Ordinal); break;
            }
        }

        var result = new List<CalendarRegistryEntry>(byId.Count);
        foreach (var (id, b) in byId)
            if (b.Name.Length > 0 || b.Type.Length > 0)
                result.Add(new CalendarRegistryEntry(id, b.Name, b.Type, b.Uri, b.Visible));
        return result;
    }
}
