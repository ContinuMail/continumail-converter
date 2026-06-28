// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;
using PSTFileFormat;

namespace Mail2Pst.Core.Writing;

/// <summary>Writes a normalized ContactRecord as an IPM.Contact item into an IPF.Contact folder.</summary>
public class ContactWriter
{
    public void WriteContact(PSTFile file, PSTFolder folder, ContactRecord c)
    {
        ContactMessage msg = ContactMessage.CreateNewContact(file, folder.NodeID);

        SetIf(msg, PropertyID.PidTagDisplayName, DisplayNameOf(c));
        SetIf(msg, PropertyID.PidTagGivenName, c.GivenName);
        SetIf(msg, PropertyID.PidTagSurname, c.Surname);
        SetIf(msg, PropertyID.PidTagMiddleName, c.MiddleName);
        SetIf(msg, PropertyID.PidTagNickname, c.Nickname);
        SetIf(msg, PropertyID.PidTagCompanyName, c.CompanyName);
        SetIf(msg, PropertyID.PidTagTitle, c.JobTitle);
        SetIf(msg, PropertyID.PidTagDepartmentName, c.Department);
        SetIf(msg, PropertyID.PidTagBusinessTelephoneNumber, c.BusinessPhone);
        SetIf(msg, PropertyID.PidTagHomeTelephoneNumber, c.HomePhone);
        SetIf(msg, PropertyID.PidTagMobileTelephoneNumber, c.MobilePhone);
        SetIf(msg, PropertyID.PidTagBusinessFaxNumber, c.FaxNumber);
        SetIf(msg, PropertyID.PidTagPagerTelephoneNumber, c.PagerNumber);
        SetIf(msg, PropertyID.PidTagBusinessHomePage, c.Webpage);

        WriteAddress(msg, c.HomeAddress,
            PropertyID.PidTagHomeAddressStreet, PropertyID.PidTagHomeAddressCity,
            PropertyID.PidTagHomeAddressStateOrProvince, PropertyID.PidTagHomeAddressPostalCode,
            PropertyID.PidTagHomeAddressCountry);
        WriteAddress(msg, c.BusinessAddress,
            PropertyID.PidTagBusinessAddressStreet, PropertyID.PidTagBusinessAddressCity,
            PropertyID.PidTagBusinessAddressStateOrProvince, PropertyID.PidTagBusinessAddressPostalCode,
            PropertyID.PidTagBusinessAddressCountry);

        if (c.Birthday is DateTimeOffset bday)
            msg.PC.SetDateTimeProperty(PropertyID.PidTagBirthday, NormalizeDateOnly(bday));

        string? notes = NotesWithCustom(c);
        SetIf(msg, PropertyID.PidTagBody, notes);

        WriteEmails(file, msg, c.Emails);

        msg.SaveChanges();
        folder.AddMessage(msg);
    }

    private static void WriteEmails(PSTFile file, ContactMessage msg, List<string> emails)
    {
        // Email1..3 -> PSETID_Address numeric named props.
        // Block offsets: 0x8080/0x8090/0x80A0 (base + i*0x10)
        int max = Math.Min(emails.Count, 3);
        for (int i = 0; i < max; i++)
        {
            int block = 0x8080 + (i * 0x10);
            string addr = emails[i];
            SetNamed(file, msg, block + 0x00, addr);    // PidLidEmail1DisplayName  (use address)
            SetNamed(file, msg, block + 0x02, "SMTP");  // PidLidEmail1AddressType
            SetNamed(file, msg, block + 0x03, addr);    // PidLidEmail1EmailAddress
            SetNamed(file, msg, block + 0x04, addr);    // PidLidEmail1OriginalDisplayName
        }
    }

    private static void SetNamed(PSTFile file, ContactMessage msg, int lid, string value)
    {
        PropertyID id = file.NameToIDMap.ObtainIDFromName(
            new PropertyName((PropertyLongID)lid, PropertySetGuid.PSETID_Address));
        msg.PC.SetStringProperty(id, value);
    }

    private static void WriteAddress(ContactMessage msg, PostalAddress? a,
        PropertyID street, PropertyID city, PropertyID state, PropertyID zip, PropertyID country)
    {
        if (a is null || a.IsEmpty) return;
        SetIf(msg, street, a.Street);
        SetIf(msg, city, a.City);
        SetIf(msg, state, a.State);
        SetIf(msg, zip, a.PostalCode);
        SetIf(msg, country, a.Country);
    }

    private static void SetIf(ContactMessage msg, PropertyID id, string? value)
    {
        if (!string.IsNullOrEmpty(value)) msg.PC.SetStringProperty(id, value);
    }

    private static string DisplayNameOf(ContactRecord c)
    {
        if (!string.IsNullOrWhiteSpace(c.DisplayName)) return c.DisplayName!;
        string composed = string.Join(" ", new[] { c.GivenName, c.Surname }).Trim();
        if (composed.Length > 0) return composed;
        return c.Emails.Count > 0 ? c.Emails[0] : "Unnamed Contact";
    }

    private static string? NotesWithCustom(ContactRecord c)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(c.Notes)) parts.Add(c.Notes!);
        foreach (string custom in c.CustomFields)
            if (!string.IsNullOrEmpty(custom)) parts.Add(custom);
        return parts.Count > 0 ? string.Join(Environment.NewLine, parts) : null;
    }

    // Date-only normalization: store as UTC midnight of the calendar date.
    // Uses the DateTimeOffset's own Y/M/D so a non-UTC zone (e.g. +13:00) keeps its date.
    private static DateTime NormalizeDateOnly(DateTimeOffset d) =>
        new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
}
