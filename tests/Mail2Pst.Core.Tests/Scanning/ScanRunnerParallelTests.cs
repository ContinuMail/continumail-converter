// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Scanning;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

public class ScanRunnerParallelTests
{
    private static string Msg(string id, int body) =>
        $"From {id}@b Thu Jan 01 00:00:00 2026\r\nMessage-ID: <{id}>\r\nSubject: {id}\r\n\r\n{new string('x', body)}\r\n";

    private static ScanReport ScanWith(string path, long chunk) =>
        new ScanRunner(new ScanRunnerOptions { TargetChunkBytes = chunk, MaxDegreeOfParallelism = 4 })
            .Scan(new[] { path }, "mbox");

    [Fact]
    public void TinyChunks_ForceManyRanges_ProduceIdenticalReportToWholeFile()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++) sb.Append(Msg("m" + i, 2000));   // ~1.1 MB
        string path = Path.Combine(Path.GetTempPath(), "m2p-par-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, sb.ToString());
        try
        {
            long len = new FileInfo(path).Length;
            // Prove tiny chunks actually split into many ranges (else the comparison proves nothing).
            Assert.True(MboxMessageSplitter.ComputeRanges(path, len, 32 * 1024).Count > 1);

            ScanReport whole = ScanWith(path, long.MaxValue);   // one range  (whole-file baseline)
            ScanReport split = ScanWith(path, 32 * 1024);       // many ranges (parallel path)

            Assert.Equal(whole.Totals.Messages, split.Totals.Messages);
            Assert.Equal(whole.Totals.Bytes, split.Totals.Bytes);
            Assert.Equal(whole.Totals.SourceBytes, split.Totals.SourceBytes);
            Assert.Equal(500, split.Totals.Messages);
            Assert.Empty(split.Skipped);
        }
        finally { File.Delete(path); }
    }

    // [R4] Blocker-1 guard: a skipped message must keep the RAW ordinal for both its #N identifier and the
    // message count, identical between the split (parallel) and whole-file paths. Build a 3-message fixture
    // whose middle message triggers a parse skip (reuse the malformed-message pattern from MboxParserTests).
    [Fact]
    public void SkippedMiddleMessage_RawOrdinalAndCount_MatchWholeFile()
    {
        // msg1 valid, msg2 MALFORMED (colon-less header lines -> MimeKit FormatException -> skip), msg3 valid.
        string mbox =
            "From a@b.com Mon Jan 01 00:00:00 2024\r\n" +
            "From: alice@example.com\r\n" +
            "Subject: Good1\r\n" +
            "\r\n" +
            "hello one\r\n" +
            "\r\n" +
            "From a@b.com Mon Jan 01 00:00:01 2024\r\n" +
            "ThisLineHasNoColon\r\n" +
            "NeitherDoesThisOne\r\n" +
            "\r\n" +
            "garbage\r\n" +
            "\r\n" +
            "From a@b.com Mon Jan 01 00:00:02 2024\r\n" +
            "From: alice@example.com\r\n" +
            "Subject: Good3\r\n" +
            "\r\n" +
            "hello three\r\n";
        string path = Path.Combine(Path.GetTempPath(), "m2p-par-skip-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, mbox);
        try
        {
            ScanReport whole = ScanWith(path, long.MaxValue);
            ScanReport split = ScanWith(path, 64);     // force msg2/msg3 into separate ranges
            // Prove the tiny chunk actually splits into >1 range (else this test proves nothing if the
            // splitter ever fail-closed-collapsed to the whole-file range). [final-review finding #6]
            Assert.True(MboxMessageSplitter.ComputeRanges(path, new FileInfo(path).Length, 64).Count > 1);
            Assert.Equal(whole.Totals.Messages, split.Totals.Messages);          // raw count incl. the skip
            Assert.Equal(3, split.Totals.Messages);
            var skip = Assert.Single(split.Skipped);
            Assert.Equal("message #2", skip.Identifier);                          // raw ordinal preserved
            Assert.Equal(whole.Skipped.Single().Identifier, skip.Identifier);
        }
        finally { File.Delete(path); }
    }
}
