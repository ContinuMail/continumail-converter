// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Contacts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

public class SqliteAddressBookReaderVCardTests
{
    private static string BuildDb(params (string card, string name, string value)[] rows)
    {
        string db = Path.Combine(Path.GetTempPath(), $"m2p-vc-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        using (var c = conn.CreateCommand()) { c.CommandText = "CREATE TABLE properties (card TEXT, name TEXT, value TEXT)"; c.ExecuteNonQuery(); }
        foreach (var (card, name, value) in rows)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO properties VALUES ($c,$n,$v)";
            ins.Parameters.AddWithValue("$c", card); ins.Parameters.AddWithValue("$n", name); ins.Parameters.AddWithValue("$v", value);
            ins.ExecuteNonQuery();
        }
        return db;
    }

    [Fact]
    public void Read_CardWithVCard_UsesVCardPath_RichFieldsPresent()
    {
        string vcard = "BEGIN:VCARD\nVERSION:4.0\nFN:Rich Card\nORG:Acme;Eng\nTITLE:Boss\nEMAIL:rich@x.dk\nEND:VCARD\n";
        string db = BuildDb(
            ("c1", "DisplayName", "Rich Card"),
            ("c1", "PrimaryEmail", "rich@x.dk"),
            ("c1", "_vCard", vcard));
        try
        {
            var book = new AddressBook { DisplayName = "P", Path = db, Format = AddressBookFormat.ThunderbirdSqlite };
            var r = new SqliteAddressBookReader().Read(book).Single();
            Assert.True(r.Success);
            Assert.Equal("Acme", r.Contact!.CompanyName);   // only in the vCard, NOT in EAV rows
            Assert.Equal("Boss", r.Contact.JobTitle);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(db); }
    }

    [Fact]
    public void Read_CardWithoutVCard_UsesEavPath()
    {
        string db = BuildDb(("c1", "DisplayName", "Plain"), ("c1", "PrimaryEmail", "p@x.dk"), ("c1", "Company", "EavCo"));
        try
        {
            var book = new AddressBook { DisplayName = "P", Path = db, Format = AddressBookFormat.ThunderbirdSqlite };
            var r = new SqliteAddressBookReader().Read(book).Single();
            Assert.True(r.Success);
            Assert.Equal("EavCo", r.Contact!.CompanyName);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(db); }
    }

    [Fact]
    public void Read_MalformedVCard_FallsBackToEav_WithWarning()
    {
        string db = BuildDb(
            ("c1", "DisplayName", "Fallback Guy"),
            ("c1", "PrimaryEmail", "fb@x.dk"),
            ("c1", "_vCard", "this is not a vcard"));
        try
        {
            var book = new AddressBook { DisplayName = "P", Path = db, Format = AddressBookFormat.ThunderbirdSqlite };
            var r = new SqliteAddressBookReader().Read(book).Single();
            Assert.True(r.Success);                       // not discarded
            Assert.Equal("Fallback Guy", r.Contact!.DisplayName); // from EAV
            Assert.NotEmpty(r.Warnings);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(db); }
    }

    [Fact]
    public void Read_ParsedButEmptyVCard_FallsBackToEav_WithWarning()
    {
        // A syntactically valid vCard with no usable identity (no FN, no EMAIL).
        string emptyish = "BEGIN:VCARD\nVERSION:4.0\nNOTE:hi\nEND:VCARD\n";
        string db = BuildDb(
            ("c1", "DisplayName", "Index Name"),
            ("c1", "PrimaryEmail", "idx@x.dk"),
            ("c1", "_vCard", emptyish));
        try
        {
            var book = new AddressBook { DisplayName = "P", Path = db, Format = AddressBookFormat.ThunderbirdSqlite };
            var r = new SqliteAddressBookReader().Read(book).Single();
            Assert.True(r.Success);                            // not discarded
            Assert.Equal("Index Name", r.Contact!.DisplayName); // identity recovered from EAV index rows
            Assert.Contains("idx@x.dk", r.Contact.Emails);
            Assert.NotEmpty(r.Warnings);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(db); }
    }
}
