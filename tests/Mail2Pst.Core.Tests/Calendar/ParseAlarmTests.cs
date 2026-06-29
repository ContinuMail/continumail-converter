// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class ParseAlarmTests
{
    [Fact]
    public void Before_start_relative_15m()
    {
        var a = ICalTextParser.ParseAlarm("BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nEND:VALARM").Value!;
        Assert.Equal("DISPLAY", a.Action);
        Assert.Equal(TimeSpan.FromMinutes(-15), a.RelativeOffset);
    }

    [Theory]
    [InlineData("-P1D")]
    [InlineData("-PT7H")]
    [InlineData("PT9H")]
    public void Various_relative_triggers_parse(string trig)
        => Assert.NotNull(ICalTextParser.ParseAlarm($"BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:{trig}\r\nEND:VALARM").Value!.RelativeOffset);

    [Fact]
    public void Related_end_is_preserved()
    {
        var a = ICalTextParser.ParseAlarm("BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;RELATED=END:-PT15M\r\nEND:VALARM").Value!;
        Assert.Equal("END", a.Related);
    }

    [Fact]
    public void Absolute_trigger_sets_utc()
    {
        var a = ICalTextParser.ParseAlarm("BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;VALUE=DATE-TIME:20260701T070000Z\r\nEND:VALARM").Value!;
        Assert.NotNull(a.AbsoluteTimeUtc);
    }

    [Fact]
    public void Description_is_preserved()
    {
        var a = ICalTextParser.ParseAlarm(
            "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nDESCRIPTION:Reminder text\r\nEND:VALARM").Value!;
        Assert.Equal("Reminder text", a.Description);
    }
}
