// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Contacts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

public class SqliteAddressBookReaderTests
{
    private static string BuildAbook(bool wal)
    {
        string db = Path.Combine(Path.GetTempPath(), $"m2p-abook-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = wal ? "PRAGMA journal_mode=WAL;" : "PRAGMA journal_mode=DELETE;";
            pragma.ExecuteNonQuery();
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE properties (card TEXT, name TEXT, value TEXT);
            INSERT INTO properties VALUES ('c1','DisplayName','Alice Smith');
            INSERT INTO properties VALUES ('c1','FirstName','Alice');
            INSERT INTO properties VALUES ('c1','PrimaryEmail','alice@example.com');
            INSERT INTO properties VALUES ('c2','DisplayName','Bob Jones');
            INSERT INTO properties VALUES ('c2','PrimaryEmail','bob@example.com');";
        cmd.ExecuteNonQuery();
        return db;
    }

    [Fact]
    public void Read_ReturnsAllCards_GroupedByCardId()
    {
        string db = BuildAbook(wal: false);
        try
        {
            var book = new AddressBook { DisplayName = "Personal", Path = db, Format = AddressBookFormat.ThunderbirdSqlite };
            List<ContactReadResult> results = new SqliteAddressBookReader().Read(book).ToList();
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.Contains(results, r => r.Contact!.DisplayName == "Alice Smith" && r.Contact.Emails.Contains("alice@example.com"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(db); }
    }

    [Fact]
    public void Read_WalModeDatabase_StillReturnsCards()
    {
        string db = BuildAbook(wal: true);
        try
        {
            var book = new AddressBook { DisplayName = "Personal", Path = db, Format = AddressBookFormat.ThunderbirdSqlite };
            Assert.Equal(2, new SqliteAddressBookReader().Read(book).Count());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(db); File.Delete(db + "-wal"); File.Delete(db + "-shm"); }
    }
}
