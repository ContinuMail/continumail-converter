// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Mail2Pst.Core.Reverse;

/// <summary>A progress tick, emitted AFTER a message is successfully written to the mbox tree.</summary>
public sealed record ExportProgress(int MessagesExported, string CurrentFolder);

/// <summary>
/// Orchestrates the reverse (PST → Thunderbird) mail export — the mirror image of the forward
/// <c>ConversionRunner</c>. Two passes over the source PST, NEITHER of which modifies it: pass 1
/// (<see cref="PstMailReader.EnumerateFolders"/>) defines the full folder structure; pass 2
/// (<see cref="PstMailReader.EnumerateMessages"/>) streams messages one at a time THROUGH the
/// <see cref="MboxTreeWriter"/> (never materialized), reconstructing each to MIME via the injected
/// <see cref="IMimeReconstructor"/>. A single warning collector is injected into the reader, reconstructor,
/// and writer; a structured skip sink into the reader — so every lossy/skip point lands in the returned
/// <see cref="ExportReport"/>. Progress ticks fire only AFTER a message is on disk.
///
/// Because <see cref="MboxTreeWriter"/> APPENDS, output is written to a sibling STAGING directory and
/// published to <paramref name="outputRoot"/> only on success, with the atomic directory move as the FINAL
/// throwable operation; a mid-export failure removes the staging dir and leaves <paramref name="outputRoot"/>
/// untouched. A non-empty existing destination (or an existing file at that path) is rejected up front.
///
/// Failure policy: PST message-read failures AND corrupt PST subtrees are reported+skipped inside
/// <see cref="PstMailReader"/>; lossy-but-recovered reconstruction decisions are warnings. UNEXPECTED
/// reconstruction / attachment-read / MIME-serialization / filesystem-write failures are FATAL and propagate
/// (the writer does not catch them) — the CLI turns a propagated failure into a fatal error event.
/// A fatal PST-open / root-walk failure likewise propagates.
/// </summary>
public sealed class PstExportRunner
{
    private readonly Func<Action<string>?, IMimeReconstructor> _reconstructorFactory;

    public PstExportRunner(Func<Action<string>?, IMimeReconstructor>? reconstructorFactory = null)
        => _reconstructorFactory = reconstructorFactory ?? (onWarning => new MimeReconstructor(onWarning));

    /// <param name="onWarning">Optional live sink (the CLI's warning-event stream). Invoked IN ADDITION to
    /// recording into the report. Skips are NOT forwarded here — see <paramref name="onSkipped"/>.</param>
    /// <param name="onProgress">Optional live progress sink, one tick per successfully written message.</param>
    /// <param name="onSkipped">Optional live sink for structured per-message skips (the CLI's skip-event
    /// stream). Invoked IN ADDITION to recording into the report; disjoint from <paramref name="onWarning"/>.</param>
    public ExportReport Run(
        string pstPath, string outputRoot, bool includeEmpty,
        Action<string>? onWarning = null, Action<ExportProgress>? onProgress = null,
        Action<ExportSkip>? onSkipped = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pstPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        // Normalize FIRST: a trailing separator makes Path.GetFileName empty -> staging would land INSIDE dest.
        string fullOutput = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));

        // Reject an existing FILE, and a non-empty existing DIR, before doing any work (MboxTreeWriter APPENDS).
        if (File.Exists(fullOutput))
            throw new IOException($"Output path '{fullOutput}' is an existing file.");
        if (Directory.Exists(fullOutput) && Directory.EnumerateFileSystemEntries(fullOutput).Any())
            throw new IOException(
                $"Output directory '{fullOutput}' already exists and is not empty; refusing to export into it.");

        var report = new ExportReport(fullOutput);

        // Two DISTINCT sinks: Collect writes report.Warnings (+ live warning stream). CollectSkip writes
        // report.Skipped ONLY (never report.Warnings — no double count) and forwards the STRUCTURED skip to
        // the live skip sink, so a consumer's warning and skip counts stay disjoint and match the report.
        void Collect(string message) { report.RecordWarning(message); onWarning?.Invoke(message); }
        void CollectSkip(ExportSkip skip)
        {
            report.RecordSkipped(skip);
            onSkipped?.Invoke(skip);
        }

        var stopwatch = Stopwatch.StartNew();

        // Pass 1 (structure authority) also performs the fatal PST-open — a bad/missing/corrupt store throws
        // here and propagates out of Run (before any staging dir is created).
        IReadOnlyList<IReadOnlyList<string>> folders = PstMailReader.EnumerateFolders(pstPath, Collect);

        string parent = Path.GetDirectoryName(fullOutput) ?? fullOutput;
        string staging = Path.Combine(parent, Path.GetFileName(fullOutput) + ".partial-" + Guid.NewGuid().ToString("N"));

        try
        {
            IMimeReconstructor reconstructor = _reconstructorFactory(Collect)
                ?? throw new InvalidOperationException("Reconstructor factory returned null.");
            var writer = new MboxTreeWriter(reconstructor);

            // Progress fires from the writer AFTER a successful append, so a message that throws never ticks.
            Action<PstMailItem, int>? written = onProgress is null
                ? null
                : (item, count) => onProgress(new ExportProgress(count, FolderPathDisplay.Join(item.FolderPath)));

            // Pass 2: lazy stream straight into the writer (attachment OpenRead fires while the item is current;
            // no .ToList()). Skips flow to CollectSkip, warnings to Collect.
            MboxTreeWriteResult staged = writer.Write(
                PstMailReader.EnumerateMessages(pstPath, Collect, CollectSkip),
                folders, staging, includeEmpty, Collect, written);

            // Build the published result BEFORE the move, so the move is the final throwable op.
            string[] publishedFiles = staged.MboxFiles
                .Select(f => Path.Combine(fullOutput, Path.GetRelativePath(staging, f)))
                .ToArray();
            MboxTreeWriteResult published = staged with { MboxFiles = publishedFiles };
            stopwatch.Stop();
            report.Complete(published, stopwatch.Elapsed);

            // Re-check the destination immediately before publishing (TOCTOU): only remove an ALLOWED empty dir.
            if (Directory.Exists(fullOutput))
            {
                if (Directory.EnumerateFileSystemEntries(fullOutput).Any())
                    throw new IOException($"Output directory '{fullOutput}' became non-empty during export.");
                Directory.Delete(fullOutput, recursive: false);
            }
            Directory.Move(staging, fullOutput);   // FINAL throwable op
            return report;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* best-effort staging cleanup; surface the original failure */ }
            throw;
        }
    }
}
