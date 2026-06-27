// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Reporting;

namespace Mail2Pst.Core.Scanning;

/// <summary>Tunables for <see cref="ScanRunner"/>'s range-parallel scan. The defaults are the production
/// values; tests inject a tiny <see cref="TargetChunkBytes"/> to force the multi-range path.</summary>
internal sealed class ScanRunnerOptions
{
    public long TargetChunkBytes { get; init; } = 64L * 1024 * 1024;
    public int MaxDegreeOfParallelism { get; init; } = Math.Min(Environment.ProcessorCount, 8);
}

public class ScanRunner
{
    // Byte-based throttle (deterministic/testable, no clock): emit a progress
    // update once this many new bytes have been read. A guaranteed final emit at
    // scan end ensures the bar always reaches 100%.
    private const long ProgressEmitThresholdBytes = 64L * 1024 * 1024;

    private readonly ScanRunnerOptions _options;

    public ScanRunner() : this(new ScanRunnerOptions()) { }

    internal ScanRunner(ScanRunnerOptions options) => _options = options;

    /// <summary>Convenience overload for a single source.</summary>
    public ScanReport Scan(string path, string sourceType) =>
        Scan(new[] { path }, sourceType);

    /// <summary>Scans one or more sources (all of <paramref name="sourceType"/>) and
    /// returns per-source rows plus run-wide totals.</summary>
    public ScanReport Scan(IReadOnlyList<string> paths, string sourceType, Action<ScanProgress>? onProgress = null)
    {
        if (paths.Count == 0)
            throw new ArgumentException("At least one input path is required.", nameof(paths));

        // Resolve (and validate) the scan parser once, eagerly: an unsupported type must fail before we
        // touch any file. Cast to MboxParser to call ScanRange (the range-parallel measure-only API).
        MboxParser parser = ResolveScanParser(sourceType);

        // Stat each source once: reused below for per-source sourceBytes. FileInfo.Length still
        // throws FileNotFoundException here (before any parsing) for a missing path, preserving the
        // existing missing-file behavior exactly. SEQUENTIAL and first, as before.
        long[] sourceSizes = new long[paths.Count];
        long scanTotalBytes = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            long length = new FileInfo(paths[i]).Length;
            sourceSizes[i] = length;
            scanTotalBytes += length;
        }

        // Ids precomputed in source order (sequential), exactly as before.
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        string[] ids = new string[paths.Count];
        for (int i = 0; i < paths.Count; i++)
            ids[i] = MakeUniqueId(paths[i], usedIds, i);

        // Build the flat list of range work items across ALL sources. Each item carries its source
        // index (for the lowest-(source,offset) tie-break and per-source merge) and a stable global
        // range index used to slot its result/error without contention.
        var workItems = new List<(int sourceIndex, string path, long start, long end, int rangeIndex)>();
        for (int i = 0; i < paths.Count; i++)
        {
            IReadOnlyList<(long Start, long End)> ranges =
                MboxMessageSplitter.ComputeRanges(paths[i], sourceSizes[i], _options.TargetChunkBytes);
            foreach ((long start, long end) in ranges)
                workItems.Add((i, paths[i], start, end, workItems.Count));
        }

        var results = new RangeScanResult?[workItems.Count];
        var errors = new (int sourceIndex, long offset, Exception ex)?[workItems.Count];
        var agg = new ScanProgressAggregator(scanTotalBytes, onProgress, ProgressEmitThresholdBytes);

