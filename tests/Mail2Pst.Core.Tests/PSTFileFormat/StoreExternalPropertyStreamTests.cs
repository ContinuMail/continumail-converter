// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Threading;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

// Fail-closed round-trip gate for the streaming INSERT-only StoreExternalProperty overload (Task 3).
// Exercises all four routing zones: heap (≤3580), subnode DataBlock (>3580,≤8176), XBlock, XXBlock.
// [L1] No hedging on the ≤3580 case — GetExternalPropertyBytes routes both heap and subnode through
// the same read API, so byte-equality at ≤3580 proves ReadExactly correctness.
public class StoreExternalPropertyStreamTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-sxp-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;

    public void Dispose()
    {
        try { _file?.CloseFile(); } catch { }
        try { File.Delete(_path); } catch { }
    }

    // Deterministic position-dependent bytes so a byte-displaced or zero-filled block fails the assertion.
    private static byte[] Make(int n)
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++) b[i] = (byte)((i * 31 + 7) & 0xFF);
        return b;
    }

    [Theory]
    [InlineData(2000)]       // heap path (≤3580)
    [InlineData(5000)]       // subnode DataBlock (>3580, ≤8176)
    [InlineData(20000)]      // XBlock (>8176)
    [InlineData(9_000_000)]  // XXBlock (>~7.96 MB)
    public void StoreViaStream_RoundTrips_AcrossRoutingBoundaries(int size)
    {
        byte[] payload = Make(size);

        // --- write phase ---
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();

        HeapOnNode heap = HeapOnNode.CreateNewHeap(_file);
        SubnodeBTree subnodeBTree = null!;   // INSERT path: may be created by the overload for >3580 B

        using var ms = new MemoryStream(payload, writable: false);
        HeapOrNodeID id = NodeStorageHelper.StoreExternalProperty(
            _file, heap, ref subnodeBTree, ms, (long)payload.Length, CancellationToken.None);

        // --- read-back in-window via GetExternalPropertyBytes ---
        // Routes both heap (IsHeapID → heap.GetHeapItem) and subnode (DataTree.GetData()) uniformly.
        // AppendData(Stream,...) ends with its own SaveChanges [R2:C1], so subnode data is BBT-indexed
        // and readable in-window before EndSavingChanges.
        byte[] readBack = NodeStorageHelper.GetExternalPropertyBytes(heap, subnodeBTree!, id);

        // [L1] Exact byte-equality at ALL FOUR sizes — no hedge. A failure here is a §8 corruption signal.
        Assert.Equal(payload, readBack);
    }
}
