// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class HeapOnNodeResidencyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-hon-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;
    public void Dispose() { try { _file?.CloseFile(); } catch { } try { File.Delete(_path); } catch { } }

    [Fact]
    public void HeapResidency_AfterAllocations_ReportsDecodedCacheAndIndexCounts()
    {
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();
        var heap = HeapOnNode.CreateNewHeap(_file);
        heap.AddItemToHeap(new byte[100]);
        heap.AddItemToHeap(new byte[200]);

        var (bufferCount, decodedBytes, freeIdx, blockAvail) = heap.HeapResidencyForTest();
        Assert.True(bufferCount >= 1);
        Assert.True(decodedBytes >= 300);
        Assert.True(freeIdx >= 0);
        Assert.True(blockAvail >= 0);
    }
}
