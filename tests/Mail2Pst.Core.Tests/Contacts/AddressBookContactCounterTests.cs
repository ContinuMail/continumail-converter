// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Mail2Pst.Core.Contacts;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

public class AddressBookContactCounterTests
{
    [Fact]
    public void Sqlite_book_counts_distinct_cards()
    {
        string path = Path.Combine(Path.GetTempPath(), $"abook-{Guid.NewGuid():N}.sqlite");
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE properties (card TEXT, name TEXT, value TEXT);" +
                "INSERT INTO properties VALUES ('c1','DisplayName','A'),('c1','PrimaryEmail','a@example.test')," +
                "('c2','DisplayName','B');";
            cmd.ExecuteNonQuery();
        }
        var book = new DiscoveredAddressBook { Path = path, Format = "thunderbird-sqlite" };
        Assert.Equal(2, AddressBookContactCounter.TryCount(book));
        File.Delete(path);
    }

    [Fact]
    public void Unreadable_book_returns_null_not_throw()
    {
        var book = new DiscoveredAddressBook { Path = "Z:/does/not/exist.sqlite", Format = "thunderbird-sqlite" };
        Assert.Null(AddressBookContactCounter.TryCount(book));
    }

    [Fact]
    public void Mab_format_returns_null_unknown()
    {
        var book = new DiscoveredAddressBook { Path = "whatever.mab", Format = "thunderbird-mab" };
        Assert.Null(AddressBookContactCounter.TryCount(book));
    }

    // Proves the COUNT(DISTINCT card) figure matches what the real converter reads,
    // so the UI count never overstates. Thunderbird stores mailing lists in separate
    // tables (not `properties`), so every distinct card is a real contact.
    [Fact]
    public void Count_matches_the_reader_output()
    {
        string path = Path.Combine(Path.GetTempPath(), $"abook-{Guid.NewGuid():N}.sqlite");
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE properties (card TEXT, name TEXT, value TEXT);" +
                "INSERT INTO properties VALUES ('c1','DisplayName','A'),('c2','DisplayName','B'),('c3','DisplayName','C');";
            cmd.ExecuteNonQuery();
        }
        int readerCount = new SqliteAddressBookReader()
            .Read(new AddressBook { Path = path, Format = AddressBookFormat.ThunderbirdSqlite })
            .Count(r => r.Success);
        int? counted = AddressBookContactCounter.TryCount(
            new DiscoveredAddressBook { Path = path, Format = "thunderbird-sqlite" });
        Assert.Equal(readerCount, counted);
        File.Delete(path);
    }
}
