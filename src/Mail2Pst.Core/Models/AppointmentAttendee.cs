// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Mail2Pst.Core.Models;

// Recipient type for the MAPI recipient row: Required→To, Optional→Cc, Resource→Bcc.
public enum AttendeeKind { Required, Optional, Resource }

// Canonical MS-OXOCAL response/track-status values (used directly as the property int).
public enum AttendeeResponse { None = 0, Organized = 1, Tentative = 2, Accepted = 3, Declined = 4, NotResponded = 5 }

public sealed class AppointmentAttendee
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public AttendeeKind Kind { get; set; } = AttendeeKind.Required;
    public AttendeeResponse Response { get; set; } = AttendeeResponse.None;
    public bool IsOrganizer { get; set; }
}
