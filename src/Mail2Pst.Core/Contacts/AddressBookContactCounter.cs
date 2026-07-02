// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using Mail2Pst.Core.Discovery;
using Mail2Pst.Core.Storage;

namespace Mail2Pst.Core.Contacts;

/// <summary>Best-effort, non-throwing contact count for a discovered address book.
/// null = unknown (count failed, or a format we don't cheaply count, e.g. .mab).</summary>
public static class AddressBookContactCounter
{
    public static int? TryCount(DiscoveredAddressBook book)
    {
        try
        {
            return book.Format switch
            {
                "thunderbird-sqlite" => CountSqlite(book.Path),
                _ => null, // .mab count deferred — shown as unknown, still convertible
            };
        }
        catch
        {
            return null; // never fail discovery over a contact count
        }
    }

    private static int? CountSqlite(string path)
    {
        using SqliteSnapshot snap = SqliteSnapshot.Open(path);
        using var cmd = snap.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT card) FROM properties";
        object? result = cmd.ExecuteScalar();
        return result is long l ? (int)l : null;
    }
}
