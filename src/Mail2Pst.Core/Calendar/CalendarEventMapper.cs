// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Calendar;

public static class CalendarEventMapper
{
    private const int EventAllDay = 4;

    public static AppointmentRecord? Map(RawEventGroup group, out IReadOnlyList<string> warnings)
    {
        var w = new List<string>();
        warnings = w;

        RawEvent? master = group.Master;
        string title = master?.Title ?? "";

        // Group-skip rules (order matters)
        if (master is null)
        {
            w.Add("orphan event override (no master) skipped");
            return null;
        }
        if (group.Overrides.Count > 0)
        {
            w.Add($"recurring event '{title}' with exceptions deferred to PR7");
            return null;
        }
        if (master.Recurrence.Count > 0)
        {
            w.Add($"recurring event '{title}' deferred to PR7");
            return null;
        }
        if (master.RecurrenceId is not null)
        {
            w.Add("event override row deferred to PR7");
            return null;
        }

        var appt = new AppointmentRecord
        {
            Subject  = title,
            SourceId = master.Id ?? "",
        };

        // All-day flag
        bool isAllDay = (master.Flags & EventAllDay) != 0;
        appt.IsAllDay = isAllDay;

        // Timezone resolution — always resolve from EventStartTz
        var rz = TimeZoneResolver.Resolve(master.EventStartTz);

        if (rz.Warning is { } rzWarn)
            w.Add(rzWarn);

        TimeZoneInfo? resolvedZone;
        if (rz.Zone != null && !rz.IsFloating)
        {
            resolvedZone   = rz.Zone;
            appt.TimeZone  = rz.Zone;
        }
        else if (isAllDay)
        {
            // All-day needs a zone for midnight boundaries; UTC is deterministic
            resolvedZone   = TimeZoneInfo.Utc;
            appt.TimeZone  = TimeZoneInfo.Utc;
            if (rz.Zone == null || rz.IsFloating)
                w.Add($"all-day event '{title}': unresolved timezone — using UTC for date boundaries");
        }
        else
        {
            // Timed floating/unresolved — keep UTC instant, no display zone
            resolvedZone   = null;
            appt.TimeZone  = null;
            w.Add($"event '{title}': floating/unresolved timezone — stored as a fixed UTC instant");
        }

        // Start / End
        if (isAllDay)
        {
            // All-day: interpret the raw micros as a calendar date in resolvedZone,
            // then compute midnight boundaries.
            var startOffset = PrTime.FromMicros(master.EventStart);
            var endOffset   = PrTime.FromMicros(master.EventEnd);

            var tz = resolvedZone ?? TimeZoneInfo.Utc;

            // Convert UTC instant to local date in tz
            if (startOffset is null)
                w.Add($"all-day event '{title}': missing start — using sentinel date");
            var startLocalDate = TimeZoneInfo.ConvertTimeFromUtc(
                startOffset?.UtcDateTime ?? default, tz).Date;

            // Build local midnight
            var startLocalMidnight = new DateTime(
                startLocalDate.Year, startLocalDate.Month, startLocalDate.Day,
                0, 0, 0, DateTimeKind.Unspecified);

            appt.StartUtc = TimeZoneInfo.ConvertTimeToUtc(startLocalMidnight, tz);

            // Compute end local midnight
            DateTime? endLocalMidnight = null;
            if (endOffset is { } eo)
            {
                var endLocalDate = TimeZoneInfo.ConvertTimeFromUtc(eo.UtcDateTime, tz).Date;
                endLocalMidnight = new DateTime(
                    endLocalDate.Year, endLocalDate.Month, endLocalDate.Day,
                    0, 0, 0, DateTimeKind.Unspecified);
            }

            // If end <= start (in local) or end is null → one-day boundary (DST-aware: next local midnight in tz).
            if (endLocalMidnight is null || endLocalMidnight.Value <= startLocalMidnight)
            {
                var startLocalDate2 = TimeZoneInfo.ConvertTimeFromUtc(appt.StartUtc, tz).Date;
                var nextLocalMidnight = new DateTime(
                    startLocalDate2.Year, startLocalDate2.Month, startLocalDate2.Day,
                    0, 0, 0, DateTimeKind.Unspecified).AddDays(1);
                appt.EndUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, tz);
            }
            else
                appt.EndUtc = TimeZoneInfo.ConvertTimeToUtc(endLocalMidnight.Value, tz);
        }
        else
        {
            // Timed events
            appt.StartUtc = PrTime.FromMicros(master.EventStart)?.UtcDateTime ?? default;
            appt.EndUtc   = PrTime.FromMicros(master.EventEnd)?.UtcDateTime   ?? appt.StartUtc;

            if (appt.EndUtc < appt.StartUtc)
            {
                w.Add($"event '{title}': end precedes start — clamped to start");
                appt.EndUtc = appt.StartUtc;
            }
            else if (appt.EndUtc == appt.StartUtc)
            {
                w.Add($"event '{title}': zero-length event");
            }
        }

        // Body (DESCRIPTION property)
        appt.Body = PropValue(master, "DESCRIPTION");

        // BodyHtml — ALTREP data:text/html on DESCRIPTION parameter
        var altrep = master.Parameters.FirstOrDefault(p =>
            string.Equals(p.Key1, "DESCRIPTION", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Key2, "ALTREP",      StringComparison.OrdinalIgnoreCase));

