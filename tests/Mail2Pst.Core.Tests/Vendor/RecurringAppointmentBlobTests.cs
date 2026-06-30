using System;
using System.IO;
using PSTFileFormat;
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

    private static byte[] HexBytes(string hex)
    {
        string[] parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        byte[] b = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++) b[i] = Convert.ToByte(parts[i], 16);
        return b;
    }
}
