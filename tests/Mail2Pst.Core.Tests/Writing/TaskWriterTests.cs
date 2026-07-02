// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;
// Disambiguates Mail2Pst.Core.Models.RecurrenceFrequency from PSTFileFormat.RecurrenceFrequency.
using RecurrenceFrequency = Mail2Pst.Core.Models.RecurrenceFrequency;

namespace Mail2Pst.Core.Tests.Writing;

public class TaskWriterTests
{
    // ---------------------------------------------------------------------------
    // Round-trip infrastructure (mirrors ContactWriterTests / TaskMessageFactoryTests)
    // ---------------------------------------------------------------------------

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

    // ---------------------------------------------------------------------------
    // PR7b recurrence tests (Task 4)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Byte-gate: <see cref="TaskRecurrenceBlob.Build"/> must reproduce the exact
    /// PidLidTaskRecurrence bytes dumped from real Outlook for "GT Task Weekly"
    /// (weekly Mon, COUNT=5, due 2026-07-06).
    /// Oracle source: docs/research/2026-07-01-pr7a-recurrence-ground-truth.md,
    /// "Task recurrence ground truth" section.
    /// </summary>
    [Fact]
    public void Weekly_task_due_monday_writes_GT_oracle_bytes()
    {
        // GT Task Weekly oracle — 54 bytes, weekly Mon COUNT=5 starting 2026-07-06.
        // Decoded:
        //   ReaderVersion=0x3004, WriterVersion=0x3004
        //   RecurFrequency=0x200B (Weekly), PatternType=0x0001 (Week), CalendarType=0
        //   FirstDateTime=8640 (6 days × 1440 min — week-start offset from 1601-01-01)
        //   Period=1, SlidingFlag=0
        //   DaysOfWeek=0x02 (Monday)
        //   EndType=0x2022 (EndAfterNOccurrences), OccurrenceCount=5, FirstDOW=0
        //   DeletedInstanceCount=0, ModifiedInstanceCount=0
        //   StartDate=2026-07-06 (155,414 × 1440 = 0x0D56DBC0)
        //   EndDate=2026-08-03 (155,442 × 1440 = 0x0D577940)
        byte[] oracle = Convert.FromHexString(
            // 54 bytes: bare RecurrencePattern (no appointment tail).
            // Fields: ReaderVer+WriterVer+RecurFreq+PatType+CalType+FirstDateTime+Period+SlidingFlag
            //       + DaysOfWeek+EndType+OccurrenceCount+FirstDOW+DeletedCount+ModifiedCount
            //       + StartDate+EndDate
            "043004300B2001000000C02100000100000000000000" +  // header+FirstDateTime+Period+SlidingFlag (22 bytes)
            "020000002220000005000000" +                        // DaysOfWeek+EndType+OccurrenceCount (12 bytes)
            "000000000000000000000000" +                        // FirstDOW+DeletedCount+ModifiedCount (12 bytes)
            "C0DB560D4079570D");                                // StartDate+EndDate (8 bytes)

        var spec = new RecurrenceSpec
        {
            Frequency         = RecurrenceFrequency.Weekly,
            Interval          = 1,
            DaysOfWeek        = new[] { DayOfWeek.Monday },
            EndKind           = RecurrenceEndKind.Count,
            Count             = 5,
            FirstStartUtc     = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            FirstStartLocal   = new DateTime(2026, 7, 6, 0, 0, 0),
            LastInstanceStartUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc),
        };

        byte[] actual = TaskRecurrenceBlob.Build(spec, TimeZoneInfo.Utc);

