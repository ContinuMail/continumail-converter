// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class SqliteCalendarReaderTests
{
    private static string MakeStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cal-{System.Guid.NewGuid():N}.sqlite");
        using var c = new SqliteConnection($"Data Source={path}"); c.Open();
        void X(string sql){ using var cmd=c.CreateCommand(); cmd.CommandText=sql; cmd.ExecuteNonQuery(); }
        X("CREATE TABLE cal_events (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,event_start INTEGER,event_end INTEGER,event_stamp INTEGER,event_start_tz TEXT,event_end_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_todos (cal_id TEXT,id TEXT,time_created INTEGER,last_modified INTEGER,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,todo_entry INTEGER,todo_due INTEGER,todo_completed INTEGER,todo_complete INTEGER,todo_entry_tz TEXT,todo_due_tz TEXT,todo_completed_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,alarm_last_ack INTEGER,todo_stamp INTEGER,offline_journal INTEGER);");
        X("CREATE TABLE cal_recurrence (item_id TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attendees (item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT,icalString TEXT);");
        X("CREATE TABLE cal_alarms (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_attachments (item_id TEXT,cal_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_relations (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
        X("CREATE TABLE cal_properties (item_id TEXT,key TEXT,value BLOB,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT);");
        X("CREATE TABLE cal_parameters (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,key1 TEXT,key2 TEXT,value TEXT);");
        // CAL / E1: master + override
        X("INSERT INTO cal_events (cal_id,id,title,flags,privacy,event_start,event_end,event_start_tz,recurrence_id) VALUES ('CAL','E1','Standup',0,'PRIVATE',1782810000000000,1782811800000000,'Europe/Copenhagen',NULL);");
        X("INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz,recurrence_id) VALUES ('CAL','E1','Standup moved',0,1782896400000000,1782898200000000,'Europe/Copenhagen',1782896400000000);");
        X("INSERT INTO cal_recurrence (item_id,cal_id,icalString) VALUES ('E1','CAL','RRULE:FREQ=DAILY');");
        X("INSERT INTO cal_attendees (item_id,cal_id,icalString) VALUES ('E1','CAL','ATTENDEE;CN=A:mailto:a@example.com');");
        X("INSERT INTO cal_attendees (item_id,cal_id,icalString) VALUES ('E1','CAL','ATTENDEE;CN=B:mailto:b@example.com');");
        X("INSERT INTO cal_alarms (cal_id,item_id,icalString) VALUES ('CAL','E1','BEGIN:VALARM\nTRIGGER:-PT15M\nEND:VALARM');");
        X("INSERT INTO cal_alarms (cal_id,item_id,icalString) VALUES ('CAL','E1','BEGIN:VALARM\nTRIGGER:-PT5M\nEND:VALARM');");
        X("INSERT INTO cal_properties (item_id,key,value,cal_id) VALUES ('E1','DESCRIPTION',CAST('d' AS BLOB),'CAL');");
        X("INSERT INTO cal_properties (item_id,key,value,cal_id) VALUES ('E1','LOCATION',CAST('room' AS BLOB),'CAL');");
        X("INSERT INTO cal_todos (cal_id,id,title,flags,todo_entry,todo_due,todo_complete,recurrence_id) VALUES ('CAL','T1','Report',0,1782810000000000,1782820000000000,0,NULL);");
        // CAL2 reuses id='E1' with its OWN single attendee (cross-attach guard)
        X("INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_start_tz,recurrence_id) VALUES ('CAL2','E1','Other',0,1782810000000000,'UTC',NULL);");
        X("INSERT INTO cal_attendees (item_id,cal_id,icalString) VALUES ('E1','CAL2','ATTENDEE;CN=Z:mailto:z@example.com');");
        return path;
    }

    [Fact]
    public void Groups_events_and_todos_attaches_side_tables_without_cartesian_or_cross_attach()
    {
        string path = MakeStore();
        try
        {
            CalendarReadResult res = new SqliteCalendarReader().Read(path);
            RawCalendarRead cal = res.Calendars.Single(c => c.CalId == "CAL");

            RawEventGroup grp = Assert.Single(cal.EventGroups);
            Assert.NotNull(grp.Master);
            Assert.Equal("Standup", grp.Master!.Title);
            Assert.Equal("PRIVATE", grp.Master!.Privacy);          // iCal CLASS field round-trips as string
            Assert.Single(grp.Overrides);                          // exactly one override
            Assert.Equal(2, grp.Master.Attendees.Count);           // NOT 4/8 — no cartesian blow-up
            Assert.Equal(2, grp.Master.Alarms.Count);
            Assert.Equal(2, grp.Master.Properties.Count);
            Assert.Equal("RRULE:FREQ=DAILY", Assert.Single(grp.Master.Recurrence).IcalString);

            RawTodoGroup tg = Assert.Single(cal.TodoGroups);
            Assert.Equal("Report", tg.Master!.Title);

            // CAL2's E1 must carry ONLY its own single attendee — no cross-calendar bleed.
            RawEventGroup other = Assert.Single(res.Calendars.Single(c => c.CalId == "CAL2").EventGroups);
            Assert.Single(other.Master!.Attendees);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Properties_stored_as_text_or_integer_are_read_not_dropped()
    {
        // Real Thunderbird stores cal_properties.value as TEXT (DESCRIPTION/LOCATION/CATEGORIES/…)
        // or INTEGER (e.g. PERCENT-COMPLETE), NOT BLOB. A hard (byte[]) cast throws and the
        // whole-table catch drops EVERY property for EVERY event. The reader must handle text/int.
        string path = Path.Combine(Path.GetTempPath(), $"cal-props-{System.Guid.NewGuid():N}.sqlite");
        try
        {
            using (var c = new SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                void X(string sql){ using var cmd=c.CreateCommand(); cmd.CommandText=sql; cmd.ExecuteNonQuery(); }
                X("CREATE TABLE cal_events (cal_id TEXT,id TEXT,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,event_start INTEGER,event_end INTEGER,event_start_tz TEXT,event_end_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,time_created INTEGER,last_modified INTEGER);");
                X("CREATE TABLE cal_todos (cal_id TEXT,id TEXT,title TEXT,priority INTEGER,privacy TEXT,ical_status TEXT,flags INTEGER,todo_entry INTEGER,todo_due INTEGER,todo_completed INTEGER,todo_complete INTEGER,todo_entry_tz TEXT,todo_due_tz TEXT,todo_completed_tz TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,time_created INTEGER,last_modified INTEGER);");
                X("CREATE TABLE cal_recurrence (item_id TEXT,cal_id TEXT,icalString TEXT);");
                X("CREATE TABLE cal_attendees (item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT,icalString TEXT);");
                X("CREATE TABLE cal_alarms (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
                X("CREATE TABLE cal_attachments (item_id TEXT,cal_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
                X("CREATE TABLE cal_relations (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,icalString TEXT);");
                X("CREATE TABLE cal_properties (item_id TEXT,key TEXT,value BLOB,recurrence_id INTEGER,recurrence_id_tz TEXT,cal_id TEXT);");
                X("CREATE TABLE cal_parameters (cal_id TEXT,item_id TEXT,recurrence_id INTEGER,recurrence_id_tz TEXT,key1 TEXT,key2 TEXT,value TEXT);");
                X("INSERT INTO cal_events (cal_id,id,title,flags,event_start,event_end,event_start_tz,recurrence_id) VALUES ('CAL','E1','Ev',0,1782810000000000,1782811800000000,'UTC',NULL);");
                X("INSERT INTO cal_properties (item_id,key,value,cal_id) VALUES ('E1','DESCRIPTION','Test','CAL');"); // TEXT
                X("INSERT INTO cal_properties (item_id,key,value,cal_id) VALUES ('E1','LOCATION','Room 5','CAL');");  // TEXT
                X("INSERT INTO cal_properties (item_id,key,value,cal_id) VALUES ('E1','SEQUENCE',2,'CAL');");         // INTEGER
            }

            var res = new SqliteCalendarReader().Read(path);

            Assert.DoesNotContain(res.Warnings, w => w.Contains("cal_properties"));
            var props = Assert.Single(res.Calendars.Single().EventGroups).Master!.Properties;
            string? Val(string key) => props.Where(p => p.Key == key)
                .Select(p => p.Value is { } b ? System.Text.Encoding.UTF8.GetString(b) : null)
                .FirstOrDefault();
            Assert.Equal("Test",   Val("DESCRIPTION"));
            Assert.Equal("Room 5", Val("LOCATION"));
            Assert.Equal("2",      Val("SEQUENCE"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
