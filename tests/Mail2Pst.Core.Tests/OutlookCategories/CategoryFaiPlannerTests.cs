// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Text;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryFaiPlannerTests
{
    private static string WriteProfile(string prefsBody)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"prof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "prefs.js"), prefsBody);
        return dir;
    }

    [Fact]
    public void No_profile_and_no_categories_returns_null_bytes()
    {
        // Nothing real to colour (plain mbox, no profile) → no FAI.
        Assert.Null(CategoryFaiPlanner.BuildXmlBytes(null, Array.Empty<string>()));
        Assert.Null(CategoryFaiPlanner.BuildXmlBytes("", Array.Empty<string>()));
    }

    [Fact]
    public void Calendar_categories_are_baked_even_without_a_profile()
    {
        // CalendarCategoryColorResolver derives a hash colour per category name, so calendar/task
        // categories must still bake with no profile / no prefs.js.
        byte[]? nullProfile = CategoryFaiPlanner.BuildXmlBytes(null, new[] { "Meeting" });
        Assert.NotNull(nullProfile);
        Assert.Contains("name=\"Meeting\"", Encoding.UTF8.GetString(nullProfile!));

        string missingProfile = Path.Combine(Path.GetTempPath(), $"noprefs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(missingProfile);   // exists, but has no prefs.js
        byte[]? noPrefs = CategoryFaiPlanner.BuildXmlBytes(missingProfile, new[] { "Meeting" });
        Assert.NotNull(noPrefs);
        Assert.Contains("name=\"Meeting\"", Encoding.UTF8.GetString(noPrefs!));
    }

    [Fact]
    public void Mail_tag_with_colour_is_baked_even_without_calendar_categories()
    {
        // A coloured mail tag defined in prefs.js -> the FAI must include it (mail-only case).
        string profile = WriteProfile(
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n");
        byte[]? bytes = CategoryFaiPlanner.BuildXmlBytes(profile, Array.Empty<string>());
        Assert.NotNull(bytes);
        string xml = Encoding.UTF8.GetString(bytes!);
        Assert.Contains("name=\"Important\"", xml);
    }

    [Fact]
    public void Calendar_category_colour_is_baked()
    {
        // A calendar category override colour -> present in the baked list.
        string profile = WriteProfile(
            "user_pref(\"calendar.category.color.meeting\", \"#00FF00\");\n");
        byte[]? bytes = CategoryFaiPlanner.BuildXmlBytes(profile, new[] { "Meeting" });
        Assert.NotNull(bytes);
        Assert.Contains("name=\"Meeting\"", Encoding.UTF8.GetString(bytes!));
    }
}
