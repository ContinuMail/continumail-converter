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
}
