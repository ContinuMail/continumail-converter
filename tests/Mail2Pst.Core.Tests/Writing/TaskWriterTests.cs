// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class TaskWriterTests
{
    // ---------------------------------------------------------------------------
    // Round-trip infrastructure (mirrors ContactWriterTests / TaskMessageFactoryTests)
    // ---------------------------------------------------------------------------

    private static MessageObject RoundTripTask(TaskRecord record)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-tw-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Tasks", FolderItemTypeName.Task);
                new TaskWriter().WriteTask(pst, folder, record);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            // Re-open read-only, load the first task, detach from file lifecycle.
            // We close and delete the file in the outer finally, but MessageObject references
            // remain valid after CloseFile (data is in-memory). Store the PSTFile reference
            // so named-property lookups can use the same NameToIDMap that was written.
            pst = new PSTFile(path, FileAccess.Read);
            PSTFolder readFolder = pst.TopOfPersonalFolders.FindChildFolder("Tasks");
            MessageObject msg = readFolder.GetMessage(0);
            // Reload via TaskMessage to ensure full PC hydration (mirrors FirstTask helper).
            return TaskMessage.GetTask(pst, msg.NodeID);
        }
        catch
        {
            pst?.CloseFile();
            File.Delete(path);
            throw;
        }
        // NOTE: pst is intentionally left open; we close + delete inside each test via the
        // wrapper below that owns the lifecycle. Tests that need pst for named-prop lookups
        // use the overload below.
    }

    /// <summary>
    /// Full round-trip: write, close, reopen; expose both the open PSTFile (for named-prop
    /// ID lookups) and the first MessageObject, then invoke <paramref name="read"/>, close,
    /// delete. This avoids leaking file handles even when assertions throw.
    /// </summary>
    private static T RoundTripTask<T>(TaskRecord record, Func<PSTFile, MessageObject, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-tw-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Tasks", FolderItemTypeName.Task);
                new TaskWriter().WriteTask(pst, folder, record);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read);
                PSTFolder readFolder = pst.TopOfPersonalFolders.FindChildFolder("Tasks");
                MessageObject msg = TaskMessage.GetTask(pst, readFolder.GetMessage(0).NodeID);
                return read(pst, msg);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void WriteTask_message_class_is_IPM_Task()
    {
        // Guards against accidentally writing a Note into a Task folder.
        var t = new TaskRecord { Subject = "Class check" };
        string cls = RoundTripTask(t, (_, msg) =>
            msg.PC.GetStringProperty(PropertyID.PidTagMessageClass));
        Assert.Equal("IPM.Task", cls);
    }

    [Fact]
    public void WriteTask_round_trips_core_fields()
    {
        var t = new TaskRecord
        {
            Subject     = "Prepare Q3 report",
            Body        = "Draft and circulate",
            Status      = TaskStatusKind.InProgress,
            PercentComplete = 50,
            StartDate   = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            DueDate     = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
            Importance  = 2,
            Categories  = new[] { "Work", "Finance" },
        };

        RoundTripTask(t, (pst, msg) =>
        {
            Assert.Equal("Prepare Q3 report", msg.PC.GetStringProperty(PropertyID.PidTagSubject));
            Assert.Equal("Draft and circulate", msg.PC.GetStringProperty(PropertyID.PidTagBody));
            Assert.Equal(2, msg.PC.GetInt32Property(PropertyID.PidTagImportance));

            // Status (PidLidTaskStatus) named-prop read-back
            PropertyID statusId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskStatus, PropertySetGuid.PSETID_Task));
            Assert.Equal(1, msg.PC.GetInt32Property(statusId));

            return true;
        });
    }

    [Fact]
    public void WriteTask_percent_round_trips_as_floating64()
    {
        // PidLidPercentComplete is PtypFloating64; SetExternalProperty writes IEEE-754 bytes.
        // Read back via GetFloat64Property (NOT GetBytesProperty — that checks PtypBinary).
        var t = new TaskRecord { Subject = "Half done", PercentComplete = 50 };
        RoundTripTask(t, (pst, msg) =>
        {
            PropertyID pcId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidPercentComplete, PropertySetGuid.PSETID_Task));
            double? value = msg.PC.GetFloat64Property(pcId);
            Assert.NotNull(value);
            Assert.Equal(0.5, value!.Value, 3);
            return true;
        });
    }

    [Fact]
    public void WriteTask_completed_sets_complete_flag_and_100_percent()
    {
        var completedAt = new DateTimeOffset(2026, 6, 25, 14, 30, 0, TimeSpan.Zero);
        var t = new TaskRecord
        {
            Subject         = "All done",
            Status          = TaskStatusKind.Complete,
            PercentComplete = 100,
            CompletedDate   = completedAt,
        };

        RoundTripTask(t, (pst, msg) =>
        {
            // PidLidTaskStatus == 2
            PropertyID statusId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskStatus, PropertySetGuid.PSETID_Task));
            Assert.Equal(2, msg.PC.GetInt32Property(statusId));

            // PidLidTaskComplete == true
            PropertyID completeId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskComplete, PropertySetGuid.PSETID_Task));
            Assert.True(msg.PC.GetBooleanProperty(completeId));

            // PidLidPercentComplete == 1.0
            PropertyID pcId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidPercentComplete, PropertySetGuid.PSETID_Task));
            double? pct = msg.PC.GetFloat64Property(pcId);
            Assert.NotNull(pct);
            Assert.Equal(1.0, pct!.Value, 3);

            // PidLidTaskDateCompleted written as instant
            PropertyID dcId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskDateCompleted, PropertySetGuid.PSETID_Task));
            DateTime? dc = msg.PC.GetDateTimeProperty(dcId);
            Assert.NotNull(dc);
            Assert.Equal(completedAt.UtcDateTime, dc!.Value);

            return true;
        });
    }

    [Fact]
    public void WriteTask_with_reminder_sets_absolute_reminder_time()
    {
        var reminderAt = new DateTimeOffset(2026, 6, 30, 9, 0, 0, TimeSpan.Zero);
        var t = new TaskRecord
        {
            Subject      = "Reminded task",
            ReminderSet  = true,
            ReminderTime = reminderAt,
        };

        RoundTripTask(t, (pst, msg) =>
        {
            // PidLidReminderSet == true
            PropertyID rsId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidReminderSet, PropertySetGuid.PSETID_Common));
            Assert.True(msg.PC.GetBooleanProperty(rsId));

            // PidLidReminderTime == the instant
            PropertyID rtId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidReminderTime, PropertySetGuid.PSETID_Common));
            DateTime? rt = msg.PC.GetDateTimeProperty(rtId);
            Assert.NotNull(rt);
            Assert.Equal(reminderAt.UtcDateTime, rt!.Value);

            // PidLidReminderSignalTime == the same instant (delta=0 — absolute, not minutes-before)
            PropertyID rstId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidReminderSignalTime, PropertySetGuid.PSETID_Common));
            DateTime? rst = msg.PC.GetDateTimeProperty(rstId);
            Assert.NotNull(rst);
            Assert.Equal(rt!.Value, rst!.Value);

            // PidLidReminderDelta == 0
            PropertyID rdId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidReminderDelta, PropertySetGuid.PSETID_Common));
            Assert.Equal(0, msg.PC.GetInt32Property(rdId));

            return true;
        });
    }

    [Fact]
    public void WriteTask_private_sets_sensitivity_and_PidLidPrivate()
    {
        // Task 0 ground-truth: a Private task sets BOTH PidTagSensitivity=2 AND PidLidPrivate=true.
        var t = new TaskRecord { Subject = "Secret task", Sensitivity = 2 };
        RoundTripTask(t, (pst, msg) =>
        {
            Assert.Equal(2, msg.PC.GetInt32Property(PropertyID.PidTagSensitivity));

            PropertyID privateId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidPrivate, PropertySetGuid.PSETID_Common));
            Assert.True(msg.PC.GetBooleanProperty(privateId));

            return true;
        });
    }

    [Fact]
    public void WriteTask_no_dates_omits_start_and_due()
    {
        // StartDate=DueDate=null → the named props should be absent.
        // Use GetIDFromName (non-mutating) because on a read-only reopen the props were never
        // registered, and ObtainIDFromName would try to add them (requiring BeginSavingChanges).
        var t = new TaskRecord { Subject = "No dates" };
        RoundTripTask(t, (pst, msg) =>
        {
            PropertyID? startId = pst.NameToIDMap.GetIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskStartDate, PropertySetGuid.PSETID_Task));
            PropertyID? dueId = pst.NameToIDMap.GetIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskDueDate, PropertySetGuid.PSETID_Task));

            // If the prop was never written it won't be in the NameToIDMap at all (null),
            // or if somehow allocated it must have no value on this specific message.
            bool startAbsent = startId is null || msg.PC.GetDateTimeProperty(startId.Value) is null;
            bool dueAbsent   = dueId   is null || msg.PC.GetDateTimeProperty(dueId.Value) is null;
            Assert.True(startAbsent, "PidLidTaskStartDate should not be written when StartDate is null");
            Assert.True(dueAbsent,   "PidLidTaskDueDate should not be written when DueDate is null");

            return true;
        });
    }

    [Fact]
    public void WriteTask_due_date_round_trips_as_same_calendar_day_in_nonUTC_offset()
    {
        // A due date authored in a +07:00 offset must read back as the same Y/M/D at UTC midnight,
        // not shifted. NormalizeDateOnly uses the DTO's own Y/M/D, so no tz-shift occurs.
        var t = new TaskRecord
        {
            Subject = "TZ check",
            DueDate = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.FromHours(7)),
        };

        RoundTripTask(t, (pst, msg) =>
        {
            PropertyID dueId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskDueDate, PropertySetGuid.PSETID_Task));
            DateTime? due = msg.PC.GetDateTimeProperty(dueId);
            Assert.NotNull(due);
            Assert.Equal(new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), due!.Value);
            return true;
        });
    }
}
