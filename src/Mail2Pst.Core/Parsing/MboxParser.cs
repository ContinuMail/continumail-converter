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
using MimeKit;

namespace Mail2Pst.Core.Parsing;

public class MboxParser : IMailSourceParser
{
    private static readonly byte[] FromMarker = Encoding.ASCII.GetBytes("From ");
    private const int BufferSize = 81920;

    private readonly MimeMessageMapper _mapper;

    public MboxParser(long tempFileThresholdBytes = 4L * 1024 * 1024)
    {
        if (tempFileThresholdBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(tempFileThresholdBytes), tempFileThresholdBytes, "Temp-file threshold must be non-negative.");
        _mapper = new MimeMessageMapper(tempFileThresholdBytes);
    }

    public IEnumerable<ParseResult> Parse(string path, Action<long>? onBytesRead = null)
    {
        using FileStream stream = File.OpenRead(path);

        int index = 0;
        foreach (byte[] messageBytes in SplitMessages(stream, onBytesRead))
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
                mime = ParseMimeMessage(messageBytes);
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                // Expected, per-message parse failures (malformed MIME / stream
                // error): record as a skip and continue. Any other exception is
                // an unexpected defect and is allowed to propagate so it surfaces
                // loudly instead of silently dropping mail.
                error = ex.Message;
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
    /// Parses one message's raw bytes into a <see cref="MimeMessage"/>. Per
    /// MimeKit, throws <see cref="FormatException"/> for malformed MIME and
    /// <see cref="IOException"/> for stream errors; <see cref="Parse"/> treats
    /// only those as a per-message skip and lets anything else propagate.
    /// Virtual so tests can substitute the parse step.
    /// </summary>
    protected virtual MimeMessage ParseMimeMessage(byte[] messageBytes)
    {
        using var messageStream = new MemoryStream(messageBytes);
        var entityParser = new MimeParser(messageStream, MimeFormat.Entity);
        return entityParser.ParseMessage();
    }

    public int CountMessages(string path)
    {
        using FileStream stream = File.OpenRead(path);
        // Same boundary engine as SplitMessages — for a given file state the count never
        // drifts from the messages Parse yields. (ConversionRunner reads the file twice:
        // count here, then Parse during conversion; external mutation of the file between
        // the two passes can still diverge — an accepted, out-of-scope limitation.)
        return EnumerateMessageChunks(stream, materialize: false, onBytesRead: null, onMessageStart: null).Count();
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
        foreach (var _ in EnumerateMessageChunks(stream, materialize: false, onBytesRead: null, onMessageStart: offsets.Add))
        { /* enumerate to drive the callback */ }
        return offsets;
    }

    /// <summary>
    /// Splits an mbox file into the raw bytes of each contained message. Thin adapter over
    /// the shared <see cref="EnumerateMessageChunks"/> engine (materialize mode). See that
    /// method for the boundary rule and why we avoid MimeKit's <see cref="MimeFormat.Mbox"/>
    /// parser. The `!` is sound: materialize mode never yields a null chunk.
    /// </summary>
    private static IEnumerable<byte[]> SplitMessages(Stream rawStream, Action<long>? onBytesRead = null) =>
        EnumerateMessageChunks(rawStream, materialize: true, onBytesRead, onMessageStart: null).Select(b => b!);

    /// <summary>
    /// THE single source of truth for "where do messages begin and end" in an mbox stream.
    /// Walks the stream once, line by line, using the shared <see cref="IsMessageBoundary"/>
    /// rule (a "From " line that is the first line, follows a blank line, or matches the
    /// envelope-postmark shape), and yields once per non-empty message. A message boundary
    /// line is mboxrd: in-body lines starting with "From " are stored escaped as ">From "
    /// and un-escaped here. The marker line itself is not part of the returned message.
    ///
    /// Return-value invariant keyed off <paramref name="materialize"/>:
    ///   - true  -> every yielded element is a NON-NULL message byte array.
    ///   - false -> yields a null placeholder per message (no per-message buffer allocated);
    ///              the caller only counts and never dereferences. Keeps counting cheap.
    /// <paramref name="onBytesRead"/> is invoked with the stream position at each yield
    /// (scan progress); pass null when counting.
    /// </summary>
    private static IEnumerable<byte[]?> EnumerateMessageChunks(
        Stream rawStream, bool materialize, Action<long>? onBytesRead, Action<long>? onMessageStart = null)
    {
        var buffer = new byte[BufferSize];
        using var line = new MemoryStream(256);
        MemoryStream? current = materialize ? new MemoryStream() : null;
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
                        yield return materialize ? current!.ToArray() : null;
                        if (materialize) current = new MemoryStream();
                        currentHasContent = false;
                    }
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
            yield return materialize ? current!.ToArray() : null;
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
    private static void WriteUnescapedFromLine(ReadOnlySpan<byte> line, MemoryStream destination)
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
