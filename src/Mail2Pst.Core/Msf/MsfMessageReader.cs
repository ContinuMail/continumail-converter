// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
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

    // Diagnostic order is contractual: flags, junkscore, label, msgOffset.
    private static MsfMessage ReadRow(MorkRow row, List<MsfDiagnostic> diagnostics)
    {
        MsfMessageFlags flags = MsfMessageFlags.None;
        int? junkScore = null;
        IReadOnlyList<string> keywords = Array.Empty<string>();
        int label = 0;
        long? msgOffset = null;
        string? messageId = null;

        return new MsfMessage(row.Id, flags, junkScore, keywords, label, msgOffset, messageId);
    }
}
