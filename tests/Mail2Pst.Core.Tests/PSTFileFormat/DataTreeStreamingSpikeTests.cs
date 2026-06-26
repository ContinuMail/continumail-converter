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
        Assert.False(tree.TryEvictLeaf(spine, _ => true), "spine (XBlock) is not a leaf DataBlock");
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
}
