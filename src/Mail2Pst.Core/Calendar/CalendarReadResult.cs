// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Calendar;

// ---------------------------------------------------------------------------
// Typed side-table key — uniquely identifies an event/todo row across tables.
// ---------------------------------------------------------------------------

public readonly record struct CalendarItemKey(
    string? CalId,
    string ItemId,
    long? RecurrenceId,
    string? RecurrenceIdTz);

// ---------------------------------------------------------------------------
// PRTime helper — Thunderbird stores times as microseconds since Unix epoch.
// The raw model keeps long? micros; this helper is for mapping/tests only.
// ---------------------------------------------------------------------------

public static class PrTime
{
    /// <summary>Thunderbird PRTime = microseconds since the Unix epoch (UTC). Raw model keeps the
    /// long? micros; this helper is for later mapping/tests. Null -> null.</summary>
    public static System.DateTimeOffset? FromMicros(long? micros) =>
        micros is null ? null : System.DateTimeOffset.FromUnixTimeMilliseconds(micros.Value / 1000L);
}

// ---------------------------------------------------------------------------
// Side-table raw carriers (dumb data, no logic).
// ---------------------------------------------------------------------------

public sealed record RawSideText(string? IcalString);

public sealed record RawProperty(
    string Key,
    byte[]? Value,
    long? RecurrenceId,
    string? RecurrenceIdTz);

public sealed record RawParameter(
    string Key1,
    string Key2,
    string? Value,
    long? RecurrenceId,
    string? RecurrenceIdTz);

// ---------------------------------------------------------------------------
// Raw event — maps 1:1 to cal_events columns + side collections.
// ---------------------------------------------------------------------------

public sealed class RawEvent
{
    // Core identity / scheduling
    public string? CalId { get; set; }
    public string? Id { get; set; }
    public long? EventStart { get; set; }
    public string? EventStartTz { get; set; }
    public long? EventEnd { get; set; }
    public string? EventEndTz { get; set; }
    public string? Title { get; set; }
    public int? Flags { get; set; }
    public int? EventStatus { get; set; }
    public long? RecurrenceId { get; set; }
    public string? RecurrenceIdTz { get; set; }
    public long? TimeCreated { get; set; }
    public long? LastModified { get; set; }
    public string? Alarm { get; set; }
    public string? Organizer { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Categories { get; set; }
    public string? Url { get; set; }
    public int? Priority { get; set; }
    public int? Privacy { get; set; }
    public int? Sequence { get; set; }
    public string? IcalStatus { get; set; }

    // Side collections (default to empty list)
    public List<RawSideText> Recurrence { get; set; } = new();
    public List<RawSideText> Attendees { get; set; } = new();
    public List<RawSideText> Alarms { get; set; } = new();
    public List<RawSideText> Attachments { get; set; } = new();
    public List<RawSideText> Relations { get; set; } = new();
    public List<RawProperty> Properties { get; set; } = new();
    public List<RawParameter> Parameters { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Raw todo — maps 1:1 to cal_todos columns + side collections.
// ---------------------------------------------------------------------------

public sealed class RawTodo
{
    // Core identity
    public string? CalId { get; set; }
    public string? Id { get; set; }
    public string? Title { get; set; }
    public int? Priority { get; set; }
    public int? Privacy { get; set; }
    public string? IcalStatus { get; set; }
    public int? Flags { get; set; }
    public long? RecurrenceId { get; set; }
    public string? RecurrenceIdTz { get; set; }
    public long? TimeCreated { get; set; }
    public long? LastModified { get; set; }

    // Todo-specific date columns
    public long? TodoEntry { get; set; }
    public string? TodoEntryTz { get; set; }
    public long? TodoDue { get; set; }
    public string? TodoDueTz { get; set; }
    public long? TodoCompleted { get; set; }
    public string? TodoCompletedTz { get; set; }
    public int? TodoComplete { get; set; }

    // Side collections (default to empty list)
    public List<RawSideText> Recurrence { get; set; } = new();
    public List<RawSideText> Attendees { get; set; } = new();
    public List<RawSideText> Alarms { get; set; } = new();
    public List<RawSideText> Attachments { get; set; } = new();
    public List<RawSideText> Relations { get; set; } = new();
    public List<RawProperty> Properties { get; set; } = new();
    public List<RawParameter> Parameters { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Grouping wrappers — one master occurrence + zero-or-more overrides.
// ---------------------------------------------------------------------------

public sealed class RawEventGroup
{
    public RawEvent? Master { get; set; }
    public List<RawEvent> Overrides { get; set; } = new();
}

public sealed class RawTodoGroup
{
    public RawTodo? Master { get; set; }
    public List<RawTodo> Overrides { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Per-calendar read result, then the top-level return type.
// ---------------------------------------------------------------------------

public sealed class RawCalendarRead
{
    public string CalId { get; set; } = "";
    public List<RawEventGroup> EventGroups { get; set; } = new();
    public List<RawTodoGroup> TodoGroups { get; set; } = new();
}

public sealed class CalendarReadResult
{
    public List<RawCalendarRead> Calendars { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
