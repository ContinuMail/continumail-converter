// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Mail2Pst.Core.Msf;

public enum AddressResolution { Identity, Server, LocalFolders, NotFound }

public sealed record PrefsAccount(string? Email, string? Host, AddressResolution Resolution);

/// <summary>
/// Reads Thunderbird account identity from prefs.js: maps each mail-server storage directory
/// (normalized "&lt;store&gt;/&lt;name&gt;") to its resolved email + host. Line-oriented, does NOT run JS.
/// </summary>
public static class PrefsAccountReader
{
    private static readonly Regex Pref = new(
        "^\\s*user_pref\\s*\\(\\s*\"(?<key>[^\"]+)\"\\s*,\\s*\"(?<val>(?:\\\\.|[^\"\\\\])*)\"\\s*\\)\\s*;?\\s*$",
        RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, PrefsAccount> Read(string prefsJsPath)
    {
        try { return ParseText(File.ReadAllText(prefsJsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new Dictionary<string, PrefsAccount>(StringComparer.Ordinal); }
    }

    public static IReadOnlyDictionary<string, PrefsAccount> ParseText(string content)
    {
        var srvDir = new Dictionary<string, string>();   // serverId -> normalized store/name
        var srvHost = new Dictionary<string, string>();
        var srvUser = new Dictionary<string, string>();
        var srvType = new Dictionary<string, string>();
        var acctServer = new Dictionary<string, string>(); // accountId -> serverId
        var acctIdents = new Dictionary<string, string>(); // accountId -> "id1,id2"
        var identMail = new Dictionary<string, string>();  // identId -> email

        foreach (string line in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            Match m = Pref.Match(line);
            if (!m.Success) continue;
            string key = m.Groups["key"].Value;
            if (!PrefsJsEscape.TryUnescape(m.Groups["val"].Value, out string val)) continue; // bad escape -> skip line
            string[] p = key.Split('.');
            if (p.Length == 4 && p[0] == "mail" && p[1] == "server")
            {
                string id = p[2];
                switch (p[3])
                {
                    case "directory-rel": srvDir[id] = NormalizeRel(val); break;
                    case "directory": if (!srvDir.ContainsKey(id)) srvDir[id] = NormalizeAbs(val); break;
                    case "hostname": srvHost[id] = val; break;
                    case "userName": srvUser[id] = val; break;
                    case "type": srvType[id] = val; break;
                }
            }
            else if (p.Length == 4 && p[0] == "mail" && p[1] == "account")
            {
                if (p[3] == "server") acctServer[p[2]] = val;
                else if (p[3] == "identities") acctIdents[p[2]] = val;
            }
            else if (p.Length == 4 && p[0] == "mail" && p[1] == "identity" && p[3] == "useremail")
            {
                identMail[p[2]] = val;
            }
        }

        // server -> first identity email (via the account that references it)
        var srvEmail = new Dictionary<string, string>();
        foreach (var (acct, srv) in acctServer)
        {
            if (!acctIdents.TryGetValue(acct, out string? ids)) continue;
            foreach (string idRaw in ids.Split(','))
            {
                string id = idRaw.Trim();
                if (id.Length > 0 && identMail.TryGetValue(id, out string? email) && email.Length > 0)
                { srvEmail[srv] = email; break; }
            }
        }

        var result = new Dictionary<string, PrefsAccount>(StringComparer.Ordinal);
        foreach (var (srv, dirKey) in srvDir)
        {
            srvHost.TryGetValue(srv, out string? host);
            srvType.TryGetValue(srv, out string? type);
            string? email = null;
            AddressResolution res;
            if (string.Equals(type, "none", StringComparison.Ordinal))
            {
                res = AddressResolution.LocalFolders;
            }
            else if (srvEmail.TryGetValue(srv, out string? idEmail))
            {
                email = idEmail; res = AddressResolution.Identity;
            }
            else if (srvUser.TryGetValue(srv, out string? user) && user.Contains('@'))
            {
                email = user; res = AddressResolution.Server;
            }
            else { res = AddressResolution.NotFound; }
            result[dirKey] = new PrefsAccount(email, host, res);
        }
        return result;
    }

    // "[ProfD]ImapMail/imap.example.com" -> "imapmail/imap.example.com"
    private static string NormalizeRel(string v)
    {
        int b = v.IndexOf(']');
        string rel = b >= 0 ? v[(b + 1)..] : v;
        return rel.Replace('\\', '/').Trim('/').ToLowerInvariant();
    }

    // "C:\...\ImapMail\imap.example.com" -> last two segments "imapmail/imap.example.com"
    private static string NormalizeAbs(string v)
    {
        string[] seg = v.Replace('\\', '/').TrimEnd('/').Split('/');
        return seg.Length >= 2
            ? (seg[^2] + "/" + seg[^1]).ToLowerInvariant()
            : v.Replace('\\', '/').ToLowerInvariant();
    }
}
