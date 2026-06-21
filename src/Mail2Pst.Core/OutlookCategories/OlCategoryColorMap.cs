// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Globalization;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// Maps a #RRGGBB hex to the nearest Outlook category colour by Euclidean RGB distance. The returned
/// integer is the OlCategoryColor enum value (1=Red .. 25=DarkMaroon; 0=None is never returned). RGBs are
/// the canonical MS-OXOCFG §2.2.5.1.1 base colours; OlCategoryColor = MS-OXOCFG index + 1.
/// </summary>
public static class OlCategoryColorMap
{
    // index 0 of this array = OlCategoryColor 1 (Red). 25 entries.
    private static readonly (byte R, byte G, byte B)[] Palette =
    {
        (214, 37, 46),  (240, 108, 21), (255, 202, 76), (255, 254, 61), (74, 182, 63),
        (64, 189, 149), (133, 154, 82), (50, 103, 184), (97, 61, 180),  (163, 78, 120),
        (196, 204, 221),(140, 156, 189),(196, 196, 196),(165, 165, 165),(28, 28, 28),
        (175, 30, 37),  (177, 79, 13),  (171, 123, 5),  (153, 148, 0),  (53, 121, 43),
        (46, 125, 100), (95, 108, 58),  (42, 81, 145),  (80, 50, 143),  (130, 55, 95),
    };

    public static bool TryNearestIndex(string hex, out int olCategoryColor)
    {
        olCategoryColor = 0;
        if (!TryParseHex(hex, out int r, out int g, out int b)) return false;

        int bestIdx = 0;
        long bestDist = long.MaxValue;
        for (int i = 0; i < Palette.Length; i++)
        {
            long dr = r - Palette[i].R, dg = g - Palette[i].G, db = b - Palette[i].B;
            long dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist) { bestDist = dist; bestIdx = i; }
        }
        olCategoryColor = bestIdx + 1; // OlCategoryColor is 1-based
        return true;
    }

    private static bool TryParseHex(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrEmpty(hex)) return false;
        string h = hex[0] == '#' ? hex.Substring(1) : hex;
        if (h.Length != 6) return false;
        return TryByte(h, 0, out r) && TryByte(h, 2, out g) && TryByte(h, 4, out b);
    }

    private static bool TryByte(string h, int pos, out int value) =>
        int.TryParse(h.AsSpan(pos, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
