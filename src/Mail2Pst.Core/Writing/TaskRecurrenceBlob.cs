// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using Mail2Pst.Core.Models;
using PSTFileFormat;
// Alias disambiguates our model enum from PSTFileFormat.RecurrenceFrequency (the vendor blob type).
using RecurrenceFrequency = Mail2Pst.Core.Models.RecurrenceFrequency;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Builds the bare MS-OXOCAL RecurrencePattern bytes for <c>PidLidTaskRecurrence</c>
/// (0x8116, PSETID_Task) from a <see cref="RecurrenceSpec"/>.
/// </summary>
/// <remarks>
/// Tasks carry only the RecurrencePattern prefix — no AppointmentRecurrencePattern tail
/// (no time offsets, no exception list).  The serialiser is the same vendored
/// <see cref="AppointmentRecurrencePatternStructure"/> used by <see cref="AppointmentWriter"/>;
/// the difference is that we call
/// <see cref="AppointmentRecurrencePatternStructure.GetRecurrencePatternBytes"/>
/// instead of <see cref="AppointmentRecurrencePatternStructure.GetBytes"/>.
/// Byte-gated against real Outlook task ground-truth oracles (PR7b, 2026-07-01).
/// </remarks>
internal static class TaskRecurrenceBlob
{
    /// <summary>Serialises <paramref name="spec"/> to bare MS-OXOCAL RecurrencePattern bytes.</summary>
    internal static byte[] Build(RecurrenceSpec spec, TimeZoneInfo zone)
        => BuildStructure(spec, zone).GetRecurrencePatternBytes();

