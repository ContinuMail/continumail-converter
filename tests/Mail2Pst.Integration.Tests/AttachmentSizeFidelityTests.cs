// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mail2Pst.Core.Models;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Guards the attachment-object invariants scanpst enforces (class 2 of the 2026-07-07 scanpst
/// root-cause): every written attachment must carry PidTagRenderingPosition (-1 = not rendered
/// inline in RTF) and a PidTagAttachSize equal to the attachment object's total property length
/// (all PC properties INCLUDING the payload — scanpst's definition, harvested from its own
/// repair output), not the raw payload length. Violations make scanpst report
/// "missing PR_RENDERING_POSITION" / "missing or invalid PR_ATTACH_SIZE" /
/// "Attachment table row doesn't match sub-object" (RepairRequired).
/// </summary>
public class AttachmentSizeFidelityTests
{
    private const int PidTagRenderingPosition = 0x370B;

    [Fact]
    public void WrittenAttachments_CarryRenderingPositionAndObjectSize()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "m2p-attsize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var msg = new MailMessage
            {
                Subject = "With attachments",
                From = new MailAddress { Name = "Sender", Email = "sender@example.com" },
                To = { new MailAddress { Name = "Rcpt", Email = "rcpt@example.com" } },
                Date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                TextBody = "See attached.",
                HtmlBody = "<p>Inline: <img src=\"cid:pic1\"></p>",
            };
            msg.Attachments.Add(new MailAttachment
            {
                FileName = "note.txt",
                MimeType = "text/plain",
                IsInline = false,
                Content = AttachmentContent.FromBytes(Encoding.UTF8.GetBytes("attachment payload")),
            });
            msg.Attachments.Add(new MailAttachment
            {
                FileName = "pic.png",
                MimeType = "image/png",
                IsInline = true,
                ContentId = "pic1",
                Content = AttachmentContent.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 }),
            });
            IReadOnlyList<string> psts = RoundTripHarness.ConvertMessages(new[] { msg }, outDir);

            int checked_ = 0;
            foreach (string pstPath in psts)
            {
                var pst = new PSTFile(pstPath, FileAccess.Read);
                try
                {
                    foreach (PSTFolder folder in pst.TopOfPersonalFolders.GetChildFolders())
                        checked_ += AssertFolderAttachments(pst, folder);
                }
                finally { pst.CloseFile(); }
            }
            Assert.Equal(2, checked_);
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static int AssertFolderAttachments(PSTFile pst, PSTFolder folder)
    {
        int checked_ = 0;
        var tc = folder.GetContentsTable();
        if (tc is not null)
        {
            for (int i = 0; i < tc.RowCount; i++)
            {
                var message = MessageObject.GetMessage(pst, new NodeID(tc.GetRowID(i)));
                for (int a = 0; a < message.AttachmentCount; a++)
                {
                    AttachmentObject att = message.GetAttachmentObject(a);
                    string name = att.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename) ?? $"[{a}]";

                    int? renderingPosition = att.PC.GetInt32Property((PropertyID)PidTagRenderingPosition);
                    Assert.True(renderingPosition == -1,
                        $"attachment '{name}': PidTagRenderingPosition should be -1 but was " +
                        $"{(renderingPosition is null ? "MISSING" : renderingPosition.ToString())} " +
                        "(scanpst: 'missing PR_RENDERING_POSITION')");

                    int? attachSize = att.PC.GetInt32Property(PropertyID.PidTagAttachSize);
                    int objectSize = att.PC.GetTotalLengthOfAllProperties();
                    Assert.True(attachSize == objectSize,
                        $"attachment '{name}': PidTagAttachSize={attachSize} but attachment-object " +
                        $"property length={objectSize} (scanpst: 'missing or invalid PR_ATTACH_SIZE')");
                    checked_++;
                }
            }
        }

        foreach (PSTFolder child in folder.GetChildFolders())
            checked_ += AssertFolderAttachments(pst, child);
        return checked_;
    }
}
