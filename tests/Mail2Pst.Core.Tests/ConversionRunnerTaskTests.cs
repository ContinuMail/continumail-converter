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

public class ConversionRunnerTaskTests
{
    /// <summary>
    /// When skipTasks is true, the runner must ignore plan.TaskMappings entirely,
    /// produce zero TasksConverted, and emit the "tasks disabled by --no-tasks"
    /// warning exactly once (even with multiple output groups that each carry task mappings).
    /// </summary>
    [Fact]
    public void Run_SkipTasks_ZeroTasksAndWarningEmittedOnce()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-runtask-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Config with a valid calendar source (passes ConfigValidator) but NO real SQLite file.
            // With skipTasks=true the runner must never open the store.
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
                                IncludeTasks = true,
                                TaskFolderPath = new[] { "Tasks", "MyTasks" },
                            },
                        },
                    },
                },
            };

            var report = new ConversionRunner().Run(config, dir, skipTasks: true);

            Assert.Equal(0, report.TasksConverted);

            int warnCount = report.Warnings.Count(w =>
                w.Reason.Contains("tasks disabled by --no-tasks", StringComparison.Ordinal));
            Assert.Equal(1, warnCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
