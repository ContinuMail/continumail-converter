// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class TimeZoneResolverTests
{
    [Theory]
    [InlineData("Europe/Copenhagen", "Europe/Copenhagen")]
    [InlineData("Asia/Bangkok", "Asia/Bangkok")]
    [InlineData("UTC", "UTC")]
    public void Resolves_olson_and_utc(string input, string expectedId)
    {
        var r = TimeZoneResolver.Resolve(input);
        Assert.False(r.IsFloating);
        Assert.Equal(expectedId, r.Zone!.Id);
        Assert.Equal(expectedId, r.ResolvedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("floating")]
    public void Floating_is_floating_no_warning(string? input)
    {
        var r = TimeZoneResolver.Resolve(input);
        Assert.True(r.IsFloating);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void No_tz_description_inline_is_floating_with_warning()
    {
        var r = TimeZoneResolver.Resolve("BEGIN:VTIMEZONE\r\nTZID:(no TZ description)\r\nEND:VTIMEZONE\r\n");
        Assert.True(r.IsFloating);
        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void Microsoft_utc_maps_to_utc()
    {
        const string block = "BEGIN:VTIMEZONE\r\nTZID:tzone://Microsoft/Utc\r\nEND:VTIMEZONE\r\n";
        var r = TimeZoneResolver.Resolve(block);
        Assert.Equal(TimeZoneInfo.Utc.Id, r.Zone!.Id);
        // OriginalId must be the full inline block, not the extracted TZID.
        Assert.StartsWith("BEGIN:VTIMEZONE", r.OriginalId);
    }

    [Fact]
    public void No_tz_description_raw_is_floating_with_warning()
    {
        // Raw "(no TZ description)" string — not wrapped in a VTIMEZONE block.
        var r = TimeZoneResolver.Resolve("(no TZ description)");
        Assert.True(r.IsFloating);
        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void Unresolvable_microsoft_tz_in_vtimezone_yields_warning_and_preserves_name()
    {
        const string block = "BEGIN:VTIMEZONE\r\nTZID:tzone://Microsoft/Made-Up-Zone\r\nEND:VTIMEZONE\r\n";
        var r = TimeZoneResolver.Resolve(block);
        Assert.Null(r.Zone);
        Assert.NotNull(r.Warning);
        // The stripped name after "tzone://Microsoft/" must be preserved in ResolvedId.
        Assert.Equal("Made-Up-Zone", r.ResolvedId);
    }

    [Fact]
    public void Unresolvable_id_yields_warning_and_preserves_id_not_throw()
    {
        var r = TimeZoneResolver.Resolve("Mars/Phobos");
        Assert.Null(r.Zone);
        Assert.NotNull(r.Warning);
        Assert.Equal("Mars/Phobos", r.ResolvedId);
    }
}
