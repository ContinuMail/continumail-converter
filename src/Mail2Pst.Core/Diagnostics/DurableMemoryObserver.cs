// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.Diagnostics;

/// <summary>Opt-in peak-over-time accumulator for durable-memory snapshots taken at write checkpoints.</summary>
public sealed class DurableMemoryObserver
{
    public DurableMemoryReport? Peak { get; private set; }

    public void Observe(DurableMemoryReport report)
    {
        if (Peak is null || report.TotalDurableBytes > Peak.TotalDurableBytes)
            Peak = report;
    }
}
