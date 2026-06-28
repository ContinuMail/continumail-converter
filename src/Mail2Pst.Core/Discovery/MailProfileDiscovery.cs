// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Msf;

namespace Mail2Pst.Core.Discovery;

/// <summary>
/// Profile-aware orchestrator over <see cref="MailTreeDiscovery"/>. Auto-classifies the input
/// (Thunderbird store-root / profile / single account tree), walks each account directory with the
/// account name as the top folder segment, and merges the results with a cross-account duplicate pass.
/// Parse-free; pairs mbox with sibling .msf via path existence only (no .msf parsing).
/// </summary>
public static class MailProfileDiscovery
{
    private static readonly string[] StoreNames = { "Mail", "ImapMail" };

    public static DiscoveryResult Discover(string path)
    {
        string ownName = new DirectoryInfo(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;

        // 1) Store-root: the input directory itself is named Mail/ImapMail.
        if (StoreNames.Contains(ownName, StringComparer.OrdinalIgnoreCase))
        {
            string? storeRootPrefs = Directory.GetParent(path)?.FullName;
            return DiscoverStores(path, new[] { path }, "thunderbird-store-root", storeRootPrefs);
        }

        // 2) Profile: contains Mail and/or ImapMail child directories.
        var stores = StoreNames
            .Select(s => Path.Combine(path, s))
            .Where(Directory.Exists)
            .ToList();
        if (stores.Count > 0)
            return DiscoverStores(path, stores, "thunderbird-profile", path);

        // 3) Single tree: today's behaviour, no prefix.
        return MailTreeDiscovery.Discover(path);
    }

    private static DiscoveryResult DiscoverStores(string root, IReadOnlyList<string> stores, string layout, string? prefsRoot)
    {
        // Track each source's ORIGIN (the account directory it came from) so the cross-account dedupe
        // can require >1 distinct origin — two same-named account dirs collide on path AND first segment,
        // so a first-segment check would miss them.
        var indexed = new List<(DiscoveredSource Source, string OriginKey)>();
        var warnings = new List<DiscoveryWarning>();
        var skipped = new List<DiscoverySkipped>();
        int paired = 0, unpaired = 0, orphan = 0;

        foreach (string store in stores)
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(store); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new DiscoverySkipped("unreadable", store, ex.Message));
                warnings.Add(new DiscoveryWarning("unreadable", store, null, null, null, null,
                    $"Could not read store: {ex.Message}"));
                continue;
            }

            foreach (string entry in entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            {
                FileAttributes attr;
                try { attr = File.GetAttributes(entry); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new DiscoverySkipped("unreadable", entry, ex.Message)); continue;
                }

                // Never follow symlinks/reparse points (mirrors MailTreeDiscovery's policy).
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    skipped.Add(new DiscoverySkipped("symlink-skipped", entry, "Symlink/reparse-point not followed."));
                    warnings.Add(new DiscoveryWarning("symlink-skipped", entry, null, null, null, null,
                        "Symlink/reparse-point skipped."));
                    continue;
                }

                if ((attr & FileAttributes.Directory) == 0)
                {
                    // Loose file directly under Mail/ or ImapMail/ — accounts are directories only.
                    skipped.Add(new DiscoverySkipped("unexpected-file-in-store-root", entry, "Not an account directory."));
                    warnings.Add(new DiscoveryWarning("unexpected-file-in-store-root", entry, null, null, null, null,
                        "Unexpected file directly under a mail store; skipped."));
                    continue;
                }

