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

    [Fact]
    public void Map_EmptyEmail_NotAddedToList()
    {
        var c = new ContactPropertyMapper().Map(
            new Dictionary<string, string> { ["DisplayName"] = "Bob", ["PrimaryEmail"] = "" }, null);
        Assert.Empty(c.Emails);
    }
}
