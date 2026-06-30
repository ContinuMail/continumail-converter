// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterTaskPhaseTests
{
    [Fact]
    public void WritePlan_WritesTasks_IntoIPFTaskFolder_AndCountsConverted()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var plan = new PstOutputPlan { Name = "Out", MaxSizeBytes = long.MaxValue };
            var taskFolders = new List<IReadOnlyList<string>> { new[] { "Tasks", "Home" } };
            var tasks = new List<PlannedTask>
            {
                new()
                {
                    Task = new TaskRecord { Subject = "Buy milk", SourceId = "task-1" },
                    TargetFolderPath = new[] { "Tasks", "Home" },
                },
            };
            var report = new ConversionReport();

            new PstWriter().WritePlan(plan, new List<PlannedMessage>(),
                new List<PlannedContact>(), new List<IReadOnlyList<string>>(),
                tasks, taskFolders,
                dir, report);

            PSTFile? f = null;
            try
            {
                f = new PSTFile(Path.Combine(dir, "Out.pst"), FileAccess.Read);
                PSTFolder tasksRoot = f.TopOfPersonalFolders.FindChildFolder("Tasks");
                Assert.NotNull(tasksRoot);
                PSTFolder homeFolder = tasksRoot.FindChildFolder("Home");
                Assert.NotNull(homeFolder);
                Assert.Equal("IPF.Task", homeFolder.ContainerClass);
                Assert.Equal(1, homeFolder.MessageCount);
                Assert.Equal(1, report.TasksConverted);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WritePlan_EmptyTaskFolders_StillCreatesIPFTaskFolder()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var plan = new PstOutputPlan { Name = "Out", MaxSizeBytes = long.MaxValue };
            var taskFolders = new List<IReadOnlyList<string>> { new[] { "Tasks", "Work" } };
            var report = new ConversionReport();

            new PstWriter().WritePlan(plan, new List<PlannedMessage>(),
                new List<PlannedContact>(), new List<IReadOnlyList<string>>(),
                new List<PlannedTask>(), taskFolders,
                dir, report);

            PSTFile? f = null;
            try
            {
                f = new PSTFile(Path.Combine(dir, "Out.pst"), FileAccess.Read);
                PSTFolder tasksRoot = f.TopOfPersonalFolders.FindChildFolder("Tasks");
                Assert.NotNull(tasksRoot);
                PSTFolder workFolder = tasksRoot.FindChildFolder("Work");
                Assert.NotNull(workFolder);
                Assert.Equal("IPF.Task", workFolder.ContainerClass);
                Assert.Equal(0, report.TasksConverted);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }
}
