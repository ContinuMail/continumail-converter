// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

public class CalendarCategoryColorResolverTests
{
    private static readonly Dictionary<string, string> NoOverrides = new();

    [Fact]
    public void Uses_hashColor_when_no_override()
    {
        var r = CalendarCategoryColorResolver.Resolve(new[] { "Meeting", "Suppliers" }, NoOverrides);
        Assert.Equal("#FFFF66", r["Meeting"]);
        Assert.Equal("#000099", r["Suppliers"]);
    }

    [Fact]
    public void Override_wins_over_hashColor()
    {
        var overrides = new Dictionary<string, string> { ["meeting"] = "#010203" }; // key = mangled "Meeting"
        var r = CalendarCategoryColorResolver.Resolve(new[] { "Meeting" }, overrides);
        Assert.Equal("#010203", r["Meeting"]);
    }

    [Fact]
    public void Case_insensitive_dedup_first_occurrence_wins()
    {
        var r = CalendarCategoryColorResolver.Resolve(new[] { "Vacation", "vacation" }, NoOverrides);
        Assert.Single(r);
        Assert.True(r.ContainsKey("Vacation")); // first occurrence casing kept
    }
}
