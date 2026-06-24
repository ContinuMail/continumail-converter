// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Msf;

namespace Mail2Pst.Core.Reporting;

public class ConversionReport
{
    private readonly object _lock = new();
    private int _convertedCount;
    private readonly List<SkippedMessage> _skipped = new();
    private readonly List<SkippedMessage> _warnings = new();
    private readonly List<string> _outputFiles = new();
    private readonly List<string> _deletedFiles = new();
    private volatile bool _cancelled;

    private int _enrMatched, _enrMissing, _enrDup, _enrNoMatch, _enrExpunged, _enrDropped;
    private int _srcAttempted, _srcEnriched, _srcDegraded;
    private int _enrOrphanDropped, _lofEnabled, _lofDisabled, _lofDuplicates;

    public int ConvertedCount => Volatile.Read(ref _convertedCount);

    // Cheap, lock-protected counts for the hot path (progress ticks) that don't
    // need to copy the whole list.
    public int SkippedCount { get { lock (_lock) return _skipped.Count; } }
    public int WarningCount { get { lock (_lock) return _warnings.Count; } }

    // Snapshots: each access returns an immutable copy taken under the lock, so
    // callers can safely enumerate while the producer/consumer threads keep
    // recording. Never hand out the live backing list.
    public IReadOnlyList<SkippedMessage> Skipped { get { lock (_lock) return _skipped.ToArray(); } }
    public IReadOnlyList<SkippedMessage> Warnings { get { lock (_lock) return _warnings.ToArray(); } }
    public IReadOnlyList<string> OutputFiles { get { lock (_lock) return _outputFiles.ToArray(); } }

    public void AddOutputFiles(IEnumerable<string> files)
    {
        lock (_lock) _outputFiles.AddRange(files);
    }

    public bool Cancelled => _cancelled;

    public IReadOnlyList<string> DeletedFiles { get { lock (_lock) return _deletedFiles.ToArray(); } }

    public void MarkCancelled() => _cancelled = true;

    public void RecordDeletedFile(string path)
    {
        lock (_lock) _deletedFiles.Add(path);
    }

    public void RecordConverted() => Interlocked.Increment(ref _convertedCount);

    public void RecordSkipped(SourceReference source, string reason)
    {
        var entry = new SkippedMessage { SourcePath = source.SourcePath, Identifier = source.Identifier, Reason = reason };
        lock (_lock) _skipped.Add(entry);
    }

    public void RecordWarning(SourceReference source, string reason)
    {
        var entry = new SkippedMessage { SourcePath = source.SourcePath, Identifier = source.Identifier, Reason = reason };
        lock (_lock) _warnings.Add(entry);
    }

    public void RecordEnrichmentSource(bool attempted, bool enriched, bool degraded)
    {
        lock (_lock)
        {
            if (attempted) _srcAttempted++;
            if (enriched) _srcEnriched++;
            if (degraded) _srcDegraded++;
        }
    }

    public void RecordEnrichmentCounts(MsfEnrichmentResult result)
    {
        lock (_lock)
        {
            _enrMatched += result.Matched;
            _enrMissing += result.SkippedMissingId;
            _enrDup += result.SkippedDuplicateId;
            _enrNoMatch += result.NoMsfMatch;
            _enrExpunged += result.ExpungedMatched;
            _enrDropped += result.ExpungedDropped;
            _enrOrphanDropped += result.OrphanedCopiesDropped;
            _lofEnabled += result.LiveOffsetFilterEnabledSources;
            _lofDisabled += result.LiveOffsetFilterDisabledSources;
            _lofDuplicates += result.DuplicateLiveOffsets;
        }
    }

    public MsfEnrichmentSummary EnrichmentSummary
    {
        get
        {
            lock (_lock)
                return new MsfEnrichmentSummary(
                    _enrMatched, _enrMissing, _enrDup, _enrNoMatch, _enrExpunged,
                    _enrDropped,
                    _srcAttempted, _srcEnriched, _srcDegraded,
                    _enrOrphanDropped, _lofEnabled, _lofDisabled, _lofDuplicates);
        }
    }

    public string ToJson()
    {
        // Single consistent snapshot under the lock, then serialize outside it.
        SkippedMessage[] skipped, warnings;
        lock (_lock)
        {
            skipped = _skipped.ToArray();
            warnings = _warnings.ToArray();
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        return JsonSerializer.Serialize(new
        {
            converted = ConvertedCount,
            skipped = skipped.Select(s => new { source = s.SourcePath, identifier = s.Identifier, reason = s.Reason }),
            warnings = warnings.Select(w => new { source = w.SourcePath, identifier = w.Identifier, reason = w.Reason }),
            enrichment = EnrichmentSummary,
        }, options);
    }

    public string ToSummary()
    {
        SkippedMessage[] skipped, warnings;
        lock (_lock)
        {
            skipped = _skipped.ToArray();
            warnings = _warnings.ToArray();
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Converted: {ConvertedCount}");
        builder.AppendLine($"Skipped: {skipped.Length}");

        foreach (SkippedMessage skip in skipped)
        {
            builder.AppendLine($"  SKIP {skip.SourcePath} [{skip.Identifier}]: {skip.Reason}");
        }

        builder.AppendLine($"Warnings: {warnings.Length}");

        foreach (SkippedMessage warning in warnings)
        {
            builder.AppendLine($"  WARN {warning.SourcePath} [{warning.Identifier}]: {warning.Reason}");
        }

        MsfEnrichmentSummary enr = EnrichmentSummary;
        builder.AppendLine(
            $"Enrichment: matched={enr.Matched} missingId={enr.SkippedMissingId} " +
            $"duplicateId={enr.SkippedDuplicateId} noMsfMatch={enr.NoMsfMatch} expunged={enr.ExpungedMatched} " +
            $"dropped={enr.ExpungedDropped} " +
            $"(sources attempted={enr.SourcesAttempted} enriched={enr.SourcesEnriched} degraded={enr.SourcesDegraded}) " +
            $"orphansDropped={enr.OrphanedCopiesDropped} lofEnabled={enr.LiveOffsetFilterEnabledSources} " +
            $"lofDisabled={enr.LiveOffsetFilterDisabledSources} dupLiveOffsets={enr.DuplicateLiveOffsets}");

        return builder.ToString();
    }
}
