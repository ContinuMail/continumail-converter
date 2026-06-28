// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FolkerKinzel.VCards;
using FolkerKinzel.VCards.Enums;
using FolkerKinzel.VCards.Extensions;
using FolkerKinzel.VCards.Models;
using FolkerKinzel.VCards.Models.Properties;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Contacts;

/// <summary>Maps a parsed FolkerKinzel VCard to ContactRecord. Used for modern Thunderbird
/// cards whose rich fields live only in the _vCard blob. Photo/skip notes go to <paramref name="warnings"/>.</summary>
public class VCardContactMapper
{
    public ContactRecord Map(VCard v, string cardId, List<string> warnings)
    {
        var c = new ContactRecord { SourceCardId = cardId };

        // FN → DisplayName
        c.DisplayName = First(v.DisplayNames);

        // N → name components (Surnames/Given/Given2 are IReadOnlyList<string> in FolkerKinzel 8.x)
        var nameProps = v.NameViews?.FirstOrDefault(n => n is not null);
        if (nameProps is not null)
        {
            c.Surname    = Join(nameProps.Value.Surnames);
            c.GivenName  = Join(nameProps.Value.Given);
            c.MiddleName = Join(nameProps.Value.Given2);
        }

        // NICKNAME: StringCollectionProperty — .Value is IReadOnlyList<string>
        c.Nickname = First(v.NickNames?.Select(n => n is null ? null : string.Join(", ", n.Value)));

        // ORG: Organization.Name (string?) / Organization.Units (IReadOnlyList<string>)
        var orgProp = v.Organizations?.FirstOrDefault(o => o is not null);
        if (orgProp is not null)
        {
            c.CompanyName = NullIfEmpty(orgProp.Value.Name);
            c.Department  = NullIfEmpty(orgProp.Value.Units.FirstOrDefault());
        }

        // TITLE / ROLE
        c.JobTitle   = First(v.Titles);
        c.Profession = First(v.Roles);

        // EMAIL: preference-ordered (PREF=1 highest, PREF=0 unset → sorted last), up to 3
        foreach (string e in OrderByPreference(v.EMails).Take(3))
            c.Emails.Add(e);

        // TEL: route by phone type (Tel enum) and property class (PCl enum for Home/Work)
        //   Tel.Cell → Mobile | Tel.Fax → Fax | Tel.Pager → Pager
        //   PCl.Home → Home | else (Work/untyped) → Business
        foreach (var tel in v.Phones ?? Enumerable.Empty<TextProperty?>())
        {
            if (tel is null || string.IsNullOrWhiteSpace(tel.Value)) continue;
            string val = tel.Value!;
            if (tel.Parameters.PhoneType.IsSet(Tel.Cell))          c.MobilePhone    ??= val;
            else if (tel.Parameters.PhoneType.IsSet(Tel.Fax))      c.FaxNumber      ??= val;
            else if (tel.Parameters.PhoneType.IsSet(Tel.Pager))    c.PagerNumber    ??= val;
            else if (tel.Parameters.PropertyClass.IsSet(PCl.Home)) c.HomePhone      ??= val;
            else                                                    c.BusinessPhone  ??= val;
        }

        // ADR: route by property class (PCl.Work → Business; else → Home/untyped)
        foreach (var adr in v.Addresses ?? Enumerable.Empty<AddressProperty?>())
        {
            if (adr is null) continue;
            var pa = ToPostal(adr);
            if (pa.IsEmpty) continue;
            if (adr.Parameters.PropertyClass.IsSet(PCl.Work)) c.BusinessAddress ??= pa;
            else                                               c.HomeAddress     ??= pa;
        }

        // URL: PCl.Home → PersonalHomePage; else (Work/untyped) → Webpage
        foreach (var url in v.Urls ?? Enumerable.Empty<TextProperty?>())
        {
            if (url is null || string.IsNullOrWhiteSpace(url.Value)) continue;
            if (url.Parameters.PropertyClass.IsSet(PCl.Home)) c.PersonalHomePage ??= url.Value;
            else                                               c.Webpage          ??= url.Value;
        }

        // BDAY: prefer DateOnly, fall back to DateTimeOffset
        var bdayProp = v.BirthDayViews?.FirstOrDefault(b => b is not null);
        if (bdayProp is not null && TryDate(bdayProp, out DateTimeOffset d))
            c.Birthday = d;

        // NOTE
        c.Notes = First(v.Notes);

        // TZ → CustomFields (writer appends to body)
        string? tz = v.TimeZones?.FirstOrDefault(t => t is not null)?.Value.Value;
        if (!string.IsNullOrWhiteSpace(tz))
            c.CustomFields.Add($"Time zone: {tz}");

        // X-CUSTOM1..4 → CustomFields
        foreach (var ns in v.NonStandards ?? Enumerable.Empty<NonStandardProperty?>())
        {
            if (ns is null) continue;
            if (ns.Key is "X-CUSTOM1" or "X-CUSTOM2" or "X-CUSTOM3" or "X-CUSTOM4"
                && !string.IsNullOrWhiteSpace(ns.Value))
                c.CustomFields.Add(ns.Value!);
        }

        MapPhoto(v, c, warnings);
        return c;
    }

