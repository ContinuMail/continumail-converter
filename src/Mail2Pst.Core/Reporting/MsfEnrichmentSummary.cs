// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.Reporting;

/// <summary>
/// Aggregate .msf-enrichment outcome across a conversion. Per-message counts are summed across sources;
/// SourcesAttempted = sources with a non-blank MsfPath, SourcesEnriched = context built successfully,
/// SourcesDegraded = .msf-specific failure (missing/locked/malformed). An mbox-read failure is neither.
/// </summary>
public sealed record MsfEnrichmentSummary(
    int Matched, int SkippedMissingId, int SkippedDuplicateId, int NoMsfMatch, int ExpungedMatched,
    int ExpungedDropped,
    int SourcesAttempted, int SourcesEnriched, int SourcesDegraded);
