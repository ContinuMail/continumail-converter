// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstPartManagerFolderTypeTests
{
    private static PstPartManager NewManager(string dir) => new PstPartManager(
        "Test", dir, long.MaxValue, 500,
        writeMessage: (_, _, _) => { },
        writeContact: (_, _, _) => { });

    private static FolderToPrecreate F(FolderItemTypeName type, params string[] path) =>
        new FolderToPrecreate(path, type);

    [Fact]
    public void Calendar_and_task_folders_with_same_leaf_name_coexist_under_different_parents()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mgr = NewManager(dir);
            // ["Calendars","Home"] (IPF.Appointment) and ["Tasks","Home"] (IPF.Task) are DISTINCT paths.
            mgr.Begin(new[]
            {
                F(FolderItemTypeName.Appointment, "Calendars", "Home"),
                F(FolderItemTypeName.Task,        "Tasks",      "Home"),
            });
            mgr.Finish();
            mgr.Close();

            PSTFile pst = new PSTFile(Path.Combine(dir, "Test.pst"), FileAccess.Read);
            try
            {
                PSTFolder cals = pst.TopOfPersonalFolders.FindChildFolder("Calendars");
                PSTFolder tasks = pst.TopOfPersonalFolders.FindChildFolder("Tasks");
                Assert.Equal("IPF.Appointment", cals.FindChildFolder("Home").ContainerClass);
                Assert.Equal("IPF.Task",        tasks.FindChildFolder("Home").ContainerClass);
            }
            finally { pst.CloseFile(); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Same_path_requested_as_two_different_item_types_throws_before_writing()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mgr = NewManager(dir);
            var ex = Assert.Throws<ConfigValidationException>(() => mgr.Begin(new[]
            {
                F(FolderItemTypeName.Appointment, "Shared"),
                F(FolderItemTypeName.Task,        "Shared"),   // same path, different leaf class
            }));
            Assert.Contains("Shared", ex.Message);
            mgr.Close();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Same_path_same_type_is_reused_not_duplicated()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mgr = NewManager(dir);
            mgr.Begin(new[]
            {
                F(FolderItemTypeName.Appointment, "Calendars", "Home"),
                F(FolderItemTypeName.Appointment, "Calendars", "Home"),   // duplicate → reused
            });
            mgr.Finish();
            mgr.Close();

            PSTFile pst = new PSTFile(Path.Combine(dir, "Test.pst"), FileAccess.Read);
            try
            {
                PSTFolder cals = pst.TopOfPersonalFolders.FindChildFolder("Calendars");
                int homeCount = 0;
                foreach (PSTFolder child in cals.GetChildFolders())
                    if (child.DisplayName == "Home") homeCount++;
                Assert.Equal(1, homeCount);
            }
            finally { pst.CloseFile(); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
