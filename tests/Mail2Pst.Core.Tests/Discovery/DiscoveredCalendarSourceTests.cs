// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class DiscoveredCalendarSourceTests
{
    [Fact]
    public void Defaults_are_safe()
    {
        var s = new DiscoveredCalendarSource();
        Assert.Empty(s.DefaultCalendarFolderPath);
        Assert.Empty(s.DefaultTaskFolderPath);
        var r = new CalendarDiscoveryResult();
        Assert.Empty(r.Calendars);
        Assert.Empty(r.Warnings);
    }
}
