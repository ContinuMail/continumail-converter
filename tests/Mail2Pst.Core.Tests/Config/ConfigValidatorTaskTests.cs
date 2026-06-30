// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class ConfigValidatorTaskTests
{
    [Fact]
    public void Validate_CalendarOnlyOutput_IsLegal()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Tasks",
                    Sources = new List<SourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "local.sqlite",
                            CalId = "home",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Home" },
                        },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_EmptyStorePath_Throws()
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
                            StorePath = "",
                            CalId = "home",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Home" },
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_IncludeAppointmentsTrueWithTasksTrue_Validates()
    {
        // PR5: IncludeAppointments is now supported — both flags true on the same calendar is legal.
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
                            StorePath = "local.sqlite",
                            CalId = "home",
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "Home" },
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "Home" },
                        },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_BothFlagsFalse_Throws()
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
                            StorePath = "local.sqlite",
                            CalId = "home",
                            IncludeAppointments = false,
                            IncludeTasks = false,
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_IncludeTasksTrueNullCalIdNoTaskFolder_Throws()
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
                            StorePath = "local.sqlite",
                            CalId = "",           // empty = "all calendars in store"
                            IncludeTasks = true,
                            TaskFolderPath = null, // no explicit path → can't build default
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_IncludeTasksTrueNullCalIdWithExplicitTaskFolder_DoesNotThrow()
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
                            StorePath = "local.sqlite",
                            CalId = "",                                    // all calendars
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "All" },    // explicit path provided
                        },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_EmptyElementInTaskFolderPath_Throws()
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
                            StorePath = "local.sqlite",
                            CalId = "home",
                            IncludeTasks = true,
                            TaskFolderPath = new[] { "Tasks", "" },   // empty segment
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_EmptyGroupWithNoSourcesContactsOrCalendars_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Empty",
                    Sources = new List<SourceConfig>(),
                    Contacts = new List<ContactSourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>(),
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }
}
