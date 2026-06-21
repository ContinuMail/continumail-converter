// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Msf;

public sealed class MsfEnrichmentOptions
{
    public IMsfTagResolver TagResolver { get; set; } = new DefaultMsfTagResolver();
    public JunkHandlingMode JunkHandling { get; set; } = JunkHandlingMode.Off;
    public bool DropExpunged { get; set; }
}

public sealed class MsfEnrichmentResult
{
    public int Matched { get; set; }
    public int SkippedMissingId { get; set; }
    public int SkippedDuplicateId { get; set; }
    public int NoMsfMatch { get; set; }
    public int ExpungedMatched { get; set; }
    public int ExpungedDropped { get; set; }
}

/// <summary>
/// Joins .msf rows to MailMessages by uniquely-resolvable normalized Message-ID and applies the
/// .msf's authoritative flags, junk, and tags. Pure; no I/O or file pairing (SP4b wires that).
/// </summary>
public static class MsfEnricher
{
    public static MsfEnrichmentResult Enrich(
        IReadOnlyList<MailMessage> messages, MsfReadResult msf, MsfEnrichmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(msf);
        ArgumentNullException.ThrowIfNull(options);

        var result = new MsfEnrichmentResult();
        MsfJoinIndex index = MsfJoinIndex.Build(msf);
        MboxDuplicateIdSet mboxDuplicates = MboxDuplicateIdSet.FromMessages(messages);
        // Batch mode keeps every message: the per-message keep return (used by the streaming
        // path to drop expunged messages) is intentionally discarded here.
        foreach (MailMessage mail in messages)
            _ = TryApply(mail, index, mboxDuplicates, options, result);
        return result;
    }

    /// <summary>
    /// Applies one message. Increments EXACTLY ONE of matched/skippedMissingId/skippedDuplicateId/
    /// noMsfMatch on <paramref name="result"/>; mutates <paramref name="mail"/> only on a unique match.
    /// Returns false ONLY for a unique match where the row is expunged and options.DropExpunged is set
    /// (the message should be dropped, not written); true in every other case.
    /// </summary>
    internal static bool TryApply(
        MailMessage mail, MsfJoinIndex index, MboxDuplicateIdSet mboxDuplicates,
        MsfEnrichmentOptions options, MsfEnrichmentResult result)
    {
        string? key = MessageIdNormalizer.NormalizeForJoin(mail.MessageId);
        if (key is null) { result.SkippedMissingId++; return true; }
        if (mboxDuplicates.Contains(key) || index.IsDuplicateMsfId(key)) { result.SkippedDuplicateId++; return true; }
        if (!index.TryGetUnique(key, out MsfMessage row)) { result.NoMsfMatch++; return true; }

        // .msf wins for scalar flags.
        mail.IsRead = row.IsRead;
        mail.IsReplied = row.IsReplied;
        mail.IsForwarded = row.IsForwarded;
        mail.IsFlagged = row.IsFlagged;
        mail.IsJunk = row.IsJunk;

        var resolved = new List<string>(options.TagResolver.Resolve(row.Keywords));
        if (options.JunkHandling == JunkHandlingMode.Category && row.IsJunk) resolved.Add("Junk");
        MergeCategories(mail.Categories, resolved);

        result.Matched++;
        if (row.IsExpunged)
        {
            result.ExpungedMatched++;
            if (options.DropExpunged)
            {
                result.ExpungedDropped++;
                return false;
            }
        }
        return true;
    }

    // Append new categories to existing, dedupe ordinal, preserve first occurrence; never remove.
    private static void MergeCategories(List<string> existing, IReadOnlyList<string> additions)
    {
        var seen = new HashSet<string>(existing, StringComparer.Ordinal);
        foreach (string c in additions) if (seen.Add(c)) existing.Add(c);
    }
}
