// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

/// <summary>
/// Unit tests for <see cref="CalendarTaskMapper.Map"/>.
/// All data is synthetic/reserved (example.com, example.org) — no real mail or PII.
/// </summary>
public class CalendarTaskMapperTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Returns PRTime microseconds for a UTC date.</summary>
    private static long MicrosFor(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static RawTodoGroup SimpleGroup(Action<RawTodo>? configure = null)
    {
        var todo = new RawTodo
        {
            Id       = "task-example-001@example.com",
            Title    = "Example Task",
            IcalStatus  = "NEEDS-ACTION",
            TodoComplete = 0,
            Priority = 5,
            Privacy  = null,
        };
        configure?.Invoke(todo);
        return new RawTodoGroup { Master = todo };
    }

    // -----------------------------------------------------------------------
    // Group-skip rules
    // -----------------------------------------------------------------------

    [Fact]
    public void NullMaster_ReturnsNullWithOrphanWarning()
    {
        var group = new RawTodoGroup(); // Master == null
        var result = CalendarTaskMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("orphan task override (no master) skipped", warnings[0]);
    }

    [Fact]
    public void GroupWithOverrides_ReturnsNullWithRecurringWarning()
    {
        var group = new RawTodoGroup
        {
            Master = new RawTodo { Id = "t1@example.com", Title = "Weekly Review" },
            Overrides = new List<RawTodo> { new RawTodo { Id = "t1@example.com", Title = "Weekly Review" } }
        };
        var result = CalendarTaskMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("recurring task 'Weekly Review' with exceptions deferred to PR7", warnings[0]);
    }

    [Fact]
    public void MasterWithRecurrenceLine_ReturnsNullWithRecurringWarning()
    {
        var group = SimpleGroup(t =>
        {
            t.Title = "Recurring Task";
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO"));
        });
        var result = CalendarTaskMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("recurring task 'Recurring Task' deferred to PR7", warnings[0]);
    }

    [Fact]
    public void MasterWithRecurrenceIdSet_ReturnsNullWithOverrideWarning()
    {
        var group = SimpleGroup(t =>
        {
            t.RecurrenceId = MicrosFor(2026, 7, 7);
        });
        var result = CalendarTaskMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("task override row deferred to PR7", warnings[0]);
    }

    // -----------------------------------------------------------------------
    // Flat non-recurring master → populated TaskRecord
    // -----------------------------------------------------------------------

    [Fact]
    public void FlatMaster_MapsBasicFieldsToTaskRecord()
    {
        var group = SimpleGroup(t =>
        {
            t.Title       = "Buy groceries";
            t.Id          = "groceries-123@example.org";
            t.IcalStatus  = "NEEDS-ACTION";
            t.TodoComplete = 0;
            t.Priority    = 5;
            t.Privacy     = null;
            t.TodoEntry   = MicrosFor(2026, 7, 1);
            t.TodoDue     = MicrosFor(2026, 7, 5);
            t.Properties.Add(new RawProperty("DESCRIPTION", Utf8("Pick up milk and bread."), null, null));
            t.Properties.Add(new RawProperty("CATEGORIES",  Utf8("Shopping,Home"),            null, null));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Empty(warnings);
        Assert.Equal("groceries-123@example.org", task.SourceId);
        Assert.Equal("Buy groceries", task.Subject);
        Assert.Equal("Pick up milk and bread.", task.Body);
        Assert.Equal(new[] { "Shopping", "Home" }, task.Categories);
        Assert.Equal(TaskStatusKind.NotStarted, task.Status);
        Assert.Equal(0, task.PercentComplete);
        Assert.Equal(1, task.Importance);   // priority 5 → normal
        Assert.Equal(0, task.Sensitivity);  // null/PUBLIC → 0
        Assert.NotNull(task.StartDate);
        Assert.NotNull(task.DueDate);
        Assert.Null(task.CompletedDate);
        Assert.False(task.ReminderSet);
    }

    // -----------------------------------------------------------------------
    // Status mapping
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("COMPLETED",   TaskStatusKind.Complete,    100)]
    [InlineData("IN-PROCESS",  TaskStatusKind.InProgress,   50)]
    [InlineData("CANCELLED",   TaskStatusKind.Deferred,      0)]
    [InlineData("NEEDS-ACTION",TaskStatusKind.NotStarted,    0)]
    [InlineData(null,          TaskStatusKind.NotStarted,    0)]
    public void StatusMapping_MatchesIcalStatus(string? icalStatus, TaskStatusKind expected, int percent)
    {
        var group = SimpleGroup(t =>
        {
            t.IcalStatus  = icalStatus;
            t.TodoComplete = percent;
        });
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(expected, task.Status);
    }

    // -----------------------------------------------------------------------
    // Status/percent invariants
    // -----------------------------------------------------------------------

    [Fact]
    public void PercentComplete100_ForcesStatusComplete()
    {
        var group = SimpleGroup(t =>
        {
            t.IcalStatus   = "NEEDS-ACTION"; // would be NotStarted
            t.TodoComplete = 100;
        });
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(TaskStatusKind.Complete, task.Status);
        Assert.Equal(100, task.PercentComplete);
    }

    [Fact]
    public void StatusComplete_WithLowPercent_ForcesPercent100()
    {
        var group = SimpleGroup(t =>
        {
            t.IcalStatus   = "COMPLETED";
            t.TodoComplete = 0; // inconsistent — should be forced to 100
        });
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(TaskStatusKind.Complete, task.Status);
        Assert.Equal(100, task.PercentComplete);
    }

    // -----------------------------------------------------------------------
    // Importance mapping
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1, 2)]  // high
    [InlineData(4, 2)]  // high
    [InlineData(5, 1)]  // normal
    [InlineData(null, 1)] // normal (null)
    [InlineData(6, 0)]  // low
    [InlineData(9, 0)]  // low
    public void Importance_MapsFromPriority(int? priority, int expected)
    {
        var group = SimpleGroup(t => t.Priority = priority);
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(expected, task.Importance);
    }

    // -----------------------------------------------------------------------
    // Sensitivity mapping
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("PRIVATE",      2)]
    [InlineData("CONFIDENTIAL", 3)]
    [InlineData("PUBLIC",       0)]
    [InlineData(null,           0)]
    public void Sensitivity_MapsFromPrivacy(string? privacy, int expected)
    {
        var group = SimpleGroup(t => t.Privacy = privacy);
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(expected, task.Sensitivity);
    }

    // -----------------------------------------------------------------------
    // Categories — unescaped-comma split + unescape \,
    // -----------------------------------------------------------------------

    [Fact]
    public void Categories_SplitOnUnescapedCommas()
    {
        var group = SimpleGroup(t =>
            t.Properties.Add(new RawProperty("CATEGORIES", Utf8(@"Work,Home\,Office,Personal"), null, null)));
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(3, task.Categories.Count);
        Assert.Equal("Work",        task.Categories[0]);
        Assert.Equal("Home,Office", task.Categories[1]); // \, → ,
        Assert.Equal("Personal",    task.Categories[2]);
    }

    // -----------------------------------------------------------------------
    // Reminder — relative TRIGGER with RELATED=START
    // -----------------------------------------------------------------------

    [Fact]
    public void Alarm_RelatedStart_NegativeOffset_SetsReminderBeforeStartDate()
    {
        var startMicros = MicrosFor(2026, 8, 1, 10, 0);
        var group = SimpleGroup(t =>
        {
            t.TodoEntry = startMicros;
            t.TodoDue   = MicrosFor(2026, 8, 5, 10, 0);
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;RELATED=START:-PT15M\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.True(task.ReminderSet);
        Assert.NotNull(task.ReminderTime);

        var expectedStart = PrTime.FromMicros(startMicros)!.Value;
        var expectedReminder = expectedStart.AddMinutes(-15);
        Assert.Equal(expectedReminder, task.ReminderTime!.Value);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Alarm_RelatedEnd_NegativeOffset_SetsReminderBeforeDueDate()
    {
        var dueMicros = MicrosFor(2026, 8, 5, 10, 0);
        var group = SimpleGroup(t =>
        {
            t.TodoEntry = MicrosFor(2026, 8, 1, 10, 0);
            t.TodoDue   = dueMicros;
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;RELATED=END:-PT30M\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.True(task.ReminderSet);
        var expectedDue = PrTime.FromMicros(dueMicros)!.Value;
        Assert.Equal(expectedDue.AddMinutes(-30), task.ReminderTime!.Value);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Alarm_NoExplicitRelated_DefaultsToStart_SetsReminderBeforeStartDate()
    {
        // Ical.Net defaults RELATED to START when not specified in the TRIGGER line.
        // So "TRIGGER:-PT1H" (no RELATED=) anchors on StartDate.
        var startMicros = MicrosFor(2026, 9, 10, 9, 0);
        var group = SimpleGroup(t =>
        {
            t.TodoEntry = startMicros; // StartDate — Ical.Net default anchor
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT1H\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.True(task.ReminderSet);
        var expectedStart = PrTime.FromMicros(startMicros)!.Value;
        Assert.Equal(expectedStart.AddHours(-1), task.ReminderTime!.Value);
    }

    // -----------------------------------------------------------------------
    // Reminder — positive/zero trigger → no reminder + warning + body preserved
    // -----------------------------------------------------------------------

    [Fact]
    public void Alarm_PositiveTrigger_NoReminderSetAndWarningAndBodyPreserved()
    {
        // Ical.Net defaults RELATED to START; so we need a StartDate for anchoring.
        // Positive offset (fires after anchor) → skip reminder + warn + preserve raw trigger.
        var group = SimpleGroup(t =>
        {
            t.Title     = "Example Task";
            t.TodoEntry = MicrosFor(2026, 8, 1);
            t.TodoDue   = MicrosFor(2026, 8, 5);
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:PT15M\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.False(task.ReminderSet);
        Assert.Null(task.ReminderTime);

        // Warning issued
        Assert.Single(warnings);
        Assert.Contains("reminder fires at/after the anchor", warnings[0]);

        // Raw trigger preserved in body
        Assert.NotNull(task.Body);
        Assert.Contains("[Thunderbird alarm not converted:", task.Body);
        Assert.Contains("TRIGGER:PT15M", task.Body);
    }

    [Fact]
    public void Alarm_ZeroTrigger_NoReminderSetAndWarning()
    {
        // Zero offset = fires exactly at anchor (START by Ical.Net default).
        var group = SimpleGroup(t =>
        {
            t.TodoEntry = MicrosFor(2026, 8, 1);
            t.TodoDue   = MicrosFor(2026, 8, 5);
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:PT0S\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.False(task.ReminderSet);
        Assert.Single(warnings);
        Assert.Contains("reminder fires at/after the anchor", warnings[0]);
    }

    // -----------------------------------------------------------------------
    // Reminder — alarm with no anchor date
    // -----------------------------------------------------------------------

    [Fact]
    public void Alarm_NoAnchorDate_WarnAndPreserveBody()
    {
        var group = SimpleGroup(t =>
        {
            // No TodoEntry, no TodoDue — default (null) anchor
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.False(task.ReminderSet);
        Assert.Single(warnings);
        Assert.Contains("alarm has no anchor date", warnings[0]);
        Assert.NotNull(task.Body);
        Assert.Contains("[Thunderbird alarm not converted:", task.Body);
    }

    // -----------------------------------------------------------------------
    // Multiple alarms → warning + only first converted
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleAlarms_WarnsAndConvertsFirst()
    {
        var startMicros = MicrosFor(2026, 8, 1, 10, 0);
        var group = SimpleGroup(t =>
        {
            t.TodoEntry = startMicros;
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;RELATED=START:-PT15M\r\nEND:VALARM"));
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;RELATED=START:-PT5M\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.True(task.ReminderSet);
        Assert.Single(warnings);
        Assert.Contains("multiple Thunderbird alarms", warnings[0]);
        // First alarm (-PT15M) is used
        var expectedStart = PrTime.FromMicros(startMicros)!.Value;
        Assert.Equal(expectedStart.AddMinutes(-15), task.ReminderTime!.Value);
    }

    // -----------------------------------------------------------------------
    // Absolute trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void Alarm_AbsoluteTrigger_SetsReminderToAbsoluteUtc()
    {
        var group = SimpleGroup(t =>
        {
            t.TodoDue = MicrosFor(2026, 8, 5);
            t.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;VALUE=DATE-TIME:20260801T070000Z\r\nEND:VALARM"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.True(task.ReminderSet);
        Assert.NotNull(task.ReminderTime);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 7, 0, 0, TimeSpan.Zero), task.ReminderTime!.Value);
        Assert.Empty(warnings);
    }
}
