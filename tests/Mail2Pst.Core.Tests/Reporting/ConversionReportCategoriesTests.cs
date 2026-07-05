// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Reporting;
using Xunit;

public class ConversionReportCategoriesTests
{
    [Fact]
    public void Accumulates_distinct_first_casing_preserved()
    {
        var r = new ConversionReport();
        r.RecordCalendarCategories(new[] { "Meeting", "Suppliers" });
        r.RecordCalendarCategories(new[] { "meeting", "", "   " }); // dup (case-insensitive) + empty + whitespace-only ignored
        Assert.Equal(new[] { "Meeting", "Suppliers" }, r.CalendarCategoryNames);
    }

    [Fact]
    public void Empty_by_default()
    {
        Assert.Empty(new ConversionReport().CalendarCategoryNames);
    }
}
