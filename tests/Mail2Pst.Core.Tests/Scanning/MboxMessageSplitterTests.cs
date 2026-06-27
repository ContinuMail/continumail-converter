// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Scanning;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

public class MboxMessageSplitterTests
{
    private static string Msg(string id, int bodyBytes) =>
        $"From {id}@b Thu Jan 01 00:00:00 2026\r\nSubject: {id}\r\n\r\n{new string('x', bodyBytes)}\r\n";

    private static string Write(string content)
    {
        string p = Path.Combine(Path.GetTempPath(), "m2p-split-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(p, content); return p;
    }

    [Fact]
    public void Ranges_TileFileExactly_AndAreBoundaryAligned()
    {
        // 4 equal-ish messages; target chunk ~ half the file -> expect 2 ranges split on a real boundary.
        string all = Msg("a", 1000) + Msg("b", 1000) + Msg("c", 1000) + Msg("d", 1000);
        string path = Write(all);
        try
        {
            long len = Encoding.ASCII.GetByteCount(all);
            var ranges = MboxMessageSplitter.ComputeRanges(path, len, targetChunkBytes: len / 2);
            Assert.True(ranges.Count >= 2);
            Assert.Equal(0, ranges.First().Start);
            Assert.Equal(len, ranges.Last().End);
            byte[] bytes = File.ReadAllBytes(path);
            for (int i = 1; i < ranges.Count; i++)
            {
                Assert.Equal(ranges[i - 1].End, ranges[i].Start);   // contiguous, no gaps/overlap
                // Each interior split lands on a REAL boundary ("From " line), not a guessed offset. [R4]
                Assert.Equal("From ", Encoding.ASCII.GetString(bytes, (int)ranges[i].Start, 5));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OneGiantMessage_AbsorbsTargets_FallsBackTowardWholeFile()
    {
        string all = Msg("a", 500_000);                 // single huge message
        string path = Write(all);
        try
        {
            long len = Encoding.ASCII.GetByteCount(all);
            var ranges = MboxMessageSplitter.ComputeRanges(path, len, targetChunkBytes: len / 8);
            Assert.Single(ranges);                       // no interior boundary -> one whole-file range
            Assert.Equal((0L, len), ranges[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SmallFile_BelowTargetChunk_ReturnsSingleRange()
    {
        string all = Msg("a", 100) + Msg("b", 100);
        string path = Write(all);
        try
        {
            long len = Encoding.ASCII.GetByteCount(all);
            var ranges = MboxMessageSplitter.ComputeRanges(path, len, targetChunkBytes: len * 4);
            Assert.Single(ranges);
            Assert.Equal((0L, len), ranges[0]);
        }
        finally { File.Delete(path); }
    }

    // Step 4: context-bootstrap stress. A "From " boundary that is a boundary ONLY because the line
    // immediately before it is blank (NOT an envelope postmark), placed > BackWindow into the file so
    // discovery's back-seek must reach back to re-observe that blank line. The target lands immediately
    // before the boundary; the preceding blank line begins before the target. With context bootstrap the
    // boundary is found and equals the offset the sequential parse engine reports for that same message.
    [Fact]
    public void FindBoundaryAtOrAfter_TargetJustBeforeBoundary_MatchesSequentialOffset()
    {
        // msg1 envelope-postmark boundary at 0; a big body pushes the second (non-postmark) boundary
        // well past the 64 KiB back-window so backStart > 0 and context must be re-bootstrapped.
        string msg1 = "From a@b Thu Jan 01 00:00:00 2026\r\nSubject: a\r\n\r\n"
                      + new string('x', 80_000) + "\r\n";
        string blankThenBareFrom = "\r\nFrom notapostmark\r\nSubject: b\r\n\r\nbody\r\n";
        string all = msg1 + blankThenBareFrom;
        string path = Write(all);
        try
        {
            long len = Encoding.ASCII.GetByteCount(all);

            // Ground truth from the SAME boundary engine the parser uses end to end.
            var sequential = new MboxParser().ScanMessageStartOffsets(path);
            Assert.Equal(2, sequential.Count);
            long bareFromOffset = sequential[1];            // the non-postmark boundary
            Assert.True(bareFromOffset > 64 * 1024,
                "fixture must place the second boundary past the back-window");

            long target = bareFromOffset - 1;               // immediately before the boundary
            using FileStream stream = File.OpenRead(path);
            long? found = MboxParser.FindBoundaryAtOrAfter(stream, target, scanCap: len);

            Assert.Equal(bareFromOffset, found);
        }
        finally { File.Delete(path); }
    }
}
