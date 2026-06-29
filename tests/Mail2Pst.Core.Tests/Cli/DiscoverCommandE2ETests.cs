// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

// Spawns the real built CLI to exercise the `discover` command wiring end-to-end
// (argument parsing, the Directory.Exists guard, the JSON projection, CliEventSerializer).
// Mirrors the spawn pattern in CliSchemaVersionE2ETests; the test project already builds the CLI.
public class DiscoverCommandE2ETests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mail2Pst.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Mail2Pst.sln).");
    }

    private static string CliDllPath()
    {
        string config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/bin/Release/") ? "Release" : "Debug";
        string dll = Path.Combine(RepoRoot(), "src", "Mail2Pst.Cli", "bin", config, "net8.0", "Mail2Pst.Cli.dll");
        Assert.True(File.Exists(dll), $"CLI build output not found at {dll}");
        return dll;
    }

    private static (int exitCode, string stdout, string stderr) RunCli(string args)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{CliDllPath()}\" {args}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = RepoRoot(),
        };
        using Process proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();   // kept for failure diagnostics
        proc.WaitForExit(60_000);
        return (proc.ExitCode, stdout, stderr);
    }

    private static void WriteMbox(string path)
        => File.WriteAllText(path, "From a@b Mon Jan  1 00:00:00 2020\r\n\r\nx\r\n", new UTF8Encoding(false));

    [Fact]
    public void Discover_NestedTree_EmitsSourcesWithSchemaVersion()
    {
        string tree = Path.Combine(Path.GetTempPath(), "m2p-disccli-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tree, "Inbox.sbd"));
        try
        {
            WriteMbox(Path.Combine(tree, "Inbox"));
            WriteMbox(Path.Combine(tree, "Inbox.sbd", "Acme"));

            (int exit, string stdout, string stderr) = RunCli($"discover --input \"{tree}\"");
            Assert.True(exit == 0, $"expected exit 0, got {exit}. stderr: {stderr}");

            using JsonDocument doc = JsonDocument.Parse(stdout);
            JsonElement root = doc.RootElement;
            Assert.Equal("discovery", root.GetProperty("type").GetString());
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("thunderbird", root.GetProperty("layout").GetString());

            JsonElement sources = root.GetProperty("sources");
            Assert.Equal(2, sources.GetArrayLength());
            JsonElement acme = sources.EnumerateArray()
                .First(s => s.GetProperty("displayName").GetString() == "Acme");
            Assert.Equal("mbox", acme.GetProperty("type").GetString());
            string[] seg = acme.GetProperty("targetFolderPath").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.Equal(new[] { "Inbox", "Acme" }, seg);
        }
        finally { Directory.Delete(tree, true); }
    }

    [Fact]
    public void Discover_MissingInputDir_EmitsFatalJsonError_Exit1()
    {
        string missing = Path.Combine(Path.GetTempPath(), "m2p-nope-" + Guid.NewGuid());
        (int exit, string stdout, string stderr) = RunCli($"discover --input \"{missing}\"");
        Assert.True(exit == 1, $"expected exit 1, got {exit}. stderr: {stderr}");
        using JsonDocument doc = JsonDocument.Parse(stdout.Trim());
        JsonElement root = doc.RootElement;
        Assert.Equal("error", root.GetProperty("type").GetString());
        Assert.Equal("discover", root.GetProperty("stage").GetString());
        Assert.True(root.GetProperty("fatal").GetBoolean());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void Discover_EmitsMsfPath_AndPairingSummary()
    {
        string dir = Path.Combine(Path.GetTempPath(), "m2p-msf-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Inbox"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(dir, "Inbox.msf"), Array.Empty<byte>());
        try
        {
            (int exit, string stdout, string stderr) = RunCli($"discover --input \"{dir}\"");
            Assert.True(exit == 0, $"expected exit 0, got {exit}. stderr: {stderr}");

            using JsonDocument doc = JsonDocument.Parse(stdout);
            JsonElement root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32()); // existing serializer contract
            JsonElement src = root.GetProperty("sources").EnumerateArray().Single();
            Assert.EndsWith("Inbox.msf", src.GetProperty("msfPath").GetString());
            Assert.Equal(1, root.GetProperty("pairing").GetProperty("pairedMsfCount").GetInt32());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Discover_Profile_EmitsCalendarsArray()
    {
        // Build a minimal Thunderbird profile: Mail/LocalFolders triggers "thunderbird-profile"
        // layout which calls DiscoverCalendars. A prefs.js registry entry + a local.sqlite row
        // produce one DiscoveredCalendarSource. The test asserts it surfaces in the emitted JSON.
        string profile = Path.Combine(Path.GetTempPath(), "m2p-cal-e2e-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(profile, "Mail", "Local Folders"));
        Directory.CreateDirectory(Path.Combine(profile, "calendar-data"));
        try
        {
            File.WriteAllText(Path.Combine(profile, "prefs.js"),
                "user_pref(\"calendar.registry.CAL1.name\", \"Work\");\n" +
                "user_pref(\"calendar.registry.CAL1.type\", \"storage\");\n" +
                "user_pref(\"calendar.registry.CAL1.calendar-main-in-composite\", true);\n",
                new UTF8Encoding(false));

            MakeCalStore(Path.Combine(profile, "calendar-data", "local.sqlite"), "CAL1", addEvent: true);

            (int exit, string stdout, string stderr) = RunCli($"discover --input \"{profile}\"");
            Assert.True(exit == 0, $"expected exit 0, got {exit}. stderr: {stderr}");

            using JsonDocument doc = JsonDocument.Parse(stdout);
            JsonElement root = doc.RootElement;
            Assert.Equal("discovery", root.GetProperty("type").GetString());
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

            JsonElement calendars = root.GetProperty("calendars");
            Assert.Equal(1, calendars.GetArrayLength());
            JsonElement cal = calendars.EnumerateArray().Single();
            Assert.Equal("CAL1", cal.GetProperty("calId").GetString());
            Assert.Equal("Work", cal.GetProperty("displayName").GetString());
        }
        finally
        {
            // Clear SQLite connection pool so the file lock is released on Windows before cleanup.
            SqliteConnection.ClearAllPools();
            Directory.Delete(profile, true);
        }
    }

    // Minimal cal store (mirrors DiscoverCalendarsTests.MakeCalStore).
    private static void MakeCalStore(string path, string calId, bool addEvent = true)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        void X(string sql) { using var cmd = conn.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
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
    }
}
