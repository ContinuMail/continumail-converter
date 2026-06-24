// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Mail2Pst.Core.Msf;

public sealed record LiveOffsetFilterResult(
    bool Active,
    IReadOnlySet<int> KeepIndices,
    int LiveRows,
    int UsableOffsets,
    int MatchedOffsets,
    int DuplicateLiveOffsets,
    IReadOnlyList<long> UnmatchedSample,
    string? DisabledReason);

/// <summary>
/// Decides, from a folder's .msf live offsets and the mbox's physical message offsets, whether it is
/// SAFE to export only the live messages (dropping uncompacted dead copies). Pure: no I/O. Activates
/// only when every live row has a usable offset and every offset maps to a real message boundary;
/// any uncertainty disables filtering (keep all). See the design spec for the rationale.
/// </summary>
public static class LiveOffsetFilter
{
    private const int UnmatchedSampleCap = 5;
    private static readonly IReadOnlySet<int> NoKeep = new HashSet<int>();

    public static LiveOffsetFilterResult Evaluate(
        IReadOnlyList<long?> liveOffsets, IReadOnlyList<long> physicalOffsets, long fileLength)
    {
        int liveRows = liveOffsets.Count;

        LiveOffsetFilterResult Disabled(string reason, int usable, int matched, IReadOnlyList<long> sample) =>
            new(false, NoKeep, liveRows, usable, matched, 0, sample, reason);

        // Condition 2: at least one live row. An empty set never drops the whole mbox.
        if (liveRows == 0)
            return Disabled("empty live set", 0, 0, System.Array.Empty<long>());

        // Condition 3: every row contributes a usable numeric offset in [0, fileLength).
        var usableList = new List<long>(liveRows);
        foreach (long? o in liveOffsets)
        {
            if (o is not { } v || v < 0 || v >= fileLength)
                return Disabled("row without usable offset", usableList.Count, 0, System.Array.Empty<long>());
            usableList.Add(v);
        }

        var liveSet = new HashSet<long>(usableList);
        int duplicates = usableList.Count - liveSet.Count;

        // Condition 4: every usable offset maps to a real message boundary.
        var physicalSet = new HashSet<long>(physicalOffsets);
        var unmatched = liveSet.Where(o => !physicalSet.Contains(o)).ToList();
        if (unmatched.Count > 0)
            return Disabled("live offset did not match an mbox boundary",
                usableList.Count, liveSet.Count - unmatched.Count, unmatched.Take(UnmatchedSampleCap).ToList());

        var keep = new HashSet<int>();
        for (int i = 0; i < physicalOffsets.Count; i++)
            if (liveSet.Contains(physicalOffsets[i])) keep.Add(i);

        return new LiveOffsetFilterResult(
            Active: true, KeepIndices: keep, LiveRows: liveRows, UsableOffsets: usableList.Count,
            MatchedOffsets: liveSet.Count, DuplicateLiveOffsets: duplicates,
            UnmatchedSample: System.Array.Empty<long>(), DisabledReason: null);
    }
}
