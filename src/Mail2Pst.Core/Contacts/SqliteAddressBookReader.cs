// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;
using Microsoft.Data.Sqlite;

namespace Mail2Pst.Core.Contacts;

public class SqliteAddressBookReader : IAddressBookReader
{
    private readonly ContactPropertyMapper _mapper = new();

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
                    ContactRecord rec = _mapper.Map(cards[card], card);
                    results.Add(ContactReadResult.Ok(rec));
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                {
                    results.Add(ContactReadResult.Failed($"{book.DisplayName}#{card}", ex.Message));
                }
            }
        }
        return results;
    }
}
