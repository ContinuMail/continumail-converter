// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class PrTimeTests
{
    [Fact]
    public void Converts_microseconds_since_epoch_to_utc()
        => Assert.Equal(new DateTimeOffset(2026, 6, 30, 9, 0, 0, TimeSpan.Zero),
                        PrTime.FromMicros(1782810000000000L));

    [Fact]
    public void Null_passes_through() => Assert.Null(PrTime.FromMicros(null));

    /// <summary>
    /// Should-fix (SF-E): PRTime is microseconds — sub-millisecond precision must be preserved
    /// (the old `micros / 1000` truncated it). 1234 µs = 12,340 ticks past the epoch.
    /// </summary>
    [Fact]
    public void Preserves_sub_millisecond_precision()
    {
        var r = PrTime.FromMicros(1234L);
        Assert.NotNull(r);
        Assert.Equal(12_340L, (r!.Value - DateTimeOffset.UnixEpoch).Ticks);
    }

    /// <summary>
    /// Should-fix (SF-E): pre-1970 (negative) micros must not mis-round toward zero.
    /// -1234 µs = -12,340 ticks (the old truncating division gave -10,000).
    /// </summary>
    [Fact]
    public void Pre_epoch_negative_micros_do_not_misround()
    {
        var r = PrTime.FromMicros(-1234L);
        Assert.NotNull(r);
        Assert.Equal(-12_340L, (r!.Value - DateTimeOffset.UnixEpoch).Ticks);
    }
}
