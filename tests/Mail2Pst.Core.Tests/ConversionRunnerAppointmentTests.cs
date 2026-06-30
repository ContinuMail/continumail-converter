// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests;

public class ConversionRunnerAppointmentTests
{
    /// <summary>
    /// When skipAppointments is true, the runner must ignore plan.AppointmentMappings entirely,
    /// produce zero AppointmentsConverted, and emit "appointments disabled by --no-appointments"
    /// exactly once even when multiple output groups carry appointment mappings.
    /// </summary>
    [Fact]
    public void Run_SkipAppointments_ZeroAppointmentsAndWarningEmittedOnce()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runappt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Config with IncludeAppointments=true and no real SQLite file.
            // With skipAppointments=true the runner must never open the store.
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
                                StorePath = Path.Combine(dir, "local.sqlite"), // does not exist
                                CalId = "cal-guid-1",
                                IncludeAppointments = true,
                                IncludeTasks = false,
                                AppointmentFolderPath = new[] { "Calendars", "MyCalendar" },
                            },
                        },
                    },
                },
            };

            var report = new ConversionRunner().Run(config, dir, skipAppointments: true);

            Assert.Equal(0, report.AppointmentsConverted);

            int warnCount = report.Warnings.Count(w =>
                w.Reason.Contains("appointments disabled by --no-appointments", StringComparison.Ordinal));
            Assert.Equal(1, warnCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// When the appointment store path does not exist, the runner records a warning
    /// and does not throw.
    /// </summary>
    [Fact]
    public void Run_NonExistentStorePath_RecordsWarningAndDoesNotThrow()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runappt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
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
                                StorePath = Path.Combine(dir, "missing.sqlite"), // does not exist
                                CalId = "cal-guid-1",
                                IncludeAppointments = true,
                                IncludeTasks = false,
                                AppointmentFolderPath = new[] { "Calendars", "MyCalendar" },
                            },
                        },
                    },
                },
            };

            var report = new ConversionRunner().Run(config, dir);

            Assert.Equal(0, report.AppointmentsConverted);
            Assert.True(report.AppointmentWarningCount > 0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
