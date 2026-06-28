// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Models;

public class ContactRecordVCardFieldsTests
{
    [Fact]
    public void ContactRecord_HoldsProfessionPersonalHomePageAndPhoto()
    {
        var c = new ContactRecord
        {
            Profession = "Test Role",
            PersonalHomePage = "https://www.test.dk",
            Photo = new ContactPhoto { Bytes = new byte[] { 1, 2, 3 }, MediaType = "image/png" },
        };
        Assert.Equal("Test Role", c.Profession);
        Assert.Equal("https://www.test.dk", c.PersonalHomePage);
        Assert.Equal(3, c.Photo!.Bytes.Length);
        Assert.Equal("image/png", c.Photo.MediaType);
    }

    [Fact]
    public void ContactRecord_PhotoDefaultsNull()
    {
        Assert.Null(new ContactRecord().Photo);
    }
}
