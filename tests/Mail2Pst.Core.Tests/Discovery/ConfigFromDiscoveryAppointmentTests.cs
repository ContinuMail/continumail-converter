// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class ConfigFromDiscoveryAppointmentTests
{
    private static DiscoveryResult SampleEventOnly(int eventCount = 3) => new(
        Root: "/data/tb/profile",
        Layout: "thunderbird-profile",
        Sources: new[]
        {
            new DiscoveredSource("/p/Inbox", "mbox", new[] { "Local Folders", "Inbox" }, "Inbox", 10, "/p/Inbox.msf"),
        },
        Warnings: Array.Empty<DiscoveryWarning>(),
        Skipped: Array.Empty<DiscoverySkipped>(),
        Pairing: new DiscoveryPairingSummary(1, 0, 0))
    {
        Calendars = new List<DiscoveredCalendarSource>
        {
            new()
            {
                CalId = "cal-guid-1",
                DisplayName = "Personal",
                StoreKind = "local",
                StorePath = "/p/local.sqlite",
                CalendarType = "calendar",
                IsVisibleInThunderbird = true,
                EventCount = eventCount,
                TaskCount = 0,
                DefaultCalendarFolderPath = new[] { "Calendars", "Personal" },
                DefaultTaskFolderPath = Array.Empty<string>(),
            },
        },
    };

    private static DiscoveryResult SampleBothEventsAndTasks() => new(
        Root: "/data/tb/profile",
        Layout: "thunderbird-profile",
        Sources: new[]
        {
            new DiscoveredSource("/p/Inbox", "mbox", new[] { "Local Folders", "Inbox" }, "Inbox", 10, "/p/Inbox.msf"),
        },
        Warnings: Array.Empty<DiscoveryWarning>(),
        Skipped: Array.Empty<DiscoverySkipped>(),
        Pairing: new DiscoveryPairingSummary(1, 0, 0))
    {
        Calendars = new List<DiscoveredCalendarSource>
        {
            new()
            {
                CalId = "cal-guid-2",
                DisplayName = "Work",
                StoreKind = "local",
                StorePath = "/p/local.sqlite",
                CalendarType = "both",
                IsVisibleInThunderbird = true,
                EventCount = 5,
                TaskCount = 3,
                DefaultCalendarFolderPath = new[] { "Calendars", "Work" },
                DefaultTaskFolderPath = new[] { "Tasks", "Work" },
            },
        },
    };

    [Fact]
    public void Build_IncludeAppointments_SynthesizesCalendarSource()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(SampleEventOnly(), includeAppointments: true);
        Assert.Contains(config.Outputs.SelectMany(o => o.Calendars), c => c.StorePath == "/p/local.sqlite");
    }

    [Fact]
    public void Build_IncludeAppointments_FlagsAndFolderPathMatchDefault()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(SampleEventOnly(), includeAppointments: true);
        var cal = config.Outputs.SelectMany(o => o.Calendars).Single(c => c.StorePath == "/p/local.sqlite");
        Assert.True(cal.IncludeAppointments);
        Assert.Equal(new[] { "Calendars", "Personal" }, cal.AppointmentFolderPath);
    }

    [Fact]
    public void Build_NoAppointments_OmitsCalendarSources()
    {
        // SampleEventOnly has EventCount=3, TaskCount=0; with includeAppointments=false
        // and includeTasks=true (default), no condition is met → nothing synthesized.
        ConversionConfig config = ConfigFromDiscovery.Build(SampleEventOnly(), includeAppointments: false);
        Assert.Empty(config.Outputs.SelectMany(o => o.Calendars));
    }

    [Fact]
    public void Build_BothEventsAndTasks_ProducesOneConfigCarryingBoth()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(
            SampleBothEventsAndTasks(), includeAppointments: true, includeTasks: true);

        var cals = config.Outputs.SelectMany(o => o.Calendars).ToList();
        Assert.Single(cals);

        var cal = cals[0];
        Assert.True(cal.IncludeAppointments);
        Assert.True(cal.IncludeTasks);
        Assert.Equal(new[] { "Calendars", "Work" }, cal.AppointmentFolderPath);
        Assert.Equal(new[] { "Tasks", "Work" }, cal.TaskFolderPath);
    }
}
