// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class ParseAttendeesTests
{
    [Fact]
    public void Attendee_and_organizer_with_cutype_and_roles()
    {
        var r = ICalTextParser.ParseAttendees(new[]{
            "ATTENDEE;CN=Alice;PARTSTAT=ACCEPTED;ROLE=REQ-PARTICIPANT;CUTYPE=INDIVIDUAL:mailto:alice@example.com",
            "ATTENDEE;ROLE=OPT-PARTICIPANT;CUTYPE=ROOM:mailto:room1@example.com",
            "ORGANIZER;CN=Boss:mailto:boss@example.com"});
        var list = r.Value!;
        var a = list.Single(x => x.Email == "alice@example.com");
        Assert.Equal("Alice", a.CommonName); Assert.Equal("ACCEPTED", a.ParticipationStatus);
        Assert.Equal("REQ-PARTICIPANT", a.Role); Assert.Equal("INDIVIDUAL", a.CuType);
        Assert.Equal("ROOM", list.Single(x => x.Email=="room1@example.com").CuType);
        Assert.Contains(list, x => x.IsOrganizer && x.Email == "boss@example.com");
    }

    [Fact]
    public void Malformed_attendee_line_returns_non_null_list_no_throw()
        => Assert.NotNull(ICalTextParser.ParseAttendees(new[]{"ATTENDEE;:::garbage"}).Value); // contract: Value is empty-or-partial, never null
}
