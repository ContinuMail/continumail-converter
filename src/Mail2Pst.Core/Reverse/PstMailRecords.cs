// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Mail2Pst.Core.Reverse;

public enum PstRecipientKind { To, Cc, Bcc }

/// <summary>One resolved recipient: address (+ optional display name) and To/Cc/Bcc kind.</summary>
public sealed record PstRecipient(string Address, string? DisplayName, PstRecipientKind Kind);

/// <summary>
/// One attachment. <see cref="OpenRead"/> is deferred and materializes the payload on demand — it MUST
/// be invoked while the source PST is still open (i.e. during enumeration of the owning message).
/// <see cref="Length"/> is null when unknown without reading (the current vendored reader).
/// </summary>
public sealed record PstAttachment(
    string FileName, string? ContentType, string? ContentId, bool IsInline,
    Func<Stream> OpenRead, long? Length);

/// <summary>Full payload of one mail message (all fields materialized except attachment bytes).</summary>
public sealed record PstMailMessage(
    string? Subject,
    string? FromAddress,
    IReadOnlyList<PstRecipient> Recipients,
    DateTimeOffset? Date,
    string? MessageId,
    string? InReplyTo,
    string? References,
    string? PlainBody,
    byte[]? HtmlBody,
    int? InternetCodepage,
    string? TransportHeaders,
    bool IsRead,
    bool IsReplied,
    bool IsForwarded,
    IReadOnlyList<string> Categories,
    IReadOnlyList<PstAttachment> Attachments)
{
    /// <summary>Sender display name (PidTagSentRepresentingName); null when absent. Paired with FromAddress.</summary>
    public string? FromName { get; init; }
}

/// <summary>One streamed item: which folder it came from, and the message.</summary>
public sealed record PstMailItem(IReadOnlyList<string> FolderPath, PstMailMessage Message);

/// <summary>A mail folder grouping (test-only convenience; the production path streams items).</summary>
public sealed record PstMailFolder(IReadOnlyList<string> Path, IReadOnlyList<PstMailMessage> Messages)
{
    public string DisplayPath => string.Join(" / ", Path);
}
