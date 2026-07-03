// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Mail2Pst.Core.Discovery;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class AddressBookAccountResolutionTests
{
    private static Account Acct(string id, string email, string host) =>
        new(id, id, id, null, email, host, AddressResolution.NotFound);

    private static string WriteBook(string dir, string file)
    {
        string path = Path.Combine(dir, file);
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE properties (card TEXT, name TEXT, value TEXT);" +
                          "INSERT INTO properties VALUES ('c1','DisplayName','A');";
        cmd.ExecuteNonQuery();
        return path;
    }

    [Fact]
    public void Carddav_book_resolves_to_account_local_book_is_null()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ab-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        WriteBook(dir, "abook.sqlite");        // local Personal Address Book
        WriteBook(dir, "abook-1.sqlite");      // CardDAV-synced
        var accounts = new List<Account> { Acct("/p/ImapMail/imap.example.com", "user@example.com", "imap.example.com") };
        var cardDav = new Dictionary<string, string> { ["abook-1.sqlite"] = "https://dav.example.com/user%40example.com/" };

        var books = MailProfileDiscovery.DiscoverAddressBooks(dir, accounts, cardDav, out var warnings);

        Assert.Null(books.Find(b => Path.GetFileName(b.Path) == "abook.sqlite")!.AccountId);
        Assert.Equal("/p/ImapMail/imap.example.com",
            books.Find(b => Path.GetFileName(b.Path) == "abook-1.sqlite")!.AccountId);
        Assert.Empty(warnings);
        Directory.Delete(dir, true);
    }
}
