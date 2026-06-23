// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class AllocationHelperTests
{
    private static string CreateTempPstCopy()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"m2p-amap-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(tempPath);
        return tempPath;
    }

    [Fact]
    public void AllocateSpaceForBlock_FirstAllocation_ReturnsOffsetAfterAMapPage()
    {
        string path = CreateTempPstCopy();
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite);

            long offset = AllocationHelper.AllocateSpaceForBlock(pst, 64);

            // Offset 0 (relative to the AMap page) is reserved for the AMap page itself
            // and is never a valid allocation.
            Assert.True(offset > AllocationMapPage.FirstPageOffset);
            Assert.Equal(0, offset % 64);
        }
        finally
        {
            pst?.CloseFile();
            File.Delete(path);
        }
    }

    [Fact]
    public void AllocateSpaceForBlock_SequentialAllocations_AreContiguous()
    {
        string path = CreateTempPstCopy();
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite);

            long offset1 = AllocationHelper.AllocateSpaceForBlock(pst, 64);
            long offset2 = AllocationHelper.AllocateSpaceForBlock(pst, 64);

            Assert.Equal(offset1 + 64, offset2);
        }
        finally
        {
            pst?.CloseFile();
            File.Delete(path);
        }
    }

    [Fact]
    public void FreeAllocation_ThenAllocateSameSize_ReusesFreedOffset()
    {
        string path = CreateTempPstCopy();
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite);

            long offset1 = AllocationHelper.AllocateSpaceForBlock(pst, 64);
            long offset2 = AllocationHelper.AllocateSpaceForBlock(pst, 64);
            Assert.Equal(offset1 + 64, offset2);

            AllocationHelper.FreeAllocation(pst, offset1, 64);

            // offset1 is once again the lowest free offset on the page, so first-fit
            // must return it again rather than allocating new space elsewhere.
            long offset3 = AllocationHelper.AllocateSpaceForBlock(pst, 64);
            Assert.Equal(offset1, offset3);
        }
        finally
        {
            pst?.CloseFile();
            File.Delete(path);
        }
    }

    [Fact]
    public void AllocateSpaceForBlock_ExhaustsFirstPage_GrowsCacheToSecondPage()
    {
        string path = CreateTempPstCopy();
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite);

            long offset;
            int iterations = 0;
            do
            {
                offset = AllocationHelper.AllocateSpaceForBlock(pst, AllocationHelper.MaxAllocationLength);
                iterations++;
            }
            while (offset < AllocationMapPage.FirstPageOffset + AllocationMapPage.MapppedLength && iterations < 1000);

            Assert.True(offset >= AllocationMapPage.FirstPageOffset + AllocationMapPage.MapppedLength,
                "Expected an allocation to land on the second AMap page within 1000 iterations");

            int expectedPageCount = pst.Header.root.NumberOfAllocationMapPages;
            Assert.True(expectedPageCount >= 2, $"Expected at least 2 AMap pages, got {expectedPageCount}");
            Assert.Equal(expectedPageCount, pst.AMapCache.Count);
            Assert.Equal(expectedPageCount, pst.AMapMaxFreeGeneral.Count);
            Assert.Equal(expectedPageCount, pst.AMapMaxFreeAligned.Count);
        }
        finally
        {
            pst?.CloseFile();
            File.Delete(path);
        }
    }

    [Fact]
    public void AllocateSpaceForBlock_ManyAllocationsAcrossManyAMapPages_CompletesQuickly()
    {
        string path = CreateTempPstCopy();
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite);
            var stopwatch = Stopwatch.StartNew();

            // 5000 allocations of the max block size (8192 bytes) is ~41MB, spanning
            // roughly 150 AMap pages (each AMap page covers ~248KB). AllocateSpace itself
            // doesn't need a BeginSavingChanges/EndSavingChanges bracket (that's only
            // required before Node/Block-level mutations, see PSTFile.cs) — calling it
            // directly here isolates AllocationHelper's own cost.
            const int totalAllocations = 5000;

            for (int i = 0; i < totalAllocations; i++)
            {
                AllocationHelper.AllocateSpaceForBlock(pst, AllocationHelper.MaxAllocationLength);
            }

            stopwatch.Stop();
            Assert.True(stopwatch.ElapsedMilliseconds < 15000,
                $"Expected {totalAllocations} allocations across ~150 AMap pages to complete in under 15 seconds, took {stopwatch.ElapsedMilliseconds}ms");
        }
        finally
        {
            pst?.CloseFile();
            File.Delete(path);
        }
    }

    [Fact]
    public void RandomizedAllocateAndFree_KeepsFreeSpaceIndexInSyncWithBitmapsAndAvoidsOverlap()
    {
        string path = CreateTempPstCopy();
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite);
            var random = new Random(12345);
            var allocations = new List<(long offset, int length)>();

            for (int i = 0; i < 500; i++)
            {
                bool shouldFree = allocations.Count > 0 && random.Next(2) == 0;
                if (shouldFree)
                {
                    int index = random.Next(allocations.Count);
                    (long offset, int length) = allocations[index];
                    AllocationHelper.FreeAllocation(pst, offset, length);
                    allocations.RemoveAt(index);
                }
                else
                {
                    bool pageAligned = random.Next(10) == 0;
                    int length = pageAligned ? Page.Length : (random.Next(128) + 1) * 64; // 64..8192, 64-byte aligned
                    long? expected = FindExpectedFirstFit(pst, length, pageAligned);

                    long offset = pageAligned
                        ? AllocationHelper.AllocateSpaceForPage(pst)
                        : AllocationHelper.AllocateSpaceForBlock(pst, length);

                    if (pageAligned)
                    {
                        Assert.Equal(0, offset % Page.Length);
                    }

                    // If an existing page had room, AllocateSpace must place the allocation
                    // exactly where a first-fit scan of the current bitmaps would. When no
                    // existing page has room (expected == null), AllocateSpace instead grows
                    // the file via GrowPST, which this check does not predict.
                    if (expected.HasValue)
                    {
                        Assert.Equal(expected.Value, offset);
                    }

                    foreach ((long existingOffset, int existingLength) in allocations)
                    {
                        bool overlaps = offset < existingOffset + existingLength && existingOffset < offset + length;
                        Assert.False(overlaps,
                            $"New allocation [{offset},{offset + length}) overlaps existing [{existingOffset},{existingOffset + existingLength})");
                    }

                    allocations.Add((offset, length));
                }

                // The free-space index must always match the actual cached bitmaps.
                for (int p = 0; p < pst.AMapCache.Count; p++)
                {
                    AllocationMapPage page = pst.AMapCache[p];
                    Assert.Equal(page.GetMaxContiguousSpace(), pst.AMapMaxFreeGeneral[p]);
                    Assert.Equal(page.GetMaxAlignedContiguousSpace(), pst.AMapMaxFreeAligned[p]);
                }
            }
        }
        finally
        {
            pst?.CloseFile();
            File.Delete(path);
        }
    }

    /// <summary>
    /// Predicts the offset AllocateSpace should return for a first-fit allocation of
    /// <paramref name="length"/> bytes against the *current* AMapCache state, by scanning
    /// pages in index order and using FindContiguousSpace directly — the same authority
    /// AllocateSpace itself defers to. Returns null if the cache isn't populated yet, or if
    /// no existing page has room (AllocateSpace would grow the file via GrowPST instead).
    /// </summary>
    private static long? FindExpectedFirstFit(PSTFile pst, int length, bool pageAligned)
    {
        if (pst.AMapCache == null)
        {
            return null;
        }

        for (int pageIndex = 0; pageIndex < pst.AMapCache.Count; pageIndex++)
        {
            AllocationMapPage page = pst.AMapCache[pageIndex];
            int startOffset = page.FindContiguousSpace(length, pageAligned);
            if (startOffset > 0)
            {
                return AllocationMapPage.FirstPageOffset + (long)pageIndex * AllocationMapPage.MapppedLength + startOffset;
            }
        }

        return null;
    }
}
