// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Calendar;

/// <summary>
/// Pure, stateless helper that maps a set of iCal recurrence lines
/// (RRULE, EXDATE, etc.) and an event anchor into a <see cref="RecurrenceSpec"/>.
/// Shared between <see cref="CalendarEventMapper"/> and any future task/todo mapper.
/// </summary>
public static class RecurrenceMapping
{
    /// <summary>
    /// Attempts to build a <see cref="RecurrenceSpec"/> from the supplied raw iCal lines
    /// and event anchor.
    /// </summary>
    /// <param name="icalLines">Raw iCal property lines from the event's recurrence block
    /// (e.g. <c>RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=5</c>).</param>
    /// <param name="firstStartUtc">UTC start of the first occurrence.</param>
    /// <param name="firstStartLocal">Local-time start of the first occurrence
    /// (in <paramref name="zone"/>; equals <paramref name="firstStartUtc"/> when zone is null).</param>
    /// <param name="zone">Display timezone, or <c>null</c> for floating/UTC events.</param>
    /// <param name="originatingTzId">Canonical IANA/Olson or tzone:// id verbatim from the source.</param>
    /// <returns>
    /// <c>(Spec, null)</c> when the rule is fully mappable;
    /// <c>(null, reason)</c> when the rule must be degraded to a single occurrence.
    /// Returns <c>(null, null)</c> when no RRULE is present or parsing fails
    /// (caller treats this as a single occurrence with no warning).
    /// </returns>
    public static (RecurrenceSpec? Spec, string? DegradeReason) FromIcal(
        IReadOnlyList<string> icalLines,
        DateTime firstStartUtc,
        DateTime firstStartLocal,
        TimeZoneInfo? zone,
        string? originatingTzId,
        ParsedRecurrence? parsedRecurrence = null)
    {
        var lines = icalLines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        // Reuse a recurrence the caller already parsed (the appointment path parses once up front to
        // harvest warnings + EXDATEs); only parse here when the caller didn't (the task path).
        ParsedRecurrence? p = parsedRecurrence ?? ICalTextParser.ParseRecurrence(lines).Value;

        if (p is null)
            return (null, null); // no RRULE or parse failure — caller handles

        string? degradeReason = ComputeDegradeReason(p, lines);
        if (degradeReason is not null)
            return (null, degradeReason);

        // Build the RRULE body for Ical.Net (strip the "RRULE:" prefix).
        string rruleLine = lines.First(l => l.StartsWith("RRULE", StringComparison.OrdinalIgnoreCase));
        // Trim() strips the trailing CRLF Mozilla stores on each property so the RRULE body embedded
        // in the COUNT-enumeration VCALENDAR text is a clean single line.
        string rawRRuleBody = StripRRulePrefix(IcalParseSupport.UnfoldIcalLines(rruleLine).Trim());

        var spec = new RecurrenceSpec
        {
            Frequency     = MapFreq(p),
            Interval      = Math.Max(1, p.Interval),
            DaysOfWeek    = p.ByDay.Select(d => d.DayOfWeek).Distinct().ToArray(),
            DayOfMonth    = p.ByMonthDay.Count == 1 ? p.ByMonthDay[0] : (int?)null,
            NthOccurrence = p.ByDay.Count == 1 ? p.ByDay[0].Offset : null,
            Month         = p.ByMonth.Count == 1 ? p.ByMonth[0] : (int?)null,
            FirstStartUtc         = firstStartUtc,
            FirstStartLocal       = firstStartLocal,
            TimeZone              = zone,
            OriginatingTimeZoneId = originatingTzId,
            RawRRuleBody          = rawRRuleBody,
        };

        // RFC 5545 §3.3.10: a bare FREQ=WEEKLY with no BYDAY recurs on the DTSTART weekday.
        if (spec.Frequency == RecurrenceFrequency.Weekly && spec.DaysOfWeek.Length == 0)
            spec.DaysOfWeek = new[] { spec.FirstStartLocal.DayOfWeek };

        if (p.Count > 0)
        {
            spec.EndKind = RecurrenceEndKind.Count;
            spec.Count   = p.Count;
        }
        else if (p.UntilUtc is { } until)
        {
            spec.EndKind  = RecurrenceEndKind.Until;
            spec.UntilUtc = until;
        }
        else
        {
            spec.EndKind = RecurrenceEndKind.NoEnd;
        }

        spec.LastInstanceStartUtc = ComputeLastInstanceUtc(spec);

        // Defensive guard: ComputeLastInstanceUtc failed (near-unreachable path — requires
        // Ical.Net to throw during COUNT enumeration). Signal as a degrade so the caller
        // can warn and fall back to a single occurrence.
        if (spec.EndKind == RecurrenceEndKind.Count && spec.LastInstanceStartUtc is null)
            return (null, "COUNT enumeration failed");

        return (spec, null);
    }

