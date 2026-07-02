using System;
using System.IO;
using PSTFileFormat;
using Utilities;
using Xunit;

namespace Mail2Pst.Core.Tests.Vendor;

// PR7a Task 0 — byte-gate the vendored recurrence serializer (RecurringAppointment +
// AppointmentRecurrencePatternStructure) against the real Outlook PidLidAppointmentRecur blobs
// captured in docs/research/2026-07-01-pr7a-recurrence-ground-truth.md. Clean (no deletion/
// exception) cases assert the FULL blob; the deletion/exception cases parse and assert semantic
// fields (byte equality there is brittle — exact ExceptionInfo/ExtendedException layout). All
// blobs were re-dumped read-only from the Outlook-authored GT store (SE Asia Standard Time).
public class RecurringAppointmentBlobTests
{
    // PSTFile is NOT IDisposable and CreateEmptyStore returns void — close in try/finally.
    private static byte[] WriteAndReadRecurBlob(string path, Action<RecurringAppointment> configure)
    {
        PSTFile.CreateEmptyStore(path);
        PSTFile? file = null;
        try
        {
            file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            file.BeginSavingChanges();
            PSTFolder cal = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);
            RecurringAppointment appt = RecurringAppointment.CreateNewRecurringAppointment(file, cal.NodeID);
            appt.InternetCodepage = 65001;
            configure(appt);
            appt.SaveChanges();
            cal.AddMessage(appt);
            cal.SaveChanges();
            file.EndSavingChanges();
        }
        finally { file?.CloseFile(); }

