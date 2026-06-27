// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Parsing;

namespace Mail2Pst.Core.Parsing.Mbox;

/// <summary>Accumulates one raw message's bytes, keeping it in memory until it exceeds a threshold, then
/// spilling to a temp file (so per-message peak RAM is bounded by the threshold). <see cref="OpenRead"/>
/// returns a FRESH, caller-owned read stream; <see cref="Dispose"/> deletes the temp file. Temp create/write/read
/// failures throw <see cref="RawMessageSpillException"/> (fatal, never a skip).</summary>
internal sealed class SpillableMessageBuffer : IDisposable
{
    private readonly long _threshold;
    private MemoryStream? _memory = new();
    private FileStream? _writeFile;   // write handle while spilling; flushed+closed on OpenRead/Dispose
    private string? _tempPath;
    private bool _disposed;

    public SpillableMessageBuffer(long thresholdBytes) => _threshold = thresholdBytes;

    public bool SpilledToDisk => _tempPath is not null;
    internal string? TempPathForTest => _tempPath;
    public long Length => _writeFile?.Length ?? _memory!.Length;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (_tempPath is null && _memory!.Length + bytes.Length > _threshold)
            SpillNow();
        if (_writeFile is not null)
        {
            try { _writeFile.Write(bytes); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new RawMessageSpillException("Failed to write the raw-message spill temp file.", ex); }
        }
        else _memory!.Write(bytes);
    }

    private void SpillNow()
    {
        try
        {
            _tempPath = Path.Combine(Path.GetTempPath(), $"mail2pst-raw-{Guid.NewGuid()}");
            _writeFile = new FileStream(_tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            _memory!.Position = 0;
            _memory.CopyTo(_writeFile);
            _memory.Dispose();
            _memory = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new RawMessageSpillException("Failed to create the raw-message spill temp file.", ex); }
    }

    /// <summary>Returns a FRESH, read-only, seek-0 stream the caller OWNS and disposes. For a spilled message
    /// this flushes+closes the write handle and opens a new read-only FileStream over the temp.</summary>
    public Stream OpenRead()
    {
        if (_tempPath is not null)
        {
            if (_writeFile is not null) { _writeFile.Flush(); _writeFile.Dispose(); _writeFile = null; }
            try { return new FileStream(_tempPath, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new RawMessageSpillException("Failed to read the raw-message spill temp file.", ex); }
        }
        return new MemoryStream(_memory!.GetBuffer(), 0, (int)_memory.Length, writable: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _memory?.Dispose();
        _writeFile?.Dispose();
        if (_tempPath is not null)
            try { File.Delete(_tempPath); } catch { /* best-effort: delete failure is a warning, not fatal */ }
    }
}
