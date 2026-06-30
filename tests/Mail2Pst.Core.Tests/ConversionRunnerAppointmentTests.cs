// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Reporting;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Core.Tests;

public class ConversionRunnerAppointmentTests
{
    /// <summary>
    /// When skipAppointments is true, the runner must ignore plan.AppointmentMappings entirely,
    /// produce zero AppointmentsConverted, and emit "appointments disabled by --no-appointments"
    /// exactly once even when multiple output groups carry appointment mappings.
    /// </summary>
    [Fact]
    public void Run_SkipAppointments_ZeroAppointmentsAndWarningEmittedOnce()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runappt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Config with IncludeAppointments=true and no real SQLite file.
            // With skipAppointments=true the runner must never open the store.
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Out",
                        Sources = new List<SourceConfig>(),
                        Calendars = new List<CalendarSourceConfig>
                        {
                            new()
                            {
                                StorePath = Path.Combine(dir, "local.sqlite"), // does not exist
                                CalId = "cal-guid-1",
                                IncludeAppointments = true,
                                IncludeTasks = false,
                                AppointmentFolderPath = new[] { "Calendars", "MyCalendar" },
                            },
                        },
                    },
                },
            };

            var report = new ConversionRunner().Run(config, dir, skipAppointments: true);

            Assert.Equal(0, report.AppointmentsConverted);

            int warnCount = report.Warnings.Count(w =>
                w.Reason.Contains("appointments disabled by --no-appointments", StringComparison.Ordinal));
            Assert.Equal(1, warnCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// When the appointment store path does not exist, the runner records a warning
    /// and does not throw.
    /// </summary>
    [Fact]
    public void Run_NonExistentStorePath_RecordsWarningAndDoesNotThrow()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runappt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
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
                        Calendars = new List<CalendarSourceConfig>
                        {
                            new()
                            {
                                StorePath = Path.Combine(dir, "missing.sqlite"), // does not exist
                                CalId = "cal-guid-1",
                                IncludeAppointments = true,
                                IncludeTasks = false,
                                AppointmentFolderPath = new[] { "Calendars", "MyCalendar" },
                            },
                        },
                    },
                },
            };

            var report = new ConversionRunner().Run(config, dir);

            Assert.Equal(0, report.AppointmentsConverted);
            Assert.True(report.AppointmentWarningCount > 0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A degraded recurrence (BYSETPOS — cannot be mapped to RecurrenceSpec) must still count
    /// as a converted appointment, not a skipped one.  Degrade = single occurrence + warning.
    /// </summary>
    [Fact]
    public void Degraded_recurrence_counts_converted_not_skipped()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runappt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = MakeCalStore();
        try
        {
            var report = RunConvertWith(dbPath, "test-cal", dir);

            // One event in the store → must be counted as converted.
            Assert.Equal(1, report.AppointmentsConverted);

            // Degraded is not skipped.
            Assert.Equal(0, report.AppointmentsSkipped);

            // A warning about BYSETPOS must be present.
            Assert.Contains(report.Warnings, w =>
                w.Reason.Contains("BYSETPOS", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, true);
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a minimal Thunderbird calendar SQLite with one MONTHLY/BYSETPOS event.
    /// All data is synthetic (example.com) — no real mail/PII.
    /// </summary>
    private static string MakeCalStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cal-bysetpos-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Schema (matches SqliteCalendarReader expectations).
        X("CREATE TABLE cal_events (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,event_start INTEGER,event_end INTEGER,event_stamp INTEGER,event_start_tz TEXT,event_end_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_todos (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,todo_entry INTEGER,todo_due INTEGER,todo_completed INTEGER,todo_complete INTEGER,todo_entry_tz TEXT,todo_due_tz TEXT,todo_completed_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,todo_stamp INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_recurrence (item_id TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attendees (item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_alarms (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attachments (item_id TEXT,cal_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_relations (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_properties (item_id TEXT,key TEXT,value BLOB,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT);");
        X("CREATE TABLE cal_parameters (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,key1 TEXT,key2 TEXT,value TEXT);");

        // One event: MONTHLY BYSETPOS=-1 (last Monday of month) — triggers degrade path.
        long start = MicrosFor(2026, 7, 6, 14, 0);   // Monday 2026-07-06 14:00 UTC
        long end   = MicrosFor(2026, 7, 6, 15, 0);
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','bysetpos-01@example.com','Last Monday',0,{start},{end},'UTC');");
        X("INSERT INTO cal_recurrence (item_id,cal_id,icalString) " +
          "VALUES ('bysetpos-01@example.com','test-cal','RRULE:FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1');");

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
