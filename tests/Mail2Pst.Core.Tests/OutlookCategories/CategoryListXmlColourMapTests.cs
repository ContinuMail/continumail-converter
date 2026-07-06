// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryListXmlColourMapTests
{
    [Fact]
    public void ReadNameToColourIndex_RoundTripsAppendedColours()
    {
        // Append writes color = OlCategoryColor - 1; the reader must invert that back to 1..25.
        string xml = CategoryListXml.Append(string.Empty, new (string, int)[] { ("Work", 5), ("Home", 25) });
        var map = CategoryListXml.ReadNameToColourIndex(xml);
        Assert.Equal(5, map["Work"]);
        Assert.Equal(25, map["Home"]);
    }

    [Fact]
    public void ReadNameToColourIndex_IsCaseInsensitiveOnNames()
    {
        string xml = CategoryListXml.Append(string.Empty, new (string, int)[] { ("Work", 3) });
        var map = CategoryListXml.ReadNameToColourIndex(xml);
        Assert.True(map.ContainsKey("WORK"));
    }

    [Fact]
    public void ReadNameToColourIndex_EmptyInput_IsEmpty() =>
        // Confirmed against the real CategoryListXml.Load: empty/whitespace synthesizes an empty
        // <categories> root (same path ReadNames("") relies on) — not an assumption.
        Assert.Empty(CategoryListXml.ReadNameToColourIndex(""));

    [Fact]
    public void ReadNameToColourIndex_Malformed_Throws() =>
        Assert.Throws<FormatException>(() => CategoryListXml.ReadNameToColourIndex("<not-closed"));

    [Theory]
    [InlineData("-1")]   // below the 0-based range
    [InlineData("25")]   // above (0-based max is 24 → OlCategoryColor 25)
    [InlineData("abc")]  // unparseable
    [InlineData("")]     // missing/empty color attribute
    public void ReadNameToColourIndex_OutOfRangeOrInvalidColour_SkipsCategory(string colour)
    {
        string xml = $"<categories><category name=\"X\" color=\"{colour}\" /></categories>";
        Assert.DoesNotContain("X", CategoryListXml.ReadNameToColourIndex(xml).Keys);
    }
}
