// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class OlCategoryColorMapTests
{
    [Theory]
    [InlineData("#FF0000", 1)]   // TB Important -> Red
    [InlineData("#FF9900", 2)]   // TB Work     -> Orange
    [InlineData("#009900", 20)]  // TB Personal -> Dark green (nearest-RGB, not Green)
    [InlineData("#3333FF", 8)]   // TB To Do    -> Blue
    [InlineData("#993399", 10)]  // TB Later    -> Maroon (nearest-RGB, not Purple)
    [InlineData("#D6252E", 1)]   // exact palette Red (214,37,46) -> Red
    [InlineData("#1c1c1c", 15)]  // near-black -> Black (case-insensitive, no leading-issue)
    public void MapsHexToNearestOlCategoryColor(string hex, int expected)
        => Assert.Equal(expected, NearestOrFail(hex));

    [Fact]
    public void NeverReturnsNone()
        => Assert.NotEqual(0, NearestOrFail("#FFFFFF")); // white -> nearest real swatch, not None(0)

    [Theory]
    [InlineData("red")]
    [InlineData("#F00")]
    [InlineData("")]
    [InlineData("#GGGGGG")]
    public void RejectsInvalidHex(string hex)
        => Assert.False(OlCategoryColorMap.TryNearestIndex(hex, out _));

    private static int NearestOrFail(string hex)
    {
        Assert.True(OlCategoryColorMap.TryNearestIndex(hex, out int idx));
        return idx;
    }
}
