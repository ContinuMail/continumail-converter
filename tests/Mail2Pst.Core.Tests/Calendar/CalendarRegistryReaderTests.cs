// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class CalendarRegistryReaderTests
{
    private const string Prefs = """
        user_pref("calendar.registry.4ad842d0.name", "Ferie \u00e6\u00f8\u00e5");
        user_pref("calendar.registry.4ad842d0.type", "storage");
        user_pref("calendar.registry.4ad842d0.uri", "moz-storage-calendar://");
        user_pref("calendar.registry.4ad842d0.calendar-main-in-composite", true);
        user_pref("calendar.registry.007f8267.name", "work@example.com");
        user_pref("calendar.registry.007f8267.type", "caldav");
        // user_pref("calendar.registry.ignored.name", "Commented");
        """;

    [Fact]
    public void Parses_entries_with_unicode_names_and_default_visibility()
    {
        var entries = CalendarRegistryReader.ParseText(Prefs);
        Assert.Equal(2, entries.Count);

        var home = entries.Single(e => e.CalId == "4ad842d0");
        Assert.Equal("Ferie \u00e6\u00f8\u00e5", home.DisplayName);   // \uXXXX unescaped via PrefsJsEscape
        Assert.Equal("storage", home.CalendarType);
        Assert.True(home.VisibleInThunderbird);

        var work = entries.Single(e => e.CalId == "007f8267");
        Assert.False(work.VisibleInThunderbird);        // no calendar-main-in-composite => default false
    }

    [Fact]
    public void Commented_lines_are_ignored()
        => Assert.DoesNotContain(CalendarRegistryReader.ParseText(Prefs), e => e.DisplayName == "Commented");
}
