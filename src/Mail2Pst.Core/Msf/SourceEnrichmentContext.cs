// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Mork;
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

    public MsfEnrichmentResult Result { get; } = new();

    private SourceEnrichmentContext(MsfJoinIndex index, MsfEnrichmentOptions options)
    {
        _index = index;
        _options = options;
    }

    public bool Apply(MailMessage message) => MsfEnricher.TryApply(message, _index, _options, Result);

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
        try
        {
            MorkDocument doc = MorkReader.ParseSharedReadWrite(msfPath);
            MsfReadResult msf = MsfMessageReader.Read(doc);
            index = MsfJoinIndex.Build(msf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MorkFormatException)
        {
            Degrade(report, onProgress, sourceRef, $"Could not read paired .msf; message flags/tags not applied: {ex.Message}");
            return null;
        }

        try
        {
            // Confirm the mbox is readable before committing to enrichment. A cheap open-probe, not a
            // full scan: mbox-side duplicate Message-IDs no longer block enrichment (a unique .msf row
            // applies to every copy), so the former headers-only duplicate pre-pass is gone.
            using FileStream probe = File.OpenRead(source.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The mbox itself is missing/unreadable — NOT an .msf degradation. The normal parse path
            // records the single authoritative source skip; do not warn or count degraded here.
            report.RecordEnrichmentSource(attempted: true, enriched: false, degraded: false);
            return null;
        }

        report.RecordEnrichmentSource(attempted: true, enriched: true, degraded: false);
        return new SourceEnrichmentContext(index, options);
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
