// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;

namespace Mail2Pst.Core.Scanning;

public class ScanRunner
{
    // Byte-based throttle (deterministic/testable, no clock): emit a progress
    // update once this many new bytes have been read. A guaranteed emit at each
    // file's end ensures the bar always reaches the file/total boundary.
    private const long ProgressEmitThresholdBytes = 64L * 1024 * 1024;

    /// <summary>Convenience overload for a single source.</summary>
    public ScanReport Scan(string path, string sourceType) =>
        Scan(new[] { path }, sourceType);

    /// <summary>Scans one or more sources (all of <paramref name="sourceType"/>) and
    /// returns per-source rows plus run-wide totals.</summary>
    public ScanReport Scan(IReadOnlyList<string> paths, string sourceType, Action<ScanProgress>? onProgress = null)
    {
        if (paths.Count == 0)
            throw new ArgumentException("At least one input path is required.", nameof(paths));

        // Resolve (and validate) the parser once, eagerly: an unsupported type must
        // fail before we touch any file, and the lookup is identical for every source.
        IMailSourceParser parser = ParserRegistry.GetForScan(sourceType);

        // Stat each source once: reused below for per-source sourceBytes. FileInfo.Length still
        // throws FileNotFoundException here (before any parsing) for a missing path, preserving the
        // existing missing-file behavior exactly.
        long[] sourceSizes = new long[paths.Count];
        long scanTotalBytes = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            long length = new FileInfo(paths[i]).Length;
            sourceSizes[i] = length;
            scanTotalBytes += length;
        }

        // Progress accounting is kept in its OWN variable, decoupled from the
        // report's `totalSourceBytes` total, so an agent can't accidentally break
        // cumulative progress by moving the report accumulation around.
        long lastEmittedBytes = 0;
        long completedSourceBytes = 0;

        var sources = new List<SourceScanResult>();
        var allSkipped = new List<SkippedMessage>();
        var allWarnings = new List<SkippedMessage>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        int totalMessages = 0;
        long totalBytes = 0;
        long totalSourceBytes = 0;

        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];

            string id = MakeUniqueId(path, usedIds, i);
            string displayName = System.IO.Path.GetFileNameWithoutExtension(path);
            long sourceBytes = sourceSizes[i];

            int messages = 0;
            long estimatedBytes = 0;
            int warningCount = 0;
            int skippedCount = 0;
            DateTimeOffset? dateFrom = null;
            DateTimeOffset? dateTo = null;

            // Bytes from already-completed files (own progress accumulator,
            // independent of the report's totalSourceBytes).
            long fileBase = completedSourceBytes;
            Action<long>? onBytesRead = onProgress is null ? null : pos =>
            {
                long cumulative = Math.Min(fileBase + pos, scanTotalBytes);
                if (cumulative - lastEmittedBytes >= ProgressEmitThresholdBytes)
                {
                    lastEmittedBytes = cumulative;
                    onProgress(new ScanProgress(cumulative, scanTotalBytes));
                }
            };

            foreach (ParseResult result in parser.Parse(path, onBytesRead))
            {
                messages++;
                if (!result.Success)
                {
                    skippedCount++;
                    allSkipped.Add(new SkippedMessage
                    {
                        SourcePath = path,
                        Identifier = result.Source.Identifier,
                        Reason = result.Error!,
                    });
                    continue;
                }

                estimatedBytes += PstWriter.EstimateMessageSize(result.Message!);

                DateTimeOffset? date = result.Message!.Date;
                if (date.HasValue)
                {
                    if (dateFrom is null || date < dateFrom) dateFrom = date;
                    if (dateTo is null || date > dateTo) dateTo = date;
                }

                foreach (string warning in result.Warnings)
                {
                    warningCount++;
                    allWarnings.Add(new SkippedMessage
                    {
                        SourcePath = path,
                        Identifier = result.Source.Identifier,
                        Reason = warning,
                    });
                }

                foreach (MailAttachment attachment in result.Message!.Attachments)
                    attachment.Content.Dispose();
            }

            // Guaranteed emit at this file's end so the bar reaches the file
            // boundary even if the last chunk was under the throttle. For the
            // last file this equals scanTotalBytes (the scan-end / 100% emit).
            // Skip if a throttled emit already landed exactly here (no duplicate).
            if (onProgress is not null)
            {
                long fileEnd = Math.Min(fileBase + sourceBytes, scanTotalBytes);
                if (fileEnd != lastEmittedBytes)
                {
                    lastEmittedBytes = fileEnd;
                    onProgress(new ScanProgress(fileEnd, scanTotalBytes));
                }
            }
            completedSourceBytes += sourceBytes;

            sources.Add(new SourceScanResult(
                id, path, displayName, messages, estimatedBytes, sourceBytes,
                dateFrom, dateTo, warningCount, skippedCount));

            totalMessages += messages;
            totalBytes += estimatedBytes;
            totalSourceBytes += sourceBytes;
        }

        var totals = new ScanTotals(totalMessages, totalBytes, totalSourceBytes, sources.Count);
        return new ScanReport(totals, sources, allSkipped, allWarnings);
    }

    private static string MakeUniqueId(string path, HashSet<string> used, int index)
    {
        string baseId = Slug(System.IO.Path.GetFileNameWithoutExtension(path));
        if (baseId.Length == 0) baseId = $"source-{index + 1}";

        string candidate = baseId;
        int suffix = 2;
        while (!used.Add(candidate))
            candidate = $"{baseId}-{suffix++}";
        return candidate;
    }

    private static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        string collapsed = Regex.Replace(sb.ToString(), "-+", "-");
        return collapsed.Trim('-');
    }
}
