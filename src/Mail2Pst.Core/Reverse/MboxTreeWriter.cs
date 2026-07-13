// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MimeKit;

namespace Mail2Pst.Core.Reverse;

/// <summary>Result of writing the mbox tree: the mbox files created, folder count, and message count.</summary>
public sealed record MboxTreeWriteResult(
    IReadOnlyList<string> MboxFiles, int FoldersWritten, int MessagesWritten);

/// <summary>
/// Writes a Thunderbird mail-folder tree to disk: a <c>.sbd</c>-nested mbox layout where each streamed
/// message becomes an mboxrd-escaped mbox entry carrying its <c>X-Mozilla-Status</c> / <c>-Status2</c> /
/// <c>-Keys</c> headers. Structure comes from a <see cref="MboxTreePlanner"/> plan; MIME body reconstruction
/// comes from the injected <see cref="IMimeReconstructor"/>. The writer owns the envelope
/// (<c>From </c> separator) and the three <c>X-Mozilla-*</c> header lines, which it writes as the FIRST
/// header lines of every entry so their position and formatting stay under its control.
/// </summary>
public sealed class MboxTreeWriter
{
    private static readonly byte[] FromMarker = Encoding.ASCII.GetBytes("From ");

    // Thunderbird reserves fixed-width blank space after "X-Mozilla-Keys: " so it can rewrite a message's
    // keywords in place. X_MOZILLA_KEYWORDS_BLANK_LEN = 80 in mozilla/releases-comm-central:
    // mailnews/base/src/nsMsgLocalFolderHdrs.h; the compactor right-pads the VALUE with spaces up to this
    // minimum. We reproduce the reserve so TB's in-place keyword edits behave. Applies ONLY to X-Mozilla-Keys
    // (Status/Status2 are already fixed-width via the x4 hex format / "00000000").
    private const int XMozillaKeywordsBlankLen = 80;

    private readonly IMimeReconstructor _reconstructor;
    private readonly FormatOptions _formatOptions;

    public MboxTreeWriter(IMimeReconstructor reconstructor)
    {
        _reconstructor = reconstructor ?? throw new ArgumentNullException(nameof(reconstructor));
        _formatOptions = FormatOptions.Default.Clone();
        _formatOptions.NewLineFormat = NewLineFormat.Dos;   // CRLF, matching the raw parts + forward fixtures
    }

    /// <param name="items">Lazy stream of read-back messages. Reconstruction/serialization happens
    /// one item at a time, so attachment OpenRead closures are invoked while the item is current.</param>
    /// <param name="folders">Every mail folder path in the PST (the structure authority — includes empty
    /// folders so <paramref name="includeEmpty"/> and structural parents can be honored).</param>
    /// <param name="includeEmpty">When true, empty LEAF folders are emitted as empty mbox files; structural
    /// parents are ALWAYS emitted regardless.</param>
    /// <param name="onMessageWritten">Optional callback invoked AFTER each message is successfully appended,
    /// with the item and the running written-count. Lets the export runner emit progress that reflects only
    /// messages actually on disk (a message that throws during reconstruction/serialization never ticks).</param>
    public MboxTreeWriteResult Write(
        IEnumerable<PstMailItem> items,
        IReadOnlyList<IReadOnlyList<string>> folders,
        string outputRoot,
        bool includeEmpty,
        Action<string>? onWarning = null,
        Action<PstMailItem, int>? onMessageWritten = null)
    {
        Directory.CreateDirectory(outputRoot);
        MboxTreePlan plan = MboxTreePlanner.Plan(folders, outputRoot, onWarning);

        var created = new List<string>();
        var createdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scaffolding: structural parents ALWAYS (empty mbox + .sbd dir); empty leaves per includeEmpty.
        foreach (MboxFolderPlan fp in plan.Folders)
        {
            if (fp.IsStructuralParent)
            {
                Directory.CreateDirectory(fp.SbdDirPath!);
                EnsureMbox(fp.MboxFilePath, created, createdSet);
            }
            else if (includeEmpty)
            {
                EnsureMbox(fp.MboxFilePath, created, createdSet);
            }
        }

        int messages = 0;
        foreach (PstMailItem item in items)
        {
            string mboxPath;
            if (plan.TryGet(item.FolderPath, out MboxFolderPlan? fp) && fp is not null)
            {
                mboxPath = fp.MboxFilePath;
            }
            else
            {
                // Defensive: an item whose folder was not declared in `folders`. Best-effort path (no
                // collision suffixing) so a message is never dropped; a correct orchestrator declares
                // every folder, so this branch is not expected in practice.
                onWarning?.Invoke(
                    $"message in undeclared folder '{FolderPathDisplay.Join(item.FolderPath)}'; creating it best-effort.");
                mboxPath = MboxTreePlanner.ResolveMboxPath(item.FolderPath, outputRoot);
            }

            EnsureMbox(mboxPath, created, createdSet);
            AppendMessage(mboxPath, item.Message);   // throws (fatal) on reconstruct/serialize/write failure
            messages++;
            onMessageWritten?.Invoke(item, messages);   // fires only AFTER a successful append
        }

        return new MboxTreeWriteResult(created, created.Count, messages);
    }

