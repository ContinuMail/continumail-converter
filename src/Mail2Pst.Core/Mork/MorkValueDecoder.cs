// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mail2Pst.Core.Mork;

/// <summary>Decodes a Mork value's assembled byte run to a string using the active charset.</summary>
internal static class MorkValueDecoder
{
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Strict ASCII: invalid high bytes throw instead of silently becoming '?' (fail-loud posture).
    private static readonly Encoding StrictAscii =
        Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    public static Encoding ResolveCharset(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return StrictUtf8;
        return hint!.Trim().ToLowerInvariant() switch
        {
            "utf-8" or "utf8" => StrictUtf8,
            "iso-8859-1" or "latin1" => Encoding.Latin1,  // Latin1 maps every byte 0..255, so it never fails
            "us-ascii" or "ascii" => StrictAscii,
            _ => throw new MorkFormatException($"Unsupported Mork charset: '{hint}'"),
        };
    }

    public static string Decode(IReadOnlyList<byte> bytes, Encoding charset)
    {
        byte[] arr = bytes as byte[] ?? bytes.ToArray();
        try { return charset.GetString(arr); }
        catch (DecoderFallbackException ex) { throw new MorkFormatException("Invalid byte sequence for charset", ex); }
    }
}
