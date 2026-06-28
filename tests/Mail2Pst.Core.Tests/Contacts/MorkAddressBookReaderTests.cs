// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Mail2Pst.Core.Contacts;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

public class MorkAddressBookReaderTests
{
    [Fact]
    public void Read_MabFixture_ReturnsContacts()
    {
        var book = new AddressBook
        {
            DisplayName = "Legacy",
            Path = "Contacts/fixtures/sample-abook.mab",
            Format = AddressBookFormat.ThunderbirdMab,
        };
        var results = new MorkAddressBookReader().Read(book).ToList();
        Assert.True(results.Count >= 2);
        Assert.Contains(results, r => r.Success && r.Contact!.Emails.Count > 0);
    }
}