        PSTFile? ro = null;
        try
        {
            ro = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            var cal = (CalendarFolder)ro.TopOfPersonalFolders.FindChildFolder("Calendar");
            return cal.GetAppointment(0).PC.GetBytesProperty(PropertyNames.PidLidAppointmentRecur);
        }
        finally { ro?.CloseFile(); }
    }

    // SE Asia Standard Time = the ground-truth zone (UTC+07, no DST).
    private static TimeZoneInfo SeAsia() =>
        TimeZoneInfo.CreateCustomTimeZone("SE Asia Standard Time", TimeSpan.FromHours(7),
            "(UTC+07:00) Bangkok, Hanoi, Jakarta", "SE Asia Standard Time");

    // The dump's "1# GT Daily every 2 days" blob (DeletedInstanceCount=0) — full-equality oracle.
    private static readonly byte[] DailyEvery2DaysBlob = HexBytes(
        "04 30 04 30 0A 20 00 00 00 00 A0 05 00 00 40 0B 00 00 00 00 00 00 22 20 00 00 05 00 00 00 " +
        "00 00 00 00 00 00 00 00 00 00 00 00 A0 BF 56 0D A0 EC 56 0D 06 30 00 00 09 30 00 00 E0 01 " +
        "00 00 FE 01 00 00 00 00 00 00 00 00 00 00 00 00");

    // The dump's "3# GT Monthly" blob (2nd Tuesday, end-by-date, OccurrenceCount=7, no deletions).
    private static readonly byte[] Monthly2ndTuesdayBlob = HexBytes(
        "04 30 04 30 0C 20 03 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 04 00 00 00 02 00 00 00 " +
        "21 20 00 00 07 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 C0 08 57 0D 60 73 5B 0D 06 30 " +
        "00 00 09 30 00 00 E0 01 00 00 FE 01 00 00 00 00 00 00 00 00 00 00 00 00");

    // The dump's "4# GT yearly" blob (July 6, no end / NeverEnd, OccurrenceCount=10, no deletions).
    private static readonly byte[] YearlyJul6Blob = HexBytes(
        "04 30 04 30 0D 20 02 00 00 00 20 FA 03 00 0C 00 00 00 00 00 00 00 06 00 00 00 23 20 00 00 " +
        "0A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 C0 DB 56 0D DF 80 E9 5A 06 30 00 00 09 30 " +
        "00 00 E0 01 00 00 FE 01 00 00 00 00 00 00 00 00 00 00 00 00");

    [Fact]
    public void Daily_every2days_count5_full_blob_matches_dump()
    {
        string path = Path.Combine(Path.GetTempPath(), $"recur-d2-{Guid.NewGuid():N}.pst");
        try
        {
            byte[] blob = WriteAndReadRecurBlob(path, a =>
            {
                a.SetOriginalTimeZone(SeAsia());
                a.RecurrenceType = RecurrenceType.EveryNDays; a.Period = 2;
                a.SetStartAndDuration(new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc), 30);
                a.EndAfterNumberOfOccurences = true;
                a.LastInstanceStartDate = new DateTime(2026, 7, 9, 1, 0, 0, DateTimeKind.Utc); // 5th: Jul 1,3,5,7,9
            });
            Assert.Equal(DailyEvery2DaysBlob, blob);   // FULL blob equality
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Monthly_2ndTuesday_full_blob_matches_dump()
    {
        string path = Path.Combine(Path.GetTempPath(), $"recur-m-{Guid.NewGuid():N}.pst");
        try
        {
            byte[] blob = WriteAndReadRecurBlob(path, a =>
            {
                a.SetOriginalTimeZone(SeAsia());
                a.RecurrenceType = RecurrenceType.EveryNthDayOfEveryNMonths; a.Period = 1;
                a.Day = (int)OutlookDayOfWeek.Tuesday;
                a.DayOccurenceNumber = DayOccurenceNumber.Second;
                a.SetStartAndDuration(new DateTime(2026, 7, 14, 1, 0, 0, DateTimeKind.Utc), 30); // 2nd Tue of Jul 2026
                a.EndAfterNumberOfOccurences = false;                                            // end-by-date (UNTIL)
                a.LastInstanceStartDate = new DateTime(2027, 1, 31, 1, 0, 0, DateTimeKind.Utc);  // EndDate from dump → OccurrenceCount=7
            });
            Assert.Equal(Monthly2ndTuesdayBlob, blob);   // FULL blob equality
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Yearly_Jul6_noEnd_full_blob_matches_dump()
    {
        string path = Path.Combine(Path.GetTempPath(), $"recur-y-{Guid.NewGuid():N}.pst");
        try
        {
            byte[] blob = WriteAndReadRecurBlob(path, a =>
            {
                a.SetOriginalTimeZone(SeAsia());
                a.RecurrenceType = RecurrenceType.EveryNYears; a.Period = 1;
                a.Day = 6;                                                                       // day-of-month; month derived from start
                a.SetStartAndDuration(new DateTime(2026, 7, 6, 1, 0, 0, DateTimeKind.Utc), 30);  // start = first occurrence (Jul 6)
                a.EndAfterNumberOfOccurences = false;
                a.LastInstanceStartDate = new DateTime(4500, 8, 31, 0, 0, 0, DateTimeKind.Utc);  // → NeverEnd, OccurrenceCount=10
            });
            Assert.Equal(YearlyJul6Blob, blob);   // FULL blob equality
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // Daily, every day, COUNT=5, with the 3rd occurrence deleted (a pure EXDATE deletion).
    // Semantic-field gate (§7 confirmed rule): OccurrenceCount stays the raw COUNT (5), NOT the
    // visible count after deletion (4); the deleted date is in DeletedInstanceDates with no exception.
    [Fact]
    public void Daily_with_deletion_keeps_raw_count_and_one_deleted_date()
    {
        string path = Path.Combine(Path.GetTempPath(), $"recur-ddel-{Guid.NewGuid():N}.pst");
        try
        {
            TimeZoneInfo tz = SeAsia();
            DateTime deletedOccUtc = new(2026, 7, 3, 1, 0, 0, DateTimeKind.Utc); // 3rd occurrence (Jul 3)
            byte[] blob = WriteAndReadRecurBlob(path, a =>
            {
                a.SetOriginalTimeZone(tz);
                a.RecurrenceType = RecurrenceType.EveryNDays; a.Period = 1;
                a.SetStartAndDuration(new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc), 30);
                a.EndAfterNumberOfOccurences = true;
                a.LastInstanceStartDate = new DateTime(2026, 7, 5, 1, 0, 0, DateTimeKind.Utc); // 5th occ = Jul 5
                a.DeletedInstanceDates.Add(DateTime.SpecifyKind(
                    TimeZoneInfo.ConvertTimeFromUtc(deletedOccUtc, tz).Date, DateTimeKind.Unspecified));
            });
            var s = AppointmentRecurrencePatternStructure.GetRecurrencePatternStructure(blob);
            Assert.Equal(0x0A, blob[4]); Assert.Equal(0x20, blob[5]);              // Daily
            Assert.Equal(RecurrenceEndType.EndAfterNOccurrences, s.EndType);
            Assert.Equal(5u, s.OccurrenceCount);                                   // raw COUNT, not 4
            Assert.Equal(1, s.DeletedInstanceDates.Count);
            Assert.Empty(s.ModifiedInstanceDates);
            Assert.Empty(s.ExceptionList);
            Assert.Equal(new DateTime(2026, 7, 3), s.DeletedInstanceDates[0].Date);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // Weekly Mon+Wed COUNT=6 (last occ Jul 20) with one overridden (modified) occurrence.
    // Semantic gate: the overridden occurrence's original date appears in BOTH the deleted-dates
    // and modified-dates arrays (§7 confirmed rule), and exactly one ExceptionInfo is serialized.
    [Fact]
    public void Weekly_with_override_modified_date_in_both_arrays()
    {
        string path = Path.Combine(Path.GetTempPath(), $"recur-wex-{Guid.NewGuid():N}.pst");
        try
        {
            TimeZoneInfo tz = SeAsia();
            DateTime origStartUtc = new(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc);     // the overridden occurrence (Jul 8 Wed)
            byte[] blob = WriteAndReadRecurBlob(path, a =>
            {
                a.SetOriginalTimeZone(tz);
                a.RecurrenceType = RecurrenceType.EveryNWeeks; a.Period = 1;
                a.Day = (int)(DaysOfWeekFlags.Monday | DaysOfWeekFlags.Wednesday);
                a.SetStartAndDuration(new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc), 30);
                a.EndAfterNumberOfOccurences = true;
                a.LastInstanceStartDate = new DateTime(2026, 7, 20, 1, 0, 0, DateTimeKind.Utc); // 6th occ = Jul 20

                a.AddModifiedInstanceAttachment(origStartUtc, 30, origStartUtc.AddHours(6), 30,
                    "GT weekly MODIFIED", "", BusyStatus.Busy, 0, MessagePriority.Normal, tz);
                var ex = new ExceptionInfoStructure();
                ex.SetOriginalStartDTUtc(origStartUtc, tz);
                ex.SetStartAndDuration(origStartUtc.AddHours(6), 30, tz);
                ex.HasModifiedSubject = true; ex.Subject = "GT weekly MODIFIED";
                a.ExceptionList.Add(ex);
                a.DeletedInstanceDates.Add(DateTime.SpecifyKind(
                    TimeZoneInfo.ConvertTimeFromUtc(origStartUtc, tz).Date, DateTimeKind.Unspecified));
            });
            var s = AppointmentRecurrencePatternStructure.GetRecurrencePatternStructure(blob);
            Assert.Equal(0x0B, blob[4]); Assert.Equal(0x20, blob[5]);   // Weekly
            Assert.Single(s.ExceptionList);
            Assert.Single(s.ModifiedInstanceDates);
            Assert.Equal(1, s.DeletedInstanceDates.Count);
            Assert.Equal(s.DeletedInstanceDates[0].Date, s.ModifiedInstanceDates[0].Date);  // both arrays
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ────────────────── Task 1 (PR7b): bare-pattern serializer tests ──────────────────

    // Builds the GT Task Weekly structure (weekly Mon, end-after-5, due 2026-07-06).
    // Reused by GetRecurrencePatternBytes_is_the_prefix test and the weekly oracle gate.
    private static WeeklyRecurrencePatternStructure BuildWeeklyMondayCount5Structure()
    {
        var s = new WeeklyRecurrencePatternStructure();
        s.PatternType = PatternType.Week;
        s.DaysOfWeek = DaysOfWeekFlags.Monday;
        s.Period = 1;
        s.FirstDateTimeInDays = AppointmentRecurrencePatternStructure.CalculateFirstDateTimeInDays(
            RecurrenceFrequency.Weekly, PatternType.Week, 1, new DateTime(2026, 7, 6));
        s.EndType = RecurrenceEndType.EndAfterNOccurrences;
        s.OccurrenceCount = 5;
        s.FirstDOW = 0;
        s.StartDTZone = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Unspecified);
        s.LastInstanceStartDate = new DateTime(2026, 8, 3);
        return s;
    }

    [Fact]
    public void GetRecurrencePatternBytes_is_the_prefix_of_GetBytes()
    {
        // The bare prefix must equal the leading bytes of the full appointment blob —
        // proves the split is a pure extraction that changes nothing for the appointment path.
        var s = BuildWeeklyMondayCount5Structure();
        byte[] full = s.GetBytes(WriterCompatibilityMode.Outlook2007RTM);
        byte[] bare = s.GetRecurrencePatternBytes();
        Assert.True(bare.Length < full.Length);
        Assert.Equal(full.AsSpan(0, bare.Length).ToArray(), bare); // prefix-equality
    }

    // Builder for the exact GT task structures from the research doc.
    private static AppointmentRecurrencePatternStructure BuildTaskGtStructure(string freq)
    {
        switch (freq)
        {
            case "weekly":
                // GT Task Weekly: weekly Mon, end-after-5, due 2026-07-06
                return BuildWeeklyMondayCount5Structure();

            case "monthly":
            {
                // GT Task Monthly: day 6, end-by 2026-12-31, OccurrenceCount=6
                var s = new MonthlyRecurrencePatternStructure();
                s.PatternType = PatternType.Month;
                s.DayOfMonth = 6;
                s.Period = 1;
                s.FirstDateTimeInDays = AppointmentRecurrencePatternStructure.CalculateFirstDateTimeInDays(
                    RecurrenceFrequency.Monthly, PatternType.Month, 1, new DateTime(2026, 7, 6));
                s.EndType = RecurrenceEndType.EndAfterDate;
                s.OccurrenceCount = 6;
                s.FirstDOW = 0;
                s.StartDTZone = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Unspecified);
                s.LastInstanceStartDate = new DateTime(2026, 12, 31);
                return s;
            }

            case "daily":
            {
                // GT Task Daily: daily (every day), no-end, OccurrenceCount=10 (sentinel), due 2026-07-01
                var s = new DailyRecurrencePatternStructure();
                s.PatternType = PatternType.Day;
                s.Period = 1440; // minutes
                s.FirstDateTimeInDays = AppointmentRecurrencePatternStructure.CalculateFirstDateTimeInDays(
                    RecurrenceFrequency.Daily, PatternType.Day, 1, new DateTime(2026, 7, 1));
                s.EndType = RecurrenceEndType.NeverEnd;
                s.OccurrenceCount = 10;
                s.FirstDOW = 0;
                s.StartDTZone = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);
                s.LastInstanceStartDate = new DateTime(4500, 8, 31);
                return s;
            }

            default:
                throw new ArgumentException($"Unknown freq: {freq}");
        }
    }

    // Identical to HexBytes but named per the brief spec (strips spaces, parses hex pairs).
    private static byte[] HexToBytes(string hex) => HexBytes(hex);

    [Theory]
    // GT Task Weekly (weekly Mon, end-after-5, due 2026-07-06) — 54 B
    [InlineData("04 30 04 30 0B 20 01 00 00 00 C0 21 00 00 01 00 00 00 00 00 00 00 02 00 00 00 22 20 00 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 C0 DB 56 0D 40 79 57 0D", "weekly")]
    // GT Task Monthly (day 6, end-by 2026-12-31) — 54 B
    [InlineData("04 30 04 30 0C 20 02 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 06 00 00 00 21 20 00 00 06 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 C0 DB 56 0D 00 C5 5A 0D", "monthly")]
    // GT Task Daily (no-end) — 50 B
    [InlineData("04 30 04 30 0A 20 00 00 00 00 00 00 00 00 A0 05 00 00 00 00 00 00 23 20 00 00 0A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 A0 BF 56 0D DF 80 E9 5A", "daily")]
    public void Bare_pattern_matches_task_GT_oracle(string hex, string freq)
    {
        byte[] oracle = HexToBytes(hex);
        var s = BuildTaskGtStructure(freq);
        Assert.Equal(oracle, s.GetRecurrencePatternBytes());
    }

    // ────────────────── Pre-merge review #2: write path must not depend on the registry ──────────────────

    /// <summary>
    /// Regression (pre-merge review #2, cross-platform): the recurring-appointment write path
    /// (StartDTUtc setter, LastInstanceStartDate setter, SaveChanges) reads <c>OriginalTimeZone</c>
    /// repeatedly. Before the fix the getter re-derived the zone from the serialized blob via
    /// <c>RegistryTimeZoneUtils</c>, which throws <see cref="PlatformNotSupportedException"/> off-Windows
    /// (and, for a zone whose key name is not in the registry, silently falls back to the local system
    /// zone). The fix caches the zone passed to <c>SetOriginalTimeZone</c> and returns it directly.
    ///
    /// Proven by identity: after SetOriginalTimeZone the getter returns the EXACT instance we set — a
    /// zone with a made-up key name that does not exist in the Windows registry, so a blob round-trip
    /// could not reconstruct it. This is deterministic on every platform.
    /// </summary>
    [Fact]
    public void SetOriginalTimeZone_is_cached_not_re_derived_from_registry()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "M2P Cross-Platform Test Zone",           // NOT a Windows time-zone key name
            TimeSpan.FromMinutes(330),                // UTC+05:30 — distinct from any test host's local zone
            "(UTC+05:30) M2P Test", "M2P Test");

        string path = Path.Combine(Path.GetTempPath(), $"recur-tzcache-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(path);
        PSTFile? file = null;
        try
        {
            file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            file.BeginSavingChanges();
            PSTFolder cal = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);
            RecurringAppointment appt = RecurringAppointment.CreateNewRecurringAppointment(file, cal.NodeID);
            appt.SetOriginalTimeZone(zone);

            // Cached field short-circuits the registry-backed blob derivation.
            Assert.Same(zone, appt.OriginalTimeZone);
        }
        finally { file?.CloseFile(); if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// Cross-platform regression: reconstructing a zone from a TimeZoneStructure must not require the Windows
    /// registry. On non-Windows the registry has no display names (GetDisplayName returns null), which used to
    /// make CreateCustomTimeZone throw when a recurring appointment was read back (the calendar tests do this,
    /// so they failed on Linux/macOS CI). Simulated on Windows by an UNKNOWN zone id: its registry key is
    /// absent, so GetDisplayName returns null exactly as on Linux/macOS. Must fall back to the id, not throw.
    /// </summary>
    [Fact]
    public void ToTimeZoneInfo_without_registry_display_names_does_not_throw()
    {
        var s = new TimeZoneStructure
        {
            lBias = -420, lStandardBias = 0, lDaylightBias = 0,   // UTC+7, no DST (like the SE Asia gate zone)
            stStandardDate = new SystemTime(),                     // wMonth == 0 -> no-DST branch
            stDaylightDate = new SystemTime(),
        };

        TimeZoneInfo tz = s.ToTimeZoneInfo("M2P Unknown Zone Id");   // no such registry key -> GetDisplayName null

        Assert.NotNull(tz);
        Assert.Equal("M2P Unknown Zone Id", tz.Id);
        Assert.Equal(TimeSpan.FromHours(7), tz.BaseUtcOffset);
    }

    /// <summary>
    /// Cross-platform regression: some non-Windows (ICU) TimeZoneInfo rules use a fixed calendar date over a
    /// multi-year span, which SYSTEMTIME's one-time absolute form can't hold. AdjustmentRuleHelper must convert
    /// it to a relative-yearly rule instead of throwing (which aborted writing any appointment in such a zone
    /// on Linux/macOS). Deterministic on Windows: build such a rule directly.
    /// </summary>
    [Fact]
    public void FromTransitionTime_multi_year_fixed_date_becomes_relative_rule()
    {
        var dstStart = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 31);   // Mar 31
        var dstEnd   = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 31);  // Oct 31
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1), new DateTime(2020, 12, 31), TimeSpan.FromHours(1), dstStart, dstEnd);

        SystemTime std = AdjustmentRuleHelper.GetStandardDate(rule);   // DaylightTransitionEnd (Oct 31) — must not throw
        Assert.Equal(0, std.wYear);    // relative (yearly), not a one-time absolute date
        Assert.Equal(10, std.wMonth);  // October
        Assert.Equal(5, std.wDay);     // day 31 -> last (5th) occurrence within the month

        SystemTime dlt = AdjustmentRuleHelper.GetDaylightDate(rule);   // DaylightTransitionStart (Mar 31)
        Assert.Equal(3, dlt.wMonth);   // March
    }

    private static byte[] HexBytes(string hex)
    {
        string[] parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        byte[] b = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++) b[i] = Convert.ToByte(parts[i], 16);
        return b;
    }
}
