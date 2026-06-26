// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Diagnostics;
using PSTFileFormat;
using Xunit;
using Xunit.Abstractions;

namespace Mail2Pst.Core.Tests.Diagnostics;

public class DurableMemoryCollectorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"m2p-collect-{Guid.NewGuid():N}.pst");
    private global::PSTFileFormat.PSTFile? _file;
    private readonly ITestOutputHelper _out;

    public DurableMemoryCollectorTests(ITestOutputHelper output) { _out = output; }

    public void Dispose()
    {
        try { _file?.CloseFile(); } catch { }
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void Collect_OnStoreWithMessages_ReportsAllFiveFamilies_RatioInRange()
    {
        // Build a live store with 50 messages in a folder — mirrors exactly how PstWriter operates.
        global::PSTFileFormat.PSTFile.CreateEmptyStore(_path);
        _file = new global::PSTFileFormat.PSTFile(_path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();

        // API: use TopOfPersonalFolders (not GetRootFolder — that's the internal root node).
        // CreateChildFolder(name, FolderItemTypeName) is the correct overload (per CLAUDE.md gotcha).
        PSTFolder folder = _file.TopOfPersonalFolders.CreateChildFolder("M", FolderItemTypeName.Note);

        for (int i = 0; i < 50; i++)
        {
            // Note.CreateNewNote requires (file, parentNodeID) — the brief's single-arg form does not exist.
            Note note = Note.CreateNewNote(_file, folder.NodeID);
            note.Subject = "msg " + i;
            folder.AddMessage(note);
        }
        folder.SaveChanges();

        DurableMemoryReport r = DurableMemoryCollector.Collect(_file, new[] { folder }, messagesWritten: 50);

        // All five families must be present.
        Assert.Equal(5, r.Families.Select(f => f.Family).Distinct().Count());
        Assert.True(r.TotalDurableBytes > 0);
        Assert.InRange(r.EvictableRatio, 0.0, 1.0);
        Assert.Contains(r.Classification, new[] { "evictable-leaf-dominant", "pinned-dominant" });

        // [R2:M2] proof: the folder-TC traversal must reach the contents-table block buffer.
        // If blockBuffer PayloadBytes == 0 the traversal is broken (family #1 would be missing
        // or empty) and the evictable ratio would be falsely 0%.
        FamilyResidency bb = r.Families.Single(f => f.Family == "blockBuffer");
        Assert.True(bb.PayloadBytes > 0,
            $"blockBuffer PayloadBytes is 0 — folder-TC traversal did not reach the DataTree block buffer. " +
            $"Full report: [{string.Join(", ", r.Families.Select(f => $"{f.Family}:{f.PayloadBytes}B"))}]");

        // Family #5 (amapCache): assert present. Do NOT require non-zero here — the unit test
        // is mid-BeginSavingChanges and AMapCache may only populate at EndSavingChanges.
        // The observed values are recorded in task-5-report.md for comparison with the Task 7
        // real run (post-checkpoint Flush/EndSavingChanges).
        FamilyResidency amap = r.Families.Single(f => f.Family == "amapCache");
        Assert.NotNull(amap.Family); // present (the Single above would throw if missing)
        // Emit observed values to test output for report recording.
        _out.WriteLine("=== DurableMemoryCollector observed values ===");
        foreach (var f in r.Families)
            _out.WriteLine($"  {f.Family}: instances={f.InstanceCount} payload={f.PayloadBytes}B pending={f.PendingBytes}B evictable={f.EvictableBytes}B pinned={f.PinnedBytes}B");
        _out.WriteLine($"  TotalDurableBytes={r.TotalDurableBytes} EvictableRatio={r.EvictableRatio:F4} Classification={r.Classification}");
        _out.WriteLine($"  [report] blockBuffer.PayloadBytes={bb.PayloadBytes}  amapCache.InstanceCount={amap.InstanceCount}  amapCache.PayloadBytes={amap.PayloadBytes}");
    }
}
