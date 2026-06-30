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

public static class CalendarTaskMapper
{
    public static TaskRecord? Map(RawTodoGroup group, out IReadOnlyList<string> warnings)
    {
        var w = new List<string>();
        warnings = w;

        RawTodo? master = group.Master;
        string title = master?.Title ?? "";
        if (master is null) { w.Add("orphan task override (no master) skipped"); return null; }
        if (group.Overrides.Count > 0) { w.Add($"recurring task '{title}' with exceptions deferred to PR7"); return null; }
        if (master.Recurrence.Count > 0) { w.Add($"recurring task '{title}' deferred to PR7"); return null; }
        if (master.RecurrenceId is not null) { w.Add("task override row deferred to PR7"); return null; }

        var t = new TaskRecord { Subject = title, SourceId = master.Id ?? "" };

        // Body (DESCRIPTION property)
        t.Body = PropValue(master, "DESCRIPTION");

        // Categories: split on unescaped commas, unescape \,, drop X-MOZ-* keys (CATEGORIES only)
        var cats = PropValue(master, "CATEGORIES");
        t.Categories = cats is not null ? SplitCategories(cats) : Array.Empty<string>();

        // Status
        t.Status = master.IcalStatus?.ToUpperInvariant() switch
        {
            "COMPLETED"  => TaskStatusKind.Complete,
            "IN-PROCESS" => TaskStatusKind.InProgress,
            "CANCELLED"  => TaskStatusKind.Deferred,
            _            => TaskStatusKind.NotStarted, // NEEDS-ACTION, null, unknown
        };

        // PercentComplete
        t.PercentComplete = Math.Clamp(master.TodoComplete ?? 0, 0, 100);

        // Status/percent invariants (Outlook treats these as coupled)
        if (t.PercentComplete == 100)
            t.Status = TaskStatusKind.Complete;
        if (t.Status == TaskStatusKind.Complete && t.PercentComplete < 100)
            t.PercentComplete = 100;

        // Dates
        t.StartDate     = PrTime.FromMicros(master.TodoEntry);
        t.DueDate       = PrTime.FromMicros(master.TodoDue);
        t.CompletedDate = PrTime.FromMicros(master.TodoCompleted);

        // Importance from iCal PRIORITY column
        t.Importance = master.Priority switch
        {
            >= 1 and <= 4 => 2, // high
            >= 6 and <= 9 => 0, // low
            _             => 1, // normal (5 or null)
        };

        // Sensitivity from CLASS (Privacy column first, then CLASS property)
        var cls = master.Privacy ?? PropValue(master, "CLASS");
        t.Sensitivity = cls?.ToUpperInvariant() switch
        {
            "PRIVATE"      => 2,
            "CONFIDENTIAL" => 3,
            _              => 0,
        };

        // Reminder — map first VALARM; warn if multiple alarms present
        if (master.Alarms.Count > 1)
            w.Add($"task '{title}': multiple Thunderbird alarms — only the first is converted");

        if (master.Alarms.Count > 0)
        {
            var rawBlock = master.Alarms[0].IcalString ?? "";
            var alarmResult = ICalTextParser.ParseAlarm(rawBlock);

            foreach (var aw in alarmResult.Warnings)
                w.Add(aw);

            if (alarmResult.Value is { } alarm)
            {
                if (alarm.AbsoluteTimeUtc is { } absUtc)
                {
                    // Absolute trigger
                    t.ReminderSet  = true;
                    t.ReminderTime = new DateTimeOffset(absUtc, TimeSpan.Zero);
                }
                else if (alarm.RelativeOffset is { } offset)
                {
                    // Relative trigger — resolve anchor
                    DateTimeOffset? anchor = alarm.Related switch
                    {
                        "START" => t.StartDate,
                        "END"   => t.DueDate,
                        _       => t.DueDate ?? t.StartDate, // default: DueDate ?? StartDate
                    };

                    if (anchor is null)
                    {
                        w.Add($"task '{title}': alarm has no anchor date — not converted");
                        AppendRawTriggerToBody(t, rawBlock);
                    }
                    else if (offset < TimeSpan.Zero)
                    {
                        // Negative offset — fires before anchor; valid reminder
                        t.ReminderSet  = true;
                        t.ReminderTime = anchor.Value + offset;
                    }
                    else
                    {
                        // Zero or positive — fires at/after anchor; skip reminder
                        w.Add($"task '{title}': reminder fires at/after the anchor — not converted");
                        AppendRawTriggerToBody(t, rawBlock);
                    }
                }
            }
            else
            {
                // Parse failed; warnings already added above; preserve raw trigger in body.
                AppendRawTriggerToBody(t, rawBlock);
            }
        }

        // Task attendees/assignment deferred: Outlook task assignment is a separate MAPI surface (not PR6).

        return t;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static string? PropValue(RawTodo todo, string key)
    {
        RawProperty? p = todo.Properties.FirstOrDefault(
            x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return p?.Value is { } b ? Encoding.UTF8.GetString(b) : null;
    }

    /// <summary>
    /// Splits a CATEGORIES value on commas that are NOT escaped by a preceding backslash,
    /// then unescapes each token's \, sequences.  Trims whitespace and drops empty tokens.
    /// </summary>
    private static IReadOnlyList<string> SplitCategories(string raw)
    {
        // Lookbehind for backslash: split on commas not preceded by '\'
        var parts = Regex.Split(raw, @"(?<!\\),");
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
    /// "not converted" notice to the task body.
    /// </summary>
    private static void AppendRawTriggerToBody(TaskRecord t, string rawBlock)
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
            t.Body = (t.Body ?? "") + $"\n[Thunderbird alarm not converted: {triggerLine}]";
    }
}
