// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FolkerKinzel.VCards;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Mail2Pst.Core.Contacts;

public class SqliteAddressBookReader : IAddressBookReader
{
    private readonly ContactPropertyMapper _eav = new();
    private readonly VCardContactMapper _vcard = new();

    public IEnumerable<ContactReadResult> Read(AddressBook book)
    {
        // Materialize fully inside the snapshot's lifetime, then return.
        var results = new List<ContactReadResult>();
        using (SqliteSnapshot snap = SqliteSnapshot.Open(book.Path))
        {
            var cards = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            var order = new List<string>();
            using (var cmd = snap.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT card, name, value FROM properties";
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string card = r.GetString(0);
                    if (!cards.TryGetValue(card, out var dict))
                    {
                        dict = new Dictionary<string, string>(StringComparer.Ordinal);
                        cards[card] = dict; order.Add(card);
                    }
                    dict[r.GetString(1)] = r.IsDBNull(2) ? string.Empty : r.GetString(2);
                }
            }

            foreach (string card in order)
            {
                try
                {
                    Dictionary<string, string> dict = cards[card];
                    var warnings = new List<string>();
                    ContactRecord? rec = null;

                    if (dict.TryGetValue("_vCard", out string? raw) && !string.IsNullOrWhiteSpace(raw))
                    {
                        try
                        {
                            VCard? vc = Vcf.Parse(raw).FirstOrDefault();
                            if (vc is not null)
                                rec = _vcard.Map(vc, card, warnings);
                            else
                                warnings.Add($"[{book.DisplayName}#{card}] _vCard could not be parsed; using index fields");
                        }
                        catch (Exception ex) when (IsExpectedVCardParseError(ex))
                        {
                            warnings.Add($"[{book.DisplayName}#{card}] _vCard parse failed: {ex.Message}; using index fields");
                        }
                    }

                    // Fall back to EAV/index rows when the vCard path produced nothing, OR produced no
                    // minimally useful identity (no display name AND no email) — a syntactically valid but
                    // contentless blob must not erase a contact the index rows could still describe.
                    if (rec is null || (string.IsNullOrWhiteSpace(rec.DisplayName) && rec.Emails.Count == 0))
                    {
                        if (rec is not null)
                            warnings.Add($"[{book.DisplayName}#{card}] _vCard had no usable identity; using index fields");
                        ContactRecord eav = _eav.Map(dict, card);
                        // prefer the richer vCard record if it had ANY data; else use EAV whole
                        rec = (rec is not null && (rec.CompanyName ?? rec.JobTitle ?? rec.Notes) is not null) ? rec : eav;
                        // ensure identity from EAV if the vCard lacked it
                        if (string.IsNullOrWhiteSpace(rec.DisplayName) && !string.IsNullOrWhiteSpace(eav.DisplayName))
                            rec.DisplayName = eav.DisplayName;
                        if (rec.Emails.Count == 0)
                            foreach (var e in eav.Emails) rec.Emails.Add(e);
                    }

                    // Failed only if even the fallback produced nothing usable:
                    if (string.IsNullOrWhiteSpace(rec.DisplayName) && rec.Emails.Count == 0)
                        results.Add(ContactReadResult.Failed($"{book.DisplayName}#{card}", "no usable contact data"));
                    else
                        results.Add(ContactReadResult.Ok(rec, warnings));
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                {
                    results.Add(ContactReadResult.Failed($"{book.DisplayName}#{card}", ex.Message));
                }
            }
        }
        return results;
    }

    // Per Task-1 spike: the exception type FolkerKinzel throws on malformed input (if it throws
    // at all — it may instead return an empty list, handled above by `vc is null`).
    private static bool IsExpectedVCardParseError(Exception ex) =>
        ex is FormatException or ArgumentException or InvalidOperationException;
}
