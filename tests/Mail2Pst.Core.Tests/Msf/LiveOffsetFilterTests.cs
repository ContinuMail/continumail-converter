// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class LiveOffsetFilterTests
{
    private const long Len = 1_000_000;

    [Fact]
    public void AllAligned_Active_KeepsLiveIndicesOnly()
    {
        // physical: 4 dead (low) + 4 live (high); .msf live = the high 4.
        var physical = new List<long> { 0, 100, 200, 300, 542614, 683025, 885105, 911121 };
        var live = new List<long?> { 542614, 683025, 885105, 911121 };
        var r = LiveOffsetFilter.Evaluate(live, physical, Len);
        Assert.True(r.Active);
        Assert.Equal(new HashSet<int> { 4, 5, 6, 7 }, r.KeepIndices);
        Assert.Equal(4, r.MatchedOffsets);
    }

    [Fact]
    public void EmptyLiveSet_Disabled_NeverDropsAll()
    {
        var r = LiveOffsetFilter.Evaluate(new List<long?>(), new List<long> { 0, 100 }, Len);
        Assert.False(r.Active);
        Assert.Equal("empty live set", r.DisabledReason);
    }

    [Fact]
    public void RowWithoutUsableOffset_Disabled()
    {
        var r = LiveOffsetFilter.Evaluate(
            new List<long?> { 0, null, 200 }, new List<long> { 0, 100, 200 }, Len);
        Assert.False(r.Active);
        Assert.Equal("row without usable offset", r.DisabledReason);
    }

    [Fact]
    public void OffsetBeyondFileLength_NotUsable_Disabled()
    {
        var r = LiveOffsetFilter.Evaluate(
            new List<long?> { 0, 2_000_000 }, new List<long> { 0, 100 }, Len);
        Assert.False(r.Active);
        Assert.Equal("row without usable offset", r.DisabledReason);
    }

    [Fact]
    public void OffsetNotAtBoundary_Disabled_WithSample()
    {
        var r = LiveOffsetFilter.Evaluate(
            new List<long?> { 100, 999 }, new List<long> { 0, 100, 200 }, Len);
        Assert.False(r.Active);
        Assert.Equal("live offset did not match an mbox boundary", r.DisabledReason);
        Assert.Contains(999L, r.UnmatchedSample);
    }

    [Fact]
    public void DuplicateLiveOffsets_Active_KeptOnce_AndCounted()
    {
        var r = LiveOffsetFilter.Evaluate(
            new List<long?> { 200, 200 }, new List<long> { 0, 100, 200 }, Len);
        Assert.True(r.Active);
        Assert.Equal(new HashSet<int> { 2 }, r.KeepIndices);
        Assert.Equal(1, r.DuplicateLiveOffsets);
    }
}
