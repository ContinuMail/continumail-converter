// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Parsing.Mime;
using MimeKit;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing.Mime;

public class MimeMessageMapperTests
{
    private static readonly SourceReference Src = new() { SourcePath = "x.eml", Identifier = "#1" };

    private static MailMessage Map(MimeMessage mime, List<string>? warnings = null) =>
        new MimeMessageMapper().Map(mime, Src, warnings ?? new List<string>());

    [Fact]
    public void Map_CoreFieldsAndBodies()
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Alice", "alice@example.com"));
        mime.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        mime.Cc.Add(new MailboxAddress("Carol", "carol@example.com"));
        mime.Bcc.Add(new MailboxAddress("Dave", "dave@example.com"));
        mime.Subject = "Hello";
        mime.Date = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        mime.Body = new BodyBuilder { TextBody = "plain", HtmlBody = "<p>html</p>" }.ToMessageBody();

        MailMessage m = Map(mime);

        Assert.Equal("Hello", m.Subject);
        Assert.Equal("Alice", m.From!.Name);
        Assert.Equal("alice@example.com", m.From!.Email);
        Assert.Equal("bob@example.com", Assert.Single(m.To).Email);
        Assert.Equal("carol@example.com", Assert.Single(m.Cc).Email);
        Assert.Equal("dave@example.com", Assert.Single(m.Bcc).Email);
        Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), m.Date);
        Assert.Equal("plain", m.TextBody);
        Assert.Equal("<p>html</p>", m.HtmlBody);
    }

    [Fact]
    public void Map_ThreadingHeaders_NormalizedWithAngleBrackets()
    {
        var mime = new MimeMessage { Subject = "t" };
        mime.From.Add(new MailboxAddress("A", "a@x.com"));
        mime.MessageId = "msg-1@x.com";
        mime.InReplyTo = "parent@x.com";
        mime.References.Add("r1@x.com");
        mime.References.Add("r2@x.com");
        mime.Body = new TextPart("plain") { Text = "b" };

        MailMessage m = Map(mime);

        Assert.Equal("<msg-1@x.com>", m.MessageId);
        Assert.Equal("<parent@x.com>", m.InReplyTo);
        Assert.Equal("<r1@x.com> <r2@x.com>", m.References);
    }

    [Fact]
    public void Map_RegularAndInlineCidAttachments()
    {
        var builder = new BodyBuilder { HtmlBody = "<img src=\"cid:img1\">" };
        builder.Attachments.Add("doc.txt", Encoding.ASCII.GetBytes("file-bytes"), ContentType.Parse("text/plain"));
        var img = builder.LinkedResources.Add("logo.png", new byte[] { 1, 2, 3 }, ContentType.Parse("image/png"));
        img.ContentId = "img1";
        var mime = new MimeMessage { Subject = "a" };
        mime.From.Add(new MailboxAddress("A", "a@x.com"));
        mime.Body = builder.ToMessageBody();

        MailMessage m = Map(mime);

        MailAttachment regular = Assert.Single(m.Attachments, a => a.FileName == "doc.txt");
        Assert.Equal("text/plain", regular.MimeType);
        Assert.False(regular.IsInline);
        Assert.Equal("file-bytes", Encoding.ASCII.GetString(regular.Content.ReadAllBytes()));

        MailAttachment inline = Assert.Single(m.Attachments, a => a.FileName == "logo.png");
        Assert.True(inline.IsInline);
        Assert.Equal("img1", inline.ContentId);
    }

    [Fact]
    public void Map_ReadState_MozillaStatusGmailLabelsAndDefault()
    {
        var read = new MimeMessage { Subject = "r" };
        read.From.Add(new MailboxAddress("A", "a@x.com"));
        read.Headers.Add("X-Mozilla-Status", "0001");
        read.Body = new TextPart("plain") { Text = "b" };
        Assert.True(Map(read).IsRead);

        var unread = new MimeMessage { Subject = "u" };
        unread.From.Add(new MailboxAddress("A", "a@x.com"));
        unread.Headers.Add("X-Gmail-Labels", "Inbox,Unread");
        unread.Body = new TextPart("plain") { Text = "b" };
        Assert.False(Map(unread).IsRead);

        var bare = new MimeMessage { Subject = "n" };
        bare.From.Add(new MailboxAddress("A", "a@x.com"));
        bare.Body = new TextPart("plain") { Text = "b" };
        Assert.True(Map(bare).IsRead);
    }

    [Fact]
    public void Map_Importance_FromXPriority()
    {
        var mime = new MimeMessage { Subject = "p" };
        mime.From.Add(new MailboxAddress("A", "a@x.com"));
        mime.Headers.Add("X-Priority", "1");
        mime.Body = new TextPart("plain") { Text = "b" };

        Assert.Equal(MailImportance.High, Map(mime).Importance);
    }

    [Fact]
    public void Map_MissingDate_IsNull()
    {
        var mime = new MimeMessage { Subject = "d" };
        mime.From.Add(new MailboxAddress("A", "a@x.com"));
        mime.Body = new TextPart("plain") { Text = "b" };
        while (mime.Headers.Contains(HeaderId.Date)) mime.Headers.Remove(HeaderId.Date);

        Assert.Null(Map(mime).Date);
    }

    [Fact]
    public void Map_LargeAttachment_SpillsToTempFileAndCleansUp()
    {
        var builder = new BodyBuilder { TextBody = "b" };
        byte[] big = Enumerable.Range(0, 2048).Select(i => (byte)(i % 251)).ToArray();
        builder.Attachments.Add("big.bin", big, ContentType.Parse("application/octet-stream"));
        var mime = new MimeMessage { Subject = "s" };
        mime.From.Add(new MailboxAddress("A", "a@x.com"));
        mime.Body = builder.ToMessageBody();

        var mapper = new MimeMessageMapper(tempFileThresholdBytes: 1024); // < 2048
        MailMessage m = mapper.Map(mime, Src, new List<string>());

        MailAttachment att = Assert.Single(m.Attachments);
        Assert.True(att.Content.IsTempFileBacked);
        string tempPath = att.Content.TempPath!;
        Assert.True(File.Exists(tempPath), "temp file must exist before dispose");
        Assert.Equal(big, att.Content.ReadAllBytes());
        att.Content.Dispose();
        Assert.False(File.Exists(tempPath), "temp file must be deleted on dispose");
    }

    [Fact]
    public void ExtractAttachments_PartWithNoContent_RecordsWarningAndSkipsAttachment()
    {
        var mime = new MimeMessage();
        var multipart = new Multipart("mixed");
        multipart.Add(new TextPart("plain") { Text = "body" });

        var brokenPart = new MimePart("application", "octet-stream")
        {
            FileName = "broken.bin",
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
        };
        multipart.Add(brokenPart);
        mime.Body = multipart;

        var warnings = new List<string>();

        List<MailAttachment> attachments = new MimeMessageMapper().ExtractAttachments(mime, warnings);

        Assert.Empty(attachments);
        Assert.Single(warnings);
        Assert.Contains("broken.bin", warnings[0]);
        Assert.Contains("application/octet-stream", warnings[0]);
    }

    [Fact]
    public void ExtractAttachments_MultipartAlternativeBodyPartsWithContentId_ProducesNoAttachments()
    {
        // Regression: LinkedIn-style emails use multipart/alternative with
        // Content-ID headers on the text/plain and text/html body parts as
        // labels (e.g. "Content-ID: text-body"). These must NOT be classified
        // as attachments — they are body content, not inline resources.
        var mime = new MimeMessage();
        var alternative = new MultipartAlternative();

        var plainPart = new TextPart("plain") { Text = "plain body" };
        plainPart.ContentId = "text-body";

        var htmlPart = new TextPart("html") { Text = "<p>html body</p>" };
        htmlPart.ContentId = "html-body";

        alternative.Add(plainPart);
        alternative.Add(htmlPart);
        mime.Body = alternative;

        var warnings = new List<string>();

        List<MailAttachment> attachments = new MimeMessageMapper().ExtractAttachments(mime, warnings);

        Assert.Empty(attachments);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExtractAttachments_InlineDispositionWithoutContentId_IsInlineWithNullContentId()
    {
        var mime = new MimeMessage();
        var multipart = new Multipart("mixed");
        multipart.Add(new TextPart("plain") { Text = "body" });

        var inlinePart = new MimePart("application", "octet-stream")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("inline data"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
        };
        multipart.Add(inlinePart);
        mime.Body = multipart;

        var warnings = new List<string>();

        List<MailAttachment> attachments = new MimeMessageMapper().ExtractAttachments(mime, warnings);

        Assert.Empty(warnings);
        Assert.Single(attachments);
        MailAttachment attachment = attachments[0];
        Assert.True(attachment.IsInline);
        Assert.Null(attachment.ContentId);
    }

    [Fact]
    public void ExtractAttachments_AttachmentDispositionWithContentId_IsNotInline()
    {
        // A genuine attachment (Content-Disposition: attachment) that also carries a
        // Content-ID — common for PDFs, ICS invites and signed parts — must stay VISIBLE,
        // not be classified inline (which would write it hidden and drop it from the
        // paperclip/HASATTACH computation). The explicit attachment disposition wins over
        // the Content-ID inline heuristic.
        var mime = new MimeMessage();
        var multipart = new Multipart("mixed");
        multipart.Add(new TextPart("plain") { Text = "body" });

        var part = new MimePart("application", "pdf")
        {
            FileName = "invoice.pdf",
            Content = new MimeContent(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 data"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentId = "part1.body@example.com",
        };
        multipart.Add(part);
        mime.Body = multipart;

        var warnings = new List<string>();
        List<MailAttachment> attachments = new MimeMessageMapper().ExtractAttachments(mime, warnings);

        MailAttachment att = Assert.Single(attachments);
        Assert.False(att.IsInline);                              // explicit attachment disposition wins
        Assert.Equal("part1.body@example.com", att.ContentId);  // Content-ID still preserved for reference
    }

    [Fact]
    public void ExtractAttachments_PartWithContentLocation_PopulatesContentLocation()
    {
        var mime = new MimeMessage();
        var multipart = new Multipart("mixed");
        multipart.Add(new TextPart("plain") { Text = "body" });

        const string location = "http://example.com/images/logo.png";
        var part = new MimePart("image", "png")
        {
            FileName = "logo.png",
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("PNGDATA"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentLocation = new Uri(location),
        };
        multipart.Add(part);
        mime.Body = multipart;

        var warnings = new List<string>();

        List<MailAttachment> attachments = new MimeMessageMapper().ExtractAttachments(mime, warnings);

        Assert.Empty(warnings);
        Assert.Single(attachments);
        Assert.Equal(location, attachments[0].ContentLocation);
    }

    [Fact]
    public void ExtractAttachments_EmbeddedMessageInMultipartReport_ProducesNoAttachments()
    {
        // NDR/bounce messages wrap the original message in multipart/report + message/rfc822.
        // Gmail hides these as system messages; we must not surface them as phantom attachments.
        var original = new MimeMessage();
        original.Subject = "Original";
        original.Body = new TextPart("plain") { Text = "original body" };

        var report = new Multipart("report");
        report.Add(new TextPart("plain") { Text = "Delivery failed." });

        var deliveryStatus = new MimePart("message", "delivery-status")
        {
            Content = new MimeContent(new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(
                "Final-Recipient: rfc822; user@example.com\r\nAction: failed\r\n"))),
        };
        report.Add(deliveryStatus);

        var embedded = new MessagePart { Message = original };
        report.Add(embedded);

        var mime = new MimeMessage();
        mime.Body = report;

        var warnings = new List<string>();
        List<MailAttachment> attachments = new MimeMessageMapper().ExtractAttachments(mime, warnings);

        Assert.Empty(attachments);
        Assert.Empty(warnings);
    }

    private static MailMessage MapWithStatus(string? mozillaStatus, string? gmailLabels = null)
    {
        var mime = new MimeMessage { Subject = "s" };
        mime.From.Add(new MailboxAddress("A", "a@x.com"));
        if (mozillaStatus is not null) mime.Headers.Add("X-Mozilla-Status", mozillaStatus);
        if (gmailLabels is not null) mime.Headers.Add("X-Gmail-Labels", gmailLabels);
        mime.Body = new TextPart("plain") { Text = "b" };
        return Map(mime);
    }

    [Fact]
    public void Map_StatusReadOnly_OnlyReadSet()
    {
        MailMessage m = MapWithStatus("0001");
        Assert.True(m.IsRead);
        Assert.False(m.IsReplied);
        Assert.False(m.IsForwarded);
        Assert.False(m.IsFlagged);
    }

    [Fact]
    public void Map_StatusReplied_ReadAndReplied()
    {
        MailMessage m = MapWithStatus("0003"); // read + replied
        Assert.True(m.IsRead);
        Assert.True(m.IsReplied);
        Assert.False(m.IsForwarded);
        Assert.False(m.IsFlagged);
    }

    [Fact]
    public void Map_StatusStarred_ReadAndFlagged()
    {
        MailMessage m = MapWithStatus("0005"); // read + marked
        Assert.True(m.IsRead);
        Assert.True(m.IsFlagged);
        Assert.False(m.IsReplied);
        Assert.False(m.IsForwarded);
    }

    [Fact]
    public void Map_StatusForwarded_ReadAndForwarded()
    {
        MailMessage m = MapWithStatus("1001"); // read + forwarded
        Assert.True(m.IsRead);
        Assert.True(m.IsForwarded);
        Assert.False(m.IsReplied);
        Assert.False(m.IsFlagged);
    }

    [Fact]
    public void Map_StatusAllFlags_AllSet()
    {
        MailMessage m = MapWithStatus("1007"); // read + replied + marked + forwarded
        Assert.True(m.IsRead);
        Assert.True(m.IsReplied);
        Assert.True(m.IsFlagged);
        Assert.True(m.IsForwarded);
    }

    [Fact]
    public void Map_StatusAllZero_UnreadNoFlags()
    {
        // Regression guard for the parse-once refactor: a PRESENT header must not imply read.
        MailMessage m = MapWithStatus("0000");
        Assert.False(m.IsRead);
        Assert.False(m.IsReplied);
        Assert.False(m.IsForwarded);
        Assert.False(m.IsFlagged);
    }

    [Fact]
    public void Map_StatusMalformed_FallsThroughToReadDefault_NoFlags()
    {
        MailMessage m = MapWithStatus("zzzz");
        Assert.True(m.IsRead); // malformed -> null -> default read
        Assert.False(m.IsReplied);
        Assert.False(m.IsForwarded);
        Assert.False(m.IsFlagged);
    }

    [Fact]
    public void Map_MalformedStatus_DoesNotBlockLegacyStatusFallback()
    {
        // Malformed X-Mozilla-Status -> null -> the legacy "Status:" fallback still applies.
        var mime = new MimeMessage { Subject = "s" };
        mime.From.Add(new MailboxAddress("A", "a@x.com"));
        mime.Headers.Add("X-Mozilla-Status", "zzzz");
        mime.Headers.Add("Status", "O"); // 'O' (old/seen-but-not-read), no 'R' -> not read
        mime.Body = new TextPart("plain") { Text = "b" };

        Assert.False(Map(mime).IsRead);
    }

    [Fact]
    public void Map_NoStatusHeader_ReadDefaultTrue_NoFlags()
    {
        MailMessage m = MapWithStatus(null);
        Assert.True(m.IsRead);
        Assert.False(m.IsReplied);
        Assert.False(m.IsForwarded);
        Assert.False(m.IsFlagged);
    }

    [Fact]
    public void Map_GmailUnreadWithMozillaFlags_ReadFollowsGmail_FlagsFromMozilla()
    {
        // Precedence split: read follows Gmail labels; replied/flagged/forwarded derive from X-Mozilla-Status.
        MailMessage m = MapWithStatus("1006", gmailLabels: "Inbox,Unread"); // replied+marked+forwarded, read bit clear
        Assert.False(m.IsRead);      // Gmail "Unread" wins for read
        Assert.True(m.IsReplied);
        Assert.True(m.IsFlagged);
        Assert.True(m.IsForwarded);
    }

    [Fact]
    public void ExtractAttachments_EmbeddedMessageInNestedMultipartReport_ProducesNoAttachments()
    {
        // KB-001: some NDRs nest the multipart/report below the top level (e.g. inside a
        // multipart/mixed wrapper). The embedded message/rfc822 and the machine-readable
        // message/delivery-status must still be suppressed, not surface as phantom
        // attachments — the old check only looked at the top-level body.
        var original = new MimeMessage { Subject = "Original" };
        original.Body = new TextPart("plain") { Text = "original body" };

        var report = new Multipart("report");
        report.Add(new TextPart("plain") { Text = "Delivery failed." });
        report.Add(new MimePart("message", "delivery-status")
        {
            Content = new MimeContent(new MemoryStream(Encoding.ASCII.GetBytes(
                "Final-Recipient: rfc822; user@example.com\r\nAction: failed\r\n"))),
        });
        report.Add(new MessagePart { Message = original });

        var outer = new Multipart("mixed");
        outer.Add(new TextPart("plain") { Text = "Your message could not be delivered." });
        outer.Add(report);

        var mime = new MimeMessage();
        mime.Body = outer;

        var warnings = new List<string>();
        List<MailAttachment> attachments = new MimeMessageMapper().ExtractAttachments(mime, warnings);

        Assert.Empty(attachments);
        Assert.Empty(warnings);
    }
}
