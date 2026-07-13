// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using Mail2Pst.Core.Reverse;
using MimeKit;

namespace Mail2Pst.Core.Tests.Reverse;

/// <summary>Stand-in for Plan 4's MimeReconstructor: builds a minimal, deterministic text/plain message
/// from a <see cref="PstMailMessage"/>. Deliberately does NOT emit X-Mozilla-* headers (the writer owns
/// those). Content-transfer-encoding is pinned to 7bit so serialization of ASCII bodies is byte-stable
/// (no quoted-printable surprises when asserting on mboxrd 'From ' escaping).</summary>
internal sealed class FakeMimeReconstructor : IMimeReconstructor
{
    public MimeMessage Reconstruct(PstMailMessage message)
    {
        var m = new MimeMessage();
        m.From.Add(new MailboxAddress(string.Empty, message.FromAddress ?? "sender@example.com"));
        m.To.Add(new MailboxAddress(string.Empty, "recipient@example.com"));
        m.Subject = message.Subject ?? string.Empty;
        m.Date = message.Date ?? DateTimeOffset.UnixEpoch;
        if (!string.IsNullOrEmpty(message.MessageId))
            m.MessageId = message.MessageId!.Trim().TrimStart('<').TrimEnd('>');
        m.Body = new TextPart("plain")
        {
            Text = message.PlainBody ?? string.Empty,
            ContentTransferEncoding = ContentEncoding.SevenBit,
        };
        return m;
    }
}
