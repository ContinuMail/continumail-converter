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
/// Opt-in independent-reader gate for appointment folders.
/// Requires MAIL2PST_PST_VALIDATOR to be set (same env-var as IndependentValidationTests).
/// Without it the test is skipped so CI (no Rust validator) stays green.
/// </summary>
public class AppointmentIndependentValidationTests
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
    public void Convert_CalendarWithEvents_ValidatesAppointmentFolderCounts()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to the built pst-validate exe to run the independent-reader gate.");

        // 2 non-recurring events → 2 IPM.Appointment items written; 1 recurring event → skipped by mapper.
        const int NonRecurringCount = 2;

        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-appt-indep-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        string dbPath = Path.Combine(outDir, "local.sqlite");

        try
        {
            // Arrange: build a temp SQLite calendar store with synthetic events.
            CreateSqliteCalendarStore(dbPath, NonRecurringCount);

            ConversionConfig config = CalendarOnlyConfig(dbPath);

            // Act: convert.
            var (outputs, _) = RoundTripHarness.Convert(config, outDir);
            Assert.NotEmpty(outputs); // a conversion that produced no PST parts would otherwise pass vacuously

            // Expected path-keyed counts from the converter's own truth model (includes appointment folders).
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

            // Every expected path must match exactly (includes the IPM.Appointment folder with N items).
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
    /// Creates a minimal Thunderbird SQLite calendar store with <paramref name="nonRecurringCount"/>
    /// non-recurring events plus one recurring event (which CalendarEventMapper skips).
    /// Schema mirrors what SqliteCalendarReader expects: cal_events, cal_recurrence, and
    /// companion side-table stubs (cal_todos, cal_alarms, cal_attendees, cal_attachments,
    /// cal_relations, cal_properties, cal_parameters).
    /// All data is synthetic — reserved example.org/example.com domains, no real PII.
    /// </summary>
    private static void CreateSqliteCalendarStore(string dbPath, int nonRecurringCount)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        void Exec(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Schema — mirrors what SqliteCalendarReader reads (full column list so the reader doesn't fail).
        Exec("CREATE TABLE cal_events (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,event_start INTEGER,event_end INTEGER,event_stamp INTEGER,event_start_tz TEXT,event_end_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,offline_journal INTEGER);");
        Exec("CREATE TABLE cal_todos (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,todo_entry INTEGER,todo_due INTEGER,todo_completed INTEGER,todo_complete INTEGER,todo_entry_tz TEXT,todo_due_tz TEXT,todo_completed_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,todo_stamp INTEGER,offline_journal INTEGER);");
        Exec("CREATE TABLE cal_recurrence (item_id TEXT,cal_id TEXT,icalString TEXT);");
        Exec("CREATE TABLE cal_attendees (item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT,icalString TEXT);");
        Exec("CREATE TABLE cal_alarms (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        Exec("CREATE TABLE cal_attachments (item_id TEXT,cal_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        Exec("CREATE TABLE cal_relations (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        Exec("CREATE TABLE cal_properties (item_id TEXT,key TEXT,value BLOB,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT);");
        Exec("CREATE TABLE cal_parameters (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,key1 TEXT,key2 TEXT,value TEXT);");

        // Non-recurring events: E1, E2, … (master rows; no cal_recurrence entries → mapper writes them).
        for (int i = 1; i <= nonRecurringCount; i++)
        {
            // event_start / event_end: synthetic PRTime values (µs since Unix epoch, 2026-07-10 UTC).
            long start = 1752148800000000L + (long)(i - 1) * 86_400_000_000L; // 2026-07-10 + (i-1) days
            long end   = start + 3_600_000_000L;                               // +1 hour

            using var insert = conn.CreateCommand();
            insert.CommandText =
                "INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz,event_end_tz,recurrence_id) " +
                "VALUES (@cal,@id,@title,0,@start,@end,'UTC','UTC',NULL)";
            insert.Parameters.AddWithValue("@cal",   "TESTCAL");
            insert.Parameters.AddWithValue("@id",    $"E{i}@example.com");
            insert.Parameters.AddWithValue("@title", $"Test Event {i}");
            insert.Parameters.AddWithValue("@start", start);
            insert.Parameters.AddWithValue("@end",   end);
            insert.ExecuteNonQuery();
        }

        // One recurring event: EREC — master row + a cal_recurrence entry → mapper skips it.
        Exec("INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz,event_end_tz,recurrence_id) " +
             "VALUES ('TESTCAL','EREC@example.com','Recurring Event (skipped)',0,1752148800000000,1752152400000000,'UTC','UTC',NULL);");
        Exec("INSERT INTO cal_recurrence (item_id,cal_id,icalString) VALUES ('EREC@example.com','TESTCAL','RRULE:FREQ=WEEKLY');");
    }

    private static ConversionConfig CalendarOnlyConfig(string dbPath) => new()
    {
        Outputs = new List<OutputGroupConfig>
        {
            new()
            {
                Name = "Appointments",
                MaxSizeMB = 50_000,
                // No mail or contact sources — appointments only.
                Sources = new List<SourceConfig>(),
                Contacts = new List<ContactSourceConfig>(),
                Calendars = new List<CalendarSourceConfig>
                {
                    new()
                    {
                        StorePath = dbPath,
                        CalId = "TESTCAL",
                        IncludeAppointments = true,
                        IncludeTasks = false,
                        AppointmentFolderPath = new[] { "Calendars", "TestCalendar" },
                    },
                },
            },
        },
    };
}
