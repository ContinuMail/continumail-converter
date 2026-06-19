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
    static MorkValueDecoder()
    {
        // Real Thunderbird profiles worldwide carry legacy code pages (windows-1252,
        // shift_jis, big5, euc-jp, koi8-r, …) that .NET Core+ does NOT support out of the
        // box. Registering the CodePages provider makes Encoding.GetEncoding resolve them
        // so a non-ASCII/UTF-8 profile is readable instead of rejected at parse time.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Strict ASCII: invalid high bytes throw instead of silently becoming '?' (fail-loud posture).
    private static readonly Encoding StrictAscii =
        Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    public static Encoding ResolveCharset(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return StrictUtf8;
        string name = hint!.Trim().ToLowerInvariant();
        switch (name)
        {
            case "utf-8":
            case "utf8":
                return StrictUtf8;
            case "iso-8859-1":
            case "latin1":
                return Encoding.Latin1;  // Latin1 maps every byte 0..255, so it never fails
            case "us-ascii":
            case "ascii":
                return StrictAscii;
        }

        // Anything else: defer to the runtime (now incl. the CodePages provider), but keep
        // the fail-loud posture — invalid byte sequences throw rather than becoming '?'.
        // A genuinely unknown charset NAME surfaces as a MorkFormatException.
        try
        {
            return Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (ArgumentException ex)
        {
            throw new MorkFormatException($"Unsupported Mork charset: '{hint}'", ex);
        }
    }

    public static string Decode(IReadOnlyList<byte> bytes, Encoding charset)
    {
        byte[] arr = bytes as byte[] ?? bytes.ToArray();
        try { return charset.GetString(arr); }
        catch (DecoderFallbackException ex) { throw new MorkFormatException("Invalid byte sequence for charset", ex); }
    }
}
