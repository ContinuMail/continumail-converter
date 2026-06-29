// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Mail2Pst.Core.Calendar;

// ---------------------------------------------------------------------------
// ParseResult<T> — lightweight discriminated union for per-item parse outcomes.
// Ok carries the value; Fail carries a warning and a null value.
// Multiple warnings can accumulate (e.g. partial parse with fallbacks).
// ---------------------------------------------------------------------------

public sealed record ParseResult<T>(T? Value, IReadOnlyList<string> Warnings)
{
    /// <summary>Successful parse — value present, no warnings.</summary>
    public static ParseResult<T> Ok(T value) =>
        new(value, Array.Empty<string>());

    /// <summary>Failed parse — null value, single warning message.</summary>
    public static ParseResult<T> Fail(string warning) =>
        new(default, new[] { warning });
}

// ---------------------------------------------------------------------------
// IcalParseSupport — low-level iCal text helpers used by the event mapper.
// ---------------------------------------------------------------------------

public static class IcalParseSupport
{
    /// <summary>
    /// RFC-5545 line unfolding: a line that begins with a SPACE or HTAB is a
    /// continuation of the previous logical line; the leading whitespace is
    /// stripped and the remainder is joined directly (no separator).
    /// Normalises all line endings (\r\n, \r, \n) before folding.
    /// </summary>
    public static string UnfoldIcalLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Normalise to bare \n so splitting is trivial.
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var result = new StringBuilder(normalised.Length);
        bool first = true;
        foreach (var line in normalised.Split('\n'))
        {
            if (!first && line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                // Continuation: strip leading whitespace, join onto previous.
                result.Append(line, 1, line.Length - 1);
            }
            else
            {
                if (!first)
                    result.Append("\r\n");
                result.Append(line);
                first = false;
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Wraps iCal body lines (e.g. a bare SUMMARY/DTSTART snippet) in a
    /// minimal but loadable VCALENDAR + VEVENT envelope.  DTSTART is
    /// included so VALARM relative triggers resolve without errors.
    /// </summary>
    public static string WrapVevent(string bodyLines)
    {
        return
            "BEGIN:VCALENDAR\r\n" +
            "VERSION:2.0\r\n" +
            "PRODID:-//ContinuMail//iCal Fragment Parser//EN\r\n" +
            "BEGIN:VEVENT\r\n" +
            "UID:fragment@example.com\r\n" +
            "DTSTART:20260101T000000Z\r\n" +
            bodyLines + "\r\n" +
            "END:VEVENT\r\n" +
            "END:VCALENDAR\r\n";
    }
}
