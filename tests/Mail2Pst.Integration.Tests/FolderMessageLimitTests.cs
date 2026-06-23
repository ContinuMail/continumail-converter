// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class FolderMessageLimitTests
{
    private const int GeneratedCount = 65_600;           // > 65,535
    private static readonly string[] ExpectedPath = { "LimitTest" };
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [SkippableFact]
    public void SingleFolderBeyond65535_WriterCompletes_IndependentReaderCountsMatch()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to run the limit test.");
        Skip.If(Environment.GetEnvironmentVariable("MAIL2PST_RUN_SLOW_PST_LIMIT_TEST") != "1",
            "Set MAIL2PST_RUN_SLOW_PST_LIMIT_TEST=1 to run the slow (>65k message) limit test.");

        // Minimal, attachment-free mbox so the test stresses per-folder COUNT, not size or I/O.
        string mbox = WriteMinimalMbox(GeneratedCount, out string outDir);
        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Archive",
                        MaxSizeMB = 50_000,                       // high cap: splits, if any, are count/structure driven
                        FolderMapping = FolderMappingMode.Flatten,
                        IncludeEmptyFolders = false,
                        Sources = new List<SourceConfig>
                        {
                            new() { Type = "mbox", Path = mbox, TargetFolder = ExpectedPath[0] },
                        },
                    },
                },
            };

            // Writer must COMPLETE (split or not), never throw.
            var (outputs, _) = RoundTripHarness.Convert(config, outDir);

            long aggregate = 0, totalAllParts = 0;
            string expectedKey = FolderPathKey.Join(ExpectedPath);
            foreach (string part in outputs)
            {
                ValidatorResult r = PstValidatorRunner.Run(part, Timeout);
                Assert.True(r.Opened, $"validator failed to open {Path.GetFileName(part)}");
                Assert.Empty(r.Errors);
                totalAllParts += r.TotalMessages;
                aggregate += r.Folders
                    .Where(f => FolderPathKey.Join(f.Path) == expectedKey)
                    .Sum(f => f.MessageCount);
            }

            Assert.Equal(GeneratedCount, aggregate);       // all messages in the expected folder, across parts
            Assert.Equal(GeneratedCount, totalAllParts);   // and nowhere else
        }
        finally
        {
            // mbox lives inside outDir, so the recursive delete removes it too.
            Directory.Delete(outDir, true);
        }
    }

    private static string WriteMinimalMbox(int count, out string outDir)
    {
        outDir = Path.Combine(Path.GetTempPath(), "mail2pst-limit-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        string mbox = Path.Combine(outDir, "limit.mbox");
        var baseDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var w = new StreamWriter(mbox);
        for (int i = 0; i < count; i++)
        {
            // Postmark MUST match MboxPostmark.EnvelopePostmark:
            //   ^From \S+ (Mon|...|Sun) (Jan|...|Dec)\s+\d{1,2} \d2:\d2:\d2(\s+\S+)? \d{4}\s*$
            // Exact-case day-of-week/month; mirror fixtures/sample.mbox ("From alice@example.com Mon Jan  1 ...").
            // A wrong postmark makes the parser treat the whole file as one malformed message — verify
            // against fixtures/sample.mbox before generating 65k+ records.
            w.WriteLine("From sender@example.com Mon Jan  1 00:00:00 2026");
            w.WriteLine("From: sender@example.com");
            w.WriteLine("To: rcpt@example.com");
            w.WriteLine($"Subject: msg {i}");
            w.WriteLine($"Date: {baseDate.AddSeconds(i):r}");   // valid RFC1123 date, unique per message
            w.WriteLine();
            w.WriteLine($"body {i}");
            w.WriteLine();
        }
        return mbox;
    }
}
