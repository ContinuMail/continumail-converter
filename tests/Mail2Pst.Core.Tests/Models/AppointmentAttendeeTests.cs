// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Models;

public class AppointmentAttendeeTests
{
    [Fact]
    public void Attendee_defaults_are_required_no_response_not_organizer()
    {
        var att = new AppointmentAttendee { DisplayName = "Req One", Email = "req1@example.com" };
        Assert.Equal(AttendeeKind.Required, att.Kind);
        Assert.Equal(AttendeeResponse.None, att.Response);
        Assert.False(att.IsOrganizer);
    }

    [Fact]
    public void Response_enum_uses_canonical_msoxocal_values()
    {
        Assert.Equal(1, (int)AttendeeResponse.Organized);
        Assert.Equal(2, (int)AttendeeResponse.Tentative);
        Assert.Equal(3, (int)AttendeeResponse.Accepted);
        Assert.Equal(4, (int)AttendeeResponse.Declined);
        Assert.Equal(5, (int)AttendeeResponse.NotResponded);
    }

    [Fact]
    public void AppointmentRecord_defaults_to_no_organizer_and_empty_attendees()
    {
        var a = new AppointmentRecord { Subject = "Sync" };
        Assert.Null(a.Organizer);
        Assert.Empty(a.Attendees);
    }
}
