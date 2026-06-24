// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Text;
using Mail2Pst.Core.Parsing;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class MboxMessageOffsetScannerTests
{
    private static string WriteBytes(string content)
    {
        string p = Path.Combine(Path.GetTempPath(), "mail2pst-off-" + System.Guid.NewGuid() + ".mbox");
        File.WriteAllText(p, content, new UTF8Encoding(false)); // no BOM: offsets are exact byte positions
        return p;
    }

    [Fact]
    public void ScanMessageStartOffsets_ReturnsFromLineByteOffsets_InOrder()
    {
        // Two messages; the second "From " starts right after the first message's body + blank line.
        string m1 = "From a@b\nSubject: one\n\nbody1\n\n";
        string m2 = "From c@d\nSubject: two\n\nbody2\n";
        string path = WriteBytes(m1 + m2);
        try
        {
            var offsets = new MboxParser().ScanMessageStartOffsets(path);
            Assert.Equal(2, offsets.Count);
            Assert.Equal(0L, offsets[0]);
            Assert.Equal((long)Encoding.ASCII.GetByteCount(m1), offsets[1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanMessageStartOffsets_CountMatchesParse()
    {
        string path = WriteBytes("From a@b\nS: 1\n\nx\n\nFrom c@d\nS: 2\n\ny\n\nFrom e@f\nS: 3\n\nz\n");
        try
        {
            var parser = new MboxParser();
            int parsed = 0; foreach (var _ in parser.Parse(path)) parsed++;
            Assert.Equal(parsed, parser.ScanMessageStartOffsets(path).Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanMessageStartOffsets_AreByteAccurate_CrlfAndNonAscii()
    {
        // CRLF endings + multibyte UTF-8 (æøå) BEFORE the second boundary. A char-count or
        // reader-character bug would put offsets[1] at the wrong byte. storeToken is a BYTE offset.
        string m1 = "From a@b\r\nSubject: one\r\n\r\næøå body\r\n\r\n";
        string m2 = "From c@d\r\nSubject: two\r\n\r\nbody2\r\n";
        string path = WriteBytes(m1 + m2);
        try
        {
            var offsets = new MboxParser().ScanMessageStartOffsets(path);
            Assert.Equal(2, offsets.Count);
            Assert.Equal(0L, offsets[0]);
            Assert.Equal((long)Encoding.UTF8.GetByteCount(m1), offsets[1]); // exact BYTE count, not char count
        }
        finally { File.Delete(path); }
    }
}
