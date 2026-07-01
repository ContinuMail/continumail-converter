// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

/// <summary>
/// TDD round-trip tests for attachment emission from <see cref="TaskWriter"/>.
/// InlineBytes → visible ByValue PST attachment; LinkOnly → body appendix, no embedded row.
/// </summary>
public class TaskWriterAttachmentTests
{
    // -----------------------------------------------------------------------
    // Round-trip infrastructure (mirrors TaskWriterTests.RoundTripTask)
    // -----------------------------------------------------------------------

    private static T RoundTripTask<T>(TaskRecord record, Func<PSTFile, MessageObject, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-twatt-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Tasks", FolderItemTypeName.Task);
                new TaskWriter().WriteTask(pst, folder, record);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read);
                PSTFolder readFolder = pst.TopOfPersonalFolders.FindChildFolder("Tasks");
                MessageObject msg = TaskMessage.GetTask(pst, readFolder.GetMessage(0).NodeID);
                return read(pst, msg);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Inline_task_attachment_is_written_by_value_and_visible()
    {
        var rec = new TaskRecord
        {
            Subject = "t",
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.InlineBytes,
                    "note.txt", "text/plain", new byte[] { 4, 5, 6 }, null, null)
            }
        };

        var (count, bytes, hidden) = RoundTripTask(rec, (f, msg) => (
            msg.AttachmentCount,
            msg.GetAttachmentObject(0).PC.GetBytesProperty(PropertyID.PidTagAttachData),
            msg.GetAttachmentObject(0).PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden)));

        Assert.Equal(1, count);
        Assert.Equal(new byte[] { 4, 5, 6 }, bytes);
        Assert.NotEqual(true, hidden);   // task InlineBytes is a VISIBLE attachment, not a hidden CID
    }

    [Fact]
    public void Link_only_task_attachment_is_appended_to_body_not_embedded()
    {
        var rec = new TaskRecord
        {
            Subject = "t",
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.LinkOnly,
                    "report.pdf", "application/pdf", null, null, "https://tasks.example.com/report.pdf")
            }
        };

        var (count, body) = RoundTripTask(rec, (f, msg) => (
            msg.AttachmentCount,
            msg.PC.GetStringProperty(PropertyID.PidTagBody) ?? ""));

        Assert.Equal(0, count);                                              // no embedded row for a link
        Assert.Contains("https://tasks.example.com/report.pdf", body);      // preserved in body
    }
}
