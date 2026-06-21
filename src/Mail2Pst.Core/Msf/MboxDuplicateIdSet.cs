// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// The set of normalized Message-IDs occurring more than once on the mbox side. Built from a
/// materialized list (batch Enrich) or from the streaming pre-pass scanner's counts.
/// </summary>
internal sealed class MboxDuplicateIdSet
{
    private readonly HashSet<string> _dup;

    private MboxDuplicateIdSet(HashSet<string> dup) => _dup = dup;

    public bool Contains(string normalizedId) => _dup.Contains(normalizedId);

    public static MboxDuplicateIdSet FromMessages(IReadOnlyList<MailMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (MailMessage mail in messages)
        {
            string? key = MessageIdNormalizer.NormalizeForJoin(mail.MessageId);
            if (key is not null) counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return FromCounts(counts);
    }

    public static MboxDuplicateIdSet FromCounts(IReadOnlyDictionary<string, int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var dup = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> kv in counts)
            if (kv.Value > 1) dup.Add(kv.Key);
        return new MboxDuplicateIdSet(dup);
    }
}
