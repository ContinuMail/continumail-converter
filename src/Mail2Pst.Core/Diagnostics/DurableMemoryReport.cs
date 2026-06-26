// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Mail2Pst.Core.Diagnostics;

/// <summary>Per-accumulator-family residency at one snapshot. Bytes are payload (content) bytes;
/// Evictable = bytes in buffered full leaf DataBlocks that pass the §4 safe-eviction predicate.</summary>
public readonly record struct FamilyResidency(
    string Family, int InstanceCount, long PayloadBytes, long PendingBytes, long EvictableBytes, long PinnedBytes);

/// <summary>Aggregate durable-memory snapshot for one PST part at one checkpoint (spec §7).
/// Measurement-only: classifies the residual; it does not bound memory.</summary>
public sealed record DurableMemoryReport(
    IReadOnlyList<FamilyResidency> Families, long FileLengthBytes, int MessagesWritten)
{
    /// <summary>Acceptance ratio threshold [A5]: below this, the soft budget cannot be sold as a bound.</summary>
    public const double EvictableDominantThreshold = 0.50;

    public long TotalDurableBytes => Families.Sum(f => f.PayloadBytes);
    public long EvictableBytes => Families.Sum(f => f.EvictableBytes);

    public double EvictableRatio =>
        TotalDurableBytes == 0 ? 0.0 : (double)EvictableBytes / TotalDurableBytes;

    /// <summary>Decision-rule classification (§7): only "evictable-leaf-dominant" warrants a follow-up prune spec.</summary>
    public string Classification =>
        EvictableRatio >= EvictableDominantThreshold ? "evictable-leaf-dominant" : "pinned-dominant";
}