                string account = Path.GetFileName(entry);
                DiscoveryResult acct;
                try { acct = MailTreeDiscovery.Discover(entry, new[] { account }); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new DiscoverySkipped("unreadable", entry, ex.Message));
                    warnings.Add(new DiscoveryWarning("unreadable", entry, null, null, null, null,
                        $"Could not read account: {ex.Message}"));
                    continue;
                }

                foreach (DiscoveredSource s in acct.Sources)
                    indexed.Add((s with { AccountId = entry }, entry)); // OriginKey = account dir path
                warnings.AddRange(acct.Warnings);
                skipped.AddRange(acct.Skipped);
                paired += acct.Pairing.PairedMsfCount;
                unpaired += acct.Pairing.UnpairedMboxCount;
                orphan += acct.Pairing.OrphanMsfCount;
            }
        }

        AddCrossAccountDuplicateWarnings(indexed, warnings);

        var sources = indexed.Select(x => x.Source).ToList();

        var prefs = string.IsNullOrEmpty(prefsRoot)
            ? new Dictionary<string, PrefsAccount>()
            : PrefsAccountReader.Read(Path.Combine(prefsRoot, "prefs.js"));

        var accounts = indexed
            .Select(x => x.OriginKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(dir =>
            {
                string seg = Path.GetFileName(dir);
                string? store = Path.GetFileName(Path.GetDirectoryName(dir));
                string key = ((store ?? "") + "/" + seg).ToLowerInvariant();
                PrefsAccount? pa = prefs.TryGetValue(key, out var v) ? v : null;
                AddressResolution fallback = IsLocalFolders(store, seg)
                    ? AddressResolution.LocalFolders
                    : AddressResolution.NotFound;
                return new Account(dir, seg, dir, store, pa?.Email, pa?.Host, pa?.Resolution ?? fallback);
            })
            .ToList();

        var addressBooks = DiscoverAddressBooks(prefsRoot ?? root).ToList();

        return new DiscoveryResult(root, layout, sources, warnings, skipped,
            new DiscoveryPairingSummary(paired, unpaired, orphan))
        {
            Accounts = accounts,
            AddressBooks = addressBooks,
        };
    }

    public static IEnumerable<DiscoveredAddressBook> DiscoverAddressBooks(string profileDir)
    {
        if (!Directory.Exists(profileDir)) yield break;
        foreach (string sqlite in Directory.EnumerateFiles(profileDir, "*.sqlite"))
        {
            string name = Path.GetFileName(sqlite);
            if (name is "abook.sqlite" or "history.sqlite" || name.StartsWith("abook", StringComparison.OrdinalIgnoreCase))
                yield return new DiscoveredAddressBook
                {
                    DisplayName = FriendlyBookName(name), Path = sqlite, Format = "thunderbird-sqlite",
                };
        }
        foreach (string mab in Directory.EnumerateFiles(profileDir, "*.mab"))
            yield return new DiscoveredAddressBook
            {
                DisplayName = FriendlyBookName(Path.GetFileName(mab)), Path = mab, Format = "thunderbird-mab",
            };
    }

    private static string FriendlyBookName(string fileName) => fileName switch
    {
        "abook.sqlite" or "abook.mab" => "Personal Address Book",
        "history.sqlite" or "history.mab" => "Collected Addresses",
        _ => Path.GetFileNameWithoutExtension(fileName),
    };

    private static bool IsLocalFolders(string? store, string segment) =>
        string.Equals(store, "Mail", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(segment, "Local Folders", StringComparison.OrdinalIgnoreCase);

    // Emit a duplicate-target-folder-path warning ONLY for groups whose sources come from >1 distinct
    // ORIGIN (account dir). Same-account duplicates were already warned by MailTreeDiscovery's per-tree
    // pass, so they are not re-emitted here.
    private static void AddCrossAccountDuplicateWarnings(
        List<(DiscoveredSource Source, string OriginKey)> indexed, List<DiscoveryWarning> warnings)
    {
        var groups = indexed
            .GroupBy(x => string.Join('\0', x.Source.TargetFolderPath), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1
                     && g.Select(x => x.OriginKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        foreach (var g in groups)
        {
            DiscoveredSource first = g.First().Source;
            warnings.Add(new DiscoveryWarning("duplicate-target-folder-path", first.Path,
                first.TargetFolderPath, null, null, g.Select(x => x.Source.Path).ToList(),
                $"Multiple sources map to target folder {string.Join(" / ", first.TargetFolderPath)}."));
        }
    }
}
