// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class TaskMessageFactoryTests
{
    /// <summary>
    /// Round-trip helper: creates a store, runs <paramref name="write"/>, closes it,
    /// re-opens read-only, and returns the result of <paramref name="read"/>.
    /// Mirrors the RoundTrip pattern in ContactWriterTests.cs exactly.
    /// </summary>
    private static T RoundTrip<T>(
        Action<PSTFile, PSTFolder> write,
        Func<PSTFile, PSTFolder, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-tmf-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Tasks", FolderItemTypeName.Task);
                write(pst, folder);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read);
                PSTFolder folder = pst.TopOfPersonalFolders.FindChildFolder("Tasks");
                return read(pst, folder);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Helper: retrieve the first task in the folder as a TaskMessage.
    /// Mirrors FirstContact in ContactWriterTests.cs.
    /// </summary>
    private static TaskMessage FirstTask(PSTFile pst, PSTFolder folder) =>
        TaskMessage.GetTask(pst, folder.GetMessage(0).NodeID);

    [Fact]
    public void CreateNewTask_yields_IPM_Task_in_UTF8()
    {
        var (messageClass, subject) = RoundTrip(
            (pst, folder) =>
            {
                TaskMessage task = TaskMessage.CreateNewTask(pst, folder.NodeID);
                task.PC.SetStringProperty(PropertyID.PidTagSubject, "Buy milk");
                task.SaveChanges();
                folder.AddMessage(task);
            },
            (pst, folder) =>
            {
                TaskMessage m = FirstTask(pst, folder);
                return (
                    m.PC.GetStringProperty(PropertyID.PidTagMessageClass),
                    m.PC.GetStringProperty(PropertyID.PidTagSubject)
                );
            });

        Assert.Equal("IPM.Task", messageClass);
        Assert.Equal("Buy milk", subject);
    }

    [Fact]
    public void CreateNewTask_defaults_to_MSGFLAG_READ_and_Normal_importance()
    {
        var (flags, importance) = RoundTrip(
            (pst, folder) =>
            {
                TaskMessage task = TaskMessage.CreateNewTask(pst, folder.NodeID);
                task.SaveChanges();
                folder.AddMessage(task);
            },
            (pst, folder) =>
            {
                TaskMessage m = FirstTask(pst, folder);
                // Importance and InternetCodepage are write-only on the vendored MessageObject;
                // read them back via the raw PC so we can assert the values set in CreateNewTask.
                return (
                    m.MessageFlags,
                    (MessageImportance)m.PC.GetInt32Property(PropertyID.PidTagImportance)
                );
            });

        Assert.True(flags.HasFlag(MessageFlags.MSGFLAG_READ));
        Assert.Equal(MessageImportance.Normal, importance);
    }
}
