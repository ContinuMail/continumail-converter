// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class RecurrenceMappingTests
{
    [Fact]
    public void FromIcal_weekly_count_maps_spec()
    {
        var (spec, reason) = RecurrenceMapping.FromIcal(
            new[] { "RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=5" },
            new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 6, 0, 0, 0), null, "UTC");
        Assert.Null(reason);
        Assert.NotNull(spec);
        Assert.Equal(RecurrenceFrequency.Weekly, spec!.Frequency);
        Assert.Equal(RecurrenceEndKind.Count, spec.EndKind);
        Assert.Equal(5, spec.Count);
    }

    [Fact]
    public void FromIcal_bysetpos_degrades()
    {
        var (spec, reason) = RecurrenceMapping.FromIcal(
            new[] { "RRULE:FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1" },
            new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 6, 0, 0, 0), null, "UTC");
        Assert.Null(spec);
        Assert.Contains("BYSETPOS", reason);
    }

    /// <summary>
    /// Pre-merge review #4: a negative BYMONTHDAY (-1 = last day of month; emitted by Google Calendar)
    /// is not representable as a fixed day-of-month — <c>(uint)(-1)</c> would corrupt the recurrence
    /// blob. It must degrade to a single occurrence + warning, not silently map.
    /// </summary>
    [Fact]
    public void FromIcal_negative_bymonthday_monthly_degrades()
    {
        var (spec, reason) = RecurrenceMapping.FromIcal(
            new[] { "RRULE:FREQ=MONTHLY;BYMONTHDAY=-1;COUNT=3" },
            new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 0, 0, 0), null, "UTC");
        Assert.Null(spec);
        Assert.Contains("BYMONTHDAY", reason);
    }

    /// <summary>
    /// Mutation-coverage (Stryker): pins the exact valid BYMONTHDAY boundaries. Both 1 and 31 must MAP
    /// (they are in range). This kills the surviving `&lt;1`→`&lt;=1`, `&gt;31`→`&gt;=31`, and
    /// `&gt;31`→`&lt;31` boundary mutations — each would wrongly degrade day 1 or day 31. (Out-of-range
    /// values like 32/0 are rejected by the iCal parser upstream, so they never reach this guard.)
    /// </summary>
    [Theory]
    [InlineData(1)]    // first of month — must map, not degrade
    [InlineData(31)]   // 31st — upper boundary, must map
    public void FromIcal_valid_bymonthday_boundaries_map(int day)
    {
        var (spec, reason) = RecurrenceMapping.FromIcal(
            new[] { $"RRULE:FREQ=MONTHLY;BYMONTHDAY={day};COUNT=3" },
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 15, 0, 0, 0), null, "UTC");

        Assert.NotNull(spec);
        Assert.Null(reason);
        Assert.Equal(day, spec!.DayOfMonth);
    }

    /// <summary>Pre-merge review #4: negative BYMONTHDAY on a yearly rule degrades too.</summary>
    [Fact]
    public void FromIcal_negative_bymonthday_yearly_degrades()
    {
        var (spec, reason) = RecurrenceMapping.FromIcal(
            new[] { "RRULE:FREQ=YEARLY;BYMONTH=12;BYMONTHDAY=-1;COUNT=3" },
            new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 0, 0, 0), null, "UTC");
        Assert.Null(spec);
        Assert.Contains("BYMONTHDAY", reason);
    }
}
