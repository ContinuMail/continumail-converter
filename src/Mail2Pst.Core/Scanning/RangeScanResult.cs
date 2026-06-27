// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Scanning;

/// <summary>
/// A single per-message record produced by <see cref="Mail2Pst.Core.Parsing.MboxParser.ScanRange"/>.
/// Structured (no rendered "message #N" identifier — that is assigned only when ranges are merged).
/// Mirrors the per-message data ScanRunner derives today: estimated PST size, the message date,
/// and the mapper warnings. A failed (skipped) message carries a <see cref="SkipReason"/> and
/// zero size / no date / no warnings.
/// </summary>
public sealed record RangeMessage(long EstimatedBytes, DateTimeOffset? Date, string? SkipReason, IReadOnlyList<string> Warnings)
{
    public bool IsSkipped => SkipReason is not null;
}

/// <summary>
/// The result of parsing one message-aligned byte range. <see cref="StartOffset"/> is the range's
/// start argument; <see cref="Messages"/> are exactly the messages whose start boundary fell in
/// <c>[startOffset, endOffset)</c>, in file order.
/// </summary>
public sealed record RangeScanResult(long StartOffset, IReadOnlyList<RangeMessage> Messages);
