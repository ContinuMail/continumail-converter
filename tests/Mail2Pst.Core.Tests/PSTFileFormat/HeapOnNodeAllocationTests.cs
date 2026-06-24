// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

// Placement-agnostic characterization + regression guards for HeapOnNode allocation. They assert
// correctness, space reuse, and the count cap — NOT exact block placement (the free-space-index
// allocator uses best-fit, so placement differs from the old first-fit scan; placement is internal
// and items resolve by HeapID regardless). A standalone heap is created via CreateNewHeap (a bare
// `new HeapOnNode(new DataTree(file))` cannot bootstrap a heap). HN blocks ~8176 B => ~2 max-size
// (3580 B) items per block; block index 8 is the first bitmap block.
public class HeapOnNodeAllocationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-hon-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;

    private HeapOnNode NewHeap()
    {
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();
        return HeapOnNode.CreateNewHeap(_file);
    }

    public void Dispose() { try { _file?.CloseFile(); } catch { } File.Delete(_path); }

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

    // Freed space in an existing block must be reused rather than always growing the heap.
    [Fact]
    public void Reuse_AfterRemove_DoesNotGrowHeap()
    {
        HeapOnNode heap = NewHeap();
        HeapID a = heap.AddItemToHeap(new byte[3580]);   // block 0
        heap.AddItemToHeap(new byte[3580]);              // block 0
        heap.AddItemToHeap(new byte[3580]);              // block 1 (max existing index = 1)
        heap.RemoveItemFromHeap(a);                      // frees ~3.5 KB in block 0
        HeapID reused = heap.AddItemToHeap(new byte[3000]);
        Assert.True(reused.hidBlockIndex <= 1, $"expected reuse of an existing block, got blk={reused.hidBlockIndex}");
    }

    // A block at the HID-index cap (MaximumHidIndex) must not be reused even though it has free space.
    [Fact]
    public void CountCap_FullBlock_NotReused_DespiteSpace()
    {
        HeapOnNode heap = NewHeap();
        HeapID first = heap.AddItemToHeap(new byte[1]);
        for (int i = 1; i < 2047; i++) heap.AddItemToHeap(new byte[1]);   // block 0 now at HID cap, space remains
        HeapID overflow = heap.AddItemToHeap(new byte[1]);
        Assert.Equal(0, first.hidBlockIndex);
        Assert.NotEqual(first.hidBlockIndex, overflow.hidBlockIndex);
    }
}
