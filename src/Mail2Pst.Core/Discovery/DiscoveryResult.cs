// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using Mail2Pst.Core.Msf;

namespace Mail2Pst.Core.Discovery;

public sealed record DiscoveredSource(
    string Path, string Type, IReadOnlyList<string> TargetFolderPath, string DisplayName, long SourceBytes,
    string? MsfPath, string? AccountId = null);

public sealed record DiscoveryWarning(
    string Code, string Path, IReadOnlyList<string>? TargetFolderPath,
    string? Segment, int? SegmentIndex, IReadOnlyList<string>? RelatedPaths, string Message);

public sealed record DiscoverySkipped(string Code, string Path, string Reason);

public sealed record DiscoveryPairingSummary(int PairedMsfCount, int UnpairedMboxCount, int OrphanMsfCount);

public sealed record Account(
    string Id, string FolderSegment, string AccountPath, string? Store,
    string? Email, string? Host, AddressResolution AddressResolution);

public sealed class DiscoveredAddressBook
{
    public string DisplayName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty; // "thunderbird-sqlite" | "thunderbird-mab"
}

public sealed record DiscoveryResult(
    string Root, string Layout,
    IReadOnlyList<DiscoveredSource> Sources,
    IReadOnlyList<DiscoveryWarning> Warnings,
    IReadOnlyList<DiscoverySkipped> Skipped,
    DiscoveryPairingSummary Pairing)
{
    public IReadOnlyList<Account> Accounts { get; init; } = System.Array.Empty<Account>();
    public List<DiscoveredAddressBook> AddressBooks { get; init; } = new();
}
