// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class IcalParseSupportTests
{
    [Fact]
    public void Unfold_joins_continuation_lines()
        => Assert.Equal("ATTENDEE:mailto:folded@example.com",
            IcalParseSupport.UnfoldIcalLines("ATTENDEE:mailto:\r\n folded@example.com"));

    [Fact]
    public void WrapVevent_embeds_body_in_a_loadable_calendar()
    {
        var ics = IcalParseSupport.WrapVevent("SUMMARY:Hi");
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("DTSTART:", ics);
        Assert.Contains("SUMMARY:Hi", ics);
        Assert.Equal("Hi", Ical.Net.Calendar.Load(ics).Events[0].Summary);
    }

    [Fact]
    public void ParseResult_fail_carries_warning_and_null_value()
    {
        var r = ParseResult<string>.Fail("bad");
        Assert.Null(r.Value);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void ParseResult_ok_carries_value_and_no_warnings()
    {
        var r = ParseResult<string>.Ok("hello");
        Assert.Equal("hello", r.Value);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Unfold_handles_tab_continuation()
        // RFC-5545: the leading whitespace (fold marker) is stripped; "long" + "text" join directly.
        => Assert.Equal("DESCRIPTION:longtext here",
            IcalParseSupport.UnfoldIcalLines("DESCRIPTION:long\r\n\ttext here"));

    [Fact]
    public void Unfold_normalises_bare_cr_line_endings()
        => Assert.Equal("A:1\r\nB:2",
            IcalParseSupport.UnfoldIcalLines("A:1\rB:2"));

    [Fact]
    public void Unfold_normalises_bare_lf_line_endings()
        => Assert.Equal("A:1\r\nB:2",
            IcalParseSupport.UnfoldIcalLines("A:1\nB:2"));

    [Fact]
    public void WrapVevent_includes_required_calendar_structure()
    {
        var ics = IcalParseSupport.WrapVevent("SUMMARY:Test");
        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains("VERSION:2.0", ics);
        Assert.Contains("END:VEVENT", ics);
        Assert.Contains("END:VCALENDAR", ics);
    }

    [Fact]
    public void Unfold_joins_three_continuation_lines()
        // A value folded across three lines — all three segments join with no separator.
        => Assert.Equal("DESCRIPTION:abcdef",
            IcalParseSupport.UnfoldIcalLines("DESCRIPTION:ab\r\n cd\r\n ef"));

    [Fact]
    public void Unfold_handles_fold_at_value_start()
        // Fold immediately after the colon: value starts on the continuation line.
        => Assert.Equal("DESCRIPTION:value",
            IcalParseSupport.UnfoldIcalLines("DESCRIPTION:\r\n value"));
}
