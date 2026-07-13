// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.OutlookCategories;

namespace Mail2Pst.Core.Reverse;

/// <summary>The three Thunderbird header values derived from a message's flags and categories.</summary>
/// <param name="Status">4-digit lowercase hex for <c>X-Mozilla-Status</c> (low-word flags).</param>
/// <param name="Status2">8-digit lowercase hex for <c>X-Mozilla-Status2</c> (reserved; "00000000" in v1).</param>
/// <param name="Keys">Space-joined <c>X-Mozilla-Keys</c> keywords; empty string when none.</param>
public sealed record MozillaStatusHeaders(string Status, string Status2, string Keys);

/// <summary>
/// Pure mapper: a message's read/replied/forwarded flags and categories → the Thunderbird
/// <c>X-Mozilla-Status</c> / <c>X-Mozilla-Status2</c> / <c>X-Mozilla-Keys</c> header values that seed
/// Thunderbird's regenerated <c>.msf</c>. The synthetic "Star" category (the forward direction's stand-in
/// for Thunderbird "Marked") sets the Status Marked bit and is NOT re-emitted as a keyword. This is the
/// inverse of the forward <c>MimeMessageMapper</c> <c>X-Mozilla-Status</c> parse; bit values come from
/// <see cref="MsfMessageFlags"/> so both directions share one source of truth. (Keyword emission is the
/// reverse-export side only; the forward parser does not re-read <c>X-Mozilla-Keys</c> into categories.)
/// </summary>
public static class MozillaStatusMapper
{
    private const string EmptyStatus2 = "00000000"; // reserved for extended flags; kept minimal in v1

    /// <summary>Maps the already-decoded flags/categories of one message to its X-Mozilla-* headers.</summary>
    public static MozillaStatusHeaders Map(
        bool isRead, bool isReplied, bool isForwarded, IReadOnlyList<string> categories)
    {
        categories ??= Array.Empty<string>();

        bool marked = false;
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string category in categories)
        {
            if (string.Equals(category, StarCategory.Name, StringComparison.OrdinalIgnoreCase))
            {
                marked = true;                               // "Star" -> Marked bit, consumed (not a keyword)
                continue;
            }
            string? key = MozillaKeywordSanitizer.Sanitize(category);
            if (key is not null && seen.Add(key)) keys.Add(key);
        }

        uint status = 0;
        if (isRead)      status |= (uint)MsfMessageFlags.Read;      // 0x0001
        if (isReplied)   status |= (uint)MsfMessageFlags.Replied;   // 0x0002
        if (marked)      status |= (uint)MsfMessageFlags.Marked;    // 0x0004
        if (isForwarded) status |= (uint)MsfMessageFlags.Forwarded; // 0x1000

        return new MozillaStatusHeaders(
            status.ToString("x4", CultureInfo.InvariantCulture),
            EmptyStatus2,
            string.Join(" ", keys));
    }

    /// <summary>Convenience overload for a read-back <see cref="PstMailMessage"/>.</summary>
    public static MozillaStatusHeaders Map(PstMailMessage message)
        => Map(message.IsRead, message.IsReplied, message.IsForwarded, message.Categories);
}
