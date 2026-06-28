// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using FolkerKinzel.VCards;
using FolkerKinzel.VCards.Models;
using FolkerKinzel.VCards.Models.Properties;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

/// <summary>
/// API spike: parses test-test.vcf and asserts real values.
/// The passing code documents the exact FolkerKinzel 8.1.x accessors
/// that Task 3 (mapper) and Task 5 (error-handling) must use.
/// </summary>
public class VCardApiSpikeTests
{
    // Entry point: Vcf.Load(path) returns IReadOnlyList<VCard>.
    // Vcf.Parse(string) does the same from a string.
    private static VCard Load()
    {
        string path = Path.Combine("Contacts", "fixtures", "test-test.vcf");
        return Vcf.Load(path).Single();
    }

    [Fact]
    public void Parse_TestCard_FnTitleRole()
    {
        VCard v = Load();
        // FN  -> v.DisplayNames -> IEnumerable<TextProperty?> -> .Value is string?
        Assert.Equal("Test Test", v.DisplayNames!.First()!.Value);
        // TITLE -> v.Titles -> IEnumerable<TextProperty?> -> .Value is string?
        Assert.Contains("Test Title", v.Titles!.Select(t => t!.Value));
        // ROLE -> v.Roles -> IEnumerable<TextProperty?> -> .Value is string?
        Assert.Contains("Test Role", v.Roles!.Select(r => r!.Value));
    }

    [Fact]
    public void Parse_TestCard_NameComponents()
    {
        VCard v = Load();
        // N -> v.NameViews -> IEnumerable<NameProperty?> -> .Value is Name
        // Name.Surnames  -> IReadOnlyList<string> (family name; plural = multi-valued N component)
        // Name.Given     -> IReadOnlyList<string> (given name / first name)
        // Name.Given2    -> IReadOnlyList<string> (additional / middle name)
        Name name = v.NameViews!.First()!.Value;
        Assert.Equal("Test", name.Surnames.First());
        Assert.Equal("Test", name.Given.First());
        Assert.Equal("Middle", name.Given2.First());
    }

    [Fact]
    public void Parse_TestCard_NickName()
    {
        VCard v = Load();
        // NICKNAME -> v.NickNames -> IEnumerable<StringCollectionProperty?> -> .Value is IReadOnlyList<string>
        // (NICKNAME is a multi-value property; use SelectMany or flatten the inner list)
        Assert.True(v.NickNames!.Any(n => n!.Value.Contains("Test1")));
    }

    [Fact]
    public void Parse_TestCard_EmailWithPreference()
    {
        VCard v = Load();
        // EMAIL -> v.EMails -> IEnumerable<TextProperty?> -> .Value is string?
        // Preference: .Parameters.Preference is int (1 = highest in vCard 4.0; 0 = unset)
        var emails = v.EMails!.Where(e => e != null).ToList();
        Assert.Contains(emails, e => e!.Value == "test@test.dk");
        Assert.Contains(emails, e => e!.Value == "second@test.dk");

        // The email with PREF=1 is the preferred address
        var preferred = emails.First(e => e!.Parameters.Preference == 1);
        Assert.Equal("test@test.dk", preferred!.Value);
    }

    [Fact]
    public void Parse_TestCard_Organization()
    {
        VCard v = Load();
        // ORG -> v.Organizations -> IEnumerable<OrgProperty?> -> .Value is Organization
        // Organization.Name  -> string? (org name)
        // Organization.Units -> IReadOnlyList<string> (org unit(s); ORG second and further components)
        Organization org = v.Organizations!.First()!.Value;
        Assert.Equal("Test Org", org.Name);
        Assert.Contains("Test Unit", (System.Collections.Generic.IEnumerable<string>)org.Units);
    }

    [Fact]
    public void Parse_TestCard_Birthday()
    {
        VCard v = Load();
        // BDAY -> v.BirthDayViews -> IEnumerable<DateAndOrTimeProperty?> -> .Value is DateAndOrTime
        // DateAndOrTime.DateOnly -> DateOnly? (set for DATE-only values like BDAY;VALUE=DATE:19990101)
        DateAndOrTime bday = v.BirthDayViews!.First()!.Value;
        Assert.True(bday.DateOnly.HasValue);
        Assert.Equal(new DateOnly(1999, 1, 1), bday.DateOnly!.Value);
    }

