// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterAttachmentTests
{

    // Writes a message carrying EXACTLY ONE attachment into a temp PST, reopens it
    // read-only, asserts the note has exactly one attachment, and returns that
    // attachment's PidTagAttachData bytes. Cleans up the PST handle + temp dir.
    private static byte[] WriteReopenAndReadSingleAttachment(MailAttachment attachment)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "mail2pst-attach-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
            var message = new MailMessage
            {
                Subject = "With attachment",
                Source = new SourceReference { SourcePath = "test.mbox", Identifier = "msg#1" },
                Attachments = new List<MailAttachment> { attachment },
            };
            var planned = new List<PlannedMessage> { new() { TargetFolderPath = new[] { "Inbox" }, Message = message } };

            var writer = new PstWriter();
            List<string> outputFiles = writer.WritePlan(plan, planned, tempDir, new ConversionReport());
            Assert.Single(outputFiles);                                   // no unexpected split
            Assert.True(File.Exists(outputFiles[0]), "WritePlan must produce the PST");

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            try
            {
                PSTFolder root = pst.TopOfPersonalFolders;
                var inbox = (MailFolder)root.FindChildFolder("Inbox")!;
                Assert.NotNull(inbox);
                Note note = inbox.GetNote(0);
                // Inline/hidden attachments are written via the same attachment subnode
                // mechanism, so they MUST still count here. If this is 0 for the inline
                // case, STOP and investigate (writer bug, reader-API nuance, or wrong read
                // path) — do not weaken the assertion.
                Assert.Equal(1, note.AttachmentCount);
                AttachmentObject att = note.GetAttachmentObject(0);
                byte[]? data = att.PC.GetBytesProperty(PropertyID.PidTagAttachData);
                Assert.NotNull(data);                                     // same clear error for all 3 cases
                return data!;
            }
            finally { pst.CloseFile(); }
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void RegularAttachment_BytesRoundTripExactly()
    {
        byte[] input = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(); // 0x00..0xFF ramp
        var attachment = new MailAttachment
        {
            FileName = "ramp.bin",
            MimeType = "application/octet-stream",
            Content = AttachmentContent.FromBytes(input),
        };

        byte[] readBack = WriteReopenAndReadSingleAttachment(attachment);

        Assert.Equal(input.Length, readBack.Length);
        Assert.True(readBack.SequenceEqual(input), "attachment bytes must round-trip exactly");
    }

    [Fact]
    public void TempFileBackedAttachment_BytesRoundTripExactly()
    {
        // A small distinctive file is enough: constructing FromTempFile directly
        // exercises the ReadAllBytes-from-disk path without needing >4 MB.
        byte[] input = { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x10, 0x7F, 0x80, 0xFF };
        string tempAttach = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempAttach, input);
            Assert.True(File.Exists(tempAttach), "source temp attachment must exist before write");

            var attachment = new MailAttachment
            {
                FileName = "blob.bin",
                MimeType = "application/octet-stream",
                Content = AttachmentContent.FromTempFile(tempAttach, input.Length),
            };

            byte[] readBack = WriteReopenAndReadSingleAttachment(attachment);

            Assert.Equal(input.Length, readBack.Length);
            Assert.True(readBack.SequenceEqual(input), "temp-file attachment bytes must round-trip exactly");
        }
        finally { if (File.Exists(tempAttach)) File.Delete(tempAttach); }
    }

    [Fact]
    public void InlineCidAttachment_BytesRoundTripExactly()
    {
        byte[] input = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG signature bytes
        var attachment = new MailAttachment
        {
            FileName = "logo.png",
            MimeType = "image/png",
            ContentId = "logo@cid",
            IsInline = true,
            Content = AttachmentContent.FromBytes(input),
        };

        byte[] readBack = WriteReopenAndReadSingleAttachment(attachment);

        Assert.Equal(input.Length, readBack.Length);
        Assert.True(readBack.SequenceEqual(input), "inline CID attachment bytes must round-trip exactly");
    }
}
