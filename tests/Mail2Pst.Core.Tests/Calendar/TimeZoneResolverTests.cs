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
    public void Floating_is_floating_no_warning(string input)
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
        var r = TimeZoneResolver.Resolve("BEGIN:VTIMEZONE\r\nTZID:tzone://Microsoft/Utc\r\nEND:VTIMEZONE\r\n");
        Assert.Equal(TimeZoneInfo.Utc.Id, r.Zone!.Id);
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
