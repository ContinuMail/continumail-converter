// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>Reproduces Thunderbird's category-colour logic so we can compute the colour Thunderbird
/// shows for a category name (Thunderbird stores no default colours — it derives them).</summary>
public static class CategoryColorHasher
{
    // Palette values observed in Thunderbird 140.12.1 calViewUtils.sys.mjs (hashColor).
    // Factual compatibility data for category colour mapping; the algorithm is reproduced, not copied.
    private static readonly string[] Palette =
    {
        "#FFFFFF", "#FFCCCC", "#FFCC99", "#FFFF99", "#FFFFCC", "#99FF99", "#99FFFF", "#CCFFFF", "#CCCCFF", "#FFCCFF",
        "#CCCCCC", "#FF6666", "#FF9966", "#FFFF66", "#FFFF33", "#66FF99", "#33FFFF", "#66FFFF", "#9999FF", "#FF99FF",
        "#C0C0C0", "#FF0000", "#FF9900", "#FFCC66", "#FFFF00", "#33FF33", "#66CCCC", "#33CCFF", "#6666CC", "#CC66CC",
        "#999999", "#CC0000", "#FF6600", "#FFCC33", "#FFCC00", "#33CC00", "#00CCCC", "#3366FF", "#6633FF", "#CC33CC",
        "#666666", "#990000", "#CC6600", "#CC9933", "#999900", "#009900", "#339999", "#3333FF", "#6600CC", "#993399",
        "#333333", "#660000", "#993300", "#996633", "#666600", "#006600", "#336666", "#000099", "#333399", "#663366",
        "#000000", "#330000", "#663300", "#663333", "#333300", "#003300", "#003333", "#000066", "#330099", "#330033",
    };

    /// <summary>Thunderbird hashColor: sum the first UTF-16 code unit of each code point of (name || " "),
    /// index the 70-colour palette by (sum % 70). Reproduces JS Array.from(str, e =&gt; e.charCodeAt(0)).</summary>
    public static string HashColor(string? name)
    {
        string s = string.IsNullOrEmpty(name) ? " " : name!;
        int sum = 0;
        foreach (Rune r in s.EnumerateRunes())
            sum += char.ConvertFromUtf32(r.Value)[0]; // first UTF-16 unit: char for BMP, high surrogate otherwise
        return Palette[sum % Palette.Length];
    }

    /// <summary>Thunderbird formatStringForCSSRule: lowercase, then space -&gt; "_", [a-z0-9] kept,
    /// else "-ux" + lowercase-hex(charCode) + "-". Used to build the override-pref lookup key.</summary>
    public static string FormatStringForCSSRule(string? name)
    {
        // Exact compatibility is guaranteed for ordinary ASCII/BMP names and the verified fixtures.
        // ToLowerInvariant may diverge from JS toLowerCase() for rare Unicode case-folding edge cases;
        // category names in practice do not hit those.
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var sb = new StringBuilder(name!.Length);
        foreach (char c in name!.ToLowerInvariant())
        {
            if (c == ' ') sb.Append('_');
            else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            else sb.Append("-ux").Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append('-');
        }
        return sb.ToString();
    }
}
