// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Linq;

namespace Mail2Pst.Cli;

internal sealed record ProfileResolution(string? Name, string? ErrorCode);

/// <summary>Precedence: explicit flag (must exist) → registry default (only if it names an existing
/// profile) → sole existing profile → error. Matches are case-insensitive; the registry's casing is
/// returned (the COM Logon gets the canonical name).</summary>
internal static class OutlookProfileResolver
{
    internal static ProfileResolution Resolve(string? explicitName, OutlookProfileInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (explicitName is not null)
        {
            string? match = info.Profiles.FirstOrDefault(p => string.Equals(p, explicitName, StringComparison.OrdinalIgnoreCase));
            return match is null ? new(null, "unknown-outlook-profile") : new(match, null);
        }
        if (info.DefaultProfile is not null)
        {
            string? match = info.Profiles.FirstOrDefault(p => string.Equals(p, info.DefaultProfile, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return new(match, null);
        }
        return info.Profiles.Count switch
        {
            0 => new(null, "no-outlook-profile"),
            1 => new(info.Profiles[0], null),
            _ => new(null, "ambiguous-outlook-profile"),
        };
    }
}
