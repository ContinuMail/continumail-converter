// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

// Characterization + regression guards for HeapOnNode allocation. They pin behavior so the
// rolling-cursor change (m_firstBlockWithPossibleSpace) stays behavior-preserving: identical
// block placement, never skipping reusable space. The throughput win itself is proven by the
// MAIL2PST_PROFILE re-run, not here.
public class HeapOnNodeAllocationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-hon-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;

    // A bare HeapOnNode cannot bootstrap itself (no HN header on block 0); CreateNewHeap is the
    // legitimate factory. HN blocks hold ~8176 bytes => ~2 max-size (3580) items per block.
    private HeapOnNode NewHeap()
    {
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();
        return HeapOnNode.CreateNewHeap(_file);
    }

    public void Dispose()
    {
        try { _file?.CloseFile(); } catch { }
        File.Delete(_path);
    }

    [Fact]
    public void Heap_RoundTrips_ItemsSpanningManyBlocks()
    {
        HeapOnNode heap = NewHeap();
        var ids = new List<(HeapID id, byte[] data)>();
        for (int i = 0; i < 200; i++)
        {
            byte[] data = Enumerable.Range(0, 300).Select(_ => (byte)(i & 0xFF)).ToArray();
            ids.Add((heap.AddItemToHeap(data), data));
        }
        Assert.True(ids.Max(x => x.id.hidBlockIndex) > 0, "fixture should span >1 block");
        foreach (var (id, data) in ids)
            Assert.Equal(data, heap.GetHeapItem(id));
    }

    // The core safety rule: a large item that cannot fit an earlier block must NOT cause the
    // cursor to skip that block — a later small item must still reuse the earlier block's space.
    [Fact]
    public void LargeItem_DoesNotAdvanceCursorPast_BlockWithSmallSpace()
    {
        HeapOnNode heap = NewHeap();
        heap.AddItemToHeap(new byte[3580]);                 // block 0
        HeapID b = heap.AddItemToHeap(new byte[3580]);      // block 0 (2 max items per ~8176 block)
        Assert.Equal(0, b.hidBlockIndex);                   // block 0 now ~1 KB free
        HeapID large = heap.AddItemToHeap(new byte[2000]);  // does NOT fit block 0 -> later block
        HeapID tiny = heap.AddItemToHeap(new byte[100]);    // must reuse block 0's small space
        Assert.True(large.hidBlockIndex > 0, $"large.blk={large.hidBlockIndex}");
        Assert.Equal(0, tiny.hidBlockIndex);
    }

    [Fact]
    public void AddAfterRemove_ReusesFreedSpaceInEarlierBlock()
    {
        HeapOnNode heap = NewHeap();
        HeapID first = heap.AddItemToHeap(new byte[3580]);  // block 0
        heap.AddItemToHeap(new byte[3580]);                 // block 0
        heap.AddItemToHeap(new byte[3580]);                 // block 1
        heap.AddItemToHeap(new byte[3580]);                 // block 1
        heap.RemoveItemFromHeap(first);                     // frees ~3.5 KB in block 0
        HeapID reused = heap.AddItemToHeap(new byte[3000]); // fits the freed block-0 space
        Assert.Equal(0, reused.hidBlockIndex);
    }

    // Bitmap blocks (index 8, then every 128) hold no items; allocation must cross them cleanly.
    // The cursor does not special-case them — they are governed by the existing AvailableSpace /
    // block-kind checks (BlockCanFit treats a bitmap block as not-fitting and the cursor skips it).
    [Fact]
    public void Allocation_CrossesBitmapBlock_AndRoundTrips()
    {
        HeapOnNode heap = NewHeap();
        var ids = new List<(HeapID id, byte[] data)>();
        int maxBlk = 0;
        for (int i = 0; i < 24; i++)
        {
            byte[] d = Enumerable.Range(0, 3580).Select(_ => (byte)(i & 0xFF)).ToArray();
            HeapID id = heap.AddItemToHeap(d);
            ids.Add((id, d));
            maxBlk = Math.Max(maxBlk, id.hidBlockIndex);
        }
        Assert.True(maxBlk >= 8, $"did not cross bitmap block 8; maxBlk={maxBlk}");
        foreach (var (id, d) in ids)
            Assert.Equal(d, heap.GetHeapItem(id));
    }
}
