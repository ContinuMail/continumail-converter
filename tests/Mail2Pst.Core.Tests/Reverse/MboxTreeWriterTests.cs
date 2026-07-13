// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class MboxTreeWriterTests
{
    private static IReadOnlyList<string> P(params string[] segments) => segments;

    private static PstMailMessage Msg(
        string subject, string body, bool read = false, string? messageId = null,
        string[]? categories = null)
        => new PstMailMessage(
            Subject: subject, FromAddress: "sender@example.com",
            Recipients: Array.Empty<PstRecipient>(), Date: DateTimeOffset.UnixEpoch, MessageId: messageId,
            InReplyTo: null, References: null,
            PlainBody: body, HtmlBody: null, InternetCodepage: null, TransportHeaders: null,
            IsRead: read, IsReplied: false, IsForwarded: false,
            Categories: categories ?? Array.Empty<string>(), Attachments: Array.Empty<PstAttachment>());

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "m2p-mboxtree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static MboxTreeWriter NewWriter() => new(new FakeMimeReconstructor());

    [Fact]
    public void Write_SingleMessage_WritesEnvelopeAndMozillaHeaders()
    {
        string root = NewTempDir();
        try
        {
            var items = new[] { new PstMailItem(P("Inbox"), Msg("Hi", "Body text", read: true)) };
            MboxTreeWriteResult r = NewWriter().Write(items, new[] { P("Inbox") }, root, includeEmpty: false);

            string mbox = Path.Combine(root, "Inbox");
            Assert.Equal(1, r.MessagesWritten);
            Assert.Contains(mbox, r.MboxFiles);

            string text = File.ReadAllText(mbox);
            Assert.StartsWith("From - ", text);                       // postmark-shaped separator
            Assert.Contains("X-Mozilla-Status: 0001", text);          // Read bit
            Assert.Contains("X-Mozilla-Status2: 00000000", text);
            Assert.Contains("X-Mozilla-Keys:", text);
            Assert.Contains("Subject: Hi", text);
            Assert.Contains("Body text", text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Write_StructuralParentWithZeroMessages_EmitsEmptyMbox_AndSbdDir()
    {
        string root = NewTempDir();
        try
        {
            // Only the child carries a message; the parent has zero messages but must still be emitted.
            var items = new[] { new PstMailItem(P("Parent", "Child"), Msg("c", "child body")) };
            NewWriter().Write(items, new[] { P("Parent"), P("Parent", "Child") }, root, includeEmpty: false);

            string parentMbox = Path.Combine(root, "Parent");
            Assert.True(File.Exists(parentMbox));                     // structural parent emitted...
            Assert.Equal(0, new FileInfo(parentMbox).Length);         // ...as a zero-length mbox
            Assert.True(Directory.Exists(Path.Combine(root, "Parent.sbd")));
            Assert.True(File.Exists(Path.Combine(root, "Parent.sbd", "Child")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Write_BodyLines_AreMboxrdEscaped_LineByLine()
    {
        string root = NewTempDir();
        try
        {
            // Three pinned mboxrd cases in one body:
            //   "From here to there"        -> ">From here to there"   (^From  gets one '>')
            //   ">From already escaped"     -> ">>From already escaped" (^>From  gets one more '>')
            //   "From: header-like but body"-> unchanged                (colon, not space -> not a boundary)
            string body = "From here to there\r\n>From already escaped\r\nFrom: header-like but body";
            var items = new[] { new PstMailItem(P("Inbox"), Msg("s", body)) };
            NewWriter().Write(items, new[] { P("Inbox") }, root, includeEmpty: false);

            string[] lines = File.ReadAllLines(Path.Combine(root, "Inbox"));
            Assert.Contains(">From here to there", lines);
            Assert.DoesNotContain("From here to there", lines);       // the un-escaped form must NOT appear
            Assert.Contains(">>From already escaped", lines);
            Assert.Contains("From: header-like but body", lines);     // header-like body line left as-is
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Write_XMozillaKeys_IsRightPaddedToBlankReserve()
    {
        string root = NewTempDir();
        try
        {
            // With keys "Work": value is "Work" + spaces up to 80 chars. With no keys: value is 80 spaces.
            var items = new[]
            {
                new PstMailItem(P("Inbox"), Msg("tagged", "b", categories: new[] { "Work" })),
                new PstMailItem(P("Inbox"), Msg("plain",  "b")),
            };
            NewWriter().Write(items, new[] { P("Inbox") }, root, includeEmpty: false);

            string[] lines = File.ReadAllLines(Path.Combine(root, "Inbox"));
            const string prefix = "X-Mozilla-Keys: ";
            var keyLines = lines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            Assert.Equal(2, keyLines.Count);

            string tagged = keyLines[0].Substring(prefix.Length);
            Assert.True(tagged.Length >= 80, $"expected padded value >= 80, got {tagged.Length}");
            Assert.Equal("Work" + new string(' ', 80 - "Work".Length), tagged);

            string plain = keyLines[1].Substring(prefix.Length);
            Assert.Equal(new string(' ', 80), plain);                 // blank reserve = 80 spaces
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Write_EmptyLeaf_ObeysIncludeEmpty()
    {
        // "Archive" is an empty leaf (no messages); "Inbox" carries one.
        var items = new[] { new PstMailItem(P("Inbox"), Msg("s", "b")) };
        var folders = new[] { P("Inbox"), P("Archive") };

        string off = NewTempDir();
        try
        {
            NewWriter().Write(items, folders, off, includeEmpty: false);
            Assert.True(File.Exists(Path.Combine(off, "Inbox")));
            Assert.False(File.Exists(Path.Combine(off, "Archive")));  // empty leaf skipped
        }
        finally { Directory.Delete(off, true); }

        string on = NewTempDir();
        try
        {
            NewWriter().Write(items, folders, on, includeEmpty: true);
            Assert.True(File.Exists(Path.Combine(on, "Inbox")));
            Assert.True(File.Exists(Path.Combine(on, "Archive")));    // empty leaf emitted
            Assert.Equal(0, new FileInfo(Path.Combine(on, "Archive")).Length);
        }
        finally { Directory.Delete(on, true); }
    }
}
