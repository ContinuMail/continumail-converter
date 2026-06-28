// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Models;

public class ContactRecordTests
{
    [Fact]
    public void ContactRecord_DefaultsAreEmptyNotNull()
    {
        var c = new ContactRecord();
        Assert.Empty(c.Emails);
        Assert.Null(c.DisplayName);
        Assert.Null(c.HomeAddress);
    }

    [Fact]
    public void ContactRecord_HoldsAllMvpFields()
    {
        var c = new ContactRecord
        {
            DisplayName = "Alice Smith",
            GivenName = "Alice",
            Surname = "Smith",
            CompanyName = "Acme",
            Emails = { "alice@example.com", "a2@example.com" },
            HomeAddress = new PostalAddress { City = "Oslo" },
        };
        Assert.Equal("Alice Smith", c.DisplayName);
        Assert.Equal(2, c.Emails.Count);
        Assert.Equal("Oslo", c.HomeAddress!.City);
    }
}
