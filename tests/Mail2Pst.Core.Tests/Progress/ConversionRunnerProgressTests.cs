// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Progress;
using Xunit;

namespace Mail2Pst.Core.Tests.Progress;

public class ConversionRunnerProgressTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static ConversionConfig MakeConfig(string mboxPath) => new()
    {
        Outputs = new List<OutputGroupConfig>
        {
            new()
            {
                Name = "Test",
                MaxSizeMB = 100,
                FolderMapping = FolderMappingMode.Mirror,
                Sources = new List<SourceConfig>
                {
                    new() { Path = mboxPath, Type = "mbox" },
                },
            },
        },
    };

    [Fact]
    public void Run_WithProgressCallback_EmitsScanEventWithCorrectTotal()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-progress-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var events = new List<ConversionProgressEvent>();
            var runner = new ConversionRunner();

            runner.Run(MakeConfig(Fixture("sample.mbox")), outputDir, events.Add);

            var scanEvent = events.OfType<ScanEvent>().Single();
            Assert.Equal(2, scanEvent.TotalMessages);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void Run_WithProgressCallback_EmitsProgressEventAfterConversion()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-progress-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var events = new List<ConversionProgressEvent>();
            // checkIntervalMessages=1 so every message triggers a checkpoint
            var runner = new ConversionRunner(checkIntervalMessages: 1);

            runner.Run(MakeConfig(Fixture("sample.mbox")), outputDir, events.Add);

            var progressEvents = events.OfType<ProgressEvent>().ToList();
            Assert.NotEmpty(progressEvents);
            Assert.Contains(progressEvents, e => e.Converted == 1 && e.TotalMessages == 2);
            var last = progressEvents.Last();
            Assert.Equal(2, last.Converted);
            Assert.Equal(2, last.TotalMessages);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void Run_WithProgressCallback_EmitsWarningEventForBrokenAttachment()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-progress-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var events = new List<ConversionProgressEvent>();
            var runner = new ConversionRunner();

            runner.Run(MakeConfig(Fixture("mbox-with-broken-attachment.mbox")), outputDir, events.Add);

            var warningEvents = events.OfType<WarningEvent>().ToList();
            Assert.NotEmpty(warningEvents);
            Assert.Contains(warningEvents, e => e.Reason.Contains("broken.bin"));
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void Run_WithoutProgressCallback_CompletesNormally()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-progress-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var runner = new ConversionRunner();
            var report = runner.Run(MakeConfig(Fixture("sample.mbox")), outputDir);
            Assert.Equal(2, report.ConvertedCount);
        }
        finally { Directory.Delete(outputDir, true); }
    }
}
