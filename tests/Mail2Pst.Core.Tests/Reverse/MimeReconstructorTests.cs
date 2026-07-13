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
        { FromName = fromName };   // Plan-1 addendum: init-only sender display name

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

    // ---- Task 1 tests ----------------------------------------------------------------------------

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
}
