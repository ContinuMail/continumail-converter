// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class MboxParserMeasureOnlyTests
{
    // One message with a base64 attachment whose decoded length is 3 bytes ("abc").
    private const string Mbox =
        "From a@b Thu Jan 01 00:00:00 2026\r\n" +
        "Subject: t\r\n" +
        "Content-Type: multipart/mixed; boundary=X\r\n\r\n" +
        "--X\r\nContent-Type: text/plain\r\n\r\nhi\r\n" +
        "--X\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=a.bin\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\nYWJj\r\n--X--\r\n";

    [Fact]
    public void MeasureOnly_Parse_AttachmentLengthExact_ContentThrows()
    {
        string path = Path.Combine(Path.GetTempPath(), "m2p-measureonly-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, Mbox);
        try
        {
            ParseResult r = new MboxParser(measureOnly: true).Parse(path).Single();
            Assert.True(r.Success);
            MailAttachment a = Assert.Single(r.Message!.Attachments);
            Assert.Equal(3, a.Content.Length);                       // decoded "abc"
            Assert.Throws<InvalidOperationException>(() => a.Content.OpenRead());
        }
        finally { File.Delete(path); }
    }
}