    [Fact]
    public void Parse_TestCard_Address()
    {
        VCard v = Load();
        // ADR -> v.Addresses -> IEnumerable<AddressProperty?> -> .Value is Address
        // All Address components are IReadOnlyList<string> (vCard 4.0 allows multi-value components)
        // Address.Street     -> IReadOnlyList<string>
        // Address.Locality   -> IReadOnlyList<string>
        // Address.Region     -> IReadOnlyList<string>
        // Address.PostalCode -> IReadOnlyList<string>
        // Address.Country    -> IReadOnlyList<string>
        // TYPE -> .Parameters.AddressType (AddressTypes flags enum)
        Address adr = v.Addresses!.First()!.Value;
        Assert.True(adr.Street.Contains("Teststreet"));
        Assert.True(adr.Locality.Contains("Testcity"));
        Assert.True(adr.Region.Contains("Teststate"));
        Assert.True(adr.PostalCode.Contains("2000"));
        Assert.True(adr.Country.Contains("Denmark"));
    }

    [Fact]
    public void Parse_TestCard_Phone()
    {
        VCard v = Load();
        // TEL -> v.Phones -> IEnumerable<TextProperty?> -> .Value is string?
        // TYPE -> .Parameters.PhoneType (PhoneTypes flags enum)
        Assert.Contains("12345678", v.Phones!.Select(p => p!.Value));
    }

    [Fact]
    public void Parse_TestCard_Url()
    {
        VCard v = Load();
        // URL -> v.Urls -> IEnumerable<TextProperty?> -> .Value is string?
        Assert.Contains("https://www.test.dk", v.Urls!.Select(u => u!.Value));
    }

    [Fact]
    public void Parse_TestCard_TimeZone()
    {
        VCard v = Load();
        // TZ -> v.TimeZones -> IEnumerable<TimeZoneProperty?> -> .Value is TimeZoneID
        // TimeZoneID.Value -> string?
        Assert.Equal("Europe/Copenhagen", v.TimeZones!.First()!.Value.Value);
    }

    [Fact]
    public void Parse_TestCard_EmbeddedPhoto_YieldsBytesAndMediaType()
    {
        VCard v = Load();
        // PHOTO -> v.Photos -> IEnumerable<DataProperty?> -> .Value is RawData
        // Embedded (data-URI): RawData.Bytes is byte[] (non-null), RawData.Uri is null
        // External (http/file): RawData.Uri is Uri (non-null), RawData.Bytes is null
        // RawData.MediaType -> string? (MIME type, e.g. "image/png")
        DataProperty photo = v.Photos!.First(p => p != null)!;
        RawData rawData = photo.Value;
        Assert.NotNull(rawData);
        // Embedded photo: Bytes is non-null, Uri is null
        Assert.NotNull(rawData.Bytes);
        Assert.Null(rawData.Uri);
        Assert.True(rawData.Bytes!.Length > 0);
        Assert.Equal("image/png", rawData.MediaType);
    }

    [Fact]
    public void Parse_TestCard_CustomXProperty_Reachable()
    {
        VCard v = Load();
        // X-CUSTOM1 -> v.NonStandards -> IEnumerable<NonStandardProperty?>
        // NonStandardProperty.Key   -> string (the X- property name, uppercased)
        // NonStandardProperty.Value -> string? (the raw text value)
        Assert.Contains(v.NonStandards!, p => p!.Key == "X-CUSTOM1" && p.Value == "Test Custom");
    }

    [Fact]
    public void Parse_MalformedInput_BehaviorDocumented()
    {
        // Task 5 depends on knowing whether Vcf.Parse("not a vcard") throws or returns empty.
        // Record: FolkerKinzel does NOT throw on unrecognized input.
        // It returns a list — either empty, or with one mostly-empty VCard.
        // For genuinely garbled input, any returned VCard has no FN, no emails, no name.
        const string garbage = "this is not a vcard";
        var result = Vcf.Parse(garbage);
        // FolkerKinzel returns an empty list for genuinely garbled input (no BEGIN:VCARD).
        Assert.Empty(result);
    }
}
