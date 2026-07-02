// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class DiscoverCalendarsTests
{
    // Creates a minimal calendar SQLite store with all required tables.
    // addEvent=true inserts one master event row for calId; addTodo=true inserts one master todo row.
    private static void MakeCalStore(string path, string calId, bool addEvent = true, bool addTodo = false)
    {
        using var c = new SqliteConnection($"Data Source={path}"); c.Open();
        void X(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        X("CREATE TABLE cal_events (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,event_start INTEGER,event_end INTEGER,event_stamp INTEGER,event_start_tz TEXT,event_end_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_todos (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,todo_entry INTEGER,todo_due INTEGER,todo_completed INTEGER,todo_complete INTEGER,todo_entry_tz TEXT,todo_due_tz TEXT,todo_completed_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,todo_stamp INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_recurrence (item_id TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attendees (item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_alarms (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attachments (item_id TEXT,cal_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_relations (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_properties (item_id TEXT,key TEXT,value BLOB,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT);");
        X("CREATE TABLE cal_parameters (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,key1 TEXT,key2 TEXT,value TEXT);");
        if (addEvent)
            X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,recurrence_id) " +
              $"VALUES ('{calId}','ev1','Test Event',0,1782810000000000,1782811800000000,NULL);");
        if (addTodo)
            X($"INSERT INTO cal_todos (cal_id,id,title,flags,todo_entry,todo_due,todo_complete,recurrence_id) " +
              $"VALUES ('{calId}','td1','Test Todo',0,1782810000000000,1782820000000000,0,NULL);");
    }

    // Both-stores: same cal_id present in local.sqlite AND cache.sqlite.
    // registry type="storage" -> local wins; type="caldav" -> cache wins.
    [Theory]
    [InlineData("storage", "local",  "cache store ignored")]
    [InlineData("caldav",  "cache",  "local store ignored")]
    public void DiscoverCalendars_BothStores_RegistryTypeControlsWinner(
        string calType, string expectedStore, string warningSnippet)
    {
        string profile = Path.Combine(Path.GetTempPath(), $"m2p-both-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profile);
        string calDataDir = Path.Combine(profile, "calendar-data");
        Directory.CreateDirectory(calDataDir);
        try
        {
            // Register BOTH with the supplied type; synthetic/reserved data only.
            File.WriteAllText(Path.Combine(profile, "prefs.js"),
                $"user_pref(\"calendar.registry.BOTH.name\", \"Work\");\n" +
                $"user_pref(\"calendar.registry.BOTH.type\", \"{calType}\");\n" +
                $"user_pref(\"calendar.registry.BOTH.calendar-main-in-composite\", true);\n");

            // Insert the same cal_id into BOTH stores so both-stores branch is taken.
            MakeCalStore(Path.Combine(calDataDir, "local.sqlite"), "BOTH", addEvent: true);
            MakeCalStore(Path.Combine(calDataDir, "cache.sqlite"), "BOTH", addEvent: true);

            var res = MailProfileDiscovery.DiscoverCalendars(profile);

            // Calendar must appear exactly once (no duplication).
            var cal = Assert.Single(res.Calendars, c => c.CalId == "BOTH");

            // Registry-driven store preference.
            Assert.Equal(expectedStore, cal.StoreKind);

            // Chosen store path must point at the winning file.
            string expectedFile = expectedStore == "local" ? "local.sqlite" : "cache.sqlite";
            Assert.Equal(expectedFile, Path.GetFileName(cal.StorePath), StringComparer.OrdinalIgnoreCase);

            // A warning about the ignored store must be emitted.
            Assert.Contains(res.Warnings, w => w.Message.Contains("BOTH") && w.Message.Contains(warningSnippet));
        }
        finally { Directory.Delete(profile, true); }
    }

    /// <summary>
    /// Pre-merge review #9: a calendar whose Thunderbird name contains a folder-invalid character
    /// (e.g. "Home / Work") must still yield a VALID synthesized folder path. Before the fix the raw
    /// name flowed into ["Calendars", name] and ConfigValidator hard-failed, aborting the ENTIRE
    /// conversion (mail + all other calendars). DisplayName keeps the real name; the folder leaf is sanitized.
    /// </summary>
    [Fact]
    public void DiscoverCalendars_InvalidCharInName_SynthesizesValidFolderPath()
    {
        string profile = Path.Combine(Path.GetTempPath(), $"m2p-badname-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profile);
        string calDataDir = Path.Combine(profile, "calendar-data");
        Directory.CreateDirectory(calDataDir);
        try
        {
            File.WriteAllText(Path.Combine(profile, "prefs.js"),
                "user_pref(\"calendar.registry.BAD.name\", \"Home / Work\");\n" +
                "user_pref(\"calendar.registry.BAD.type\", \"storage\");\n");
            MakeCalStore(Path.Combine(calDataDir, "local.sqlite"), "BAD", addEvent: true, addTodo: true);

            var res = MailProfileDiscovery.DiscoverCalendars(profile);
            var cal = Assert.Single(res.Calendars, c => c.CalId == "BAD");

            Assert.Equal("Home / Work", cal.DisplayName);   // real name preserved for display
            // Synthesized folder paths must pass the engine's folder-name validator (no throw).
            Mail2Pst.Core.Config.FolderNameValidator.ValidatePath(cal.DefaultCalendarFolderPath);
            Mail2Pst.Core.Config.FolderNameValidator.ValidatePath(cal.DefaultTaskFolderPath);
        }
        finally { Directory.Delete(profile, true); }
    }

    [Fact]
    public void DiscoverCalendars_RegistryPlusOrphan()
    {
        string profile = Path.Combine(Path.GetTempPath(), $"m2p-cal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profile);
        string calDataDir = Path.Combine(profile, "calendar-data");
        Directory.CreateDirectory(calDataDir);
        try
        {
            // prefs.js: REG1 = "Home", type=storage, visible; reserved/synthetic data
            File.WriteAllText(Path.Combine(profile, "prefs.js"),
                "user_pref(\"calendar.registry.REG1.name\", \"Home\");\n" +
                "user_pref(\"calendar.registry.REG1.type\", \"storage\");\n" +
                "user_pref(\"calendar.registry.REG1.calendar-main-in-composite\", true);\n");

            // local.sqlite: one event row for registered calendar REG1
            MakeCalStore(Path.Combine(calDataDir, "local.sqlite"), "REG1", addEvent: true);

            // cache.sqlite: one event row for unregistered ORPH — no prefs.js entry
            MakeCalStore(Path.Combine(calDataDir, "cache.sqlite"), "ORPH", addEvent: true);

            var res = MailProfileDiscovery.DiscoverCalendars(profile);

            // Registered calendar: name + store from prefs.js; count from actual rows
            var home = res.Calendars.Single(c => c.CalId == "REG1");
            Assert.Equal("Home", home.DisplayName);
            Assert.Equal("local", home.StoreKind);
            Assert.Equal(1, home.EventCount);       // one EventGroup (master only)
            Assert.Equal(new[] { "Calendars", "Home" }, home.DefaultCalendarFolderPath);

            // Unregistered orphan: synthesized name, cache store, not visible, warning emitted
            var orphan = res.Calendars.Single(c => c.CalId == "ORPH");
            Assert.Equal("cache", orphan.StoreKind);
            Assert.False(orphan.IsVisibleInThunderbird);
            Assert.Contains("ORPH", orphan.DisplayName);
            Assert.Contains(res.Warnings, w => w.Message.Contains("ORPH"));
        }
        finally { Directory.Delete(profile, true); }
    }
}
