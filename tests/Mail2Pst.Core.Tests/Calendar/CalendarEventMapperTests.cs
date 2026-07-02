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
    public void Overrides_without_rrule_write_single_with_warning()
    {
        // Overrides present but no RRULE → degrades to single occurrence, warns overrides dropped.
        var group = SimpleGroup(e => e.Title = "Weekly Standup");
        group.Overrides.Add(new RawEvent { Id = "e1@example.com" });

        var result = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(result);
        Assert.Null(result!.Recurrence);
        Assert.Single(warnings);
        Assert.Contains("overrides dropped", warnings[0]);
    }

    [Fact]
    public void MasterWithRecurrenceLine_MapsRecurrenceSpec()
    {
        // RRULE:FREQ=DAILY — always mappable; result should have Recurrence set.
        var group = SimpleGroup(e =>
        {
            e.Title = "Daily Sync";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=DAILY"));
        });
        var result = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(result);
        Assert.NotNull(result!.Recurrence);
        Assert.Equal(RecurrenceFrequency.Daily, result.Recurrence!.Frequency);
        Assert.Empty(warnings);
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
        Assert.Contains("mis-grouped as master", warnings[0]);
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
        // Exactly two warnings: resolver emits "no TZ description", mapper adds "floating/unresolved timezone"
        Assert.Equal(2, warnings.Count);
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
            e.Flags        = 8; // EVENT_ALLDAY
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
    public void EventWithPropertiesFlagButNotAllDayBit_IsTimedNotAllDay()
    {
        // Regression (real Thunderbird data): Mozilla flags bit 4 = HAS_PROPERTIES,
        // bit 8 = EVENT_ALLDAY. Nearly every real event has HAS_PROPERTIES set
        // (DESCRIPTION/LOCATION live in cal_properties), so a timed event routinely
        // arrives with (flags & 4) != 0. All-day detection must key on bit 8 only —
        // otherwise every event renders as all-day in Outlook.
        var group = SimpleGroup(e =>
        {
            e.Title        = "Team Standup";
            e.Flags        = 4 | 16 | 256; // HAS_PROPERTIES | HAS_RECURRENCE | HAS_ALARMS, NOT EVENT_ALLDAY
            e.EventStart   = MicrosFor(2026, 7, 13, 9, 0);
            e.EventStartTz = "UTC";
            e.EventEnd     = MicrosFor(2026, 7, 13, 9, 15);
            e.EventEndTz   = "UTC";
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.False(appt!.IsAllDay);
        // Timed boundaries preserved (not snapped to midnight).
        Assert.Equal(new DateTime(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc), appt.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 13, 9, 15, 0, DateTimeKind.Utc), appt.EndUtc);
    }

    [Fact]
    public void EventWithAllDayBitSet_IsAllDay()
    {
        // The genuine all-day flag is bit 8 (EVENT_ALLDAY), here OR'd with HAS_PROPERTIES.
        var group = SimpleGroup(e =>
        {
            e.Title        = "Company Anniversary";
            e.Flags        = 8 | 4; // EVENT_ALLDAY | HAS_PROPERTIES
            e.EventStart   = MicrosFor(2026, 7, 15, 0, 0);
            e.EventStartTz = "UTC";
            e.EventEnd     = MicrosFor(2026, 7, 16, 0, 0);
            e.EventEndTz   = "UTC";
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.True(appt!.IsAllDay);
    }

    [Fact]
    public void AllDayEvent_EndEqualStart_EndSetToOneDayLater()
    {
        var startMicros = MicrosFor(2026, 7, 15, 0, 0);

        var group = SimpleGroup(e =>
        {
            e.Title        = "Single Day Holiday";
            e.Flags        = 8;
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

    [Fact]
    public void AllDayEvent_NullEventStart_UsesSentinelDateAndWarns()
    {
        // All-day event where EventStart is null → must not use DateTime.UtcNow
        // (non-deterministic). Should fall back to default(DateTime) and warn.
        var group = SimpleGroup(e =>
        {
            e.Title        = "Sentinel Holiday";
            e.Flags        = 8; // EVENT_ALLDAY
            e.EventStart   = null;
            e.EventStartTz = "UTC";
            e.EventEnd     = null;
            e.EventEndTz   = "UTC";
        });

        var appt1 = CalendarEventMapper.Map(group, out var warnings1);
        var appt2 = CalendarEventMapper.Map(group, out var warnings2);

        Assert.NotNull(appt1);
        Assert.True(appt1.IsAllDay);

        // Sentinel date — default(DateTime) round-tripped through UTC midnight
        Assert.Equal(default(DateTime), appt1.StartUtc);

        // Warning emitted for missing start
        Assert.Contains(warnings1, w => w.Contains("missing start"));

        // Deterministic: two calls produce identical output
        Assert.Equal(appt1.StartUtc, appt2.StartUtc);
        Assert.Equal(warnings1.Count, warnings2.Count);
    }

    [Fact]
    public void AllDayEvent_NoEndDate_EndUtcIsExactly24hAfterStartUtc()
    {
        // Non-DST zone (UTC): all-day with null end → EndUtc − StartUtc must equal exactly 24 h.
        var startMicros = MicrosFor(2026, 7, 15, 0, 0);

        var group = SimpleGroup(e =>
        {
            e.Title        = "Exact Day Event";
            e.Flags        = 8; // EVENT_ALLDAY
            e.EventStart   = startMicros;
            e.EventStartTz = "UTC";
            e.EventEnd     = null; // missing end → fallback to one-day boundary
            e.EventEndTz   = "UTC";
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.True(appt.IsAllDay);
        Assert.Equal(TimeSpan.FromHours(24), appt.EndUtc - appt.StartUtc);
    }

    // -----------------------------------------------------------------------
    // Floating all-day event → anchored to LOCAL zone (local-midnight-in-UTC)
    // -----------------------------------------------------------------------

    [Fact]
    public void AllDayEvent_FloatingTimezone_AnchorsToLocalMidnightInUtc()
    {
        // Regression (real Thunderbird all-day events are floating): a floating all-day event must
        // anchor midnight to the machine LOCAL zone (local-midnight-in-UTC) and carry that zone —
        // NOT UTC. Anchoring to UTC makes the event straddle two calendar days for any viewer east
        // of UTC (e.g. a UTC+2 viewer sees a one-day event span both days).
        var group = SimpleGroup(e =>
        {
            e.Title        = "Company Holiday";
            e.Flags        = 8 | 4;                       // EVENT_ALLDAY | HAS_PROPERTIES
            e.EventStart   = MicrosFor(2026, 7, 1, 0, 0); // floating wall-clock July 1 00:00
            e.EventStartTz = "floating";
            e.EventEnd     = MicrosFor(2026, 7, 2, 0, 0);
            e.EventEndTz   = "floating";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt!.IsAllDay);
        Assert.Equal(TimeZoneInfo.Local, appt.TimeZone);

        // local-midnight-in-UTC for July 1 / July 2 — deterministic on any machine (incl. UTC CI,
        // where Local == UTC so this reduces to UTC midnight).
        var expStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        var expEnd   = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        Assert.Equal(expStart, appt.StartUtc);
        Assert.Equal(expEnd, appt.EndUtc);
        // Genuine floating is normal → no warning.
        Assert.Empty(warnings);
    }

    // -----------------------------------------------------------------------
    // All-day event with unresolved (bogus) timezone → LOCAL anchor + resolver warn
    // -----------------------------------------------------------------------

    [Fact]
    public void AllDayEvent_UnresolvedTimezone_AnchorsToLocalAndWarns()
    {
        var startMicros = MicrosFor(2026, 7, 15, 0, 0);
        var endMicros   = MicrosFor(2026, 7, 16, 0, 0);

        var group = SimpleGroup(e =>
        {
            e.Title        = "Holiday";
            e.Flags        = 8;
            e.EventStart   = startMicros;
            e.EventStartTz = "Unknown/Bogus_Timezone_99";
            e.EventEnd     = endMicros;
            e.EventEndTz   = "Unknown/Bogus_Timezone_99";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt.IsAllDay);
        // Unresolvable zone → anchored to LOCAL (same policy as floating), never straddles days.
        Assert.Equal(TimeZoneInfo.Local, appt.TimeZone);
        // The resolver still surfaces the lost zone.
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
    public void BusyStatus_Default_TimedEvent_Returns2()
    {
        var group = SimpleGroup(e =>
        {
            e.IcalStatus = null;
            // no TRANSP property; Flags=0 → timed event
        });
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Equal(2, appt.BusyStatus);
    }

    [Fact]
    public void BusyStatus_AllDay_NoTransp_NoStatus_Returns0_Free()
    {
        // All-day event with no TRANSP and no TENTATIVE → Free (0), not Busy.
        var group = SimpleGroup(e =>
        {
            e.Flags        = 8; // EVENT_ALLDAY
            e.EventStartTz = "UTC";
            e.IcalStatus   = null;
            // no TRANSP property
        });
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.True(appt.IsAllDay);
        Assert.Equal(0, appt.BusyStatus);
    }

    [Fact]
    public void BusyStatus_AllDay_OpaqueTransp_Returns2_Busy()
    {
        // All-day event WITH explicit TRANSP=OPAQUE → Busy (2); explicit wins over all-day default.
        var group = SimpleGroup(e =>
        {
            e.Flags        = 8; // EVENT_ALLDAY
            e.EventStartTz = "UTC";
            e.IcalStatus   = null;
            e.Properties.Add(new RawProperty("TRANSP", Utf8("OPAQUE"), null, null));
        });
        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.True(appt.IsAllDay);
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

    /// <summary>
    /// Should-fix (SF-A): a malformed EXDATE value must be skipped (null), not coerced to
    /// default(0001-01-01) which would feed the vendored serializer an invalid date; whitespace around a
    /// valid value must be trimmed.
    /// </summary>
    [Fact]
    public void ToInstanceId_malformed_returns_null_and_trims_whitespace()
    {
        Assert.Null(CalendarEventMapper.ToInstanceId("notadate", "UTC", isDateOnly: true));

        var ok = CalendarEventMapper.ToInstanceId("  20260708  ", "UTC", isDateOnly: true);
        Assert.NotNull(ok);
        Assert.Equal(new DateTime(2026, 7, 8), ok!.OriginalStartLocal.Date);
    }

    /// <summary>
    /// Mutation-coverage (Stryker): a resolved-zone (non-floating) all-day event must anchor its start to
    /// LOCAL midnight in that zone, not read the raw UTC instant's date. For an all-day event stored as
    /// 2026-07-09 17:00 UTC with tz=Asia/Bangkok (= Jul 10 00:00 local), StartUtc must be Jul 9 17:00 UTC.
    /// Kills the `allDayFloating ? rawUtc.Date : ConvertTimeFromUtc(...)` conditional mutations.
    /// </summary>
    [Fact]
    public void AllDay_resolved_zone_anchors_start_to_local_midnight()
    {
        var group = SimpleGroup(e =>
        {
            e.Flags        = 8; // EVENT_ALLDAY
            e.EventStart   = MicrosFor(2026, 7, 9, 17, 0);   // Jul 10 00:00 Asia/Bangkok (UTC+7)
            e.EventStartTz = "Asia/Bangkok";
            e.EventEnd     = MicrosFor(2026, 7, 10, 17, 0);  // next day
            e.EventEndTz   = "Asia/Bangkok";
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.True(appt!.IsAllDay);
        Assert.Equal(new DateTime(2026, 7, 9, 17, 0, 0, DateTimeKind.Utc), appt.StartUtc);
    }

    /// <summary>
    /// Mutation-coverage (Stryker): an absolute alarm firing EXACTLY at the event start (delta 0) must be
    /// dropped, pinning the `minutesBefore &gt; 0` boundary (kills the `&gt;= 0` mutation that would keep it).
    /// </summary>
    [Fact]
    public void Alarm_AbsoluteExactlyAtStart_NotConverted()
    {
        // SimpleGroup starts 2026-07-10 14:00 UTC; trigger exactly at start.
        var group = SimpleGroup(e => e.Alarms.Add(new RawSideText(
            "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;VALUE=DATE-TIME:20260710T140000Z\r\nEND:VALARM")));

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.False(appt!.ReminderSet);
        Assert.Contains(warnings, x => x.Contains("at/after"));
    }

    /// <summary>
    /// Mutation-coverage (Stryker): a relative alarm with a ZERO offset (TRIGGER:PT0M, fires at start) must
    /// be dropped, pinning the `offset &lt; TimeSpan.Zero` boundary (kills the `&lt;= Zero` mutation).
    /// </summary>
    [Fact]
    public void Alarm_RelativeZeroOffset_NotConverted()
    {
        var group = SimpleGroup(e => e.Alarms.Add(new RawSideText(
            "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:PT0M\r\nEND:VALARM")));

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.False(appt!.ReminderSet);
        Assert.Contains(warnings, x => x.Contains("at/after"));
    }

    /// <summary>
    /// Should-fix (SF-A): an absolute alarm firing AT or AFTER the event start must be dropped with a
    /// warning (not silently clamped to fire-at-start), matching the relative-trigger path.
    /// </summary>
    [Fact]
    public void Alarm_AbsoluteAtOrAfterStart_NotConvertedAndWarnAndBodyPreserved()
    {
        // SimpleGroup starts 2026-07-10 14:00 UTC; absolute trigger at 15:00 UTC is after start.
        var group = SimpleGroup(e => e.Alarms.Add(new RawSideText(
            "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;VALUE=DATE-TIME:20260710T150000Z\r\nEND:VALARM")));

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.False(appt!.ReminderSet);
        Assert.Equal(0, appt.ReminderMinutesBefore);
        Assert.Contains(warnings, x => x.Contains("at/after"));
        Assert.NotNull(appt.Body);
        Assert.Contains("[Thunderbird alarm not converted:", appt.Body);
    }

    /// <summary>
    /// Should-fix (SF-A): a VALARM that parses but yields no usable trigger (no TRIGGER line) must be
    /// dropped with a warning, not silently ignored. (There is no TRIGGER text to preserve in the body.)
    /// </summary>
    [Fact]
    public void Alarm_NoTrigger_NotConvertedAndWarned()
    {
        var group = SimpleGroup(e => e.Alarms.Add(new RawSideText(
            "BEGIN:VALARM\r\nACTION:DISPLAY\r\nEND:VALARM")));

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.False(appt!.ReminderSet);
        Assert.Contains(warnings, x => x.Contains("no usable trigger"));
    }

    /// <summary>
    /// Pre-merge review #6: a RELATED=END reminder must fire relative to the event END, not START.
    /// For the 60-min SimpleGroup event (14:00–15:00 UTC) with TRIGGER;RELATED=END:-PT15M the reminder
    /// fires at 14:45 = 45 min AFTER start, so PidLidReminderDelta (minutes-before-start) is -45 and the
    /// writer's signal time (Start − delta) lands on 14:45 = End−15. Before the fix the anchor was
    /// ignored and the delta was 15, firing ~60 min early (15 min before START).
    /// </summary>
    [Fact]
    public void Alarm_RelatedEnd_AnchorsReminderToEnd()
    {
        var group = SimpleGroup(e =>
        {
            e.Alarms.Add(new RawSideText(
                "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;RELATED=END:-PT15M\r\nEND:VALARM"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt!.ReminderSet);
        Assert.Equal(-45, appt.ReminderMinutesBefore);
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
    // Recurrence (PR7a Task 2)
    // -----------------------------------------------------------------------

    [Fact]
    public void Daily_rrule_maps_spec_and_iana_id()
    {
        // RRULE:FREQ=DAILY with IANA tz — spec must be non-null, IANA id must pass through.
        var group = SimpleGroup(e =>
        {
            e.Title        = "Daily Standup";
            e.EventStartTz = "Europe/Copenhagen";
            e.EventEndTz   = "Europe/Copenhagen";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=DAILY"));
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Recurrence);
        Assert.Equal(RecurrenceFrequency.Daily, appt.Recurrence!.Frequency);
        Assert.Equal("Europe/Copenhagen", appt.OriginatingTimeZoneId);
        Assert.Equal("Europe/Copenhagen", appt.Recurrence.OriginatingTimeZoneId);
    }

    [Fact]
    public void Bysetpos_degrades_to_single_with_warning()
    {
        // BYSETPOS is not representable in RecurrenceSpec → degrade to single, never skip.
        var group = SimpleGroup(e =>
        {
            e.Title = "Last Monday";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Null(appt!.Recurrence);
        Assert.Contains(warnings, w => w.Contains("BYSETPOS"));
    }

    [Fact]
    public void Monthly_byday_without_offset_degrades()
    {
        // BYDAY without a numeric offset on a MONTHLY rule is unrepresentable → degrade.
        var group = SimpleGroup(e =>
        {
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=MONTHLY;BYDAY=MO"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Null(appt!.Recurrence);
        Assert.Contains(warnings, w => w.Contains("unrepresentable"));
    }

    [Fact]
    public void AllDay_weekly_recurrence_maps_local_midnight()
    {
        // All-day weekly event (Mon) — spec frequency, BYDAY, and EndKind must be correct.
        var group = SimpleGroup(e =>
        {
            e.Title        = "Weekly Review";
            e.Flags        = 8; // EVENT_ALLDAY
            e.EventStart   = MicrosFor(2026, 7, 13, 0, 0); // Monday 2026-07-13
            e.EventEnd     = MicrosFor(2026, 7, 14, 0, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=4"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.True(appt!.IsAllDay);
        Assert.NotNull(appt.Recurrence);
        Assert.Equal(RecurrenceFrequency.Weekly, appt.Recurrence!.Frequency);
        Assert.Contains(DayOfWeek.Monday, appt.Recurrence.DaysOfWeek);
        Assert.Equal(RecurrenceEndKind.Count, appt.Recurrence.EndKind);
        Assert.Equal(4, appt.Recurrence.Count);
        Assert.Empty(warnings);
    }

    [Fact]
    public void DateOnly_exdate_in_bangkok_converts_to_correct_utc()
    {
        // Bangkok = UTC+7; midnight July 6 Bangkok = 2026-07-05T17:00:00Z
        var group = SimpleGroup(e =>
        {
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO"));
            e.Recurrence.Add(new RawSideText("EXDATE;TZID=Asia/Bangkok;VALUE=DATE:20260706"));
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Recurrence);
        var deleted = Assert.Single(appt.DeletedOccurrences);
        Assert.True(deleted.IsDateOnly);
        var expectedUtc = new DateTime(2026, 7, 5, 17, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expectedUtc, deleted.OriginalStartUtc);
    }

    [Fact]
    public void Override_with_body_change_creates_exception_with_warning()
    {
        // An override with changed DESCRIPTION → exception in model + "changed Body not encoded" warning.
        var overrideEv = new RawEvent
        {
            Id             = "event-example-001@example.com",
            Title          = "Modified Occurrence",
            RecurrenceId   = MicrosFor(2026, 7, 11, 14, 0),
            RecurrenceIdTz = "UTC",
            EventStart     = MicrosFor(2026, 7, 11, 15, 0),
            EventEnd       = MicrosFor(2026, 7, 11, 16, 0),
        };
        overrideEv.Properties.Add(new RawProperty("DESCRIPTION", Utf8("Changed body text"), null, null));

        var group = SimpleGroup(e =>
        {
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=DAILY"));
        });
        group.Overrides.Add(overrideEv);

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Recurrence);
        var ex = Assert.Single(appt.Exceptions);
        Assert.True(ex.ChangeFlags.HasFlag(AppointmentExceptionChangeFlags.Body));
        Assert.Contains(warnings, w => w.Contains("changed Body not encoded"));
    }

    [Fact]
    public void Override_with_reminder_change_creates_exception_with_warning()
    {
        // An override with a VALARM → Reminder flag in exception + warning.
        var overrideEv = new RawEvent
        {
            Id             = "event-example-001@example.com",
            Title          = "Rescheduled",
            RecurrenceId   = MicrosFor(2026, 7, 11, 14, 0),
            RecurrenceIdTz = "UTC",
            EventStart     = MicrosFor(2026, 7, 11, 14, 0),
            EventEnd       = MicrosFor(2026, 7, 11, 15, 0),
        };
        overrideEv.Alarms.Add(new RawSideText("BEGIN:VALARM\r\nTRIGGER:-PT10M\r\nEND:VALARM"));

        var group = SimpleGroup(e =>
        {
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=DAILY"));
        });
        group.Overrides.Add(overrideEv);

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        var ex = Assert.Single(appt!.Exceptions);
        Assert.True(ex.ChangeFlags.HasFlag(AppointmentExceptionChangeFlags.Reminder));
        Assert.Contains(warnings, w => w.Contains("changed Reminder not encoded"));
    }

    [Fact]
    public void Override_with_categories_creates_exception_with_warning()
    {
        // An override with CATEGORIES → Categories flag in exception + warning.
        var overrideEv = new RawEvent
        {
            Id             = "event-example-001@example.com",
            Title          = "Recategorized",
            RecurrenceId   = MicrosFor(2026, 7, 11, 14, 0),
            RecurrenceIdTz = "UTC",
            EventStart     = MicrosFor(2026, 7, 11, 14, 0),
            EventEnd       = MicrosFor(2026, 7, 11, 15, 0),
        };
        overrideEv.Properties.Add(new RawProperty("CATEGORIES", Utf8("Work"), null, null));

        var group = SimpleGroup(e =>
        {
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=DAILY"));
        });
        group.Overrides.Add(overrideEv);

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        var ex = Assert.Single(appt!.Exceptions);
        Assert.True(ex.ChangeFlags.HasFlag(AppointmentExceptionChangeFlags.Categories));
        Assert.Contains(warnings, w => w.Contains("changed Categories not encoded"));
    }

    [Fact]
    public void ComputeLastInstanceUtc_Weekly_Count_Returns_Nth_Occurrence()
    {
        // WEEKLY MO+WE COUNT=6 from Wed 2026-07-01 14:00 UTC.
        // Raw occurrences: 1-Jul, 6-Jul, 8-Jul, 13-Jul, 15-Jul, 20-Jul → 6th = Mon 20-Jul.
        var group = SimpleGroup(e =>
        {
            e.EventStart   = MicrosFor(2026, 7, 1, 14, 0);
            e.EventEnd     = MicrosFor(2026, 7, 1, 15, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=6"));
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt?.Recurrence);
        Assert.Equal(RecurrenceEndKind.Count, appt!.Recurrence!.EndKind);
        Assert.Equal(6, appt.Recurrence.Count);
        var expected = new DateTime(2026, 7, 20, 14, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, appt.Recurrence.LastInstanceStartUtc);
    }

    [Fact]
    public void ComputeLastInstanceUtc_Weekly_Until_ReturnsUntilDirectly()
    {
        // Until-bounded rule: LastInstanceStartUtc must equal UntilUtc without enumeration.
        var group = SimpleGroup(e =>
        {
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO;UNTIL=20260831T000000Z"));
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt?.Recurrence);
        Assert.Equal(RecurrenceEndKind.Until, appt!.Recurrence!.EndKind);
        Assert.NotNull(appt.Recurrence.UntilUtc);
        // Must be the same value — returned directly, no enumeration.
        Assert.Equal(appt.Recurrence.UntilUtc, appt.Recurrence.LastInstanceStartUtc);
    }

    [Fact]
    public void ComputeLastInstanceUtc_CountWithExdate_CountNotReduced()
    {
        // EXDATE removes an instance from the visible set but does NOT reduce COUNT.
        // Raw occurrences: Jul 6, Jul 13, Jul 20 → COUNT=3 → last = Jul 20 (EXDATE on Jul 13 ignored).
        var group = SimpleGroup(e =>
        {
            e.EventStart   = MicrosFor(2026, 7, 6, 14, 0);   // Monday
            e.EventEnd     = MicrosFor(2026, 7, 6, 15, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=3"));
            e.Recurrence.Add(new RawSideText("EXDATE:20260713T140000Z")); // delete 2nd raw occurrence
        });

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt?.Recurrence);
        // 3rd raw occurrence is Jul 20 — not Jul 27 (which would wrongly count EXDATE as consuming one slot).
        var expected = new DateTime(2026, 7, 20, 14, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, appt!.Recurrence!.LastInstanceStartUtc);
        // EXDATE is still tracked as a deleted occurrence.
        Assert.Single(appt.DeletedOccurrences);
    }

    // -----------------------------------------------------------------------
    // I1: bare FREQ=WEEKLY with no BYDAY must default to DTSTART weekday (RFC 5545)
    // -----------------------------------------------------------------------

    [Fact]
    public void Weekly_bare_rrule_no_byday_defaults_to_dtstart_weekday()
    {
        // RRULE:FREQ=WEEKLY with no BYDAY — must default DaysOfWeek to the DTSTART weekday
        // per RFC 5545 §3.3.10.  2026-07-06 is a Monday.
        var group = SimpleGroup(e =>
        {
            e.Title        = "Bare Weekly";
            e.EventStart   = MicrosFor(2026, 7, 6, 14, 0); // Monday 2026-07-06
            e.EventEnd     = MicrosFor(2026, 7, 6, 15, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=WEEKLY"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Recurrence);
        Assert.Equal(RecurrenceFrequency.Weekly, appt.Recurrence!.Frequency);
        // RFC 5545: bare FREQ=WEEKLY (no BYDAY) must recur on the DTSTART weekday (Monday here).
        Assert.Equal(new[] { DayOfWeek.Monday }, appt.Recurrence.DaysOfWeek);
        Assert.Empty(warnings);
    }

    // -----------------------------------------------------------------------
    // Bare FREQ=YEARLY / FREQ=MONTHLY (no BY* parts) — recur on DTSTART, don't degrade
    // -----------------------------------------------------------------------

    [Fact]
    public void Yearly_bare_rrule_maps_to_yearly_not_degraded()
    {
        // RRULE:FREQ=YEARLY with no BY* parts recurs on the DTSTART month + day-of-month
        // (RFC 5545). It is representable (writer defaults day from DTSTART, Outlook derives
        // the month from the start date) and must NOT degrade to a single occurrence.
        var group = SimpleGroup(e =>
        {
            e.Title        = "Bare Yearly";
            e.EventStart   = MicrosFor(2026, 7, 1, 9, 0);
            e.EventEnd     = MicrosFor(2026, 7, 1, 10, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=YEARLY"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Recurrence);
        Assert.Equal(RecurrenceFrequency.Yearly, appt.Recurrence!.Frequency);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Monthly_bare_rrule_maps_to_monthly_not_degraded()
    {
        // RRULE:FREQ=MONTHLY with no BY* parts recurs on the DTSTART day-of-month (RFC 5545).
        var group = SimpleGroup(e =>
        {
            e.Title        = "Bare Monthly";
            e.EventStart   = MicrosFor(2026, 7, 15, 9, 0);
            e.EventEnd     = MicrosFor(2026, 7, 15, 10, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=MONTHLY"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Recurrence);
        Assert.Equal(RecurrenceFrequency.Monthly, appt.Recurrence!.Frequency);
        Assert.Empty(warnings);
    }

    // -----------------------------------------------------------------------
    // Yearly recurrence — offset-range guard (finding 1 follow-up tests)
    // -----------------------------------------------------------------------

    [Fact]
    public void Yearly_5th_byday_degrades_to_single_with_warning()
    {
        // BYDAY=5MO on a YEARLY rule — 5th Monday is unsupported (only 1..4 and -1 are valid).
        // Must degrade to single occurrence and emit a warning; must NOT return null.
        var group = SimpleGroup(e =>
        {
            e.EventStart   = MicrosFor(2026, 3, 1, 9, 0);
            e.EventEnd     = MicrosFor(2026, 3, 1, 10, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=YEARLY;BYDAY=5MO;BYMONTH=3"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Null(appt!.Recurrence);
        Assert.Contains(warnings, w => w.Contains("unrepresentable"));
    }

    [Fact]
    public void Yearly_2nd_byday_maps_to_YearlyNth()
    {
        // BYDAY=2MO;BYMONTH=3 on YEARLY — second Monday in March, valid offset (2).
        // Must map to YearlyNth with NthOccurrence=2.
        var group = SimpleGroup(e =>
        {
            e.EventStart   = MicrosFor(2026, 3, 9, 9, 0); // second Monday in March 2026
            e.EventEnd     = MicrosFor(2026, 3, 9, 10, 0);
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
            e.Recurrence.Add(new RawSideText("RRULE:FREQ=YEARLY;BYDAY=2MO;BYMONTH=3"));
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Recurrence);
        Assert.Equal(RecurrenceFrequency.YearlyNth, appt.Recurrence!.Frequency);
        Assert.Equal(2, appt.Recurrence.NthOccurrence);
        Assert.Empty(warnings);
    }

    // -----------------------------------------------------------------------
    // Mapper-level timezone integration (finding 2)
    // -----------------------------------------------------------------------

    [Fact]
    public void Timezone_AsiaBangkok_PreservesIanaIdAndResolvesToUtcPlus7()
    {
        // Asia/Bangkok = UTC+7 (no DST). Must resolve without warning and set TimeZone.
        var group = SimpleGroup(e =>
        {
            e.EventStartTz = "Asia/Bangkok";
            e.EventEndTz   = "Asia/Bangkok";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Equal("Asia/Bangkok", appt!.OriginatingTimeZoneId);
        Assert.NotNull(appt.TimeZone);
        Assert.Equal(TimeSpan.FromHours(7), appt.TimeZone!.BaseUtcOffset);
        // No timezone-resolution warning.
        Assert.DoesNotContain(warnings, w => w.Contains("unresolved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Timezone_TzoneMicrosoftUtc_ResolvesToUtcNoWarning()
    {
        // tzone://Microsoft/Utc is the canonical Thunderbird id for UTC.
        // Must resolve to TimeZoneInfo.Utc with no warning.
        var group = SimpleGroup(e =>
        {
            e.EventStartTz = "tzone://Microsoft/Utc";
            e.EventEndTz   = "tzone://Microsoft/Utc";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.TimeZone);
        Assert.Equal(TimeZoneInfo.Utc, appt.TimeZone);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Timezone_Utc_ResolvesToUtcNoWarning()
    {
        // Plain "UTC" id must resolve to TimeZoneInfo.Utc with no warning.
        var group = SimpleGroup(e =>
        {
            e.EventStartTz = "UTC";
            e.EventEndTz   = "UTC";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.TimeZone);
        Assert.Equal(TimeZoneInfo.Utc, appt.TimeZone);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Timezone_Unresolvable_EmitsWarningAndPreservesOriginalId()
    {
        // "Mars/Phobos" is not a known timezone. The mapper must:
        //   1. Add a warning naming the unresolved zone.
        //   2. Preserve OriginatingTimeZoneId verbatim.
        //   3. Fall back to appt.TimeZone = null (timed event with unresolvable tz → stored as UTC instant).
        var group = SimpleGroup(e =>
        {
            e.EventStartTz = "Mars/Phobos";
            e.EventEndTz   = "Mars/Phobos";
        });

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Equal("Mars/Phobos", appt!.OriginatingTimeZoneId);
        Assert.Null(appt.TimeZone);
        Assert.Contains(warnings, w => w.Contains("Mars/Phobos", StringComparison.OrdinalIgnoreCase));
    }
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
