// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mail2Pst.Core.Msf;

/// <summary>Immutable typed view of one Thunderbird msgs-table row. A faithful mirror — no reconciliation.</summary>
public sealed class MsfMessage
{
    public string RowId { get; }
    public MsfMessageFlags RawFlags { get; }
    public int? JunkScore { get; }
    public IReadOnlyList<string> Keywords { get; }
    public int Label { get; }
    public long? MsgOffset { get; }
    public long? StoreToken { get; }
    /// <summary>The message's mbox byte offset Thunderbird records: storeToken (modern mbox store)
    /// preferred, msgOffset (legacy Berkeley) as fallback. Null if neither is a usable number.</summary>
    public long? LiveOffset => StoreToken ?? MsgOffset;
    /// <summary>Raw Thunderbird nsMsgPriority (0=notSet,1=none,2=lowest,3=low,4=normal,5=high,6=highest); null if absent. Faithful mirror — mapping to importance is the enricher's job.</summary>
    public int? Priority { get; }
    public string? MessageId { get; }

    public MsfMessage(
        string rowId,
        MsfMessageFlags rawFlags,
        int? junkScore,
        IReadOnlyList<string> keywords,
        int label,
        long? msgOffset,
        long? storeToken,
        int? priority,
        string? messageId)
    {
        RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
        if (keywords is null) throw new ArgumentNullException(nameof(keywords));
        RawFlags = rawFlags;
        JunkScore = junkScore;
        Keywords = new ReadOnlyCollection<string>(keywords.ToList()); // defensive copy
        Label = label;
        MsgOffset = msgOffset;
        StoreToken = storeToken;
        Priority = priority;
        MessageId = messageId;
    }

    public bool IsRead      => (RawFlags & MsfMessageFlags.Read) != 0;
    public bool IsReplied   => (RawFlags & MsfMessageFlags.Replied) != 0;
    public bool IsForwarded => (RawFlags & MsfMessageFlags.Forwarded) != 0;
    public bool IsFlagged   => (RawFlags & MsfMessageFlags.Marked) != 0;
    public bool IsExpunged  => (RawFlags & MsfMessageFlags.Expunged) != 0;
    public bool IsJunk      => JunkScore >= 50;
}
