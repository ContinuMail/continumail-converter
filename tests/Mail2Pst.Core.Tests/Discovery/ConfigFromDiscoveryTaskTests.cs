// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class ConfigFromDiscoveryTaskTests
{
    private static DiscoveryResult Sample(int taskCount = 2) => new(
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
                DisplayName = "Personal Tasks",
                StoreKind = "local",
                StorePath = "/p/local.sqlite",
                CalendarType = "task",
                IsVisibleInThunderbird = true,
                EventCount = 0,
                TaskCount = taskCount,
                DefaultCalendarFolderPath = Array.Empty<string>(),
                DefaultTaskFolderPath = new[] { "Tasks", "Personal Tasks" },
            },
        },
    };

    [Fact]
    public void Build_IncludeTasks_SynthesizesCalendarSource()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(), includeTasks: true);
        Assert.Contains(config.Outputs.SelectMany(o => o.Calendars), c => c.StorePath == "/p/local.sqlite");
    }

    [Fact]
    public void Build_IncludeTasks_TaskFolderPathMatchesDefault()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(), includeTasks: true);
        var cal = config.Outputs.SelectMany(o => o.Calendars).Single(c => c.StorePath == "/p/local.sqlite");
        Assert.Equal(new[] { "Tasks", "Personal Tasks" }, cal.TaskFolderPath);
    }

    [Fact]
    public void Build_NoTasks_OmitsCalendarSources()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(), includeTasks: false);
        Assert.Empty(config.Outputs.SelectMany(o => o.Calendars));
    }

    [Fact]
    public void Build_CalendarTaskCountZero_NotSynthesized()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(taskCount: 0), includeTasks: true);
        Assert.Empty(config.Outputs.SelectMany(o => o.Calendars));
    }
}
