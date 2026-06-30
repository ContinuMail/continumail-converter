// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Models;

public class AppointmentRecordTests
{
    [Fact]
    public void Defaults_are_busy_normal_importance_not_allday_no_reminder()
    {
        var a = new AppointmentRecord { Subject = "Standup" };
        Assert.Equal(2, a.BusyStatus);          // Busy
        Assert.Equal(1, a.Importance);          // Normal
        Assert.False(a.IsAllDay);
        Assert.False(a.ReminderSet);
        Assert.Null(a.TimeZone);
        Assert.Empty(a.Categories);
    }
}