        Parallel.ForEach(
            workItems,
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism },
            work =>
            {
                try
                {
                    long prevPos = work.start;   // [R3] prevPos init = range start
                    results[work.rangeIndex] = parser.ScanRange(work.path, work.start, work.end,
                        pos => { agg.Add(pos - prevPos); prevPos = pos; });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Spill (RawMessageSpillException) and any non-FormatException/IOException from
                    // ScanRange land here → fatal. Recorded per-range; rethrown lowest-(source,offset).
                    errors[work.rangeIndex] = (work.sourceIndex, work.start, ex);
                }
            });

        // Fatal path FIRST (before EmitFinal): if any range failed, rethrow the lowest-(sourceIndex, offset)
        // one preserving its original stack — no success progress is emitted on failure. [R1/R2.5/R3]
        (int sourceIndex, long offset, Exception ex)? lowest = null;
        foreach (var e in errors)
        {
            if (e is null) continue;
            if (lowest is null
                || e.Value.sourceIndex < lowest.Value.sourceIndex
                || (e.Value.sourceIndex == lowest.Value.sourceIndex && e.Value.offset < lowest.Value.offset))
                lowest = e;
        }
        if (lowest is not null)
            ExceptionDispatchInfo.Throw(lowest.Value.ex);

        // Success only: drive the bar to 100%. [R3]
        agg.EmitFinal();

        // Merge per source (in source order). Byte-identity with the old sequential ScanRunner:
        //   - rawOrdinal increments for EVERY message (skip or not) and IS both the "message #N"
        //     identifier AND the source's `messages` count (old `messages++` was unconditional).
        //   - "message #N" is rendered ONLY here (structured RangeMessage carries no identifier). [R2 Blocker 3]
        var sources = new List<SourceScanResult>(paths.Count);
        var allSkipped = new List<SkippedMessage>();
        var allWarnings = new List<SkippedMessage>();

        int totalMessages = 0;
        long totalBytes = 0;
        long totalSourceBytes = 0;

        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];

            // This source's range results, sorted by StartOffset (file order).
            IEnumerable<RangeScanResult> sourceRanges = workItems
                .Where(w => w.sourceIndex == i)
                .Select(w => results[w.rangeIndex]!)
                .OrderBy(r => r.StartOffset);

            int rawOrdinal = 0;
            long estBytes = 0;
            int warningCount = 0;
            int skippedCount = 0;
            DateTimeOffset? dateFrom = null;
            DateTimeOffset? dateTo = null;

            foreach (RangeScanResult range in sourceRanges)
            {
                foreach (RangeMessage m in range.Messages)
                {
                    rawOrdinal++;
                    if (m.IsSkipped)
                    {
                        skippedCount++;
                        allSkipped.Add(new SkippedMessage
                        {
                            SourcePath = path,
                            Identifier = $"message #{rawOrdinal}",
                            Reason = m.SkipReason!,
                        });
                        continue;
                    }

                    estBytes += m.EstimatedBytes;
                    if (m.Date.HasValue)
                    {
                        if (dateFrom is null || m.Date < dateFrom) dateFrom = m.Date;
                        if (dateTo is null || m.Date > dateTo) dateTo = m.Date;
                    }

                    foreach (string warning in m.Warnings)
                    {
                        warningCount++;
                        allWarnings.Add(new SkippedMessage
                        {
                            SourcePath = path,
                            Identifier = $"message #{rawOrdinal}",
                            Reason = warning,
                        });
                    }
                }
            }

            long sourceBytes = sourceSizes[i];
            sources.Add(new SourceScanResult(
                ids[i], path, System.IO.Path.GetFileNameWithoutExtension(path),
                rawOrdinal, estBytes, sourceBytes,
                dateFrom, dateTo, warningCount, skippedCount));

            totalMessages += rawOrdinal;
            totalBytes += estBytes;
            totalSourceBytes += sourceBytes;
        }

        var totals = new ScanTotals(totalMessages, totalBytes, totalSourceBytes, sources.Count);
        return new ScanReport(totals, sources, allSkipped, allWarnings);
    }

    /// <summary>Resolves the measure-only scan parser. <c>internal virtual</c> so tests can inject a
    /// parser whose <see cref="MboxParser.ScanRange"/> throws (fatal-path coverage); production behavior
    /// is unchanged.</summary>
    internal virtual MboxParser ResolveScanParser(string sourceType) =>
        (MboxParser)ParserRegistry.GetForScan(sourceType);

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
