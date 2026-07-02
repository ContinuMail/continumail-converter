// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
// Alias disambiguates our model enum from PSTFileFormat.RecurrenceFrequency (the vendor blob type).
using RecurrenceFrequency = Mail2Pst.Core.Models.RecurrenceFrequency;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

/// <summary>
/// TDD tests for the recurrence-master-blob path in <see cref="AppointmentWriter"/>.
/// Mirrors the harness from <see cref="AppointmentWriterTests"/> but returns the
/// PidLidAppointmentRecur blob for structural assertions.
///
/// PR7a Task 3: write recurring master blob + timezone definitions (IANA→Windows).
/// </summary>
public class AppointmentWriterRecurrenceTests
{
    // -----------------------------------------------------------------------
    // Round-trip infrastructure — write, close, reopen, return (count, blob?)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Write one appointment record, close, reopen, and return the folder item count
    /// plus the PidLidAppointmentRecur blob (null for a non-recurring item).
    /// </summary>
    private static (int count, byte[]? blob, Appointment appt_) WriteAndReadAppointment(AppointmentRecord record,
        Func<PSTFile, Appointment, Appointment>? inspect = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-recur-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            // Write phase
            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder(
                    "Calendar", FolderItemTypeName.Appointment);
                new AppointmentWriter().WriteAppointment(pst, folder, record);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            // Read phase
            try
            {
                pst = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
                PSTFolder found = pst.TopOfPersonalFolders.FindChildFolder("Calendar");
                CalendarFolder cal = Assert.IsType<CalendarFolder>(found);
                Appointment appt = cal.GetAppointment(0);
                byte[]? blob = appt.PC.GetBytesProperty(PropertyNames.PidLidAppointmentRecur);
                inspect?.Invoke(pst, appt);
                return (cal.AppointmentCount, blob, appt);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Helpers: canned records
    // -----------------------------------------------------------------------

    /// <summary>
    /// Weekly Mon+Wed, 6 occurrences starting Wed 2026-07-01 01:00 UTC, last 2026-07-20 01:00 UTC.
    /// TimeZone = Utc, OriginatingTimeZoneId = "UTC".
    /// </summary>
    private static AppointmentRecord WeeklyRecord() => new AppointmentRecord
    {
        Subject  = "Weekly recurring",
        StartUtc = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
        EndUtc   = new DateTime(2026, 7, 1, 1, 30, 0, DateTimeKind.Utc),
        TimeZone = TimeZoneInfo.Utc,
        OriginatingTimeZoneId = "UTC",
        Recurrence = new RecurrenceSpec
        {
            Frequency           = RecurrenceFrequency.Weekly,
            Interval            = 1,
            DaysOfWeek          = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday },
            EndKind             = RecurrenceEndKind.Count,
            Count               = 6,
            LastInstanceStartUtc = new DateTime(2026, 7, 20, 1, 0, 0, DateTimeKind.Utc),
            FirstStartUtc       = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            FirstStartLocal     = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            TimeZone            = TimeZoneInfo.Utc,
            OriginatingTimeZoneId = "UTC",
        },
    };

    /// <summary>Daily, no end (NoEnd sentinel), OriginatingTimeZoneId = "Asia/Bangkok".</summary>
    private static AppointmentRecord BangkokRecord() => new AppointmentRecord
    {
        Subject  = "Bangkok daily",
        StartUtc = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
        EndUtc   = new DateTime(2026, 7, 1, 1, 30, 0, DateTimeKind.Utc),
        // TimeZone left null intentionally — OriginatingTimeZoneId drives zone resolution
        OriginatingTimeZoneId = "Asia/Bangkok",
        Recurrence = new RecurrenceSpec
        {
            Frequency             = RecurrenceFrequency.Daily,
            Interval              = 1,
            DaysOfWeek            = Array.Empty<DayOfWeek>(),
            EndKind               = RecurrenceEndKind.NoEnd,
            FirstStartUtc         = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            FirstStartLocal       = new DateTime(2026, 7, 1, 8, 0, 0), // UTC+7
            OriginatingTimeZoneId = "Asia/Bangkok",
        },
    };

    /// <summary>
    /// All-day weekly event on Monday starting 2026-07-13 (UTC midnight boundaries, flags=8=EVENT_ALLDAY).
    /// Used as input to <see cref="CalendarEventMapper.Map"/> so that PR5's all-day normalisation
    /// (midnight boundaries / IsAllDay flag) is applied before writing.
    /// </summary>
    private static RawEventGroup AllDayWeeklyGroup()
    {
        var ev = new RawEvent
        {
            Id           = "allday-weekly-001@example.com",
            Title        = "All-Day Weekly Review",
            EventStart   = new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L, // Monday
            EventStartTz = "UTC",
            EventEnd     = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L,
            EventEndTz   = "UTC",
            Flags        = 8, // EVENT_ALLDAY
        };
        ev.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=4"));
        return new RawEventGroup { Master = ev };
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Weekly Mon+Wed with COUNT=6: exactly one folder item is written and the
    /// PidLidAppointmentRecur blob is present with RecurrenceFrequency=Weekly (0x200B).
    /// </summary>
    [Fact]
    public void Weekly_recurring_writes_recur_blob_and_one_item()
    {
        var rec = WeeklyRecord();
        var (count, blob, _) = WriteAndReadAppointment(rec);
        Assert.Equal(1, count);
        Assert.NotNull(blob);
        Assert.Equal(0x0B, blob![4]);   // RecurrenceFrequency low byte  = 0x0B (weekly = 0x200B)
        Assert.Equal(0x20, blob[5]);    // RecurrenceFrequency high byte = 0x20
    }

    /// <summary>
    /// Regression: an all-day event anchored to a positive-offset zone stores StartUtc as
    /// local-midnight-in-UTC (July 1 00:00 Bangkok = June 30 17:00 UTC). The recurrence
    /// day-of-month must be derived from the LOCAL date (1), not the UTC date (30).
    /// </summary>
    [Fact]
    public void Yearly_all_day_local_anchor_uses_local_day_of_month()
    {
        var startUtc = new DateTime(2026, 6, 30, 17, 0, 0, DateTimeKind.Utc); // = July 1 00:00 Bangkok (UTC+7)
        var rec = new AppointmentRecord
        {
            Subject  = "Bare yearly all-day",
            StartUtc = startUtc,
            EndUtc   = startUtc.AddDays(1),
            IsAllDay = true,
            OriginatingTimeZoneId = "Asia/Bangkok",
            Recurrence = new RecurrenceSpec
            {
                Frequency             = RecurrenceFrequency.Yearly,
                Interval              = 1,
                DaysOfWeek            = Array.Empty<DayOfWeek>(),
                EndKind               = RecurrenceEndKind.NoEnd,
                FirstStartUtc         = startUtc,                          // .Day == 30 (UTC)
                FirstStartLocal       = new DateTime(2026, 7, 1, 0, 0, 0), // .Day == 1 (local)
                OriginatingTimeZoneId = "Asia/Bangkok",
            },
        };

        var (_, _, appt) = WriteAndReadAppointment(rec);
        var ra = Assert.IsType<RecurringAppointment>(appt);
        Assert.Equal(1, ra.Day);   // day-of-month = LOCAL day (July 1), not UTC day (June 30)
    }

    /// <summary>
    /// A recurring appointment with OriginatingTimeZoneId="Asia/Bangkok" must produce
    /// a non-null PidLidTimeZoneStruct (0x8233) AND PidLidAppointmentTimeZoneDefinitionStartDisplay
    /// (0x825E) on the written item — proving SetOriginalTimeZone was called with the Bangkok zone.
    ///
    /// Guarded with SkipOnPlatform since TryConvertIanaIdToWindowsId has no ICU on non-Windows .NET
    /// embedded test hosts without globalization data (Linux musl etc.) — best effort on this path.
    /// </summary>
    [Fact]
    public void Recurring_with_Bangkok_tz_writes_tz_definition_blobs()
    {
        var rec = BangkokRecord();
        var (count, blob, appt) = WriteAndReadAppointment(rec);
        Assert.Equal(1, count);

        // The recur blob must be present (proving we hit the recurring path).
        Assert.NotNull(blob);

        // PidLidTimeZoneStruct (0x8233) — written by RecurringAppointment.SetOriginalTimeZone
        byte[]? tzStruct = appt.PC.GetBytesProperty(PropertyNames.PidLidTimeZoneStruct);
        Assert.NotNull(tzStruct);

        // PidLidAppointmentTimeZoneDefinitionStartDisplay (0x825E) — written by SetOriginalTimeZone
        byte[]? tzDefStart = appt.PC.GetBytesProperty(PropertyNames.PidLidAppointmentTimeZoneDefinitionStartDisplay);
        Assert.NotNull(tzDefStart);
    }

    /// <summary>
    /// A recurring appointment with OriginatingTimeZoneId=null AND TimeZone=null must not throw.
    /// The writer falls back to UTC (recurring appt always gets a non-null zone to avoid the
    /// Win32 SaveChanges() fallback). UTC TZ blobs must be present.
    /// </summary>
    [Fact]
    public void Recurring_with_null_tz_id_falls_back_to_utc_no_throw()
    {
        var rec = new AppointmentRecord
        {
            Subject               = "No-TZ recurring",
            StartUtc              = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            EndUtc                = new DateTime(2026, 7, 1, 1, 30, 0, DateTimeKind.Utc),
            TimeZone              = null,             // floating
            OriginatingTimeZoneId = null,             // triggers UTC fallback
            Recurrence = new RecurrenceSpec
            {
                Frequency    = RecurrenceFrequency.Daily,
                Interval     = 1,
                DaysOfWeek   = Array.Empty<DayOfWeek>(),
                EndKind      = RecurrenceEndKind.NoEnd,
                FirstStartUtc = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            },
        };

        // Must not throw — UTC fallback keeps RecurringAppointment.SaveChanges() off the Win32 path.
        var (count, blob, appt) = WriteAndReadAppointment(rec);
        Assert.Equal(1, count);
        Assert.NotNull(blob);

        // UTC zone definition blobs must be present (SetOriginalTimeZone(UTC) was called).
        byte[]? tzStruct = appt.PC.GetBytesProperty(PropertyNames.PidLidTimeZoneStruct);
        Assert.NotNull(tzStruct);
    }

    /// <summary>
    /// Regression (pre-merge review #1, CRITICAL): a recurring appointment in a timezone that carries
    /// MULTIPLE DST adjustment rules (e.g. America/New_York → "Eastern Standard Time", which has
    /// historical rules) must NOT throw. The recurring path writes the legacy PidLidTimeZoneStruct via
    /// <c>TimeZoneStructure.FromTimeZoneInfo</c>, which throws <see cref="ArgumentException"/> on a zone
    /// with &gt;1 DST rule — aborting the ENTIRE conversion. Every prior recurrence test used a
    /// zero/one-rule zone (UTC / Asia-Bangkok), so this crash was invisible to CI and the owner's
    /// Thailand render-gate. The writer must collapse the resolved zone to a single-rule static zone
    /// (keeping the first rule, matching the vendor's own <c>GetFirstRule</c> choice) before handing it
    /// to the vendor blob writer.
    /// </summary>
    [Fact]
    public void Recurring_with_multi_DST_rule_timezone_does_not_throw()
    {
        // Precondition: resolve the Windows zone the same way the writer does and confirm it is genuinely
        // multi-rule, so this test exercises the crash path (not a vacuous pass).
        TimeZoneInfo winZone =
            TimeZoneInfo.TryConvertIanaIdToWindowsId("America/New_York", out string? winId) && winId is not null
                ? TimeZoneInfo.FindSystemTimeZoneById(winId)
                : TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.True(winZone.GetAdjustmentRules().Length > 1,
            "test precondition: America/New_York must resolve to a multi-DST-rule zone");

        var rec = new AppointmentRecord
        {
            Subject               = "Eastern weekly",
            StartUtc              = new DateTime(2026, 7, 1, 13, 0, 0, DateTimeKind.Utc), // 09:00 EDT
            EndUtc                = new DateTime(2026, 7, 1, 13, 30, 0, DateTimeKind.Utc),
            TimeZone              = winZone,
            OriginatingTimeZoneId = "America/New_York",
            Recurrence = new RecurrenceSpec
            {
                Frequency             = RecurrenceFrequency.Weekly,
                Interval              = 1,
                DaysOfWeek            = new[] { DayOfWeek.Wednesday },
                EndKind               = RecurrenceEndKind.Count,
                Count                 = 6,
                LastInstanceStartUtc  = new DateTime(2026, 8, 5, 13, 0, 0, DateTimeKind.Utc),
                FirstStartUtc         = new DateTime(2026, 7, 1, 13, 0, 0, DateTimeKind.Utc),
                FirstStartLocal       = new DateTime(2026, 7, 1, 9, 0, 0),
                TimeZone              = winZone,
                OriginatingTimeZoneId = "America/New_York",
            },
        };

        // Must not throw (currently throws ArgumentException "...multiple DST rules").
        var (count, blob, appt) = WriteAndReadAppointment(rec);
        Assert.Equal(1, count);
        Assert.NotNull(blob);

        // Legacy PidLidTimeZoneStruct must be present (proving SetOriginalTimeZone succeeded).
        byte[]? tzStruct = appt.PC.GetBytesProperty(PropertyNames.PidLidTimeZoneStruct);
        Assert.NotNull(tzStruct);
    }

    /// <summary>
    /// Pre-merge review #5: for a COUNT-terminated series the blob's OccurrenceCount must be the RRULE
    /// COUNT, not a date-span heuristic. A month-overflow pattern (BYMONTHDAY=31 skips 30-day months)
    /// exposes the bug: 5 occurrences of "day 31" from Jan 2026 land on Jan/Mar/May/Jul/Aug 31, a
    /// 7-month span → the old heuristic wrote 8. Outlook must see exactly 5.
    /// </summary>
    [Fact]
    public void Count_series_month_overflow_writes_exact_occurrence_count()
    {
        var rec = new AppointmentRecord
        {
            Subject  = "Day-31 monthly",
            StartUtc = new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc),
            EndUtc   = new DateTime(2026, 1, 31, 9, 30, 0, DateTimeKind.Utc),
            TimeZone = TimeZoneInfo.Utc,
            OriginatingTimeZoneId = "UTC",
            Recurrence = new RecurrenceSpec
            {
                Frequency             = RecurrenceFrequency.Monthly,
                Interval              = 1,
                DayOfMonth            = 31,
                DaysOfWeek            = Array.Empty<DayOfWeek>(),
                EndKind               = RecurrenceEndKind.Count,
                Count                 = 5,
                LastInstanceStartUtc  = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), // Jan,Mar,May,Jul,Aug
                FirstStartUtc         = new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc),
                FirstStartLocal       = new DateTime(2026, 1, 31, 9, 0, 0),
                TimeZone              = TimeZoneInfo.Utc,
                OriginatingTimeZoneId = "UTC",
            },
        };
        var (_, blob, _) = WriteAndReadAppointment(rec);
        var s = AppointmentRecurrencePatternStructure.GetRecurrencePatternStructure(blob!);
        Assert.Equal((uint)5, s.OccurrenceCount);
    }

    /// <summary>
    /// Should-fix (SF-A): a weekly series with an empty day-mask must fall back to the DTSTART weekday
    /// (matching TaskRecurrenceBlob), not write Day=0 (an invalid empty weekly pattern). 2026-07-01 is a
    /// Wednesday, so the mask must be non-zero.
    /// </summary>
    [Fact]
    public void Weekly_empty_day_mask_falls_back_to_start_weekday()
    {
        var start = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc); // Wednesday
        var rec = new AppointmentRecord
        {
            Subject  = "Weekly no mask",
            StartUtc = start,
            EndUtc   = start.AddMinutes(30),
            TimeZone = TimeZoneInfo.Utc,
            OriginatingTimeZoneId = "UTC",
            Recurrence = new RecurrenceSpec
            {
                Frequency             = RecurrenceFrequency.Weekly,
                Interval              = 1,
                DaysOfWeek            = Array.Empty<DayOfWeek>(),   // empty mask
                EndKind               = RecurrenceEndKind.Count,
                Count                 = 3,
                LastInstanceStartUtc  = new DateTime(2026, 7, 15, 1, 0, 0, DateTimeKind.Utc),
                FirstStartUtc         = start,
                FirstStartLocal       = start,
                TimeZone              = TimeZoneInfo.Utc,
                OriginatingTimeZoneId = "UTC",
            },
        };
        var (_, _, appt) = WriteAndReadAppointment(rec);
        var ra = Assert.IsType<RecurringAppointment>(appt);
        Assert.NotEqual(0, ra.Day);   // fell back to the DTSTART weekday mask
    }

    /// <summary>
    /// Mutation-coverage (Stryker): the MonthlyNth "Nth weekday" appointment path was uncovered.
    /// "2nd Tuesday of every month" must map to RecurrenceType=EveryNthDayOfEveryNMonths, Day=Tuesday
    /// (OutlookDayOfWeek 0x04), DayOccurenceNumber=Second.
    /// </summary>
    [Fact]
    public void MonthlyNth_second_Tuesday_sets_day_and_occurrence()
    {
        var (ra, _) = WriteMonthlyNth(new[] { DayOfWeek.Tuesday }, nth: 2);
        Assert.Equal(RecurrenceType.EveryNthDayOfEveryNMonths, ra.RecurrenceType);
        Assert.Equal((int)OutlookDayOfWeek.Tuesday, ra.Day);
        Assert.Equal(DayOccurenceNumber.Second, ra.DayOccurenceNumber);
    }

    /// <summary>
    /// Mutation-coverage (Stryker): "Last Friday of the month" (NthOccurrence=-1) must map to
    /// Day=Friday (0x20) and DayOccurenceNumber=Last — the previously-untested `Last` branch.
    /// </summary>
    [Fact]
    public void MonthlyNth_last_Friday_sets_occurrence_Last()
    {
        var (ra, _) = WriteMonthlyNth(new[] { DayOfWeek.Friday }, nth: -1);
        Assert.Equal((int)OutlookDayOfWeek.Friday, ra.Day);
        Assert.Equal(DayOccurenceNumber.Last, ra.DayOccurenceNumber);
    }

    // Writes a MonthlyNth (Nth weekday of every month, no end) master and returns the reopened
    // RecurringAppointment + its recur blob.
    private static (RecurringAppointment ra, byte[] blob) WriteMonthlyNth(DayOfWeek[] days, int nth)
    {
        var start = new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc); // 2nd Tuesday of Jul 2026
        var rec = new AppointmentRecord
        {
            Subject  = "Nth-day monthly",
            StartUtc = start,
            EndUtc   = start.AddMinutes(30),
            TimeZone = TimeZoneInfo.Utc,
            OriginatingTimeZoneId = "UTC",
            Recurrence = new RecurrenceSpec
            {
                Frequency             = RecurrenceFrequency.MonthlyNth,
                Interval              = 1,
                DaysOfWeek            = days,
                NthOccurrence         = nth,
                EndKind               = RecurrenceEndKind.NoEnd,
                FirstStartUtc         = start,
                FirstStartLocal       = start,
                TimeZone              = TimeZoneInfo.Utc,
                OriginatingTimeZoneId = "UTC",
            },
        };
        var (_, blob, appt) = WriteAndReadAppointment(rec);
        return (Assert.IsType<RecurringAppointment>(appt), blob!);
    }

    // -----------------------------------------------------------------------
    // Task 4: EXDATE deleted occurrences
    // -----------------------------------------------------------------------

    /// <summary>
    /// A single EXDATE (deleted occurrence) must produce exactly one entry in
    /// <c>DeletedInstanceDates</c> on the PidLidAppointmentRecur blob, set to the
    /// zone-local day-start (date only, Kind=Unspecified).
    /// </summary>
    [Fact]
    public void Exdate_adds_one_deleted_instance()
    {
        var rec = WeeklyRecord();
        rec.DeletedOccurrences = new[] { new RecurrenceInstanceId(
            new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Unspecified), "UTC", false) };
        var (_, blob, _) = WriteAndReadAppointment(rec);
        var s = AppointmentRecurrencePatternStructure.GetRecurrencePatternStructure(blob!);
        Assert.Single(s.DeletedInstanceDates);
        Assert.Equal(new DateTime(2026, 7, 8), s.DeletedInstanceDates[0].Date);
    }

    // -----------------------------------------------------------------------
    // Task 5: overridden occurrences (modified instances / embedded attachments)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the number of attachments on the written appointment, read while the PST file is still
    /// open (AttachmentTable is lazily loaded from the file via the subnode tree — accessing it after
    /// CloseFile throws ObjectDisposedException).
    /// </summary>
    private static int ReopenAttachmentCount(AppointmentRecord rec)
    {
        int count = 0;
        WriteAndReadAppointment(rec, (_, appt) => { count = appt.AttachmentCount; return appt; });
        return count;
    }

    /// <summary>
    /// A single overridden occurrence must produce:
    /// <list type="bullet">
    ///   <item>Exactly one entry in <c>ExceptionList</c> (→ one <c>ModifiedInstanceDates</c> entry).</item>
    ///   <item><c>DeletedInstanceDates.Count ≥ ModifiedInstanceDates.Count</c> (vendored GetBytes invariant).</item>
    ///   <item>Exactly one embedded <c>method=5</c> exception attachment on the master item.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Override_writes_exception_blob_entry_and_attachment()
    {
        var rec = WeeklyRecord();
        rec.Exceptions = new[] { new AppointmentException {
            OriginalInstance = new RecurrenceInstanceId(new DateTime(2026,7,8,1,0,0,DateTimeKind.Utc),
                new DateTime(2026,7,8,8,0,0,DateTimeKind.Unspecified), "UTC", false),
            NewStartUtc = new(2026,7,8,7,0,0,DateTimeKind.Utc), NewEndUtc = new(2026,7,8,7,30,0,DateTimeKind.Utc),
            Subject = "Standup MOVED", ChangeFlags = AppointmentExceptionChangeFlags.Subject | AppointmentExceptionChangeFlags.StartEnd } };
        var (_, blob, _) = WriteAndReadAppointment(rec);
        var s = AppointmentRecurrencePatternStructure.GetRecurrencePatternStructure(blob!);
        Assert.Single(s.ExceptionList);
        Assert.Single(s.ModifiedInstanceDates);
        Assert.True(s.DeletedInstanceDates.Count >= s.ModifiedInstanceDates.Count);  // GetBytes invariant
        Assert.Equal(1, ReopenAttachmentCount(rec));   // embedded method=5 exception
    }

    /// <summary>
    /// Pre-merge review #10: MS-OXOCAL 2.2.1.44 requires the ExceptionInfo array and ModifiedInstanceDates
    /// to be in the SAME ascending order and to correspond positionally. Overrides can arrive in arbitrary
    /// (SQLite row) order; here two are supplied newest-first. ModifiedInstanceDates is always written
    /// sorted, so before the fix the unsorted ExceptionInfo array desynced from it. After the fix both are
    /// ascending and ExceptionInfo[i].NewStartDT.Date == ModifiedInstanceDates[i].
    /// </summary>
    [Fact]
    public void Out_of_order_overrides_keep_exception_and_modified_date_arrays_corresponding()
    {
        var rec = WeeklyRecord();
        rec.Exceptions = new[]
        {
            // Jul 15 first (later), then Jul 8 (earlier) — reverse chronological input order.
            new AppointmentException {
                OriginalInstance = new RecurrenceInstanceId(new DateTime(2026,7,15,1,0,0,DateTimeKind.Utc),
                    new DateTime(2026,7,15,1,0,0,DateTimeKind.Unspecified), "UTC", false),
                NewStartUtc = new(2026,7,15,7,0,0,DateTimeKind.Utc), NewEndUtc = new(2026,7,15,7,30,0,DateTimeKind.Utc),
                Subject = "Later moved", ChangeFlags = AppointmentExceptionChangeFlags.Subject | AppointmentExceptionChangeFlags.StartEnd },
            new AppointmentException {
                OriginalInstance = new RecurrenceInstanceId(new DateTime(2026,7,8,1,0,0,DateTimeKind.Utc),
                    new DateTime(2026,7,8,1,0,0,DateTimeKind.Unspecified), "UTC", false),
                NewStartUtc = new(2026,7,8,7,0,0,DateTimeKind.Utc), NewEndUtc = new(2026,7,8,7,30,0,DateTimeKind.Utc),
                Subject = "Earlier moved", ChangeFlags = AppointmentExceptionChangeFlags.Subject | AppointmentExceptionChangeFlags.StartEnd },
        };

        var (_, blob, _) = WriteAndReadAppointment(rec);
        var s = AppointmentRecurrencePatternStructure.GetRecurrencePatternStructure(blob!);

        Assert.Equal(2, s.ExceptionList.Count);
        Assert.Equal(2, s.ModifiedInstanceDates.Count);
        // ExceptionInfo array must be ascending by new start...
        Assert.True(s.ExceptionList[0].NewStartDT < s.ExceptionList[1].NewStartDT);
        // ...and correspond positionally to ModifiedInstanceDates (both ascending).
        for (int i = 0; i < s.ExceptionList.Count; i++)
            Assert.Equal(s.ModifiedInstanceDates[i], s.ExceptionList[i].NewStartDT.Date);
    }

    /// <summary>
    /// A unicode subject on an overridden occurrence must round-trip through the embedded
    /// <c>method=5</c> attachment's <see cref="ModifiedAppointmentInstance.Subject"/> property
    /// (stored as MAPI PT_UNICODE — full UTF-16, not the ANSI blob ExceptionInfo field).
    /// </summary>
    [Fact]
    public void Override_unicode_subject_survives_from_embedded_attachment()
    {
        const string UnicodeSubject = "Mødet ✅ æøå";
        var rec = WeeklyRecord();
        rec.Exceptions = new[] { new AppointmentException {
            OriginalInstance = new RecurrenceInstanceId(new DateTime(2026,7,8,1,0,0,DateTimeKind.Utc),
                new DateTime(2026,7,8,1,0,0,DateTimeKind.Unspecified), "UTC", false),
            NewStartUtc = new(2026,7,8,7,0,0,DateTimeKind.Utc), NewEndUtc = new(2026,7,8,7,30,0,DateTimeKind.Utc),
            Subject = UnicodeSubject,
            ChangeFlags = AppointmentExceptionChangeFlags.Subject | AppointmentExceptionChangeFlags.StartEnd } };

        // Read the embedded attachment's Subject while the file is open (GetModifiedInstance accesses
        // the subnode tree, which requires the file handle).
        string? attachSubject = null;
        WriteAndReadAppointment(rec, (_, appt) =>
        {
            if (appt is RecurringAppointment ra && ra.AttachmentCount > 0)
                attachSubject = ra.GetModifiedInstance(0).Subject;
            return appt;
        });
        Assert.Equal(UnicodeSubject, attachSubject);
    }

    // -----------------------------------------------------------------------
    // Task 6: recurring MEETING preserves PR6 attendees + meeting state + recur blob
    // -----------------------------------------------------------------------

    /// <summary>
    /// A recurring meeting (organizer + ≥1 attendee) must write BOTH the
    /// <c>PidLidAppointmentRecur</c> blob (recurrence preserved) AND the PR6 meeting surface:
    /// recipient table populated, <c>PidLidAppointmentStateFlags</c> has <c>asfMeeting</c>
    /// (bit 0x1), and <c>PidLidResponseStatus == respOrganized</c> (1).
    ///
    /// Proves <c>WriteAttendees</c> runs on the recurring path — Task 3 routed it through
    /// BOTH the single and recurring code paths in <see cref="AppointmentWriter.WriteAppointment"/>.
    ///
    /// All recipient-table / named-prop reads happen inside the <c>inspect</c> callback while
    /// the <see cref="PSTFile"/> is still open (lazy-load gotcha — Task 5).
    /// </summary>
    [Fact]
    public void Recurring_meeting_writes_state_recipients_and_recur_blob()
    {
        var rec = WeeklyRecord();
        rec.Organizer = new AppointmentAttendee
        {
            DisplayName = "Org",
            Email       = "org@example.com",
            Kind        = AttendeeKind.Required,
            Response    = AttendeeResponse.Organized,
        };
        rec.Attendees = new[]
        {
            new AppointmentAttendee { DisplayName = "Req", Email = "req@example.com", Kind = AttendeeKind.Required, Response = AttendeeResponse.None },
        };

        int recipientCount = 0;
        int stateFlags = 0;
        int? responseStatus = null;

        var (_, blob, _) = WriteAndReadAppointment(rec, (pst, appt) =>
        {
            recipientCount = appt.RecipientCount;   // inside callback — recipient table lazily loaded from subnode tree

            // PidLidAppointmentStateFlags (write-only on Appointment) — read via named prop, same as AppointmentWriterAttendeeTests
            PropertyID sfId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidAppointmentStateFlags, PropertySetGuid.PSETID_Appointment));
            stateFlags = appt.PC.GetInt32Property(sfId) ?? 0;

            PropertyID rsId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidResponseStatus, PropertySetGuid.PSETID_Appointment));
            responseStatus = appt.PC.GetInt32Property(rsId);

            return appt;
        });

        Assert.NotNull(blob);                                               // recurrence preserved (Task 3)
        Assert.True(recipientCount >= 1, "RecipientCount must be >= 1 for a recurring meeting");   // PR6 recipients
        Assert.Equal(0x1, stateFlags & 0x1);                               // asfMeeting bit set (PidLidAppointmentStateFlags)
        Assert.Equal(1, responseStatus);                                    // respOrganized (PidLidResponseStatus)
    }

    /// <summary>
    /// Regression test for the zone-before-start ordering fix.
    ///
    /// <para>
    /// <c>RecurringAppointment.StartDTUtc</c> setter (inside <c>SetStartAndDuration</c>) computes
    /// <c>PidLidClipStart</c> as local-midnight in <c>this.OriginalTimeZone</c>.  Before the fix,
    /// <c>SetOriginalTimeZone</c> was called AFTER <c>SetStartAndDuration</c>, so on a UTC CI host
    /// <c>ClipStart</c> was midnight-UTC rather than midnight in the event's zone — the wrong date
    /// for events where start-of-day differs between the event zone and UTC.
    /// </para>
    /// <para>
    /// Scenario: Bangkok (UTC+7) recurring appointment, <c>StartUtc = 2026-07-01T02:00:00Z</c>
    /// (= 09:00 Bangkok local).  Bangkok local-midnight = 2026-07-01T00:00:00+07:00 = 2026-06-30T17:00:00Z.
    /// After the fix <c>ClipStart</c> must be <c>2026-06-30T17:00:00Z</c>;
    /// before the fix it would be <c>2026-07-01T00:00:00Z</c> on any UTC host.
    /// </para>
    /// </summary>
    [Fact]
    public void Recurring_Bangkok_ClipStart_is_event_zone_midnight_not_utc_midnight()
    {
        var rec = new AppointmentRecord
        {
            Subject               = "Bangkok ClipStart regression",
            StartUtc              = new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Utc),  // 09:00 Bangkok
            EndUtc                = new DateTime(2026, 7, 1, 2, 30, 0, DateTimeKind.Utc),
            OriginatingTimeZoneId = "Asia/Bangkok",
            Recurrence = new RecurrenceSpec
            {
                Frequency             = RecurrenceFrequency.Daily,
                Interval              = 1,
                DaysOfWeek            = Array.Empty<DayOfWeek>(),
                EndKind               = RecurrenceEndKind.NoEnd,
                FirstStartUtc         = new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Utc),
                FirstStartLocal       = new DateTime(2026, 7, 1, 9, 0, 0),   // UTC+7
                OriginatingTimeZoneId = "Asia/Bangkok",
            },
        };

        DateTime? clipStart = null;
        WriteAndReadAppointment(rec, (_, appt) =>
        {
            clipStart = appt.PC.GetDateTimeProperty(PropertyNames.PidLidClipStart);
            return appt;
        });

        Assert.NotNull(clipStart);
        // Bangkok midnight for the 2026-07-01 event date = 2026-06-30T17:00:00Z.
        // Before the fix (zone applied AFTER SetStartAndDuration) this would be 2026-07-01T00:00:00Z on a UTC host.
        Assert.Equal(new DateTime(2026, 6, 30, 17, 0, 0, DateTimeKind.Utc), clipStart!.Value.ToUniversalTime());
    }

    // -----------------------------------------------------------------------
    // I1: bare FREQ=WEEKLY writer round-trip — weekday mask bit
    // -----------------------------------------------------------------------

    /// <summary>
    /// Bare FREQ=WEEKLY (no BYDAY) where DTSTART is Monday 2026-07-06.
    /// The mapper defaults DaysOfWeek to [Monday]; the writer must reflect that in
    /// the PidLidAppointmentRecur blob (DaysOfWeekFlags.Monday = 0x02).
    /// </summary>
    private static RawEventGroup BareWeeklyGroup()
    {
        var ev = new RawEvent
        {
            Id           = "bare-weekly-001@example.com",
            Title        = "Bare Weekly",
            EventStart   = new DateTimeOffset(2026, 7, 6, 14, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L,
            EventStartTz = "UTC",
            EventEnd     = new DateTimeOffset(2026, 7, 6, 15, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L,
            EventEndTz   = "UTC",
            Flags        = 0,
        };
        ev.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY"));
        return new RawEventGroup { Master = ev };
    }

    /// <summary>
    /// Writer round-trip for I1: a bare FREQ=WEEKLY (no BYDAY) with DTSTART on Monday
    /// must produce a PidLidAppointmentRecur blob whose WeeklyRecurrencePatternStructure
    /// has DaysOfWeek == Monday (0x02).
    /// </summary>
    [Fact]
    public void Weekly_no_byday_blob_has_monday_mask()
    {
        var g   = BareWeeklyGroup();
        var rec = CalendarEventMapper.Map(g, out _);
        Assert.NotNull(rec);
        // Mapper must have defaulted DaysOfWeek to Monday (I1 fix).
        Assert.Equal(new[] { DayOfWeek.Monday }, rec!.Recurrence!.DaysOfWeek);

        var (_, blob, _) = WriteAndReadAppointment(rec);
        Assert.NotNull(blob);
        var weekly = (WeeklyRecurrencePatternStructure)
            AppointmentRecurrencePatternStructure.GetRecurrencePatternStructure(blob!);
        Assert.Equal(DaysOfWeekFlags.Monday, weekly.DaysOfWeek);
    }

    // -----------------------------------------------------------------------
    // Task 7: all-day recurring writer test
    // -----------------------------------------------------------------------

    /// <summary>
    /// An all-day weekly event (flags=4) mapped through <see cref="CalendarEventMapper.Map"/>
    /// must produce both a recurrence blob (<c>PidLidAppointmentRecur</c>) and
    /// <c>PidLidAppointmentSubType == true</c> (all-day) on the written item.
    ///
    /// The record is built via the mapper so that PR5's all-day normalisation (midnight
    /// boundaries, IsAllDay flag) is applied before the writer sees it — mirroring what the
    /// production pipeline does. Named-prop reads happen inside the inspect callback while
    /// the PST file is still open (lazy-load gotcha).
    /// </summary>
    [Fact]
    public void AllDay_weekly_recurring_writes_subtype_and_blob()
    {
        // Build the record through the mapper so that all-day normalisation applies.
        var g = AllDayWeeklyGroup();
        var rec = CalendarEventMapper.Map(g, out _);
        Assert.True(rec!.IsAllDay, "mapper must set IsAllDay from flags=4");
        Assert.NotNull(rec.Recurrence);

        bool isAllDayEvent = false;
        var (count, blob, _) = WriteAndReadAppointment(rec, (_, appt) =>
        {
            // IsAllDayEvent wraps PidLidAppointmentSubType (PT_BOOLEAN) — read while file is open.
            isAllDayEvent = appt.IsAllDayEvent;
            return appt;
        });

        Assert.Equal(1, count);
        Assert.NotNull(blob);   // PidLidAppointmentRecur must be present
        Assert.True(isAllDayEvent, "PidLidAppointmentSubType must be true for an all-day recurring appointment");
    }
}
