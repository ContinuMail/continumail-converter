// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;

namespace Mail2Pst.Core.Parsing.Mime;

/// <summary>Write-only <see cref="Stream"/> that counts bytes written and discards them. The scan
/// measure-only path decodes each attachment into this sink to get its exact decoded length without
/// retaining any bytes or spilling a temp file.</summary>
internal sealed class CountingStream : Stream
{
    public long BytesWritten { get; private set; }

    public override bool CanWrite => true;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override long Length => BytesWritten;
    public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count)); // validates args
    public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;
    public override void WriteByte(byte value) => BytesWritten += 1;
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
