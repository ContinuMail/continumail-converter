// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mail2Pst.Cli;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Integration.Tests;

[Collection("ConsoleCapture")]
public class ConvertCommandColourPlanTests
{
    private static string CreateMinimalMbox(string dir)
    {
        string mbox = Path.Combine(dir, "in.mbox");
        File.WriteAllText(mbox, "From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: <a@h>\nSubject: t\n\nbody\n");
        return mbox;
    }

    private static string CreateConfig(string dir, string mbox, string? profilePath = null)
    {
        string cfg = Path.Combine(dir, "config.json");
        string profilePart = profilePath is not null
            ? ",\"profilePath\":" + JsonSerializer.Serialize(profilePath)
            : "";
        File.WriteAllText(cfg,
            "{\"outputs\":[{\"name\":\"Out\",\"sources\":[{\"path\":" +
            JsonSerializer.Serialize(mbox) + ",\"type\":\"mbox\"}]}]" +
            profilePart + "}");
        return cfg;
    }

    // -----------------------------------------------------------------------
    // Calendar-only config (no mail source) — a synthetic Thunderbird calendar
    // SQLite store, mirroring ConversionRunnerCalendarAttachmentTests' schema.
    // -----------------------------------------------------------------------

    private static string CreateCalendarConfig(string dir, string storePath, string? profilePath)
    {
        string cfg = Path.Combine(dir, "config.json");
        string profilePart = profilePath is not null
            ? ",\"profilePath\":" + JsonSerializer.Serialize(profilePath)
            : "";
        File.WriteAllText(cfg,
            "{\"outputs\":[{\"name\":\"Out\",\"sources\":[],\"calendars\":[{" +
            "\"storePath\":" + JsonSerializer.Serialize(storePath) + "," +
            "\"calId\":\"test-cal\"," +
            "\"includeAppointments\":true," +
            "\"includeTasks\":false" +
            "}]}]" + profilePart + "}");
        return cfg;
    }

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

    /// <summary>A calendar store with one event carrying CATEGORIES "Meeting,Suppliers".</summary>
    private static string CreateCalStoreWithCategories(string dir)
    {
        string path = Path.Combine(dir, "cal-categories.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        CreateCalSchema(conn);

        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // 2026-07-09 09:00-10:00 UTC as PRTime microseconds.
        const long Start = 1752051600000000L;
        const long End = 1752055200000000L;
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','categories-01@example.com','Categorised Event',0,{Start},{End},'UTC');");
        X("INSERT INTO cal_properties (item_id,cal_id,key,value,recurrence_id,recurrence_id_tz) " +
          "VALUES ('categories-01@example.com','test-cal','CATEGORIES','Meeting,Suppliers',NULL,NULL);");

        return path;
    }

    /// <summary>A calendar store with one event that has NO categories.</summary>
    private static string CreateCalStoreWithoutCategories(string dir)
    {
        string path = Path.Combine(dir, "cal-nocategories.sqlite");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        CreateCalSchema(conn);

        void X(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        const long Start = 1752051600000000L;
        const long End = 1752055200000000L;
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz) " +
          $"VALUES ('test-cal','plain-01@example.com','Plain Event',0,{Start},{End},'UTC');");

        return path;
    }

