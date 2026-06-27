// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Mork;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Progress;
using Mail2Pst.Core.Reporting;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Per-source .msf enrichment for the streaming conversion path. TryCreate builds the join index +
/// mbox-duplicate set, recording the source-level outcome (attempted/enriched/degraded) and any
/// degradation warning on the report. Optional metadata: ANY .msf failure degrades (warns, no abort);
/// an mbox pre-pass I/O failure drops the context silently so the parse path reports the source skip
/// exactly once (no double-report).
/// </summary>
internal sealed class SourceEnrichmentContext
{
    private readonly MsfJoinIndex _index;
    private readonly MsfEnrichmentOptions _options;
    private readonly LiveOffsetFilterResult _filter;

    public MsfEnrichmentResult Result { get; } = new();

    private SourceEnrichmentContext(MsfJoinIndex index, MsfEnrichmentOptions options, LiveOffsetFilterResult filter)
    {
        _index = index;
        _options = options;
        _filter = filter;
    }

    public bool Apply(MailMessage message) => MsfEnricher.TryApply(message, _index, _options, Result);

    /// <summary>Pure predicate (no mutation). True iff filtering is active and this index is not live.
    /// The caller increments OrphanedCopiesDropped at the actual drop site.</summary>
    public bool ShouldDropOrphan(int messageIndex) =>
        _filter.Active && !_filter.KeepIndices.Contains(messageIndex);

    public static SourceEnrichmentContext? TryCreate(
        SourceConfig source, MsfEnrichmentOptions options, ConversionReport report,
        Action<ConversionProgressEvent>? onProgress = null)
    {
        string? msfPath = source.MsfPath;
        if (string.IsNullOrWhiteSpace(msfPath))
        {
            report.RecordEnrichmentSource(attempted: false, enriched: false, degraded: false);
            return null;
        }

        var sourceRef = new SourceReference { SourcePath = source.Path, Identifier = "(.msf)" };

        if (!File.Exists(msfPath))
        {
            Degrade(report, onProgress, sourceRef, $"Paired .msf not found; message flags/tags not applied: {msfPath}");
            return null;
        }

        MsfJoinIndex index;
        MsfReadResult msf;
        try
        {
            MorkDocument doc = MorkReader.ParseSharedReadWrite(msfPath);
            msf = MsfMessageReader.Read(doc);
            index = MsfJoinIndex.Build(msf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MorkFormatException)
        {
            Degrade(report, onProgress, sourceRef, $"Could not read paired .msf; message flags/tags not applied: {ex.Message}");
            return null;
        }

        LiveOffsetFilterResult filter;
        try
        {
            IReadOnlyList<long> physical = new MboxParser().ScanMessageStartOffsets(source.Path);
            long fileLength = new FileInfo(source.Path).Length;
            var liveOffsets = msf.Messages.Select(m => m.LiveOffset).ToList();
            filter = LiveOffsetFilter.Evaluate(liveOffsets, physical, fileLength);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // mbox missing/unreadable — not an .msf degradation; the parse path reports the source skip once.
            report.RecordEnrichmentSource(attempted: true, enriched: false, degraded: false);
            return null;
        }

        report.RecordEnrichmentSource(attempted: true, enriched: true, degraded: false);
        var context = new SourceEnrichmentContext(index, options, filter);

        // Per-source filter counters live on context.Result; they reach the summary via the EXISTING
        // RecordEnrichmentCounts(context.Result) call in ConversionRunner's finally (NOT set on the summary
        // here — EnrichmentSummary is a computed getter).
        context.Result.DuplicateLiveOffsets = filter.DuplicateLiveOffsets;
        if (filter.Active)
        {
            context.Result.LiveOffsetFilterEnabledSources = 1;
        }
        else if (filter.LiveRows > 0)   // HAD a live set but refused to filter -> count it (keep all)
        {
            // A disabled filter is an internal optimisation declining to run (the common, expected case
            // for IMAP-derived .msf, which usually lacks usable per-row offsets), NOT a user-actionable
            // problem. The .msf is NOT degraded and the source stays enriched:true. We keep the aggregate
            // count on the enrichment summary for diagnostics but DO NOT surface a per-source warning or
            // WarningEvent — doing so produced one alarming warning per IMAP folder for no user benefit.
            context.Result.LiveOffsetFilterDisabledSources = 1;
        }
        // filter.LiveRows == 0 (empty live set): keep all, NO warning, NOT counted disabled — nothing to filter.
        return context;
    }

    // An .msf-specific degradation: record on the report AND emit the live WarningEvent so streaming
    // consumers (CLI/GUI) see it, mirroring how parse warnings are surfaced in ConversionRunner.
    private static void Degrade(
        ConversionReport report, Action<ConversionProgressEvent>? onProgress, SourceReference sourceRef, string warning)
    {
        report.RecordWarning(sourceRef, warning);
        onProgress?.Invoke(new WarningEvent(sourceRef.SourcePath, sourceRef.Identifier, warning));
        report.RecordEnrichmentSource(attempted: true, enriched: false, degraded: true);
    }
}
