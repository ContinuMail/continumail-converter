// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Models;

/// <summary>Normalized, source-agnostic contact. Produced by every IAddressBookReader.</summary>
public class ContactRecord
{
    public string? DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? Surname { get; set; }
    public string? MiddleName { get; set; }
    public string? Nickname { get; set; }
    public string? CompanyName { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public List<string> Emails { get; set; } = new();            // up to 3 used (Email1..3)
    public string? BusinessPhone { get; set; }
    public string? HomePhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? FaxNumber { get; set; }
    public string? PagerNumber { get; set; }
    public PostalAddress? HomeAddress { get; set; }
    public PostalAddress? BusinessAddress { get; set; }
    public string? Webpage { get; set; }
    public DateTimeOffset? Birthday { get; set; }
    public string? Notes { get; set; }
    public List<string> CustomFields { get; set; } = new();      // appended to Notes on write

    /// <summary>Identity for warnings: source card id or best-effort fallback.</summary>
    public string? SourceCardId { get; set; }
}
