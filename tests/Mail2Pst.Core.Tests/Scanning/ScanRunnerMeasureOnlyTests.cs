// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Scanning;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

public class ScanRunnerMeasureOnlyTests
{
    private const string Mbox =
        "From a@b Thu Jan 01 00:00:00 2026\r\n" +
        "Subject: t\r\n" +
        "Content-Type: multipart/mixed; boundary=X\r\n\r\n" +
        "--X\r\nContent-Type: text/plain\r\n\r\nhi\r\n" +
        "--X\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=a.bin\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\nYWJj\r\n--X--\r\n";

    [Fact]
    public void Scan_WithAttachment_EstimateAndCountUnchanged_NoCrash()
    {
        string path = Path.Combine(Path.GetTempPath(), "m2p-scan-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, Mbox);
        try
        {
            ScanReport report = new ScanRunner().Scan(path, "mbox");
            Assert.Equal(1, report.Totals.Messages);
            Assert.True(report.Totals.Bytes > 0);     // estimate computed from the length-only attachment
        }
        finally { File.Delete(path); }
    }

    // Proves the SCAN registry path actually returns a measure-only parser (length-only content),
    // not just that scan happens to still work.
    [Fact]
    public void GetForScan_ReturnsMeasureOnlyParser_AttachmentIsLengthOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), "m2p-getforscan-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, Mbox);
        try
        {
            var result = ParserRegistry.GetForScan("mbox").Parse(path).Single();
            Assert.Throws<InvalidOperationException>(() =>
            {
                result.Message!.Attachments.Single().Content.OpenRead();
            });
        }
        finally { File.Delete(path); }
    }
}
