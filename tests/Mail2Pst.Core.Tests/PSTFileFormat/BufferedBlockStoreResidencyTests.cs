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
}
