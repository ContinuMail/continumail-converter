// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class ConfigValidatorAppointmentTests
{
    [Fact]
    public void Validate_IncludeAppointmentsTrue_Validates()
    {
        // PR5: IncludeAppointments is now supported — must NOT throw.
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
                        },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_AppointmentsOnlyGroup_IsLegal()
    {
        // A group that contributes only appointments (no mail, no contacts, no tasks).
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Calendars",
                    Sources = new List<SourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath = "local.sqlite",
                            CalId = "home",
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "Home" },
                        },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_IncludeAppointmentsTrueNullCalIdNoAppointmentFolder_Throws()
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
                            CalId = "",                   // empty = "all calendars in store"
                            IncludeTasks = false,         // appointments only
                            IncludeAppointments = true,
                            AppointmentFolderPath = null, // no explicit path → can't build default
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_IncludeAppointmentsTrueNullCalIdWithExplicitAppointmentFolder_DoesNotThrow()
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
                            CalId = "",                                           // all calendars
                            IncludeTasks = false,                                 // appointments only
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "All" }, // explicit path
                        },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_EmptyElementInAppointmentFolderPath_Throws()
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
                            IncludeAppointments = true,
                            AppointmentFolderPath = new[] { "Calendars", "" }, // empty segment
                        },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }
}
