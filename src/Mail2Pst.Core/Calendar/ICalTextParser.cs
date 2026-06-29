// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Ical.Net.DataTypes;

namespace Mail2Pst.Core.Calendar;

// ---------------------------------------------------------------------------
// Domain types for parsed recurrence data.
// ---------------------------------------------------------------------------

public enum ParsedFrequency { Unknown, Daily, Weekly, Monthly, Yearly }

public sealed record ParsedByDay(DayOfWeek DayOfWeek, int? Offset);

/// <summary>Structured representation of an EXDATE or RDATE line.</summary>
public sealed record ParsedDateList(
    string Raw,
    string? TzId,
    bool IsDateOnly,
    IReadOnlyList<string> Values);

/// <summary>Full parsed recurrence for one VEVENT.</summary>
public sealed record ParsedRecurrence(
    ParsedFrequency Frequency,
    string RawFrequency,
    int Interval,
    int Count,
    DateTime? UntilUtc,
    IReadOnlyList<ParsedByDay> ByDay,
    IReadOnlyList<int> ByMonth,
    IReadOnlyList<int> ByMonthDay,
    IReadOnlyList<ParsedDateList> ExDates,
    IReadOnlyList<ParsedDateList> RDates);

// ---------------------------------------------------------------------------
// ICalTextParser — first slice: ParseRecurrence.
// ---------------------------------------------------------------------------

public static class ICalTextParser
{
    /// <summary>
    /// Parses recurrence from a flat list of iCal property lines (already
    /// belonging to one VEVENT — no BEGIN/END envelope needed).
    /// Lines are individually unfolded before inspection.
    /// Returns <c>Value=null</c> (no warnings) when no RRULE line is present.
    /// </summary>
    public static ParseResult<ParsedRecurrence> ParseRecurrence(IReadOnlyList<string> icalLines)
    {
        string? rruleLine = null;
        var exDates = new List<ParsedDateList>();
        var rDates  = new List<ParsedDateList>();

        foreach (var raw in icalLines)
        {
            // Unfold each individual line (handles wrapped continuations).
            var line = IcalParseSupport.UnfoldIcalLines(raw);
            if (line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
            {
                rruleLine ??= line;   // first RRULE wins
            }
            else if (line.StartsWith("EXDATE", StringComparison.OrdinalIgnoreCase))
            {
                exDates.Add(ParseDateListLine(line));
            }
            else if (line.StartsWith("RDATE", StringComparison.OrdinalIgnoreCase))
            {
                rDates.Add(ParseDateListLine(line));
            }
        }

        // No RRULE — normal (single-occurrence event); return null value, no warning.
        if (rruleLine is null)
            return new ParseResult<ParsedRecurrence>(null, Array.Empty<string>());

        // Strip the "RRULE:" prefix to get the bare rule body.
        var ruleBody = rruleLine.Substring("RRULE:".Length);

        RecurrencePattern rr;
        try
        {
            rr = new RecurrencePattern(ruleBody);
        }
        catch (Exception ex)
        {
            return ParseResult<ParsedRecurrence>.Fail(
                $"RRULE parse failed: {ex.Message} (rule: {ruleBody})");
        }

        var freq = MapFrequency(rr.Frequency);

        var byDay = new List<ParsedByDay>(rr.ByDay?.Count ?? 0);
        if (rr.ByDay is not null)
        {
            foreach (var wd in rr.ByDay)
            {
                int? offset = wd.Offset == int.MinValue ? null : wd.Offset;
                byDay.Add(new ParsedByDay(wd.DayOfWeek, offset));
            }
        }

        var byMonth    = (IReadOnlyList<int>)(rr.ByMonth    is { Count: > 0 } bm  ? bm  : Array.Empty<int>());
        var byMonthDay = (IReadOnlyList<int>)(rr.ByMonthDay is { Count: > 0 } bmd ? bmd : Array.Empty<int>());

        DateTime? untilUtc = rr.Until is not null ? rr.Until.AsUtc : null;

        var recurrence = new ParsedRecurrence(
            Frequency:    freq,
            RawFrequency: rr.Frequency.ToString(),
            Interval:     rr.Interval,
            Count:        rr.Count ?? 0,
            UntilUtc:     untilUtc,
            ByDay:        byDay,
            ByMonth:      byMonth,
            ByMonthDay:   byMonthDay,
            ExDates:      exDates,
            RDates:       rDates);

        return ParseResult<ParsedRecurrence>.Ok(recurrence);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses a single EXDATE or RDATE line into a <see cref="ParsedDateList"/>.
    /// Format: NAME[;param;param]:value,value,...
    /// Recognised params: TZID=..., VALUE=DATE.
    /// </summary>
    internal static ParsedDateList ParseDateListLine(string line)
    {
        // Find the colon that separates property (name + params) from value.
        // A TZID param value may itself contain a colon on some broken TZIDs, but
        // RFC 5545 §3.1 says the first unescaped colon delimits the value.
        int colonIdx = line.IndexOf(':');
        if (colonIdx < 0)
        {
            // Malformed — return empty.
            return new ParsedDateList(line, null, false, Array.Empty<string>());
        }

        var propSection  = line.Substring(0, colonIdx);   // e.g. "EXDATE;TZID=Europe/Oslo;VALUE=DATE"
        var valueSection = line.Substring(colonIdx + 1);  // e.g. "20250516,20250523"

        // Split propSection on ';' — first token is the property name, rest are params.
        var parts = propSection.Split(';');

        string? tzId = null;
        bool isDateOnly = false;

        for (int i = 1; i < parts.Length; i++)
        {
            var param = parts[i];
            if (param.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))
                tzId = param.Substring("TZID=".Length);
            else if (param.Equals("VALUE=DATE", StringComparison.OrdinalIgnoreCase))
                isDateOnly = true;
        }

        var values = valueSection.Length > 0
            ? (IReadOnlyList<string>)valueSection.Split(',')
            : Array.Empty<string>();

        return new ParsedDateList(line, tzId, isDateOnly, values);
    }

    private static ParsedFrequency MapFrequency(Ical.Net.FrequencyType ft) => ft switch
    {
        Ical.Net.FrequencyType.Daily   => ParsedFrequency.Daily,
        Ical.Net.FrequencyType.Weekly  => ParsedFrequency.Weekly,
        Ical.Net.FrequencyType.Monthly => ParsedFrequency.Monthly,
        Ical.Net.FrequencyType.Yearly  => ParsedFrequency.Yearly,
        _                              => ParsedFrequency.Unknown,
    };
}
