// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Indexes the .msf side of the join by normalized Message-ID. Duplicate keys are removed-and-marked
/// exactly as SP3's Enrich did: a key seen more than once never matches (TryGetUnique false) and is
/// reported via IsDuplicateMsfId.
/// </summary>
internal sealed class MsfJoinIndex
{
    private readonly Dictionary<string, MsfMessage> _byId;
    private readonly HashSet<string> _dup;

    private MsfJoinIndex(Dictionary<string, MsfMessage> byId, HashSet<string> dup)
    {
        _byId = byId;
        _dup = dup;
    }

    public static MsfJoinIndex Build(MsfReadResult msf)
    {
        ArgumentNullException.ThrowIfNull(msf);
        var byId = new Dictionary<string, MsfMessage>(StringComparer.Ordinal);
        var dup = new HashSet<string>(StringComparer.Ordinal);
        foreach (MsfMessage m in msf.Messages)
        {
            string? key = MessageIdNormalizer.NormalizeForJoin(m.MessageId);
            if (key is null) continue;
            // On a duplicate, drop the first-seen entry too so _byId and _dup stay consistent (SP3 rule).
            if (!byId.TryAdd(key, m)) { byId.Remove(key); dup.Add(key); }
        }
        return new MsfJoinIndex(byId, dup);
    }

    public bool IsDuplicateMsfId(string normalizedId) => _dup.Contains(normalizedId);

    public bool TryGetUnique(string normalizedId, out MsfMessage row) => _byId.TryGetValue(normalizedId, out row!);
}
