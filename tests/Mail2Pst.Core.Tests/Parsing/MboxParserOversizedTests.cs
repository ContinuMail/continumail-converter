// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Parsing;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class MboxParserOversizedTests
{
    private static string WriteTempMbox(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-oversized-{Guid.NewGuid():N}.mbox");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // One valid, MimeKit-parseable message body (headers + blank + body).
    private static string Msg(string subject, string body) =>
        $"From sender@example.com Mon Jan  1 00:00:00 2020\r\nSubject: {subject}\r\n\r\n{body}\r\n\r\n";

    [Fact]
    public void Parse_OversizedMessageFollowedByNormalMessages_SkipsOnlyTheOversized_KeepsRest()
    {
        // Message #1 is built from many short lines whose total exceeds a tiny injected cap (200 bytes),
        // so it is oversized. Messages #2..#4 are normal and must still be parsed.
        var big = new StringBuilder("From sender@example.com Mon Jan  1 00:00:00 2020\r\nSubject: Huge\r\n\r\n");
        for (int i = 0; i < 100; i++) big.Append("padding-line-x\r\n");   // >200 bytes of content
        big.Append("\r\n");
        string content = big.ToString() + Msg("Two", "body two") + Msg("Three", "body three") + Msg("Four", "body four");
        string path = WriteTempMbox(content);
        try
        {
            var results = new MboxParser(maxMessageBytes: 200).Parse(path).ToList();

            Assert.Equal(4, results.Count);
            Assert.False(results[0].Success);                                  // #1 oversized -> skip
            Assert.Contains("maximum size", results[0].Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("message #1", results[0].Source.Identifier);
            Assert.True(results[1].Success && results[2].Success && results[3].Success);   // rest kept
            Assert.Equal("Two",   results[1].Message!.Subject);
            Assert.Equal("Three", results[2].Message!.Subject);
            Assert.Equal("Four",  results[3].Message!.Subject);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_SingleLineExceedingCap_SkipsThatMessage_KeepsRest_NoOverflow()
    {
        // A single line longer than the buffer (80 KiB) with no newline, exceeding the cap, exercises
        // the per-line drain path (skipToNewline). It must skip that message, not overflow.
        string monster = new string('x', 200_000);   // one 200 KB line, no interior newline
        string content =
            "From sender@example.com Mon Jan  1 00:00:00 2020\r\nSubject: Monster\r\n\r\n" + monster + "\r\n\r\n"
            + Msg("After", "body after");
        string path = WriteTempMbox(content);
        try
        {
            var results = new MboxParser(maxMessageBytes: 50_000).Parse(path).ToList();

            Assert.Equal(2, results.Count);
            Assert.False(results[0].Success);
            Assert.Contains("maximum size", results[0].Error, StringComparison.OrdinalIgnoreCase);
            Assert.True(results[1].Success);
            Assert.Equal("After", results[1].Message!.Subject);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CountMessages_CountsOversizedMessage()
    {
        var big = new StringBuilder("From sender@example.com Mon Jan  1 00:00:00 2020\r\nSubject: Huge\r\n\r\n");
        for (int i = 0; i < 100; i++) big.Append("padding-line-x\r\n");
        big.Append("\r\n");
        string content = big.ToString() + Msg("Two", "body two");
        string path = WriteTempMbox(content);
        try
        {
            // Oversized message still counts as one message (real boundary), so total == 2.
            Assert.Equal(2, new MboxParser(maxMessageBytes: 200).CountMessages(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_HugeFirstLineNoNewlineAtEof_StillYieldsOversizedSkip_NotDropped()
    {
        // The ONLY content is one huge no-newline line at EOF, right after the From boundary — so NO
        // completed non-boundary line ever runs (a following blank line or message would set
        // currentHasContent via the else branch and mask the bug). Without MarkOversized setting
        // currentHasContent, the oversized message is silently DROPPED (0 results). With the fix it is
        // yielded as a single-message skip (1 result). This is the review-fix-1 regression lock.
        string monster = new string('y', 200_000);   // 200 KB, no interior newline, no trailing newline
        string content = "From sender@example.com Mon Jan  1 00:00:00 2020\r\n" + monster;
        string path = WriteTempMbox(content);
        try
        {
            var results = new MboxParser(maxMessageBytes: 50_000).Parse(path).ToList();

            var failed = Assert.Single(results);   // dropped -> 0 results WITHOUT the fix
            Assert.False(failed.Success);
            Assert.Contains("maximum size", failed.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanRange_OversizedMessage_EmitsSkippedRangeMessage_AndKeepsFollowing()
    {
        var big = new StringBuilder("From sender@example.com Mon Jan  1 00:00:00 2020\r\nSubject: Huge\r\n\r\n");
        for (int i = 0; i < 100; i++) big.Append("padding-line-x\r\n");
        big.Append("\r\n");
        string content = big.ToString() + Msg("Two", "body two");
        string path = WriteTempMbox(content);
        try
        {
            var result = new MboxParser(maxMessageBytes: 200)
                .ScanRange(path, 0, long.MaxValue, onBytesRead: null);

            Assert.Equal(2, result.Messages.Count);
            Assert.True(result.Messages[0].IsSkipped);
            Assert.Contains("maximum size", result.Messages[0].SkipReason!, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.Messages[1].IsSkipped);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanMessageStartOffsets_IncludesOversizedAndFollowingMessages()
    {
        // Byte-identity lock: even after draining the oversized message, the NEXT message's boundary
        // offset must be correct (consumed accounting stays accurate through the drain path).
        var big = new StringBuilder("From sender@example.com Mon Jan  1 00:00:00 2020\r\nSubject: Huge\r\n\r\n");
        for (int i = 0; i < 100; i++) big.Append("padding-line-x\r\n");
        big.Append("\r\n");
        string content = big.ToString() + Msg("Two", "body two");
        string path = WriteTempMbox(content);
        try
        {
            var offsets = new MboxParser(maxMessageBytes: 200).ScanMessageStartOffsets(path);

            Assert.Equal(2, offsets.Count);
            Assert.Equal(0, offsets[0]);
            Assert.True(offsets[1] > offsets[0]);
        }
        finally { File.Delete(path); }
    }
}
