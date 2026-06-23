// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Progress;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterCancellationTests
{

    private static PlannedMessage SmallMessage(int i, AttachmentContent? attachment = null) => new()
    {
        TargetFolderPath = new[] { "Imported Mail" },
        Message = new MailMessage
        {
            Subject = $"Message {i}",
            TextBody = "body",
            Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = $"#{i}" },
            Attachments = attachment is null
                ? new List<MailAttachment>()
                : new List<MailAttachment> { new() { FileName = $"a{i}.bin", MimeType = "application/octet-stream", Content = attachment } },
        },
    };

    [Fact]
    public void WritePlan_PreCancelledToken_CreatesNoFileAndRecordsNoDeleted()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Personal", MaxSizeBytes = 100L * 1024 * 1024 };
            var report = new ConversionReport();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                new PstWriter().WritePlan(
                    plan, new List<PlannedMessage> { SmallMessage(0) }, outputDir, report,
                    cancellationToken: cts.Token));

            Assert.Empty(Directory.GetFiles(outputDir, "*.pst"));
            Assert.Empty(report.DeletedFiles);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_CancelledMidWrite_DeletesCurrentPartAndThrows()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Personal", MaxSizeBytes = 100L * 1024 * 1024 };
            var messages = Enumerable.Range(0, 20).Select(i => SmallMessage(i)).ToList();
            var report = new ConversionReport();
            using var cts = new CancellationTokenSource();

            // Cancel at the first checkpoint (after 2 messages). No split happens.
            var writer = new PstWriter(checkIntervalMessages: 2);
            Assert.Throws<OperationCanceledException>(() =>
                writer.WritePlan(plan, messages, outputDir, report, onProgress: _ => cts.Cancel(),
                    cancellationToken: cts.Token));

            Assert.Empty(Directory.GetFiles(outputDir, "*.pst")); // single in-progress part deleted
            Assert.Equal(new[] { Path.Combine(outputDir, "Personal.pst") }, report.DeletedFiles.ToArray());
            Assert.Empty(report.OutputFiles); // no completed parts
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_CancelledDuringSplit_KeepsCompletedPartDeletesCurrent()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 20_000 };
            string bigBody = new string('x', 5000);
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 20; i++)
            {
                var m = SmallMessage(i);
                m.Message.TextBody = bigBody;
                messages.Add(m);
            }

            var report = new ConversionReport();
            string part2 = Path.Combine(outputDir, "Archive-2.pst");
            using var cts = new CancellationTokenSource();

            // Cancel only once the writer has split and started part 2. progressIntervalMessages: 1
            // makes a progress event fire after EVERY write so the cancel triggers the moment part 2
            // appears — robust to split cadence (the per-message predictive split keeps resetting the
            // checkpoint counter, so the every-N-checkpoint progress can't be relied on here).
            var writer = new PstWriter(checkIntervalMessages: 2, progressIntervalMessages: 1);
            Assert.Throws<OperationCanceledException>(() =>
                writer.WritePlan(plan, messages, outputDir, report,
                    onProgress: _ => { if (File.Exists(part2)) cts.Cancel(); },
                    cancellationToken: cts.Token));

            string part1 = Path.Combine(outputDir, "Archive-1.pst");
            Assert.True(File.Exists(part1), "completed part 1 must remain on disk");
            Assert.False(File.Exists(part2), "in-progress part 2 must be deleted");
            Assert.Equal(new[] { part2 }, report.DeletedFiles.ToArray());
            Assert.Contains(part1, report.OutputFiles);
            Assert.DoesNotContain(part2, report.OutputFiles);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_CancelWithQueuedTempFileAttachments_DisposesQueuedAttachmentContent()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        var tempFiles = new List<string>();
        try
        {
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 12; i++)
            {
                string tmp = Path.GetTempFileName();
                File.WriteAllBytes(tmp, new byte[] { 1, 2, 3 });
                tempFiles.Add(tmp);
                messages.Add(SmallMessage(i, AttachmentContent.FromTempFile(tmp, 3)));
            }

            var plan = new PstOutputPlan { Name = "Personal", MaxSizeBytes = 100L * 1024 * 1024 };
            var report = new ConversionReport();
            using var cts = new CancellationTokenSource();

            var writer = new PstWriter(checkIntervalMessages: 2);
            Assert.Throws<OperationCanceledException>(() =>
                writer.WritePlan(plan, messages, outputDir, report, onProgress: _ => cts.Cancel(),
                    cancellationToken: cts.Token));

            // Every attachment's temp file must be disposed (deleted), whether the
            // message was written, in-flight at cancel, or still queued.
            foreach (string tmp in tempFiles)
                Assert.False(File.Exists(tmp), $"temp file leaked: {tmp}");
        }
        finally
        {
            foreach (string tmp in tempFiles) if (File.Exists(tmp)) File.Delete(tmp);
            Directory.Delete(outputDir, true);
        }
    }
}
