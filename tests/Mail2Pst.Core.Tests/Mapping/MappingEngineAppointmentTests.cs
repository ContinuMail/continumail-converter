// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Xunit;

namespace Mail2Pst.Core.Tests.Mapping;

public class MappingEngineAppointmentTests
{
    [Fact]
    public void BuildPlan_AppointmentExplicitFolder_UsesCustomPath()
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
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "Home" },
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        AppointmentMapping am = plan.AppointmentMappings.Single();
        Assert.Equal(new[] { "Calendars", "Home" }, am.TargetFolderPath);
        Assert.Same(config.Outputs[0].Calendars[0], am.Source);
    }

    [Fact]
    public void BuildPlan_AppointmentDefaultFolder_IsCalendarsSlashCalId()
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
                            IncludeAppointments = true,
                            // AppointmentFolderPath intentionally null → should default to ["Calendars", CalId]
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        AppointmentMapping am = plan.AppointmentMappings.Single();
        Assert.Equal(new[] { "Calendars", "home-cal" }, am.TargetFolderPath);
    }

    [Fact]
    public void BuildPlan_CalendarWithBothFlags_ProducesTaskMappingAndAppointmentMapping()
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
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "Home" },
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        Assert.Single(plan.TaskMappings);
        Assert.Single(plan.AppointmentMappings);
        Assert.Equal(new[] { "Tasks", "Home" }, plan.TaskMappings[0].TargetFolderPath);
        Assert.Equal(new[] { "Calendars", "Home" }, plan.AppointmentMappings[0].TargetFolderPath);
    }

    [Fact]
    public void BuildPlan_AppointmentPathCollidesWithMailFolder_Throws()
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
                                TargetFolderPath = new List<string> { "Calendars", "Home" } },
                    },
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "home",
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "Home" },
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => MappingEngine.BuildPlan(config));
    }

    [Fact]
    public void BuildPlan_AppointmentPathCollidesWithContactFolder_Throws()
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
                            TargetFolderPath = new[] { "Calendars", "Home" },
                        },
                    },
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "/p/local.sqlite",
                            CalId = "home",
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "Home" },
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => MappingEngine.BuildPlan(config));
    }

    [Fact]
    public void BuildPlan_AppointmentPathCollidesWithTaskFolder_Throws()
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
                            TaskFolderPath = new[] { "Shared", "home" },
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Shared", "home" },
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => MappingEngine.BuildPlan(config));
    }

    [Fact]
    public void BuildPlan_IncludeAppointmentsFalse_NoAppointmentMapping()
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
                            IncludeAppointments = false,
                            // IncludeTasks not set (defaults to false); this test only asserts
                            // that no appointment mapping is produced when IncludeAppointments=false.
                        },
                    },
                },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        Assert.Empty(plan.AppointmentMappings);
    }
}
