// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mail2Pst.Core.Reverse;

/// <summary>One skipped message: which folder, its 0-based index in that folder (null when not applicable),
/// and why. Mirrors the forward <c>ConversionReport.Skipped</c>'s structured skip list.</summary>
public sealed record ExportSkip(string FolderPath, int? MessageIndex, string Reason);

/// <summary>
/// The report for a reverse (PST → Thunderbird) export. Unlike the forward <c>ConversionReport</c> (output
/// PST files, split parts, contact/task/appointment counters), the reverse export writes a folder TREE, so
/// this records the output root, the mbox files written, message/folder counts, structured per-message
/// <see cref="Skipped"/> entries, and the aggregated <see cref="Warnings"/> from the reader + reconstructor +
/// writer. Skips (a message that could not be read) are kept SEPARATE from warnings (lossy-but-recovered
/// reconstruction decisions), mirroring the forward report's Skipped/Warnings split.
/// </summary>
public sealed class ExportReport
{
    private readonly object _lock = new();
    private readonly List<string> _warnings = new();
    private readonly List<ExportSkip> _skipped = new();

    public ExportReport(string outputRoot) => OutputRoot = outputRoot;

    public string OutputRoot { get; }
    public int MessagesExported { get; private set; }
    public int FoldersWritten { get; private set; }
    public IReadOnlyList<string> MboxFiles { get; private set; } = Array.Empty<string>();
    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<string> Warnings { get { lock (_lock) return _warnings.ToArray(); } }
    public int WarningCount { get { lock (_lock) return _warnings.Count; } }
    public IReadOnlyList<ExportSkip> Skipped { get { lock (_lock) return _skipped.ToArray(); } }
    public int SkippedCount { get { lock (_lock) return _skipped.Count; } }

    /// <summary>The shared warning sink the runner injects into the reader, reconstructor, and writer.</summary>
    public void RecordWarning(string message)
    {
        lock (_lock) _warnings.Add(message);
    }

    /// <summary>The structured skip sink the runner injects into the reader (per-message read failure).</summary>
    public void RecordSkipped(ExportSkip skip)
    {
        lock (_lock) _skipped.Add(skip);
    }

    /// <summary>Folds the writer's result and the run duration into the report. Called once by the runner.</summary>
    internal void Complete(MboxTreeWriteResult result, TimeSpan elapsed)
    {
        MessagesExported = result.MessagesWritten;
        FoldersWritten = result.FoldersWritten;
        MboxFiles = result.MboxFiles.ToArray();   // snapshot — do not retain the writer's backing list
        Elapsed = elapsed;
    }

    public string ToJson()
    {
        string[] warnings;
        ExportSkip[] skipped;
        lock (_lock) { warnings = _warnings.ToArray(); skipped = _skipped.ToArray(); }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        return JsonSerializer.Serialize(new
        {
            outputRoot = OutputRoot,
            messagesExported = MessagesExported,
            foldersWritten = FoldersWritten,
            elapsedMs = (long)Elapsed.TotalMilliseconds,
            mboxFiles = MboxFiles,
            skippedCount = skipped.Length,
            skipped = Array.ConvertAll(skipped, s => new { folderPath = s.FolderPath, messageIndex = s.MessageIndex, reason = s.Reason }),
            warningCount = warnings.Length,
            warnings,
        }, options);
    }

    public string ToSummary()
    {
        string[] warnings;
        ExportSkip[] skipped;
        lock (_lock) { warnings = _warnings.ToArray(); skipped = _skipped.ToArray(); }

        var builder = new StringBuilder();
        builder.AppendLine($"Output root: {OutputRoot}");
        builder.AppendLine($"Messages exported: {MessagesExported}");
        builder.AppendLine($"Folders written: {FoldersWritten}");
        builder.AppendLine($"Mbox files: {MboxFiles.Count}");
        builder.AppendLine($"Skipped: {skipped.Length}");
        foreach (ExportSkip s in skipped)
        {
            string location = s.MessageIndex is int idx ? $"{s.FolderPath}[{idx}]" : s.FolderPath;
            builder.AppendLine($"  SKIP {location}: {s.Reason}");
        }
        builder.AppendLine($"Warnings: {warnings.Length}");
        foreach (string w in warnings)
            builder.AppendLine($"  WARN {w}");
        return builder.ToString();
    }
}
