// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.Msf;

/// <summary>
/// Canonicalizes a Message-ID for joining: trims, treats blank as absent, and
/// ensures angle brackets. Shared by the MIME mapper and the .msf joiner so both
/// sides compare with identical normalization. Comparison is ordinal.
/// </summary>
public static class MessageIdNormalizer
{
    public static string? NormalizeForJoin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("<") && value.EndsWith(">")) return value;
        return $"<{value}>";
    }
}
