// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Core.Tests;

public class ConversionRunnerContactTests
{
    [Fact]
    public void Run_ContactOnlyProfile_WritesContactsAndCounts()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "abook.sqlite");
        using (var conn = new SqliteConnection($"Data Source={db}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE properties(card TEXT,name TEXT,value TEXT);
                INSERT INTO properties VALUES('c1','DisplayName','Alice');
                INSERT INTO properties VALUES('c1','PrimaryEmail','alice@example.com');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new() { Name = "Out", Sources = new List<SourceConfig>(),
                        Contacts = new List<ContactSourceConfig>
                        { new() { Path = db, Format = "thunderbird-sqlite" } } },
                },
            };
            var report = new ConversionRunner().Run(config, dir);
            Assert.Equal(1, report.ContactsConverted);
            Assert.True(File.Exists(Path.Combine(dir, "Out.pst")));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    }
}
