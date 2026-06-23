// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Mail2Pst.Core.Mork;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Interprets a generic <see cref="MorkDocument"/> into typed Thunderbird per-message metadata.
/// Pure and in-memory: no I/O, no mbox join, no PST coupling.
/// </summary>
public static class MsfMessageReader
{
    internal const string MsgsScope = "ns:msg:db:row:scope:msgs:all";
    internal const string MsgsKind  = "ns:msg:db:table:kind:msgs";

    /// <summary>
    /// Interprets the single msgs table in <paramref name="doc"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
    /// <exception cref="MorkFormatException">The document does not contain exactly one msgs table.</exception>
    public static MsfReadResult Read(MorkDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        IReadOnlyList<MorkTable> tables = doc.GetTables(MsgsScope, MsgsKind);
        if (tables.Count != 1)
        {
            throw new MorkFormatException(
                $"Expected exactly one Thunderbird msgs table, found {tables.Count}.");
        }

        var messages = new List<MsfMessage>();
        var diagnostics = new List<MsfDiagnostic>();
        foreach (MorkRow row in tables[0].Rows.Values)
        {
            messages.Add(ReadRow(row, diagnostics));
        }

        return new MsfReadResult(messages, diagnostics);
    }

    // Diagnostic order is contractual: flags, junkscore, label, msgOffset, priority.
    private static MsfMessage ReadRow(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        MsfMessageFlags flags = ParseFlags(row, diagnostics);
        int? junkScore = ParseJunkScore(row, diagnostics);
        IReadOnlyList<string> keywords = ParseKeywords(row);
        int label = ParseLabel(row, diagnostics);
        long? msgOffset = ParseMsgOffset(row, diagnostics);
        int? priority = ParsePriority(row, diagnostics);
        string? messageId = ParseMessageId(row);

        return new MsfMessage(row.Id, flags, junkScore, keywords, label, msgOffset, priority, messageId);
    }

    // Thunderbird nsMsgPriority (decimal, like junkscore/label — not the hex `flags`). Kept verbatim
    // as a raw int; the enricher maps it to importance. Values 0-6 parse identically in hex/decimal.
    private static int? ParsePriority(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        if (!row.TryGetCell("priority", out string raw) || raw.Length == 0)
        {
            return null;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        diagnostics.Add(new MsfDiagnostic(row.Id, "priority", raw, "not a number"));
        return null;
    }

    private static string? ParseMessageId(MorkRow row)
    {
        if (!row.TryGetCell("message-id", out string raw) || raw.Length == 0)
        {
            return null;
        }
        return raw; // verbatim — SP3 normalizes both sides before matching
    }

    private static long? ParseMsgOffset(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        if (!row.TryGetCell("msgOffset", out string raw) || raw.Length == 0)
        {
            return null;
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value >= 0)
        {
            return value;
        }

        diagnostics.Add(new MsfDiagnostic(row.Id, "msgOffset", raw, "not a non-negative integer"));
        return null;
    }

    private static int ParseLabel(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        if (!row.TryGetCell("label", out string raw) || raw.Length == 0)
        {
            return 0;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value; // verbatim — no clamping (faithful mirror)
        }

        diagnostics.Add(new MsfDiagnostic(row.Id, "label", raw, "not a number"));
        return 0;
    }

    private static IReadOnlyList<string> ParseKeywords(MorkRow row)
    {
        if (!row.TryGetCell("keywords", out string raw) || raw.Length == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string token in raw.Split(' ')) // ASCII space only, by intent
        {
            if (token.Length == 0) continue;        // drop empties from consecutive/leading/trailing spaces
            if (seen.Add(token)) result.Add(token); // ordinal dedupe, first wins, order preserved
        }
        return result;
    }

    private static int? ParseJunkScore(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        if (!row.TryGetCell("junkscore", out string raw) || raw.Length == 0)
        {
            return null;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        diagnostics.Add(new MsfDiagnostic(row.Id, "junkscore", raw, "not a number"));
        return null;
    }

    private static MsfMessageFlags ParseFlags(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        if (!row.TryGetCell("flags", out string raw) || raw.Length == 0)
        {
            return MsfMessageFlags.None;
        }

        if (uint.TryParse(raw, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
        {
            return (MsfMessageFlags)value;
        }

        diagnostics.Add(new MsfDiagnostic(row.Id, "flags", raw, "not valid hex"));
        return MsfMessageFlags.None;
    }
}
