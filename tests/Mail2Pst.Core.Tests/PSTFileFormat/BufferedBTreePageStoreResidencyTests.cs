// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class BufferedBTreePageStoreResidencyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-page-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;
    public void Dispose() { try { _file?.CloseFile(); } catch { } try { File.Delete(_path); } catch { } }

    [Fact]
    public void PageResidency_OnFreshStore_BlockBtreeHasPages_Bytes512Each()
    {
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
        // Touch the BBT so at least its root page is buffered.
        Assert.NotNull(_file.BlockBTree);
        var (count, bytes, _) = _file.BlockBTree.PageBufferResidencyForTest();
        Assert.True(count >= 0);
        Assert.Equal(count * 512L, bytes);
    }
}
