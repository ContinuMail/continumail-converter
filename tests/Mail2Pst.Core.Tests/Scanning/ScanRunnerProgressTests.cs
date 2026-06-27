// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Scanning;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

public class ScanRunnerProgressTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Scan_WithOnProgress_EmitsCumulativeBytesReachingTotal()
    {
        var runner = new ScanRunner();
        var progresses = new List<ScanProgress>();
        string a = Fixture("sample.mbox");
        string b = Fixture("mbox-with-attachments.mbox");
        long total = new FileInfo(a).Length + new FileInfo(b).Length;

        runner.Scan(new[] { a, b }, "mbox", progresses.Add);

        // Under parallel range aggregation the exact emit count is no longer fixed (it depends on
        // range tiling + the byte throttle), so assert the DURABLE invariants instead: at least one
        // emit, every emit clamped to total, non-decreasing, and the final EmitFinal reaches 100%.
        Assert.NotEmpty(progresses);
        Assert.All(progresses, p => Assert.Equal(total, p.TotalBytes));
        Assert.All(progresses, p => Assert.True(p.Bytes <= p.TotalBytes, "bytes must be clamped to total"));
        for (int i = 1; i < progresses.Count; i++)
            Assert.True(progresses[i].Bytes >= progresses[i - 1].Bytes, "bytes must be non-decreasing");
        Assert.Equal(total, progresses[^1].Bytes); // guaranteed scan-end emit reaches 100%
    }

    [Fact]
    public void Scan_WithoutOnProgress_StillProducesReport()
    {
        var runner = new ScanRunner();
        ScanReport report = runner.Scan(Fixture("sample.mbox"), "mbox");
        Assert.Equal(2, report.Totals.Messages);
    }
}