    // ---------------------------------------------------------------------------
    // Internal helpers (pure — no AppointmentRecord coupling)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Strips the <c>RRULE:</c> prefix (case-insensitive) from a raw iCal RRULE line,
    /// returning the bare rule body.
    /// </summary>
    internal static string StripRRulePrefix(string line)
    {
        const string prefix = "RRULE:";
        return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? line.Substring(prefix.Length)
            : line;
    }

    /// <summary>
    /// Returns a non-null reason string when the parsed recurrence cannot be faithfully
    /// represented in the <see cref="RecurrenceSpec"/> model, or <c>null</c> when mappable.
    /// </summary>
    private static string? ComputeDegradeReason(ParsedRecurrence p, List<string> lines)
    {
        // 1. Unknown FREQ (ical.net returned ParsedFrequency.Unknown for an unrecognised value).
        if (p.Frequency == ParsedFrequency.Unknown) return "Unknown FREQ";

        // 2. BYSETPOS (e.g. "last Monday of month") — not representable in RecurrenceSpec.
        if (p.BySetPosition.Count > 0) return "BYSETPOS";

        // 3. RDATE — additional explicit dates outside of the RRULE pattern.
        if (p.RDates.Count > 0) return "RDATE";

        // 4. Multiple RRULE lines — we only handle a single rule.
        int rruleCount = lines.Count(l => l.StartsWith("RRULE", StringComparison.OrdinalIgnoreCase));
        if (rruleCount > 1) return "multiple RRULE";

        // 5. WKST when INTERVAL > 1 — the work-week start shifts grouping boundaries; degrade.
        bool hasWkst = lines.Any(l => l.Contains("WKST=", StringComparison.OrdinalIgnoreCase));
        if (hasWkst && p.Interval > 1) return "WKST interval-sensitive";

        // 5b. Negative or out-of-range BYMONTHDAY (e.g. -1 = last day of month; emitted by Google
        // Calendar) is not representable as a fixed day-of-month — (uint)(-1) would corrupt the
        // recurrence blob. Degrade to a single occurrence + warning.
        if (p.ByMonthDay.Count == 1 && p.ByMonthDay[0] is < 1 or > 31) return "unrepresentable BYMONTHDAY";

        // 6 & 7. Cardinality rules for Monthly / Yearly.
        if (p.Frequency == ParsedFrequency.Monthly)
        {
            // MonthlyNth: exactly one BYDAY with a supported offset (1..4 or -1).
            if (p.ByDay.Count == 1 && p.ByDay[0].Offset is { } off)
                return (off is >= 1 and <= 4 or -1) ? null : "unrepresentable monthly/yearly pattern";

            // Monthly-by-day: exactly one BYMONTHDAY, no BYDAY.
            if (p.ByMonthDay.Count == 1 && p.ByDay.Count == 0) return null;

            // Bare FREQ=MONTHLY (no BY* parts): RFC 5545 recurs on the DTSTART day-of-month.
            if (p.ByDay.Count == 0 && p.ByMonthDay.Count == 0) return null;

            return "unrepresentable monthly/yearly pattern";
        }

        if (p.Frequency == ParsedFrequency.Yearly)
        {
            // YearlyNth: one BYDAY with a supported offset (1..4 or -1) + one BYMONTH.
            if (p.ByDay.Count == 1 && p.ByDay[0].Offset is { } off && p.ByMonth.Count == 1)
                return (off is >= 1 and <= 4 or -1) ? null : "unrepresentable monthly/yearly pattern";

            // Yearly: one BYMONTH + one BYMONTHDAY, no BYDAY.
            if (p.ByMonth.Count == 1 && p.ByMonthDay.Count == 1 && p.ByDay.Count == 0)
                return null;

            // Bare FREQ=YEARLY (no BY* parts): RFC 5545 recurs on the DTSTART month + day-of-month.
            if (p.ByDay.Count == 0 && p.ByMonth.Count == 0 && p.ByMonthDay.Count == 0) return null;

            return "unrepresentable monthly/yearly pattern";
        }

        // Daily / Weekly — always mappable.
        return null;
    }

