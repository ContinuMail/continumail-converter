// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Source-level reproduction for the large-PST allocation defects (2026-07-08). The first FPMap
/// page only exists once a store grows past ~2.08 GB (AMap index 128*64), so the earlier
/// scanpst-clean rung ladder (tiny stores) never exercised it. This grows a real store past that
/// boundary via the vendored allocator and verifies, with our own reader, that:
///   (1) a valid FPMap page sits at FPMapPage.FirstPageOffset with ptype=ptypeFPMap and a
///       PAGETRAILER BID equal to its own file offset (the fix to GetFPMapPageIndex);
///   (2) the header's cbAMapFree exactly equals the free bytes computed from the AMap bitmaps
///       (the finalize recompute in ValidateAllocationMap).
///
/// Opt-in (writes &gt; 2 GB): set MAIL2PST_RUN_SLOW_FPMAP_TEST=1.
/// </summary>
public class LargeStoreFPMapTests
{
    [SkippableFact]
    public void StoreGrownPastFirstFPMap_HasValidFPMapPage_AndConsistentAMapFree()
    {
        Skip.If(Environment.GetEnvironmentVariable("MAIL2PST_RUN_SLOW_FPMAP_TEST") != "1",
            "Slow (>2 GB write); set MAIL2PST_RUN_SLOW_FPMAP_TEST=1 to run.");

        // When keeping for an external scanpst run, use a deterministic path and don't delete.
        bool keep = Environment.GetEnvironmentVariable("MAIL2PST_KEEP_FPMAP_PST") == "1";
        string path = keep
            ? Path.Combine(Path.GetTempPath(), "m2p-fpmap-scan.pst")
            : Path.Combine(Path.GetTempPath(), $"m2p-fpmap-{Guid.NewGuid():N}.pst");
        try
        {
            PSTFile.CreateEmptyStore(path);

            // Grow just past the first FPMap page so it is written and (crucially) survives.
            long target = FPMapPage.FirstPageOffset + 4L * 1024 * 1024;
            var file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                file.BeginSavingChanges();
                while (file.BaseStream.Length < target)
                {
                    AllocationHelper.AllocateSpaceForBlock(file, 8192);
                }
                file.EndSavingChanges();
            }
            finally { file.CloseFile(); }

            var reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                // (1) FPMap page trailer at its canonical offset.
                reopened.BaseStream.Seek(FPMapPage.FirstPageOffset, SeekOrigin.Begin);
                byte[] page = new byte[Page.Length];
                int read = reopened.BaseStream.Read(page, 0, page.Length);
                Assert.Equal(Page.Length, read);
                PageTrailer trailer = PageTrailer.ReadFromPage(page);
                Assert.Equal(PageTypeName.ptypeFPMap, trailer.ptype);
                Assert.Equal((ulong)FPMapPage.FirstPageOffset, trailer.bid.Value);

                // (2) Header cbAMapFree matches the bitmap-computed free total.
                ulong computed = 0;
                int pages = reopened.Header.root.NumberOfAllocationMapPages;
                for (int i = 0; i < pages; i++)
                {
                    AllocationMapPage amap = AllocationMapPage.ReadAllocationMapPage(reopened, i);
                    computed += (ulong)amap.GetFreeByteCount();
                }
                Assert.Equal(computed, reopened.Header.root.cbAMapFree);
            }
            finally { reopened.CloseFile(); }
        }
        finally
        {
            if (!keep)
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
    }
}
