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
using System.Text;
using Utilities;

namespace PSTFileFormat
{
    public class FPMapPage : Page // FPMAPPAGE
    {
        // ContinuMail fix (2026-07-08): the first FPMap sits at the +1024 slot of AMap interval
        // 128*64, NOT +1536. FMap is always absent at FPMap intervals (FPMap intervals ≡ 256 mod
        // 496, FMap intervals ≡ 128 mod 496 — never coincide), so the special pages pack as
        // AMap@+0, PMap@+512, FPMap@+1024. scanpst validates the FPMap at this offset; the old
        // 0x7C004A00 (+1536) left +1024 unallocated (a message overwrote it) and orphaned the FPMap.
        public const int FirstPageOffset = 0x7C004800; // 0x4800 + 253952 * 64 * 128
        public const long MapppedLength = 8061452288; // the number of bytes mapped by an FMap (496 * 253952 * 64)

        public byte[] rgbFPMapBits = new byte[496];
        
        public FPMapPage()
        {
            pageTrailer.ptype = PageTypeName.ptypeFPMap;
            pageTrailer.wSig = 0x00; // zero for FPMap
        }

        public FPMapPage(byte[] buffer) : base(buffer)
        {
            Array.Copy(buffer, 0, rgbFPMapBits, 0, rgbFPMapBits.Length);
        }

        /// <param name="fileOffset">Irrelevant for AMap</param>
        public override byte[] GetBytes(ulong fileOffset)
        {
            byte[] buffer = new byte[Length];
            Array.Copy(rgbFPMapBits, 0, buffer, 0, rgbFPMapBits.Length);
            pageTrailer.WriteToPage(buffer, fileOffset);

            return buffer;
        }

        /// <returns>-1 for header</returns>
        public static int GetFPMapPageIndex(int aMapPageIndex)
        {
            if (aMapPageIndex < 128 * 64)
            {
                return -1;
            }
            else
            {
                // ContinuMail fix (2026-07-08): FPMap is FMap scaled ×64 — it starts at AMap index
                // 128*64 and each FPMap covers 496*64 AMaps (see MapppedLength + GetFPMapEntryIndex).
                // The original ((n-128)/496) was copied verbatim from FreeMapPage, so the first
                // FPMap resolved to page index 16, encoding a PAGETRAILER BID ~131 GB from where the
                // page sits → scanpst Sig/PTYPE/CRC/BID mismatches on every PST > ~2 GB.
                return (aMapPageIndex - 128 * 64) / (496 * 64);
            }
        }

        public static int GetFPMapEntryIndex(int aMapPageIndex)
        {
            if (aMapPageIndex < 128 * 64)
            {
                return aMapPageIndex;
            }
            else
            {
                return (aMapPageIndex - 128 * 64) % (496 * 64);
            }
        }
    }
}
