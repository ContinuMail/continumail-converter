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

    /// <summary>Largest a single message may be before it is skipped. 1900 MiB — safely under
    /// Array.MaxLength (~2 GiB) with headroom for MemoryStream capacity-doubling, so neither the
    /// per-line MemoryStream nor the per-message buffer can reach the ~2 GiB overflow point.</summary>
    public const long DefaultMaxMessageBytes = 1900L * 1024 * 1024;

    /// <summary>Convert-path raw-message spill threshold (64 MiB): a message larger than this spills to
    /// a temp file during conversion so parse-side peak RAM stays bounded. Used by the convert parser in
    /// <see cref="ParserRegistry"/>; scan uses its own tighter 4 MiB threshold.</summary>
    public const long DefaultRawSpillThreshold = 64L * 1024 * 1024;

    private readonly long _maxMessageBytes;

    public MboxParser(long tempFileThresholdBytes = 4L * 1024 * 1024, bool measureOnly = false,
                      long rawSpillThreshold = long.MaxValue, long maxMessageBytes = DefaultMaxMessageBytes)
    {
        if (tempFileThresholdBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(tempFileThresholdBytes), tempFileThresholdBytes, "Temp-file threshold must be non-negative.");
        if (maxMessageBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessageBytes), maxMessageBytes, "Max message size must be positive.");
        _rawSpillThreshold = rawSpillThreshold;
        _maxMessageBytes = maxMessageBytes;
        _mapper = new MimeMessageMapper(tempFileThresholdBytes, measureOnly);
    }

    public IEnumerable<ParseResult> Parse(string path, Action<long>? onBytesRead = null)
    {
        using FileStream stream = File.OpenRead(path);

        int index = 0;
        foreach (MessageChunk chunk in EnumerateMessageChunks(
                     rawStream: stream, materialize: true, rawSpillThreshold: _rawSpillThreshold,
                     maxMessageBytes: _maxMessageBytes, onBytesRead: onBytesRead, onMessageStart: null))
        {
            index++;
            var sourceRef = new SourceReference
            {
                SourcePath = path,
                Identifier = $"message #{index}",
            };

            if (chunk.IsOversized)
            {
                yield return ParseResult.Failed(sourceRef,
                    $"message exceeds the maximum size of {_maxMessageBytes} bytes and was skipped " +
                    $"(read ~{chunk.OversizedBytes} bytes)");
                continue;
            }

            SpillableMessageBuffer raw = chunk.Buffer!;   // materialize mode never yields a null buffer (except oversized, handled above)

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
    public virtual RangeScanResult ScanRange(string path, long startOffset, long endOffset, Action<long>? onBytesRead)
    {
        using FileStream stream = File.OpenRead(path);
        stream.Seek(startOffset, SeekOrigin.Begin);

        var messages = new List<RangeMessage>();
        foreach (MessageChunk chunk in EnumerateMessageChunks(
                     rawStream: stream, materialize: true, rawSpillThreshold: _rawSpillThreshold,
                     maxMessageBytes: _maxMessageBytes, onBytesRead: onBytesRead, onMessageStart: null,
                     startAbsolute: startOffset, endOffset: endOffset))
        {
            // Same sourceRef shape as Parse, minus the rendered "message #N" identifier
            // (assigned only at merge in the range-merge step).
            var sourceRef = new SourceReference { SourcePath = path };

            if (chunk.IsOversized)
            {
                messages.Add(new RangeMessage(0, null,
                    $"message exceeds the maximum size of {_maxMessageBytes} bytes and was skipped " +
                    $"(read ~{chunk.OversizedBytes} bytes)",
                    Array.Empty<string>()));
                continue;
            }

            SpillableMessageBuffer raw = chunk.Buffer!;

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
        // Same boundary engine as Parse — for a given file state the count never
        // drifts from the messages Parse yields. (ConversionRunner reads the file twice:
        // count here, then Parse during conversion; external mutation of the file between
        // the two passes can still diverge — an accepted, out-of-scope limitation.)
        return EnumerateMessageChunks(rawStream: stream, materialize: false, rawSpillThreshold: 0,
            maxMessageBytes: _maxMessageBytes, onBytesRead: null, onMessageStart: null).Count();
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
        foreach (var _ in EnumerateMessageChunks(rawStream: stream, materialize: false, rawSpillThreshold: 0,
                     maxMessageBytes: _maxMessageBytes, onBytesRead: null, onMessageStart: offsets.Add))
        { /* enumerate to drive the callback */ }
        return offsets;
    }

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
    /// (<see cref="Parse"/>, <see cref="CountMessages"/>, <see cref="ScanMessageStartOffsets"/>)
    /// behave exactly as before.
    /// </summary>
    private static IEnumerable<MessageChunk> EnumerateMessageChunks(
        Stream rawStream, bool materialize, long rawSpillThreshold, long maxMessageBytes,
        Action<long>? onBytesRead, Action<long>? onMessageStart = null,
        long startAbsolute = 0, long endOffset = long.MaxValue)
    {
        var buffer = new byte[BufferSize];
        using var line = new MemoryStream(256);
        SpillableMessageBuffer? current = materialize ? new SpillableMessageBuffer(rawSpillThreshold) : null;
        bool previousLineWasBlank = true;
        bool currentHasContent = false;
        long consumed = 0;       // logical byte offset of line starts (drives boundary offsets)
        long currentStart = 0;   // byte offset of the current message's From_ boundary line
        long messageBytes = 0;   // content bytes accumulated for the current message (for the cap)
        bool currentOversized = false;
        bool skipToNewline = false;   // draining the tail of a single over-cap line

        int bytesRead;
        while ((bytesRead = rawStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            int offset = 0;
            while (offset < bytesRead)
            {
                if (skipToNewline)
                {
                    // Discard bytes (advancing `consumed`) until this over-cap line's newline.
                    int nlIdx = Array.IndexOf(buffer, (byte)'\n', offset, bytesRead - offset);
                    int take = (nlIdx == -1 ? bytesRead : nlIdx + 1) - offset;
                    consumed += take;
                    messageBytes += take;        // drained bytes still count toward OversizedBytes (report only)
                    offset += take;
                    if (nlIdx == -1)
                        break;                        // whole remaining buffer was mid-line; read more
                    skipToNewline = false;
                    previousLineWasBlank = false;     // an over-cap line is never a blank line
                    line.SetLength(0);
                    continue;
                }

                int newlineIndex = Array.IndexOf(buffer, (byte)'\n', offset, bytesRead - offset);
                if (newlineIndex == -1)
                {
                    int chunk = bytesRead - offset;
                    // Per-line cap: a single line whose accumulation would exceed the cap makes the
                    // message oversized; account the bytes already in `line`, drop them, and drain the
                    // rest of the line rather than growing `line` toward the ~2 GiB MemoryStream limit.
                    if (!currentOversized && messageBytes + line.Length + chunk > maxMessageBytes)
                    {
                        MarkOversized(ref currentOversized, ref currentHasContent, ref current);
                        messageBytes += line.Length;  // count the accumulated partial toward OversizedBytes
                        consumed += line.Length;      // partial bytes already accumulated for this line
                        line.SetLength(0);
                        skipToNewline = true;
                        continue;                     // re-enter into the skip branch (offset unchanged)
                    }
                    line.Write(buffer, offset, chunk);
                    break;                            // partial line — carry to next read
                }

                int lineLength = newlineIndex - offset + 1;

                // Completed-line cap check BEFORE writing into the per-line buffer, so `line` never grows
                // past the cap even when the segment that crosses the cap also contains the newline (the
                // partial-line branch above handles the no-newline-in-this-buffer case). This keeps
                // `line.Length <= maxMessageBytes` for ANY cap value — the default 1900 MiB already has
                // headroom below Array.MaxLength for one 80 KiB segment, but a caller may set a larger cap.
                if (!currentOversized && messageBytes + line.Length + lineLength > maxMessageBytes)
                {
                    long fullLine = line.Length + lineLength;   // accumulated partial + this final segment
                    MarkOversized(ref currentOversized, ref currentHasContent, ref current);
                    messageBytes += fullLine;                   // honest OversizedBytes
                    consumed += fullLine;                       // the whole line's bytes, counted once
                    line.SetLength(0);
                    offset = newlineIndex + 1;
                    previousLineWasBlank = false;               // an over-cap line is content, never a boundary/blank
                    continue;
                }

                line.Write(buffer, offset, lineLength);
                offset = newlineIndex + 1;

                int lineLen = (int)line.Length;
                bool isBoundary = IsMessageBoundary(line.GetBuffer().AsSpan(0, lineLen), previousLineWasBlank);
                bool isBlank    = IsBlankLine(line.GetBuffer().AsSpan(0, lineLen));

                if (!isBoundary)
                {
                    // Under the cap here (enforced above). The content write is still gated on
                    // !currentOversized so a message already made oversized by an EARLIER line keeps
                    // parsing lines for boundary detection without buffering any more content.
                    if (!currentOversized && materialize)
                        WriteUnescapedFromLine(line.GetBuffer().AsSpan(0, lineLen), current!);
                    messageBytes += lineLen;
                }

                long lineStart = consumed;
                consumed += lineLen;
                line.SetLength(0);

                if (isBoundary)
                {
                    if (currentHasContent)
                    {
                        onBytesRead?.Invoke(rawStream.Position);
                        onMessageStart?.Invoke(currentStart);
                        yield return currentOversized
                            ? MessageChunk.Oversized(messageBytes)
                            : MessageChunk.Ok(materialize ? current : null);
                        current = materialize ? new SpillableMessageBuffer(rawSpillThreshold) : null;
                        currentHasContent = false;
                        messageBytes = 0;
                        currentOversized = false;
                    }
                    if (startAbsolute + lineStart >= endOffset)
                        yield break;
                    currentStart = lineStart;
                }
                else
                {
                    currentHasContent = true;
                }

                previousLineWasBlank = isBlank;
            }
        }

        // Flush a final line with no trailing '\n' (unless we're still draining an over-cap line).
        if (line.Length > 0 && !skipToNewline)
        {
            int finalLen = (int)line.Length;
            if (!IsMessageBoundary(line.GetBuffer().AsSpan(0, finalLen), previousLineWasBlank))
            {
                if (!currentOversized && messageBytes + finalLen > maxMessageBytes)
                    MarkOversized(ref currentOversized, ref currentHasContent, ref current);
                if (!currentOversized && materialize)
                    WriteUnescapedFromLine(line.GetBuffer().AsSpan(0, finalLen), current!);
                messageBytes += finalLen;
                currentHasContent = true;
            }
        }

        if (currentHasContent)
        {
            onBytesRead?.Invoke(rawStream.Position);
            onMessageStart?.Invoke(currentStart);
            yield return currentOversized
                ? MessageChunk.Oversized(messageBytes)
                : MessageChunk.Ok(materialize ? current : null);
        }
    }

    /// <summary>Marks the current message oversized and releases its partial buffer (deletes any temp
    /// file), so a message past the cap consumes no further memory/disk. Content writes stop; the engine
    /// keeps parsing lines for boundary detection and yields a <see cref="MessageChunk.Oversized"/>.
    /// <para>Also sets <paramref name="currentHasContent"/>: a message big enough to trip the cap always
    /// has content, so it MUST still be yielded (as oversized) even when the cap trips on the FIRST
    /// content line via the partial-line path — where the normal <c>currentHasContent = true</c> in the
    /// completed-line <c>else</c> branch never runs. Without this, a bare <c>From </c> boundary followed
    /// by one huge no-newline line at EOF would be silently dropped — the exact tail-drop this fix
    /// prevents.</para></summary>
    private static void MarkOversized(ref bool oversized, ref bool currentHasContent, ref SpillableMessageBuffer? current)
    {
        oversized = true;
        currentHasContent = true;
        current?.Dispose();
        current = null;
    }

    /// <summary>Back-seek window for <see cref="FindBoundaryAtOrAfter"/>'s context bootstrap.</summary>
    private const int BoundaryBackWindow = 64 * 1024;

    /// <summary>
    /// Discovery helper for the byte-range splitter (<see cref="Scanning.MboxMessageSplitter"/>): returns the
    /// absolute offset of the first REAL message boundary at offset &gt;= <paramref name="target"/>, decided by
    /// the SAME <see cref="IsMessageBoundary"/>/<see cref="IsBlankLine"/> rule the parse engine uses — so a
    /// split offset can never disagree with where <see cref="Parse"/>/<see cref="ScanRange"/> would begin a
    /// message (the byte-identity guarantee depends on the two notions of "boundary" being one and the same).
    ///
    /// Context bootstrap `[R3]`: seeks back up to <see cref="BoundaryBackWindow"/> before <paramref name="target"/>
    /// to a clean line start, then refuses to accept any boundary until at least one full line has been observed
    /// since that clean start, so <c>previousLineWasBlank</c> reflects a line actually read (not assumed). When the
    /// back-window reaches BOF the scan starts with <c>previousLineWasBlank = true</c> (the engine's BOF rule).
    ///
    /// Returns <c>null</c> if no boundary is found before <c>target + <paramref name="scanCap"/></c>.
    /// </summary>
    internal static long? FindBoundaryAtOrAfter(Stream stream, long target, long scanCap)
    {
        long backStart = Math.Max(0, target - BoundaryBackWindow);
        stream.Seek(backStart, SeekOrigin.Begin);

        long scanLimit = target + scanCap;            // boundaries at >= this offset are out of budget
        bool previousLineWasBlank = backStart == 0;   // BOF: previous "line" is treated as blank
        bool contextEstablished = backStart == 0;     // at BOF the blank-state is real immediately
        bool needCleanStart = backStart > 0;          // mid-stream: discard the first (partial) line

        var buffer = new byte[BufferSize];
        using var line = new MemoryStream(256);
        long lineStart = backStart;                   // absolute offset of the current line's first byte

        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
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

                int lineLen = (int)line.Length;

                if (needCleanStart)
                {
                    // We seeked into the middle of this line; discard it. The next line begins at a
                    // clean line start, and only then does observed blank-state become trustworthy.
                    needCleanStart = false;
                    lineStart += lineLen;
                    line.SetLength(0);
                    continue;
                }

                ReadOnlySpan<byte> span = line.GetBuffer().AsSpan(0, lineLen);
                if (contextEstablished && lineStart >= target
                    && IsMessageBoundary(span, previousLineWasBlank))
                {
                    return lineStart;
                }

                previousLineWasBlank = IsBlankLine(span);
                contextEstablished = true;            // one full line observed since the clean start
                lineStart += lineLen;
                line.SetLength(0);

                if (lineStart >= scanLimit)
                    return null;
            }
        }

        // A boundary can be the final line even without a trailing '\n'.
        if (line.Length > 0 && !needCleanStart)
        {
            int finalLen = (int)line.Length;
            ReadOnlySpan<byte> span = line.GetBuffer().AsSpan(0, finalLen);
            if (contextEstablished && lineStart >= target && lineStart < scanLimit
                && IsMessageBoundary(span, previousLineWasBlank))
            {
                return lineStart;
            }
        }

        return null;
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
