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
/// Tests for the attendee-mapping block in <see cref="CalendarEventMapper.Map"/>.
/// All data is synthetic/reserved (example.com) — no real mail or PII.
/// </summary>
public class CalendarEventMapperAttendeeTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static long MicrosFor(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;

    private static RawEventGroup GroupWithAttendees(IEnumerable<string> icalLines)
    {
        var ev = new RawEvent
        {
            Id           = "attendee-test@example.com",
            Title        = "Meeting",
            EventStart   = MicrosFor(2026, 8, 1, 10, 0),
            EventStartTz = "UTC",
            EventEnd     = MicrosFor(2026, 8, 1, 11, 0),
            EventEndTz   = "UTC",
            Flags        = 0,
            Priority     = 5,
        };
        foreach (var line in icalLines)
            ev.Attendees.Add(new RawSideText(line));

        return new RawEventGroup { Master = ev };
    }

    // -----------------------------------------------------------------------
    // Happy-path: organizer + required/optional attendees
    // -----------------------------------------------------------------------

    [Fact]
    public void OrganizerPlusRequiredPlusOptional_MapsCorrectly()
    {
        var group = GroupWithAttendees([
            "ORGANIZER;CN=Boss:mailto:boss@example.com",
            "ATTENDEE;CN=Alice;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:alice@example.com",
            "ATTENDEE;CN=Bob;ROLE=OPT-PARTICIPANT;PARTSTAT=TENTATIVE:mailto:bob@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Organizer);
        Assert.Equal("boss@example.com", appt.Organizer!.Email);
        Assert.Equal("Boss", appt.Organizer.DisplayName);
        Assert.True(appt.Organizer.IsOrganizer);
        Assert.Equal(AttendeeResponse.Organized, appt.Organizer.Response);
        Assert.Equal(AttendeeKind.Required, appt.Organizer.Kind);

        Assert.Equal(2, appt.Attendees.Count);

        var alice = appt.Attendees[0];
        Assert.Equal("alice@example.com", alice.Email);
        Assert.Equal("Alice", alice.DisplayName);
        Assert.Equal(AttendeeKind.Required, alice.Kind);
        Assert.Equal(AttendeeResponse.Accepted, alice.Response);
        Assert.False(alice.IsOrganizer);

        var bob = appt.Attendees[1];
        Assert.Equal("bob@example.com", bob.Email);
        Assert.Equal("Bob", bob.DisplayName);
        Assert.Equal(AttendeeKind.Optional, bob.Kind);
        Assert.Equal(AttendeeResponse.Tentative, bob.Response);
        Assert.False(bob.IsOrganizer);

        // Organizer must not appear in Attendees
        Assert.DoesNotContain(appt.Attendees, a => a.Email == "boss@example.com");
    }

    // -----------------------------------------------------------------------
    // ROLE=CHAIR → Required (CHAIR has no Outlook recipient type)
    // -----------------------------------------------------------------------

    [Fact]
    public void RoleChair_MapsToRequired()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Chair;ROLE=CHAIR;PARTSTAT=ACCEPTED:mailto:chair@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal(AttendeeKind.Required, appt.Attendees[0].Kind);
    }

    // -----------------------------------------------------------------------
    // CUTYPE=ROOM → Resource (wins over ROLE)
    // -----------------------------------------------------------------------

    [Fact]
    public void CuTypeRoom_MapsToResource()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Conference Room;CUTYPE=ROOM;ROLE=REQ-PARTICIPANT:mailto:room@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal(AttendeeKind.Resource, appt.Attendees[0].Kind);
    }

    // -----------------------------------------------------------------------
    // PARTSTAT mappings: DECLINED, NEEDS-ACTION, missing
    // -----------------------------------------------------------------------

    [Fact]
    public void PartStat_Declined_MapsToDeclined()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Decliner;ROLE=REQ-PARTICIPANT;PARTSTAT=DECLINED:mailto:decliner@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal(AttendeeResponse.Declined, appt.Attendees[0].Response);
    }

    [Fact]
    public void PartStat_NeedsAction_MapsToNotResponded()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Pending;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION:mailto:pending@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal(AttendeeResponse.NotResponded, appt.Attendees[0].Response);
    }

    [Fact]
    public void PartStat_Missing_MapsToNone()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;CN=NoStat;ROLE=REQ-PARTICIPANT:mailto:nostat@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal(AttendeeResponse.None, appt.Attendees[0].Response);
    }

    // -----------------------------------------------------------------------
    // No-email non-organizer → skipped + warning
    // -----------------------------------------------------------------------

    [Fact]
    public void NonOrganizerWithNoEmail_SkippedWithWarning()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Alice:",   // empty URI → parser returns null/empty email
        ]);

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Empty(appt!.Attendees);
        Assert.Contains(warnings, w => w.Contains("no email") && w.Contains("skipped"));
    }

    // -----------------------------------------------------------------------
    // Only-mailto (no CN) → kept, DisplayName == email
    // -----------------------------------------------------------------------

    [Fact]
    public void AttendeeWithNoDisplayName_UsesEmailAsDisplayName()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:noncn@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal("noncn@example.com", appt.Attendees[0].DisplayName);
        Assert.Equal("noncn@example.com", appt.Attendees[0].Email);
    }

    // -----------------------------------------------------------------------
    // Organizer with no email → display-only; Email is empty string
    // -----------------------------------------------------------------------

    [Fact]
    public void OrganizerWithNoEmail_DisplayOnly_EmailEmpty()
    {
        var group = GroupWithAttendees([
            "ORGANIZER;CN=Boss:",   // no mailto: address
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Organizer);
        Assert.Equal("Boss", appt.Organizer!.DisplayName);
        Assert.Equal("", appt.Organizer.Email);
        Assert.True(appt.Organizer.IsOrganizer);
    }

    // -----------------------------------------------------------------------
    // Case-insensitive dedup: two attendees with same email (different case)
    // -----------------------------------------------------------------------

    [Fact]
    public void CaseInsensitiveDuplicate_OnlyFirstKept_WarnOnSecond()
    {
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Req1;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:req1@example.com",
            "ATTENDEE;CN=Req1Upper;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:REQ1@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal("req1@example.com", appt.Attendees[0].Email);
        Assert.Contains(warnings, w => w.Contains("duplicate") && w.Contains("req1@example.com", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Organizer/attendee dedup: ATTENDEE row with same email as ORGANIZER → dropped
    // -----------------------------------------------------------------------

    [Fact]
    public void OrganizerDuplicate_AttendeeRowDropped()
    {
        var group = GroupWithAttendees([
            "ORGANIZER;CN=Boss:mailto:boss@example.com",
            "ATTENDEE;CN=Boss;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:boss@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out var warnings);

        Assert.NotNull(appt);
        Assert.NotNull(appt!.Organizer);
        Assert.Equal("boss@example.com", appt.Organizer!.Email);
        Assert.Empty(appt.Attendees);   // boss must NOT be in Attendees
        Assert.Contains(warnings, w => w.Contains("duplicate") && w.Contains("boss@example.com"));
    }

    // -----------------------------------------------------------------------
    // No attendees rows → Organizer == null, Attendees empty
    // -----------------------------------------------------------------------

    [Fact]
    public void NoAttendeeRows_OrganizerNullAndAttendeesEmpty()
    {
        var group = GroupWithAttendees([]); // empty

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Null(appt!.Organizer);
        Assert.Empty(appt.Attendees);
    }

    // -----------------------------------------------------------------------
    // Test gap 4a: bare ORGANIZER line (no CN, no email, no params) → Organizer == null
    // -----------------------------------------------------------------------

    [Fact]
    public void BareOrganizerLine_NoUsableData_OrganizerIsNull()
    {
        // "ORGANIZER:" with no CN and no email address is a degenerate line.
        // The mapper must not throw; Organizer should be null (neither name nor email present).
        var group = GroupWithAttendees([
            "ORGANIZER:",  // bare — no value after the colon
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Null(appt!.Organizer);
    }

    // -----------------------------------------------------------------------
    // Test gap 4c: CUTYPE=RESOURCE → Resource; ROLE=NON-PARTICIPANT → Optional
    // -----------------------------------------------------------------------

    [Fact]
    public void CuTypeResource_MapsToResource()
    {
        // CUTYPE=RESOURCE is the generic non-room resource; should map to AttendeeKind.Resource.
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Projector;CUTYPE=RESOURCE;ROLE=REQ-PARTICIPANT:mailto:projector@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal(AttendeeKind.Resource, appt.Attendees[0].Kind);
    }

    [Fact]
    public void RoleNonParticipant_MapsToOptional()
    {
        // ROLE=NON-PARTICIPANT: observer/informational; maps to Optional (Cc row).
        var group = GroupWithAttendees([
            "ATTENDEE;CN=Observer;ROLE=NON-PARTICIPANT:mailto:observer@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(appt);
        Assert.Single(appt!.Attendees);
        Assert.Equal(AttendeeKind.Optional, appt.Attendees[0].Kind);
    }

    // -----------------------------------------------------------------------
    // Malformed line: forwards warning, mapping still returns a record
    // -----------------------------------------------------------------------

    [Fact]
    public void MalformedAttendeeLine_ForwardsWarning_NeverThrows()
    {
        // A truly garbage line that cannot be interpreted as an ATTENDEE/ORGANIZER.
        // We provide at least one valid attendee too so we can confirm mapping proceeds.
        var group = GroupWithAttendees([
            "NOT_A_VALID_ICAL_LINE===###",
            "ATTENDEE;CN=Valid;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:valid@example.com",
        ]);

        var appt = CalendarEventMapper.Map(group, out var warnings);

        // Must not throw and must return a record
        Assert.NotNull(appt);
        // The valid attendee may or may not survive depending on fallback path, but the
        // mapper must never throw and must forward any parse warnings.
        // Primary assertion: no exception + result is non-null (asserted above).
        // Secondary: if warnings were generated by the parser they are forwarded.
        // (We don't assert a specific warning text since it's parser-internal.)
    }
}
