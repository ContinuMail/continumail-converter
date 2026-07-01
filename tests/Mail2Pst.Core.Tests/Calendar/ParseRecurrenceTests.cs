// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class ParseRecurrenceTests
{
    [Fact]
    public void Weekly_byday_interval_count_with_structured_exdate()
    {
        var r = ICalTextParser.ParseRecurrence(new[] {
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;INTERVAL=2;COUNT=10",
            "EXDATE;TZID=(no TZ description);VALUE=DATE:20250516" });
        Assert.Empty(r.Warnings);
        var v = r.Value!;
        Assert.Equal(ParsedFrequency.Weekly, v.Frequency);
        Assert.Equal(2, v.Interval);
        Assert.Equal(10, v.Count);
        Assert.Contains(v.ByDay, d => d.DayOfWeek == System.DayOfWeek.Monday);
        var ex = Assert.Single(v.ExDates);
        Assert.Equal("(no TZ description)", ex.TzId);
        Assert.True(ex.IsDateOnly);
        Assert.Equal("20250516", ex.Values.Single());
    }

    [Fact]
    public void Rrule_with_trailing_crlf_parses_successfully()
    {
        // Real Thunderbird cal_recurrence stores each property with a trailing CRLF line
        // terminator. The final token (WKST=SU) must not carry the "\r\n" into Ical.Net,
        // which rejects "SU\r\n" as an invalid day-of-week indicator.
        var r = ICalTextParser.ParseRecurrence(new[] {
            "RRULE:FREQ=WEEKLY;BYDAY=MO;UNTIL=20260901T120000Z;WKST=SU\r\n" });
        Assert.Empty(r.Warnings);
        var v = r.Value!;
        Assert.Equal(ParsedFrequency.Weekly, v.Frequency);
        Assert.Equal(System.DayOfWeek.Monday, v.ByDay.Single().DayOfWeek);
        Assert.NotNull(v.UntilUtc);
    }

    [Fact]
    public void Exdate_with_trailing_crlf_value_is_clean()
    {
        // The trailing CRLF must be stripped from the EXDATE value, or the downstream
        // date parse (yyyyMMddTHHmmss) fails and the deletion is silently lost.
        var r = ICalTextParser.ParseRecurrence(new[] {
            "RRULE:FREQ=WEEKLY;BYDAY=MO\r\n",
            "EXDATE;TZID=Europe/Copenhagen:20260706T140000\r\n" });
        Assert.Empty(r.Warnings);
        var ex = Assert.Single(r.Value!.ExDates);
        Assert.Equal("Europe/Copenhagen", ex.TzId);
        Assert.Equal("20260706T140000", ex.Values.Single());
    }

    [Theory]
    [InlineData("RRULE:FREQ=DAILY", ParsedFrequency.Daily)]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=2MO;COUNT=5", ParsedFrequency.Monthly)]
    [InlineData("RRULE:FREQ=YEARLY;BYMONTH=5;BYMONTHDAY=23", ParsedFrequency.Yearly)]
    [InlineData("RRULE:FREQ=WEEKLY;UNTIL=20261231T235959Z", ParsedFrequency.Weekly)]
    public void Parses_frequency_variants(string rrule, ParsedFrequency expected)
        => Assert.Equal(expected, ICalTextParser.ParseRecurrence(new[]{rrule}).Value!.Frequency);

    [Fact]
    public void Monthly_byday_keeps_offset()
    {
        var v = ICalTextParser.ParseRecurrence(new[]{"RRULE:FREQ=MONTHLY;BYDAY=2MO;COUNT=5"}).Value!;
        Assert.Equal(2, v.ByDay.Single().Offset);
    }

    [Fact]
    public void Rdate_is_structured()
    {
        var v = ICalTextParser.ParseRecurrence(new[]{
            "RRULE:FREQ=WEEKLY",
            "RDATE;TZID=Europe/Copenhagen:20260701T090000,20260708T090000"}).Value!;
        var rd = Assert.Single(v.RDates);
        Assert.Equal("Europe/Copenhagen", rd.TzId);
        Assert.Equal(2, rd.Values.Count);
    }

    [Fact]
    public void No_rrule_line_returns_null_value_no_warning()
    {
        var r = ICalTextParser.ParseRecurrence(new[]{"EXDATE:20260706"});
        Assert.Null(r.Value);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Weekly_byday_plain_has_null_offset()
    {
        var v = ICalTextParser.ParseRecurrence(new[]{"RRULE:FREQ=WEEKLY;BYDAY=MO"}).Value!;
        Assert.Null(v.ByDay.Single().Offset);
    }

    [Fact]
    public void Malformed_rrule_returns_warning_not_throw()
        => Assert.NotEmpty(ICalTextParser.ParseRecurrence(new[]{"RRULE:FREQ=GARBAGE;;;"}).Warnings);

    [Fact]
    public void ParseRecurrence_surfaces_bysetpos()
    {
        var r = ICalTextParser.ParseRecurrence(new[] { "RRULE:FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1" });
        Assert.NotNull(r.Value);
        Assert.Equal(new[] { -1 }, r.Value!.BySetPosition);
    }
}
