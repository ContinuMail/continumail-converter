// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

namespace Mail2Pst.Core.Parsing.Mbox;

/// <summary>One result from the mbox boundary engine: either a materialized message buffer
/// (or <c>null</c> in count/offset mode) OR an "oversized" marker for a message that exceeded the
/// max-message-size cap and was skipped without being fully buffered.</summary>
internal readonly struct MessageChunk
{
    public SpillableMessageBuffer? Buffer { get; }
    public bool IsOversized { get; }
    /// <summary>Approximate content bytes seen before the message was cut off (for the skip message).</summary>
    public long OversizedBytes { get; }

    private MessageChunk(SpillableMessageBuffer? buffer, bool oversized, long bytes)
    {
        Buffer = buffer;
        IsOversized = oversized;
        OversizedBytes = bytes;
    }

    public static MessageChunk Ok(SpillableMessageBuffer? buffer) => new(buffer, false, 0);
    public static MessageChunk Oversized(long bytes) => new(null, true, bytes);
}
