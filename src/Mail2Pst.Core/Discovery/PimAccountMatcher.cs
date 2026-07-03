// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Discovery;

/// <summary>Result of resolving a calendar URI / CardDAV URL to a mail account.
/// AccountId==null &amp; Ambiguous==false = genuinely local / no candidate (silent).
/// AccountId==null &amp; Ambiguous==true  = ≥2 host candidates; caller emits a warning.</summary>
public readonly record struct AccountMatch(string? AccountId, bool Ambiguous);

/// <summary>Resolves a Thunderbird calendar/CardDAV URI to a discovered account.
/// Safety-by-default: email match (raw or %40-encoded) first, then normalized host
/// (exact or subdomain — never bare substring); ≥2 host candidates → null+Ambiguous.</summary>
public static class PimAccountMatcher
{
    public static AccountMatch Match(string? uriOrUrl, IReadOnlyList<Account> accounts)
    {
        if (string.IsNullOrEmpty(uriOrUrl)) return new AccountMatch(null, false);

        string candidate = uriOrUrl!;
        string decoded = SafeUnescape(candidate);

        // 1. Email match (raw OR url-encoded), case-insensitive. Ambiguity -> null.
        string? emailMatchId = null;
        bool emailAmbiguous = false;
        foreach (Account a in accounts)
        {
            if (string.IsNullOrEmpty(a.Email)) continue;
            string enc = SafeEscape(a.Email!);   // aksel@x -> aksel%40x
            bool hit = decoded.IndexOf(a.Email!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       candidate.IndexOf(a.Email!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       candidate.IndexOf(enc, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hit) continue;
            if (emailMatchId is null) emailMatchId = a.Id;
            else if (!string.Equals(emailMatchId, a.Id, StringComparison.Ordinal)) emailAmbiguous = true;
        }
        if (emailAmbiguous) return new AccountMatch(null, true);   // never guess between two emails
        if (emailMatchId is not null) return new AccountMatch(emailMatchId, false);

        // 2. Host match (exact or subdomain). Ambiguity -> null+Ambiguous.
        string? host = TryGetHost(candidate);
        if (host is not null)
        {
            string? matchedId = null;
            bool ambiguous = false;
            foreach (Account a in accounts)
            {
                string? ah = NormalizeHost(a.Host);
                if (ah is null) continue;
                bool hit = string.Equals(host, ah, StringComparison.OrdinalIgnoreCase) ||
                           host.EndsWith("." + ah, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
                if (matchedId is null) matchedId = a.Id;
                else if (!string.Equals(matchedId, a.Id, StringComparison.Ordinal)) ambiguous = true;
            }
            if (ambiguous) return new AccountMatch(null, true);
            if (matchedId is not null) return new AccountMatch(matchedId, false);
        }

        return new AccountMatch(null, false);
    }

    private static string SafeUnescape(string s)
    {
        try { return Uri.UnescapeDataString(s); } catch { return s; }
    }

    private static string SafeEscape(string s)
    {
        try { return Uri.EscapeDataString(s); } catch { return s; }
    }

    private static string? TryGetHost(string s)
    {
        if (Uri.TryCreate(s, UriKind.Absolute, out Uri? u) && !string.IsNullOrEmpty(u.Host))
            return u.Host;
        return null;
    }

    private static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        string h = host!.Trim();
        int scheme = h.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) h = h[(scheme + 3)..];
        int slash = h.IndexOf('/');
        if (slash >= 0) h = h[..slash];
        int colon = h.IndexOf(':');
        if (colon >= 0) h = h[..colon];
        return h.Length == 0 ? null : h;
    }
}
