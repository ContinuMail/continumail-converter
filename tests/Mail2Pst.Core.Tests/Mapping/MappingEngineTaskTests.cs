// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Xunit;

namespace Mail2Pst.Core.Tests.Mapping;

public class MappingEngineTaskTests
{
    [Fact]
    public void BuildPlan_TaskExplicitFolder_UsesCustomPath()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    Sources = new List<SourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "home",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Home" },
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        TaskMapping tm = plan.TaskMappings.Single();
        Assert.Equal(new[] { "Tasks", "Home" }, tm.TargetFolderPath);
        Assert.Same(config.Outputs[0].Calendars[0], tm.Source);
    }

    [Fact]
    public void BuildPlan_TaskDefaultFolder_IsTasksSlashCalId()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    Sources = new List<SourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "home-cal",
                            IncludeTasks = true,
                            // TaskFolderPath intentionally null → should default to ["Tasks", CalId]
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        TaskMapping tm = plan.TaskMappings.Single();
        Assert.Equal(new[] { "Tasks", "home-cal" }, tm.TargetFolderPath);
    }

    [Fact]
    public void BuildPlan_TwoCalendarsInSameStore_ProduceTwoDistinctMappings()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    Sources = new List<SourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "work",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Work" },
                        },
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "personal",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Personal" },
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        Assert.Equal(2, plan.TaskMappings.Count);
        Assert.Equal(new[] { "Tasks", "Work" }, plan.TaskMappings[0].TargetFolderPath);
        Assert.Equal(new[] { "Tasks", "Personal" }, plan.TaskMappings[1].TargetFolderPath);
    }

    [Fact]
    public void BuildPlan_TaskPathCollidesWithMailFolder_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    FolderMapping = FolderMappingMode.Flatten,
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "/p/x.mbox", Type = "mbox",
                                TargetFolderPath = new List<string> { "Tasks", "Home" } },
                    },
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "home",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Home" },
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => MappingEngine.BuildPlan(config));
    }

    [Fact]
    public void BuildPlan_TaskPathCollidesWithContactFolder_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    Sources = new List<SourceConfig>(),
                    Contacts = new List<ContactSourceConfig>
                    {
                        new()
                        {
                            Path = "/p/abook.sqlite",
                            Format = "thunderbird-sqlite",
                            TargetFolderPath = new[] { "Tasks", "Home" },
                        },
                    },
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "home",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Home" },
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => MappingEngine.BuildPlan(config));
    }

    [Fact]
    public void BuildPlan_IncludeTasksFalse_NoTaskMapping()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "/p/x.mbox", Type = "mbox" },
                    },
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "home",
                            IncludeTasks = false,
                            // IncludeAppointments defaults false too, but we bypass validator here
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        Assert.Empty(plan.TaskMappings);
    }
}
