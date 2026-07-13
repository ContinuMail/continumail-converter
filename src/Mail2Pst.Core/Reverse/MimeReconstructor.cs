// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using MimeKit;

namespace Mail2Pst.Core.Reverse;

/// <summary>
/// Reconstructs a MimeKit <see cref="MimeMessage"/> from one read-back <see cref="PstMailMessage"/> — the
/// inverse of the forward <c>PstWriter</c>/<c>AttachmentWriter</c>. Discrete MAPI props win for structure and
/// display fields; MIME-structural headers are regenerated from the built body tree; identity/thread/trace
/// headers may be filled from <see cref="PstMailMessage.TransportHeaders"/> only where a discrete prop left
/// them unset. NEVER emits <c>X-Mozilla-*</c> headers — Plan 3's <c>MboxTreeWriter</c> owns those. Lossy points
/// are reported through the constructor-injected <paramref name="onWarning"/> sink (the <see cref="Reconstruct"/>
/// seam has no warning parameter).
/// </summary>
public sealed class MimeReconstructor : IMimeReconstructor
{
    private readonly Action<string>? _onWarning;

    public MimeReconstructor(Action<string>? onWarning = null) => _onWarning = onWarning;

    public MimeMessage Reconstruct(PstMailMessage message)
    {
        var mime = new MimeMessage();
        ApplyIdentityHeaders(mime, message);   // discrete props WIN
        mime.Body = BuildBody(message);        // regenerated MIME-structural tree (Tasks 2–3 extend this)
        ApplyTransportHeaders(mime, message);  // trace/thread from transport, non-contradicting only (Task 4)
        return mime;
    }

    // Identity/display headers from discrete props. These are the authoritative values; the transport-header
    // pass (Task 4) only fills what these leave unset.
    private void ApplyIdentityHeaders(MimeMessage mime, PstMailMessage m)
    {
        mime.Subject = m.Subject ?? string.Empty;

        // Sender display name (FromName) + address -> "John Doe <john@example.com>", or the bare address when
        // FromName is null. Routed through TryAddMailbox so one malformed address can't abort the whole message.
        if (!string.IsNullOrWhiteSpace(m.FromAddress))
            TryAddMailbox(mime.From, m.FromName, m.FromAddress, "from");

        foreach (PstRecipient r in m.Recipients)
        {
            if (string.IsNullOrWhiteSpace(r.Address)) continue;   // no usable address -> skipped
            switch (r.Kind)
            {
                case PstRecipientKind.To: TryAddMailbox(mime.To, r.DisplayName, r.Address, "to"); break;
                case PstRecipientKind.Cc: TryAddMailbox(mime.Cc, r.DisplayName, r.Address, "cc"); break;
                case PstRecipientKind.Bcc: TryAddMailbox(mime.Bcc, r.DisplayName, r.Address, "bcc"); break;
            }
        }

        // A message with no date genuinely has none; MimeKit forces a Date header, so pin a deterministic
        // epoch fallback rather than leaking DateTime.Now into the output.
        mime.Date = m.Date ?? DateTimeOffset.UnixEpoch;

        if (!string.IsNullOrWhiteSpace(m.MessageId))
            TrySetHeader(() => mime.MessageId = StripAngle(m.MessageId!), "Message-Id");
        if (!string.IsNullOrWhiteSpace(m.InReplyTo))
            TrySetHeader(() => mime.InReplyTo = StripAngle(m.InReplyTo!), "In-Reply-To");
        if (!string.IsNullOrWhiteSpace(m.References))
            foreach (string id in m.References!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                string cleaned = StripAngle(id);
                TrySetHeader(() => mime.References.Add(cleaned), "References");
            }
    }

    // Body/attachment tree. Task 1 handles plain-only + the empty fallback; Tasks 2–3 add HTML/alternative
    // and attachments.
    private MimeEntity BuildBody(PstMailMessage m)
    {
        if (m.PlainBody is not null)
            return new TextPart("plain") { Text = m.PlainBody };

        // No body at all: minimal empty text/plain so subject/headers still round-trip.
        return new TextPart("plain") { Text = string.Empty };
    }

    // Transport-header carry. Stub in Task 1; implemented in Task 4.
    private void ApplyTransportHeaders(MimeMessage mime, PstMailMessage m)
    {
        // no-op until Task 4
    }

    // Narrow catch: MimeKit's Message-Id/In-Reply-To/References setters reject a malformed value with
    // ArgumentException/FormatException — those are recoverable (warn + skip that one header). Anything else is a
    // real bug and must surface (repo fail-loud policy).
    private void TrySetHeader(Action set, string what)
    {
        try { set(); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            _onWarning?.Invoke($"could not set {what} header: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // A malformed/legacy non-SMTP Outlook address makes new MailboxAddress throw ArgumentException; one bad
    // address must not abort the whole message, so warn + skip it (valid addresses still land).
    private void TryAddMailbox(InternetAddressList target, string? displayName, string address, string role)
    {
        // VERIFIED (MimeKit 4.17.0 MailboxAddress.cs): the ctor sets the Address property, whose setter runs
        // TryParseAddrspec and throws MimeKit.ParseException (NOT ArgumentException) on a malformed addr-spec
        // (e.g. "not an address"); it throws ArgumentNullException only for a null address. Catch both so one
        // malformed Outlook address (non-SMTP/X.500/garbage) warns + skips instead of aborting the message.
        try { target.Add(new MailboxAddress(displayName ?? string.Empty, address)); }
        catch (Exception ex) when (ex is ParseException or ArgumentException)
        {
            _onWarning?.Invoke($"could not reconstruct {role} address '{address}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    // IDs may arrive bracketed (ContinuMail-written; NormalizeForJoin adds <>) or unbracketed (arbitrary source
    // PSTs) — strip any surrounding <> so MimeKit (which adds its own) never double-wraps.
    private static string StripAngle(string id)
    {
        id = id.Trim();
        if (id.Length >= 2 && id.StartsWith("<", StringComparison.Ordinal) && id.EndsWith(">", StringComparison.Ordinal))
            id = id.Substring(1, id.Length - 2);
        return id;
    }
}
