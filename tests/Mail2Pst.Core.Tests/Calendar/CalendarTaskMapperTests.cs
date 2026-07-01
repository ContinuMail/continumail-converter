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
    public void GroupWithOverrides_NoRrule_MapsMasterNormally()
    {
        // Overrides with no RRULE in master — the override list is only relevant when
        // there is also a recurrence rule. Without one, the master is a non-recurring task
        // and the override rows are effectively orphaned; map the master as a plain task.
        var group = new RawTodoGroup
        {
            Master = new RawTodo { Id = "t1@example.com", Title = "Weekly Review" },
            Overrides = new List<RawTodo> { new RawTodo { Id = "t1@example.com", Title = "Weekly Review" } }
        };
        var result = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(result);
        Assert.Empty(warnings);
        Assert.Equal("Weekly Review", result.Subject);
    }

    [Fact]
    public void MasterWithRecurrenceLine_NoDates_DegradesWith_NoDueDateWarning()
    {
        // RRULE present but no anchor date → degrade to single non-recurring task + warning.
        var group = SimpleGroup(t =>
        {
            t.Title = "Recurring Task";
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO"));
        });
        var result = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(result);
        Assert.Single(warnings);
        Assert.Contains("no due/start date to anchor recurrence", warnings[0]);
        Assert.Null(result.Recurrence);
    }

    [Fact]
    public void MasterWithRecurrenceIdSet_ReturnsNullWithMisgroupedWarning()
    {
        // A task with RecurrenceId is an override row that got mis-grouped as a master; skip it.
        var group = SimpleGroup(t =>
        {
            t.RecurrenceId = MicrosFor(2026, 7, 7);
        });
        var result = CalendarTaskMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("task override row mis-grouped as master; skipped", warnings[0]);
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
    // PercentComplete fallback (cal_properties PERCENT-COMPLETE)
    // -----------------------------------------------------------------------
    // Thunderbird's storage calendar persists PERCENT-COMPLETE only as a
    // cal_properties row (TEXT), leaving cal_todos.todo_complete NULL —
    // observed on a real profile (ICS import, TB 140). The column, when
    // populated, stays authoritative.

    [Fact]
    public void PercentComplete_FallsBackToProperty_WhenColumnIsNull()
    {
        var group = SimpleGroup(t =>
        {
            t.IcalStatus   = "IN-PROCESS";
            t.TodoComplete = null;
            t.Properties.Add(new RawProperty("PERCENT-COMPLETE", Utf8("50"), null, null));
        });
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(50, task.PercentComplete);
    }

    [Fact]
    public void PercentComplete_ColumnWins_OverProperty()
    {
        var group = SimpleGroup(t =>
        {
            t.TodoComplete = 25;
            t.Properties.Add(new RawProperty("PERCENT-COMPLETE", Utf8("75"), null, null));
        });
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(25, task.PercentComplete);
    }

    [Fact]
    public void PercentComplete_PropertyOf100_ForcesStatusComplete()
    {
        var group = SimpleGroup(t =>
        {
            t.IcalStatus   = "IN-PROCESS";
            t.TodoComplete = null;
            t.Properties.Add(new RawProperty("PERCENT-COMPLETE", Utf8("100"), null, null));
        });
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(100, task.PercentComplete);
        Assert.Equal(TaskStatusKind.Complete, task.Status);
    }

    [Fact]
    public void PercentComplete_NonNumericProperty_IsIgnored()
    {
        var group = SimpleGroup(t =>
        {
            t.TodoComplete = null;
            t.Properties.Add(new RawProperty("PERCENT-COMPLETE", Utf8("half"), null, null));
        });
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(0, task.PercentComplete);
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

    [Fact]
    public void Categories_DropsXMozPrefixedTokens()
    {
        // X-MOZ-* tokens are Mozilla-internal and must be stripped from CATEGORIES.
        var group = SimpleGroup(t =>
            t.Properties.Add(new RawProperty("CATEGORIES", Utf8("Work,X-MOZ-SNOOZE-TIME,Personal"), null, null)));
        var task = CalendarTaskMapper.Map(group, out _);

        Assert.NotNull(task);
        Assert.Equal(2, task.Categories.Count);
        Assert.Equal("Work",     task.Categories[0]);
        Assert.Equal("Personal", task.Categories[1]);
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

    // -----------------------------------------------------------------------
    // Reminder — VALARM parse failure → no reminder + warning + body preserved
    // -----------------------------------------------------------------------

    [Fact]
    public void Alarm_ParseFailure_NoReminderAndWarningAndBodyPreserved()
    {
        // A "VALARM block" without a BEGIN:VALARM/END:VALARM envelope is stored by
        // Thunderbird as bare iCal text.  ICalTextParser.ParseAlarm wraps it in a
        // VEVENT; because there is no nested VALARM component, Ical.Net produces
        // evt.Alarms.Count == 0, which ParseAlarm maps to Fail (Value=null + warning).
        // The raw TRIGGER line in the block must then be preserved in the task body.
        var group = SimpleGroup(t =>
        {
            t.TodoEntry = MicrosFor(2026, 8, 1);
            // Bare text: has a TRIGGER line but no BEGIN:VALARM wrapper.
            t.Alarms.Add(new RawSideText(
                "TRIGGER:-PT15M\r\nX-SYNTHETIC-PROPERTY:failsafe"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.False(task.ReminderSet);
        Assert.Null(task.ReminderTime);
        Assert.True(warnings.Count > 0, "Expected at least one warning for parse failure");
        Assert.NotNull(task.Body);
        Assert.Contains("[Thunderbird alarm not converted:", task.Body);
        Assert.Contains("TRIGGER", task.Body);
    }

    // -----------------------------------------------------------------------
    // Recurring task support (PR7)
    // -----------------------------------------------------------------------

    [Fact]
    public void Recurring_task_is_written_with_recurrence_pattern()
    {
        // Basic recurring task with a weekly RRULE and a DueDate anchor.
        // 2026-07-13 is a Monday — consistent with the BYDAY=MO rule.
        var group = SimpleGroup(t =>
        {
            t.Title   = "Weekly Standup";
            t.TodoDue = MicrosFor(2026, 7, 13); // Monday
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=5"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Empty(warnings);
        Assert.NotNull(task.Recurrence);
        Assert.Equal(RecurrenceFrequency.Weekly, task.Recurrence!.Frequency);
        Assert.Equal(RecurrenceEndKind.Count, task.Recurrence.EndKind);
        Assert.Equal(5, task.Recurrence.Count);
    }

    [Fact]
    public void Recurring_task_with_exception_override_is_written_correctly()
    {
        // A recurring task whose series has an override row (exception) should degrade to
        // a single non-recurring task with a warning — NOT return null.
        var overrideTodo = new RawTodo
        {
            Id    = "task-override-001@example.com",
            Title = "Weekly Review",
        };
        var group = new RawTodoGroup
        {
            Master = new RawTodo
            {
                Id    = "task-master-001@example.com",
                Title = "Weekly Review",
                TodoDue = MicrosFor(2026, 7, 13),
            },
            Overrides = new List<RawTodo> { overrideTodo },
        };
        group.Master.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;COUNT=5"));

        var task = CalendarTaskMapper.Map(group, out var warnings);

        // Task is returned (NOT null) — it degrades to single non-recurring
        Assert.NotNull(task);
        Assert.Single(warnings);
        Assert.Contains("deletions/exceptions not supported", warnings[0]);
        Assert.Null(task.Recurrence);
    }

    [Fact]
    public void Weekly_task_with_due_anchor_maps_recurrence()
    {
        // RRULE anchored on DueDate (preferred over StartDate).
        // 2026-07-13 is a Monday — consistent with BYDAY=MO.
        var group = SimpleGroup(t =>
        {
            t.Title      = "Monday Task";
            t.TodoEntry  = MicrosFor(2026, 7, 6);   // start (Sunday)
            t.TodoDue    = MicrosFor(2026, 7, 13);  // due (Monday) — anchor
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=5"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Empty(warnings);
        Assert.NotNull(task.Recurrence);
        Assert.Equal(RecurrenceFrequency.Weekly, task.Recurrence!.Frequency);
        Assert.Equal(5, task.Recurrence.Count);
        Assert.Equal(RecurrenceEndKind.Count, task.Recurrence.EndKind);
        // Anchor is UTC-midnight of the DueDate (2026-07-13)
        Assert.Equal(new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc), task.Recurrence.FirstStartUtc);
    }

    [Fact]
    public void No_due_falls_back_to_start_anchor()
    {
        // When there is no DueDate, StartDate is used as the anchor.
        var group = SimpleGroup(t =>
        {
            t.Title     = "Daily Habit";
            t.TodoEntry = MicrosFor(2026, 7, 14); // start only
            // No TodoDue
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=DAILY;COUNT=3"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Empty(warnings);
        Assert.NotNull(task.Recurrence);
        Assert.Equal(RecurrenceFrequency.Daily, task.Recurrence!.Frequency);
        Assert.Equal(new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc), task.Recurrence.FirstStartUtc);
    }

    [Fact]
    public void No_due_no_start_degrades_with_warning()
    {
        // Neither DueDate nor StartDate — cannot anchor the recurrence; degrade to single task.
        var group = SimpleGroup(t =>
        {
            t.Title = "Undated Recurring";
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;COUNT=3"));
            // No TodoEntry, no TodoDue
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        // Task is still returned (non-null) — just without Recurrence
        Assert.NotNull(task);
        Assert.Single(warnings);
        Assert.Contains("no due/start date to anchor recurrence", warnings[0]);
        Assert.Null(task.Recurrence);
    }

    [Fact]
    public void Exdate_present_degrades_to_single_task_with_warning()
    {
        // An EXDATE line means the series has deleted occurrences — not representable;
        // degrade to a single non-recurring task with a warning, task is NOT null.
        var group = SimpleGroup(t =>
        {
            t.Title   = "Skipped Monday";
            t.TodoDue = MicrosFor(2026, 7, 13); // Monday
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=5"));
            t.Recurrence.Add(new RawSideText("EXDATE:20260720T000000Z"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Single(warnings);
        Assert.Contains("deletions/exceptions not supported", warnings[0]);
        Assert.Null(task.Recurrence);
    }

    [Fact]
    public void Override_rows_degrade_to_single_task_with_warning()
    {
        // group.Overrides.Count > 0 with RRULE present → degrade, task still returned (NOT null).
        var group = new RawTodoGroup
        {
            Master = new RawTodo
            {
                Id      = "master-002@example.com",
                Title   = "Weekly Sync",
                TodoDue = MicrosFor(2026, 7, 13),
            },
            Overrides = new List<RawTodo>
            {
                new RawTodo { Id = "override-002@example.com", Title = "Weekly Sync" }
            },
        };
        group.Master.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;COUNT=5"));

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Single(warnings);
        Assert.Contains("deletions/exceptions not supported", warnings[0]);
        Assert.Null(task.Recurrence);
    }

    [Fact]
    public void Bysetpos_degrades_with_warning()
    {
        // BYSETPOS (e.g. "last Monday of month") is not representable — degrade.
        var group = SimpleGroup(t =>
        {
            t.Title   = "Last Monday";
            t.TodoDue = MicrosFor(2026, 7, 27); // last Monday of July 2026
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Single(warnings);
        Assert.Contains("BYSETPOS", warnings[0]);
        Assert.Null(task.Recurrence);
    }

    [Fact]
    public void Completed_recurring_task_degrades_if_unsupported()
    {
        // A completed recurring task triggers Outlook live-regeneration when written.
        // The converter cannot author that lifecycle, so degrade to a single completed task.
        var group = SimpleGroup(t =>
        {
            t.Title        = "Done Series";
            t.IcalStatus   = "COMPLETED";
            t.TodoComplete = 100;
            t.TodoDue      = MicrosFor(2026, 7, 13);
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;COUNT=5"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);
        Assert.Single(warnings);
        Assert.Contains("completed recurring tasks not supported", warnings[0]);
        Assert.Null(task.Recurrence);
        // Task is still complete
        Assert.Equal(TaskStatusKind.Complete, task.Status);
        Assert.Equal(100, task.PercentComplete);
    }

    // -----------------------------------------------------------------------
    // I1: RRULE present but body is malformed — must warn, not silently drop
    // -----------------------------------------------------------------------

    [Fact]
    public void Recurring_task_with_unparseable_rrule_degrades_with_warning()
    {
        // RRULE:FREQ=WEEKLY;BYDAY=ZZ — "ZZ" is not a valid BYDAY weekday abbreviation; Ical.Net's
        // RecurrencePattern constructor throws, causing ICalTextParser.ParseRecurrence to return
        // (Value=null, Warnings=[...]).  RecurrenceMapping.FromIcal collapses this to (null,null).
        // With hasRrule==true the task mapper must emit "RRULE could not be parsed" — not silently
        // produce a non-recurring task with zero warnings.
        var group = SimpleGroup(t =>
        {
            t.Title   = "Bad RRULE Task";
            t.TodoDue = MicrosFor(2026, 7, 13);
            t.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=ZZ"));
        });

        var task = CalendarTaskMapper.Map(group, out var warnings);

        Assert.NotNull(task);                              // task is returned, not null
        Assert.Null(task.Recurrence);                     // degraded — no recurrence pattern
        Assert.Single(warnings);                          // exactly one warning
        Assert.Contains("could not be parsed", warnings[0]); // the parse-failure warning
    }
}
