// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Contacts;

/// <summary>Maps Thunderbird card property dictionaries (SQLite + Mork share key names) to ContactRecord.</summary>
public class ContactPropertyMapper
{
    public ContactRecord Map(IReadOnlyDictionary<string, string> p, string? cardId)
    {
        var c = new ContactRecord
        {
            SourceCardId = cardId,
            DisplayName = Get(p, "DisplayName"),
            GivenName = Get(p, "FirstName"),
            Surname = Get(p, "LastName"),
            Nickname = Get(p, "NickName"),
            CompanyName = Get(p, "Company"),
            JobTitle = Get(p, "JobTitle"),
            Department = Get(p, "Department"),
            BusinessPhone = Get(p, "WorkPhone"),
            HomePhone = Get(p, "HomePhone"),
            MobilePhone = Get(p, "CellularNumber"),
            FaxNumber = Get(p, "FaxNumber"),
            PagerNumber = Get(p, "PagerNumber"),
            Webpage = Get(p, "WebPage1"),
            Notes = Get(p, "Notes"),
        };

        AddEmail(c, Get(p, "PrimaryEmail"));
        AddEmail(c, Get(p, "SecondEmail"));

        c.HomeAddress = Address(p, "HomeAddress", "HomeCity", "HomeState", "HomeZipCode", "HomeCountry");
        c.BusinessAddress = Address(p, "WorkAddress", "WorkCity", "WorkState", "WorkZipCode", "WorkCountry");

        c.Birthday = Birthday(Get(p, "BirthYear"), Get(p, "BirthMonth"), Get(p, "BirthDay"));

        foreach (string key in new[] { "Custom1", "Custom2", "Custom3", "Custom4" })
        {
            string? v = Get(p, key);
            if (!string.IsNullOrEmpty(v)) c.CustomFields.Add($"{key}: {v}");
        }
        return c;
    }

    private static string? Get(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out string? v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static void AddEmail(ContactRecord c, string? email)
    {
        if (!string.IsNullOrWhiteSpace(email)) c.Emails.Add(email!);
    }

    private static PostalAddress? Address(IReadOnlyDictionary<string, string> p,
        string street, string city, string state, string zip, string country)
    {
        var a = new PostalAddress
        {
            Street = Get(p, street), City = Get(p, city), State = Get(p, state),
            PostalCode = Get(p, zip), Country = Get(p, country),
        };
        return a.IsEmpty ? null : a;
    }

    private static DateTimeOffset? Birthday(string? y, string? m, string? d)
    {
        if (int.TryParse(m, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mm) &&
            int.TryParse(d, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dd) &&
            mm is >= 1 and <= 12 && dd is >= 1 and <= 31)
        {
            int yy = int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : 1604;
            try { return new DateTimeOffset(yy, mm, dd, 0, 0, 0, TimeSpan.Zero); } catch { return null; }
        }
        return null;
    }
}
