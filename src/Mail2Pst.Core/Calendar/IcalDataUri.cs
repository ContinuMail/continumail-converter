// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Text;

namespace Mail2Pst.Core.Calendar;

/// <summary>
/// Decodes <c>data:</c> URIs as used in iCal ATTACH and ALTREP properties.
/// Handles both the <c>;base64</c> and percent-encoded variants, and honours
/// an optional <c>;charset=…</c> token (currently only read, not used for
/// decoding — callers handle charset themselves).
/// </summary>
internal static class IcalDataUri
{
    /// <summary>
    /// Tries to decode a <c>data:</c> URI into its media type and raw bytes.
    /// </summary>
    /// <param name="uri">The raw URI string (e.g. <c>data:text/html;base64,aGk=</c>).</param>
    /// <param name="mediaType">
    ///   On success, the bare media type without parameters (e.g. <c>text/html</c>).
    ///   Empty string when no media type is specified.
    /// </param>
    /// <param name="bytes">On success, the decoded payload bytes.</param>
    /// <returns><c>true</c> when the URI was successfully parsed and decoded.</returns>
    public static bool TryDecode(string uri, out string mediaType, out byte[] bytes)
    {
        mediaType = string.Empty;
        bytes     = Array.Empty<byte>();

        if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        var commaIdx = uri.IndexOf(',');
        if (commaIdx < 0)
            return false;

        // meta = everything between "data:" and the first comma
        // e.g. "text/html;charset=utf-8;base64" or "text/plain"
        var meta    = uri.Substring("data:".Length, commaIdx - "data:".Length);
        var payload = uri.Substring(commaIdx + 1);

        // Split meta on ';' — first part is the media type, rest are params.
        var metaParts   = meta.Split(';');
        mediaType       = metaParts.Length > 0 ? metaParts[0] : string.Empty;
        bool isBase64   = false;

        for (int i = 1; i < metaParts.Length; i++)
        {
            if (metaParts[i].Equals("base64", StringComparison.OrdinalIgnoreCase))
                isBase64 = true;
            // charset= is recognised but not used — callers decode bytes themselves.
        }

        if (isBase64)
        {
            try
            {
                bytes = Convert.FromBase64String(payload);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Percent-encoded payload (default for text/* without ;base64).
        try
        {
            bytes = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}
