// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Contacts;

public class ContactReadResult
{
    public ContactRecord? Contact { get; private init; }
    public string? Error { get; private init; }
    public string Source { get; private init; } = string.Empty;
    public List<string> Warnings { get; private init; } = new();
    public bool Success => Error is null;

    public static ContactReadResult Ok(ContactRecord c, List<string>? warnings = null) =>
        new() { Contact = c, Warnings = warnings ?? new List<string>() };
    public static ContactReadResult Failed(string source, string error) =>
        new() { Source = source, Error = error };
}
