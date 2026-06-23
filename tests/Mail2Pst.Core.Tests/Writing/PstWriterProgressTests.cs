// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Progress;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterProgressTests
{

    [Fact]
    public void WritePlan_ProgressEvent_ReportsCurrentSourceAndFolder()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "m1",
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "#1" },
                    },
                },
            };

            var events = new List<ProgressEvent>();
            new PstWriter(checkIntervalMessages: 1).WritePlan(
                plan, messages, outputDir, new ConversionReport(), totalMessages: 1,
                onProgress: e => { if (e is ProgressEvent p) events.Add(p); });

            Assert.Contains(events, p => p.CurrentFolder == "Inbox" && p.CurrentSource == "Inbox.mbox");
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_EmitsProgressMoreOftenThanFlushInterval()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 10; i++)
            {
                messages.Add(new PlannedMessage
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = $"m{i}",
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = $"#{i}" },
                    },
                });
            }

            var events = new List<ProgressEvent>();
            new PstWriter(checkIntervalMessages: 100, progressIntervalMessages: 2).WritePlan(
                plan, messages, outputDir, new ConversionReport(), totalMessages: 10,
                onProgress: e => { if (e is ProgressEvent p) events.Add(p); });

            Assert.True(events.Count >= 4, $"Expected several progress events, got {events.Count}");

            var tuples = events
                .Select(p => (p.Converted, p.Skipped, p.Warnings, p.EstimatedOutputBytes))
                .ToList();
            Assert.Equal(tuples.Count, tuples.Distinct().Count());
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_ProgressBytes_AreMonotonicAndPositive()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 6; i++)
            {
                messages.Add(new PlannedMessage
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = $"m{i}",
                        TextBody = new string('x', 1000),
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = $"#{i}" },
                    },
                });
            }

            var events = new List<ProgressEvent>();
            new PstWriter(checkIntervalMessages: 2, progressIntervalMessages: 1).WritePlan(
                plan, messages, outputDir, new ConversionReport(), totalMessages: 6,
                onProgress: e => { if (e is ProgressEvent p) events.Add(p); });

            long prev = -1;
            foreach (ProgressEvent p in events)
            {
                Assert.True(p.EstimatedOutputBytes >= prev, "EstimatedOutputBytes must not decrease");
                prev = p.EstimatedOutputBytes;
            }
            Assert.True(events[^1].EstimatedOutputBytes > 0, "Final EstimatedOutputBytes should be > 0");
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_ProgressBytes_KeepIncreasingAcrossSplit()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 20_000 };
            string bigBody = new string('x', 5000);
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 10; i++)
            {
                messages.Add(new PlannedMessage
                {
                    TargetFolderPath = new[] { "Imported Mail" },
                    Message = new MailMessage
                    {
                        Subject = $"Message {i}",
                        TextBody = bigBody,
                        Date = DateTimeOffset.UtcNow,
                        Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = $"message #{i}" },
                    },
                });
            }

            var report = new ConversionReport();
            var events = new List<ProgressEvent>();
            List<string> outputFiles = new PstWriter(checkIntervalMessages: 2, progressIntervalMessages: 1)
                .WritePlan(plan, messages, outputDir, report, totalMessages: 10,
                    onProgress: e => { if (e is ProgressEvent p) events.Add(p); });

            Assert.True(outputFiles.Count >= 2, "Expected a split");
            long prev = -1;
            foreach (ProgressEvent p in events)
            {
                Assert.True(p.EstimatedOutputBytes >= prev, "EstimatedOutputBytes reset across a split");
                prev = p.EstimatedOutputBytes;
            }
        }
        finally { Directory.Delete(outputDir, true); }
    }
}
