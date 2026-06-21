// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Msf;
using MimeKit;
using MimeKit.Utils;

namespace Mail2Pst.Core.Parsing;

/// <summary>
/// Headers-only Message-ID pre-pass over an mbox. Reuses MboxParser's boundary engine (boundaries match
/// Parse exactly) and derives the SAME normalized Message-ID that MimeMessageMapper puts on
/// MailMessage.MessageId (so duplicate accounting cannot drift). No body/attachment decode.
/// </summary>
internal static class MboxMessageIdScanner
{
    /// <summary>The set of normalized Message-IDs occurring more than once in the mbox.</summary>
    internal static MboxDuplicateIdSet ScanDuplicateIds(string path)
    {
        using FileStream stream = File.OpenRead(path); // ordinary read, same as MboxParser
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (byte[] chunk in MboxParser.EnumerateRawMessages(stream))
        {
            string? id;
            try
            {
                id = ExtractNormalizedMessageId(chunk);
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                // Per-message header parse failure: treat as "no usable Message-ID" and continue —
                // the pre-pass must be no stricter than the parser, which skips such messages. A
                // file-level IOException from File.OpenRead above still propagates (no-double-report).
                continue;
            }
            if (id is not null) counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        return MboxDuplicateIdSet.FromCounts(counts);
    }

    private static string? ExtractNormalizedMessageId(byte[] messageBytes)
    {
        using var ms = new MemoryStream(messageBytes, writable: false);
        // Load ONLY the header block (MimeKit stops at the blank line) — no body/attachment decode.
        HeaderList headers = HeaderList.Load(ms);
        string? raw = headers["Message-Id"];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Parse the addr-spec the SAME way MimeMessage.MessageId does (strips <>, comments, folding),
        // then normalize identically to the mapper. This guarantees value parity.
        string? parsed = MimeUtils.EnumerateReferences(raw).FirstOrDefault();
        return MessageIdNormalizer.NormalizeForJoin(parsed ?? raw);
    }
}
