// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
namespace Mail2Pst.Core.Models;

public enum RecurrenceFrequency { Daily, Weekly, Monthly, MonthlyNth, Yearly, YearlyNth }
public enum RecurrenceEndKind { NoEnd, Count, Until }

/// <summary>Original-occurrence identity. TimeZoneId is the canonical IANA/Olson id (or a tzone:// Windows id) verbatim from TB.</summary>
public sealed record RecurrenceInstanceId(
    DateTime OriginalStartUtc, DateTime OriginalStartLocal, string? TimeZoneId, bool IsDateOnly);

[Flags]
public enum AppointmentExceptionChangeFlags
{
    None = 0, Subject = 1, Location = 2, Body = 4, StartEnd = 8,
    BusyStatus = 16, Reminder = 32, Sensitivity = 64, Categories = 128
}

public sealed class RecurrenceSpec
{
    public RecurrenceFrequency Frequency { get; set; }
    public int Interval { get; set; } = 1;     // in RecurrenceType natural units (years for yearly)
    public DayOfWeek[] DaysOfWeek { get; set; } = Array.Empty<DayOfWeek>();
    public int? DayOfMonth { get; set; }
    public int? NthOccurrence { get; set; }     // 1..4, -1 = last
    public int? Month { get; set; }
    public RecurrenceEndKind EndKind { get; set; }
    public int? Count { get; set; }
    public DateTime? UntilUtc { get; set; }
    public DateTime FirstStartUtc { get; set; }
    public DateTime FirstStartLocal { get; set; }
    public TimeZoneInfo? TimeZone { get; set; }        // for offset math only
    public string? OriginatingTimeZoneId { get; set; } // canonical IANA/Olson id from event_start_tz
    /// <summary>UTC start of the LAST occurrence (COUNT-th, or final occ ≤ UNTIL), computed by enumeration. Null only for NoEnd.</summary>
    public DateTime? LastInstanceStartUtc { get; set; }
    public string RawRRuleBody { get; set; } = "";     // RRULE body WITHOUT the "RRULE:" prefix
}

public sealed class AppointmentException
{
    public required RecurrenceInstanceId OriginalInstance { get; set; }
    public DateTime? NewStartUtc { get; set; }
    public DateTime? NewEndUtc { get; set; }
    public string? Subject { get; set; }
    public string? Location { get; set; }
    public string? Body { get; set; }
    public int? BusyStatus { get; set; }
    public bool? ReminderSet { get; set; }
    public int? ReminderMinutesBefore { get; set; }
    public AppointmentExceptionChangeFlags ChangeFlags { get; set; }
}
