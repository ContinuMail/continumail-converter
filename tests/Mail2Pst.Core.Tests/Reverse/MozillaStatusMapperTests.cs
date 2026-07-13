// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Globalization;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class MozillaStatusMapperTests
{
    private static string[] None => Array.Empty<string>();

    // The mapper single-sources its Status bits from MsfMessageFlags. Pin the four values it relies on so
    // that an accidental change to the shared enum fails loudly here rather than silently shifting headers.
    [Fact]
    public void MsfMessageFlags_ExpectedThunderbirdBits_AreStable()
    {
        Assert.Equal(0x0001u, (uint)MsfMessageFlags.Read);
        Assert.Equal(0x0002u, (uint)MsfMessageFlags.Replied);
        Assert.Equal(0x0004u, (uint)MsfMessageFlags.Marked);
        Assert.Equal(0x1000u, (uint)MsfMessageFlags.Forwarded);
    }

    [Fact]
    public void Map_NoFlagsNoCategories_AllZero()
    {
        MozillaStatusHeaders h = MozillaStatusMapper.Map(false, false, false, None);
        Assert.Equal("0000", h.Status);
        Assert.Equal("00000000", h.Status2);
        Assert.Equal("", h.Keys);
    }

    [Theory]
    [InlineData(true, false, false, "0001")]   // Read
    [InlineData(false, true, false, "0002")]   // Replied
    [InlineData(false, false, true, "1000")]   // Forwarded
    [InlineData(true, true, true, "1003")]     // Read | Replied | Forwarded
    public void Map_Flags_SetStatusBits(bool read, bool replied, bool forwarded, string expected)
        => Assert.Equal(expected, MozillaStatusMapper.Map(read, replied, forwarded, None).Status);

    [Theory]
    [InlineData("Star")]
    [InlineData("star")]   // case-insensitive, matching the forward StarCategory match
    public void Map_StarCategory_SetsMarkedBit_AndIsNotAKeyword(string star)
    {
        MozillaStatusHeaders h = MozillaStatusMapper.Map(false, false, false, new[] { star });
        Assert.Equal("0004", h.Status);   // Marked
        Assert.Equal("", h.Keys);         // Star consumed, not emitted as a keyword
    }

    [Fact]
    public void Map_Categories_BecomeSanitizedKeys()
    {
        MozillaStatusHeaders h = MozillaStatusMapper.Map(false, false, false, new[] { "Work", "Follow Up" });
        Assert.Equal("0000", h.Status);
        Assert.Equal("Work follow_up", h.Keys);
    }

    [Fact]
    public void Map_StarPlusTags_MarksAndKeepsOtherTags()
    {
        MozillaStatusHeaders h = MozillaStatusMapper.Map(true, false, false, new[] { "Star", "Work" });
        Assert.Equal("0005", h.Status);   // Read | Marked
        Assert.Equal("Work", h.Keys);
    }

    [Fact]
    public void Map_DuplicateAndEmptyKeys_AreDeduplicatedAndDropped()
    {
        MozillaStatusHeaders h = MozillaStatusMapper.Map(
            false, false, false, new[] { "Follow Up", "follow_up", "!!!", "Work" });
        // "Follow Up" -> follow_up (added); "follow_up" -> follow_up (dup, dropped);
        // "!!!" -> nothing usable (dropped); "Work" -> Work.
        Assert.Equal("follow_up Work", h.Keys);
    }

    [Fact]
    public void Map_StatusHex_RoundTripsThroughForwardHexParse()
    {
        // Symmetry with the forward MimeMessageMapper, which reads X-Mozilla-Status via
        // uint.TryParse(value, HexNumber). Proves our emitted hex parses back to the same bits.
        MozillaStatusHeaders h = MozillaStatusMapper.Map(true, false, true, None); // Read | Forwarded = 0x1001
        Assert.True(uint.TryParse(h.Status, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint flags));
        Assert.Equal(0x1001u, flags);
    }

    [Fact]
    public void Map_MessageOverload_MatchesDiscreteOverload()
    {
        var message = new PstMailMessage(
            Subject: "s", FromAddress: "a@b", Recipients: Array.Empty<PstRecipient>(),
            Date: null, MessageId: null, InReplyTo: null, References: null, PlainBody: null, HtmlBody: null,
            InternetCodepage: null, TransportHeaders: null, IsRead: true, IsReplied: false, IsForwarded: false,
            Categories: new[] { "Star", "Work" }, Attachments: Array.Empty<PstAttachment>());
        Assert.Equal(
            MozillaStatusMapper.Map(true, false, false, new[] { "Star", "Work" }),
            MozillaStatusMapper.Map(message));
    }
}
