// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

// Gating spike for Phase-2 attachment streaming (spec §8). Fail-closed: any tripped assertion
// means Target A needs a different design. Operates at the DataTree level inside one
// BeginSavingChanges() window; the round-trip test (Task 4) closes the loop via reopen.
public class DataTreeStreamingSpikeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-spike-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;

    private global::PSTFileFormat.PSTFile NewStore()
    {
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();
        return _file;
    }

    // Deterministic, position-dependent bytes so a misplaced block fails the round-trip.
    internal static byte[] MakeBytes(int length, int seed)
    {
        byte[] b = new byte[length];
        for (int i = 0; i < length; i++) b[i] = (byte)((i * 31 + seed) & 0xFF);
        return b;
    }

    public void Dispose() { try { _file?.CloseFile(); } catch { } try { File.Delete(_path); } catch { } }

    [Fact]
    public void IncrementalPersist_LeafIsBbtIndexedAndReadable_BeforeTransactionCommit()
    {
        var file = NewStore();
        var tree = new DataTree(file);
        // >8176 B forces DataBlock -> XBlock (at least two leaves + a spine).
        byte[] payload = MakeBytes(20_000, 1);
        tree.AppendData(payload);

        ulong leaf0 = tree.GetDataBlock(0).BlockID.Value;

        // Persist within the window (existing primitive); BBT entry is inserted in-memory.
        tree.SaveChanges();

        Assert.NotNull(file.FindBlockEntryByBlockID(leaf0));     // BBT-indexed in-memory
        Assert.Equal(payload, tree.GetData());                  // re-readable, no EndSavingChanges yet
    }

    [Fact]
    public void StreamingFlush_KeepsSpinePending_NoReallocationAcrossBatches()
    {
        var file = NewStore();
        var tree = new DataTree(file);

        // Batch 1: append > 8176 B so an XBlock spine exists with several leaves.
        tree.AppendData(MakeBytes(40_000, 7));        // ~5 leaves + XBlock root
        int count = tree.DataBlockCount;
        ulong spineBidBefore = tree.RootBlock.BlockID.Value;

        // Flush the full leaves (0 .. count-2); the partial tail (count-1) stays pending.
        var fullLeaves = Enumerable.Range(0, count - 1)
                                   .Select(i => tree.GetDataBlock(i).BlockID.Value)
                                   .ToList();
        tree.PersistLeafBlocks(fullLeaves);

        Assert.True(tree.IsRootPendingWriteForTest(), "spine must remain pending after a leaf flush");

        // Batch 2: more appends update the spine IN PLACE (pending) -> BID unchanged.
        tree.AppendData(MakeBytes(40_000, 9));
        Assert.Equal(spineBidBefore, tree.RootBlock.BlockID.Value);
    }

    [Fact]
    public void NaiveSaveChanges_ReallocatesSpine_DemonstratingWhyFlushIsMandatory()  // [A2] fail-closed contrast
    {
        var file = NewStore();
        var tree = new DataTree(file);
        tree.AppendData(MakeBytes(40_000, 7));
        ulong spineBidBefore = tree.RootBlock.BlockID.Value;

        tree.SaveChanges();                            // clears m_blocksToWrite -> spine no longer pending
        tree.AppendData(MakeBytes(40_000, 9));         // spine update now allocates a NEW BID

        Assert.NotEqual(spineBidBefore, tree.RootBlock.BlockID.Value);
    }

    [Fact]
    public void TryEvictLeaf_RemovesPersistedLeafFromBuffer_PendingAndSpineSurvive()
    {
        var file = NewStore();
        var tree = new DataTree(file);
        tree.AppendData(MakeBytes(40_000, 3));
        int count = tree.DataBlockCount;

        ulong leaf0 = tree.GetDataBlock(0).BlockID.Value;
        ulong tailLeaf = tree.GetDataBlock(count - 1).BlockID.Value;
        ulong spine = tree.RootBlock.BlockID.Value;

        tree.PersistLeafBlocks(Enumerable.Range(0, count - 1).Select(i => tree.GetDataBlock(i).BlockID.Value).ToList());
        int bufBefore = tree.BufferedBlockCountForTest;

        bool evicted = tree.TryEvictLeaf(leaf0, _ => true);   // full + persisted + leaf
        Assert.True(evicted);
        Assert.Equal(bufBefore - 1, tree.BufferedBlockCountForTest);

        Assert.False(tree.TryEvictLeaf(tailLeaf, _ => true), "partial pending tail must not be evictable");

        tree.SaveChanges();   // persist the spine to the BBT + clear it from m_blocksToWrite so the next check reaches cond 4/6
        Assert.False(tree.TryEvictLeaf(spine, _ => true), "spine is an XBlock, not a leaf DataBlock — fails cond 4/6");
    }

    [Fact]
    public void ReadDataLeafWithoutCaching_ReturnsBytes_WithoutRepopulatingBuffer()  // [A12]
    {
        var file = NewStore();
        var tree = new DataTree(file);
        byte[] payload = MakeBytes(40_000, 5);
        tree.AppendData(payload);
        int count = tree.DataBlockCount;

        ulong leaf0 = tree.GetDataBlock(0).BlockID.Value;
        byte[] leaf0Bytes = tree.GetDataBlock(0).Data;       // capture expected leaf bytes
        tree.PersistLeafBlocks(Enumerable.Range(0, count - 1).Select(i => tree.GetDataBlock(i).BlockID.Value).ToList());
        tree.TryEvictLeaf(leaf0, _ => true);
        int bufAfterEvict = tree.BufferedBlockCountForTest;

        byte[] readBack = tree.ReadDataLeafWithoutCaching(leaf0);
        Assert.Equal(leaf0Bytes, readBack);
        Assert.Equal(bufAfterEvict, tree.BufferedBlockCountForTest);   // NOT re-cached
    }

    [Theory]
    [InlineData(20_000)]        // DataBlock -> XBlock
    [InlineData(8_347_696)]     // exactly 1021 * 8176 — boundary at the XBlock -> XXBlock transition [R3:NEW-02]
    [InlineData(9_000_000)]     // XXBlock, non-8176-aligned tail
    public void StreamAppend_RoundTrips_AcrossSpineTransitions(int length)
    {
        byte[] payload = MakeBytes(length, 42);
        ulong rootBid;

        // --- write phase (streaming) ---
        {
            var file = NewStore();
            var tree = new DataTree(file);
            using (var src = new MemoryStream(payload, writable: false))
            {
                tree.AppendData(src, payload.Length);
            }
            Assert.Equal(0, tree.PendingWriteCountForTest);    // [C1] spine + all leaves flushed, nothing pending [R3]
            rootBid = tree.RootBlock.BlockID.Value;
            file.EndSavingChanges();
            file.CloseFile();
            _file = null;
        }

        // --- read phase (reopen) --- PSTFile has no IDisposable; close via try/finally [R3:F2]
        var file2 = new global::PSTFileFormat.PSTFile(_path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
        try
        {
            Block root = file2.FindBlockByBlockID(rootBid);
            var tree2 = new DataTree(file2, root);
            Assert.Equal(payload, tree2.GetData());
        }
        finally
        {
            file2.CloseFile();
        }
    }

    [Fact]
    public void StreamAppend_KeepsBufferBounded_IndependentOfAttachmentSize()  // peak-memory intent
    {
        var file = NewStore();
        var tree = new DataTree(file);
        using (var src = new MemoryStream(MakeBytes(9_000_000, 1), writable: false))
        {
            tree.AppendData(src, 9_000_000);
        }
        // ~1100 leaves written; after AppendData's final drain only tail + preceding + spine (~5) remain
        // resident, independent of attachment size. (Mid-stream PEAK is ~one 8 MB batch of leaves — the
        // bounded "+ batch" term in spec §1; this end-state check proves the drain reclaims it.) [R3:F1]
        Assert.True(tree.BufferedBlockCountForTest < 16,
            $"buffer not bounded after drain: {tree.BufferedBlockCountForTest} resident blocks");
    }

    [Fact]
    public void Streaming_DoesNotReallocateSpine_AcrossManyBatches()  // [A2]
    {
        var file = NewStore();
        var tree = new DataTree(file);
        long before = tree.BlockReallocationsForTest;

        using (var src = new MemoryStream(MakeBytes(9_000_000, 13), writable: false))
        {
            tree.AppendData(src, 9_000_000);            // many 8 MB-batch boundaries
        }
        long spineReallocs = tree.BlockReallocationsForTest - before;

        // In-place spine updates only. A small constant (initial DataBlock->XBlock->XXBlock promotions)
        // is fine; growth proportional to batch count is the failure mode.
        Assert.True(spineReallocs < 16, $"spine reallocated {spineReallocs} times — superlinear churn");
    }

    [Fact]
    public void EveryPersistedInteriorLeaf_IsFull8176_NoPartialInteriorBlock()  // [R2:M4] — round-trip alone is vacuous
    {
        var file = NewStore();
        var tree = new DataTree(file);
        using (var src = new MemoryStream(MakeBytes(9_000_000, 11), writable: false))
        {
            tree.AppendData(src, 9_000_000);
        }
        // Read interior leaf BIDs from the in-memory spine WITHOUT GetDataBlock (which re-caches evicted
        // leaves via GetBlock, violating [A12]); assert each BBT-recorded length == 8176. [R3:F3]
        var interiorBids = InteriorLeafBids(file, tree);
        Assert.NotEmpty(interiorBids);
        foreach (ulong bid in interiorBids)
        {
            var entry = file.FindBlockEntryByBlockID(bid);
            Assert.NotNull(entry);
            Assert.Equal(DataBlock.MaximumDataLength, (int)entry.cb);   // cb is ushort (BlockBTreeEntry.cs:19) [R3]
        }
    }

    [Fact]
    public void StreamAppend_CancelledAtBatchBoundary_ThrowsOperationCanceled()
    {
        var file = NewStore();
        var tree = new DataTree(file);
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();   // already cancelled -> must throw at/just before the first batch boundary
        using var src = new System.IO.MemoryStream(MakeBytes(9_000_000, 5), writable: false);
        Assert.Throws<System.OperationCanceledException>(() => tree.AppendData(src, 9_000_000, cts.Token));
    }

    // Every leaf BID except the final (tail) leaf, read from the live spine without re-caching. [R3:F3]
    private static System.Collections.Generic.List<ulong> InteriorLeafBids(global::PSTFileFormat.PSTFile file, DataTree tree)
    {
        var bids = new System.Collections.Generic.List<ulong>();
        Block root = tree.RootBlock;
        if (root is XBlock xb)
        {
            foreach (BlockID b in xb.rgbid) bids.Add(b.Value);
        }
        else if (root is XXBlock xxb)
        {
            foreach (BlockID xbid in xxb.rgbid)
            {
                var child = (XBlock)file.FindBlockByBlockID(xbid.Value);   // non-caching (PSTFile level)
                foreach (BlockID b in child.rgbid) bids.Add(b.Value);
            }
        }
        if (bids.Count > 0) bids.RemoveAt(bids.Count - 1);   // drop the tail (allowed to be partial)
        return bids;
    }
}
