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
    // Completing a recurring task triggers Outlook live-regeneration (advance current +
    // spawn a TaskDeadOccurrence=true copy) — a converter must not author that lifecycle.
    // Keep false: completed recurring tasks degrade to a single completed non-recurring task.
    private const bool CompletedRecurringSupported = false;

    public static TaskRecord? Map(RawTodoGroup group, out IReadOnlyList<string> warnings)
    {
        var w = new List<string>();
        warnings = w;

        RawTodo? master = group.Master;
        string title = master?.Title ?? "";
        if (master is null) { w.Add("orphan task override (no master) skipped"); return null; }
        // A task with RecurrenceId is an override row that got mis-grouped as a master; skip it.
        if (master.RecurrenceId is not null) { w.Add("task override row mis-grouped as master; skipped"); return null; }

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

        // PercentComplete — cal_todos.todo_complete when populated; otherwise fall back to the
        // PERCENT-COMPLETE cal_properties row, which is where Thunderbird's storage calendar
        // actually persists it (leaving the column NULL — observed on a real TB 140 profile).
        int percent = master.TodoComplete
            ?? (int.TryParse(PropValue(master, "PERCENT-COMPLETE"), out int pc) ? pc : 0);
        t.PercentComplete = Math.Clamp(percent, 0, 100);

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

        // --- Relations (cal_relations side-table: RELATED-TO etc.) ---
        // Classic Outlook has no native related-task surface; preserve raw relation lines
        // in the body appendix (via CalendarBodyAppendix in TaskWriter) + warn once per line.
        t.Relations = master.Relations
            .Select(r => r.IcalString)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
        foreach (var rel in t.Relations)
            w.Add($"relation on '{t.Subject}': preserved (not natively converted)");

        // Task attendees/assignment deferred: Outlook task assignment is a separate MAPI surface (not PR6).

        // Recurrence — map RRULE if present, degrade on exceptions/completions/unrepresentable rules.
        bool hasRrule = master.Recurrence.Any(s =>
            (s.IcalString ?? "").TrimStart().StartsWith("RRULE", StringComparison.OrdinalIgnoreCase));

        if (hasRrule)
        {
            bool hasExdateOrRdate = master.Recurrence.Any(s =>
                (s.IcalString ?? "").TrimStart().StartsWith("EXDATE", StringComparison.OrdinalIgnoreCase) ||
                (s.IcalString ?? "").TrimStart().StartsWith("RDATE",  StringComparison.OrdinalIgnoreCase));
            bool hasOverrides = group.Overrides.Count > 0;

            // Warning precedence (most-specific first): exceptions/deletions, then completed-recurring,
            // then rule-degrade. Short-circuit so only one reason is reported.
            if (hasExdateOrRdate || hasOverrides)
            {
                w.Add($"recurring task '{title}': deletions/exceptions not supported — wrote a single task");
            }
            else if (t.Status == TaskStatusKind.Complete && !CompletedRecurringSupported)
            {
                w.Add($"recurring task '{title}': completed recurring tasks not supported — wrote a single completed task");
            }
            else
            {
                // Prefer DueDate as anchor; fall back to StartDate.
                var anchor = t.DueDate ?? t.StartDate;
                if (anchor is null)
                {
                    w.Add($"recurring task '{title}': no due/start date to anchor recurrence — wrote a single task");
                }
                else
                {
                    // Task dates are DATE-ONLY; anchor at UTC-midnight of that calendar day (deterministic,
                    // consistent with PR4 which stores task dates as UTC-midnight).
                    var utcMidnight = new DateTime(
                        anchor.Value.Year, anchor.Value.Month, anchor.Value.Day,
                        0, 0, 0, DateTimeKind.Utc);
                    var lines = master.Recurrence
                        .Select(s => s.IcalString)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!)
                        .ToList();
                    var (spec, reason) = RecurrenceMapping.FromIcal(
                        lines, utcMidnight, utcMidnight, TimeZoneInfo.Utc, originatingTzId: null);
                    if (reason is not null)
                        w.Add($"recurring task '{title}': {reason}; wrote a single task");
                    else if (spec is null)
                        // hasRrule is true, so RRULE was present — (null,null) uniquely means parse failure.
                        w.Add($"recurring task '{title}': RRULE could not be parsed — wrote a single task");
                    else
                        t.Recurrence = spec;
                }
            }
        }

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
