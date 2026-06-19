// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Core.Tests.Mork;

public class MorkValueDecoderTests
{
    [Fact]
    public void Decode_Latin1_HighByte_DecodesToChar()
    {
        // 0xE6 under ISO-8859-1 is 'æ'
        string s = MorkValueDecoder.Decode(new byte[] { 0xE6 }, MorkValueDecoder.ResolveCharset("iso-8859-1"));
        Assert.Equal("æ", s);
    }

    [Fact]
    public void Decode_Utf8_MultiByteRun_DecodesAsWhole()
    {
        // 0xC3 0xA6 is 'æ' in UTF-8 — must decode the whole run, not byte-by-byte
        string s = MorkValueDecoder.Decode(new byte[] { 0xC3, 0xA6 }, MorkValueDecoder.ResolveCharset(null));
        Assert.Equal("æ", s);
    }

    [Fact]
    public void Decode_AsciiRun_RoundTrips()
    {
        string s = MorkValueDecoder.Decode(Encoding.ASCII.GetBytes("flags"), MorkValueDecoder.ResolveCharset(null));
        Assert.Equal("flags", s);
    }

    [Fact]
    public void ResolveCharset_NullOrEmpty_DefaultsUtf8()
    {
        Assert.Equal(Encoding.UTF8.WebName, MorkValueDecoder.ResolveCharset(null).WebName);
        Assert.Equal(Encoding.UTF8.WebName, MorkValueDecoder.ResolveCharset("").WebName);
    }

    [Fact]
    public void ResolveCharset_Unknown_Throws()
    {
        // A genuinely non-existent charset name still fails loud. (Note: real code-page
        // names like "ebcdic-cp-us" now resolve via the CodePages provider — see below.)
        Assert.Throws<MorkFormatException>(() => MorkValueDecoder.ResolveCharset("definitely-not-a-real-charset"));
    }

    [Fact]
    public void ResolveCharset_Windows1252_Resolves()
    {
        // windows-1252 is extremely common in real-world Thunderbird profiles but is NOT
        // in the hand-written fast-path allowlist — it must resolve via the CodePages provider.
        Assert.Equal("windows-1252", MorkValueDecoder.ResolveCharset("windows-1252").WebName);
    }

    [Fact]
    public void Decode_Windows1252_HighByte_DecodesToChar()
    {
        // 0x80 under windows-1252 is the euro sign '€' (U+20AC) — not a control char as in Latin1.
        string s = MorkValueDecoder.Decode(new byte[] { 0x80 }, MorkValueDecoder.ResolveCharset("windows-1252"));
        Assert.Equal("€", s);
    }

    [Fact]
    public void ResolveCharset_FallbackCharset_UsesExceptionFallback()
    {
        // Charsets resolved via the CodePages fallback path must keep the fail-loud posture:
        // an invalid byte sequence throws rather than silently becoming a replacement char.
        // Asserting the wired-up decoder fallback is premise-solid (it does not depend on
        // which specific bytes a given code page leaves unmapped).
        Encoding enc = MorkValueDecoder.ResolveCharset("shift_jis");
        Assert.Equal("shift_jis", enc.WebName);
        Assert.IsType<DecoderExceptionFallback>(enc.DecoderFallback);
    }

    [Fact]
    public void Decode_InvalidUtf8_ThrowsMorkFormatException()
    {
        // 0xC3 0x28 is an invalid UTF-8 sequence — must fail loud, not emit a replacement char.
        Assert.Throws<MorkFormatException>(() =>
            MorkValueDecoder.Decode(new byte[] { 0xC3, 0x28 }, MorkValueDecoder.ResolveCharset("utf-8")));
    }
}
