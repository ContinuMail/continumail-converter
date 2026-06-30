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
}