    private static void EnsureMbox(string path, List<string> created, HashSet<string> createdSet)
    {
        if (!createdSet.Add(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
            using (File.Create(path)) { }
        created.Add(path);
    }

    private void AppendMessage(string mboxPath, PstMailMessage message)
    {
        using MimeMessage mime = _reconstructor.Reconstruct(message);
        MozillaStatusHeaders h = MozillaStatusMapper.Map(message);

        using var fs = new FileStream(mboxPath, FileMode.Append, FileAccess.Write, FileShare.None);

        // Envelope separator (postmark-shaped) then the three writer-owned X-Mozilla-* header lines, written
        // RAW (not via MimeKit's header collection, which trims/folds trailing whitespace and would destroy
        // the X-Mozilla-Keys reserve). Status/Status2 are already fixed-width; the Keys VALUE is right-padded
        // with spaces to a minimum of XMozillaKeywordsBlankLen so Thunderbird can rewrite keywords in place.
        WriteAscii(fs, "From - " + Asctime(message.Date) + "\r\n");
        WriteAscii(fs, "X-Mozilla-Status: " + h.Status + "\r\n");
        WriteAscii(fs, "X-Mozilla-Status2: " + h.Status2 + "\r\n");
        WriteAscii(fs, "X-Mozilla-Keys: " + h.Keys.PadRight(XMozillaKeywordsBlankLen) + "\r\n");

        // Serialize the reconstructed message (its own headers + body), then mboxrd-escape every line.
        using var ms = new MemoryStream();
        mime.WriteTo(_formatOptions, ms);
        EscapeFromLines(new ReadOnlySpan<byte>(ms.GetBuffer(), 0, (int)ms.Length), fs);

        WriteAscii(fs, "\r\n");   // blank-line separator before the next From_ boundary
    }

    private static string Asctime(DateTimeOffset? date)
        => (date?.UtcDateTime ?? DateTime.UnixEpoch)
            .ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);

    private static void WriteAscii(Stream fs, string s)
    {
        byte[] b = Encoding.ASCII.GetBytes(s);
        fs.Write(b, 0, b.Length);
    }

    // mboxrd escaping: any serialized line matching ^>*From  gets ONE extra leading '>'. Exact inverse of
    // MboxParser.WriteUnescapedFromLine (which strips one '>' from ^>+From  on read).
    private static void EscapeFromLines(ReadOnlySpan<byte> data, Stream fs)
    {
        int i = 0;
        while (i < data.Length)
        {
            int nl = data.Slice(i).IndexOf((byte)'\n');
            int len = nl == -1 ? data.Length - i : nl + 1;
            ReadOnlySpan<byte> line = data.Slice(i, len);

            int gt = 0;
            while (gt < line.Length && line[gt] == (byte)'>') gt++;
            if (StartsWithMarkerAt(line, gt, FromMarker))
                fs.WriteByte((byte)'>');
            fs.Write(line);

            i += len;
        }
    }

    private static bool StartsWithMarkerAt(ReadOnlySpan<byte> line, int index, ReadOnlySpan<byte> marker)
    {
        if (line.Length - index < marker.Length) return false;
        for (int i = 0; i < marker.Length; i++)
            if (line[index + i] != marker[i]) return false;
        return true;
    }
}
