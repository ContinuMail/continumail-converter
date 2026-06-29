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

public sealed class DiscoveredCalendarSource
{
    public string CalId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string StoreKind { get; init; } = string.Empty; // e.g. "local" | "caldav"
    public string StorePath { get; init; } = string.Empty;
    public string CalendarType { get; init; } = string.Empty; // "calendar" | "task" | "both"
    public bool IsVisibleInThunderbird { get; init; }
    public int EventCount { get; init; }
    public int TaskCount { get; init; }
    public IReadOnlyList<string> DefaultCalendarFolderPath { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> DefaultTaskFolderPath { get; init; } = System.Array.Empty<string>();
}

public sealed class CalendarDiscoveryResult
{
    public List<DiscoveredCalendarSource> Calendars { get; init; } = new();
    public List<DiscoveryWarning> Warnings { get; init; } = new();
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
    public List<DiscoveredCalendarSource> Calendars { get; init; } = new();
}
