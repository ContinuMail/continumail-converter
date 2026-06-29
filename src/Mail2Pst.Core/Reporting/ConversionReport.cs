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
    private int _contactsConverted;
    private int _contactsSkipped;
    private int _contactWarningCount;
    private int _appointmentsConverted;
    private int _appointmentsSkipped;
    private int _appointmentWarningCount;
    private int _tasksConverted;
    private int _tasksSkipped;
    private int _taskWarningCount;
    private readonly List<SkippedMessage> _skipped = new();
    private readonly List<SkippedMessage> _warnings = new();
    private readonly List<string> _outputFiles = new();
    private readonly List<string> _deletedFiles = new();
    private volatile bool _cancelled;

    private int _enrMatched, _enrMissing, _enrDup, _enrNoMatch, _enrExpunged, _enrDropped;
    private int _srcAttempted, _srcEnriched, _srcDegraded;
    private int _enrOrphanDropped, _lofEnabled, _lofDisabled, _lofDuplicates;

    public int ConvertedCount => Volatile.Read(ref _convertedCount);

    // Contact counters (Task 14 fills the full surface; Task 7 stub kept compatible).
    public int ContactsConverted => Volatile.Read(ref _contactsConverted);
    public int ContactsSkipped => Volatile.Read(ref _contactsSkipped);
    public int ContactWarningCount => Volatile.Read(ref _contactWarningCount);

    public int AppointmentsConverted => Volatile.Read(ref _appointmentsConverted);
    public int AppointmentsSkipped => Volatile.Read(ref _appointmentsSkipped);
    public int AppointmentWarningCount => Volatile.Read(ref _appointmentWarningCount);
    public int TasksConverted => Volatile.Read(ref _tasksConverted);
    public int TasksSkipped => Volatile.Read(ref _tasksSkipped);
    public int TaskWarningCount => Volatile.Read(ref _taskWarningCount);

    /// <summary>All written item types — the count PstOutputVerifier expects across all parts.</summary>
    public int TotalWrittenItems =>
        ConvertedCount + ContactsConverted + AppointmentsConverted + TasksConverted;

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

    public void RecordContactConverted() => Interlocked.Increment(ref _contactsConverted);

    public void RecordContactSkipped(string source, string error)
    {
        Interlocked.Increment(ref _contactsSkipped);
        AddWarning($"Contact skipped [{source}]: {error}");
    }

    public void RecordContactWarning(string message)
    {
        Interlocked.Increment(ref _contactWarningCount);
        AddWarning(message);
    }

    public void RecordAppointmentConverted() => Interlocked.Increment(ref _appointmentsConverted);
    public void RecordAppointmentSkipped(string source, string error)
    {
        Interlocked.Increment(ref _appointmentsSkipped);
        AddWarning($"Appointment skipped [{source}]: {error}");
    }
    public void RecordAppointmentWarning(string message)
    {
        Interlocked.Increment(ref _appointmentWarningCount);
        AddWarning(message);
    }
    public void RecordTaskConverted() => Interlocked.Increment(ref _tasksConverted);
    public void RecordTaskSkipped(string source, string error)
    {
        Interlocked.Increment(ref _tasksSkipped);
        AddWarning($"Task skipped [{source}]: {error}");
    }
    public void RecordTaskWarning(string message)
    {
        Interlocked.Increment(ref _taskWarningCount);
        AddWarning(message);
    }

    private void AddWarning(string message)
    {
        var entry = new SkippedMessage { SourcePath = string.Empty, Identifier = string.Empty, Reason = message };
        lock (_lock) _warnings.Add(entry);
    }

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
            contactsConverted = ContactsConverted,
            contactsSkipped = ContactsSkipped,
            contactWarnings = ContactWarningCount,
            appointmentsConverted = AppointmentsConverted,
            appointmentsSkipped = AppointmentsSkipped,
            appointmentWarnings = AppointmentWarningCount,
            tasksConverted = TasksConverted,
            tasksSkipped = TasksSkipped,
            taskWarnings = TaskWarningCount,
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
        builder.AppendLine($"Appointments: {AppointmentsConverted}");
        builder.AppendLine($"Tasks: {TasksConverted}");
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
