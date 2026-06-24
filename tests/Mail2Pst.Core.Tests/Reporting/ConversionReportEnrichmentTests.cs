// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests.Reporting;

public class ConversionReportEnrichmentTests
{
    [Fact]
    public void RecordEnrichment_AccumulatesCountsAndSourceOutcomes()
    {
        var report = new ConversionReport();
        report.RecordEnrichmentSource(attempted: true, enriched: true, degraded: false);
        report.RecordEnrichmentCounts(new MsfEnrichmentResult { Matched = 3, SkippedDuplicateId = 1 });
        report.RecordEnrichmentSource(attempted: true, enriched: false, degraded: true);

        MsfEnrichmentSummary s = report.EnrichmentSummary;
        Assert.Equal(3, s.Matched);
        Assert.Equal(1, s.SkippedDuplicateId);
        Assert.Equal(2, s.SourcesAttempted);
        Assert.Equal(1, s.SourcesEnriched);
        Assert.Equal(1, s.SourcesDegraded);
    }

    [Fact]
    public void ToJsonAndSummary_IncludeEnrichment()
    {
        var report = new ConversionReport();
        report.RecordEnrichmentSource(attempted: true, enriched: true, degraded: false);
        report.RecordEnrichmentCounts(new MsfEnrichmentResult { Matched = 5 });

        Assert.Contains("enrichment", report.ToJson());
        Assert.Contains("\"matched\": 5", report.ToJson());
        Assert.Contains("Enrichment", report.ToSummary());
    }

    [Fact]
    public void RecordEnrichmentCounts_AggregatesLiveOffsetCounters_AcrossSources()
    {
        var report = new Mail2Pst.Core.Reporting.ConversionReport();
        report.RecordEnrichmentCounts(new Mail2Pst.Core.Msf.MsfEnrichmentResult
        {
            OrphanedCopiesDropped = 4, LiveOffsetFilterEnabledSources = 1, DuplicateLiveOffsets = 1,
        });
        report.RecordEnrichmentCounts(new Mail2Pst.Core.Msf.MsfEnrichmentResult
        {
            LiveOffsetFilterDisabledSources = 2,
        });
        Mail2Pst.Core.Reporting.MsfEnrichmentSummary s = report.EnrichmentSummary;
        Assert.Equal(4, s.OrphanedCopiesDropped);
        Assert.Equal(1, s.LiveOffsetFilterEnabledSources);
        Assert.Equal(2, s.LiveOffsetFilterDisabledSources);
        Assert.Equal(1, s.DuplicateLiveOffsets);
    }
}
