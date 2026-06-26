// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;

namespace Mail2Pst.Core.Models;

public sealed class AttachmentContent : IDisposable
{
    private readonly byte[]? _bytes;
    private readonly string? _tempPath;
    private bool _disposed;

    public long Length { get; }

    internal bool IsTempFileBacked => _tempPath is not null;
    internal string? TempPath => _tempPath;

    private AttachmentContent(byte[]? bytes, string? tempPath, long length)
    {
        _bytes = bytes;
        _tempPath = tempPath;
        Length = length;
    }

    public static AttachmentContent FromBytes(byte[] bytes) =>
        new(bytes, null, bytes.Length);

    public static AttachmentContent FromTempFile(string path, long length) =>
        new(null, path, length);

    // Materializes the whole attachment in memory (in-memory bytes, or the entire temp
    // file). The temp-file path bounds the parse/write queue's sustained memory, not peak —
    // see MimeMessageMapper.ToAttachmentContent.
    public byte[] ReadAllBytes()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AttachmentContent));
        return _bytes ?? File.ReadAllBytes(_tempPath!);
    }

    /// <summary>
    /// Opens a readable, seekable stream over the content WITHOUT materializing it: a MemoryStream
    /// wrapping the existing byte[] (no copy) for in-memory content, or a FileStream over the temp file
    /// (no read). The caller owns + disposes the returned stream. Enables streaming attachment writes
    /// so a large attachment is never copied into a writer-side byte[].
    /// </summary>
    public Stream OpenRead()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AttachmentContent));
        if (_bytes is not null)
        {
            return new MemoryStream(_bytes, writable: false);
        }
        return new FileStream(_tempPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_tempPath is not null)
        {
            try { File.Delete(_tempPath); } catch (Exception) { }
        }
    }
}
