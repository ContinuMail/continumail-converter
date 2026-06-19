// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Parsing.Mbox;

namespace Mail2Pst.Core.Discovery;

/// <summary>
/// Reconstructs a nested mail-folder tree (Thunderbird <name> + sibling <name>.sbd/ layout, or a
/// flat directory of .mbox files) into a list of sources each carrying an explicit nested
/// TargetFolderPath. Parse-free: filesystem reads only (a small prefix per file for the content
/// sniff). Observational: emits raw names + structured warnings, never renames.
/// </summary>
public static class MailTreeDiscovery
{
    private const int SniffPrefixBytes = 4096;

    private static readonly HashSet<string> MetadataExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".msf", ".dat", ".sqlite", ".sqlite-wal", ".sqlite-shm" };

    private sealed class Ctx
    {
        public readonly List<DiscoveredSource> Sources = new();
        public readonly List<DiscoveryWarning> Warnings = new();
        public readonly List<DiscoverySkipped> Skipped = new();
        public bool SawSbd;
        public int MetadataCount;
        public int PairedMsf, UnpairedMbox, OrphanMsf;
    }

    public static DiscoveryResult Discover(string rootDir, IReadOnlyList<string>? pathPrefix = null)
    {
        var ctx = new Ctx();

        // Root enumeration failure propagates (caller treats as fatal). Sub-directory failures
        // are caught inside Walk and recorded.
        string[] rootEntries = Directory.GetFileSystemEntries(rootDir);
        ProcessEntries(rootDir, rootEntries, pathPrefix ?? Array.Empty<string>(), ctx);

        if (ctx.MetadataCount > 0)
            ctx.Skipped.Add(new DiscoverySkipped("metadata-files-skipped", rootDir,
                $"Skipped {ctx.MetadataCount} Thunderbird metadata/index files."));

        AddDuplicateWarnings(ctx);
        AddInvalidNameWarnings(ctx);

        string layout = ComputeLayout(ctx);
        return new DiscoveryResult(rootDir, layout, ctx.Sources, ctx.Warnings, ctx.Skipped,
            new DiscoveryPairingSummary(ctx.PairedMsf, ctx.UnpairedMbox, ctx.OrphanMsf));
    }

    private static void Walk(string dir, IReadOnlyList<string> prefix, Ctx ctx)
    {
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ctx.Skipped.Add(new DiscoverySkipped("unreadable", dir, ex.Message));
            ctx.Warnings.Add(new DiscoveryWarning("unreadable", dir, null, null, null, null,
                $"Could not read directory: {ex.Message}"));
            return;
        }
        ProcessEntries(dir, entries, prefix, ctx);
    }

    private static void ProcessEntries(string dir, string[] entries, IReadOnlyList<string> prefix, Ctx ctx)
    {
        Array.Sort(entries, (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));

        // Pre-pass: classify this directory's .msf entries. ONLY a normal, readable, non-reparse,
        // non-directory .msf FILE is eligible to pair (so SP4b never gets a dangerous/invalid MsfPath). A
        // .msf that is a symlink/directory/unreadable is fully handled here — and the main loop below skips
        // ALL .msf-named entries — so each is processed exactly once.
        var msfByBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // base -> full .msf path
        foreach (string e in entries)
        {
            string n = Path.GetFileName(e);
            if (!n.EndsWith(".msf", StringComparison.OrdinalIgnoreCase) || n.StartsWith(".", StringComparison.Ordinal))
                continue;
            FileAttributes a;
            try { a = File.GetAttributes(e); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ctx.Skipped.Add(new DiscoverySkipped("unreadable", e, ex.Message));
                ctx.Warnings.Add(new DiscoveryWarning("unreadable", e, null, null, null, null, $"Could not read entry: {ex.Message}"));
                continue;
            }
            if ((a & FileAttributes.ReparsePoint) != 0)
            {
                ctx.Skipped.Add(new DiscoverySkipped("symlink-skipped", e, "Symlink/reparse-point not followed."));
                ctx.Warnings.Add(new DiscoveryWarning("symlink-skipped", e, null, null, null, null, "Symlink/reparse-point skipped."));
                continue;
            }
            if ((a & FileAttributes.Directory) != 0)
            {
                ctx.Skipped.Add(new DiscoverySkipped("unexpected-subdirectory", e, "Directory named .msf is not a summary file."));
                ctx.Warnings.Add(new DiscoveryWarning("unexpected-subdirectory", e, null, null, null, null, "Directory named .msf skipped."));
                continue;
            }
            msfByBase[n[..^4]] = e; // a normal .msf file — eligible to pair
        }
        var pairedBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);

            // .msf entries were fully classified by the pre-pass — skip every .msf-named entry here.
            if (name.EndsWith(".msf", StringComparison.OrdinalIgnoreCase) && !name.StartsWith(".", StringComparison.Ordinal))
                continue;

            FileAttributes attr;
            try { attr = File.GetAttributes(entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ctx.Skipped.Add(new DiscoverySkipped("unreadable", entry, ex.Message));
                ctx.Warnings.Add(new DiscoveryWarning("unreadable", entry, null, null, null, null,
                    $"Could not read entry: {ex.Message}"));
                continue;
            }

            // 1) Symlink / reparse-point FIRST — never followed, never classified further.
            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                ctx.Skipped.Add(new DiscoverySkipped("symlink-skipped", entry, "Symlink/reparse-point not followed."));
                ctx.Warnings.Add(new DiscoveryWarning("symlink-skipped", entry, null, null, null, null,
                    "Symlink/reparse-point skipped."));
                continue;
            }

            if ((attr & FileAttributes.Directory) != 0)
            {
                if (name.EndsWith(".sbd", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.SawSbd = true;
                    string segment = name[..^4]; // strip ".sbd"
                    var childPrefix = new List<string>(prefix) { segment };
                    Walk(entry, childPrefix, ctx);
                }
                else
                {
                    ctx.Skipped.Add(new DiscoverySkipped("unexpected-subdirectory", entry, "Not a .sbd directory."));
                    ctx.Warnings.Add(new DiscoveryWarning("unexpected-subdirectory", entry, null, null, null, null,
                        "Unexpected non-.sbd subdirectory skipped."));
                }
                continue;
            }

            // File (non-.msf).
            string ext = Path.GetExtension(name);
            if (name.StartsWith(".", StringComparison.Ordinal) || MetadataExtensions.Contains(ext))
            {
                ctx.MetadataCount++;
                continue;
            }

            long size;
            bool isMbox;
            try
            {
                size = new FileInfo(entry).Length;
                isMbox = SniffIsMbox(entry, size);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ctx.Skipped.Add(new DiscoverySkipped("unreadable", entry, ex.Message));
                ctx.Warnings.Add(new DiscoveryWarning("unreadable", entry, null, null, null, null,
                    $"Could not read file: {ex.Message}"));
                continue;
            }

            if (!isMbox)
            {
                ctx.Skipped.Add(new DiscoverySkipped("not-mbox", entry, "Not a recognized mbox file."));
                ctx.Warnings.Add(new DiscoveryWarning("not-mbox", entry, null, null, null, null,
                    "File is not a recognized mbox store; skipped."));
                continue;
            }

            string display = name.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase) ? name[..^5] : name;
            var targetPath = new List<string>(prefix) { display };

            // Pair this ACCEPTED mbox with its sibling <name>.msf, if present.
            string? msfPath = null;
            if (msfByBase.TryGetValue(name, out string? mp)) { msfPath = mp; pairedBases.Add(name); ctx.PairedMsf++; }
            else ctx.UnpairedMbox++;

            ctx.Sources.Add(new DiscoveredSource(entry, "mbox", targetPath, display, size, msfPath));
        }

        // Orphan .msf: a .msf whose base was not an accepted mbox in this directory.
        foreach (var kv in msfByBase)
        {
            if (pairedBases.Contains(kv.Key)) continue;
            ctx.OrphanMsf++;
            ctx.Skipped.Add(new DiscoverySkipped("orphan-msf", kv.Value, "No paired mbox for this .msf summary file."));
        }
    }

    private static bool SniffIsMbox(string path, long size)
    {
        if (size == 0) return true; // exactly 0 bytes = empty folder

        using FileStream fs = File.OpenRead(path);
        int cap = (int)Math.Min(SniffPrefixBytes, size);
        byte[] buffer = new byte[cap];
        int read = fs.ReadAtLeast(buffer, cap, throwOnEndOfStream: false);
        ReadOnlySpan<byte> span = buffer.AsSpan(0, read);

        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            span = span[3..]; // skip UTF-8 BOM

        while (true)
        {
            int nl = span.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line = nl >= 0 ? span[..nl] : span;
            if (!IsBlank(line))
                return MboxPostmark.IsEnvelopePostmark(line);
            if (nl < 0) return false;          // only blank lines within the prefix
            span = span[(nl + 1)..];
            if (span.IsEmpty) return false;
        }
    }

    private static bool IsBlank(ReadOnlySpan<byte> line)
    {
        foreach (byte b in line)
            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
                return false;
        return true;
    }

    private static void AddDuplicateWarnings(Ctx ctx)
    {
        var groups = ctx.Sources
            .GroupBy(s => string.Join('\0', s.TargetFolderPath), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var g in groups)
        {
            var first = g.First();
            ctx.Warnings.Add(new DiscoveryWarning(
                "duplicate-target-folder-path", first.Path, first.TargetFolderPath, null, null,
                g.Select(s => s.Path).ToList(),
                $"Multiple sources map to target folder {string.Join(" / ", first.TargetFolderPath)}."));
        }
    }

    private static void AddInvalidNameWarnings(Ctx ctx)
    {
        foreach (DiscoveredSource s in ctx.Sources)
            for (int i = 0; i < s.TargetFolderPath.Count; i++)
            {
                try { FolderNameValidator.Validate(s.TargetFolderPath[i]); }
                catch (ConfigValidationException ex)
                {
                    ctx.Warnings.Add(new DiscoveryWarning(
                        "invalid-folder-name", s.Path, s.TargetFolderPath, s.TargetFolderPath[i], i, null, ex.Message));
                }
            }
    }

    private static string ComputeLayout(Ctx ctx)
    {
        if (ctx.SawSbd) return "thunderbird";
        if (ctx.Sources.Count == 0) return "empty";
        bool allRootMbox = ctx.Sources.All(s =>
            s.TargetFolderPath.Count == 1 && s.Path.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase));
        return allRootMbox ? "flat-mbox" : "mixed";
    }
}
