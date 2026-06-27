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

public class MboxParserSpillTests
{
    [Fact]
    public void SpillThreshold_LargeMessage_ParsesIdenticallyToNoSpill()
    {
        // A message whose body is larger than the tiny spill threshold but small enough to inline-compare.
        string big = new string('x', 50_000);
        string mbox = $"From a@b Thu Jan 01 00:00:00 2026\r\nSubject: t\r\n\r\n{big}\r\n";
        string path = Path.Combine(Path.GetTempPath(), "m2p-spill-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, mbox);
        try
        {
            var noSpill = new MboxParser(measureOnly: true, rawSpillThreshold: long.MaxValue).Parse(path).Single();
            var spilled = new MboxParser(measureOnly: true, rawSpillThreshold: 4096).Parse(path).Single();
            Assert.True(noSpill.Success && spilled.Success);
            Assert.Equal(noSpill.Message!.Subject, spilled.Message!.Subject);
            Assert.Equal(noSpill.Message!.TextBody, spilled.Message!.TextBody); // body byte-identical via spill
        }
        finally { File.Delete(path); }
    }
}
