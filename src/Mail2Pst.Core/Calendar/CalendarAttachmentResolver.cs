// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Calendar;

public sealed class CalendarAttachmentResolver
{
    private readonly string? _exportRoot;   // canonical full path; null ⇒ no file resolution allowed
    private readonly long _maxEmbedBytes;   // attachments larger than this degrade to LinkOnly

    public CalendarAttachmentResolver(string? exportRoot, long maxEmbedBytes = int.MaxValue)
    {
        _exportRoot = string.IsNullOrWhiteSpace(exportRoot) ? null : Path.GetFullPath(exportRoot);
        _maxEmbedBytes = maxEmbedBytes;
    }

    public (IReadOnlyList<CalendarAttachment> Attachments, IReadOnlyList<string> Warnings)
        ResolveAll(IEnumerable<RawSideText> rawAttachLines, string subjectForWarnings)
    {
        var atts = new List<CalendarAttachment>();
        var warns = new List<string>();
        foreach (RawSideText raw in rawAttachLines)
        {
            if (string.IsNullOrWhiteSpace(raw.IcalString)) continue;
            var parsed = ICalTextParser.ParseAttachment(raw.IcalString!);
            if (parsed.Value is null)
            {
                var errorMsg = parsed.Warnings.Count > 0 ? parsed.Warnings[0] : "parse error";
                warns.Add($"attachment on '{subjectForWarnings}': parse failed ({errorMsg}) — preserved raw ATTACH text in body");
                atts.Add(new CalendarAttachment(CalendarAttachmentKind.LinkOnly,
                    FileNameOrDefault(null), "application/octet-stream", null, null, raw.IcalString));
                continue;
            }
            ParsedAttachment p = parsed.Value;
            string fileName = FileNameOrDefault(p.FileName);
            string mime = string.IsNullOrWhiteSpace(p.FormatType) ? "application/octet-stream" : p.FormatType!;

            if (p.InlineData is { Length: > 0 })
            {
                if (p.InlineData.Length > _maxEmbedBytes)
                {
                    // Inline data has no external reference, so it is dropped (not "preserved as link").
                    warns.Add($"attachment on '{subjectForWarnings}': inline attachment too large to embed — dropped");
                    atts.Add(new CalendarAttachment(CalendarAttachmentKind.LinkOnly, fileName, mime, null, null, null));
                    continue;
                }
                atts.Add(new CalendarAttachment(CalendarAttachmentKind.InlineBytes, fileName, mime, p.InlineData, null, null));
                continue;
            }

            string? uri = p.Uri;
            if (uri is not null && uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                (CalendarAttachmentKind kind, string? localPath, string? reason) = ResolveLocalFile(uri);
                if (kind == CalendarAttachmentKind.LocalFileByValue)
                {
                    // Thunderbird stores only the URI (no FILENAME param) — an embedded file must
                    // carry its real basename or Outlook gets an extensionless "attachment".
                    if (string.IsNullOrWhiteSpace(p.FileName))
                    {
                        string basename = Path.GetFileName(localPath!);
                        if (!string.IsNullOrWhiteSpace(basename)) fileName = basename;
                    }
                    atts.Add(new CalendarAttachment(kind, fileName, mime, null, localPath, null));
                }
                else
                {
                    warns.Add($"attachment on '{subjectForWarnings}': {reason} — preserved as link");
                    atts.Add(new CalendarAttachment(CalendarAttachmentKind.LinkOnly, fileName, mime, null, null, uri));
                }
                continue;
            }

            // Remote URL (http/https/webcal/…): never fetched.
            warns.Add($"attachment on '{subjectForWarnings}': remote URL preserved as link, not fetched");
            atts.Add(new CalendarAttachment(CalendarAttachmentKind.LinkOnly, fileName, mime, null, null, uri));
        }
        return (atts, warns);
    }

    // Path comparison is a SAFETY boundary: case-insensitive on Windows (its FS is), case-sensitive
    // elsewhere (Linux/macOS FS is) — OrdinalIgnoreCase everywhere would be more permissive than the FS.
    private static readonly StringComparison RootComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Defense-in-depth: Thunderbird credential/key stores that must never be embedded even when a
    // file:// ATTACH resolves to one inside the export root (a crafted calendar could otherwise
    // exfiltrate secrets into the output PST). Matched by file name, case-insensitively.
    private static readonly HashSet<string> SensitiveFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "logins.json", "logins-backup.json", "key3.db", "key4.db",
        "cert8.db", "cert9.db", "signons.sqlite", "credentialstate.sqlite",
    };

    private (CalendarAttachmentKind, string?, string?) ResolveLocalFile(string fileUri)
    {
        if (_exportRoot is null) return (CalendarAttachmentKind.LinkOnly, null, "local file outside export root");
        string full;
        try { full = Path.GetFullPath(new Uri(fileUri).LocalPath); }
        catch (Exception) { return (CalendarAttachmentKind.LinkOnly, null, "local file path unreadable"); }

        string rootWithSep = _exportRoot!.EndsWith(Path.DirectorySeparatorChar)
            ? _exportRoot! : _exportRoot! + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, RootComparison))
            return (CalendarAttachmentKind.LinkOnly, null, "local file outside export root");
        if (SensitiveFileNames.Contains(Path.GetFileName(full)))
            return (CalendarAttachmentKind.LinkOnly, null, "sensitive profile file, not embedded");
        if (!File.Exists(full))
            return (CalendarAttachmentKind.LinkOnly, null, "local file missing / unreadable");
        // Reject symlinks/reparse points — a link inside the root can target a file OUTSIDE it (escape),
        // and the StartsWith check above only guards the LITERAL path, not the link's target. Mirrors the
        // existing MailTreeDiscovery/MailProfileDiscovery symlink-skip policy.
        try { if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            return (CalendarAttachmentKind.LinkOnly, null, "local file is a symlink/reparse point"); }
        catch (Exception) { return (CalendarAttachmentKind.LinkOnly, null, "local file missing / unreadable"); }
        try { using var _ = File.OpenRead(full); }
        catch (Exception) { return (CalendarAttachmentKind.LinkOnly, null, "local file missing / unreadable"); }
        if (new FileInfo(full).Length > _maxEmbedBytes)
            return (CalendarAttachmentKind.LinkOnly, null, "local file too large to embed (>2 GB)");
        return (CalendarAttachmentKind.LocalFileByValue, full, null);
    }

    private static string FileNameOrDefault(string? n) =>
        string.IsNullOrWhiteSpace(n) ? "attachment" : n!;
}