    private static JsonElement RunConvertAndGetColourPlan(string cfg, string outDir)
    {
        var sw = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(sw);
        try
        {
            int exit = ConvertCommand.Run(new[] { "--config", cfg, "--output", outDir });
            Assert.Equal(0, exit);

            string done = sw.ToString().Split('\n').First(l => l.Contains("\"type\":\"done\""));
            using var doc = JsonDocument.Parse(done);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("colourPlan", out JsonElement plan), "done must have colourPlan");
            Assert.Equal(JsonValueKind.Array, plan.ValueKind);
            return plan.Clone();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Convert_WithProfilePathAndColouredTag_DoneHasColourPlanEntry()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-colour-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string profileDir = Path.Combine(dir, "profile");
        Directory.CreateDirectory(profileDir);

        // Write a minimal prefs.js with one coloured tag
        File.WriteAllText(Path.Combine(profileDir, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n");

        string mbox = CreateMinimalMbox(dir);
        string cfg = CreateConfig(dir, mbox, profileDir);
        string outDir = Path.Combine(dir, "out");

        var sw = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(sw);
        try
        {
            int exit = ConvertCommand.Run(new[] { "--config", cfg, "--output", outDir });
            Assert.Equal(0, exit);

            string done = sw.ToString().Split('\n').First(l => l.Contains("\"type\":\"done\""));
            using var doc = JsonDocument.Parse(done);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("colourPlan", out JsonElement plan), "done must have colourPlan");
            Assert.Equal(JsonValueKind.Array, plan.ValueKind);

            // Find the Important entry
            var entry = plan.EnumerateArray().FirstOrDefault(e =>
                e.TryGetProperty("name", out var n) && n.GetString() == "Important");
            Assert.NotEqual(default, entry);
            Assert.Equal("#FF0000", entry.GetProperty("hex").GetString());
            Assert.Equal("would-add", entry.GetProperty("action").GetString());
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Convert_WithoutProfilePath_DoneHasEmptyColourPlan()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-colour-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        string mbox = CreateMinimalMbox(dir);
        string cfg = CreateConfig(dir, mbox, profilePath: null);
        string outDir = Path.Combine(dir, "out");

        var sw = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(sw);
        try
        {
            int exit = ConvertCommand.Run(new[] { "--config", cfg, "--output", outDir });
            Assert.Equal(0, exit);

            string done = sw.ToString().Split('\n').First(l => l.Contains("\"type\":\"done\""));
            using var doc = JsonDocument.Parse(done);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("colourPlan", out JsonElement plan), "done must have colourPlan");
            Assert.Equal(JsonValueKind.Array, plan.ValueKind);
            Assert.Equal(0, plan.GetArrayLength());
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// Converted calendar events carrying CATEGORIES "Meeting"/"Suppliers" must surface as
    /// colour-plan candidates (Thunderbird's computed hash colours, since no override is set).
    /// </summary>
    [Fact]
    public void Convert_WithCalendarCategories_ColourPlanContainsCalendarCategoryColours()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-colour-cal-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string profileDir = Path.Combine(dir, "profile");
        Directory.CreateDirectory(profileDir);

        // No overrides — resolver falls back to Thunderbird's computed hash colours.
        File.WriteAllText(Path.Combine(profileDir, "prefs.js"), "");

        string dbPath = CreateCalStoreWithCategories(dir);
        string cfg = CreateCalendarConfig(dir, dbPath, profileDir);
        string outDir = Path.Combine(dir, "out");

        try
        {
            JsonElement plan = RunConvertAndGetColourPlan(cfg, outDir);

            var meeting = plan.EnumerateArray().FirstOrDefault(e =>
                e.TryGetProperty("name", out var n) && n.GetString() == "Meeting");
            Assert.NotEqual(default, meeting);
            Assert.Equal("#FFFF66", meeting.GetProperty("hex").GetString());
            Assert.Equal("would-add", meeting.GetProperty("action").GetString());

            var suppliers = plan.EnumerateArray().FirstOrDefault(e =>
                e.TryGetProperty("name", out var n) && n.GetString() == "Suppliers");
            Assert.NotEqual(default, suppliers);
            Assert.Equal("#000099", suppliers.GetProperty("hex").GetString());
            Assert.Equal("would-add", suppliers.GetProperty("action").GetString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// No-regression case: a profile with mail tags but NO calendar/task categories must
    /// produce the same colourPlan as the pre-change (mail-tags-only) code path.
    /// </summary>
    [Fact]
    public void Convert_WithMailTagsButNoCalendarCategories_ColourPlanUnchanged()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-colour-noregr-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string profileDir = Path.Combine(dir, "profile");
        Directory.CreateDirectory(profileDir);

        File.WriteAllText(Path.Combine(profileDir, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n");

        string mbox = CreateMinimalMbox(dir);
        string cfg = CreateConfig(dir, mbox, profileDir);
        string outDir = Path.Combine(dir, "out");

        try
        {
            JsonElement plan = RunConvertAndGetColourPlan(cfg, outDir);

            // Mail-only conversion (no calendar sources) -> report.CalendarCategoryNames is empty,
            // so the merge in CategoryColorPlan.Build's 3-arg overload contributes nothing: the
            // plan must be identical in shape/content to the mail-tags-only (2-arg) behaviour.
            var entries = plan.EnumerateArray().ToList();
            var important = Assert.Single(entries, e =>
                e.TryGetProperty("name", out var n) && n.GetString() == "Important");
            Assert.Equal("#FF0000", important.GetProperty("hex").GetString());
            Assert.Equal("would-add", important.GetProperty("action").GetString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// Source-colour guard: a prefs.js with a per-calendar source colour
    /// (calendar.registry.&lt;uuid&gt;.color) must never surface as a colour-plan candidate —
    /// that pref is a calendar *source* colour, not a category colour, and the converted
    /// item carries no categories either.
    /// </summary>
    [Fact]
    public void Convert_WithCalendarRegistrySourceColourButNoCategories_NeverAppearsInColourPlan()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-colour-guard-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string profileDir = Path.Combine(dir, "profile");
        Directory.CreateDirectory(profileDir);

        // A per-calendar source colour (NOT a category colour) — must be ignored entirely.
        const string SourceColour = "#FF0080";
        File.WriteAllText(Path.Combine(profileDir, "prefs.js"),
            "user_pref(\"calendar.registry.94e695bc.color\", \"" + SourceColour + "\");\n");

        string dbPath = CreateCalStoreWithoutCategories(dir);
        string cfg = CreateCalendarConfig(dir, dbPath, profileDir);
        string outDir = Path.Combine(dir, "out");

        try
        {
            JsonElement plan = RunConvertAndGetColourPlan(cfg, outDir);

            // The store's per-calendar source colour must never leak into the colour plan
            // (built-in mail-tag candidates with no colour are allowed; that colour specifically must not appear).
            Assert.DoesNotContain(plan.EnumerateArray(), e =>
                e.TryGetProperty("hex", out var h) && h.GetString() == SourceColour);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
