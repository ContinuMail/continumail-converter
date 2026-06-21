// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using Mail2Pst.Core.Config;

namespace Mail2Pst.Core.Mapping;

/// <summary>
/// Pure decision for junk message placement. In <see cref="JunkHandlingMode.Folder"/>, a message
/// whose .msf enrichment set <c>IsJunk</c> is routed to a single top-level "Junk Email" folder;
/// otherwise the source's mapped path is returned unchanged. No I/O; trivially unit-testable.
/// The constant lives here, NOT on MappingEngine (which keeps its own DefaultFlattenFolderName).
/// </summary>
internal static class JunkRouting
{
    internal const string DefaultJunkFolderName = "Junk Email";

    internal static IReadOnlyList<string> ResolveTargetFolderPath(
        IReadOnlyList<string> mapped, bool isJunk, JunkHandlingMode mode)
        => mode == JunkHandlingMode.Folder && isJunk
            ? new[] { DefaultJunkFolderName }
            : mapped;
}
