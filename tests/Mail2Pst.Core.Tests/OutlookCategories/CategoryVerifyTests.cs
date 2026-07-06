// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryVerifyTests
{
    private static CategoryCandidate C(string name, int? colour, string action) => new(name, null, colour, action);

    [Fact]
    public void ExpectedAdded_KeepsOnlyAddedWithColour()
    {
        var results = new[]
        {
            C("Work", 5, "added"),
            C("Home", 3, "skipped-existing"),
            C("NoColour", null, "skipped-no-colour"),
        };
        var expected = CategoryVerify.ExpectedAdded(results);
        Assert.Single(expected);
        Assert.Equal(5, expected["Work"]);
    }

    [Fact]
    public void Missing_FlagsAbsentAndWrongColour()
    {
        var expected = new Dictionary<string, int> { ["Work"] = 5, ["Home"] = 3, ["Ops"] = 7 };
        var actual = new Dictionary<string, int> { ["Work"] = 5, ["Home"] = 9 /* wrong */ };
        var missing = CategoryVerify.Missing(expected, actual);
        Assert.Contains("Home", missing); // wrong colour
        Assert.Contains("Ops", missing);  // absent
        Assert.DoesNotContain("Work", missing);
    }

    [Fact]
    public void Missing_AllPresent_IsEmpty()
    {
        var expected = new Dictionary<string, int> { ["Work"] = 5 };
        var actual = new Dictionary<string, int> { ["WORK"] = 5 }; // note: case-SENSITIVE dict on purpose
        Assert.Empty(CategoryVerify.Missing(expected, actual));    // Missing must normalize internally
    }

    [Fact]
    public void Missing_EmptyExpected_IsEmpty() =>
        Assert.Empty(CategoryVerify.Missing(new Dictionary<string, int>(), new Dictionary<string, int>()));

    [Fact]
    public void MergeAdded_UnionsAcrossAttempts()
    {
        var acc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        CategoryVerify.MergeAdded(acc, new[] { C("Work", 5, "added") });
        // attempt 2 sees Work as already-present, but adds Ops
        CategoryVerify.MergeAdded(acc, new[] { C("Work", 5, "skipped-existing"), C("Ops", 7, "added") });
        Assert.Equal(5, acc["Work"]); // retained from attempt 1 even though attempt 2 skipped it
        Assert.Equal(7, acc["Ops"]);
    }
}
