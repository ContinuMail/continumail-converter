// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.OutlookCategories;
using Microsoft.Data.Sqlite;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class CategoryFaiBakeTests
{
    // A profile whose prefs.js gives one mail tag a colour. No tagged messages are needed — the
    // mail-tag colour comes from the tag DEFINITION in prefs.js.
    private static string WriteProfileWithColouredTag(string root)
    {
        string profile = Path.Combine(root, "profile");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n");
        return profile;
    }

    private static ConversionConfig MailOnlyConfig(string profilePath, string groupName = "Personal", int maxSizeMB = 100)
    {
        string mbox = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.mbox");
        return new ConversionConfig
        {
            ProfilePath = profilePath,
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = groupName,
                    MaxSizeMB = maxSizeMB,
                    FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig> { new() { Type = "mbox", Path = mbox } },
                },
            },
        };
    }

    [Fact]
    public void Mail_only_conversion_with_coloured_tag_bakes_fai_into_a_created_calendar_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bake-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        try
        {
            new ConversionRunner().Run(MailOnlyConfig(WriteProfileWithColouredTag(root)), outDir);
            string pst = Directory.GetFiles(outDir, "*.pst")[0];

            var file = new PSTFile(pst, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            PSTFolder calendar = file.TopOfPersonalFolders.FindChildFolder("Calendar");
            Assert.NotNull(calendar);                                   // created for the FAI (mail-only store)
            Assert.Equal(1, calendar!.AssociatedMessageCount);
            MessageObject fai = calendar.GetAssociatedMessage(0);
            Assert.Equal(CategoryListFaiWriter.CategoryListMessageClass,
                fai.PC.GetStringProperty(PropertyID.PidTagMessageClass));
            Assert.Contains("name=\"Important\"",
                Encoding.UTF8.GetString(fai.PC.GetBytesProperty(PropertyID.PidTagRoamingXmlStream)));
            file.CloseFile();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // -----------------------------------------------------------------------
    // Appointment case — the fixed EnsureCalendarFolder must host the FAI in a dedicated
    // TOP-LEVEL "Calendar" folder, separate from the appointment's own nested event tree
    // (default ["Calendars", CalId] per MappingEngine). This is the case that exposed the
    // original direct-child-only scan bug (the event folder is a grandchild of the root).
    // -----------------------------------------------------------------------

    [Fact]
    public void Appointment_conversion_bakes_fai_into_dedicated_top_level_Calendar_separate_from_events()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-bakeappt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = MakeCalStoreWithCategorisedAppointment();
        string outDir = Path.Combine(dir, "out");
        Directory.CreateDirectory(outDir);
        try
        {
            var report = RunAppointmentConvertWith(dbPath, "test-cal", outDir);
            Assert.Equal(1, report.AppointmentsConverted);
            Assert.Contains("Meeting", report.CalendarCategoryNames);

            string pst = Directory.GetFiles(outDir, "*.pst")[0];
            var file = new PSTFile(pst, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);

            // 1. The FAI host: a dedicated top-level "Calendar" folder (IPF.Appointment), with
            //    exactly the one baked FAI whose XML mentions the appointment's category.
            PSTFolder calendar = file.TopOfPersonalFolders.FindChildFolder("Calendar");
            Assert.NotNull(calendar);
            Assert.Equal(PSTFolder.GetContainerClass(FolderItemTypeName.Appointment), calendar!.ContainerClass);
            Assert.Equal(1, calendar.AssociatedMessageCount);
            MessageObject fai = calendar.GetAssociatedMessage(0);
            Assert.Equal(CategoryListFaiWriter.CategoryListMessageClass,
                fai.PC.GetStringProperty(PropertyID.PidTagMessageClass));
            Assert.Contains("name=\"Meeting\"",
                Encoding.UTF8.GetString(fai.PC.GetBytesProperty(PropertyID.PidTagRoamingXmlStream)));

            // 2. The nested event tree exists separately — "Calendar" (FAI host) and
            //    "Calendars" (events) are distinct folders.
            PSTFolder calendarsRoot = file.TopOfPersonalFolders.FindChildFolder("Calendars");
            Assert.NotNull(calendarsRoot);
            Assert.NotSame(calendar, calendarsRoot);

            file.CloseFile();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// A minimal Thunderbird calendar SQLite store with one event carrying a CATEGORIES
    /// property, so mapping produces a categorised AppointmentRecord (drives a colour plan).
    /// Schema mirrors ConversionRunnerCalendarAttachmentTests.CreateCalSchema.
    /// </summary>
    private static string MakeCalStoreWithCategorisedAppointment()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cal-bakefai-{Guid.NewGuid():N}.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

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

        long start = MicrosFor(2026, 7, 9, 9, 0);
        long end = MicrosFor(2026, 7, 9, 10, 0);
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','bakefai-01@example.com','Categorised Event',0,{start},{end},'UTC');");
        X("INSERT INTO cal_properties (item_id,cal_id,key,value,recurrence_id,recurrence_id_tz) " +
          "VALUES ('bakefai-01@example.com','test-cal','CATEGORIES','Meeting,Suppliers',NULL,NULL);");

        return path;
    }

    // No ProfilePath: CategoryFaiPlanner hash-resolves a colour from the category name alone.
    private static Mail2Pst.Core.Reporting.ConversionReport RunAppointmentConvertWith(
        string dbPath, string calId, string outputDir)
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
                            StorePath = dbPath,
                            CalId = calId,
                            IncludeAppointments = true,
                            IncludeTasks = false,
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
