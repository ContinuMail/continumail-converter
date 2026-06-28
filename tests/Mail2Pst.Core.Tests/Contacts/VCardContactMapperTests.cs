// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FolkerKinzel.VCards;
using Mail2Pst.Core.Contacts;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

public class VCardContactMapperTests
{
    private static ContactRecord MapFixture(out List<string> warnings)
    {
        VCard v = Vcf.Load(Path.Combine("Contacts", "fixtures", "test-test.vcf")).Single();
        warnings = new List<string>();
        return new VCardContactMapper().Map(v, "card-1", warnings);
    }

    [Fact]
    public void Map_FullCard_MapsAllScalarFields()
    {
        var c = MapFixture(out _);
        Assert.Equal("Test Test", c.DisplayName);
        Assert.Equal("Test", c.GivenName);
        Assert.Equal("Test", c.Surname);
        Assert.Equal("Middle", c.MiddleName);
        Assert.Equal("Test1", c.Nickname);
        Assert.Equal("Test Org", c.CompanyName);
        Assert.Equal("Test Unit", c.Department);
        Assert.Equal("Test Title", c.JobTitle);
        Assert.Equal("Test Role", c.Profession);
        Assert.Equal(new[] { "test@test.dk", "second@test.dk" }, c.Emails);
    }

    [Fact]
    public void Map_UntypedAddressGoesHome_UntypedPhoneGoesBusiness_HomeUrlGoesPersonal()
    {
        var c = MapFixture(out _);
        Assert.Equal("Testcity", c.HomeAddress!.City);       // untyped ADR -> Home
        Assert.Equal("Denmark", c.HomeAddress.Country);
        Assert.Equal("12345678", c.BusinessPhone);            // untyped TEL -> Business
        Assert.Equal("https://www.test.dk", c.PersonalHomePage); // URL TYPE=home -> Personal
    }

    [Fact]
    public void Map_TimezoneAndCustom_AppendedToNotesOrCustomFields()
    {
        var c = MapFixture(out _);
        string all = string.Join("\n", new[] { c.Notes ?? "" }.Concat(c.CustomFields));
        Assert.Contains("Europe/Copenhagen", all);
        Assert.Contains("Test Custom", all);
    }

    [Fact]
    public void Map_EmbeddedPhoto_PopulatesPhotoBytesAndMediaType()
    {
        var c = MapFixture(out _);
        Assert.NotNull(c.Photo);
        Assert.True(c.Photo!.Bytes.Length > 0);
        Assert.Equal("image/png", c.Photo.MediaType);
    }

    [Fact]
    public void Map_BirthdayDate_PreservesCalendarDate()
    {
        var c = MapFixture(out _);
        Assert.Equal(1999, c.Birthday!.Value.Year);
        Assert.Equal(1, c.Birthday.Value.Month);
        Assert.Equal(1, c.Birthday.Value.Day);
    }

    [Theory]
    [InlineData("BDAY:19900520")]
    [InlineData("BDAY:1990-05-20")]
    [InlineData("BDAY;VALUE=date:1990-05-20")]
    public void Map_BirthdayFormats_AllYield1990_05_20(string bdayLine)
    {
        string vcf = "BEGIN:VCARD\nVERSION:4.0\nFN:X\n" + bdayLine + "\nEND:VCARD\n";
        VCard v = Vcf.Parse(vcf).Single();
        var c = new VCardContactMapper().Map(v, "c", new List<string>());
        Assert.Equal(new DateTime(1990, 5, 20), new DateTime(c.Birthday!.Value.Year, c.Birthday.Value.Month, c.Birthday.Value.Day));
    }

    [Fact]
    public void Map_ExternalPhotoUri_SkipsPhotoAndWarns()
    {
        string vcf = "BEGIN:VCARD\nVERSION:4.0\nFN:Ext\nPHOTO;VALUE=uri:https://example.com/p.jpg\nEND:VCARD\n";
        VCard v = Vcf.Parse(vcf).Single();
        var warnings = new List<string>();
        var c = new VCardContactMapper().Map(v, "c", warnings);
        Assert.Null(c.Photo);
        Assert.Contains(warnings, w => w.Contains("external", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Map_OversizedPhoto_SkipsPhotoAndWarns()
    {
        // Build a data: URI whose decoded bytes exceed the cap.
        byte[] big = new byte[(int)ContactPhotoPolicy.MaxContactPhotoBytes + 16];
        string b64 = Convert.ToBase64String(big);
        string vcf = "BEGIN:VCARD\nVERSION:4.0\nFN:Big\nPHOTO:data:image/jpeg;base64," + b64 + "\nEND:VCARD\n";
        VCard v = Vcf.Parse(vcf).Single();
        var warnings = new List<string>();
        var c = new VCardContactMapper().Map(v, "c", warnings);
        Assert.Null(c.Photo);
        Assert.Contains(warnings, w => w.Contains("exceeds"));
    }

    /// <summary>Exercises typed routing branches not covered by the untyped fixture.</summary>
    [Fact]
    public void Map_TypedAdrCellAndWorkUrl_RouteToCorrectFields()
    {
        string vcf =
            "BEGIN:VCARD\nVERSION:4.0\nFN:Typed\n" +
            "ADR;TYPE=work:;;Work Street;Work City;;;Country\n" +
            "TEL;TYPE=cell:0987654321\n" +
            "URL;TYPE=work:https://www.work.example.com\n" +
            "END:VCARD\n";
        VCard v = Vcf.Parse(vcf).Single();
        var c = new VCardContactMapper().Map(v, "t", new List<string>());
        Assert.Equal("Work City", c.BusinessAddress!.City);  // ADR;TYPE=work → BusinessAddress
        Assert.Equal("0987654321", c.MobilePhone);           // TEL;TYPE=cell → MobilePhone
        Assert.Equal("https://www.work.example.com", c.Webpage); // URL;TYPE=work → Webpage
    }
}
