// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Text;
using Mail2Pst.Core.Config;

namespace Mail2Pst.Core.Reverse;

/// <summary>
/// Coerces a PST folder name into a filesystem-safe Thunderbird mbox file/dir base name. Reuses the forward
/// <see cref="FolderNameValidator.Sanitize"/> (path separators -> space, control chars -> space, trims
/// leading/trailing dots+spaces, Windows reserved device names -> fallback), then additionally replaces the
/// remaining Windows-illegal filename characters (<c>: * ? " &lt; &gt; |</c>) with '_'. A name that reduces to
/// nothing falls back to <c>"Folder"</c>. NOTE: long-name hashing (Thunderbird's NS_MsgHashIfNecessary, which
/// truncates+hashes names over the OS limit) is NOT implemented in v1 — see the plan self-review.
/// </summary>
public static class MboxFolderNameSanitizer
{
    private const string Fallback = "Folder";

    public static string ToFileName(string? folderName)
    {
        // Forward helper first: handles '/'\\, control chars, dots/spaces trimming, and reserved names.
        string s = FolderNameValidator.Sanitize(folderName, Fallback);

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(IsFileNameIllegal(c) ? '_' : c);

        // Replacing illegal chars cannot introduce a trailing dot/space (we insert '_'), but re-trim
        // defensively so the result is always a legal Windows path segment.
        string result = sb.ToString().Trim().TrimEnd('.').Trim();
        return result.Length == 0 ? Fallback : result;
    }

    // The forward Sanitize already removes '/'\\ and control chars, but keep them here so ToFileName is
    // correct on its own terms regardless of the upstream helper's exact rule set.
    private static bool IsFileNameIllegal(char c) =>
        c is ':' or '*' or '?' or '"' or '<' or '>' or '|' or '/' or '\\' || c < 0x20;
}
