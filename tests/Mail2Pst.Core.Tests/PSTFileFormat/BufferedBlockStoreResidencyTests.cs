// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class BufferedBlockStoreResidencyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-resid-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;

    private global::PSTFileFormat.PSTFile NewStore()
    {
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();
        return _file;
    }

    public void Dispose() { try { _file?.CloseFile(); } catch { } try { File.Delete(_path); } catch { } }

    [Fact]
    public void Residency_AfterPersist_CountsFullLeavesAsEvictable_TailAndRootExcluded()
    {
        var file = NewStore();
        var tree = new DataTree(file);
        byte[] payload = new byte[40_000];                       // ~5 leaves + XBlock spine
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 31 & 0xFF);
        tree.AppendData(payload);
        int count = tree.DataBlockCount;
        var fullLeaves = new System.Collections.Generic.List<ulong>();
        for (int i = 0; i < count - 1; i++) fullLeaves.Add(tree.GetDataBlock(i).BlockID.Value);
        tree.PersistLeafBlocks(fullLeaves);                       // full leaves -> BBT-indexed, not pending

        ulong rootBid = tree.RootBlock.BlockID.Value;
        var (bufCount, payloadBytes, pending, evictable) = tree.BlockBufferResidencyForTest(rootBid);

        Assert.True(bufCount >= count);                           // leaves + spine resident
        Assert.True(payloadBytes >= 40_000);                     // at least the content bytes
        Assert.True(evictable > 0);                              // the persisted full leaves
        Assert.True(pending > 0);                                // the partial tail + spine still pending
        // The spine (XBlock root) is never evictable even though it may be BBT-indexed later.
        Assert.True(evictable <= payloadBytes);
    }

    // Scenario A — falsifies the `!isPending` guard (and that a full leaf can be pending).
    // 16,352 B = 2 x 8176 -> two FULL data leaves under an XBlock spine. Nothing persisted,
    // so the full leaves are still pending: they must count as pending, NEVER evictable.
    [Fact]
    public void Residency_FullButPendingLeaves_CountedAsPending_NotEvictable()
    {
        var file = NewStore();
        var tree = new DataTree(file);
        byte[] payload = new byte[16_352];                       // exactly 2 full leaves
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 31 & 0xFF);
        tree.AppendData(payload);                                 // no PersistLeafBlocks / SaveChanges

        Assert.Equal(2, tree.DataBlockCount);                     // two leaves
        var (_, payloadBytes, pending, evictable) = tree.BlockBufferResidencyForTest(tree.RootBlock.BlockID.Value);

        Assert.True(payloadBytes >= 16_352);                     // both leaves resident
        Assert.True(pending > 0);                                // the full leaves are pending
        Assert.Equal(0, evictable);                              // pending => excluded from evictable
    }

    // Scenario B — falsifies the `!isRoot` guard. 8176 B -> a single FULL DataBlock that is
    // itself the root (no XBlock). After SaveChanges it is BBT-indexed and not pending, so it
    // satisfies isFullLeaf && bbtIndexed && !isPending — it is excluded SOLELY because it is the root.
    [Fact]
    public void Residency_FullBbtIndexedRoot_ExcludedByRootGuardOnly()
    {
        var file = NewStore();
        var tree = new DataTree(file);
        byte[] payload = new byte[8_176];                        // exactly one full leaf == the root
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 31 & 0xFF);
        tree.AppendData(payload);
        Assert.Equal(1, tree.DataBlockCount);                    // single-DataBlock root, no XBlock
        tree.SaveChanges();                                      // root -> BBT-indexed, not pending

        ulong rootBid = tree.RootBlock.BlockID.Value;

        var withRoot = tree.BlockBufferResidencyForTest(rootBid);
        Assert.Equal(0, withRoot.pending);                       // persisted -> nothing pending
        Assert.Equal(0, withRoot.evictable);                     // excluded solely by !isRoot

        // Sanity: with no root context the SAME block IS counted evictable, proving the only
        // disqualifier above was the root guard (it is a full, BBT-indexed, non-pending DataBlock).
        var noRoot = tree.BlockBufferResidencyForTest(null);
        Assert.True(noRoot.evictable > 0);
    }
}
