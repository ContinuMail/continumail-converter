// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Parsing.Mime;
using Mail2Pst.Core.Parsing.Mbox;
using Mail2Pst.Core.Scanning;
using Mail2Pst.Core.Writing;
using MimeKit;

namespace Mail2Pst.Core.Parsing;

public class MboxParser : IMailSourceParser
{
    private static readonly byte[] FromMarker = Encoding.ASCII.GetBytes("From ");
    private const int BufferSize = 81920;

    private readonly MimeMessageMapper _mapper;
    private readonly long _rawSpillThreshold;

    public MboxParser(long tempFileThresholdBytes = 4L * 1024 * 1024, bool measureOnly = false,
                      long rawSpillThreshold = long.MaxValue)
    {
        if (tempFileThresholdBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(tempFileThresholdBytes), tempFileThresholdBytes, "Temp-file threshold must be non-negative.");
        _rawSpillThreshold = rawSpillThreshold;
        _mapper = new MimeMessageMapper(tempFileThresholdBytes, measureOnly);
    }

    public IEnumerable<ParseResult> Parse(string path, Action<long>? onBytesRead = null)
    {
        using FileStream stream = File.OpenRead(path);

        int index = 0;
        foreach (SpillableMessageBuffer raw in SplitMessages(stream, onBytesRead))
        {
            index++;
            var sourceRef = new SourceReference
            {
                SourcePath = path,
                Identifier = $"message #{index}",
            };

            MimeMessage? mime = null;
            string? error = null;
            try
            {
                using var s = raw.OpenRead();
                mime = ParseMimeMessage(s);
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                // Expected, per-message parse failures (malformed MIME / stream
                // error): record as a skip and continue. Any other exception is
                // an unexpected defect and is allowed to propagate so it surfaces
                // loudly instead of silently dropping mail.
                // RawMessageSpillException is NOT FormatException/IOException so it
                // propagates through the finally → fatal path, never swallowed here.
                error = ex.Message;
            }
            finally
            {
                // Delete temp file immediately after parse — before yielding the result.
                raw.Dispose();
            }

            if (error is not null)
            {
                yield return ParseResult.Failed(sourceRef, error);
                continue;
            }

            var warnings = new List<string>();
            MailMessage message = _mapper.Map(mime!, sourceRef, warnings);
            yield return ParseResult.Ok(message, warnings.Count > 0 ? warnings : null);
        }
    }

    /// <summary>
    /// Parses only the messages whose start ("From ") boundary falls in <c>[startOffset, endOffset)</c>
    /// and returns a structured per-message record for each. Used by the range-parallel scan: a file is
    /// split into message-aligned windows, each parsed independently, then merged. No "message #N"
    /// identifier is assigned here — that is rendered only at merge.
    ///
    /// <paramref name="startOffset"/> is always a real boundary offset (or 0); the stream seeks there
    /// and the shared boundary engine runs over the window (stopping at the first boundary &gt;=
    /// <paramref name="endOffset"/>). Measure-only + spill is used (this instance is the scan parser),
    /// so per-message data is derived IDENTICALLY to <see cref="Parse"/> / ScanRunner:
    /// <see cref="PstWriter.EstimateMessageSize"/>, <c>message.Date</c>, and the mapper warnings; a
    /// per-message <see cref="FormatException"/>/<see cref="IOException"/> becomes a skip
    /// (<see cref="RangeMessage.SkipReason"/>) and anything else (incl. spill) propagates.
    /// </summary>
    public RangeScanResult ScanRange(string path, long startOffset, long endOffset, Action<long>? onBytesRead)
    {
        using FileStream stream = File.OpenRead(path);
        stream.Seek(startOffset, SeekOrigin.Begin);

        var messages = new List<RangeMessage>();
        foreach (SpillableMessageBuffer raw in EnumerateMessageChunks(
                     stream, materialize: true, _rawSpillThreshold, onBytesRead,
                     onMessageStart: null, startAbsolute: startOffset, endOffset: endOffset)
                 .Select(b => b!))
        {
            // Same sourceRef shape as Parse, minus the rendered "message #N" identifier
            // (assigned only at merge in the range-merge step).
            var sourceRef = new SourceReference { SourcePath = path };

            MimeMessage? mime = null;
            string? error = null;
            try
            {
                using var s = raw.OpenRead();
                mime = ParseMimeMessage(s);
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                // Same per-message skip allowlist as Parse: malformed MIME / stream error.
                // RawMessageSpillException is neither, so it propagates → fatal (handled at merge).
                error = ex.Message;
            }
            finally
            {
                raw.Dispose();
            }

            if (error is not null)
            {
                messages.Add(new RangeMessage(0, null, error, Array.Empty<string>()));
                continue;
            }

            var warnings = new List<string>();
            MailMessage message = _mapper.Map(mime!, sourceRef, warnings);
            long estimatedBytes = PstWriter.EstimateMessageSize(message);
            DateTimeOffset? date = message.Date;

            // Mirror ScanRunner: release measured attachment content immediately.
            foreach (MailAttachment attachment in message.Attachments)
                attachment.Content.Dispose();

            messages.Add(new RangeMessage(
                estimatedBytes, date, null,
                warnings.Count > 0 ? warnings : Array.Empty<string>()));
        }

        return new RangeScanResult(startOffset, messages);
    }

