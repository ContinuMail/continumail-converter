// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Core.Tests;

public class ConversionRunnerCalendarAttachmentTests
{
    // -----------------------------------------------------------------------
    // Internal-seam unit test — assert record.Attachments directly.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_sets_inline_attachment_on_record()
    {
        var rec = new AppointmentRecord { Subject = "s" };
        var warns = new List<string>();
        ConversionRunner.ApplyCalendarAttachments(
            rec, new[] { new RawSideText("ATTACH;FILENAME=hi.txt;VALUE=BINARY;ENCODING=BASE64;FMTTYPE=text/plain:aGk=") },
            new CalendarAttachmentResolver(exportRoot: null), warns.Add);
        var a = Assert.Single(rec.Attachments);
        Assert.Equal(CalendarAttachmentKind.InlineBytes, a.Kind);
        Assert.Empty(warns);
    }

    // -----------------------------------------------------------------------
    // Full-run warning plumbing — via ConversionRunner.Run over synthetic store.
    // -----------------------------------------------------------------------

    /// <summary>
    /// An event with a remote-URL ATTACH must produce at least one appointment warning
    /// containing "remote URL preserved as link, not fetched".
    /// </summary>
    [Fact]
    public void Event_with_remote_url_attach_records_appointment_warning()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runattach-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = MakeCalStoreWithRemoteAttach();
        try
        {
            var report = RunConvertWith(dbPath, "test-cal", dir);

            Assert.Equal(1, report.AppointmentsConverted);
            Assert.True(report.AppointmentWarningCount >= 1,
                $"Expected at least one appointment warning but got {report.AppointmentWarningCount}");
            Assert.Contains(report.Warnings, w =>
                w.Reason.Contains("remote URL preserved as link, not fetched", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, true);
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    /// <summary>
    /// An event with an inline (base64-encoded) ATTACH must count as converted
    /// and produce no attachment-related warning.
    /// </summary>
    [Fact]
    public void Event_with_inline_attach_counts_converted_no_attachment_warning()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runattach-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = MakeCalStoreWithInlineAttach();
        try
        {
            var report = RunConvertWith(dbPath, "test-cal", dir);

            Assert.Equal(1, report.AppointmentsConverted);
            // Inline attach must not produce a "preserved as link" or "not fetched" warning.
            Assert.DoesNotContain(report.Warnings, w =>
                w.Reason.Contains("preserved as link", StringComparison.Ordinal) ||
                w.Reason.Contains("not fetched", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, true);
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // -----------------------------------------------------------------------
    // Pre-merge review #3: one bad item must not abort the whole conversion.
    // -----------------------------------------------------------------------

    /// <summary>
    /// A single event whose data throws inside the mapper (here: an out-of-range event_start that
    /// overflows during date construction) must be recorded as a skip, NOT abort the entire run —
    /// a good event in the same calendar must still convert. Before the fix the uncaught exception
    /// escaped ConversionRunner.Run and aborted mail/contacts/all calendars.
    /// </summary>
    [Fact]
    public void Event_that_throws_in_mapper_is_skipped_not_fatal_and_good_event_survives()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runbad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = MakeCalStoreWithBadAndGoodEvent();
        try
        {
            // Must not throw.
            var report = RunConvertWith(dbPath, "test-cal", dir);

            // The good event converted; the bad one was skipped (not fatal).
            Assert.Equal(1, report.AppointmentsConverted);
            Assert.True(report.AppointmentsSkipped >= 1,
                $"Expected the bad event to be recorded as skipped but got {report.AppointmentsSkipped}");
        }
        finally
        {
            Directory.Delete(dir, true);
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    /// <summary>
    /// A store with one event carrying an out-of-range (overflowing) start plus one normal event.
    /// The bad event is ordered first to prove the good one is still reached after containment.
    /// </summary>
    private static string MakeCalStoreWithBadAndGoodEvent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cal-badgood-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        CreateCalSchema(conn);

        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Bad event: all-day (flags=8) with an absurd event_start that overflows date construction.
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','bad-01@example.com','Bad Event',8,{long.MaxValue},{long.MaxValue},'UTC');");
        // Good event: a normal timed event.
        long start = MicrosFor(2026, 7, 8, 9, 0);
        long end   = MicrosFor(2026, 7, 8, 10, 0);
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','good-01@example.com','Good Event',0,{start},{end},'UTC');");

        return path;
    }

    // -----------------------------------------------------------------------
    // Helpers — synthetic SQLite stores (no real mail/PII, all example.com).
    // -----------------------------------------------------------------------

    private static void CreateCalSchema(SqliteConnection conn)
    {
        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        X("CREATE TABLE cal_events (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,event_start INTEGER,event_end INTEGER,event_stamp INTEGER,event_start_tz TEXT,event_end_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_todos (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,todo_entry INTEGER,todo_due INTEGER,todo_completed INTEGER,todo_complete INTEGER,todo_entry_tz TEXT,todo_due_tz TEXT,todo_completed_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,todo_stamp INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_recurrence (item_id TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attendees (item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_alarms (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attachments (item_id TEXT,cal_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_relations (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_properties (item_id TEXT,key TEXT,value BLOB,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT);");
        X("CREATE TABLE cal_parameters (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,key1 TEXT,key2 TEXT,value TEXT);");
    }

    /// <summary>
    /// A minimal Thunderbird calendar store with one event that has a remote-URL attachment.
    /// </summary>
    private static string MakeCalStoreWithRemoteAttach()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cal-remoteatt-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        CreateCalSchema(conn);

        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        long start = MicrosFor(2026, 7, 6, 14, 0);
        long end   = MicrosFor(2026, 7, 6, 15, 0);
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','remoteatt-01@example.com','Remote Attach Event',0,{start},{end},'UTC');");
        // Remote URL attachment.
        X("INSERT INTO cal_attachments (item_id,cal_id,recurrence_id,recurrence_id_tz,icalString) " +
          "VALUES ('remoteatt-01@example.com','test-cal',NULL,NULL," +
          "'ATTACH;FILENAME=doc.pdf:https://drive.example.com/doc.pdf');");

        return path;
    }

    /// <summary>
    /// A minimal Thunderbird calendar store with one event that has a base64-inline attachment.
    /// </summary>
    private static string MakeCalStoreWithInlineAttach()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cal-inlineatt-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        CreateCalSchema(conn);

        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        long start = MicrosFor(2026, 7, 7, 10, 0);
        long end   = MicrosFor(2026, 7, 7, 11, 0);
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','inlineatt-01@example.com','Inline Attach Event',0,{start},{end},'UTC');");
        // Inline base64 attachment — "hi" encoded as aGk=.
        X("INSERT INTO cal_attachments (item_id,cal_id,recurrence_id,recurrence_id_tz,icalString) " +
          "VALUES ('inlineatt-01@example.com','test-cal',NULL,NULL," +
          "'ATTACH;FILENAME=hi.txt;VALUE=BINARY;ENCODING=BASE64;FMTTYPE=text/plain:aGk=');");

        return path;
    }

    private static ConversionReport RunConvertWith(string dbPath, string calId, string outputDir)
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name    = "Out",
                    Sources = new List<SourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath             = dbPath,
                            CalId                 = calId,
                            IncludeAppointments   = true,
                            IncludeTasks          = false,
                            AppointmentFolderPath = new[] { "Calendars", "TestCal" },
                        },
                    },
                },
            },
        };
        return new ConversionRunner().Run(config, outputDir);
    }

    private static long MicrosFor(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)
            .ToUnixTimeMilliseconds() * 1000L;
}
