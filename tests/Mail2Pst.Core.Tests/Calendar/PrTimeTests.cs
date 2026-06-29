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
}
