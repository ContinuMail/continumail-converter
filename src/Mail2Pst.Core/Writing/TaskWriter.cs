// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;
using PSTFileFormat;

namespace Mail2Pst.Core.Writing;

/// <summary>Writes a <see cref="TaskRecord"/> as an IPM.Task item into an IPF.Task folder.</summary>
public class TaskWriter
{
    public void WriteTask(PSTFile file, PSTFolder folder, TaskRecord t)
    {
        TaskMessage msg = TaskMessage.CreateNewTask(file, folder.NodeID);

        // Defensive invariants — TaskWriter must never emit invalid MAPI even if handed a bad
        // TaskRecord (it is called directly in tests / future pipelines, not only via CalendarTaskMapper).
        int percent = Math.Clamp(t.PercentComplete, 0, 100);
        TaskStatusKind status = t.Status;
        if (percent == 100) status = TaskStatusKind.Complete;
        if (status == TaskStatusKind.Complete && percent < 100) percent = 100;
        bool reminderSet = t.ReminderSet && t.ReminderTime is not null;

        // Body with optional link-only attachment appendix (tasks are plain-text only; no HTML body).
        string? appendix = CalendarBodyAppendix.Format(t.Attachments,
            t.Relations,
            existingText: t.Body);
        string? effectiveBody = appendix is null ? t.Body
            : string.IsNullOrEmpty(t.Body) ? appendix : t.Body + "\n\n" + appendix;

        // Static-tag props
        SetIf(msg, PropertyID.PidTagSubject, t.Subject);
        SetIf(msg, PropertyID.PidTagBody, effectiveBody);
        msg.PC.SetInt32Property(PropertyID.PidTagImportance, t.Importance);
        msg.PC.SetInt32Property(PropertyID.PidTagSensitivity, t.Sensitivity);

        // Private flag (Task 0 batch 2): a Private task sets BOTH PidTagSensitivity=2 AND
        // PidLidPrivate=true (PSETID_Common 0x8506). PidLidPrivate is coupled to Sensitivity==2 only;
        // CONFIDENTIAL (3) intentionally does NOT set it — this matches Outlook ground truth.
        SetNamedBool(file, msg, PropertyLongID.PidLidPrivate, PropertySetGuid.PSETID_Common, t.Sensitivity == 2);

        // Task named props (PSETID_Task unless noted)
        SetNamedInt(file, msg, PropertyLongID.PidLidTaskStatus,    PropertySetGuid.PSETID_Task, (int)status);
        SetNamedDouble(file, msg, PropertyLongID.PidLidPercentComplete, PropertySetGuid.PSETID_Task, percent / 100.0);
        SetNamedBool(file, msg, PropertyLongID.PidLidTaskComplete, PropertySetGuid.PSETID_Task, status == TaskStatusKind.Complete);
        SetNamedInt(file, msg, PropertyLongID.PidLidTaskOwnership, PropertySetGuid.PSETID_Task, 0);  // 0 = not assigned
        SetNamedInt(file, msg, PropertyLongID.PidLidTaskMode,      PropertySetGuid.PSETID_Common, 0); // 0 = not assigned

        // Intentional seam: start/due are written date-only (UTC midnight via SetNamedDateOnly) while
        // reminders (below) are written as absolute instants — this matches Outlook ground truth.
        if (t.StartDate is { } sd)
            SetNamedDateOnly(file, msg, PropertyLongID.PidLidTaskStartDate, PropertySetGuid.PSETID_Task, sd);
        if (t.DueDate is { } dd)
            SetNamedDateOnly(file, msg, PropertyLongID.PidLidTaskDueDate, PropertySetGuid.PSETID_Task, dd);
        if (t.CompletedDate is { } cd)
            SetNamedDate(file, msg, PropertyLongID.PidLidTaskDateCompleted, PropertySetGuid.PSETID_Task, cd); // instant

        // Reminder — tasks use an ABSOLUTE time (Task 0 ground-truth): ReminderTime + ReminderSignalTime,
        // PidLidReminderDelta stays 0. The appointment-style minutes-before delta does NOT apply to tasks.
        // Never write ReminderSet=true without a ReminderTime (the `reminderSet` guard above enforces this).
        SetNamedBool(file, msg, PropertyLongID.PidLidReminderSet, PropertySetGuid.PSETID_Common, reminderSet);
        if (reminderSet && t.ReminderTime is { } rt)
        {
            SetNamedDate(file, msg, PropertyLongID.PidLidReminderTime,       PropertySetGuid.PSETID_Common, rt);
            SetNamedDate(file, msg, PropertyLongID.PidLidReminderSignalTime, PropertySetGuid.PSETID_Common, rt);
            SetNamedInt(file, msg, PropertyLongID.PidLidReminderDelta,       PropertySetGuid.PSETID_Common, 0);
        }

        // Categories (reuse the mail "Keywords" path — PstWriter.cs:603)
        if (t.Categories.Count > 0)
        {
            ushort kw = PropertyNameToIDMap.GetOrCreateStringNamedProperty(file, 2, "Keywords");
            msg.PC.SetMultiStringProperty((PropertyID)kw, t.Categories);
        }

        // Recurrence — only PidLidTaskRecurrence (the bare pattern) + PidLidTaskFRecurring=true.
        // Non-recurring tasks write neither prop (absent is equivalent to FRecurring=false per GT).
        if (t.Recurrence is { } rec)
        {
            var zone = rec.TimeZone ?? TimeZoneInfo.Utc;   // deterministic; mapper passes UTC. NEVER TimeZoneInfo.Local.
            SetNamedBytes(file, msg, PropertyLongID.PidLidTaskRecurrence,  PropertySetGuid.PSETID_Task,
                TaskRecurrenceBlob.Build(rec, zone));
            SetNamedBool(file, msg, PropertyLongID.PidLidTaskFRecurring, PropertySetGuid.PSETID_Task, true);
        }

        WriteAttachments(file, msg, t.Attachments);   // ByValue inline/local-file attachments + no-op for LinkOnly

        msg.SaveChanges();
        folder.AddMessage(msg);
    }

