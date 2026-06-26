// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Text;
using System.Text.Json;

namespace Mail2Pst.Core.Diagnostics;

/// <summary>Serializes a durable-memory report to JSON (machine) and a short text summary (human).</summary>
public static class DurableMemoryReportWriter
{
    public static string ToJson(DurableMemoryReport r)
    {
        var doc = new
        {
            messagesWritten = r.MessagesWritten,
            fileLengthBytes = r.FileLengthBytes,
            totalDurableBytes = r.TotalDurableBytes,
            evictableBytes = r.EvictableBytes,
            evictableRatio = r.EvictableRatio,
            classification = r.Classification,
            families = r.Families,
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string ToSummary(DurableMemoryReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Durable memory @ {r.MessagesWritten} msgs / file {r.FileLengthBytes:N0} B");
        sb.AppendLine($"  totalDurable={r.TotalDurableBytes:N0} B  evictable={r.EvictableBytes:N0} B  evictableRatio={r.EvictableRatio:P1}");
        sb.AppendLine($"  classification={r.Classification}");
        foreach (var f in r.Families)
            sb.AppendLine($"    {f.Family}: n={f.InstanceCount} payload={f.PayloadBytes:N0} pending={f.PendingBytes:N0} evictable={f.EvictableBytes:N0} pinned={f.PinnedBytes:N0}");
        return sb.ToString();
    }
}
