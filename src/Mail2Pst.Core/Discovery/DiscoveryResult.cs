// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Discovery;

public sealed record DiscoveredSource(
    string Path, string Type, IReadOnlyList<string> TargetFolderPath, string DisplayName, long SourceBytes,
    string? MsfPath);

public sealed record DiscoveryWarning(
    string Code, string Path, IReadOnlyList<string>? TargetFolderPath,
    string? Segment, int? SegmentIndex, IReadOnlyList<string>? RelatedPaths, string Message);

public sealed record DiscoverySkipped(string Code, string Path, string Reason);

public sealed record DiscoveryPairingSummary(int PairedMsfCount, int UnpairedMboxCount, int OrphanMsfCount);

public sealed record DiscoveryResult(
    string Root, string Layout,
    IReadOnlyList<DiscoveredSource> Sources,
    IReadOnlyList<DiscoveryWarning> Warnings,
    IReadOnlyList<DiscoverySkipped> Skipped,
    DiscoveryPairingSummary Pairing);
