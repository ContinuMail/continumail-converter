// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Reverse;
using MimeKit;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class MimeReconstructorTests
{
    // ---- builders --------------------------------------------------------------------------------

    private static PstMailMessage Msg(
        string? subject = "Subject",
        string? from = "sender@example.com",
        string? fromName = null,
        IReadOnlyList<PstRecipient>? recipients = null,
        DateTimeOffset? date = null,
        string? messageId = null,
        string? inReplyTo = null,
        string? references = null,
        string? plainBody = "Body text",
        byte[]? htmlBody = null,
        int? codepage = null,
        string? transportHeaders = null,
        IReadOnlyList<string>? categories = null,
        IReadOnlyList<PstAttachment>? attachments = null)
        => new PstMailMessage(
            Subject: subject, FromAddress: from,
            Recipients: recipients ?? Array.Empty<PstRecipient>(),
            Date: date, MessageId: messageId, InReplyTo: inReplyTo, References: references,
            PlainBody: plainBody, HtmlBody: htmlBody, InternetCodepage: codepage,
            TransportHeaders: transportHeaders,
            IsRead: false, IsReplied: false, IsForwarded: false,
            Categories: categories ?? Array.Empty<string>(),
            Attachments: attachments ?? Array.Empty<PstAttachment>())
        { FromName = fromName };   // init-only sender display name

    private static PstRecipient Rcpt(string address, PstRecipientKind kind, string? display = null)
        => new PstRecipient(address, display, kind);

    private static MimeReconstructor NewReconstructor(out List<string> warnings)
    {
        warnings = new List<string>();
        var captured = warnings;
        return new MimeReconstructor(captured.Add);
    }

    private static string Serialize(MimeMessage m)
    {
        using var ms = new MemoryStream();
        m.WriteTo(ms);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void AssertNoMozillaHeaders(MimeMessage m)
    {
        foreach (Header h in m.Headers)
            Assert.False(
                h.Field.StartsWith("X-Mozilla", StringComparison.OrdinalIgnoreCase),
                $"reconstructor must not emit '{h.Field}' (the mbox writer owns X-Mozilla-* headers)");
    }

    // ---- identity/header tests ---------------------------------------------------------------------

    [Fact]
    public void Reconstruct_DiscreteProps_SetsIdentityAndDisplayHeaders()
    {
        var msg = Msg(
            subject: "Hello world",
            from: "alice@example.com",
            recipients: new[]
            {
                Rcpt("bob@example.com", PstRecipientKind.To, "Bob"),
                Rcpt("carol@example.com", PstRecipientKind.Cc),
                Rcpt("dave@example.com", PstRecipientKind.Bcc),
            },
            date: new DateTimeOffset(2021, 6, 1, 12, 0, 0, TimeSpan.Zero),
            messageId: "id-1@example.com",           // unbracketed here; both forms covered by the Theory below
            inReplyTo: "parent@example.com",
            references: "root@example.com parent@example.com");

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        Assert.Equal("Hello world", m.Subject);
        Assert.Equal("alice@example.com", Assert.IsType<MailboxAddress>(m.From[0]).Address);
        Assert.Equal("bob@example.com", Assert.IsType<MailboxAddress>(m.To[0]).Address);
        Assert.Equal("Bob", Assert.IsType<MailboxAddress>(m.To[0]).Name);
        Assert.Equal("carol@example.com", Assert.IsType<MailboxAddress>(m.Cc[0]).Address);
        Assert.Equal("dave@example.com", Assert.IsType<MailboxAddress>(m.Bcc[0]).Address);
        Assert.Equal(new DateTimeOffset(2021, 6, 1, 12, 0, 0, TimeSpan.Zero), m.Date);
        Assert.Equal("id-1@example.com", m.MessageId);          // MimeKit stores id without <>
        Assert.Equal("parent@example.com", m.InReplyTo);
        Assert.Equal(new[] { "root@example.com", "parent@example.com" }, m.References.ToArray());
        AssertNoMozillaHeaders(m);
    }

    [Theory]
    [InlineData("id-1@example.com")]        // arbitrary source PST may surface an unbracketed id
    [InlineData("<id-1@example.com>")]      // ContinuMail-written ids are bracketed (NormalizeForJoin adds <>)
    public void Reconstruct_MessageId_AcceptsBracketedAndUnbracketed(string id)
        => Assert.Equal("id-1@example.com", new MimeReconstructor().Reconstruct(Msg(messageId: id)).MessageId);

    [Fact]
    public void Reconstruct_FromName_Present_SetsDisplayNameOnFrom()
    {
        MimeMessage m = new MimeReconstructor().Reconstruct(
            Msg(from: "john@example.com", fromName: "John Doe"));

        MailboxAddress from = Assert.IsType<MailboxAddress>(m.From[0]);
        Assert.Equal("John Doe", from.Name);            // display name preserved -> "John Doe <john@example.com>"
        Assert.Equal("john@example.com", from.Address);
    }

    [Fact]
    public void Reconstruct_FromName_Null_LeavesBareAddress()
    {
        MimeMessage m = new MimeReconstructor().Reconstruct(
            Msg(from: "john@example.com", fromName: null));

        MailboxAddress from = Assert.IsType<MailboxAddress>(m.From[0]);
        Assert.Equal(string.Empty, from.Name);          // no display name -> bare address
        Assert.Equal("john@example.com", from.Address);
    }

    [Fact]
    public void Reconstruct_MalformedRecipientAddress_SkipsItWithWarning_KeepsValidOnes()
    {
        MimeReconstructor rec = NewReconstructor(out List<string> warnings);
        var msg = Msg(recipients: new[]
        {
            Rcpt("not an address", PstRecipientKind.To),      // malformed -> skipped + warned
            Rcpt("valid@example.com", PstRecipientKind.To),   // valid -> still lands
        });

        MimeMessage m = rec.Reconstruct(msg);                 // must NOT throw

        Assert.Contains(m.To.Mailboxes, mb => mb.Address == "valid@example.com");
        Assert.Contains(warnings, w => w.Contains("could not reconstruct to address"));
    }

    [Fact]
    public void Reconstruct_PlainOnly_ProducesTextPlainBody()
    {
        MimeMessage m = new MimeReconstructor().Reconstruct(Msg(plainBody: "Just plain text", htmlBody: null));

        TextPart body = Assert.IsType<TextPart>(m.Body);
        Assert.True(body.ContentType.IsMimeType("text", "plain"));
        Assert.Equal("Just plain text", body.Text);
    }

    [Fact]
    public void Reconstruct_NeverEmitsXMozillaHeaders()
        => AssertNoMozillaHeaders(new MimeReconstructor().Reconstruct(Msg()));

    [Fact]
    public void Reconstruct_EmptyMessage_ProducesMinimalTextPlain()
    {
        // No usable discrete props and no body at all: still round-trips as an empty text/plain part.
        var msg = Msg(subject: null, from: null, plainBody: null, htmlBody: null);
        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        TextPart body = Assert.IsType<TextPart>(m.Body);
        Assert.True(body.ContentType.IsMimeType("text", "plain"));
        Assert.Equal(string.Empty, body.Text);
        Assert.Equal(string.Empty, m.Subject);
    }

    // ---- body tests ----------------------------------------------------------------------------------

    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Reconstruct_BothBodies_ProducesMultipartAlternative_PlainThenHtml()
    {
        var msg = Msg(plainBody: "plain version", htmlBody: Utf8("<p>html version</p>"), codepage: 65001);
        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        MultipartAlternative alt = Assert.IsType<MultipartAlternative>(m.Body);
        Assert.Equal(2, alt.Count);
        Assert.True(((TextPart)alt[0]).ContentType.IsMimeType("text", "plain"));   // least-rich first
        Assert.True(((TextPart)alt[1]).ContentType.IsMimeType("text", "html"));
        Assert.Equal("plain version", ((TextPart)alt[0]).Text);
        Assert.Contains("html version", ((TextPart)alt[1]).Text);
    }

    [Fact]
    public void Reconstruct_HtmlOnly_ProducesTextHtml()
    {
        MimeMessage m = new MimeReconstructor().Reconstruct(
            Msg(plainBody: null, htmlBody: Utf8("<p>only html</p>"), codepage: 65001));

        TextPart body = Assert.IsType<TextPart>(m.Body);
        Assert.True(body.ContentType.IsMimeType("text", "html"));
        Assert.Contains("only html", body.Text);
    }

    [Fact]
    public void Reconstruct_EmptyHtmlProperty_ProducesEmptyTextHtml()
    {
        // A present-but-empty PidTagHtml is a PRESENT html body, not an absent one -> empty text/html, not a
        // text/plain fallback.
        MimeMessage m = new MimeReconstructor().Reconstruct(
            Msg(plainBody: null, htmlBody: Array.Empty<byte>(), codepage: 65001));

        TextPart body = Assert.IsType<TextPart>(m.Body);
        Assert.True(body.IsHtml);
        Assert.Equal(string.Empty, body.Text);
    }

    [Fact]
    public void Reconstruct_HtmlBytes_DecodedAsUtf8_ByDefault()
    {
        // Non-ASCII content proves the bytes are decoded (not mangled). Forward writer always writes UTF-8.
        MimeMessage m = new MimeReconstructor().Reconstruct(
            Msg(plainBody: null, htmlBody: Utf8("<p>café — naïve</p>"), codepage: 65001));
        Assert.Contains("café — naïve", ((TextPart)m.Body).Text);
    }

    [Fact]
    public void Reconstruct_LegacyCodepage1252_DecodesToCorrectUnicode()
    {
        // Windows-1252 byte 0xE9 = 'é' and 0x93/0x94 = curly double quotes. In UTF-8 these are invalid/mangled,
        // so a correct result proves the 1252 provider decode ran (not a UTF-8 fallback). Requires the
        // CodePages provider the static ctor registers.
        byte[] cp1252 = { 0x93, (byte)'c', (byte)'a', (byte)'f', 0xE9, 0x94 };   // "café" in curly quotes
        MimeMessage m = new MimeReconstructor().Reconstruct(
            Msg(plainBody: null, htmlBody: cp1252, codepage: 1252));

        string text = ((TextPart)m.Body).Text;
        Assert.Contains("café", text);            // 0xE9 -> é
        Assert.Contains("“", text);          // 0x93 -> left double quotation mark
        Assert.Contains("”", text);          // 0x94 -> right double quotation mark
    }

    [Fact]
    public void Reconstruct_InvalidUtf8Bytes_FallBackTolerantly_AndWarn()
    {
        MimeReconstructor rec = NewReconstructor(out List<string> warnings);
        // {0xC3,0x28} is an invalid UTF-8 sequence. Under the STRICT UTF-8 decoder it throws
        // DecoderFallbackException; DecodeHtml then decodes tolerantly (invalid -> U+FFFD '�') and warns.
        MimeMessage m = rec.Reconstruct(Msg(plainBody: null, htmlBody: new byte[] { 0xC3, 0x28 }, codepage: 65001));

        Assert.Contains("�", ((TextPart)m.Body).Text);     // '�' replacement char
        Assert.Contains(warnings, w => w.Contains("failed to decode HTML body"));
    }

    [Fact]
    public void Reconstruct_UnknownCodepage_FallsBackToUtf8_AndWarns()
    {
        MimeReconstructor rec = NewReconstructor(out List<string> warnings);
        // 999999 is not a real code page -> Encoding.GetEncoding throws -> UTF-8 fallback + warning.
        MimeMessage m = rec.Reconstruct(Msg(plainBody: null, htmlBody: Utf8("<p>hi</p>"), codepage: 999999));

        Assert.Contains("hi", ((TextPart)m.Body).Text);
        Assert.Contains(warnings, w => w.Contains("999999") && w.Contains("UTF-8", StringComparison.OrdinalIgnoreCase));
    }

    // ---- attachment tests ------------------------------------------------------------------------

    // In-memory attachment; `read` flips true when the closure is invoked, proving synchronous reads.
    private static PstAttachment Att(
        string fileName, string? contentType, string? contentId, bool inline, byte[] bytes,
        Action? onOpen = null, string? contentLocation = null)
        => new PstAttachment(
            fileName, contentType, contentId, inline,
            OpenRead: () => { onOpen?.Invoke(); return new MemoryStream(bytes, writable: false); },
            Length: bytes.Length)
        { ContentLocation = contentLocation };   // init-only PidTagAttachContentLocation

    [Fact]
    public void Reconstruct_NonInlineAttachment_ProducesMultipartMixed_WithExactBytes()
    {
        byte[] payload = { 1, 2, 3, 4, 5, 42, 200, 255 };
        var msg = Msg(
            plainBody: "see attachment",
            attachments: new[] { Att("data.bin", "application/octet-stream", null, inline: false, payload) });

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        Multipart mixed = Assert.IsType<Multipart>(m.Body);
        Assert.True(mixed.ContentType.IsMimeType("multipart", "mixed"));
        Assert.True(mixed[0].ContentType.IsMimeType("text", "plain"));           // body first
        MimePart att = Assert.IsType<MimePart>(mixed[1]);
        Assert.Equal("data.bin", att.FileName);
        Assert.Equal(ContentDisposition.Attachment, att.ContentDisposition!.Disposition);

        using var got = new MemoryStream();
        att.Content.DecodeTo(got);
        Assert.Equal(payload, got.ToArray());
    }

    [Fact]
    public void Reconstruct_InlineCidAttachment_ProducesMultipartRelated_WithContentId()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47 };
        var msg = Msg(
            plainBody: null, htmlBody: Utf8("<img src=\"cid:img1@example\">"), codepage: 65001,
            attachments: new[] { Att("logo.png", "image/png", "img1@example", inline: true, png) });

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        MultipartRelated related = Assert.IsType<MultipartRelated>(m.Body);
        Assert.Equal(2, related.Count);                                          // root + one inline, no duplicate
        Assert.Same(related[0], related.Root);                                   // body is the root, added once
        Assert.True(related.Root.ContentType.IsMimeType("text", "html"));        // root document is the html
        MimePart inline = related.OfType<MimePart>().Single(p => p.ContentId is not null);
        Assert.Equal("img1@example", inline.ContentId);                          // matches the cid: reference
        Assert.Equal(ContentDisposition.Inline, inline.ContentDisposition!.Disposition);
    }

    [Fact]
    public void Reconstruct_AttachmentContentLocation_IsRestored()
    {
        var msg = Msg(
            plainBody: null, htmlBody: Utf8("<img src=\"img/logo.png\">"), codepage: 65001,
            attachments: new[]
            {
                Att("logo.png", "image/png", "img1@example", inline: true, new byte[] { 1 },
                    contentLocation: "img/logo.png"),
            });

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        MultipartRelated related = Assert.IsType<MultipartRelated>(m.Body);
        Assert.Equal(2, related.Count);                                          // root + one inline, no duplicate
        Assert.Same(related[0], related.Root);
        MimePart inline = related.OfType<MimePart>().Single(p => p.ContentId is not null);
        Assert.NotNull(inline.ContentLocation);
        Assert.Equal("img/logo.png", inline.ContentLocation!.ToString());
    }

    [Fact]
    public void Reconstruct_ReadsAttachmentBytesSynchronously_DuringReconstruct()
    {
        bool read = false;
        var msg = Msg(attachments: new[]
        {
            Att("f.bin", "application/octet-stream", null, inline: false, new byte[] { 9 }, onOpen: () => read = true),
        });

        new MimeReconstructor().Reconstruct(msg);
        Assert.True(read, "attachment OpenRead must be invoked synchronously during Reconstruct");
    }

    [Fact]
    public void Reconstruct_InlineAndRegular_NestsRelatedInsideMixed()
    {
        var msg = Msg(
            plainBody: "body", htmlBody: Utf8("<img src=\"cid:c@x\">"), codepage: 65001,
            attachments: new[]
            {
                Att("logo.png", "image/png", "c@x", inline: true, new byte[] { 1 }),
                Att("report.pdf", "application/pdf", null, inline: false, new byte[] { 2 }),
            });

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        Multipart mixed = Assert.IsType<Multipart>(m.Body);
        Assert.True(mixed.ContentType.IsMimeType("multipart", "mixed"));
        MultipartRelated related = Assert.IsType<MultipartRelated>(mixed[0]);     // related nested inside mixed
        Assert.Equal(2, related.Count);                                          // root + one inline, no duplicate
        Assert.Equal("report.pdf", Assert.IsType<MimePart>(mixed[1]).FileName);
        AssertNoMozillaHeaders(m);
    }

    // ---- transport-header tests ------------------------------------------------------------------

    private const string TransportBlock =
        "Message-ID: <transport-id@example.com>\r\n" +
        "In-Reply-To: <transport-parent@example.com>\r\n" +
        "References: <transport-root@example.com>\r\n" +
        "Received: from mx1.example.com by mx2.example.com; Tue, 01 Jun 2021 12:00:00 +0000\r\n" +
        "X-Mozilla-Status: 1234\r\n" +                       // must NOT be carried (writer owns it)
        "Content-Type: text/plain; boundary=\"STALE-BOGUS-BOUNDARY\"\r\n" +   // must NOT be copied
        "Content-Transfer-Encoding: x-bogus\r\n" +           // structural: must NOT be copied
        "Content-Disposition: attachment; filename=\"stale.txt\"\r\n" +       // structural: must NOT be copied
        "MIME-Version: 9.9\r\n";                             // must be regenerated, not copied

    [Fact]
    public void Reconstruct_TransportHeaders_RegeneratesStructuralHeaders_NotCopied()
    {
        // Both bodies force a multipart/alternative; NONE of the stale structural headers may appear — they are
        // regenerated from the real body/attachment tree, never copied from the transport block.
        var msg = Msg(
            messageId: null, plainBody: "p", htmlBody: Utf8("<p>h</p>"), codepage: 65001,
            transportHeaders: TransportBlock);

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);
        string wire = Serialize(m);

        Assert.IsType<MultipartAlternative>(m.Body);
        Assert.DoesNotContain("STALE-BOGUS-BOUNDARY", wire);          // stale boundary dropped
        Assert.DoesNotContain("x-bogus", wire);                      // stale CTE dropped
        Assert.DoesNotContain("stale.txt", wire);                    // stale Content-Disposition dropped
        Assert.Contains("multipart/alternative", wire);              // fresh structural Content-Type
        Assert.Contains("MIME-Version: 1.0", wire);                  // regenerated, not the bogus 9.9
        Assert.DoesNotContain("MIME-Version: 9.9", wire);
    }

    [Fact]
    public void Reconstruct_TransportHeaders_CarryTraceAndThread_WhenDiscreteAbsent()
    {
        // No discrete Message-ID/In-Reply-To/References -> filled from the transport block; Received carried.
        var msg = Msg(messageId: null, inReplyTo: null, references: null, transportHeaders: TransportBlock);

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        Assert.Equal("transport-id@example.com", m.MessageId);
        Assert.Equal("transport-parent@example.com", m.InReplyTo);
        Assert.Contains("transport-root@example.com", m.References);
        Assert.Contains(m.Headers, h => h.Id == HeaderId.Received);
        AssertNoMozillaHeaders(m);                                    // X-Mozilla-Status NOT carried
    }

    [Fact]
    public void Reconstruct_DiscreteProps_WinOverTransportHeaders()
    {
        // Discrete Message-ID is present -> the transport Message-ID must be ignored (conflict rule).
        var msg = Msg(messageId: "discrete-id@example.com", transportHeaders: TransportBlock);

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        Assert.Equal("discrete-id@example.com", m.MessageId);
        Assert.Single(m.Headers, h => h.Id == HeaderId.MessageId);    // no duplicate Message-ID
    }

    [Fact]
    public void Reconstruct_TransportReceivedHeader_RoundTripsUnchanged()
    {
        const string received = "from mx1.example.com by mx2.example.com; Tue, 01 Jun 2021 12:00:00 +0000";
        var msg = Msg(transportHeaders: "Received: " + received + "\r\n");

        MimeMessage m = new MimeReconstructor().Reconstruct(msg);

        Header carried = Assert.Single(m.Headers, h => h.Id == HeaderId.Received);
        Assert.Equal(received, carried.Value);                       // Received value survives verbatim
    }
}
