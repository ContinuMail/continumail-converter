// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Linq;

namespace Mail2Pst.Cli;

internal static class OutlookDetection
{
    /// <summary>Classic (COM-automatable) Outlook is registered. Detection ≠ automation success:
    /// spawn/COM failures later get their own stages (outlook-spawn-failed / com-activation-failed).</summary>
    internal static bool ClassicOutlookAvailable() =>
        OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Outlook.Application") is not null;

    /// <summary>Heuristic for the MAPI "profile needs an interactive/connected logon" failure
    /// (Exchange/modern-auth profiles under headless COM). Message-based — MAPI surfaces no
    /// distinct HRESULT for this through late-bound OOM.</summary>
    internal static bool LooksLikeInteractiveLogonRequired(Exception ex) =>
        ex.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns an error message, or null when the name is safe to pass to /PIM,
    /// /profile, and MAPI Logon. Spaces are legal (quoted via argument arrays).</summary>
    internal static string? ValidateProfileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Profile name must not be empty.";
        if (name.Length > 64) return "Profile name too long (max 64 characters).";
        if (name.Any(c => c is '\\' or '/' or '"' or '\'' || char.IsControl(c)))
            return "Profile name must not contain path separators, quotes, or control characters.";
        return null;
    }
}
