// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Parsing.Mime;
using MimeKit;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class MimeMessageMapperMeasureOnlyTests
{
    // Literal MIME parsed by MimeKit — avoids MimeContent/transfer-encoding construction ambiguity.
    // The attachment body "YWJj" is base64 for "abc", so its DECODED length is exactly 3.
    private static MimeMessage MsgWithBase64Attachment()
    {
        const string raw =
            "From: A <a@example.com>\r\nTo: B <b@example.com>\r\nSubject: t\r\n" +
            "Content-Type: multipart/mixed; boundary=X\r\n\r\n" +
            "--X\r\nContent-Type: text/plain\r\n\r\nhi\r\n" +
            "--X\r\nContent-Type: application/octet-stream\r\n" +
            "Content-Disposition: attachment; filename=a.bin\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\nYWJj\r\n--X--\r\n";
        using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(raw));
        return MimeMessage.Load(ms);
    }

    [Fact]
    public void MeasureOnly_AttachmentLengthMatchesMaterialized_ButContentThrows()
    {
        var src = new SourceReference { SourcePath = "p", Identifier = "message #1" };

        var materialized = new MimeMessageMapper(4L * 1024 * 1024)
            .Map(MsgWithBase64Attachment(), src, new List<string>());
        long materializedLen = Assert.Single(materialized.Attachments).Content.Length;

        var measured = new MimeMessageMapper(4L * 1024 * 1024, measureOnly: true)
            .Map(MsgWithBase64Attachment(), src, new List<string>());
        AttachmentContent content = Assert.Single(measured.Attachments).Content;

        Assert.Equal(3, materializedLen);                // decoded "abc"
        Assert.Equal(materializedLen, content.Length);   // byte-identical estimate input
        Assert.Throws<InvalidOperationException>(() => content.OpenRead());
    }

    // An embedded message/rfc822 attachment (a forwarded .eml), explicitly attached.
    private static MimeMessage MsgWithEmbeddedMessageAttachment()
    {
        var original = new MimeMessage { Subject = "Original" };
        original.From.Add(new MailboxAddress("O", "o@example.com"));
        original.Body = new TextPart("plain") { Text = new string('x', 500) };

        var mixed = new Multipart("mixed");
        mixed.Add(new TextPart("plain") { Text = "see attached" });
        mixed.Add(new MessagePart
        {
            Message = original,
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
        });

        var outer = new MimeMessage { Subject = "fwd" };
        outer.From.Add(new MailboxAddress("A", "a@example.com"));
        outer.Body = mixed;
        return outer;
    }

    [Fact]
    public void MeasureOnly_EmbeddedMessage_LengthMatchesMaterialized_ButWritesNoTempFile()
    {
        var src = new SourceReference { SourcePath = "p", Identifier = "message #1" };

        // Build ONCE and map twice: MimeKit assigns per-construction boundaries, so two separate
        // constructions could serialize to different lengths. Mapping the same message guarantees a
        // byte-identical WriteTo and a deterministic length comparison.
        MimeMessage mime = MsgWithEmbeddedMessageAttachment();

        // Materialize at threshold 1: the embedded message is fully buffered and spills to a temp file.
        var materialized = new MimeMessageMapper(tempFileThresholdBytes: 1).Map(mime, src, new List<string>());
        MailAttachment matAtt = Assert.Single(materialized.Attachments);
        long materializedLen = matAtt.Content.Length;
        Assert.True(matAtt.Content.IsTempFileBacked, "materialize mode should spill the embedded message");
        matAtt.Content.Dispose();

        // Measure-only at threshold 1: exact length retained, but NOTHING buffered or spilled — the
        // measure-only/parallel-scan memory-safety guarantee must hold for embedded messages too.
        var measured = new MimeMessageMapper(tempFileThresholdBytes: 1, measureOnly: true).Map(mime, src, new List<string>());
        AttachmentContent content = Assert.Single(measured.Attachments).Content;

        Assert.False(content.IsTempFileBacked, "measure-only must not spill the embedded message");
        Assert.Equal(materializedLen, content.Length);   // byte-identical estimate input
        Assert.Throws<InvalidOperationException>(() => content.OpenRead());
    }

    [Fact]
    public void MeasureOnly_WritesNoAttachmentTempFile_EvenAtTinyThreshold()
    {
        var src = new SourceReference { SourcePath = "p", Identifier = "message #1" };
        // Materialize mode at threshold 1: the (decoded) attachment spills to a temp file.
        var materialized = new MimeMessageMapper(tempFileThresholdBytes: 1)
            .Map(MsgWithBase64Attachment(), src, new System.Collections.Generic.List<string>());
        Assert.True(Assert.Single(materialized.Attachments).Content.IsTempFileBacked);
        Assert.Single(materialized.Attachments).Content.Dispose();
        // Measure-only mode at threshold 1: length-only content, NO temp file.
        var measured = new MimeMessageMapper(tempFileThresholdBytes: 1, measureOnly: true)
            .Map(MsgWithBase64Attachment(), src, new System.Collections.Generic.List<string>());
        Assert.False(Assert.Single(measured.Attachments).Content.IsTempFileBacked);
    }
}
