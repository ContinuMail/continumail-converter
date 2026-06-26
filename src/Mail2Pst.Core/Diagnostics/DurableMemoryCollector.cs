// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using PSTFileFormat;

namespace Mail2Pst.Core.Diagnostics;

/// <summary>
/// Walks the live PST writer structures and aggregates the five durable accumulator families (spec §7)
/// into a DurableMemoryReport. Read-only: it only reads the …ForTest residency snapshots. The folder
/// traversal (folders -> GetContentsTable() -> Heap -> DataTree) is REQUIRED [R2:M2]: keying only on
/// PSTFile's public BlockBTree/NodeBTree would miss family #1 (the evictable folder-TC leaves) -> false 0% ratio.
/// Assumes already-materialized folders: GetContentsTable() lazy-loads and caches the contents table, so
/// call this only on folders the writer has already populated (true at the PstPartManager checkpoint hook).
/// </summary>
public static class DurableMemoryCollector
{
    public static DurableMemoryReport Collect(PSTFile file, IEnumerable<PSTFolder> openFolders, int messagesWritten)
    {
        // Family #1: folder contents-table DataTree block buffers (the evictable leaf term).
        int bbCount = 0; long bbPayload = 0, bbPending = 0, bbEvictable = 0;

        // Family #3 + partial #4: heap decoded-cache + free-space/row-index counts.
        int heapCount = 0; long heapBytes = 0; int freeIdx = 0, blockAvail = 0, rowIndex = 0;

        foreach (PSTFolder folder in openFolders)
        {
            NamedTableContext tc = folder.GetContentsTable();
            rowIndex += tc.RowCount;

            HeapOnNode heap = tc.Heap;
            var hr = heap.HeapResidencyForTest();
            heapCount += hr.bufferCount;
            heapBytes += hr.decodedBytes;
            freeIdx   += hr.freeIndexEntries;
            blockAvail += hr.blockAvailEntries;

            // Family #1: the TC's backing DataTree block buffer (the evictable leaf measure).
            // Deferred: the TC's SubnodeBTree block buffer is not summed here (empty/minimal for
            // small-to-medium TCs); only the contents-table DataTree is traversed.
            DataTree dt = tc.DataTree;
            ulong? rootBid = dt.RootBlock?.BlockID.Value;
            var dr = dt.BlockBufferResidencyForTest(rootBid);
            bbCount    += dr.count;
            bbPayload  += dr.payload;
            bbPending  += dr.pending;
            bbEvictable += dr.evictable;
        }

        // pinned = payload that is neither pending nor evictable; clamp at 0 (pending+evictable
        // are disjoint subsets of payload by construction, so this never goes negative in practice).
        long bbPinned = bbPayload - bbPending - bbEvictable;
        if (bbPinned < 0) bbPinned = 0;

        // Family #2: the two file-level page B-trees (BBT + NBT), always pinned.
        var bbt = file.BlockBTree.PageBufferResidencyForTest();
        var nbt = file.NodeBTree.PageBufferResidencyForTest();
        long pagePinned = bbt.bytes + nbt.bytes;
        int pageCount = bbt.count + nbt.count;
        long pagePending = bbt.pending + nbt.pending;

        // Family #5: AMap cache pages + free index, pinned.
        var amap = file.AMapResidencyForTest();

        var families = new List<FamilyResidency>
        {
            new("blockBuffer",               bbCount,                    bbPayload,  bbPending,  bbEvictable,  bbPinned),
            new("pageBuffer",                pageCount,                  pagePinned, pagePending, 0,           pagePinned),
            new("heapDecodedCache",          heapCount,                  heapBytes,  0,          0,            heapBytes),
            new("freeSpaceIndex+rowIndex",   freeIdx + blockAvail + rowIndex, 0,    0,          0,            0),
            new("amapCache",                 amap.pageCount,             amap.bytes, 0,          0,            amap.bytes),
        };

        return new DurableMemoryReport(families, file.BaseStream.Length, messagesWritten);
    }
}
