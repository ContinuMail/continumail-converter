// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Mail2Pst.Core.Storage;

namespace Mail2Pst.Core.Calendar;

/// <summary>
/// Reads one Mozilla calendar SQLite store into <see cref="CalendarReadResult"/>.
/// Side tables are materialized separately (no wide join) to prevent cartesian explosion.
/// Events and todos are grouped by (cal_id, id): recurrence_id NULL → master;
/// non-null → override list. Per-item failures are caught and appended as Warnings.
/// </summary>
public sealed class SqliteCalendarReader : ICalendarReader
{
    public CalendarReadResult Read(string storePath)
    {
        var outp = new CalendarReadResult();
        using SqliteSnapshot snap = SqliteSnapshot.Open(storePath);
        SqliteConnection c = snap.Connection;

        // Materialize each side table into a keyed lookup before touching events/todos —
        // this avoids wide joins that would cartesian-multiply multi-attendee + multi-alarm rows.
        // cal_recurrence has no recurrence_id columns; force null rid/tz so rows attach to the master.
        var attendees   = LoadSide(c, "cal_attendees",   hasRid: true,  outp.Warnings);
        var alarms      = LoadSide(c, "cal_alarms",      hasRid: true,  outp.Warnings);
        var attachments = LoadSide(c, "cal_attachments", hasRid: true,  outp.Warnings);
        var relations   = LoadSide(c, "cal_relations",   hasRid: true,  outp.Warnings);
        var recurrence  = LoadSide(c, "cal_recurrence",  hasRid: false, outp.Warnings);
        var props       = LoadProps(c, outp.Warnings);
        var parms       = LoadParams(c, outp.Warnings);

        // Lazy calendar-bucket factory.
        var byCal = new Dictionary<string, RawCalendarRead>(StringComparer.Ordinal);
        RawCalendarRead Cal(string id)
        {
            if (!byCal.TryGetValue(id, out var r))
                byCal[id] = r = new RawCalendarRead { CalId = id };
            return r;
        }

        // Retrieve a pre-materialized list or return an empty list (never null).
        static List<T> Get<T>(Dictionary<CalendarItemKey, List<T>> dict, CalendarItemKey key)
            => dict.TryGetValue(key, out var v) ? v : new List<T>();

        // ---- Events ----------------------------------------------------------------
        var eventGroups = new Dictionary<(string calId, string id), RawEventGroup>();
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                "SELECT cal_id, id, title, priority, privacy, ical_status, flags, " +
                "event_start, event_end, event_start_tz, event_end_tz, " +
                "recurrence_id, recurrence_id_tz, time_created, last_modified " +
                "FROM cal_events";
            using var rdr = cmd.ExecuteReader();
            int oCalId    = rdr.GetOrdinal("cal_id");
            int oId       = rdr.GetOrdinal("id");
            int oTitle    = rdr.GetOrdinal("title");
            int oPriority = rdr.GetOrdinal("priority");
            int oPrivacy  = rdr.GetOrdinal("privacy");
            int oStatus   = rdr.GetOrdinal("ical_status");
            int oFlags    = rdr.GetOrdinal("flags");
            int oStart    = rdr.GetOrdinal("event_start");
            int oEnd      = rdr.GetOrdinal("event_end");
            int oStartTz  = rdr.GetOrdinal("event_start_tz");
            int oEndTz    = rdr.GetOrdinal("event_end_tz");
            int oRid      = rdr.GetOrdinal("recurrence_id");
            int oRidTz    = rdr.GetOrdinal("recurrence_id_tz");
            int oCreated  = rdr.GetOrdinal("time_created");
            int oModified = rdr.GetOrdinal("last_modified");

            while (rdr.Read())
            {
                string? calId = rdr.IsDBNull(oCalId) ? null : rdr.GetString(oCalId);
                string? id    = rdr.IsDBNull(oId)    ? null : rdr.GetString(oId);
                if (calId is null || id is null)
                {
                    outp.Warnings.Add("event row skipped: null cal_id or id");
                    continue;
                }
                try
                {
                    long?   rid   = rdr.IsDBNull(oRid)   ? null : rdr.GetInt64(oRid);
                    string? ridTz = rdr.IsDBNull(oRidTz) ? null : rdr.GetString(oRidTz);

                    var itemKey   = new CalendarItemKey(calId, id, rid, ridTz);
                    var masterKey = new CalendarItemKey(calId, id, null, null);

                    var ev = new RawEvent
                    {
                        CalId          = calId,
                        Id             = id,
                        Title          = rdr.IsDBNull(oTitle)    ? null : rdr.GetString(oTitle),
                        Priority       = rdr.IsDBNull(oPriority) ? null : rdr.GetInt32(oPriority),
                        Privacy        = rdr.IsDBNull(oPrivacy)  ? null : rdr.GetString(oPrivacy),
                        IcalStatus     = rdr.IsDBNull(oStatus)   ? null : rdr.GetString(oStatus),
                        Flags          = rdr.IsDBNull(oFlags)    ? null : rdr.GetInt32(oFlags),
                        EventStart     = rdr.IsDBNull(oStart)    ? null : rdr.GetInt64(oStart),
                        EventEnd       = rdr.IsDBNull(oEnd)      ? null : rdr.GetInt64(oEnd),
                        EventStartTz   = rdr.IsDBNull(oStartTz)  ? null : rdr.GetString(oStartTz),
                        EventEndTz     = rdr.IsDBNull(oEndTz)    ? null : rdr.GetString(oEndTz),
                        RecurrenceId   = rid,
                        RecurrenceIdTz = ridTz,
                        TimeCreated    = rdr.IsDBNull(oCreated)  ? null : rdr.GetInt64(oCreated),
                        LastModified   = rdr.IsDBNull(oModified) ? null : rdr.GetInt64(oModified),
                        // Side collections — keyed by this row's own (cal_id,id,rid,ridTz).
                        // Recurrence lines have no rid in the DB; they land on the null-rid key → master only.
                        Attendees      = Get(attendees,   itemKey),
                        Alarms         = Get(alarms,      itemKey),
                        Attachments    = Get(attachments, itemKey),
                        Relations      = Get(relations,   itemKey),
                        Properties     = Get(props,       itemKey),
                        Parameters     = Get(parms,       itemKey),
                        Recurrence     = rid is null ? Get(recurrence, masterKey) : new List<RawSideText>(),
                    };

                    var groupKey = (calId, id);
                    if (!eventGroups.TryGetValue(groupKey, out var grp))
                        eventGroups[groupKey] = grp = new RawEventGroup();
                    if (rid is null) grp.Master = ev;
                    else             grp.Overrides.Add(ev);
                }
                catch (Exception ex)
                {
                    outp.Warnings.Add($"event {id} (cal {calId}) skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            outp.Warnings.Add($"cal_events could not be read: {ex.Message}");
        }

        foreach (var (key, grp) in eventGroups)
            Cal(key.calId).EventGroups.Add(grp);

        // ---- Todos -----------------------------------------------------------------
        var todoGroups = new Dictionary<(string calId, string id), RawTodoGroup>();
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                "SELECT cal_id, id, title, priority, privacy, ical_status, flags, " +
                "todo_entry, todo_due, todo_completed, todo_complete, " +
                "todo_entry_tz, todo_due_tz, todo_completed_tz, " +
                "recurrence_id, recurrence_id_tz, time_created, last_modified " +
                "FROM cal_todos";
            using var rdr = cmd.ExecuteReader();
            int oCalId       = rdr.GetOrdinal("cal_id");
            int oId          = rdr.GetOrdinal("id");
            int oTitle       = rdr.GetOrdinal("title");
            int oPriority    = rdr.GetOrdinal("priority");
            int oPrivacy     = rdr.GetOrdinal("privacy");
            int oStatus      = rdr.GetOrdinal("ical_status");
            int oFlags       = rdr.GetOrdinal("flags");
            int oEntry       = rdr.GetOrdinal("todo_entry");
            int oDue         = rdr.GetOrdinal("todo_due");
            int oCompleted   = rdr.GetOrdinal("todo_completed");
            int oComplete    = rdr.GetOrdinal("todo_complete");
            int oEntryTz     = rdr.GetOrdinal("todo_entry_tz");
            int oDueTz       = rdr.GetOrdinal("todo_due_tz");
            int oCompletedTz = rdr.GetOrdinal("todo_completed_tz");
            int oRid         = rdr.GetOrdinal("recurrence_id");
            int oRidTz       = rdr.GetOrdinal("recurrence_id_tz");
            int oCreated     = rdr.GetOrdinal("time_created");
            int oModified    = rdr.GetOrdinal("last_modified");

            while (rdr.Read())
            {
                string? calId = rdr.IsDBNull(oCalId) ? null : rdr.GetString(oCalId);
                string? id    = rdr.IsDBNull(oId)    ? null : rdr.GetString(oId);
                if (calId is null || id is null)
                {
                    outp.Warnings.Add("todo row skipped: null cal_id or id");
                    continue;
                }
                try
                {
                    long?   rid   = rdr.IsDBNull(oRid)   ? null : rdr.GetInt64(oRid);
                    string? ridTz = rdr.IsDBNull(oRidTz) ? null : rdr.GetString(oRidTz);

                    var itemKey   = new CalendarItemKey(calId, id, rid, ridTz);
                    var masterKey = new CalendarItemKey(calId, id, null, null);

                    var todo = new RawTodo
                    {
                        CalId           = calId,
                        Id              = id,
                        Title           = rdr.IsDBNull(oTitle)       ? null : rdr.GetString(oTitle),
                        Priority        = rdr.IsDBNull(oPriority)    ? null : rdr.GetInt32(oPriority),
                        Privacy         = rdr.IsDBNull(oPrivacy)     ? null : rdr.GetString(oPrivacy),
                        IcalStatus      = rdr.IsDBNull(oStatus)      ? null : rdr.GetString(oStatus),
                        Flags           = rdr.IsDBNull(oFlags)       ? null : rdr.GetInt32(oFlags),
                        TodoEntry       = rdr.IsDBNull(oEntry)       ? null : rdr.GetInt64(oEntry),
                        TodoDue         = rdr.IsDBNull(oDue)         ? null : rdr.GetInt64(oDue),
                        TodoCompleted   = rdr.IsDBNull(oCompleted)   ? null : rdr.GetInt64(oCompleted),
                        TodoComplete    = rdr.IsDBNull(oComplete)    ? null : rdr.GetInt32(oComplete),
                        TodoEntryTz     = rdr.IsDBNull(oEntryTz)    ? null : rdr.GetString(oEntryTz),
                        TodoDueTz       = rdr.IsDBNull(oDueTz)      ? null : rdr.GetString(oDueTz),
                        TodoCompletedTz = rdr.IsDBNull(oCompletedTz)? null : rdr.GetString(oCompletedTz),
                        RecurrenceId    = rid,
                        RecurrenceIdTz  = ridTz,
                        TimeCreated     = rdr.IsDBNull(oCreated)    ? null : rdr.GetInt64(oCreated),
                        LastModified    = rdr.IsDBNull(oModified)   ? null : rdr.GetInt64(oModified),
                        Attendees       = Get(attendees,   itemKey),
                        Alarms          = Get(alarms,      itemKey),
                        Attachments     = Get(attachments, itemKey),
                        Relations       = Get(relations,   itemKey),
                        Properties      = Get(props,       itemKey),
                        Parameters      = Get(parms,       itemKey),
                        Recurrence      = rid is null ? Get(recurrence, masterKey) : new List<RawSideText>(),
                    };

                    var groupKey = (calId, id);
                    if (!todoGroups.TryGetValue(groupKey, out var grp))
                        todoGroups[groupKey] = grp = new RawTodoGroup();
                    if (rid is null) grp.Master = todo;
                    else             grp.Overrides.Add(todo);
                }
                catch (Exception ex)
                {
                    outp.Warnings.Add($"todo {id} (cal {calId}) skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            outp.Warnings.Add($"cal_todos could not be read: {ex.Message}");
        }

        foreach (var (key, grp) in todoGroups)
            Cal(key.calId).TodoGroups.Add(grp);

        outp.Calendars.AddRange(byCal.Values);
        return outp;
    }

    // Loads a side table that carries cal_id + item_id + icalString [+ recurrence_id + recurrence_id_tz].
    // hasRid=false (cal_recurrence): forces null rid/tz so rows key to the master's null-rid slot.
    // A SELECT failure (missing table) produces an empty lookup + a warning rather than a throw.
    private static Dictionary<CalendarItemKey, List<RawSideText>> LoadSide(
        SqliteConnection c, string table, bool hasRid, List<string> warnings)
    {
        var dict = new Dictionary<CalendarItemKey, List<RawSideText>>();
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {table}";
            using var rdr = cmd.ExecuteReader();
            int oCalId  = rdr.GetOrdinal("cal_id");
            int oItemId = rdr.GetOrdinal("item_id");
            int oIcal   = rdr.GetOrdinal("icalString");
            int oRid    = hasRid ? rdr.GetOrdinal("recurrence_id")    : -1;
            int oRidTz  = hasRid ? rdr.GetOrdinal("recurrence_id_tz") : -1;

            while (rdr.Read())
            {
                string? calId  = rdr.IsDBNull(oCalId)  ? null : rdr.GetString(oCalId);
                string  itemId = rdr.IsDBNull(oItemId) ? ""   : rdr.GetString(oItemId);
                string? ical   = rdr.IsDBNull(oIcal)   ? null : rdr.GetString(oIcal);
                long?   rid    = hasRid && !rdr.IsDBNull(oRid)   ? rdr.GetInt64(oRid)    : null;
                string? ridTz  = hasRid && !rdr.IsDBNull(oRidTz) ? rdr.GetString(oRidTz) : null;

                var key = new CalendarItemKey(calId, itemId, rid, ridTz);
                if (!dict.TryGetValue(key, out var list))
                    dict[key] = list = new List<RawSideText>();
                list.Add(new RawSideText(ical));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"{table} could not be read: {ex.Message}");
        }
        return dict;
    }

    // Normalises a cal_properties.value cell (BLOB / TEXT / INTEGER / REAL) to UTF-8 bytes so
    // downstream consumers can decode it as a string regardless of the stored SQLite type.
    private static byte[] ToValueBytes(object raw) => raw switch
    {
        byte[] b => b,
        string s => Encoding.UTF8.GetBytes(s),
        _        => Encoding.UTF8.GetBytes(Convert.ToString(raw, CultureInfo.InvariantCulture) ?? ""),
    };

    // Loads cal_properties; value column is BLOB, returned as byte[].
    private static Dictionary<CalendarItemKey, List<RawProperty>> LoadProps(
        SqliteConnection c, List<string> warnings)
    {
        var dict = new Dictionary<CalendarItemKey, List<RawProperty>>();
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM cal_properties";
            using var rdr = cmd.ExecuteReader();
            int oCalId  = rdr.GetOrdinal("cal_id");
            int oItemId = rdr.GetOrdinal("item_id");
            int oKey    = rdr.GetOrdinal("key");
            int oValue  = rdr.GetOrdinal("value");
            int oRid    = rdr.GetOrdinal("recurrence_id");
            int oRidTz  = rdr.GetOrdinal("recurrence_id_tz");

            while (rdr.Read())
            {
                string? calId  = rdr.IsDBNull(oCalId)  ? null : rdr.GetString(oCalId);
                string  itemId = rdr.IsDBNull(oItemId) ? ""   : rdr.GetString(oItemId);
                string  key    = rdr.IsDBNull(oKey)    ? ""   : rdr.GetString(oKey);
                // cal_properties.value has BLOB column affinity, but SQLite's dynamic typing means
                // Thunderbird stores most values as TEXT (DESCRIPTION/LOCATION/CATEGORIES/…) and some
                // as INTEGER (e.g. PERCENT-COMPLETE). A hard (byte[]) cast throws on those, and the
                // whole-table catch would drop EVERY property. Normalise any type to UTF-8 bytes.
                byte[]? value  = rdr.IsDBNull(oValue) ? null : ToValueBytes(rdr.GetValue(oValue));
                long?   rid    = rdr.IsDBNull(oRid)    ? null : rdr.GetInt64(oRid);
                string? ridTz  = rdr.IsDBNull(oRidTz)  ? null : rdr.GetString(oRidTz);

                var itemKey = new CalendarItemKey(calId, itemId, rid, ridTz);
                if (!dict.TryGetValue(itemKey, out var list))
                    dict[itemKey] = list = new List<RawProperty>();
                list.Add(new RawProperty(key, value, rid, ridTz));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"cal_properties could not be read: {ex.Message}");
        }
        return dict;
    }

    // Loads cal_parameters; key1/key2 are the compound parameter name parts.
    private static Dictionary<CalendarItemKey, List<RawParameter>> LoadParams(
        SqliteConnection c, List<string> warnings)
    {
        var dict = new Dictionary<CalendarItemKey, List<RawParameter>>();
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM cal_parameters";
            using var rdr = cmd.ExecuteReader();
            int oCalId  = rdr.GetOrdinal("cal_id");
            int oItemId = rdr.GetOrdinal("item_id");
            int oKey1   = rdr.GetOrdinal("key1");
            int oKey2   = rdr.GetOrdinal("key2");
            int oValue  = rdr.GetOrdinal("value");
            int oRid    = rdr.GetOrdinal("recurrence_id");
            int oRidTz  = rdr.GetOrdinal("recurrence_id_tz");

            while (rdr.Read())
            {
                string? calId  = rdr.IsDBNull(oCalId)  ? null : rdr.GetString(oCalId);
                string  itemId = rdr.IsDBNull(oItemId) ? ""   : rdr.GetString(oItemId);
                string  key1   = rdr.IsDBNull(oKey1)   ? ""   : rdr.GetString(oKey1);
                string  key2   = rdr.IsDBNull(oKey2)   ? ""   : rdr.GetString(oKey2);
                string? value  = rdr.IsDBNull(oValue)  ? null : rdr.GetString(oValue);
                long?   rid    = rdr.IsDBNull(oRid)    ? null : rdr.GetInt64(oRid);
                string? ridTz  = rdr.IsDBNull(oRidTz)  ? null : rdr.GetString(oRidTz);

                var itemKey = new CalendarItemKey(calId, itemId, rid, ridTz);
                if (!dict.TryGetValue(itemKey, out var list))
                    dict[itemKey] = list = new List<RawParameter>();
                list.Add(new RawParameter(key1, key2, value, rid, ridTz));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"cal_parameters could not be read: {ex.Message}");
        }
        return dict;
    }
}
