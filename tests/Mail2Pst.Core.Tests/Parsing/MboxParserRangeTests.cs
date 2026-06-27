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

public class MboxParserRangeTests
{
    private static string Msg(string id, string body) =>
        $"From {id}@b Thu Jan 01 00:00:00 2026\r\nMessage-ID: <{id}>\r\nSubject: {id}\r\n\r\n{body}\r\n";

    [Fact]
    public void ScanRange_SplitAtBoundary_OwnsCorrectMessages_NoOverlapNoDrop()
    {
        string m1 = Msg("a", "one"), m2 = Msg("b", "two"), m3 = Msg("c", "three");
        string all = m1 + m2 + m3;
        long o2 = Encoding.ASCII.GetByteCount(m1);              // start boundary of m2
        long o3 = Encoding.ASCII.GetByteCount(m1 + m2);         // start boundary of m3
        string path = Path.Combine(Path.GetTempPath(), "m2p-range-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, all);
        try
        {
            var p = new MboxParser(measureOnly: true, rawSpillThreshold: 4L * 1024 * 1024);
            var left  = p.ScanRange(path, 0,  o3, null);  // owns m1, m2
            var right = p.ScanRange(path, o3, Encoding.ASCII.GetByteCount(all), null); // owns m3
            Assert.Equal(2, left.Messages.Count);
            Assert.Equal(1, right.Messages.Count);
            Assert.Equal(0, left.StartOffset);
            Assert.Equal(o3, right.StartOffset);
            // Whole-file count equals the sum of the two ranges (no overlap, no drop)
            int whole = p.Parse(path).Count();
            Assert.Equal(whole, left.Messages.Count + right.Messages.Count);
        }
        finally { File.Delete(path); }
    }
}
