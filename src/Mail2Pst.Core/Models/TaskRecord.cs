// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
namespace Mail2Pst.Core.Models;

public enum TaskStatusKind
{
    NotStarted = 0,   // PidLidTaskStatus 0
    InProgress = 1,   // 1
    Complete   = 2,   // 2
    Waiting    = 3,   // 3 (waiting on someone else)
    Deferred   = 4,   // 4
}

public sealed class TaskRecord
{
    public string Subject { get; set; } = "";
    public string? Body { get; set; }                     // plain text from DESCRIPTION
    public TaskStatusKind Status { get; set; } = TaskStatusKind.NotStarted;
    public int PercentComplete { get; set; }              // 0..100
    public DateTimeOffset? StartDate { get; set; }        // todo_entry
    public DateTimeOffset? DueDate { get; set; }          // todo_due
    public DateTimeOffset? CompletedDate { get; set; }    // todo_completed
    public int Importance { get; set; } = 1;              // PidTagImportance 0=low 1=normal 2=high
    public int Sensitivity { get; set; }                  // PidTagSensitivity 0=normal 2=private 3=confidential
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    // Reminder — ABSOLUTE for tasks (Task 0 ground-truth: PidLidReminderTime + PidLidReminderSignalTime,
    // PidLidReminderDelta stays 0; the appointment-style minutes-before delta does NOT apply to tasks).
    public bool ReminderSet { get; set; }
    public DateTimeOffset? ReminderTime { get; set; }     // absolute reminder instant

    public string SourceId { get; set; } = "";            // cal_todos.id, for skip/warning messages

    // Recurrence — null for non-recurring tasks.
    public RecurrenceSpec? Recurrence { get; set; }

    public IReadOnlyList<CalendarAttachment> Attachments { get; set; } = Array.Empty<CalendarAttachment>();
    public IReadOnlyList<string> Relations { get; set; } = Array.Empty<string>();
}
