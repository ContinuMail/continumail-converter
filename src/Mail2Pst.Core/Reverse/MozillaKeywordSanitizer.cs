// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Text;

namespace Mail2Pst.Core.Reverse;

/// <summary>
/// Sanitizes a PST category into a Thunderbird <c>X-Mozilla-Keys</c> keyword token. A category that is
/// already a safe, token-like key (ASCII letters/digits/underscore) is preserved verbatim; otherwise it
/// is lowercased and any run of unsupported characters (whitespace, punctuation, non-ASCII) collapses to
/// a single underscore, with leading/trailing separators trimmed. A category that yields nothing usable
/// returns <c>null</c>.
/// </summary>
public static class MozillaKeywordSanitizer
{
    /// <summary>True when <paramref name="key"/> is a non-empty run of ASCII letters, digits, or '_'.</summary>
    public static bool IsSafeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (char c in key)
            if (!IsSafeChar(c)) return false;
        return true;
    }

    /// <summary>
    /// Returns the sanitized keyword for <paramref name="category"/>, or <c>null</c> when nothing usable
    /// remains. A safe key is returned unchanged (original case preserved).
    /// </summary>
    public static string? Sanitize(string category)
    {
        if (string.IsNullOrEmpty(category)) return null;
        if (IsSafeKey(category)) return category;

        var sb = new StringBuilder(category.Length);
        foreach (char c in category)
        {
            char lower = char.ToLowerInvariant(c);
            if (IsAlphaNum(lower))
                sb.Append(lower);                                              // keep ASCII letters/digits
            else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                sb.Append('_');                                                // any run of separators -> single '_'
        }
        // Leading separators never append (guard above); trim any trailing separator.
        while (sb.Length > 0 && sb[sb.Length - 1] == '_') sb.Length--;
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static bool IsSafeChar(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';

    // Sanitize keeps ASCII letters/digits and treats everything else — including underscores in an
    // otherwise-unsafe category — as separators. Already-safe keys return before this path (preserved
    // verbatim), so multi-word categories still keep their word boundaries.
    private static bool IsAlphaNum(char c) =>
        (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
}
