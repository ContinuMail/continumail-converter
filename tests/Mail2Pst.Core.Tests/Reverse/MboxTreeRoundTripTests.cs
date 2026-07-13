// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class MboxTreeRoundTripTests
{
    private static IReadOnlyList<string> P(params string[] segments) => segments;

    private static PstMailMessage Msg(
        string subject, string body, bool read, string[]? categories = null)
        => new PstMailMessage(
            Subject: subject, FromAddress: "sender@example.com",
            Recipients: Array.Empty<PstRecipient>(), Date: DateTimeOffset.UnixEpoch, MessageId: null,
            InReplyTo: null, References: null,
            PlainBody: body, HtmlBody: null, InternetCodepage: null, TransportHeaders: null,
            IsRead: read, IsReplied: false, IsForwarded: false,
            Categories: categories ?? Array.Empty<string>(), Attachments: Array.Empty<PstAttachment>());

    [Fact]
    public void ExportedMbox_CarriesMozillaHeaders_AndReparsesWithMboxParser()
    {
        string root = Path.Combine(Path.GetTempPath(), "m2p-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var items = new[]
            {
                new PstMailItem(P("Inbox"), Msg("Read and starred", "first body",  read: true,
                                                categories: new[] { "Star", "Work" })),
                new PstMailItem(P("Inbox"), Msg("Unread plain",     "second body", read: false)),
            };
            var writer = new MboxTreeWriter(new FakeMimeReconstructor());
            MboxTreeWriteResult result = writer.Write(items, new[] { P("Inbox") }, root, includeEmpty: false);

            Assert.Equal(2, result.MessagesWritten);
            string mbox = Path.Combine(root, "Inbox");

            // Raw-scan: both messages carry all three headers; the starred/read one is Read|Marked = 0x0005.
            string[] lines = File.ReadAllLines(mbox);
            Assert.Equal(2, lines.Count(l => l.StartsWith("X-Mozilla-Status:", StringComparison.Ordinal)));
            Assert.Equal(2, lines.Count(l => l.StartsWith("X-Mozilla-Status2:", StringComparison.Ordinal)));
            Assert.Equal(2, lines.Count(l => l.StartsWith("X-Mozilla-Keys:", StringComparison.Ordinal)));
            Assert.Contains("X-Mozilla-Status: 0005", lines);                       // Read | Marked
            Assert.Contains("X-Mozilla-Status: 0000", lines);                       // unread, no flags
            Assert.Contains(lines, l => l.StartsWith("X-Mozilla-Keys: Work", StringComparison.Ordinal));

            // Self-consistency: our own forward parser re-reads the exported mbox as two messages.
            var reparsed = new MboxParser().Parse(mbox).ToList();
            Assert.Equal(2, reparsed.Count);
            Assert.All(reparsed, r => Assert.True(r.Success, r.Error));
            Assert.Equal("Read and starred", reparsed[0].Message!.Subject);
            Assert.Equal("Unread plain",     reparsed[1].Message!.Subject);

            // Flag state survives the round trip: MimeMessageMapper parses X-Mozilla-Status back into
            // IsRead (0x0001) and IsFlagged (Marked 0x0004, from our consumed "Star" category).
            Assert.True(reparsed[0].Message!.IsRead);
            Assert.True(reparsed[0].Message!.IsFlagged);
            Assert.False(reparsed[1].Message!.IsRead);
            Assert.False(reparsed[1].Message!.IsFlagged);
        }
        finally { Directory.Delete(root, true); }
    }
}
