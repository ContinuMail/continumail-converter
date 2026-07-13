// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

    static MimeReconstructor()
    {
        // Legacy Windows code pages (1250–1258, etc.) are not in .NET's built-in encoding provider. Register
        // the CodePages provider so Encoding.GetEncoding(1252) etc. resolve during a reverse export. Idempotent
        // — registering the same provider twice is harmless (the forward .msf reader may also have registered it).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public MimeReconstructor(Action<string>? onWarning = null) => _onWarning = onWarning;

    public MimeMessage Reconstruct(PstMailMessage message)
    {
        var mime = new MimeMessage();
        ApplyIdentityHeaders(mime, message);   // discrete props WIN
        mime.Body = WrapWithAttachments(BuildBody(message), message);   // regenerated MIME-structural tree
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

    // Regenerate the body tree from discrete props. Both bodies -> multipart/alternative (least-rich first,
    // per RFC 2046); one body -> that part; neither -> an empty text/plain so subject/headers still round-trip.
    private MimeEntity BuildBody(PstMailMessage m)
    {
        TextPart? plain = m.PlainBody is not null ? new TextPart("plain") { Text = m.PlainBody } : null;

        // A present-but-empty HtmlBody (zero-length byte[]) is a PRESENT html body (the forward writer wrote an
        // empty PidTagHtml), so treat `not null` as present — only a null HtmlBody is absent.
        TextPart? html = null;
        if (m.HtmlBody is not null)
            html = new TextPart("html") { Text = DecodeHtml(m.HtmlBody, m.InternetCodepage) };

        if (plain is not null && html is not null)
        {
            var alt = new MultipartAlternative();
            alt.Add(plain);   // least-rich first
            alt.Add(html);
            return alt;
        }
        if (html is not null) return html;
        if (plain is not null) return plain;

        return new TextPart("plain") { Text = string.Empty };
    }

    private string DecodeHtml(byte[] bytes, int? codepage)
    {
        // ResolveEncoding returns a STRICT encoder (DecoderFallback.ExceptionFallback), so genuinely invalid
        // bytes throw DecoderFallbackException here instead of silently becoming U+FFFD — that is what makes the
        // fallback-and-warn path live rather than dead code. On failure, decode tolerantly as UTF-8.
        Encoding enc = ResolveEncoding(codepage);
        try { return enc.GetString(bytes); }
        catch (DecoderFallbackException)
        {
            _onWarning?.Invoke($"failed to decode HTML body with code page {codepage}; falling back to UTF-8.");
            return TolerantUtf8.GetString(bytes);
        }
    }

    // InternetCodepage informs the decode charset. null/65001 -> STRICT UTF-8 fast path (the forward writer's
    // HTML encoding). Anything else is resolved via Encoding.GetEncoding with STRICT encoder/decoder fallbacks —
    // including legacy Windows code pages (1250–1258, etc.), which work because the static ctor registered the
    // CodePages provider. An unknown/unsupported code page (GetEncoding throws) falls back to strict UTF-8 with a
    // warning; invalid bytes under the chosen page are handled by DecodeHtml's DecoderFallbackException catch.
    private Encoding ResolveEncoding(int? codepage)
    {
        if (codepage is null or 65001) return StrictUtf8;
        try { return Encoding.GetEncoding(codepage.Value, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)   // unknown/unsupported code page
        {
            _onWarning?.Invoke($"unknown/unsupported InternetCodepage {codepage}; decoding HTML body as UTF-8.");
            return StrictUtf8;
        }
    }

    // STRICT: throwOnInvalidBytes=true so invalid input surfaces as DecoderFallbackException (see DecodeHtml).
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    // TOLERANT: the post-failure fallback — invalid bytes become U+FFFD ('�') rather than throwing again.
    private static readonly Encoding TolerantUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    // Inline (CID) parts -> multipart/related around the body (inverting the forward inline-CID handling);
    // non-inline parts -> multipart/mixed around whatever the body/related is. Attachment bytes are read
    // SYNCHRONOUSLY here (the PstAttachment.OpenRead closure is valid only during this message's enumeration).
    private MimeEntity WrapWithAttachments(MimeEntity body, PstMailMessage m)
    {
        var inline = new List<PstAttachment>();
        var regular = new List<PstAttachment>();
        foreach (PstAttachment a in m.Attachments)
            (a.IsInline ? inline : regular).Add(a);

        MimeEntity bodyOrRelated = body;
        if (inline.Count > 0)
        {
            // Set Root in the initializer on an EMPTY collection: in MimeKit 4.17.0 the Root setter Insert(0,·)s
            // when it can't resolve an existing root, so `related.Add(body); related.Root = body;` would insert
            // the body TWICE. Assigning Root first (empty collection) adds it once as the root document; the
            // inline parts are appended after.
            var related = new MultipartRelated { Root = body };
            foreach (PstAttachment a in inline)
                related.Add(BuildPart(a, inlinePart: true));
            bodyOrRelated = related;
        }

        if (regular.Count == 0) return bodyOrRelated;

        var mixed = new Multipart("mixed");
        mixed.Add(bodyOrRelated);
        foreach (PstAttachment a in regular)
            mixed.Add(BuildPart(a, inlinePart: false));
        return mixed;
    }

    // Instance (not static) so it can warn on invalid attachment metadata. One detached MemoryStream that
    // MimeContent takes ownership of — NO extra byte[]/MemoryStream copy. On any failure before the part owns
    // the stream, dispose it so it can't leak.
    private MimePart BuildPart(PstAttachment a, bool inlinePart)
    {
        MemoryStream content = a.Length is > 0 and <= int.MaxValue ? new MemoryStream((int)a.Length) : new MemoryStream();
        try
        {
            using (Stream src = a.OpenRead()) src.CopyTo(content);   // read synchronously; never capture the closure
            content.Position = 0;

            ContentType ct;
            if (ContentType.TryParse(a.ContentType, out ContentType? parsed) && parsed is not null)
                ct = parsed;
            else
            {
                ct = new ContentType("application", "octet-stream");
                _onWarning?.Invoke($"attachment '{a.FileName}' has an invalid MIME type '{a.ContentType}'; using application/octet-stream.");
            }

            var part = new MimePart(ct)
            {
                Content = new MimeContent(content),
                ContentTransferEncoding = ContentEncoding.Base64,
                ContentDisposition = new ContentDisposition(inlinePart ? ContentDisposition.Inline : ContentDisposition.Attachment) { FileName = a.FileName },
            };
            part.ContentType.Name = a.FileName;
            if (inlinePart && !string.IsNullOrWhiteSpace(a.ContentId))
                part.ContentId = StripAngle(a.ContentId);
            if (!string.IsNullOrWhiteSpace(a.ContentLocation))
            {
                if (Uri.TryCreate(a.ContentLocation, UriKind.RelativeOrAbsolute, out Uri? location))
                    part.ContentLocation = location;
                else
                    _onWarning?.Invoke($"attachment '{a.FileName}' has an invalid Content-Location '{a.ContentLocation}'; omitting it.");
            }
            return part;
        }
        catch { content.Dispose(); throw; }
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
