// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.OutlookCategories;
using Xunit;

public class CalendarCategoryOverrideReaderTests
{
    [Fact]
    public void Reads_category_color_overrides()
    {
        string prefs =
            "user_pref(\"calendar.category.color.meeting\", \"#123456\");\n" +
            "user_pref(\"calendar.category.color.follow_up\", \"AABBCC\");\n"; // no leading # -> normalised
        var map = CalendarCategoryOverrideReader.ParseText(prefs);
        Assert.Equal("#123456", map["meeting"]);
        Assert.Equal("#AABBCC", map["follow_up"]);
    }

    [Fact]
    public void Ignores_per_calendar_source_colours_and_unrelated_prefs()
    {
        // The owner's real shape: per-calendar source colours + mail-tag colours, NO category colours.
        string prefs =
            "user_pref(\"calendar.registry.94e695bc.color\", \"#ff0080\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n";
        Assert.Empty(CalendarCategoryOverrideReader.ParseText(prefs));
    }

    [Fact]
    public void Tolerates_whitespace_and_optional_semicolon()
    {
        string prefs = "  user_pref( \"calendar.category.color.follow_up\" , \"#AABBCC\" ) ;  \n";
        Assert.Equal("#AABBCC", CalendarCategoryOverrideReader.ParseText(prefs)["follow_up"]);
    }
}
