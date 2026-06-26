// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Diagnostics;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class DurableMemoryMeasurementTests
{
    private static string? CorpusConfig => Environment.GetEnvironmentVariable("MAIL2PST_MEMORY_CORPUS_CONFIG");

    [SkippableFact]
    public void Measure_LargeCorpus_EmitsWellFormedDurableMemoryReport()
    {
        Skip.If(string.IsNullOrWhiteSpace(CorpusConfig),
            "Set MAIL2PST_MEMORY_CORPUS_CONFIG to a corpus config.json to run.");

        ConversionConfig config = ConfigLoader.Load(CorpusConfig!);
        string outDir = Path.Combine(Path.GetTempPath(), "m2p-memrun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var observer = new DurableMemoryObserver();
        var runner = new ConversionRunner();
        runner.Run(config, outDir, onProgress: null, memoryObserver: observer);

        DurableMemoryReport? peak = observer.Peak;
        Assert.NotNull(peak);
        string reportPath = Path.Combine(outDir, "durable-memory-report.txt");
        File.WriteAllText(reportPath, DurableMemoryReportWriter.ToSummary(peak!));
        File.WriteAllText(Path.ChangeExtension(reportPath, ".json"), DurableMemoryReportWriter.ToJson(peak!));

        Assert.InRange(peak!.EvictableRatio, 0.0, 1.0);
        Assert.True(peak.TotalDurableBytes > 0);
        // The classification + which family dominates is read manually from the emitted report (spec §7).
    }
}
