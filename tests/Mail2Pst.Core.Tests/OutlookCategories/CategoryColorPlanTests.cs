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
}
