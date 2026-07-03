// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Mail2Pst.Core.Discovery;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class CalendarAccountResolutionTests
{
    private static Account Acct(string id, string email, string host) =>
        new(id, id, id, null, email, host, AddressResolution.NotFound);

    // Minimal calendar SQLite store schema, copied verbatim from DiscoverCalendarsTests.MakeCalStore
    // (do not invent columns — cal_events/cal_todos have specific required columns).
    private static void MakeCalStore(string path, string calId)
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
        X($"INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,recurrence_id) " +
          $"VALUES ('{calId}','ev1','Test Event',0,1782810000000000,1782811800000000,NULL);");
    }

    // Builds a minimal profile dir with a prefs.js registry + a local.sqlite so a calendar is
    // discovered with a known URI.
    private static string WriteProfile(string calId, string uri, string calName)
    {
        string dir = Path.Combine(Path.GetTempPath(), "prof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "calendar-data"));
        File.WriteAllText(Path.Combine(dir, "prefs.js"),
            $"user_pref(\"calendar.registry.{calId}.name\", \"{calName}\");\n" +
            $"user_pref(\"calendar.registry.{calId}.type\", \"caldav\");\n" +
            $"user_pref(\"calendar.registry.{calId}.uri\", \"{uri}\");\n");
        MakeCalStore(Path.Combine(dir, "calendar-data", "local.sqlite"), calId);
        return dir;
    }

    [Fact]
    public void Caldav_calendar_resolves_to_matching_account()
    {
        string dir = WriteProfile("cal1", "https://dav/user%40example.com/", "Work");
        var accounts = new List<Account> { Acct("/p/ImapMail/imap.example.com", "user@example.com", "imap.example.com") };
        var res = MailProfileDiscovery.DiscoverCalendars(dir, accounts);
        var cal = Assert.Single(res.Calendars);
        Assert.Equal("/p/ImapMail/imap.example.com", cal.AccountId);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Local_storage_calendar_resolves_to_null()
    {
        string dir = WriteProfile("cal2", "moz-storage-calendar://", "Home");
        var res = MailProfileDiscovery.DiscoverCalendars(dir, new List<Account>());
        Assert.Null(Assert.Single(res.Calendars).AccountId);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Ambiguous_host_emits_warning_and_null_account()
    {
        string dir = WriteProfile("cal3", "https://caldav.example.com/dav", "Shared");
        var accounts = new List<Account>
        {
            Acct("/p/A", null!, "example.com"),
            Acct("/p/B", null!, "example.com"),
        };
        var res = MailProfileDiscovery.DiscoverCalendars(dir, accounts);
        Assert.Null(Assert.Single(res.Calendars).AccountId);
        Assert.Contains(res.Warnings, w => w.Code == "calendar-ambiguous-account");
        Directory.Delete(dir, true);
    }
}