    /// <summary>
    /// Parses one message's raw bytes (via stream) into a <see cref="MimeMessage"/>. Per
    /// MimeKit, throws <see cref="FormatException"/> for malformed MIME and
    /// <see cref="IOException"/> for stream errors; <see cref="Parse"/> treats
    /// only those as a per-message skip and lets anything else propagate.
    /// Virtual so tests can substitute the parse step.
    /// </summary>
    protected virtual MimeMessage ParseMimeMessage(Stream s)
    {
        var entityParser = new MimeParser(s, MimeFormat.Entity);
        return entityParser.ParseMessage();
    }

    public int CountMessages(string path)
    {
        using FileStream stream = File.OpenRead(path);
        // Same boundary engine as SplitMessages — for a given file state the count never
        // drifts from the messages Parse yields. (ConversionRunner reads the file twice:
        // count here, then Parse during conversion; external mutation of the file between
        // the two passes can still diverge — an accepted, out-of-scope limitation.)
        return EnumerateMessageChunks(stream, materialize: false, rawSpillThreshold: 0, onBytesRead: null, onMessageStart: null).Count();
    }

    /// <summary>
    /// Byte offset of each message's "From " boundary line, in the SAME order/count as <see cref="Parse"/>
    /// (shared boundary engine). Boundary-only: no MIME parsing. Used to align .msf live offsets to
    /// physical messages for uncompacted-copy filtering.
    /// </summary>
    public IReadOnlyList<long> ScanMessageStartOffsets(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var offsets = new List<long>();
        foreach (var _ in EnumerateMessageChunks(stream, materialize: false, rawSpillThreshold: 0, onBytesRead: null, onMessageStart: offsets.Add))
        { /* enumerate to drive the callback */ }
        return offsets;
    }

    /// <summary>
    /// Splits an mbox file into a <see cref="SpillableMessageBuffer"/> per message. Thin adapter over
    /// the shared <see cref="EnumerateMessageChunks"/> engine (materialize mode). See that
    /// method for the boundary rule and why we avoid MimeKit's <see cref="MimeFormat.Mbox"/>
    /// parser. The `!` is sound: materialize mode never yields a null chunk.
    /// </summary>
    private IEnumerable<SpillableMessageBuffer> SplitMessages(Stream rawStream, Action<long>? onBytesRead = null) =>
        EnumerateMessageChunks(rawStream, materialize: true, _rawSpillThreshold, onBytesRead, onMessageStart: null).Select(b => b!);