    // ---- photo ----

    private static void MapPhoto(VCard v, ContactRecord c, List<string> warnings)
    {
        var photo = v.Photos?.FirstOrDefault(p => p is not null);
        if (photo is null || photo.Value.IsEmpty) return;

        byte[]? bytes        = null;
        string  mediaType    = photo.Value.MediaType ?? "image/jpeg";
        bool    isExternal   = false;
        bool    isTextEncoded = false;

        // RawData.Switch dispatches on embedded-bytes / external-URI / text-encoded
        photo.Value.Switch(
            b   => bytes         = b,
            _   => isExternal    = true,
            _   => isTextEncoded = true
        );

        if (isExternal)
        {
            warnings.Add($"[{c.SourceCardId}] contact photo is an external URI; skipped");
            return;
        }
        if (isTextEncoded)
        {
            warnings.Add($"[{c.SourceCardId}] contact photo is in an unsupported encoding; skipped");
            return;
        }
        if (bytes is null || bytes.Length == 0) return;
        if (bytes.Length > ContactPhotoPolicy.MaxContactPhotoBytes)
        {
            warnings.Add($"[{c.SourceCardId}] contact photo {bytes.Length} bytes exceeds {ContactPhotoPolicy.MaxContactPhotoBytes}; skipped");
            return;
        }
        c.Photo = new ContactPhoto
        {
            Bytes     = bytes,
            MediaType = string.IsNullOrWhiteSpace(mediaType) ? "image/jpeg" : mediaType,
        };
    }

    // ---- birthday ----

    private static bool TryDate(DateAndOrTimeProperty p, out DateTimeOffset d)
    {
        DateAndOrTime doa = p.Value;
        // Prefer DateOnly (DATE-only values: BDAY;VALUE=DATE:19990101 or BDAY:19990101)
        if (doa.DateOnly.HasValue)
        {
            DateOnly date = doa.DateOnly.Value;
            d = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
            return true;
        }
        // Fall back to DateTimeOffset for BDAY with time component
        if (doa.DateTimeOffset.HasValue)
        {
            d = doa.DateTimeOffset.Value;
            return true;
        }
        d = default;
        return false;
    }

    // ---- helpers ----

    private static string? First(IEnumerable<TextProperty?>? props) =>
        props?.Select(p => p?.Value).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static string? First(IEnumerable<string?>? values) =>
        values?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static string? Join(IEnumerable<string>? parts)
    {
        string s = string.Join(
            " ",
            (parts ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p))
        ).Trim();
        return s.Length == 0 ? null : s;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Orders emails so PREF=1 (most preferred) comes first; PREF=0 (unset) sorts last.</summary>
    private static IEnumerable<string> OrderByPreference(IEnumerable<TextProperty?>? emails) =>
        (emails ?? Enumerable.Empty<TextProperty?>())
        .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Value))
        .OrderBy(e => e!.Parameters.Preference == 0 ? int.MaxValue : e!.Parameters.Preference)
        .Select(e => e!.Value!);

    private static PostalAddress ToPostal(AddressProperty adr) => new()
    {
        Street     = NullIfEmpty(Join(adr.Value.Street)),
        City       = NullIfEmpty(Join(adr.Value.Locality)),
        State      = NullIfEmpty(Join(adr.Value.Region)),
        PostalCode = NullIfEmpty(Join(adr.Value.PostalCode)),
        Country    = NullIfEmpty(Join(adr.Value.Country)),
    };
}
