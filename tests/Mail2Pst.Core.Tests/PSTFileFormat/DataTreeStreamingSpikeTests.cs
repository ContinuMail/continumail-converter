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
}
