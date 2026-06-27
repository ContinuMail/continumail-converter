// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.IO;
using System.Text;
using Mail2Pst.Core.Parsing.Mbox;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class SpillableMessageBufferTests
{
    [Fact]
    public void BelowThreshold_StaysInMemory_RoundTrips()
    {
        using var buf = new SpillableMessageBuffer(thresholdBytes: 1024);
        buf.Write(Encoding.ASCII.GetBytes("hello"));
        Assert.False(buf.SpilledToDisk);
        using var s = buf.OpenRead();
        Assert.Equal("hello", new StreamReader(s).ReadToEnd());
    }

    [Fact]
    public void AboveThreshold_SpillsToTemp_RoundTrips_AndDeletesOnDispose()
    {
        string tempPath;
        using (var buf = new SpillableMessageBuffer(thresholdBytes: 4))
        {
            buf.Write(Encoding.ASCII.GetBytes("abcdefghij")); // 10 > 4 -> spill
            Assert.True(buf.SpilledToDisk);
            tempPath = buf.TempPathForTest!;
            Assert.True(File.Exists(tempPath));
            using var s = buf.OpenRead();
            Assert.Equal("abcdefghij", new StreamReader(s).ReadToEnd());
        }
        Assert.False(File.Exists(tempPath)); // deleted on Dispose
    }
}
