// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using Mail2Pst.Core.Diagnostics;
using Xunit;

namespace Mail2Pst.Core.Tests.Diagnostics;

public class DurableMemoryObserverTests
{
    private static DurableMemoryReport R(long payload) => new DurableMemoryReport(
        new[] { new FamilyResidency("blockBuffer", 1, payload, 0, payload / 2, payload / 2) }, 0, 0);

    [Fact]
    public void Observer_KeepsThePeakReport()
    {
        var obs = new DurableMemoryObserver();
        obs.Observe(R(100));
        obs.Observe(R(900));        // peak
        obs.Observe(R(300));
        Assert.NotNull(obs.Peak);
        Assert.Equal(900, obs.Peak!.TotalDurableBytes);
    }

    [Fact]
    public void ReportWriter_EmitsRatioAndClassification()
    {
        string summary = DurableMemoryReportWriter.ToSummary(R(1000));
        Assert.Contains("evictableRatio", summary, System.StringComparison.OrdinalIgnoreCase);
        string json = DurableMemoryReportWriter.ToJson(R(1000));
        Assert.Contains("\"classification\"", json);
    }
}
