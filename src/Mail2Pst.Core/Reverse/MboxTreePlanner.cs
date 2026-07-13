// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mail2Pst.Core.Reverse;

/// <summary>The on-disk plan for one PST mail folder.</summary>
/// <param name="SourcePath">Original PST folder path (identity; segments are display names).</param>
/// <param name="MboxFilePath">Absolute path of the (extensionless) mbox file for this folder.</param>
/// <param name="SbdDirPath">Absolute path of the <c>Name.sbd/</c> child directory, or null for a leaf.</param>
/// <param name="IsStructuralParent">True when this folder has at least one descendant folder.</param>
public sealed record MboxFolderPlan(
    IReadOnlyList<string> SourcePath, string MboxFilePath, string? SbdDirPath, bool IsStructuralParent);

/// <summary>Lookup over the planned folders, keyed by the folder path identity.</summary>
public sealed class MboxTreePlan
{
    private readonly Dictionary<string, MboxFolderPlan> _byKey;

    public IReadOnlyList<MboxFolderPlan> Folders { get; }

    public MboxTreePlan(IReadOnlyList<MboxFolderPlan> folders)
    {
        Folders = folders;
        _byKey = new Dictionary<string, MboxFolderPlan>(StringComparer.Ordinal);
        foreach (MboxFolderPlan f in folders)
            _byKey[FolderPathKey.Join(f.SourcePath)] = f;
    }

    public bool TryGet(IReadOnlyList<string> path, out MboxFolderPlan? plan)
        => _byKey.TryGetValue(FolderPathKey.Join(path), out plan);
}

/// <summary>
/// Turns the PST mail-folder tree (a list of folder paths) into an on-disk <see cref="MboxTreePlan"/>: the
/// Thunderbird <c>.sbd</c> layout, with filesystem-safe name sanitization and deterministic collision
/// suffixes. Pure: computes paths only, touches no disk. Missing intermediate folders are synthesized so a
/// structural parent is always represented even if the reader omitted it.
/// </summary>
public static class MboxTreePlanner
{
    public static MboxTreePlan Plan(
        IReadOnlyList<IReadOnlyList<string>> folders, string outputRoot, Action<string>? onWarning = null)
    {
        // 0. Expand to every ancestor prefix (distinct, shallow-first / first-seen order). Guarantees an
        //    ancestor is always present and a structural parent is always materialized.
        List<IReadOnlyList<string>> allPaths = ExpandWithAncestors(folders);

        // 1. Structural-parent set: P is structural iff some other path has P as a strict prefix.
        var structural = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> p in allPaths)
            foreach (IReadOnlyList<string> q in allPaths)
                if (q.Count > p.Count && IsPrefix(p, q)) { structural.Add(FolderPathKey.Join(p)); break; }

        // 2. Choose the on-disk segment name per folder, top-down, with per-parent collision suffixing.
        //    Sibling names collide case-insensitively (Windows FS). A structural parent occupies BOTH its
        //    file name and its "<name>.sbd" directory, so reserve both to avoid a file/dir clash.
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedPerParent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> p in allPaths.OrderBy(x => x.Count))   // stable within a depth
        {
            string parentKey = FolderPathKey.Join(p.Take(p.Count - 1).ToArray());
            if (!usedPerParent.TryGetValue(parentKey, out HashSet<string>? used))
                usedPerParent[parentKey] = used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string baseName = MboxFolderNameSanitizer.ToFileName(p[^1]);
            bool isStructural = structural.Contains(FolderPathKey.Join(p));

            string candidate = baseName;
            int n = 2;
            while (used.Contains(candidate) || (isStructural && used.Contains(candidate + ".sbd")))
                candidate = $"{baseName} ({n++})";

            if (!string.Equals(candidate, baseName, StringComparison.Ordinal))
                onWarning?.Invoke(
                    $"folder name '{FolderPathDisplay.Join(p)}' collides on disk; using '{candidate}'.");

            used.Add(candidate);
            if (isStructural) used.Add(candidate + ".sbd");
            chosen[FolderPathKey.Join(p)] = candidate;
        }

        // 3. Build absolute paths from the chosen ancestor names.
        var result = new List<MboxFolderPlan>(allPaths.Count);
        foreach (IReadOnlyList<string> p in allPaths)
        {
            string dir = outputRoot;
            for (int i = 0; i < p.Count - 1; i++)
                dir = Path.Combine(dir, chosen[FolderPathKey.Join(p.Take(i + 1).ToArray())] + ".sbd");
            string leaf = chosen[FolderPathKey.Join(p)];
            string mbox = Path.Combine(dir, leaf);
            bool isStructural = structural.Contains(FolderPathKey.Join(p));
            string? sbd = isStructural ? Path.Combine(dir, leaf + ".sbd") : null;
            result.Add(new MboxFolderPlan(p, mbox, sbd, isStructural));
        }
        return new MboxTreePlan(result);
    }

    /// <summary>Best-effort mbox path for a folder path (sanitized <c>.sbd</c> layout, NO collision
    /// suffixing). The writer uses this only for a message whose folder was not declared in the plan.</summary>
    public static string ResolveMboxPath(IReadOnlyList<string> path, string outputRoot)
    {
        string dir = outputRoot;
        for (int i = 0; i < path.Count - 1; i++)
            dir = Path.Combine(dir, MboxFolderNameSanitizer.ToFileName(path[i]) + ".sbd");
        return Path.Combine(dir, MboxFolderNameSanitizer.ToFileName(path[^1]));
    }

    private static List<IReadOnlyList<string>> ExpandWithAncestors(IReadOnlyList<IReadOnlyList<string>> folders)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<IReadOnlyList<string>>();
        foreach (IReadOnlyList<string> p in folders)
            for (int d = 1; d <= p.Count; d++)
            {
                string[] prefix = p.Take(d).ToArray();
                if (seen.Add(FolderPathKey.Join(prefix)))
                    ordered.Add(prefix);
            }
        return ordered;
    }

    private static bool IsPrefix(IReadOnlyList<string> p, IReadOnlyList<string> q)
    {
        for (int i = 0; i < p.Count; i++)
            if (!string.Equals(p[i], q[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
