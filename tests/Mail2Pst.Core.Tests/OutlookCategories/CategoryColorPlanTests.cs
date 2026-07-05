// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryColorPlanTests
{
    private static Dictionary<string, string> D(params (string k, string v)[] kv)
    {
        var d = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void BuiltinLabels_AlwaysPresent_WithDefaultColours_WhenNoPrefs()
    {
        var plan = CategoryColorPlan.Build(D(), D());
        CategoryCandidate label1 = plan.Single(c => c.Name == "Important");
        Assert.Equal("would-add", label1.Action);
        Assert.Equal(1, label1.OutlookColor);   // #FF0000 -> Red
        Assert.Contains(plan, c => c.Name == "Later" && c.OutlookColor == 10); // #993399 -> Maroon
    }

    [Fact]
    public void PrefsName_And_PrefsColour_Override_Builtin()
    {
        var plan = CategoryColorPlan.Build(D(("$label1", "Critique")), D(("$label1", "#00FF00")));
        CategoryCandidate c = plan.Single(x => x.Name == "Critique");
        Assert.Equal("would-add", c.Action);
        Assert.Equal(5, c.OutlookColor);   // #00FF00 (0,255,0) nearest -> Green(5)
        Assert.DoesNotContain(plan, x => x.Name == "Important"); // $label1 resolved to Critique, not Important
    }

    [Fact]
    public void CustomTag_WithColour_Included()
    {
        var plan = CategoryColorPlan.Build(D(("proj", "Client X")), D(("proj", "#3333FF")));
        Assert.Contains(plan, c => c.Name == "Client X" && c.OutlookColor == 8 && c.Action == "would-add");
    }

    [Fact]
    public void CustomTag_NameButNoColour_SkippedNoColour()
    {
        var plan = CategoryColorPlan.Build(D(("proj", "Client X")), D());
        CategoryCandidate c = plan.Single(x => x.Name == "Client X");
        Assert.Equal("skipped-no-colour", c.Action);
        Assert.Null(c.OutlookColor);
    }

    [Fact]
    public void NameWithComma_SkippedInvalidName()
    {
        var plan = CategoryColorPlan.Build(D(("proj", "Foo, Bar")), D(("proj", "#FF0000")));
        Assert.Equal("skipped-invalid-name", plan.Single(x => x.Name == "Foo, Bar").Action);
    }

    [Fact]
    public void NameTooLong_SkippedInvalidName()
    {
        string longName = new string('x', 256);
        var plan = CategoryColorPlan.Build(D(("proj", longName)), D(("proj", "#FF0000")));
        Assert.Equal("skipped-invalid-name", plan.Single(x => x.Name == longName).Action);
    }

    [Fact]
    public void NonJunk_IsFiltered_NotACandidate()
    {
        var plan = CategoryColorPlan.Build(D(("NonJunk", "NonJunk")), D(("NonJunk", "#FF0000")));
        Assert.DoesNotContain(plan, c => c.Name == "NonJunk");
    }
}

public class CategoryColorPlanCalendarMergeTests
{
    private static readonly Dictionary<string, string> NoMailNames = new();
    private static readonly Dictionary<string, string> NoMailColors = new();

    [Fact]
    public void Calendar_only_category_is_added_would_add()
    {
        var cal = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["Meeting"] = "#FFFF66" };
        var plan = CategoryColorPlan.Build(NoMailNames, NoMailColors, cal);
        var m = System.Linq.Enumerable.Single(plan, c => c.Name == "Meeting");
        Assert.Equal("would-add", m.Action);
        Assert.NotNull(m.OutlookColor);
    }

    [Fact]
    public void Coloured_mail_tag_wins_over_calendar_same_name()
    {
        var mailNames = new Dictionary<string, string> { ["$label1"] = "Work" };
        var mailColors = new Dictionary<string, string> { ["$label1"] = "#FF0000" };
        var cal = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["Work"] = "#00FF00" };
        var plan = CategoryColorPlan.Build(mailNames, mailColors, cal);
        var work = System.Linq.Enumerable.Single(plan, c => c.Name == "Work");
        Assert.Equal("#FF0000", work.Hex); // mail colour kept
    }

    [Fact]
    public void Uncoloured_mail_tag_is_upgraded_by_calendar_colour()
    {
        var mailNames = new Dictionary<string, string> { ["custom1"] = "Trips" };
        var mailColors = new Dictionary<string, string>(); // no colour for "Trips"
        var cal = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["Trips"] = "#3333FF" };
        var plan = CategoryColorPlan.Build(mailNames, mailColors, cal);
        var trips = System.Linq.Enumerable.Single(plan, c => c.Name == "Trips");
        Assert.Equal("would-add", trips.Action);
        Assert.Equal("#3333FF", trips.Hex); // upgraded in place
    }

    [Fact]
    public void Two_arg_and_empty_calendar_three_arg_are_identical_builtins_and_custom()
    {
        // No-regression: a realistic mail-tag set (a renamed built-in label + a custom tag) must produce
        // an IDENTICAL plan via the 2-arg path and the 3-arg path with an empty calendar map.
        var mailNames = new Dictionary<string, string> { ["$label2"] = "Work", ["custom_a"] = "Receipts" };
        var mailColors = new Dictionary<string, string> { ["$label2"] = "#FF0000", ["custom_a"] = "#00FF00" };
        var viaTwo = CategoryColorPlan.Build(mailNames, mailColors);
        var viaThree = CategoryColorPlan.Build(mailNames, mailColors,
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            System.Linq.Enumerable.Select(viaTwo, c => (c.Name, c.Hex, c.OutlookColor, c.Action)),
            System.Linq.Enumerable.Select(viaThree, c => (c.Name, c.Hex, c.OutlookColor, c.Action)));
        // And it is non-trivial: the built-in $labelN set plus the custom tag all appear.
        Assert.Contains(viaTwo, c => c.Name == "Work");
        Assert.Contains(viaTwo, c => c.Name == "Receipts");
    }
}
