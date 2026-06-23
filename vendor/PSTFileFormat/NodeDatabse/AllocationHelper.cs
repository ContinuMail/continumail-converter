/* Copyright (C) 2012-2016 ROM Knowledgeware. All rights reserved.
 *
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 *
 * Maintainer: Tal Aloni <tal@kmrom.com>
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PSTFileFormat
{
    public class AllocationHelper
    {
        public const int MaxAllocationLength = 8192;

        /// <summary>
        /// Caller must call ValidateAllocationMap() when done
        /// </summary>
        public static long AllocateSpaceForPage(PSTFile file)
        {
            return AllocateSpace(file, Page.Length, true);
        }

        /// <summary>
        /// Caller must call ValidateAllocationMap() when done
        /// </summary>
        public static long AllocateSpaceForBlock(PSTFile file, int allocationLength)
        {
            return AllocateSpace(file, allocationLength, false);
        }

        /// <returns>Offset of space allocated</returns>
        private static long AllocateSpace(PSTFile file, int allocationLength, bool pageAligned)
        {
            if (allocationLength > MaxAllocationLength)
            {
                throw new Exception("Invalid allocation length requested");
            }

            EnsureAMapCache(file);

            AllocationMapPage targetPage;
            int targetPageIndex;
            int targetPageStartOffset;
            while (true)
            {
                targetPageIndex = FindFirstPageWithSpace(file, allocationLength, pageAligned);
                if (targetPageIndex < 0)
                {
                    // no space was found within existing pages
                    targetPageIndex = file.AMapCache.Count;
                    GrowPST(file, targetPageIndex);
                }

                targetPage = file.AMapCache[targetPageIndex];
                targetPageStartOffset = targetPage.FindContiguousSpace(allocationLength, pageAligned);
                if (targetPageStartOffset > 0) // 0 is allocated to the AMAP itself and is not a valid value
                {
                    break;
                }

                // The free-space index reported room on this page but FindContiguousSpace
                // disagrees. This should not happen if the index is kept in sync (see
                // docs/superpowers/specs/2026-06-13-allocationhelper-amap-cache-design.md);
                // refresh this page's entries and retry from scratch. FindContiguousSpace
                // remains the final authority on placement, not the index.
                RefreshFreeSpaceIndex(file, targetPageIndex);
            }

            targetPage.AllocateSpace(targetPageStartOffset, allocationLength);
            targetPage.WriteAllocationMapPage(file, targetPageIndex);
            RefreshFreeSpaceIndex(file, targetPageIndex);

            UpdateFMap(file, targetPage, targetPageIndex);

            file.Header.root.cbAMapFree -= (uint)allocationLength;

            //InvalidatePMap(file);
            //InvalidateDList(file);
            return AllocationMapPage.FirstPageOffset + (long)targetPageIndex * AllocationMapPage.MapppedLength + targetPageStartOffset;
        }

        /// <summary>
        /// Returns the index of the first AMap page whose cached free-space index reports
        /// enough contiguous space for <paramref name="allocationLength"/>, or -1 if none
        /// of the currently-known pages can satisfy the request (caller must grow the file).
        /// </summary>
        private static int FindFirstPageWithSpace(PSTFile file, int allocationLength, bool pageAligned)
        {
            List<int> maxFree = pageAligned ? file.AMapMaxFreeAligned : file.AMapMaxFreeGeneral;
            for (int index = 0; index < maxFree.Count; index++)
            {
                if (maxFree[index] >= allocationLength)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Recomputes the free-space index entries for a single AMap page from its
        /// current (cached, in-memory) bitmap.
        /// </summary>
        private static void RefreshFreeSpaceIndex(PSTFile file, int pageIndex)
        {
            AllocationMapPage page = file.AMapCache[pageIndex];
            file.AMapMaxFreeGeneral[pageIndex] = page.GetMaxContiguousSpace();
            file.AMapMaxFreeAligned[pageIndex] = page.GetMaxAlignedContiguousSpace();
        }

        /// <summary>
        /// Lazily populates the in-memory AMap page cache and free-space index the first
        /// time this PSTFile is used for allocation/free. Subsequent calls extend the
        /// cache only if the file grew without going through this class, which should not
        /// normally happen (see docs/superpowers/specs/2026-06-13-allocationhelper-amap-cache-design.md).
        /// </summary>
        private static void EnsureAMapCache(PSTFile file)
        {
            if (file.AMapCache == null)
            {
                file.AMapCache = new Dictionary<int, AllocationMapPage>();
                file.AMapMaxFreeGeneral = new List<int>();
                file.AMapMaxFreeAligned = new List<int>();
            }

            int numberOfPages = file.Header.root.NumberOfAllocationMapPages;
            for (int index = file.AMapCache.Count; index < numberOfPages; index++)
            {
                AllocationMapPage page = AllocationMapPage.ReadAllocationMapPage(file, index);
                file.AMapCache[index] = page;
                file.AMapMaxFreeGeneral.Add(page.GetMaxContiguousSpace());
                file.AMapMaxFreeAligned.Add(page.GetMaxAlignedContiguousSpace());
            }
        }

        private static void UpdateFMap(PSTFile file, AllocationMapPage targetPage, int targetPageIndex)
        {
            byte fMax = (byte)Math.Min(255, targetPage.GetMaxContiguousSpace() / 64);
            int fmapPageIndex = FreeMapPage.GetFreeMapPageIndex(targetPageIndex);
            int fmapPageEntryIndex = FreeMapPage.GetFreeMapEntryIndex(targetPageIndex);

            if (fmapPageIndex >= 0)
            {
                FreeMapPage fmap = FreeMapPage.ReadFreeMapPage(file, fmapPageIndex);
                fmap.rgbFMapBits[fmapPageEntryIndex] = fMax;
                fmap.WriteFreeMapPage(file, fmapPageIndex);
            }
            else
            {
                file.Header.rgbFM[fmapPageEntryIndex] = fMax;
            }
        }

        /*
        private static void InvalidatePMap(PSTFile file)
        {
            file.BaseStream.Seek(PMapPage.FirstPageOffset, SeekOrigin.Begin);
            PMapPage pmap = PMapPage.GetFilledPMapPage();
            byte[] buffer = pmap.GetBytes(PMapPage.FirstPageOffset);
            file.BaseStream.Write(buffer, 0, buffer.Length);
        }

        private static void InvalidateDList(PSTFile file)
        {
            file.BaseStream.Seek(DensityListPage.FirstPageOffset, SeekOrigin.Begin);
            byte[] buffer = new byte[Page.Length];
            file.BaseStream.Write(buffer, 0, buffer.Length);
        }*/

        private static AllocationMapPage GrowPST(PSTFile file, int newPageIndex)
        {
            // The PST file MUST grow at integer multiples of the number of bytes mapped by an AMap
            ulong oldLength = (ulong)file.BaseStream.Length;
            ulong newLength = oldLength + AllocationMapPage.MapppedLength;
            file.BaseStream.Seek(0, SeekOrigin.End);
            AllocationMapPage amap = new AllocationMapPage();
            int freeSpaceInAMap = AllocationMapPage.MapppedLength - 512;
            byte[] buffer = new byte[AllocationMapPage.MapppedLength];
            if (newPageIndex % 8 == 0)
            {
                // We need to write a PMap for backward compatibility
                PMapPage pmap = PMapPage.GetFilledPMapPage();
                ulong pmapOffset = PMapPage.FirstPageOffset + (ulong)(newPageIndex / 8) * PMapPage.MapppedLength;
                Array.Copy(pmap.GetBytes(pmapOffset), 0, buffer, 512, Page.Length);

                // Allocate the PMap out of the AMap
                amap.AllocateSpace(512, 512);
                freeSpaceInAMap -= 512;

                if (newPageIndex >= 128 && FreeMapPage.GetFreeMapEntryIndex(newPageIndex) == 0)
                {
                    // We need to write an FMap for backward compatibility
                    FreeMapPage fmap = new FreeMapPage();
                    fmap.rgbFMapBits[0] = 255;
                    int fmapPageIndex = FreeMapPage.GetFreeMapPageIndex(newPageIndex);
                    ulong fmapOffset = FreeMapPage.FirstPageOffset + (ulong)fmapPageIndex * FreeMapPage.MapppedLength;
                    Array.Copy(fmap.GetBytes(fmapOffset), 0, buffer, 1024, Page.Length);

                    // Allocate the FMap out of the AMap
                    amap.AllocateSpace(1024, 512);
                    freeSpaceInAMap -= 512;
                }

                // FPMap page will always follow an FMap page
                if (newPageIndex >= (128 * 64) && FPMapPage.GetFPMapEntryIndex(newPageIndex) == 0)
                {
                    // We need to write a FPMap for backward compatibility
                    FPMapPage fpmap = new FPMapPage();
                    int fpmapPageIndex = FPMapPage.GetFPMapPageIndex(newPageIndex);
                    ulong fpmapOffset = FPMapPage.FirstPageOffset + (ulong)fpmapPageIndex * FPMapPage.MapppedLength;
                    Array.Copy(fpmap.GetBytes(fpmapOffset), 0, buffer, 1536, Page.Length);

                    // Allocate the FPMap out of the AMap
                    amap.AllocateSpace(1536, 512);
                    freeSpaceInAMap -= 512;
                }
            }
            Array.Copy(amap.GetBytes(oldLength), 0, buffer, 0, AllocationMapPage.Length);
            file.BaseStream.Write(buffer, 0, buffer.Length);
            file.Header.root.ibFileEOF = newLength;
            file.Header.root.ibAMapLast = oldLength;
            file.Header.root.cbAMapFree += (ulong)freeSpaceInAMap;
            // ContinuMail: mark the newly-grown AMap's header free-map entry as present/free
            // (rgbFM[i]=0xFF for every AMap that exists), matching a scanpst-perfected store.
            // UpdateFMap refines it on first allocation; FMap pages cover indices >= 128.
            if (newPageIndex < 128) { file.Header.rgbFM[newPageIndex] = 0xFF; }
            file.Header.WriteToStream(file.BaseStream, file.WriterCompatibilityMode);

            file.AMapCache[newPageIndex] = amap;
            file.AMapMaxFreeGeneral.Add(amap.GetMaxContiguousSpace());
            file.AMapMaxFreeAligned.Add(amap.GetMaxAlignedContiguousSpace());

            return amap;
        }

        public static void FreePageAllocation(PSTFile file, long offset)
        {
            FreeAllocation(file, offset, Page.Length);
        }

        /// <param name="offset">Within the PST file</param>
        public static void FreeAllocation(PSTFile file, long offset, int allocationLength)
        {
            EnsureAMapCache(file);

            int numberOfPages = file.Header.root.NumberOfAllocationMapPages;

            if (offset < AllocationMapPage.FirstPageOffset)
            {
                throw new ArgumentException("Allocation offset to free cannot be before the first AMAP");
            }

            int pageIndex = (int)((offset - AllocationMapPage.FirstPageOffset) / AllocationMapPage.MapppedLength);
            if (pageIndex >= numberOfPages)
            {
                throw new ArgumentException("Allocation offset to free cannot be after the area mapped by the last AMAP");
            }

            AllocationMapPage page = file.AMapCache[pageIndex];
            int pageStartOffset = (int)((offset - AllocationMapPage.FirstPageOffset) % AllocationMapPage.MapppedLength);
            if (pageStartOffset < AllocationMapPage.Length)
            {
                throw new ArgumentException("Allocation offset to free cannot be within the AMAP page");
            }
            page.FreeAllocatedSpace(pageStartOffset, allocationLength);

            page.WriteAllocationMapPage(file, pageIndex);
            UpdateFMap(file, page, pageIndex);
            RefreshFreeSpaceIndex(file, pageIndex);

            file.Header.root.cbAMapFree += (uint)allocationLength;
        }

        public static void InvalidateAllocationMap(PSTFile file)
        {
            file.Header.root.IsAllocationMapValid = false;
            file.Header.WriteToStream(file.BaseStream, file.WriterCompatibilityMode);
        }

        public static void ValidateAllocationMap(PSTFile file)
        {
            file.Header.root.IsAllocationMapValid = true;
            file.Header.WriteToStream(file.BaseStream, file.WriterCompatibilityMode);
        }
    }
}
