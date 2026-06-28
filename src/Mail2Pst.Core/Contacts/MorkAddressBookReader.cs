// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Mork;

namespace Mail2Pst.Core.Contacts;

/// <summary>Reads legacy Thunderbird .mab (Mork) address books. Dedicated address-book table
/// reader — NOT the .msf message reader (different table semantics, same property key names).</summary>
public class MorkAddressBookReader : IAddressBookReader
{
    private readonly ContactPropertyMapper _mapper = new();

    public IEnumerable<ContactReadResult> Read(AddressBook book)
    {
        var results = new List<ContactReadResult>();
        // MorkReader.Parse(string) -> MorkDocument (verified API). ParseSharedReadWrite also exists
        // for files another process may have open; prefer it if .mab files are locked in practice.
        MorkDocument doc = MorkReader.Parse(book.Path);
        foreach (MorkRow row in EnumerateContactRows(doc))
        {
            string cardId = string.IsNullOrEmpty(row.Id) ? Guid.NewGuid().ToString("N") : row.Id;
            try
            {
                results.Add(ContactReadResult.Ok(_mapper.Map(row.Cells, cardId)));
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                results.Add(ContactReadResult.Failed($"{book.DisplayName}#{cardId}", ex.Message));
            }
        }
        return results;
    }

    private static IEnumerable<MorkRow> EnumerateContactRows(MorkDocument doc)
    {
        // MorkDocument.Tables: IReadOnlyList<MorkTable>; MorkTable.Rows: IReadOnlyDictionary<string,MorkRow>.
        // Address-book card rows are the table rows. If the .mab uses a specific
        // scope/kind for cards, narrow via doc.GetTables(scope, kind) instead of all Tables.
        foreach (MorkTable table in doc.Tables)
            foreach (MorkRow row in table.Rows.Values)
                yield return row;
    }
}