    /// <summary>
    /// THE single source of truth for "where do messages begin and end" in an mbox stream.
    /// Walks the stream once, line by line, using the shared <see cref="IsMessageBoundary"/>
    /// rule (a "From " line that is the first line, follows a blank line, or matches the
    /// envelope-postmark shape), and yields once per non-empty message. A message boundary
    /// line is mboxrd: in-body lines starting with "From " are stored escaped as ">From "
    /// and un-escaped here. The marker line itself is not part of the returned message.
    ///
    /// Return-value invariant keyed off <paramref name="materialize"/>:
    ///   - true  -> every yielded element is a NON-NULL SpillableMessageBuffer (caller owns + disposes).
    ///   - false -> yields a null placeholder per message (no per-message buffer allocated);
    ///              the caller only counts and never dereferences. Keeps counting cheap.
    /// <paramref name="onBytesRead"/> is invoked with the stream position at each yield
    /// (scan progress); pass null when counting.
    ///
    /// Windowing (for range-parallel scan, see <see cref="ScanRange"/>):
    /// <paramref name="startAbsolute"/> is the absolute file offset that the stream is already
    /// positioned at (the engine's local <c>consumed</c> is relative to it), so a boundary's
    /// absolute offset is <c>startAbsolute + lineStart</c>. When a boundary's absolute offset is
    /// &gt;= <paramref name="endOffset"/>, that boundary begins the NEXT window's first message:
    /// the engine yields the just-completed message (which is owned by this window) and stops —
    /// it does not begin/parse the out-of-window message. The defaults
    /// (<c>startAbsolute = 0</c>, <c>endOffset = long.MaxValue</c>) make whole-file callers
    /// (<see cref="SplitMessages"/>, <see cref="CountMessages"/>, <see cref="ScanMessageStartOffsets"/>)
    /// behave exactly as before.
    /// </summary>
    private static IEnumerable<SpillableMessageBuffer?> EnumerateMessageChunks(
        Stream rawStream, bool materialize, long rawSpillThreshold, Action<long>? onBytesRead,
        Action<long>? onMessageStart = null, long startAbsolute = 0, long endOffset = long.MaxValue)
    {
        var buffer = new byte[BufferSize];
        using var line = new MemoryStream(256);
        SpillableMessageBuffer? current = materialize ? new SpillableMessageBuffer(rawSpillThreshold) : null;
        bool previousLineWasBlank = true;
        bool currentHasContent = false;
        long consumed = 0;      // total bytes of completed lines (independent of rawStream.Position)
        long currentStart = 0;  // byte offset of the current message's From_ boundary line

        int bytesRead;
        while ((bytesRead = rawStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            int offset = 0;
            while (offset < bytesRead)
            {
                int newlineIndex = Array.IndexOf(buffer, (byte)'\n', offset, bytesRead - offset);
                if (newlineIndex == -1)
                {
                    // Partial line — carry it across to the next read.
                    line.Write(buffer, offset, bytesRead - offset);
                    break;
                }

                int lineLength = newlineIndex - offset + 1;
                line.Write(buffer, offset, lineLength);
                offset = newlineIndex + 1;

                // Process the fully-assembled line. Derive all span-based values (booleans
                // and any content writes) BEFORE clearing `line` or crossing a yield point —
                // ReadOnlySpan<byte> is a ref struct and cannot survive a yield boundary.
                int lineLen = (int)line.Length;
                bool isBoundary = IsMessageBoundary(line.GetBuffer().AsSpan(0, lineLen), previousLineWasBlank);
                bool isBlank    = IsBlankLine(line.GetBuffer().AsSpan(0, lineLen));
                if (!isBoundary && materialize)
                    WriteUnescapedFromLine(line.GetBuffer().AsSpan(0, lineLen), current!);

                long lineStart = consumed;  // byte offset of this line's first byte
                consumed += lineLen;        // advance AFTER capturing lineStart

                line.SetLength(0);

                if (isBoundary)
                {
                    if (currentHasContent)
                    {
                        onBytesRead?.Invoke(rawStream.Position);
                        onMessageStart?.Invoke(currentStart);
                        yield return materialize ? current : null;
                        if (materialize) current = new SpillableMessageBuffer(rawSpillThreshold);
                        currentHasContent = false;
                    }
                    // A boundary at or beyond endOffset begins the NEXT window's first message:
                    // the message just yielded above is the last one this window owns, so stop
                    // here without starting (or parsing) the out-of-window message.
                    if (startAbsolute + lineStart >= endOffset)
                        yield break;
                    currentStart = lineStart;  // this boundary begins the next message
                    // The marker line is not written into the message.
                }
                else
                {
                    currentHasContent = true;
                }

                previousLineWasBlank = isBlank;
            }
        }

        // Flush the final line if the file doesn't end with '\n'.
        if (line.Length > 0)
        {
            int finalLen = (int)line.Length;
            if (!IsMessageBoundary(line.GetBuffer().AsSpan(0, finalLen), previousLineWasBlank))
            {
                if (materialize) WriteUnescapedFromLine(line.GetBuffer().AsSpan(0, finalLen), current!);
                currentHasContent = true;
            }
            // A bare trailing boundary line yields nothing (no content) — matches splitting.
        }

        if (currentHasContent)
        {
            onBytesRead?.Invoke(rawStream.Position);
            onMessageStart?.Invoke(currentStart);
            yield return materialize ? current : null;
        }
    }

    private static bool StartsWithMarkerAt(ReadOnlySpan<byte> line, int index, ReadOnlySpan<byte> marker)
    {
        if (line.Length - index < marker.Length)
        {
            return false;
        }

        for (int i = 0; i < marker.Length; i++)
        {
            if (line[index + i] != marker[i])
            {
                return false;
            }
        }

        return true;
    }

    // mboxrd un-escaping: a body line that originally matched ^>*From  was stored with one
    // extra leading '>' to distinguish it from a real envelope boundary. Strip exactly one
    // '>' from any line of the form ^>+From ; write every other line unchanged. Writes
    // straight into the message buffer — no per-line allocation.
    private static void WriteUnescapedFromLine(ReadOnlySpan<byte> line, SpillableMessageBuffer destination)
    {
        int gt = 0;
        while (gt < line.Length && line[gt] == (byte)'>')
            gt++;

        if (gt == 0 || !StartsWithMarkerAt(line, gt, FromMarker))
        {
            destination.Write(line);          // not an escaped From-line — write as-is
            return;
        }

        destination.Write(line.Slice(1));     // drop exactly one leading '>'
    }

    private static bool StartsWithFromMarker(ReadOnlySpan<byte> line) =>
        StartsWithMarkerAt(line, 0, FromMarker);

    // A line is a message boundary when it starts with the "From " marker AND
    // either the previous line was blank (the common mbox case; previousLineWasBlank
    // is initialised true so the first line qualifies) OR the line itself matches
    // the envelope postmark shape (so messages with no blank separator still split,
    // without splitting on unescaped body lines that merely begin with "From ").
    private static bool IsMessageBoundary(ReadOnlySpan<byte> line, bool previousLineWasBlank) =>
        StartsWithFromMarker(line) && (previousLineWasBlank || MboxPostmark.IsEnvelopePostmark(line));

    private static bool IsBlankLine(ReadOnlySpan<byte> line)
    {
        foreach (byte b in line)
        {
            if (b != (byte)'\r' && b != (byte)'\n')
            {
                return false;
            }
        }

        return true;
    }
}