    // -----------------------------------------------------------------------
    // Helpers (mirror ContactWriter pattern)
    // -----------------------------------------------------------------------

    private static void WriteAttachments(PSTFile file, TaskMessage msg, IReadOnlyList<CalendarAttachment> atts)
    {
        var writer = new AttachmentWriter();
        foreach (CalendarAttachment att in atts)
        {
            // No IsInline: task attachments are VISIBLE ByValue attachments, never hidden CID resources.
            if (att.Kind == CalendarAttachmentKind.InlineBytes && att.InlineData is not null)
                writer.Write(file, msg, new AttachmentSpec(att.FileName, att.MimeType,
                    AttachmentContent.FromBytes(att.InlineData)));
            else if (att.Kind == CalendarAttachmentKind.LocalFileByValue && att.LocalPath is not null)
                writer.Write(file, msg, new AttachmentSpec(att.FileName, att.MimeType,
                    AttachmentContent.FromExistingFile(att.LocalPath)));   // NEVER FromTempFile — that deletes the source
            // LinkOnly → body appendix only; no PST attachment row.
        }
    }

    private static void SetIf(TaskMessage msg, PropertyID id, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            msg.PC.SetStringProperty(id, value);
    }

    private static PropertyID Obtain(PSTFile file, PropertyLongID lid, Guid set)
        => file.NameToIDMap.ObtainIDFromName(new PropertyName(lid, set));

    private static void SetNamedInt(PSTFile file, TaskMessage msg, PropertyLongID lid, Guid set, int value)
        => msg.PC.SetInt32Property(Obtain(file, lid, set), value);

    private static void SetNamedBool(PSTFile file, TaskMessage msg, PropertyLongID lid, Guid set, bool value)
        => msg.PC.SetBooleanProperty(Obtain(file, lid, set), value);

    private static void SetNamedBytes(PSTFile file, TaskMessage msg, PropertyLongID lid, Guid set, byte[] value)
        => msg.PC.SetBytesProperty(Obtain(file, lid, set), value);

    private static void SetNamedDate(PSTFile file, TaskMessage msg, PropertyLongID lid, Guid set, DateTimeOffset value)
        => msg.PC.SetDateTimeProperty(Obtain(file, lid, set), value.UtcDateTime);

    // Date-only fields (start/due): UTC midnight of the calendar day.
    // Uses the DateTimeOffset's own Y/M/D so a non-UTC zone (e.g. +07:00) keeps its date.
    private static void SetNamedDateOnly(PSTFile file, TaskMessage msg, PropertyLongID lid, Guid set, DateTimeOffset value)
        => msg.PC.SetDateTimeProperty(Obtain(file, lid, set),
               new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc));

    // PidLidPercentComplete is PtypFloating64 — write as IEEE-754 bytes via SetExternalProperty.
    private static void SetNamedDouble(PSTFile file, TaskMessage msg, PropertyLongID lid, Guid set, double value)
        => msg.PC.SetExternalProperty(Obtain(file, lid, set), PropertyTypeName.PtypFloating64, BitConverter.GetBytes(value));
}
