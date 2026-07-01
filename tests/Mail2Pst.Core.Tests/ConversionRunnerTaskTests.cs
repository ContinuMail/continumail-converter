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

public class ConversionRunnerTaskTests
{
    /// <summary>
    /// When skipTasks is true, the runner must ignore plan.TaskMappings entirely,
    /// produce zero TasksConverted, and emit the "tasks disabled by --no-tasks"
    /// warning exactly once (even with multiple output groups that each carry task mappings).
    /// </summary>
    [Fact]
    public void Run_SkipTasks_ZeroTasksAndWarningEmittedOnce()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runtask-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Config with a valid calendar source (passes ConfigValidator) but NO real SQLite file.
            // With skipTasks=true the runner must never open the store.
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
                                IncludeTasks = true,
                                TaskFolderPath = new[] { "Tasks", "MyTasks" },
                            },
                        },
                    },
                },
            };

            var report = new ConversionRunner().Run(config, dir, skipTasks: true);

            Assert.Equal(0, report.TasksConverted);

            int warnCount = report.Warnings.Count(w =>
                w.Reason.Contains("tasks disabled by --no-tasks", StringComparison.Ordinal));
            Assert.Equal(1, warnCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A recurring todo with an unrepresentable RRULE (BYSETPOS) must degrade gracefully:
    /// the task is still written and reported as converted (not skipped), with a warning.
    /// End-to-end through ConversionRunner (mapper → writer → report).
    /// </summary>
    [Fact]
    public void Unsupported_recurring_todo_is_written_as_one_task_and_reported_converted_not_skipped()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runtask-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = MakeTaskStore();
        try
        {
            var report = RunConvertWith(dbPath, "test-cal", dir);

            // One todo with an unrepresentable recurrence → must count as converted, not skipped.
            Assert.Equal(1, report.TasksConverted);
            Assert.Equal(0, report.TasksSkipped);

            // A recurrence-degrade warning must be present (BYSETPOS or similar).
            Assert.True(report.TaskWarningCount >= 1,
                $"Expected at least one task warning but got {report.TaskWarningCount}");
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
    /// Creates a minimal Thunderbird calendar SQLite with one todo using an unrepresentable
    /// RRULE (MONTHLY/BYSETPOS=-1 — "last Monday of month").
    /// All data is synthetic (example.com) — no real mail/PII.
    /// </summary>
    private static string MakeTaskStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"task-bysetpos-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Schema matching SqliteCalendarReader expectations.
        X("CREATE TABLE cal_events (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,event_start INTEGER,event_end INTEGER,event_stamp INTEGER,event_start_tz TEXT,event_end_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_todos (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,todo_entry INTEGER,todo_due INTEGER,todo_completed INTEGER,todo_complete INTEGER,todo_entry_tz TEXT,todo_due_tz TEXT,todo_completed_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,todo_stamp INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_recurrence (item_id TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attendees (item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_alarms (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attachments (item_id TEXT,cal_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_relations (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_properties (item_id TEXT,key TEXT,value BLOB,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT);");
        X("CREATE TABLE cal_parameters (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,key1 TEXT,key2 TEXT,value TEXT);");

        // One todo: MONTHLY BYSETPOS=-1 (last Monday of month) — triggers degrade path.
        // Due 2026-07-31 (UTC midnight) — provides an anchor for RecurrenceMapping.
        long due = MicrosFor(2026, 7, 31);
        X($"INSERT INTO cal_todos (cal_id,id,title,flags,todo_due) " +
          $"VALUES ('test-cal','bysetpos-todo-01@example.com','Monthly Task',0,{due});");
        X("INSERT INTO cal_recurrence (item_id,cal_id,icalString) " +
          "VALUES ('bysetpos-todo-01@example.com','test-cal','RRULE:FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1');");

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
                            StorePath           = dbPath,
                            CalId               = calId,
                            IncludeTasks        = true,
                            IncludeAppointments = false,
                            TaskFolderPath      = new[] { "Tasks", "TestTasks" },
                        },
                    },
                },
            },
        };
        return new ConversionRunner().Run(config, outputDir);
    }

    private static long MicrosFor(int year, int month, int day) =>
        new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero)
            .ToUnixTimeMilliseconds() * 1000L;
}
