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
/// Unit tests for <see cref="CalendarEventMapper.Map"/>.
/// All data is synthetic/reserved (example.com, example.org) — no real mail or PII.
/// </summary>
public class CalendarEventMapperTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static long MicrosFor(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static RawEventGroup SimpleGroup(Action<RawEvent>? configure = null)
    {
        var ev = new RawEvent
        {
            Id          = "event-example-001@example.com",
            Title       = "Example Event",
            EventStart  = MicrosFor(2026, 7, 10, 14, 0),
            EventStartTz = "UTC",
            EventEnd    = MicrosFor(2026, 7, 10, 15, 0),
            EventEndTz  = "UTC",
            Flags       = 0,
            Priority    = 5,
            Privacy     = null,
            IcalStatus  = null,
        };
        configure?.Invoke(ev);
        return new RawEventGroup { Master = ev };
    }

    // -----------------------------------------------------------------------
    // Group-skip rules
    // -----------------------------------------------------------------------

    [Fact]
    public void NullMaster_ReturnsNullWithOrphanWarning()
    {
        var group = new RawEventGroup(); // Master == null
        var result = CalendarEventMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("orphan event override (no master) skipped", warnings[0]);
    }

    [Fact]
    public void GroupWithOverrides_ReturnsNullWithRecurringWarning()
    {
        var group = new RawEventGroup
        {
            Master    = new RawEvent { Id = "e1@example.com", Title = "Weekly Standup" },
            Overrides = new List<RawEvent> { new RawEvent { Id = "e1@example.com" } }
        };
        var result = CalendarEventMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("recurring event 'Weekly Standup' with exceptions deferred to PR7", warnings[0]);
    }

    [Fact]
    public void MasterWithRecurrenceLine_ReturnsNullWithRecurringWarning()
    {
        var group = SimpleGroup(e =>
        {
            e.Title = "Daily Sync";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=DAILY"));
        });
        var result = CalendarEventMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("recurring event 'Daily Sync' deferred to PR7", warnings[0]);
    }

    [Fact]
    public void MasterWithRecurrenceIdSet_ReturnsNullWithOverrideWarning()
    {
        var group = SimpleGroup(e =>
        {
            e.RecurrenceId = MicrosFor(2026, 7, 10);
        });
        var result = CalendarEventMapper.Map(group, out var warnings);

        Assert.Null(result);
        Assert.Single(warnings);
        Assert.Contains("event override row deferred to PR7", warnings[0]);
    }

    // -----------------------------------------------------------------------
    // Flat timed event with resolved timezone
    // -----------------------------------------------------------------------

    [Fact]
    public void FlatTimedEvent_ResolvedTimezone_MapsFieldsCorrectly()
    {
        var startMicros = MicrosFor(2026, 7, 10, 12, 0);
        var endMicros   = MicrosFor(2026, 7, 10, 13, 0);

        var group = SimpleGroup(e =>
        {
            e.Id           = "flat-event-001@example.com";
            e.Title        = "Team Meeting";
            e.EventStart   = startMicros;
            e.EventStartTz = "UTC";
            e.EventEnd     = endMicros;
            e.EventEndTz   = "UTC";
            e.Flags        = 0;
            e.Properties.Add(new RawProperty("DESCRIPTION", Utf8("Meeting agenda."), null, null));
            e.Properties.Add(new RawProperty("LOCATION",    Utf8("Room 101"),         null, null));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Empty(warnings);
        Assert.Equal("flat-event-001@example.com", appt.SourceId);
        Assert.Equal("Team Meeting", appt.Subject);
        Assert.Equal("Meeting agenda.", appt.Body);
        Assert.Equal("Room 101", appt.Location);
        Assert.False(appt.IsAllDay);
        Assert.NotNull(appt.TimeZone);
        Assert.Equal(TimeZoneInfo.Utc, appt.TimeZone);
        Assert.Equal(new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc), appt.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 10, 13, 0, 0, DateTimeKind.Utc), appt.EndUtc);
    }

    // -----------------------------------------------------------------------
    // Timed event with floating/unresolved timezone → TimeZone=null + warn
    // -----------------------------------------------------------------------

    [Fact]
    public void TimedEvent_FloatingTimezone_TimeZoneNullAndWarn()
    {
        var group = SimpleGroup(e =>
        {
            e.Title        = "Floating Meeting";
            e.EventStartTz = ""; // floating
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Null(appt.TimeZone);
        Assert.Single(warnings);
        Assert.Contains("floating/unresolved timezone", warnings[0]);
    }

    [Fact]
    public void TimedEvent_NoTzDescription_TimeZoneNullAndWarn()
    {
        var group = SimpleGroup(e =>
        {
            e.Title        = "No-TZ Event";
            e.EventStartTz = "(no TZ description)";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Null(appt.TimeZone);
        // TimeZoneResolver emits "no TZ description" + mapper adds floating/unresolved warning
        Assert.True(warnings.Count >= 1, "Expected at least one warning for no-TZ event");
        Assert.Contains("floating/unresolved timezone", string.Join(" ", warnings));
    }

    // -----------------------------------------------------------------------
    // All-day event with timezone
    // -----------------------------------------------------------------------

    [Fact]
    public void AllDayEvent_ResolvedTimezone_IsAllDayTrueAndMidnightBoundaries()
    {
        // 2026-07-15 as an all-day event in UTC
        // UTC midnight = 2026-07-15T00:00:00Z
        var startMicros = MicrosFor(2026, 7, 15, 0, 0);
        var endMicros   = MicrosFor(2026, 7, 16, 0, 0);

        var group = SimpleGroup(e =>
        {
            e.Title        = "Company Holiday";
            e.Flags        = 4; // EVENT_ALLDAY
            e.EventStart   = startMicros;
            e.EventStartTz = "UTC";
            e.EventEnd     = endMicros;
            e.EventEndTz   = "UTC";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt.IsAllDay);
        Assert.NotNull(appt.TimeZone);
        Assert.Equal(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), appt.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc), appt.EndUtc);
    }

    [Fact]
    public void AllDayEvent_EndEqualStart_EndSetToOneDayLater()
    {
        var startMicros = MicrosFor(2026, 7, 15, 0, 0);

        var group = SimpleGroup(e =>
        {
            e.Title        = "Single Day Holiday";
            e.Flags        = 4;
            e.EventStart   = startMicros;
            e.EventStartTz = "UTC";
            e.EventEnd     = startMicros; // same as start
            e.EventEndTz   = "UTC";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt.IsAllDay);
        Assert.Equal(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), appt.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc), appt.EndUtc);
    }

    // -----------------------------------------------------------------------
    // All-day event with unresolved timezone → TimeZone=Utc + warn
    // -----------------------------------------------------------------------

    [Fact]
    public void AllDayEvent_UnresolvedTimezone_UsesUtcAndWarn()
    {
        var startMicros = MicrosFor(2026, 7, 15, 0, 0);
        var endMicros   = MicrosFor(2026, 7, 16, 0, 0);

        var group = SimpleGroup(e =>
        {
            e.Title        = "Holiday";
            e.Flags        = 4;
            e.EventStart   = startMicros;
            e.EventStartTz = "Unknown/Bogus_Timezone_99";
            e.EventEnd     = endMicros;
            e.EventEndTz   = "Unknown/Bogus_Timezone_99";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt.IsAllDay);
        Assert.Equal(TimeZoneInfo.Utc, appt.TimeZone);
        Assert.True(warnings.Count > 0, "Expected a warning for unresolved timezone");
        Assert.Contains("unresolved", string.Join(" ", warnings), StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // EndUtc < StartUtc → warn + clamp
    // -----------------------------------------------------------------------

    [Fact]
    public void TimedEvent_EndPrecedesStart_ClampsEndToStart()
    {
        var startMicros = MicrosFor(2026, 7, 10, 14, 0);
        var endMicros   = MicrosFor(2026, 7, 10, 13, 0); // before start

        var group = SimpleGroup(e =>
        {
            e.Title      = "Bad Times Event";
            e.EventStart = startMicros;
            e.EventEnd   = endMicros;
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Equal(appt.StartUtc, appt.EndUtc);
        Assert.True(warnings.Count > 0);
        Assert.Contains("end precedes start", warnings[0]);
    }

    // -----------------------------------------------------------------------
    // Zero-length event → warn (allowed)
    // -----------------------------------------------------------------------

    [Fact]
    public void TimedEvent_ZeroLength_AllowedWithWarn()
    {
        var startMicros = MicrosFor(2026, 7, 10, 14, 0);

        var group = SimpleGroup(e =>
        {
            e.Title      = "Zero Length";
            e.EventStart = startMicros;
            e.EventEnd   = startMicros; // same as start
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Equal(appt.StartUtc, appt.EndUtc);
        Assert.Single(warnings);
        Assert.Contains("zero-length event", warnings[0]);
    }

    // -----------------------------------------------------------------------
    // BusyStatus precedence
    // -----------------------------------------------------------------------

    [Fact]
    public void BusyStatus_Tentative_Returns1()
    {
        var group = SimpleGroup(e =>
        {
            e.IcalStatus = "TENTATIVE";
            e.Properties.Add(new RawProperty("TRANSP", Utf8("OPAQUE"), null, null));
        });
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal(1, appt.BusyStatus);
    }

    [Fact]
    public void BusyStatus_TranspTransparent_NoStatus_Returns0()
    {
        var group = SimpleGroup(e =>
        {
            e.IcalStatus = null;
            e.Properties.Add(new RawProperty("TRANSP", Utf8("TRANSPARENT"), null, null));
        });
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal(0, appt.BusyStatus);
    }

    [Fact]
    public void BusyStatus_Default_Returns2()
    {
        var group = SimpleGroup(e =>
        {
            e.IcalStatus = null;
            // no TRANSP property
        });
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal(2, appt.BusyStatus);
    }

    // -----------------------------------------------------------------------
    // Sensitivity / Privacy
    // -----------------------------------------------------------------------

    [Fact]
    public void Privacy_Private_Sensitivity2()
    {
        var group = SimpleGroup(e => e.Privacy = "PRIVATE");
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal(2, appt.Sensitivity);
    }

    // -----------------------------------------------------------------------
    // ALTREP HTML body
    // -----------------------------------------------------------------------

    [Fact]
    public void Altrep_DataUriPercentEncoded_SetsBodyHtml()
    {
        // data:text/html;charset=utf-8,<b>Hello</b>  (percent-encoded)
        var htmlEncoded = Uri.EscapeDataString("<b>Hello</b>");
        var dataUri = $"data:text/html;charset=utf-8,{htmlEncoded}";

        var group = SimpleGroup(e =>
        {
            e.Parameters.Add(new RawParameter("DESCRIPTION", "ALTREP", dataUri, null, null));
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal("<b>Hello</b>", appt.BodyHtml);
    }

    [Fact]
    public void Altrep_DataUriBase64_SetsBodyHtml()
    {
        var html = "<p>Test</p>";
        var b64  = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        var dataUri = $"data:text/html;base64,{b64}";

        var group = SimpleGroup(e =>
        {
            e.Parameters.Add(new RawParameter("DESCRIPTION", "ALTREP", dataUri, null, null));
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal("<p>Test</p>", appt.BodyHtml);
    }

    // -----------------------------------------------------------------------
    // Categories
    // -----------------------------------------------------------------------

    [Fact]
    public void Categories_EscapedComma_SplitCorrectly()
    {
        var group = SimpleGroup(e =>
            e.Properties.Add(new RawProperty("CATEGORIES", Utf8(@"A\,B,C"), null, null)));
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal(2, appt.Categories.Count);
        Assert.Equal("A,B", appt.Categories[0]);
        Assert.Equal("C",   appt.Categories[1]);
    }

    [Fact]
    public void Categories_XMozPrefixed_Dropped()
    {
        var group = SimpleGroup(e =>
            e.Properties.Add(new RawProperty("CATEGORIES", Utf8("Work,X-MOZ-SNOOZE-TIME,Home"), null, null)));
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal(2, appt.Categories.Count);
        Assert.Equal("Work", appt.Categories[0]);
        Assert.Equal("Home", appt.Categories[1]);
    }

    // -----------------------------------------------------------------------
    // VALARM -PT15M → ReminderSet + ReminderMinutesBefore=15
    // -----------------------------------------------------------------------

    [Fact]
    public void Alarm_NegativeTrigger_SetsReminderMinutesBefore()
    {
        var group = SimpleGroup(e =>
        {
            e.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nEND:VALARM"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt.ReminderSet);
        Assert.Equal(15, appt.ReminderMinutesBefore);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Alarm_PositiveTrigger_NoReminderAndWarnAndBodyPreserved()
    {
        var group = SimpleGroup(e =>
        {
            e.Title = "Post-meeting note";
            e.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:PT15M\r\nEND:VALARM"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.False(appt.ReminderSet);
        Assert.Equal(0, appt.ReminderMinutesBefore);
        Assert.True(warnings.Count > 0);
        Assert.Contains("reminder fires at/after", warnings[0]);
        Assert.NotNull(appt.Body);
        Assert.Contains("[Thunderbird alarm not converted:", appt.Body);
        Assert.Contains("TRIGGER:PT15M", appt.Body);
    }

    // -----------------------------------------------------------------------
    // Pending: recurring-event support (PR7)
    // -----------------------------------------------------------------------

    [Fact(Skip = "Recurring events land in PR7; tracked by this skipped test.")]
    public void Recurring_event_is_written_with_recurrence_pattern() { /* TODO PR7 */ }

    [Fact(Skip = "Recurring events land in PR7; tracked by this skipped test.")]
    public void Recurring_event_with_exception_override_is_written_correctly() { /* TODO PR7 */ }
}

/// <summary>
/// Unit tests for <see cref="IcalDataUri.TryDecode"/>.
/// </summary>
public class IcalDataUriTests
{
    [Fact]
    public void TryDecode_Base64_DecodesCorrectly()
    {
        var b64    = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"));
        var uri    = $"data:text/html;base64,{b64}";
        var result = IcalDataUri.TryDecode(uri, out var mediaType, out var bytes);

        Assert.True(result);
        Assert.Equal("text/html", mediaType);
        Assert.Equal("hello", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void TryDecode_PercentEncoded_DecodesCorrectly()
    {
        var encoded = Uri.EscapeDataString("<b>Test</b>");
        var uri     = $"data:text/html;charset=utf-8,{encoded}";
        var result  = IcalDataUri.TryDecode(uri, out var mediaType, out var bytes);

        Assert.True(result);
        Assert.Equal("text/html", mediaType);
        Assert.Equal("<b>Test</b>", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void TryDecode_PlainTextNoBase64_DecodesAsUtf8()
    {
        var uri    = "data:text/plain,hello";
        var result = IcalDataUri.TryDecode(uri, out var mediaType, out var bytes);

        Assert.True(result);
        Assert.Equal("text/plain", mediaType);
        Assert.Equal("hello", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void TryDecode_MalformedNoComma_ReturnsFalse()
    {
        var result = IcalDataUri.TryDecode("data:text/plain", out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryDecode_NonDataUri_ReturnsFalse()
    {
        var result = IcalDataUri.TryDecode("https://example.com/file.html", out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryDecode_InvalidBase64_ReturnsFalse()
    {
        var uri    = "data:text/html;base64,!!!not-valid-base64!!!";
        var result = IcalDataUri.TryDecode(uri, out _, out _);
        Assert.False(result);
    }
}
