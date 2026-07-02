// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Mail2Pst.Core.Config;

/// <summary>
/// Validates a per-source <see cref="SourceConfig.TargetFolder"/> as a safe PST
/// folder name. This is the engine-side enforcement of the SAME policy the GUI
/// applies in <c>desktop/src/lib/options.ts</c> <c>validateFolderName</c> — keep the
/// two rule-sets in sync (a parity test in both projects guards drift). The GUI must
/// not be the only defense: a hand-written config or direct CLI use bypasses it.
/// Throws <see cref="ConfigValidationException"/>. The caller only validates non-null
/// values, but a null/empty/whitespace name is rejected here too.
/// </summary>
public static class FolderNameValidator
{
    // Windows reserved device names, optionally followed by an extension
    // (e.g. "CON", "con.txt"). Mirrors the GUI regex
    // /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?$/i.
    private static readonly Regex ReservedName =
        new(@"^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Validate(string? name)
    {
        string value = name ?? string.Empty;
        string trimmed = value.Trim();

        if (trimmed.Length == 0)
            throw new ConfigValidationException("Folder name can't be empty.");

        if (value.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new ConfigValidationException("Folder name can't contain \\ or /.");

        if (value.Any(ch => ch < 0x20))
            throw new ConfigValidationException("Folder name can't contain control characters.");

        if (value != trimmed)
            throw new ConfigValidationException("Folder name can't start or end with a space.");

        if (trimmed.StartsWith('.') || trimmed.EndsWith('.'))
            throw new ConfigValidationException("Folder name can't start or end with a dot.");

        if (ReservedName.IsMatch(trimmed))
            throw new ConfigValidationException("Folder name is reserved on Windows.");
    }

    /// <summary>
    /// Coerces an arbitrary display name (e.g. a Thunderbird calendar name, which may contain '/',
    /// control chars, leading/trailing spaces or dots, or be a reserved device name) into a folder
    /// segment that always passes <see cref="Validate"/>. Path separators and control characters become
    /// spaces; leading/trailing whitespace and dots are trimmed; an empty or reserved result falls back
    /// to <paramref name="fallback"/>. Used by discovery to synthesize folder paths so a bad calendar
    /// name can never abort the whole conversion at config validation.
    /// </summary>
    public static string Sanitize(string? name, string fallback)
    {
        var sb = new StringBuilder((name ?? string.Empty).Length);
        foreach (char ch in name ?? string.Empty)
            sb.Append(ch is '/' or '\\' || ch < 0x20 ? ' ' : ch);

        string s = sb.ToString().Trim().Trim('.').Trim();   // no leading/trailing space or dot
        return s.Length == 0 || ReservedName.IsMatch(s) ? fallback : s;
    }

    public const int MaxDepth = 32;

    /// <summary>
    /// Validates a nested folder path: 1..MaxDepth segments, each a valid folder name.
    /// Whitespace-padded segments are rejected (not trimmed), matching <see cref="Validate"/>.
    /// </summary>
    public static void ValidatePath(IReadOnlyList<string>? path)
    {
        if (path is null || path.Count == 0)
            throw new ConfigValidationException("Folder path must have at least one segment.");
        if (path.Count > MaxDepth)
            throw new ConfigValidationException(
                $"Folder path depth {path.Count} exceeds the maximum of {MaxDepth}.");
        foreach (string segment in path)
            Validate(segment); // existing per-segment rules (null/empty/whitespace/sep/control/dot/reserved)
    }
}