        if (altrep?.Value is { } altrepUri &&
            altrepUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
            IcalDataUri.TryDecode(altrepUri, out var altrepMediaType, out var altrepBytes) &&
            altrepMediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            appt.BodyHtml = Encoding.UTF8.GetString(altrepBytes);
        }

        // Location
        appt.Location = PropValue(master, "LOCATION");

        // Categories
        var cats = PropValue(master, "CATEGORIES");
        appt.Categories = cats is not null ? SplitCategories(cats) : Array.Empty<string>();

        // BusyStatus (explicit precedence)
        // 1. TENTATIVE status → 1 (Tentative)
        // 2. TRANSP=TRANSPARENT → 0 (Free)
        // 3. TRANSP=OPAQUE → 2 (Busy) — explicit wins over all-day default
        // 4. no explicit TRANSP → all-day defaults to 0 (Free); timed defaults to 2 (Busy)
        var transp = PropValue(master, "TRANSP");
        if (string.Equals(master.IcalStatus, "TENTATIVE", StringComparison.OrdinalIgnoreCase))
            appt.BusyStatus = 1;
        else if (string.Equals(transp, "TRANSPARENT", StringComparison.OrdinalIgnoreCase))
            appt.BusyStatus = 0;
        else if (string.Equals(transp, "OPAQUE", StringComparison.OrdinalIgnoreCase))
            appt.BusyStatus = 2;
        else
            appt.BusyStatus = isAllDay ? 0 : 2; // all-day events default to Free; timed events default to Busy

        // Importance from iCal PRIORITY
        appt.Importance = master.Priority switch
        {
            >= 1 and <= 4 => 2, // high
            >= 6 and <= 9 => 0, // low
            _             => 1, // normal (5 or null)
        };

        // Sensitivity from CLASS (Privacy column first, then CLASS property)
        var cls = master.Privacy ?? PropValue(master, "CLASS");
        appt.Sensitivity = cls?.ToUpperInvariant() switch
        {
            "PRIVATE"      => 2,
            "CONFIDENTIAL" => 3,
            _              => 0,
        };

        // Reminder — map first VALARM; warn if multiple alarms present
        if (master.Alarms.Count > 1)
            w.Add($"event '{title}': multiple Thunderbird alarms — only the first is converted");

        if (master.Alarms.Count > 0)
        {
            var rawBlock   = master.Alarms[0].IcalString ?? "";
            var alarmResult = ICalTextParser.ParseAlarm(rawBlock);

            foreach (var aw in alarmResult.Warnings)
                w.Add(aw);

            if (alarmResult.Value is { } alarm)
            {
                if (alarm.AbsoluteTimeUtc is { } absUtc)
                {
                    // Absolute trigger — compute minutes before StartUtc
                    appt.ReminderSet = true;
                    appt.ReminderMinutesBefore =
                        (int)Math.Max(0, Math.Round((appt.StartUtc - absUtc).TotalMinutes));
                }
                else if (alarm.RelativeOffset is { } offset)
                {
                    // Relative trigger — anchor by Related
                    DateTime anchor = alarm.Related switch
                    {
                        "END"   => appt.EndUtc,
                        _       => appt.StartUtc, // START or default
                    };

                    if (offset < TimeSpan.Zero)
                    {
                        // Negative offset — fires before anchor; valid reminder
                        appt.ReminderSet = true;
                        appt.ReminderMinutesBefore = (int)Math.Round(-offset.TotalMinutes);
                    }
                    else
                    {
                        // Zero or positive — fires at/after anchor; skip reminder
                        w.Add($"event '{title}': reminder fires at/after the anchor — not converted");
                        AppendRawTriggerToBody(appt, rawBlock);
                    }
                }
            }
            else
            {
                // Parse failed; warnings already added above; preserve raw trigger in body.
                AppendRawTriggerToBody(appt, rawBlock);
            }
        }

        return appt;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static string? PropValue(RawEvent ev, string key)
    {
        RawProperty? p = ev.Properties.FirstOrDefault(
            x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return p?.Value is { } b ? Encoding.UTF8.GetString(b) : null;
    }

    /// <summary>
    /// Splits a CATEGORIES value on commas that are NOT escaped by a preceding backslash,
    /// then unescapes each token's \, sequences.  Trims whitespace and drops empty tokens
    /// and X-MOZ-* prefixed categories.
    /// </summary>
    private static IReadOnlyList<string> SplitCategories(string raw)
    {
        var parts  = Regex.Split(raw, @"(?<!\\),");
        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var cat = part.Replace("\\,", ",").Trim();
            if (!string.IsNullOrEmpty(cat) &&
                !cat.StartsWith("X-MOZ-", StringComparison.OrdinalIgnoreCase))
                result.Add(cat);
        }
        return result;
    }

    /// <summary>
    /// Extracts the raw TRIGGER line from a VALARM block and appends a
    /// "not converted" notice to the appointment body.
    /// </summary>
    private static void AppendRawTriggerToBody(AppointmentRecord appt, string rawBlock)
    {
        string? triggerLine = null;
        foreach (var line in rawBlock.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (trimmed.StartsWith("TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                triggerLine = trimmed;
                break;
            }
        }
        if (triggerLine is not null)
            appt.Body = (appt.Body ?? "") + $"\n[Thunderbird alarm not converted: {triggerLine}]";
    }
}
