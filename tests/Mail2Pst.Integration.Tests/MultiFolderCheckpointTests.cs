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
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class MultiFolderCheckpointTests
{
    private static string NewOutDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-mfc-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    // Interleave messages across the given folders so every folder is dirty at the same time.
    private static List<PlannedMessage> Interleave(int perFolder, params string[] folders)
    {
        var planned = new List<PlannedMessage>();
        for (int i = 0; i < perFolder; i++)
            foreach (string f in folders)
                planned.Add(new PlannedMessage
                {
                    Message = new MailMessage { MessageId = $"<{f}-{i}@h>", Subject = $"{f}-{i}" },
                    TargetFolderPath = new[] { f },
                });
        return planned;
    }

    private static IReadOnlyList<ReadFolder> WriteAndRead(
        IEnumerable<PlannedMessage> planned, string outDir, int checkIntervalMessages)
    {
        var plan = new PstOutputPlan { Name = "P", MaxSizeBytes = 1000L * 1024 * 1024, IncludeEmptyFolders = true };
        var report = new ConversionReport();
        var outputs = new PstWriter(RoundTripHarness.TemplatePath, checkIntervalMessages)
            .WritePlan(plan, planned, outDir, report);
        return PstReader.Read(outputs);
    }

    [Fact]
    public void MultipleFolders_SinglePart_BelowCheckpoint_AllSavedAtFinish()
    {
        // 3 folders x 3 = 9 messages, default checkInterval 500 -> no checkpoint; only Finish() flushes.
        var planned = Interleave(perFolder: 3, "A", "B", "C");
        string outDir = NewOutDir();
        try
        {
            IReadOnlyList<ReadFolder> folders = WriteAndRead(planned, outDir, checkIntervalMessages: 500);
            foreach (string name in new[] { "A", "B", "C" })
            {
                ReadFolder f = folders.Single(x => x.DisplayPath == name);
                Assert.Equal(
                    new[] { $"<{name}-0@h>", $"<{name}-1@h>", $"<{name}-2@h>" },
                    f.Messages.Select(m => m.MessageId).OrderBy(s => s, StringComparer.Ordinal));
            }
        }
        finally { Directory.Delete(outDir, true); }
    }

    [Fact]
    public void MultipleFolders_AcrossCheckpoint_AllSurvive()
    {
        // 2 folders x 6 = 12 messages, checkInterval 5 -> checkpoint Flush fires mid-run with BOTH dirty.
        var planned = Interleave(perFolder: 6, "A", "B");
        string outDir = NewOutDir();
        try
        {
            IReadOnlyList<ReadFolder> folders = WriteAndRead(planned, outDir, checkIntervalMessages: 5);
            foreach (string name in new[] { "A", "B" })
            {
                ReadFolder f = folders.Single(x => x.DisplayPath == name);
                Assert.Equal(6, f.Messages.Count);
                Assert.Equal(
                    Enumerable.Range(0, 6).Select(i => $"<{name}-{i}@h>"),
                    f.Messages.Select(m => m.MessageId).OrderBy(s => s, StringComparer.Ordinal));
            }
        }
        finally { Directory.Delete(outDir, true); }
    }
}
