// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterDisposalTests
{

    [Fact]
    public void WritePlan_DisposesAttachmentContentAfterWrite()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-disposal-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        string tempAttachPath = Path.GetTempFileName();
        File.WriteAllBytes(tempAttachPath, [1, 2, 3, 4, 5]);

        try
        {
            var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
            var attachment = new MailAttachment
            {
                FileName = "big.bin",
                MimeType = "application/octet-stream",
                Content = AttachmentContent.FromTempFile(tempAttachPath, 5),
            };
            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "With large attachment",
                        Source = new SourceReference { SourcePath = "test.mbox", Identifier = "message #1" },
                        Attachments = [attachment],
                    },
                },
            };

            var report = new ConversionReport();
            var writer = new PstWriter();
            writer.WritePlan(plan, messages, outputDir, report);

            Assert.False(File.Exists(tempAttachPath),
                "Temp attachment file should be deleted after WritePlan");
            Assert.Equal(1, report.ConvertedCount);
        }
        finally
        {
            Directory.Delete(outputDir, true);
            if (File.Exists(tempAttachPath)) File.Delete(tempAttachPath);
        }
    }

    [Fact]
    public void WritePlan_DisposesAttachmentContentWhenMessageWriteFails()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-disposal-fail-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        string tempAttachPath = Path.GetTempFileName();
        File.WriteAllBytes(tempAttachPath, [1, 2, 3]);

        // Pre-dispose the second attachment so ReadAllBytes() throws ObjectDisposedException
        // during WriteMessage. Under the fatal-by-default write taxonomy (empty recoverable
        // allowlist) this is NOT a recoverable per-message skip — it propagates as fatal.
        var badContent = AttachmentContent.FromBytes([4, 5, 6]);
        badContent.Dispose();

        try
        {
            var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
            var message = new MailMessage
            {
                Subject = "Failing message",
                Source = new SourceReference { SourcePath = "test.mbox", Identifier = "message #1" },
                Attachments =
                [
                    new MailAttachment
                    {
                        FileName = "good.bin",
                        MimeType = "application/octet-stream",
                        Content = AttachmentContent.FromTempFile(tempAttachPath, 3),
                    },
                    new MailAttachment
                    {
                        FileName = "bad.bin",
                        MimeType = "application/octet-stream",
                        Content = badContent, // ReadAllBytes() throws -> WriteMessage throws -> fatal
                    },
                ],
            };

            var messages = new List<PlannedMessage>
            {
                new() { TargetFolderPath = new[] { "Inbox" }, Message = message },
            };

            var report = new ConversionReport();
            var writer = new PstWriter();

            // The write failure now propagates as fatal (no silent skip)...
            Assert.ThrowsAny<Exception>(() => writer.WritePlan(plan, messages, outputDir, report));

            // ...but the per-message finally must still dispose the attachment temp file (no leak).
            Assert.False(File.Exists(tempAttachPath),
                "Temp attachment file should be deleted even when the message write fails");
        }
        finally
        {
            Directory.Delete(outputDir, true);
            if (File.Exists(tempAttachPath)) File.Delete(tempAttachPath);
        }
    }

    [Fact]
    public void WritePlan_FatalErrorMidRun_DisposesQueuedAttachmentTempFiles()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-disposal-fatal-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        var tempPaths = new List<string>();
        var messages = new List<PlannedMessage>();
        for (int i = 0; i < 5; i++)
        {
            string tp = Path.GetTempFileName();
            File.WriteAllBytes(tp, [1, 2, 3]);
            tempPaths.Add(tp);
            messages.Add(new PlannedMessage
            {
                TargetFolderPath = new[] { "Inbox" },
                Message = new MailMessage
                {
                    Subject = $"m{i}",
                    Source = new SourceReference { SourcePath = "test.mbox", Identifier = $"#{i}" },
                    Attachments =
                    [
                        new MailAttachment
                        {
                            FileName = $"f{i}.bin",
                            MimeType = "application/octet-stream",
                            Content = AttachmentContent.FromTempFile(tp, 3),
                        },
                    ],
                },
            });
        }

        try
        {
            var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
            // checkInterval=1 so the first message hits a checkpoint and fires the
            // progress callback, which throws (a fatal error) while the remaining
            // messages are still queued. The sleep lets the producer enqueue them all.
            var writer = new PstWriter(checkIntervalMessages: 1);

            Assert.ThrowsAny<Exception>(() => writer.WritePlan(
                plan, messages, outputDir, new ConversionReport(), totalMessages: 5,
                onProgress: _ => { Thread.Sleep(50); throw new InvalidOperationException("boom"); }));

            // Despite the fatal error, no attachment temp file is left behind.
            foreach (string tp in tempPaths)
                Assert.False(File.Exists(tp), $"temp file leaked after fatal error: {tp}");
        }
        finally
        {
            Directory.Delete(outputDir, true);
            foreach (string tp in tempPaths)
                if (File.Exists(tp)) File.Delete(tp);
        }
    }
}
