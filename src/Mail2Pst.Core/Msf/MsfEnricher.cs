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
}

public sealed class MsfEnrichmentResult
{
    public int Matched { get; set; }
    public int SkippedMissingId { get; set; }
    public int SkippedDuplicateId { get; set; }
    public int NoMsfMatch { get; set; }
    public int ExpungedMatched { get; set; }
}

/// <summary>
/// Joins .msf rows to MailMessages by uniquely-resolvable normalized Message-ID and applies
/// the .msf's authoritative flags, junk, and tags. Pure; no I/O or file pairing (SP4 wires that).
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

        // Index the .msf side by normalized id; mark non-unique keys.
        var byId = new Dictionary<string, MsfMessage>(StringComparer.Ordinal);
        var dupMsf = new HashSet<string>(StringComparer.Ordinal);
        foreach (MsfMessage m in msf.Messages)
        {
            string? key = MessageIdNormalizer.NormalizeForJoin(m.MessageId);
            if (key is null) continue;
            // On a duplicate, drop the first-seen entry too so byId and dupMsf stay consistent.
            if (!byId.TryAdd(key, m)) { byId.Remove(key); dupMsf.Add(key); }
        }
        // Mark mbox-side duplicate keys too.
        var mailKeyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (MailMessage mail in messages)
        {
            string? key = MessageIdNormalizer.NormalizeForJoin(mail.MessageId);
            if (key is not null) mailKeyCounts[key] = mailKeyCounts.GetValueOrDefault(key) + 1;
        }

        foreach (MailMessage mail in messages)
        {
            string? key = MessageIdNormalizer.NormalizeForJoin(mail.MessageId);
            if (key is null) { result.SkippedMissingId++; continue; }
            if (mailKeyCounts[key] > 1 || dupMsf.Contains(key)) { result.SkippedDuplicateId++; continue; }
            if (!byId.TryGetValue(key, out MsfMessage? row)) { result.NoMsfMatch++; continue; }

            // .msf wins for scalar flags.
            mail.IsRead = row.IsRead;
            mail.IsReplied = row.IsReplied;
            mail.IsForwarded = row.IsForwarded;
            mail.IsFlagged = row.IsFlagged;
            mail.IsJunk = row.IsJunk;

            var resolved = new List<string>(options.TagResolver.Resolve(row.Keywords));
            if (options.JunkHandling == JunkHandlingMode.Category && row.IsJunk) resolved.Add("Junk");
            MergeCategories(mail.Categories, resolved);

            if (row.IsExpunged) result.ExpungedMatched++;
            result.Matched++;
        }
        return result;
    }

    // Append new categories to existing, dedupe ordinal, preserve first occurrence; never remove.
    private static void MergeCategories(List<string> existing, IReadOnlyList<string> additions)
    {
        var seen = new HashSet<string>(existing, StringComparer.Ordinal);
        foreach (string c in additions) if (seen.Add(c)) existing.Add(c);
    }
}
