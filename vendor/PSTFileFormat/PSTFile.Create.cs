/* ContinuMail addition (not part of the upstream ROM-Knowledgeware PSTFileFormat).
 *
 * From-scratch creation of a valid, empty Unicode (.pst) store, so the converter no
 * longer needs to copy the retired in-repo blank PST seed. See tools/pst-genblueprint/README.md.
 *
 * This is "Phase A": it lays down the raw scaffold — header + first AMap/PMap + empty
 * NBT/BBT root leaves — so that `new PSTFile(path)` opens cleanly and the existing write
 * path (BeginSavingChanges / CreateChildFolder / node insertion) can build on top of it.
 * It deliberately mirrors what AllocationHelper.GrowPST(file, 0) produces for the first
 * AMap span, so the on-disk allocation state is exactly what the runtime allocator expects.
 */
using System;
using System.IO;

namespace PSTFileFormat
{
    public partial class PSTFile
    {
        // The standard fixed file layout of the first AMap span (all pages are 512 bytes):
        //   0x0000  header (564 bytes) + reserved padding
        //   0x4400  first AMap        (maps 253,952 bytes starting here; bit = 64 bytes)
        //   0x4600  first PMap        (filled; back-compat only)
        //   0x4800  empty NBT root leaf
        //   0x4A00  empty BBT root leaf
        //   ...     free space, up to ibFileEOF = 0x4400 + 253,952 = 271,360
        private const long ScaffoldNbtRootOffset = 0x4800;
        private const long ScaffoldBbtRootOffset = 0x4A00;

        /// <summary>
        /// The size, in bytes, of a freshly created empty store (one full AMap span).
        /// Equals what GrowPST yields for the first span. Useful as a split-size seed.
        /// </summary>
        public const long EmptyStoreSizeBytes =
            AllocationMapPage.FirstPageOffset + AllocationMapPage.MapppedLength; // 271,360

        /// <summary>
        /// Create a brand-new, valid, empty Unicode PST file at <paramref name="path"/>,
        /// overwriting any existing file. The result is a real, Outlook-openable store with
        /// the standard default folders (Top of Information Store, Deleted Items, Search Root)
        /// and a Message Store node — the drop-in replacement for the retired in-repo blank PST seed.
        /// </summary>
        public static void CreateEmptyStore(string path)
        {
            // Outlook2007RTM writes fAMapValid=VALID_AMAP2 (modern marker, matching the template)
            // and supports appointment recurrence, without requiring a Density List (which the
            // vendored allocator does not implement — Outlook2007SP2 would). See tools/pst-genblueprint/README.md.
            CreateEmptyStore(path, WriterCompatibilityMode.Outlook2007RTM);
        }

        public static void CreateEmptyStore(string path, WriterCompatibilityMode writerCompatibilityMode)
        {
            // Phase A: lay the raw scaffold so the file opens.
            WriteRawScaffold(path, writerCompatibilityMode);

            // Phase B: open and populate the default store (template TCs, folders, store node).
            PSTFile file = new PSTFile(path, System.IO.FileAccess.ReadWrite, writerCompatibilityMode);
            try
            {
                DefaultStoreTemplates.Build(file);
            }
            finally
            {
                file.CloseFile();
            }
        }

        /// <summary>
        /// Phase A only: write the minimal valid scaffold (header + first AMap/PMap + empty
        /// NBT/BBT roots) so that <c>new PSTFile(path)</c> opens. Exposed for testing/diagnostics.
        /// </summary>
        public static void WriteRawScaffold(string path, WriterCompatibilityMode writerCompatibilityMode)
        {
            const int pageLength = Page.Length;                 // 512
            const long amapOffset = AllocationMapPage.FirstPageOffset; // 0x4400
            const long pmapOffset = PMapPage.FirstPageOffset;         // 0x4600

            PSTHeader header = PSTHeader.CreateNew();

            // Page BIDs for the two root pages (the AMap/PMap pages use bid == file offset,
            // assigned automatically by PageTrailer.WriteToPage, so they need no BID here).
            BlockID nbtBid = header.AllocateNextPageBlockID();
            BlockID bbtBid = header.AllocateNextPageBlockID();

            // First AMap: bit 0 (the AMap page itself) is preset by the ctor. Mark the PMap,
            // NBT root and BBT root pages — each a 512-byte, page-aligned allocation — as used.
            // Mapped offsets are relative to the AMap's own file offset (0x4400):
            //   PMap @0x4600 -> 512, NBT @0x4800 -> 1024, BBT @0x4A00 -> 1536.
            AllocationMapPage amap = new AllocationMapPage();
            amap.AllocateSpace(pageLength * 1, pageLength); // PMap
            amap.AllocateSpace(pageLength * 2, pageLength); // NBT root
            amap.AllocateSpace(pageLength * 3, pageLength); // BBT root

            // Wire the root structure. cbAMapFree mirrors GrowPST: (MapppedLength - AMap)
            // minus the PMap, then minus the two root pages = MapppedLength - 4*512.
            header.root.ibAMapLast = amapOffset;
            header.root.ibFileEOF = EmptyStoreSizeBytes;
            header.root.cbAMapFree = (ulong)(AllocationMapPage.MapppedLength - 4 * pageLength);
            header.root.cbPMapFree = 0;
            header.root.fAMapValid = RootStructure.VALID_AMAP2;
            header.root.BREFNBT.bid = nbtBid;
            header.root.BREFNBT.ib = (ulong)ScaffoldNbtRootOffset;
            header.root.BREFBBT.bid = bbtBid;
            header.root.BREFBBT.ib = (ulong)ScaffoldBbtRootOffset;

            // rgbFM[0] / rgbFP are set by PSTHeader.CreateNew() to match a scanpst-perfected store
            // (rgbFM=0xFF only for AMaps that exist; rgbFP all 0xFF). GrowPST marks grown AMaps.

            NodeBTreeLeafPage nbtRoot = NodeBTreeLeafPage.CreateEmptyRoot(nbtBid);
            BlockBTreeLeafPage bbtRoot = BlockBTreeLeafPage.CreateEmptyRoot(bbtBid);

            // Empty Density List page at 0x4200 (in the reserved pre-AMap region, so not tracked
            // by the AMap). Outlook always writes one; omitting it makes scanpst repair the file.
            BlockID dlistBid = header.AllocateNextPageBlockID();
            DensityListPage dlist = DensityListPage.CreateEmpty(dlistBid);

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            {
                // Grow the file to one full AMap span up front (zero-filled), exactly like
                // GrowPST. Pages are then written over the zeroed regions; the tail is free.
                stream.SetLength(EmptyStoreSizeBytes);

                WritePage(stream, DensityListPage.FirstPageOffset, dlist.GetBytes(DensityListPage.FirstPageOffset));
                WritePage(stream, amapOffset, amap.GetBytes((ulong)amapOffset));
                WritePage(stream, pmapOffset, PMapPage.GetFilledPMapPage().GetBytes((ulong)pmapOffset));
                WritePage(stream, ScaffoldNbtRootOffset, nbtRoot.GetBytes((ulong)ScaffoldNbtRootOffset));
                WritePage(stream, ScaffoldBbtRootOffset, bbtRoot.GetBytes((ulong)ScaffoldBbtRootOffset));

                // Header last, once every root field is final (also writes both CRCs).
                header.WriteToStream(stream, writerCompatibilityMode);
                stream.Flush();
            }
        }

        private static void WritePage(Stream stream, long offset, byte[] pageBytes)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            stream.Write(pageBytes, 0, pageBytes.Length);
        }
    }
}
