// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mail2Pst.Core.Scanning;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

public class ScanProgressAggregatorTests
{
    [Fact]
    public async Task ConcurrentDeltas_Monotonic_Clamped_ReachesTotalOnFinal()
    {
        var emitted = new List<ScanProgress>();
        var gate = new object();
        long total = 8_000_000;
        var agg = new ScanProgressAggregator(total,
            p => { lock (gate) emitted.Add(p); }, thresholdBytes: 1_000_000);

        // 8 "ranges" each contributing 1,000,000 bytes in 100 chunks, concurrently.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++) agg.Add(10_000);
        })));
        agg.EmitFinal();

        Assert.All(emitted, p => Assert.True(p.Bytes <= total));
        for (int i = 1; i < emitted.Count; i++) Assert.True(emitted[i].Bytes >= emitted[i - 1].Bytes);
        Assert.Equal(total, emitted.Last().Bytes);
    }
}
