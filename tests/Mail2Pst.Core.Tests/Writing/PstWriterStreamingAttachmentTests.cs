// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterStreamingAttachmentTests
{
    // Builds deterministic bytes of the given length via a repeating ramp pattern.
    private static byte[] Ramp(int length)
    {
        var buf = new byte[length];
        for (int i = 0; i < length; i++) buf[i] = (byte)(i & 0xFF);
        return buf;
    }

    // Writes a single-attachment message, reopens the PST, and returns the attachment bytes.
    // Does NOT dispose the PST temp dir — callers are responsible for cleanup.
    private static (byte[] attachData, string pstPath) WriteReopenAndReadBack(
        AttachmentContent attachment, long thresholdBytes, out string tempDir)
    {
        tempDir = Path.Combine(Path.GetTempPath(), "mail2pst-stream-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
        var message = new MailMessage
        {
            Subject = "Streaming test",
            Source = new SourceReference { SourcePath = "test.mbox", Identifier = "msg#1" },
            Attachments = new List<MailAttachment>
            {
                new() { FileName = "data.bin", MimeType = "application/octet-stream", Content = attachment },
            },
        };
        var planned = new List<PlannedMessage>
        {
            new() { TargetFolderPath = new[] { "Inbox" }, Message = message },
        };

        var writer = new PstWriter();
        writer.StreamingThresholdBytes = thresholdBytes;

        List<string> outputFiles = writer.WritePlan(plan, planned, tempDir, new ConversionReport());
        Assert.Single(outputFiles);
        Assert.True(File.Exists(outputFiles[0]));

        var pst = new PSTFile(outputFiles[0], FileAccess.Read);
        byte[] data;
        try
        {
            PSTFolder root = pst.TopOfPersonalFolders;
            var inbox = (MailFolder)root.FindChildFolder("Inbox")!;
            Assert.NotNull(inbox);
            Note note = inbox.GetNote(0);
            Assert.Equal(1, note.AttachmentCount);
            AttachmentObject att = note.GetAttachmentObject(0);
            byte[]? readBack = att.PC.GetBytesProperty(PropertyID.PidTagAttachData);
            Assert.NotNull(readBack);
            data = readBack!;
        }
        finally
        {
            pst.CloseFile();
        }

        return (data, outputFiles[0]);
    }

    // Test A: streaming path (threshold = 1 → even small attachments stream).
    // Uses a >8176-byte body so the write crosses the DataBlock → XBlock spine transition,
    // exercising the multi-block path of SetExternalProperty/AppendData.
    [Fact]
    public void StreamingPath_LargerThanSpine_BytesRoundTripExactly()
    {
        // 9 000 bytes > one DataBlock (8 176 bytes), so this exercises the XBlock path.
        byte[] input = Ramp(9_000);
        string tempAttach = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempAttach, input);

            var content = AttachmentContent.FromTempFile(tempAttach, input.Length);
            // threshold = 1 → streaming path for ALL attachments
            (byte[] readBack, _) = WriteReopenAndReadBack(content, thresholdBytes: 1, out string tempDir);
            try
            {
                Assert.Equal(input.Length, readBack.Length);
                Assert.True(readBack.SequenceEqual(input),
                    "PidTagAttachData must round-trip exactly via the streaming write path");
            }
            finally { Directory.Delete(tempDir, true); }
        }
        finally { if (File.Exists(tempAttach)) File.Delete(tempAttach); }
    }

    // Test A2: byte[] path (default threshold → small attachment stays in-memory).
    // Guards that the non-streaming path is unaffected by the production change.
    [Fact]
    public void ByteArrayPath_SmallAttachment_BytesRoundTripExactly()
    {
        byte[] input = Ramp(256); // well below the 16 MB default threshold
        var content = AttachmentContent.FromBytes(input);
        // default threshold: 16 MB — small attachment takes the byte[] path
        (byte[] readBack, _) = WriteReopenAndReadBack(content, thresholdBytes: 16L * 1024 * 1024,
            out string tempDir);
        try
        {
            Assert.Equal(input.Length, readBack.Length);
            Assert.True(readBack.SequenceEqual(input),
                "PidTagAttachData must round-trip exactly via the byte[] write path");
        }
        finally { Directory.Delete(tempDir, true); }
    }

    // Test B: cancel mid-streamed-attachment → OCE thrown, in-progress PST deleted, temp file disposed.
    // The subclass cancels the CancellationTokenSource just before WriteMessage calls WriteAttachment,
    // so SetExternalProperty receives an already-cancelled token and throws OperationCanceledException.
    // Attachment must be > HeapOnNode.MaximumAllocationLength (3580 B) so StoreExternalProperty takes
    // the DataTree/AppendData path, which checks the cancellation token on each DataBlock iteration.
    [Fact]
    public void StreamingPath_CancelDuringWrite_ThrowsOce_DeletesPartAndDisposesTempFile()
    {
        // 4000 bytes > HeapOnNode.MaximumAllocationLength (3580) → DataTree.AppendData path checks token
        byte[] input = Ramp(4_000);
        string tempAttach = Path.GetTempFileName();
        File.WriteAllBytes(tempAttach, input);

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-stream-cancel-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Streaming", MaxSizeBytes = 100L * 1024 * 1024 };
            var message = new MailMessage
            {
                Subject = "Cancel test",
                Source = new SourceReference { SourcePath = "test.mbox", Identifier = "msg#1" },
                Attachments = new List<MailAttachment>
                {
                    new() { FileName = "data.bin", MimeType = "application/octet-stream",
                            Content = AttachmentContent.FromTempFile(tempAttach, input.Length) },
                },
            };
            var planned = new List<PlannedMessage>
            {
                new() { TargetFolderPath = new[] { "Inbox" }, Message = message },
            };

            using var cts = new CancellationTokenSource();
            var report = new ConversionReport();
            // CancelOnWriteWriter cancels the token inside WriteMessageCore, before the attachment
            // write, so SetExternalProperty(... _activeCancellationToken) sees a cancelled token.
            var writer = new CancelOnWriteWriter(cts);
            writer.StreamingThresholdBytes = 1; // force streaming path

            Assert.Throws<OperationCanceledException>(() =>
                writer.WritePlan(plan, planned, outputDir, report, cancellationToken: cts.Token));

            // In-progress PST part must be deleted
            Assert.Empty(Directory.GetFiles(outputDir, "*.pst"));
            Assert.Equal(new[] { Path.Combine(outputDir, "Streaming.pst") }, report.DeletedFiles.ToArray());

            // Temp attachment file must be disposed (deleted by AttachmentContent.Dispose)
            Assert.False(File.Exists(tempAttach), "temp attachment file must be deleted after cancel");
        }
        finally
        {
            if (File.Exists(tempAttach)) File.Delete(tempAttach);
            Directory.Delete(outputDir, true);
        }
    }

    /// <summary>
    /// Cancels the CancellationTokenSource at the start of the first WriteMessageCore call so
    /// that the streaming SetExternalProperty call receives an already-cancelled token and throws
    /// OperationCanceledException — simulating a mid-attachment-write cancellation.
    /// </summary>
    private sealed class CancelOnWriteWriter : PstWriter
    {
        private readonly CancellationTokenSource _cts;
        private int _callCount;

        public CancelOnWriteWriter(CancellationTokenSource cts) => _cts = cts;

        internal override void WriteMessageCore(PSTFile file, PSTFolder folder, MailMessage message)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
                _cts.Cancel(); // cancelled before WriteAttachment → SetExternalProperty sees it
            base.WriteMessageCore(file, folder, message);
        }
    }
}
