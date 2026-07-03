// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mail2Pst.Core.Msf;   // PrefsJsEscape

namespace Mail2Pst.Core.Contacts;

/// <summary>Reads <c>ldap_2.servers.&lt;key&gt;.{filename,carddav.url}</c> from prefs.js and
/// returns basename(filename) → carddav.url, only for server entries that have BOTH. Modern
/// Thunderbird (102+) CardDAV address-book layout. Line-oriented (does NOT execute JS); mirrors
/// CalendarRegistryReader and reuses PrefsJsEscape. Missing file → empty map.</summary>
public static class AddressBookRegistryReader
{
    private static readonly Regex Line = new(
        "^\\s*user_pref\\s*\\(\\s*\"ldap_2\\.servers\\.(?<key>[^.\"]+)\\.(?<sub>[^\"]+)\"\\s*,\\s*\"(?<val>(?:\\\\.|[^\"\\\\])*)\"\\s*\\)\\s*;?\\s*$",
        RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string> ReadFilenameToCardDavUrl(string prefsJsPath)
    {
        try { return ParseText(File.ReadAllText(prefsJsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    public static IReadOnlyDictionary<string, string> ParseText(string content)
    {
        var fileName = new Dictionary<string, string>(StringComparer.Ordinal);
        var cardDav = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string raw in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            Match m = Line.Match(raw);
            if (!m.Success) continue;
            string key = m.Groups["key"].Value, sub = m.Groups["sub"].Value;
            if (!PrefsJsEscape.TryUnescape(m.Groups["val"].Value, out string val)) continue;
            if (sub == "filename") fileName[key] = Path.GetFileName(val);
            else if (sub == "carddav.url") cardDav[key] = val;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, fn) in fileName)
            if (fn.Length > 0 && cardDav.TryGetValue(key, out string? url) && url.Length > 0)
                map[fn] = url;
        return map;
    }
}
