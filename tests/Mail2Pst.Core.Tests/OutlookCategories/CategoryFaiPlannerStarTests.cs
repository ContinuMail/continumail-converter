// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;
using System.Text.RegularExpressions;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryFaiPlannerStarTests
{
    private static string Xml(byte[]? bytes) => bytes is null ? string.Empty : Encoding.UTF8.GetString(bytes);

    [Fact]
    public void BuildXmlBytes_IncludeStar_NoOtherCategories_BakesYellowStar()
    {
        byte[]? bytes = CategoryFaiPlanner.BuildXmlBytes(
            profilePath: null, categoryNames: Array.Empty<string>(), includeStarCategory: true);

        Assert.NotNull(bytes);
        var colours = CategoryListXml.ReadNameToColourIndex(Xml(bytes));
        Assert.True(colours.TryGetValue("Star", out int c));
        Assert.Equal(4, c); // OlCategoryColor 4 = Yellow
    }

    [Fact]
    public void BuildXmlBytes_ExcludeStar_NoOtherCategories_ReturnsNull()
    {
        Assert.Null(CategoryFaiPlanner.BuildXmlBytes(
            profilePath: null, categoryNames: Array.Empty<string>(), includeStarCategory: false));
    }

    [Fact]
    public void BuildXmlBytes_IncludeStar_AlongsideCalendarCategory_BothPresent()
    {
        byte[]? bytes = CategoryFaiPlanner.BuildXmlBytes(
            profilePath: null, categoryNames: new[] { "Meeting" }, includeStarCategory: true);

        var names = CategoryListXml.ReadNames(Xml(bytes));
        Assert.Contains("Meeting", names);
        Assert.Contains("Star", names);
    }

    [Fact]
    public void BuildXmlBytes_IncludeStar_WhenCategoryNamedStarExists_NotDuplicated()
    {
        // A real category already named "Star" (e.g. a calendar category) keeps its own colour and is
        // NOT duplicated by the synthetic yellow one.
        byte[]? bytes = CategoryFaiPlanner.BuildXmlBytes(
            profilePath: null, categoryNames: new[] { "Star" }, includeStarCategory: true);

        int count = Regex.Matches(Xml(bytes), "name=\"Star\"", RegexOptions.IgnoreCase).Count;
        Assert.Equal(1, count);
    }
}
