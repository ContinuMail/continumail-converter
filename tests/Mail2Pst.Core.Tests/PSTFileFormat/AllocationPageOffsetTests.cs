// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

/// <summary>
/// Regression for the FPMap page-index defect (2026-07-08). The deprecated PMap/FMap/FPMap pages
/// are placed by AllocationHelper.GrowPST at a fixed slot inside the grown AMap interval
/// (AMap at +0, PMap +512, FMap +1024, FPMap +1536). Each page's PAGETRAILER encodes its own
/// file offset as the BID via FirstPageOffset + pageIndex*MapppedLength, so that computed offset
/// MUST equal the physical location — otherwise scanpst reports Sig/PTYPE/CRC/BID mismatches on
/// the page (only on PSTs &gt; ~2 GB, where FPMap pages first appear — which is why the earlier
/// scanpst-clean arc, tested on 1–2-message stores, never hit it).
///
/// FPMap's GetFPMapPageIndex had been copied verbatim from FMap ((n-128)/496) instead of scaled
/// ×64 ((n-128*64)/(496*64)), so the first FPMap resolved to page index 16 → a BID ~131 GB away
/// from where the page sits.
/// </summary>
public class AllocationPageOffsetTests
{
    private const int FMapSlot = 1024;
    // FMap is always absent at an FPMap interval (FPMap ≡ 256 mod 496, FMap ≡ 128 mod 496), so the
    // FPMap packs into the +1024 slot — the same in-interval offset an FMap would occupy.
    private const int FPMapSlot = 1024;

    // Physical offset GrowPST writes a page to for AMap index n at the given in-interval slot.
    private static long PhysicalOffset(int aMapPageIndex, int slot) =>
        AllocationMapPage.FirstPageOffset + (long)aMapPageIndex * AllocationMapPage.MapppedLength + slot;

    [Theory]
    [InlineData(128)]        // first FMap
    [InlineData(128 + 496)]  // second FMap boundary
    public void FreeMapPage_TrailerOffset_MatchesPhysicalLocation(int aMapPageIndex)
    {
        Assert.Equal(0, FreeMapPage.GetFreeMapEntryIndex(aMapPageIndex)); // n lands on a page boundary
        long trailerOffset = FreeMapPage.FirstPageOffset
            + (long)FreeMapPage.GetFreeMapPageIndex(aMapPageIndex) * FreeMapPage.MapppedLength;
        Assert.Equal(PhysicalOffset(aMapPageIndex, FMapSlot), trailerOffset);
    }

    [Theory]
    [InlineData(128 * 64)]              // first FPMap (~2 GB)
    [InlineData(128 * 64 + 496 * 64)]   // second FPMap boundary
    public void FPMapPage_TrailerOffset_MatchesPhysicalLocation(int aMapPageIndex)
    {
        Assert.Equal(0, FPMapPage.GetFPMapEntryIndex(aMapPageIndex)); // n lands on a page boundary
        long trailerOffset = FPMapPage.FirstPageOffset
            + (long)FPMapPage.GetFPMapPageIndex(aMapPageIndex) * FPMapPage.MapppedLength;
        Assert.Equal(PhysicalOffset(aMapPageIndex, FPMapSlot), trailerOffset);
    }

    [Fact]
    public void FPMapPageIndex_IsZeroAtFirstFPMap_AndIncrementsPerCoverage()
    {
        Assert.Equal(0, FPMapPage.GetFPMapPageIndex(128 * 64));
        Assert.Equal(1, FPMapPage.GetFPMapPageIndex(128 * 64 + 496 * 64));
    }
}
