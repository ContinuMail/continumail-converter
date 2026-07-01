// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
namespace Mail2Pst.Core.Models;

public sealed class AppointmentRecord
{
    public string Subject { get; set; } = "";
    public string? Body { get; set; }                 // plain text (DESCRIPTION)
    public string? BodyHtml { get; set; }             // ALTREP HTML alternate, if present

    public DateTime StartUtc { get; set; }            // event_start as UTC instant
    public DateTime EndUtc { get; set; }              // event_end as UTC instant
    public bool IsAllDay { get; set; }                // flags & 4
    public TimeZoneInfo? TimeZone { get; set; }       // resolved display zone (null = floating/all-day)

    public string? Location { get; set; }
    public int BusyStatus { get; set; } = 2;          // 0=Free 1=Tentative 2=Busy 3=OOF (vendor BusyStatus enum)
    public int Importance { get; set; } = 1;          // 0/1/2
    public int Sensitivity { get; set; }              // 0 normal / 2 private / 3 confidential
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    public bool ReminderSet { get; set; }
    public int ReminderMinutesBefore { get; set; }    // PidLidReminderDelta (appointments use a delta)

    public string SourceId { get; set; } = "";        // cal_events.id, for skip/warning messages

    public AppointmentAttendee? Organizer { get; set; }
    public IReadOnlyList<AppointmentAttendee> Attendees { get; set; } = Array.Empty<AppointmentAttendee>();

    public string? OriginatingTimeZoneId { get; set; }   // canonical IANA/Olson id (event_start_tz)
    public RecurrenceSpec? Recurrence { get; set; }
    public IReadOnlyList<RecurrenceInstanceId> DeletedOccurrences { get; set; } = Array.Empty<RecurrenceInstanceId>();
    public IReadOnlyList<AppointmentException> Exceptions { get; set; } = Array.Empty<AppointmentException>();

    public IReadOnlyList<CalendarAttachment> Attachments { get; set; } = Array.Empty<CalendarAttachment>();
    public IReadOnlyList<string> Relations { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Raw 56-byte GlobalObjectId blob, hex-decoded from an Exchange-cached event id.
    /// Null when the source id is a Mozilla UUID, CalDAV UID, or otherwise not a GOID
    /// (Outlook generates a GOID on demand for non-Exchange items).
    /// </summary>
    public byte[]? GlobalObjectId { get; set; }
}
