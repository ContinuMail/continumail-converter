// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace Mail2Pst.Core.Msf;

internal static class PrefsJsEscape
{
    public static bool TryUnescape(string raw, out string result)
    {
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c != '\\') { sb.Append(c); continue; }
            i++;
            if (i >= raw.Length) { result = ""; return false; } // dangling backslash
            switch (raw[i])
            {
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 >= raw.Length) { result = ""; return false; }
                    if (!ushort.TryParse(raw.Substring(i + 1, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out ushort code)) { result = ""; return false; }
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: result = ""; return false; // unknown escape -> skip line
            }
        }
        result = sb.ToString();
        return true;
    }
}
