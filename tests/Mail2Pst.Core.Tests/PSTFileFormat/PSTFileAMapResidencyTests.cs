// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class PSTFileAMapResidencyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-amap-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;
    public void Dispose() { try { _file?.CloseFile(); } catch { } try { File.Delete(_path); } catch { } }

    [Fact]
    public void AMapResidency_OnFreshStore_NonNegativeAndConsistent()
    {
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();
        var tree = new DataTree(_file);
        tree.AppendData(new byte[20_000]);                       // force at least one allocation -> AMap touched
        tree.SaveChanges();

        var (pageCount, bytes, g, a) = _file.AMapResidencyForTest();
        Assert.True(pageCount >= 0);
        Assert.Equal(pageCount * 512L, bytes);
        Assert.True(g >= 0 && a >= 0);
    }
}
