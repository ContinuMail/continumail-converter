// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;

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
    IReadOnlyList<int> BySetPosition,
    IReadOnlyList<ParsedDateList> ExDates,
    IReadOnlyList<ParsedDateList> RDates);

// ---------------------------------------------------------------------------
// Domain types for attendees and alarms.
// ---------------------------------------------------------------------------

/// <summary>One participant (attendee or organizer) from a VEVENT.</summary>
public sealed record ParsedAttendee(
    string? CommonName,
    string? Email,
    string? Role,
    string? ParticipationStatus,
    string? CuType,
    bool IsOrganizer);

/// <summary>Parsed VALARM for a VEVENT.</summary>
public sealed record ParsedAlarm(
    string? Action,
    TimeSpan? RelativeOffset,
    DateTime? AbsoluteTimeUtc,
    string? Related,
    string? Description);

// ---------------------------------------------------------------------------
// Domain type for parsed attachment data.
// ---------------------------------------------------------------------------

/// <summary>
/// Represents a parsed ATTACH property.  Exactly one of <see cref="Uri"/> or
/// <see cref="InlineData"/> is non-null.
/// </summary>
public sealed record ParsedAttachment(
    string? Uri,
    byte[]? InlineData,
    string? FileName,
    string? FormatType);

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
            // Unfold each individual line (handles wrapped continuations), then strip any trailing
            // line terminator. Mozilla's cal_recurrence stores each property with a trailing CRLF;
            // left in place it corrupts the final RRULE token (Ical.Net rejects "SU\r\n") and every
            // EXDATE/RDATE value (the date parse fails and the deletion is silently lost).
            var line = IcalParseSupport.UnfoldIcalLines(raw).Trim();
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

        try
        {
            var rr = new RecurrencePattern(ruleBody);

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

            var byMonth      = (IReadOnlyList<int>)(rr.ByMonth      is { Count: > 0 } bm  ? bm  : Array.Empty<int>());
            var byMonthDay   = (IReadOnlyList<int>)(rr.ByMonthDay   is { Count: > 0 } bmd ? bmd : Array.Empty<int>());
            var bySetPosition = (IReadOnlyList<int>)(rr.BySetPosition is { Count: > 0 } bsp ? bsp.ToList() : Array.Empty<int>());

            DateTime? untilUtc = rr.Until is not null ? rr.Until.AsUtc : null;

            var recurrence = new ParsedRecurrence(
                Frequency:     freq,
                RawFrequency:  rr.Frequency.ToString(),
                Interval:      rr.Interval,
                Count:         rr.Count ?? 0,
                UntilUtc:      untilUtc,
                ByDay:         byDay,
                ByMonth:       byMonth,
                ByMonthDay:    byMonthDay,
                BySetPosition: bySetPosition,
                ExDates:       exDates,
                RDates:        rDates);

            return ParseResult<ParsedRecurrence>.Ok(recurrence);
        }
        catch (Exception ex)
        {
            return ParseResult<ParsedRecurrence>.Fail(
                $"RRULE parse failed: {ex.Message} (rule: {ruleBody})");
        }
    }

    /// <summary>
    /// Parses ATTENDEE and ORGANIZER lines from a flat list of iCal property lines.
    /// All lines are wrapped into one VEVENT and loaded together so that attendees
    /// and the organizer coexist in one calendar event.  On load failure the method
    /// falls back to loading each line individually, emitting a warning per failure.
    /// The returned <c>Value</c> is never null — it may be an empty or partial list.
    /// </summary>
    public static ParseResult<IReadOnlyList<ParsedAttendee>> ParseAttendees(IReadOnlyList<string> icalLines)
    {
        var warnings = new List<string>();
        var result   = new List<ParsedAttendee>();

        // Attempt: load all lines in one VEVENT so organizer + attendees coexist.
        var combined = string.Join("\r\n", icalLines);
        try
        {
            var cal = IcalCalendar.Load(IcalParseSupport.WrapVevent(combined));
            var evt = cal?.Events.Count > 0 ? cal.Events[0] : null;
            if (evt is not null)
                ExtractAttendeesFromEvent(evt, result);
        }
        catch (Exception ex)
        {
            warnings.Add($"Combined ATTENDEE/ORGANIZER load failed, falling back to per-line: {ex.Message}");

            // Per-line fallback — collect whatever parses.
            foreach (var line in icalLines)
            {
                try
                {
                    var cal = IcalCalendar.Load(IcalParseSupport.WrapVevent(line));
                    var evt = cal?.Events.Count > 0 ? cal.Events[0] : null;
                    if (evt is not null)
                        ExtractAttendeesFromEvent(evt, result);
                }
                catch (Exception lineEx)
                {
                    warnings.Add($"Attendee line parse failed: {lineEx.Message} (line: {line})");
                }
            }
        }

        return new ParseResult<IReadOnlyList<ParsedAttendee>>(result, warnings);
    }

    /// <summary>
    /// Parses a raw VALARM block (BEGIN:VALARM … END:VALARM) into a
    /// <see cref="ParsedAlarm"/>.  The block is wrapped in a VEVENT so that
    /// relative triggers resolve correctly.
    /// Returns <c>Value=null</c> with a warning when loading fails or the
    /// alarm list is empty.
    /// </summary>
    public static ParseResult<ParsedAlarm> ParseAlarm(string valarmBlock)
    {
        try
        {
            var cal = IcalCalendar.Load(IcalParseSupport.WrapVevent(valarmBlock));
            var evt = cal?.Events.Count > 0 ? cal.Events[0] : null;

            if (evt is null)
                return ParseResult<ParsedAlarm>.Fail("VALARM: no VEVENT found after wrapping.");

            if (evt.Alarms is null || evt.Alarms.Count == 0)
                return ParseResult<ParsedAlarm>.Fail("VALARM: alarm list is empty.");

            var alarm = evt.Alarms[0];

            // Trigger.Related is a TriggerRelation enum (Start/End); normalise to
            // uppercase to match the raw iCal convention ("START"/"END").
            string? related = alarm.Trigger is null
                ? null
                : alarm.Trigger.Related.ToString().ToUpperInvariant();

            // Trigger.Duration is Ical.Net.DataTypes.Duration (struct).
            // ToTimeSpanUnspecified() converts weeks/days/hours/minutes/seconds without
            // needing a CalDateTime anchor (safe for alarm offsets which never use months/years).
            TimeSpan? relativeOffset = alarm.Trigger?.Duration is { } dur
                ? dur.ToTimeSpanUnspecified()
                : (TimeSpan?)null;

            var parsed = new ParsedAlarm(
                Action:          alarm.Action,
                RelativeOffset:  relativeOffset,
                AbsoluteTimeUtc: alarm.Trigger?.DateTime?.AsUtc,
                Related:         related,
                Description:     alarm.Description);

            return ParseResult<ParsedAlarm>.Ok(parsed);
        }
        catch (Exception ex)
        {
            return ParseResult<ParsedAlarm>.Fail($"VALARM parse failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a single raw ATTACH property line and classifies the attachment
    /// as a URI link, inline binary (ENCODING=BASE64;VALUE=BINARY), or a
    /// <c>data:</c> URI decoded to inline bytes.
    /// Returns <c>Value=null</c> with a warning when loading or classification fails.
    /// </summary>
    public static ParseResult<ParsedAttachment> ParseAttachment(string attachLine)
    {
        Ical.Net.CalendarComponents.CalendarEvent? evt;
        try
        {
            var cal = IcalCalendar.Load(IcalParseSupport.WrapVevent(attachLine));
            evt = cal?.Events.Count > 0 ? cal.Events[0] : null;
        }
        catch (Exception ex)
        {
            return ParseResult<ParsedAttachment>.Fail($"ATTACH parse failed: {ex.Message}");
        }

        if (evt is null)
            return ParseResult<ParsedAttachment>.Fail("ATTACH: no VEVENT found after wrapping.");

        if (evt.Attachments is null || evt.Attachments.Count == 0)
            return ParseResult<ParsedAttachment>.Fail("ATTACH: no ATTACH property found in VEVENT.");

        var att = evt.Attachments[0];

        var fileName   = att.Parameters?.Get("FILENAME");
        var formatType = att.FormatType;

        try
        {
            // Case 1: inline binary — Ical.Net decoded ENCODING=BASE64;VALUE=BINARY into att.Data.
            if (att.Data is not null)
            {
                return ParseResult<ParsedAttachment>.Ok(
                    new ParsedAttachment(null, att.Data, fileName, formatType));
            }

            // Case 2: data: URI — Ical.Net leaves the raw URI in att.Uri without decoding.
            var uriStr = att.Uri?.ToString();
            if (uriStr is not null &&
                uriStr.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (!IcalDataUri.TryDecode(uriStr, out var mediaType, out var inlineData))
                    return ParseResult<ParsedAttachment>.Fail($"ATTACH: malformed or undecodable data: URI: {uriStr}");

                // Use the data: media type as FormatType when Ical.Net didn't provide one.
                var resolvedFormatType = string.IsNullOrEmpty(formatType) && !string.IsNullOrEmpty(mediaType)
                    ? mediaType
                    : formatType;

                return ParseResult<ParsedAttachment>.Ok(
                    new ParsedAttachment(null, inlineData, fileName, resolvedFormatType));
            }

            // Case 3: remote/external URI.
            if (uriStr is not null)
            {
                return ParseResult<ParsedAttachment>.Ok(
                    new ParsedAttachment(uriStr, null, fileName, formatType));
            }

            return ParseResult<ParsedAttachment>.Fail("ATTACH: no data, no URI — unrecognised attachment.");
        }
        catch (Exception ex)
        {
            return ParseResult<ParsedAttachment>.Fail($"ATTACH classification failed: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts attendees and the organizer from a calendar event, appending
    /// <see cref="ParsedAttendee"/> entries to <paramref name="target"/>.
    /// </summary>
    private static void ExtractAttendeesFromEvent(
        Ical.Net.CalendarComponents.CalendarEvent evt,
        List<ParsedAttendee> target)
    {
        if (evt.Attendees is not null)
        {
            foreach (var att in evt.Attendees)
            {
                target.Add(new ParsedAttendee(
                    CommonName:          att.CommonName,
                    Email:               ExtractEmail(att.Value),
                    Role:                att.Role,
                    ParticipationStatus: att.ParticipationStatus,
                    CuType:              att.Parameters?.Get("CUTYPE"),
                    IsOrganizer:         false));
            }
        }

        if (evt.Organizer is not null)
        {
            target.Add(new ParsedAttendee(
                CommonName:          evt.Organizer.CommonName,
                Email:               ExtractEmail(evt.Organizer.Value),
                Role:                null,
                ParticipationStatus: null,
                CuType:              null,
                IsOrganizer:         true));
        }
    }

    /// <summary>
    /// Strips a leading <c>mailto:</c> scheme (case-insensitive) and
    /// percent-decodes the remainder to get a plain e-mail address.
    /// </summary>
    private static string? ExtractEmail(Uri? uri)
    {
        if (uri is null) return null;
        var s = uri.ToString();
        if (s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            s = s.Substring("mailto:".Length);
        return Uri.UnescapeDataString(s);
    }

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