        Assert.Equal(54, actual.Length);
        Assert.True(oracle.SequenceEqual(actual),
            $"Bytes differ.\nExpected: {Convert.ToHexString(oracle)}\nActual:   {Convert.ToHexString(actual)}");
    }

    /// <summary>
    /// Pre-merge review #5 (task path): for a COUNT-terminated task series the blob's OccurrenceCount
    /// must be the RRULE COUNT, not a date-span heuristic. Month-overflow (BYMONTHDAY=31 skips 30-day
    /// months) exposes the bug: 5 "day 31" occurrences from Jan 2026 span 7 months → the old heuristic
    /// wrote 8. OccurrenceCount is a 4-byte LE field at offset 30 in the bare RecurrencePattern
    /// (22-byte header + 4-byte pattern field + 4-byte EndType).
    /// </summary>
    [Fact]
    public void Count_task_month_overflow_writes_exact_occurrence_count()
    {
        var spec = new RecurrenceSpec
        {
            Frequency            = RecurrenceFrequency.Monthly,
            Interval             = 1,
            DayOfMonth           = 31,
            DaysOfWeek           = Array.Empty<DayOfWeek>(),
            EndKind              = RecurrenceEndKind.Count,
            Count                = 5,
            FirstStartUtc        = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            FirstStartLocal      = new DateTime(2026, 1, 31, 0, 0, 0),
            LastInstanceStartUtc = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc), // Jan,Mar,May,Jul,Aug
        };

        byte[] blob = TaskRecurrenceBlob.Build(spec, TimeZoneInfo.Utc);
        Assert.Equal((uint)5, BitConverter.ToUInt32(blob, 30));
    }

    /// <summary>
    /// Mutation-coverage (Stryker): the MonthlyNth "Nth weekday" task blob path was entirely uncovered.
    /// For MonthNth the bare RecurrencePattern writes DayOfWeek (uint at offset 22) then DayOccurenceNumber
    /// (uint at offset 26). "2nd Tuesday" → Tuesday(0x04)/Second(2); "Last Friday" → Friday(0x20)/Last(5).
    /// </summary>
    [Theory]
    [InlineData(DayOfWeek.Tuesday, 2,  (uint)OutlookDayOfWeek.Tuesday, (uint)DayOccurenceNumber.Second)]
    [InlineData(DayOfWeek.Friday, -1,  (uint)OutlookDayOfWeek.Friday,  (uint)DayOccurenceNumber.Last)]
    public void MonthlyNth_task_blob_writes_day_and_occurrence(DayOfWeek day, int nth, uint expectDow, uint expectOcc)
    {
        var spec = new RecurrenceSpec
        {
            Frequency       = RecurrenceFrequency.MonthlyNth,
            Interval        = 1,
            DaysOfWeek      = new[] { day },
            NthOccurrence   = nth,
            EndKind         = RecurrenceEndKind.NoEnd,
            FirstStartUtc   = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            FirstStartLocal = new DateTime(2026, 7, 14, 0, 0, 0),
        };

        byte[] blob = TaskRecurrenceBlob.Build(spec, TimeZoneInfo.Utc);
        Assert.Equal(expectDow, BitConverter.ToUInt32(blob, 22));   // DayOfWeek
        Assert.Equal(expectOcc, BitConverter.ToUInt32(blob, 26));   // DayOccurenceNumber
    }

    [Fact]
    public void Recurring_task_writes_TaskRecurrence_and_FRecurring_true()
    {
        // A TaskRecord with a RecurrenceSpec must round-trip with PidLidTaskFRecurring=true
        // and PidLidTaskRecurrence present (non-null bytes).
        var spec = new RecurrenceSpec
        {
            Frequency         = RecurrenceFrequency.Weekly,
            Interval          = 1,
            DaysOfWeek        = new[] { DayOfWeek.Monday },
            EndKind           = RecurrenceEndKind.Count,
            Count             = 5,
            FirstStartUtc     = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            FirstStartLocal   = new DateTime(2026, 7, 6, 0, 0, 0),
            LastInstanceStartUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        var t = new TaskRecord
        {
            Subject    = "Recurring GT Weekly",
            DueDate    = new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero),
            Recurrence = spec,
        };

        RoundTripTask(t, (pst, msg) =>
        {
            // PidLidTaskFRecurring == true
            PropertyID frId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskFRecurring, PropertySetGuid.PSETID_Task));
            Assert.True(msg.PC.GetBooleanProperty(frId),
                "PidLidTaskFRecurring should be true for a recurring task");

            // PidLidTaskRecurrence present and non-empty
            PropertyID recId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskRecurrence, PropertySetGuid.PSETID_Task));
            byte[]? recBytes = msg.PC.GetBytesProperty(recId);
            Assert.NotNull(recBytes);
            Assert.True(recBytes!.Length > 0, "PidLidTaskRecurrence should be non-empty");

            return true;
        });
    }

    [Fact]
    public void Non_recurring_task_writes_no_recurrence_props()
    {
        // A non-recurring TaskRecord (Recurrence=null) must NOT write PidLidTaskRecurrence
        // or PidLidTaskFRecurring — absent is the correct state (PR7b ground-truth confirms
        // Outlook omits these props on non-recurring tasks; writing them absent is equivalent
        // to FRecurring=false without the prop overhead).
        var t = new TaskRecord { Subject = "Non-recurring" };

        RoundTripTask(t, (pst, msg) =>
        {
            // Use GetIDFromName (non-mutating) so we don't allocate these names in the store.
            PropertyID? recId = pst.NameToIDMap.GetIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskRecurrence, PropertySetGuid.PSETID_Task));
            PropertyID? frId = pst.NameToIDMap.GetIDFromName(
                new PropertyName(PropertyLongID.PidLidTaskFRecurring, PropertySetGuid.PSETID_Task));

            // Prop is absent if: name was never registered (null) OR registered but no value on this message.
            bool recAbsent = recId is null || msg.PC.GetBytesProperty(recId.Value) is null;
            bool frAbsent  = frId  is null || msg.PC.GetBooleanProperty(frId.Value) is null;

            Assert.True(recAbsent, "PidLidTaskRecurrence should not be written for a non-recurring task");
            Assert.True(frAbsent,  "PidLidTaskFRecurring should not be written for a non-recurring task");

            return true;
        });
    }
}
