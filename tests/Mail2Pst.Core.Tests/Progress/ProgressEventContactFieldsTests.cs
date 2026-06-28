// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Progress;
using Xunit;

namespace Mail2Pst.Core.Tests.Progress;

public class ProgressEventContactFieldsTests
{
    [Fact]
    public void ProgressEvent_CarriesAdditiveContactFields_DefaultsMailOnly()
    {
        var mail = new ProgressEvent(Converted: 10, TotalMessages: 20, Warnings: 0, Skipped: 0);
        Assert.Equal(0, mail.ContactsConverted);
        Assert.Equal(0, mail.ContactsTotal);
        Assert.Equal("mail", mail.Phase);

        var contacts = new ProgressEvent(Converted: 10, TotalMessages: 20, Warnings: 0, Skipped: 0,
            ContactsConverted: 2, ContactsTotal: 5, Phase: "contacts");
        Assert.Equal(2, contacts.ContactsConverted);
        Assert.Equal(5, contacts.ContactsTotal);
        Assert.Equal("contacts", contacts.Phase);
    }
}
