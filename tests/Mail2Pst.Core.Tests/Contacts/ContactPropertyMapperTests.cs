// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Contacts;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

public class ContactPropertyMapperTests
{
    [Fact]
    public void Map_ThunderbirdKeys_PopulatesContactRecord()
    {
        var props = new Dictionary<string, string>
        {
            ["DisplayName"] = "Alice Smith",
            ["FirstName"] = "Alice",
            ["LastName"] = "Smith",
            ["PrimaryEmail"] = "alice@example.com",
            ["SecondEmail"] = "a2@example.com",
            ["Company"] = "Acme",
            ["JobTitle"] = "Engineer",
            ["WorkPhone"] = "+47 111",
            ["HomeCity"] = "Oslo",
            ["Notes"] = "hello",
        };
        var c = new ContactPropertyMapper().Map(props, "card-1");

        Assert.Equal("Alice Smith", c.DisplayName);
        Assert.Equal("Alice", c.GivenName);
        Assert.Equal(new[] { "alice@example.com", "a2@example.com" }, c.Emails);
        Assert.Equal("Acme", c.CompanyName);
        Assert.Equal("Engineer", c.JobTitle);
        Assert.Equal("+47 111", c.BusinessPhone);
        Assert.Equal("Oslo", c.HomeAddress!.City);
        Assert.Equal("hello", c.Notes);
        Assert.Equal("card-1", c.SourceCardId);
    }

    /// <summary>
    /// Mutation-coverage (Stryker): the contact birthday parser (BirthYear/Month/Day) was entirely
    /// uncovered. Valid dates must map; the month 1..12 and day 1..31 range guards must reject out-of-range.
    /// </summary>
    [Theory]
    [InlineData("1990", "1", "1", 1990, 1, 1)]     // lower boundaries valid
    [InlineData("1990", "12", "31", 1990, 12, 31)] // upper boundaries valid
    public void Map_Birthday_valid_date(string y, string m, string d, int ey, int em, int ed)
    {
        var c = new ContactPropertyMapper().Map(new Dictionary<string, string>
            { ["BirthYear"] = y, ["BirthMonth"] = m, ["BirthDay"] = d }, null);
        Assert.NotNull(c.Birthday);
        Assert.Equal(ey, c.Birthday!.Value.Year);
        Assert.Equal(em, c.Birthday.Value.Month);
        Assert.Equal(ed, c.Birthday.Value.Day);
    }

    [Theory]
    [InlineData("1990", "13", "15")]  // month out of range
    [InlineData("1990", "0", "15")]   // month out of range
    [InlineData("1990", "6", "32")]   // day out of range
    public void Map_Birthday_out_of_range_is_null(string y, string m, string d)
    {
        var c = new ContactPropertyMapper().Map(new Dictionary<string, string>
            { ["BirthYear"] = y, ["BirthMonth"] = m, ["BirthDay"] = d }, null);
        Assert.Null(c.Birthday);
    }

    /// <summary>
    /// Mutation-coverage: a missing/zero year with a valid month+day must fall back to the 1604 sentinel
    /// (Outlook's date-only birthday convention), not be dropped.
    /// </summary>
    [Fact]
    public void Map_Birthday_missing_year_uses_sentinel()
    {
        var c = new ContactPropertyMapper().Map(new Dictionary<string, string>
            { ["BirthMonth"] = "6", ["BirthDay"] = "15" }, null);
        Assert.NotNull(c.Birthday);
        Assert.Equal(1604, c.Birthday!.Value.Year);
        Assert.Equal(6, c.Birthday.Value.Month);
        Assert.Equal(15, c.Birthday.Value.Day);
    }

    [Fact]
    public void Map_EmptyEmail_NotAddedToList()
    {
        var c = new ContactPropertyMapper().Map(
            new Dictionary<string, string> { ["DisplayName"] = "Bob", ["PrimaryEmail"] = "" }, null);
        Assert.Empty(c.Emails);
    }
}
