// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Parsing;

namespace Mail2Pst.Core.Scanning;

/// <summary>
/// Fail-closed byte-range split discovery for the range-parallel scan `[spec §2.3.3, R3]`. Computes
/// message-aligned <c>(Start, End)</c> ranges that tile <c>[0, length)</c> exactly. Each interior split
/// is found by seeking near an even target offset and scanning forward to the first REAL message boundary
/// (<see cref="MboxParser.FindBoundaryAtOrAfter"/> — the parser's own boundary engine, so a returned split
/// can never disagree with where <see cref="MboxParser.ScanRange"/> begins a message). If a target finds no
/// boundary within its scan cap that split is dropped (the range absorbs the oversized message). The final
/// list is validated against the tiling invariant; on ANY violation it fails closed to the single whole-file
/// range <c>[(0, length)]</c>, so a degenerate discovery can never corrupt the scan — only forgo parallelism.
/// </summary>
public static class MboxMessageSplitter
{
    public static IReadOnlyList<(long Start, long End)> ComputeRanges(string path, long length, long targetChunkBytes)
    {
        if (targetChunkBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetChunkBytes), targetChunkBytes,
                "Target chunk size must be positive.");

        if (length == 0)
            return new[] { (0L, 0L) };

        // A file no larger than one chunk needs no interior split — skip discovery entirely.
        if (length <= targetChunkBytes)
            return new[] { (0L, length) };

        long n = Math.Max(1, (length + targetChunkBytes - 1) / targetChunkBytes);   // ceil(length / chunk)
        long scanCap = targetChunkBytes;

        var offsets = new List<long>();
        using (FileStream stream = File.OpenRead(path))
        {
            for (long k = 1; k < n; k++)
            {
                long target = k * length / n;
                long? boundary = MboxParser.FindBoundaryAtOrAfter(stream, target, scanCap);
                if (boundary is long b)
                    offsets.Add(b);
            }
        }

        // Keep only strictly-interior offsets, deduped and sorted.
        List<long> splits = offsets
            .Where(o => o > 0 && o < length)
            .Distinct()
            .OrderBy(o => o)
            .ToList();

        var ranges = new List<(long Start, long End)>(splits.Count + 1);
        long prev = 0;
        foreach (long s in splits)
        {
            ranges.Add((prev, s));
            prev = s;
        }
        ranges.Add((prev, length));

        // Fail-closed: any breach of the tiling invariant collapses to the whole-file range.
        return RangesTile(ranges, length) ? ranges : new[] { (0L, length) };
    }

    /// <summary>
    /// True iff the ranges form a sorted, gap-free, overlap-free tiling of <c>[0, length)</c>: first starts
    /// at 0, last ends at <paramref name="length"/>, each range is non-empty, and each starts exactly where
    /// the previous one ended.
    /// </summary>
    private static bool RangesTile(IReadOnlyList<(long Start, long End)> ranges, long length)
    {
        if (ranges.Count == 0)
            return false;
        if (ranges[0].Start != 0 || ranges[^1].End != length)
            return false;

        for (int i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Start >= ranges[i].End)               // empty or inverted
                return false;
            if (i > 0 && ranges[i].Start != ranges[i - 1].End)  // gap or overlap
                return false;
        }

        return true;
    }
}
