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
    /// Interprets the msgs table in <paramref name="doc"/>. Zero msgs tables means an empty
    /// Thunderbird folder (no messages stored) and yields an empty result. Two or more tables
    /// is genuinely ambiguous (we cannot pick which holds the live messages) and throws.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
    /// <exception cref="MorkFormatException">The document contains more than one msgs table.</exception>
    public static MsfReadResult Read(MorkDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        IReadOnlyList<MorkTable> tables = doc.GetTables(MsgsScope, MsgsKind);
        if (tables.Count == 0)
        {
            // Empty folder: a .msf with no msgs table is "zero messages", not a malformed file.
            return new MsfReadResult(System.Array.Empty<MsfMessage>(), System.Array.Empty<MsfDiagnostic>());
        }
        if (tables.Count > 1)
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
    // (storeToken is parsed here too but is intentionally diagnostic-free — see ParseStoreToken.)
    private static MsfMessage ReadRow(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        MsfMessageFlags flags = ParseFlags(row, diagnostics);
        int? junkScore = ParseJunkScore(row, diagnostics);
        IReadOnlyList<string> keywords = ParseKeywords(row);
        int label = ParseLabel(row, diagnostics);
        long? msgOffset = ParseMsgOffset(row, diagnostics);
        long? storeToken = ParseStoreToken(row);
        int? priority = ParsePriority(row, diagnostics);
        string? messageId = ParseMessageId(row);

        return new MsfMessage(row.Id, flags, junkScore, keywords, label, msgOffset, storeToken, priority, messageId);
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

    private static long? ParseStoreToken(MorkRow row)
    {
        if (!row.TryGetCell("storeToken", out string raw) || raw.Length == 0)
            return null;
        // A non-numeric storeToken (e.g. the maildir store, where it is a filename) is NORMAL, not a parse
        // error: yield no offset, emit NO diagnostic, and NEVER mark the .msf degraded. Per the spec
        // (must-fix 6), the live-offset filter's activation rules handle "no usable offset" safely by
        // keeping all messages. Degradation remains reserved for the .msf-structure failures
        // (MorkFormatException / the KB-003 two-tables case) the reader already raises.
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value >= 0
            ? value : null;
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
