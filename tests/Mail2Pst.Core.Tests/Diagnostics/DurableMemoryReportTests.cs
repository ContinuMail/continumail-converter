// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Diagnostics;
using Xunit;

namespace Mail2Pst.Core.Tests.Diagnostics;

public class DurableMemoryReportTests
{
    private static DurableMemoryReport Report(params FamilyResidency[] f) =>
        new DurableMemoryReport(f, FileLengthBytes: 1_000_000, MessagesWritten: 500);

    [Fact]
    public void TotalsAndRatio_SumAcrossFamilies()
    {
        var r = Report(
            new FamilyResidency("blockBuffer", 3, PayloadBytes: 800, PendingBytes: 100, EvictableBytes: 600, PinnedBytes: 100),
            new FamilyResidency("pageBuffer", 2, PayloadBytes: 200, PendingBytes: 0, EvictableBytes: 0, PinnedBytes: 200));
        Assert.Equal(1000, r.TotalDurableBytes);           // 800 + 200 payload
        Assert.Equal(600, r.EvictableBytes);
        Assert.Equal(0.60, r.EvictableRatio, 3);
    }

    [Fact]
    public void Classification_EvictableLeafDominant_WhenRatioAtOrAboveHalf()
    {
        var r = Report(new FamilyResidency("blockBuffer", 1, 1000, 0, 600, 400));
        Assert.Equal("evictable-leaf-dominant", r.Classification);   // >= 0.50 -> warrants follow-up prune spec
    }

    [Fact]
    public void Classification_PinnedDominant_WhenRatioBelowHalf()
    {
        var r = Report(new FamilyResidency("pageBuffer", 1, 1000, 0, 100, 900));
        Assert.Equal("pinned-dominant", r.Classification);           // < 0.50 -> ship as measured residual, no prune
    }

    [Fact]
    public void EvictableRatio_ZeroDurableBytes_IsZeroNotNaN()
    {
        var r = Report(new FamilyResidency("blockBuffer", 0, 0, 0, 0, 0));
        Assert.Equal(0.0, r.EvictableRatio, 3);
        Assert.Equal("pinned-dominant", r.Classification);           // no evictable term -> not prune-warranted
    }
}