    /// <summary>Maps a <see cref="ParsedRecurrence"/> frequency to the model enum.
    /// <see cref="ComputeDegradeReason"/> must return <c>null</c> before this is called.</summary>
    private static RecurrenceFrequency MapFreq(ParsedRecurrence p) => p.Frequency switch
    {
        ParsedFrequency.Daily   => RecurrenceFrequency.Daily,
        ParsedFrequency.Weekly  => RecurrenceFrequency.Weekly,
        ParsedFrequency.Monthly =>
            p.ByDay.Count == 1 && p.ByDay[0].Offset is not null
                ? RecurrenceFrequency.MonthlyNth
                : RecurrenceFrequency.Monthly,
        ParsedFrequency.Yearly  =>
            p.ByDay.Count == 1 && p.ByDay[0].Offset is not null
                ? RecurrenceFrequency.YearlyNth
                : RecurrenceFrequency.Yearly,
        _ => throw new InvalidOperationException($"Unmapped ParsedFrequency: {p.Frequency}"),
    };

    /// <summary>
    /// Computes the UTC start of the last occurrence of a recurrence rule.
    /// Returns <c>null</c> for <see cref="RecurrenceEndKind.NoEnd"/>.
    /// For <see cref="RecurrenceEndKind.Until"/> returns <see cref="RecurrenceSpec.UntilUtc"/> directly.
    /// For <see cref="RecurrenceEndKind.Count"/> enumerates the COUNT-th raw occurrence
    /// (EXDATE does NOT reduce the COUNT).
    /// </summary>
    private static DateTime? ComputeLastInstanceUtc(RecurrenceSpec s)
    {
        switch (s.EndKind)
        {
            case RecurrenceEndKind.NoEnd:
                return null;

            case RecurrenceEndKind.Until:
                return s.UntilUtc;

            case RecurrenceEndKind.Count:
            {
                // Build a minimal VCALENDAR anchored at FirstStartLocal (floating — no TZ suffix)
                // so Ical.Net enumerates in local time without any IANA / Windows zone lookups.
                var anchor = s.FirstStartLocal;
                string dtFmt = anchor.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);
                string calText =
                    "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//ContinuMail//recurrence//EN\r\n" +
                    "BEGIN:VEVENT\r\nUID:count-enum@continmail\r\n" +
                    $"DTSTART:{dtFmt}\r\n" +
                    $"DTEND:{dtFmt}\r\n" +
                    $"RRULE:{s.RawRRuleBody}\r\n" +
                    "END:VEVENT\r\nEND:VCALENDAR\r\n";

                try
                {
                    var enumCal = IcalCalendar.Load(calText);
                    if (enumCal?.Events.Count == 0) return null;

                    var startDt = new CalDateTime(
                        anchor.Year, anchor.Month, anchor.Day,
                        anchor.Hour, anchor.Minute, anchor.Second, string.Empty);

                    var occs = enumCal.GetOccurrences(startDt)
                                      .OrderBy(o => o.Period.StartTime)
                                      .ToList();

                    int idx = (s.Count ?? 0) - 1;
                    if (idx < 0 || idx >= occs.Count) return null;

                    var lastLocalRaw = occs[idx].Period.StartTime.Value;
                    var lastLocal    = new DateTime(
                        lastLocalRaw.Year, lastLocalRaw.Month, lastLocalRaw.Day,
                        lastLocalRaw.Hour, lastLocalRaw.Minute, lastLocalRaw.Second,
                        DateTimeKind.Unspecified);

                    return s.TimeZone != null
                        ? TimeZoneInfo.ConvertTimeToUtc(lastLocal, s.TimeZone)
                        : DateTime.SpecifyKind(lastLocal, DateTimeKind.Utc);
                }
                catch
                {
                    return null;
                }
            }

            default:
                return null;
        }
    }
}
