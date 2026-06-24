# Local modifications to PSTFileFormat

This project vendors [`ROM-Knowledgeware/PSTFileFormat`](https://github.com/ROM-Knowledgeware/PSTFileFormat)
(upstream unmaintained since 2019), licensed under the GNU Lesser General Public License v3
(see `vendor/LICENSE-PSTFileFormat.txt`). The component remains LGPLv3; the complete corresponding
source — including the modifications listed below — is in `vendor/PSTFileFormat/`. Users may modify
that component and rebuild the application from source.

## Modifications (ContinuMail Converter, 2026)

Commit references are from the source repository history where available. Source snapshots
or public archives may not preserve the same history, so the summaries below are the
authoritative description of the local PSTFileFormat modifications.

- Added `PidTagNativeBody` property ID, used to tag HTML native bodies (`42ca02f`).
- Allocation-map free-space helpers + AMap-page caching to improve PST write throughput:
  `AllocationMapPage.GetMaxAlignedContiguousSpace` and an `AllocationHelper` free-space
  index/cache (`74dc446`, `6e41d6c`).
- Write correct To/Cc/Bcc recipient types and grouped display columns on written messages
  (`7129962`).
- Gmail-Takeout fidelity tweaks: NDR (non-delivery-report) suppression, unread-count
  handling, and locale handling (`e558191`).
- Added string-named (MNID_STRING) property support to `PropertyNameToIDMap`/`PropertyName`
  (register/read/round-trip; corrected string-id hash-bucket index `(wGuid<<1)|N`) + MV-Unicode
  multi-string setter on `PropertyContext` (ContinuMail, 2026).

- From-scratch empty-store creation: new file `PSTFile.Create.cs` adds `PSTFile.CreateEmptyStore` /
  `WriteRawScaffold` — builds a valid empty Unicode store (header + first AMap/PMap + empty NBT/BBT
  root leaves + Density List page) so the converter no longer needs to copy the retired blank-PST
  seed asset at startup (ContinuMail, 2026).
- `DefaultStoreTemplates` (new file `DefaultStoreTemplates.cs` + generated partial
  `DefaultStoreTemplates.Blueprint.g.cs`): replays a 1:1 node blueprint of a real Outlook empty
  store (43 nodes, byte-for-byte) into the raw scaffold, with per-store fresh-GUID substitution and
  English default-folder names, so Outlook accepts the from-scratch store without re-provisioning
  (ContinuMail, 2026).
- `PSTHeader.cs`: added `CreateNew` factory (allocates a zeroed header with correct magic/version
  fields), `EnsureNodeIndexAtLeast` (advances per-type NID high-water marks), and initialises
  `rgbFM`/`rgbFP` bytes to the post-scanpst pattern expected by the runtime allocator (ContinuMail, 2026).
- `RootStructure.cs`: added parameterless create-time constructor for use during from-scratch scaffold
  initialisation (ContinuMail, 2026).
- `AllocationHelper.cs` (`GrowPST`): marks the AMap page for each newly grown span as used in
  `rgbFM`, keeping the free-map consistent with the scanner's expectations (ContinuMail, 2026).
- `BTree/NodeBTreeLeafPage.cs`, `BTree/BlockBTreeLeafPage.cs`: added `CreateEmptyRoot` factory on
  each, producing a zeroed BTree leaf page with a valid page trailer (ContinuMail, 2026).
- `Pages/DensityListPage.cs`: added `CreateEmpty` factory, producing the empty Density List page
  that Outlook expects at offset 0x4200 in every store (ContinuMail, 2026).
- `HeapOnNode.cs`: added a rolling free-space cursor (`m_firstBlockWithPossibleSpace`) so
  `AddItemToHeap` scans for free space starting from the lowest not-provably-full block instead of
  block 0, turning per-allocation O(blocks) into O(1) amortized for append-heavy use (fixes the
  O(n²) growth in `PSTFolder.AddMessage`'s contents-table row index). First-fit placement is
  unchanged; the cursor advances only past blocks that can fit no item and resets on removal
  (ContinuMail, 2026).

See the project git history (`git log -- vendor/PSTFileFormat`) for the full diffs.
