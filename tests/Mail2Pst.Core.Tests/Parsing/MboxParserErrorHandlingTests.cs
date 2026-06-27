// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using MimeKit;
using Mail2Pst.Core.Parsing;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class MboxParserErrorHandlingTests
{
    private static string WriteTempMbox(string content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Parse_MalformedMimeMessage_RecordedAsFailedNotThrown()
    {
        // First message is valid; the second has garbage header lines that
        // MimeKit rejects with FormatException. That is an EXPECTED parse error:
        // it must be recorded as a per-message skip, not abort the whole run.
        string mbox =
            "From a@b.com Mon Jan 01 00:00:00 2024\r\n" +
            "From: alice@example.com\r\n" +
            "Subject: Good\r\n" +
            "\r\n" +
            "hello\r\n" +
            "\r\n" +
            "From a@b.com Mon Jan 01 00:00:01 2024\r\n" +
            "ThisLineHasNoColon\r\n" +
            "NeitherDoesThisOne\r\n" +
            "\r\n" +
            "garbage\r\n";
        string path = WriteTempMbox(mbox);
        try
        {
            var results = new MboxParser().Parse(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.True(results[0].Success, results[0].Error);
            Assert.False(results[1].Success);
            Assert.NotNull(results[1].Error);
        }
        finally { File.Delete(path); }
    }

    // A parser whose parse step throws an UNEXPECTED exception (a bug class,
    // not malformed MIME). Parse must let it propagate rather than silently
    // downgrade it to a per-message skip, which would hide real defects.
    private sealed class BugParser : MboxParser
    {
        protected override MimeMessage ParseMimeMessage(Stream s) =>
            throw new InvalidOperationException("simulated bug");
    }

    [Fact]
    public void Parse_UnexpectedExceptionDuringParse_Propagates()
    {
        string path = WriteTempMbox(
            "From a@b.com Mon Jan 01 00:00:00 2024\r\n" +
            "From: alice@example.com\r\n" +
            "\r\n" +
            "hello\r\n");
        try
        {
            var parser = new BugParser();
            Assert.Throws<InvalidOperationException>(() => parser.Parse(path).ToList());
        }
        finally { File.Delete(path); }
    }
}
