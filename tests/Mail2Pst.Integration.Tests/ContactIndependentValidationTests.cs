// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Opt-in independent-reader gate for contact folders.
/// Requires MAIL2PST_PST_VALIDATOR to be set (same env-var as IndependentValidationTests).
/// Without it the test is skipped so CI (no Rust validator) stays green.
/// </summary>
public class ContactIndependentValidationTests
{
    // Zero-count folders the independent reader may surface that the converter did not create
    // (known template/system folders). Mirrors IndependentValidationTests.ZeroCountAllowlist.
    private static readonly HashSet<string> ZeroCountAllowlist = new(StringComparer.Ordinal)
    {
        // The from-scratch store (PSTFile.CreateEmptyStore) seeds a default "Deleted Items"
        // folder under the IPM subtree, which the independent reader surfaces with 0 messages.
        FolderPathKey.Join(new[] { "Deleted Items" }),
    };

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [SkippableFact]
    public void Convert_ProfileWithContacts_ValidatesContactFolderCounts()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to the built pst-validate exe to run the independent-reader gate.");

        const int ContactCount = 3;
        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-contact-indep-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        string dbPath = Path.Combine(outDir, "abook.sqlite");

        try
        {
            // Arrange: build a temp SQLite address book with N contacts.
            CreateSqliteAddressBook(dbPath, ContactCount);

            ConversionConfig config = ContactOnlyConfig(dbPath);

            // Act: convert.
            var (outputs, _) = RoundTripHarness.Convert(config, outDir);
            Assert.NotEmpty(outputs); // a conversion that produced no PST parts would otherwise pass vacuously

            // Expected path-keyed counts from the converter's own truth model (includes contact folders).
            Dictionary<string, int> expected = RoundTripHarness.BuildTruth(config)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);

            // Actual path-keyed counts, aggregated across all output parts via the INDEPENDENT reader.
            var actual = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string part in outputs)
            {
                ValidatorResult r = PstValidatorRunner.Run(part, Timeout);
                Assert.True(r.Opened, $"validator could not open {Path.GetFileName(part)}: " +
                    string.Join("; ", r.Errors.Select(e => $"{e.Stage}:{e.Message}")));
                Assert.Empty(r.Errors);
                foreach (ValidatedFolder f in r.Folders)
                {
                    string key = FolderPathKey.Join(f.Path);
                    actual[key] = actual.GetValueOrDefault(key) + f.MessageCount;
                }
            }

            // Every expected path must match exactly (includes the IPF.Contact folder with N contacts).
            foreach ((string key, int count) in expected)
            {
                Assert.True(actual.TryGetValue(key, out long got),
                    $"expected folder '{key}' missing from independent reader output");
                Assert.Equal(count, got);
            }

            // Any UNEXPECTED folder with messages is a failure; zero-count surprises must be allowlisted.
            foreach ((string key, long got) in actual)
            {
                if (expected.ContainsKey(key)) continue;
                if (got == 0 && ZeroCountAllowlist.Contains(key)) continue;
                Assert.Fail($"unexpected folder '{key}' with {got} message(s) in independent reader output");
            }
        }
        finally { Directory.Delete(outDir, true); }
    }

    /// <summary>
    /// Creates a minimal Thunderbird SQLite address book with <paramref name="count"/> contacts.
    /// Schema mirrors what SqliteAddressBookReader expects: a properties table with card/name/value columns.
    /// </summary>
    private static void CreateSqliteAddressBook(string dbPath, int count)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE properties (card TEXT NOT NULL, name TEXT NOT NULL, value TEXT)";
        create.ExecuteNonQuery();

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO properties (card, name, value) VALUES (@card, @name, @value)";
        var pCard = insert.Parameters.Add("@card", SqliteType.Text);
        var pName = insert.Parameters.Add("@name", SqliteType.Text);
        var pValue = insert.Parameters.Add("@value", SqliteType.Text);

        for (int i = 1; i <= count; i++)
        {
            pCard.Value = $"card{i}";
            pName.Value = "DisplayName";
            pValue.Value = $"Test Contact {i}";
            insert.ExecuteNonQuery();
        }
    }

    private static ConversionConfig ContactOnlyConfig(string dbPath) => new()
    {
        Outputs = new List<OutputGroupConfig>
        {
            new()
            {
                Name = "Contacts",
                MaxSizeMB = 50_000,
                // No mail sources — contacts only. ConfigValidator allows this (hasMail=false, hasContacts=true).
                Sources = new List<SourceConfig>(),
                Contacts = new List<ContactSourceConfig>
                {
                    new()
                    {
                        Path = dbPath,
                        Format = "thunderbird-sqlite",
                        TargetFolderPath = new[] { "Contacts", "TestBook" },
                    },
                },
            },
        },
    };
}
