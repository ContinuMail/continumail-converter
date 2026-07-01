// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Models;

public class AttachmentContentTests
{
    [Fact]
    public void FromBytes_ReadAllBytes_ReturnsOriginalBytes()
    {
        byte[] data = [1, 2, 3, 4, 5];
        using var content = AttachmentContent.FromBytes(data);

        Assert.Equal(5, content.Length);
        Assert.Equal(data, content.ReadAllBytes());
    }

    [Fact]
    public void FromBytes_IsNotTempFileBacked()
    {
        using var content = AttachmentContent.FromBytes([1, 2, 3]);
        Assert.False(content.IsTempFileBacked);
        Assert.Null(content.TempPath);
    }

    [Fact]
    public void FromBytes_Dispose_IsNoOp()
    {
        using var content = AttachmentContent.FromBytes([1, 2, 3]);
        content.Dispose(); // must not throw
    }

    [Fact]
    public void FromBytes_ReadAllBytes_AfterDispose_ThrowsObjectDisposedException()
    {
        var content = AttachmentContent.FromBytes([1, 2, 3]);
        content.Dispose();
        Assert.Throws<ObjectDisposedException>((Action)(() => content.ReadAllBytes()));
    }

    [Fact]
    public void FromTempFile_ReadAllBytes_ReturnsFileContents()
    {
        byte[] data = [10, 20, 30];
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);

        using var content = AttachmentContent.FromTempFile(path, data.Length);

        Assert.Equal(3, content.Length);
        Assert.Equal(data, content.ReadAllBytes());
        Assert.True(content.IsTempFileBacked);
        Assert.Equal(path, content.TempPath);
    }

    [Fact]
    public void FromTempFile_Dispose_DeletesTempFile()
    {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, [1, 2, 3]);

        try
        {
            var content = AttachmentContent.FromTempFile(path, 3);
            Assert.True(File.Exists(path));
            content.Dispose();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FromTempFile_DoubleDispose_DoesNotThrow()
    {
        string path = Path.GetTempFileName();
        try
        {
            var content = AttachmentContent.FromTempFile(path, 0);
            content.Dispose();
            content.Dispose(); // must not throw
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FromTempFile_ReadAllBytes_AfterDispose_ThrowsObjectDisposedException()
    {
        string path = Path.GetTempFileName();
        try
        {
            var content = AttachmentContent.FromTempFile(path, 0);
            content.Dispose();
            Assert.Throws<ObjectDisposedException>((Action)(() => content.ReadAllBytes()));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OpenRead_BytesBacked_StreamsExactContent_LengthMatches()
    {
        byte[] data = { 1, 2, 3, 4, 5 };
        using var c = AttachmentContent.FromBytes(data);
        using Stream s = c.OpenRead();
        Assert.Equal(data.Length, c.Length);
        var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal(data, ms.ToArray());
    }

    [Fact]
    public void OpenRead_TempFileBacked_StreamsFileContent_WithoutPreReading()
    {
        string path = Path.Combine(Path.GetTempPath(), "m2p-att-" + System.Guid.NewGuid().ToString("N"));
        byte[] data = { 9, 8, 7, 6 };
        File.WriteAllBytes(path, data);
        try
        {
            using var c = AttachmentContent.FromTempFile(path, data.Length);
            using Stream s = c.OpenRead();
            var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(data, ms.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FromExistingFile_does_not_delete_source_on_dispose()
    {
        string f = Path.Combine(Path.GetTempPath(), $"src-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(f, new byte[] { 7 });
        try
        {
            using (var c = AttachmentContent.FromExistingFile(f)) { Assert.Equal(1, c.Length); }
            Assert.True(File.Exists(f));
        }
        finally { File.Delete(f); }
    }
}

public class AttachmentContentLengthOnlyTests
{
    [Fact]
    public void FromLengthOnly_ExposesLength_ButByteAccessThrows()
    {
        var c = AttachmentContent.FromLengthOnly(4096);
        Assert.Equal(4096, c.Length);
        Assert.Throws<InvalidOperationException>(() => c.OpenRead());
        Assert.Throws<InvalidOperationException>(() => c.ReadAllBytes());
        c.Dispose(); // no temp file, no throw
    }

    [Fact]
    public void FromLengthOnly_NegativeLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AttachmentContent.FromLengthOnly(-1));
    }
}
