// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Config;

public class CalendarSourceConfig
{
    /// <summary>Path to the calendar SQLite store (local.sqlite or cache.sqlite).</summary>
    public string StorePath { get; set; } = "";

    /// <summary>
    /// Registry GUID identifying one calendar within the store.
    /// Selects rows for that calendar only. Empty string means all calendars in the store,
    /// but ConfigValidator rejects that combination when TaskFolderPath is also unset.
    /// </summary>
    public string CalId { get; set; } = "";

    /// <summary>
    /// Whether to import calendar appointments. Defaults false.
    /// PR4 rejects true — appointment writing is wired in PR5.
    /// </summary>
    public bool IncludeAppointments { get; set; } = false;

    /// <summary>Whether to import tasks. Defaults true.</summary>
    public bool IncludeTasks { get; set; } = true;

    /// <summary>Inert in PR4. Reserved for the appointment folder path (PR5).</summary>
    public IReadOnlyList<string>? AppointmentFolderPath { get; set; }

    /// <summary>
    /// Explicit PST folder path for tasks.
    /// Null → MappingEngine defaults to ["Tasks", CalId].
    /// Discovery always sets this to a human-readable display name;
    /// the CalId fallback applies only to hand-authored configs.
    /// </summary>
    public IReadOnlyList<string>? TaskFolderPath { get; set; }
}
