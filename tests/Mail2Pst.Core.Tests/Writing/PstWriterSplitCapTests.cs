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

public class PstWriterSplitCapTests
{

    // A message with a large text body so the byte estimate (≈ body length) tracks the
    // on-disk content closely (the case the split-cap fix targets).
    private static PlannedMessage BigMessage(int i, int bodyBytes) => new()
    {
        TargetFolderPath = new[] { "Imported Mail" },
        Message = new MailMessage
        {
            Subject = $"Message {i}",
            TextBody = new string('x', bodyBytes),
            Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = $"#{i}" },
            Attachments = new List<MailAttachment>(),
        },
    };

    // A message dominated by one large ATTACHMENT (small body), so on-disk size ≈ attachment
    // bytes — the attachment-dominated split case the existing text-body tests don't cover.
    private static PlannedMessage BigAttachmentMessage(int i, int attachmentBytes) => new()
    {
        TargetFolderPath = new[] { "Imported Mail" },
        Message = new MailMessage
        {
            Subject = $"Message {i}",
            Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = $"#{i}" },
            Attachments = new List<MailAttachment>
            {
                new()
                {
                    FileName = $"blob{i}.bin",
                    MimeType = "application/octet-stream",
                    Content = AttachmentContent.FromBytes(new byte[attachmentBytes]),
                },
            },
        },
    };

    private static int CountMessagesInPart(string partPath)
    {
        var pst = new PSTFile(partPath, FileAccess.Read);
        try
        {
            PSTFolder root = pst.TopOfPersonalFolders;
            PSTFolder? folder = root.FindChildFolder("Imported Mail");
            return folder?.MessageCount ?? 0;
        }
        finally { pst.CloseFile(); }
    }

    [Fact]
    public void PredictiveSplit_HappensBeforeDefaultCheckpoint()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-splitcap-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            // Small cap + sizeable bodies, but only 40 messages — far FEWER than the
            // default 500-message checkpoint. Their aggregate content far exceeds the cap,
            // so a correct predictive split makes multiple parts BEFORE the 500 checkpoint
            // is ever reached. Old code (no predictive split) only checks size at the
            // 500-message checkpoint, so it produces a SINGLE part for 40 messages.
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 30_000 };
            var messages = Enumerable.Range(0, 40).Select(i => BigMessage(i, 5000)).ToList();

            var report = new ConversionReport();
            var writer = new PstWriter(); // DEFAULT checkIntervalMessages (500)
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.True(outputFiles.Count > 1,
                $"predictive split should produce multiple parts before the 500-message checkpoint; got {outputFiles.Count}");
            Assert.Equal(40, outputFiles.Sum(CountMessagesInPart)); // self-contained: no messages lost while splitting early
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void CompletedParts_StayWithinCapPlusBoundedMargin()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-splitcap-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            const int bodyBytes = 5000;
            long cap = templateSize + 30_000;
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = cap };
            var messages = Enumerable.Range(0, 40).Select(i => BigMessage(i, bodyBytes)).ToList();

            var writer = new PstWriter();
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, new ConversionReport());

            // BEST-EFFORT, NOT a hard cap: the byte estimate is a lower bound, so allow a
            // bounded margin of ~one message + per-part overhead. This proves the overshoot
            // is tiny (< one message) vs the old up-to-500-message overshoot — it does NOT
            // prove a general hard <= cap guarantee. The margin is empirically tunable: if
            // this fails by a small stable amount, raise margin to the measured PST overhead;
            // if it fails by many messages' worth, predictive splitting is broken.
            long margin = bodyBytes + 64 * 1024;
            foreach (string part in outputFiles)
            {
                long len = new FileInfo(part).Length;
                Assert.True(len <= cap + margin,
                    $"part {Path.GetFileName(part)} = {len} bytes exceeds cap+margin {cap + margin}");
            }
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AllMessagesPreserved_NoDropsOrDuplicates()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-splitcap-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 30_000 };
            var messages = Enumerable.Range(0, 40).Select(i => BigMessage(i, 5000)).ToList();

            var report = new ConversionReport();
            var writer = new PstWriter();
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            int total = outputFiles.Sum(CountMessagesInPart);
            Assert.Equal(40, total);
            Assert.Equal(40, report.ConvertedCount);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void NoEmptyPartsProduced()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-splitcap-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 30_000 };
            var messages = Enumerable.Range(0, 40).Select(i => BigMessage(i, 5000)).ToList();

            var writer = new PstWriter();
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, new ConversionReport());

            Assert.True(outputFiles.Count > 1);
            foreach (string part in outputFiles)
                Assert.True(CountMessagesInPart(part) > 0, $"part {Path.GetFileName(part)} is empty");
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void SingleMessageLargerThanCap_StillConvertsIntoItsOwnPart()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-splitcap-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            // Cap leaves only ~10 KB of content room, but message #1's body is 50 KB —
            // larger than the cap can hold. It must still convert (its own oversized part),
            // not be dropped or loop.
            long cap = templateSize + 10_000;
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = cap };
            var messages = new List<PlannedMessage>
            {
                BigMessage(0, 2000),
                BigMessage(1, 50_000),
                BigMessage(2, 2000),
            };

            var report = new ConversionReport();
            var writer = new PstWriter();
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.Equal(3, report.ConvertedCount);              // all converted, none dropped
            var counts = outputFiles.Select(CountMessagesInPart).ToList();
            Assert.Equal(3, counts.Sum());                       // all present across parts
            Assert.True(outputFiles.Count >= 2);                 // the big message forced splits
            Assert.Contains(1, counts);                          // the oversized message lands in its own part
            Assert.All(counts, c => Assert.True(c > 0, "no empty parts"));
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void LargeAttachmentMessages_SplitAndStayWithinCapPlusMargin()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-splitcap-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            const int attachmentBytes = 256 * 1024;          // 256 KB per message, in the attachment
            long cap = templateSize + 700_000;               // ~2 attachments' worth of content room
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = cap };
            var messages = Enumerable.Range(0, 8).Select(i => BigAttachmentMessage(i, attachmentBytes)).ToList();

            var report = new ConversionReport();
            var writer = new PstWriter(); // DEFAULT checkIntervalMessages (500) — far > 8
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            // Predictive split (driven by the per-message estimate, which counts attachment bytes)
            // must produce multiple parts well before the 500-message checkpoint.
            Assert.True(outputFiles.Count > 1, $"expected a split from large attachments; got {outputFiles.Count}");

            // All messages preserved, none dropped or duplicated.
            Assert.Equal(8, outputFiles.Sum(CountMessagesInPart));
            Assert.Equal(8, report.ConvertedCount);

            // No empty parts.
            foreach (string part in outputFiles)
                Assert.True(CountMessagesInPart(part) > 0, $"part {Path.GetFileName(part)} is empty");

            // BEST-EFFORT, NOT a hard cap (see spec Non-goals): the estimate is a lower bound, so allow
            // a bounded margin of ~one message's attachment + per-part PST/block overhead. This proves
            // the attachment-dominated overshoot is tiny vs. the old up-to-500-message overshoot — it
            // does NOT prove a general hard <= cap guarantee.
            long margin = attachmentBytes + 256 * 1024;
            foreach (string part in outputFiles)
            {
                long len = new FileInfo(part).Length;
                Assert.True(len <= cap + margin,
                    $"part {Path.GetFileName(part)} = {len} bytes exceeds cap+margin {cap + margin}");
            }
        }
        finally { Directory.Delete(outputDir, true); }
    }
}
