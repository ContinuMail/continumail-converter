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

    [Fact]
    public void Run_ContactSourceWithCorruptMab_SkipsBookAndCompletes_DoesNotAbort()
    {
        // #1 end-to-end: a corrupt .mab must NOT abort the whole conversion. The run completes and
        // the book is recorded as a skipped contact (ContactsSkipped), not thrown as a fatal.
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runner-mab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string mab = Path.Combine(dir, "abook.mab");
        File.WriteAllText(mab, "< (80=ns:msg:db:row:scope:cards:all)(81=DisplayName");
        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Out",
                        Sources = new List<SourceConfig>(),
                        Contacts = new List<ContactSourceConfig>
                        {
                            new() { Path = mab, Format = "thunderbird-mab" },
                        },
                    },
                },
            };

            // var (not an explicit type) to match the existing test's style — ConversionReport
            // lives in Mail2Pst.Core.Reporting, which this file does not import.
            var report = new ConversionRunner().Run(config, dir);

            Assert.Equal(1, report.ContactsSkipped);
            Assert.Equal(0, report.ContactsConverted);
            // §3.1: the dropped book must be VISIBLE in the report surface, naming what was skipped.
            // RecordContactSkipped adds a warning entry "Contact skipped [<book>]: <error>"; the
            // derived book name is Path.GetFileNameWithoutExtension("abook.mab") == "abook".
            Assert.Contains(report.Warnings, w =>
                w.Reason.Contains("Contact skipped [abook]", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, true); }
    }
}