    private static AppointmentRecurrencePatternStructure BuildStructure(RecurrenceSpec spec, TimeZoneInfo zone)
    {
        // Map our model enum → vendored PSTFileFormat.RecurrenceType (mirrors AppointmentWriter.ApplyRecurrence).
        PSTFileFormat.RecurrenceType recType = spec.Frequency switch
        {
            RecurrenceFrequency.Daily      => PSTFileFormat.RecurrenceType.EveryNDays,
            RecurrenceFrequency.Weekly     => PSTFileFormat.RecurrenceType.EveryNWeeks,
            RecurrenceFrequency.Monthly    => PSTFileFormat.RecurrenceType.EveryNMonths,
            RecurrenceFrequency.MonthlyNth => PSTFileFormat.RecurrenceType.EveryNthDayOfEveryNMonths,
            RecurrenceFrequency.Yearly     => PSTFileFormat.RecurrenceType.EveryNYears,
            RecurrenceFrequency.YearlyNth  => PSTFileFormat.RecurrenceType.EveryNthDayOfEveryNYears,
            _                              => PSTFileFormat.RecurrenceType.EveryNDays,
        };

        var freq    = RecurrenceTypeHelper.GetRecurrenceFrequency(recType);
        var patType = RecurrenceTypeHelper.GetPatternType(recType);
        int period  = Math.Max(1, spec.Interval);

        AppointmentRecurrencePatternStructure structure;
        int dayForCount; // passed to CalendarHelper.CalculateNumberOfOccurences as 'day'

        switch (freq)
        {
            case PSTFileFormat.RecurrenceFrequency.Weekly:
            {
                // Guard: default to the DTSTART weekday if DaysOfWeek is empty.
                var mask = ToDaysMask(spec.DaysOfWeek);
                if (mask == 0) mask = ToDaysMask(new[] { spec.FirstStartLocal.DayOfWeek });
                var w = new WeeklyRecurrencePatternStructure { DaysOfWeek = mask };
                dayForCount = (int)mask;
                structure   = w;
                break;
            }
            case PSTFileFormat.RecurrenceFrequency.Monthly:
            {
                var m = new MonthlyRecurrencePatternStructure();
                if (patType == PSTFileFormat.PatternType.Month)
                {
                    m.DayOfMonth = (uint)(spec.DayOfMonth ?? spec.FirstStartLocal.Day);
                    dayForCount  = (int)m.DayOfMonth;
                }
                else // MonthNth
                {
                    m.DayOfWeek          = ToOutlookDay(spec.DaysOfWeek.Length > 0 ? spec.DaysOfWeek[0] : DayOfWeek.Monday);
                    m.DayOccurenceNumber = ToOccurrenceNumber(spec.NthOccurrence);
                    dayForCount          = (int)m.DayOfWeek;
                }
                structure = m;
                break;
            }
            case PSTFileFormat.RecurrenceFrequency.Yearly:
            {
                var y = new YearlyRecurrencePatternStructure();
                if (patType == PSTFileFormat.PatternType.Month)
                {
                    y.DayOfMonth = (uint)(spec.DayOfMonth ?? spec.FirstStartLocal.Day);
                    dayForCount  = (int)y.DayOfMonth;
                }
                else // MonthNth
                {
                    y.DayOfWeek          = ToOutlookDay(spec.DaysOfWeek.Length > 0 ? spec.DaysOfWeek[0] : DayOfWeek.Monday);
                    y.DayOccurenceNumber = ToOccurrenceNumber(spec.NthOccurrence);
                    dayForCount          = (int)y.DayOfWeek;
                }
                structure = y;
                break;
            }
            default: // Daily
            {
                structure   = new DailyRecurrencePatternStructure();
                dayForCount = 0;
                break;
            }
        }

        structure.PatternType = patType;
        structure.PeriodInRecurrenceTypeUnits = period;

        // SetStartAndDuration sets StartDate (the private backing field) via StartDTZone.
        // duration=0: tasks have no appointment duration; StartTimeOffset/EndTimeOffset are
        // NOT part of the bare RecurrencePattern emitted by GetRecurrencePatternBytes().
        structure.SetStartAndDuration(spec.FirstStartUtc, 0, zone);

        // FirstDateTime: days-within-period offset from Jan 1, 1601 epoch.
        // Must use zone-local start (not UTC) — same convention as RecurringAppointment.GetRecurrencePattern.
        DateTime startDTZone = TimeZoneInfo.ConvertTimeFromUtc(spec.FirstStartUtc, zone);
        structure.FirstDateTimeInDays = AppointmentRecurrencePatternStructure.CalculateFirstDateTimeInDays(
            freq, patType, period, startDTZone);

        // EndType + OccurrenceCount + EndDate (LastInstanceStartDate).
        if ((spec.EndKind is RecurrenceEndKind.Count or RecurrenceEndKind.Until)
            && spec.LastInstanceStartUtc is { } last)
        {
            bool isCount = (spec.EndKind == RecurrenceEndKind.Count);
            structure.EndType = isCount
                ? PSTFileFormat.RecurrenceEndType.EndAfterNOccurrences
                : PSTFileFormat.RecurrenceEndType.EndAfterDate;
            // For a COUNT series, OccurrenceCount is the RRULE COUNT itself — NOT a date-span heuristic,
            // which overcounts period-skipping patterns (e.g. BYMONTHDAY=31 skips 30-day months). The
            // heuristic is retained only for the end-by-date (UNTIL) branch, where it matches Outlook.
            structure.OccurrenceCount = isCount
                ? (uint)Math.Max(1, spec.Count ?? 1)   // must not be 0 (Outlook 2003 recurrence-window guard)
                : (uint)CalendarHelper.CalculateNumberOfOccurences(
                    spec.FirstStartUtc, last, recType, period, dayForCount);
            structure.LastInstanceStartDate = last;
        }
        else // NoEnd
        {
            structure.EndType         = PSTFileFormat.RecurrenceEndType.NeverEnd;
            structure.OccurrenceCount = 10; // no-end sentinel per MS-OXOCAL + Outlook GT
            structure.LastInstanceStartDate = new DateTime(4500, 8, 31, 0, 0, 0, DateTimeKind.Utc);
        }

        // Tasks never carry deleted/modified exceptions in PidLidTaskRecurrence.
        // DeletedInstanceDates and ModifiedInstanceDates remain empty (default).
        return structure;
    }

    // -----------------------------------------------------------------------
    // Helpers (mirrors AppointmentWriter.ToMask / ToOutlookDay)
    // -----------------------------------------------------------------------

    private static DaysOfWeekFlags ToDaysMask(DayOfWeek[] days)
    {
        DaysOfWeekFlags m = 0;
        foreach (var d in days) m |= (DaysOfWeekFlags)(1u << (int)d);
        return m;
    }

    private static OutlookDayOfWeek ToOutlookDay(DayOfWeek d)
        => (OutlookDayOfWeek)(1u << (int)d);

    private static DayOccurenceNumber ToOccurrenceNumber(int? n) => (n ?? 1) switch
    {
        -1 => DayOccurenceNumber.Last,
        2  => DayOccurenceNumber.Second,
        3  => DayOccurenceNumber.Third,
        4  => DayOccurenceNumber.Fourth,
        _  => DayOccurenceNumber.First,
    };
}
