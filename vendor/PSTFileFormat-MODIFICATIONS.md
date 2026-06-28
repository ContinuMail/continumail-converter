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
- `HeapOnNode.cs`: replaced the linear free-space scan in `AddItemToHeap` with a maintained best-fit
  free-space index — a `SortedSet<(availableSpace, blockIndex)>` over not-provably-full blocks plus a
  per-block available-space map, queried in O(log n) and verified against the real block before use,
  maintained on every heap-item mutation and populated lazily at first allocation. This makes
  allocation cost independent of block count and item-size mix (fixes the O(n²) growth in
  `PSTFolder.AddMessage`'s per-row external columns). Allocation placement changes from first-fit to
  best-fit (valid PSTs; not byte-identical to prior output) (ContinuMail, 2026).

- Removed dead reader-only code with zero references from the converter: the appointment/calendar
  message family (`Appointment`, `SingleAppointment`, `RecurringAppointment`,
  `ModifiedAppointmentInstance`, `CalendarFolder`, `MeetingType`), the recurrence-pattern structures
  (`Messaging/Messages/RecurrencePatternStructure/`), the time-zone structures and helpers
  (`TimeZoneStructure/`, `TimeZoneDefinitionStructure/`, `Utils/TimeZoneInfoUtils*`,
  `Utils/RegistryTimeZoneUtils.Win32`, `Utils/AdjustmentRuleUtils.Win32`), and
  `InvalidRecurrencePatternException`. Also dropped the appointment-only members that referenced them
  (`AttachmentObject.StoreModifiedInstance` / `CreateNewExceptionAttachmentObject`) and the
  `CalendarFolder` branch in `PSTFolder.GetFolder` (an `IPF.Appointment` folder now returns a base
  `PSTFolder`). The write path (search-update queue included) is untouched; output PSTs are byte-for-byte
  unchanged and pass the round-trip + independent-reader gates (ContinuMail, 2026).

- Phase-2 streaming spike — new public APIs added to `BufferedBlockStore` and `DataTree` for streaming
  large-attachment writes with bounded resident memory (ContinuMail, 2026):
  - `BufferedBlockStore.PersistLeafBlocks(IEnumerable<ulong>)`: flush named leaf blocks to the BBT
    without clearing the pending-write set wholesale, leaving the spine pending for in-place updates.
  - `BufferedBlockStore.TryEvictLeaf(ulong, Func<ulong,bool>)`: residency-only eviction of a
    persisted, full leaf DataBlock from the in-memory buffer so block count stays bounded.
  - `BufferedBlockStore.ReadDataLeafWithoutCaching(ulong)`: read a persisted data leaf by BID without
    re-adding it to the resident buffer.
  - `BufferedBlockStore.PendingWriteCountForTest`: spike instrumentation — current pending (unwritten)
    block count.
  - `BufferedBlockStore.BufferedBlockCountForTest`: spike instrumentation — current resident block count.
  - `BufferedBlockStore.BlockReallocationsForTest`: spike instrumentation — BID reallocation counter
    (incremented in the non-pending `UpdateBlock` branch); used by the scaling gate to assert the spine
    is updated in-place across many batch boundaries.
  - `DataTree.AppendData(Stream, long)`: streaming append of `length` bytes from a `Stream` across
    leaf blocks with batched persist+evict, so resident memory is independent of attachment size.
  - `DataTree.IsRootPendingWriteForTest()`: spike instrumentation — whether the current root/spine
    block is still in the pending-write set.

- Phase-2 Target-A production wiring — streaming-write APIs wired into the production write path,
  removing the writer-side OOM cliff on large attachments (ContinuMail, 2026):
  - `DataTree.SaveChanges()`: hoisted `DataBlockCount - 1` out of the zero-fill loop (the
    `DataBlockCount` getter clones the last XBlock/XXBlock for multi-level trees, so evaluating it
    on every iteration was wasteful). Behaviour-preserving performance fix; a modification to an
    existing method, not a new API. (`[R2:L2]`)
  - `DataTree.AppendData(Stream, long, CancellationToken)`: cancellation-aware streaming append
    overload. Checks the token at the top of each leaf iteration (~8 KB); an
    `OperationCanceledException` propagates before the final spine `SaveChanges()`, leaving the
    tree partial+pending for the caller to discard with the in-progress PST part. The two-argument
    `AppendData(Stream, long)` overload (shipping since the Phase-2 spike) delegates to this with
    `CancellationToken.None`.
  - `NodeStorageHelper.StoreExternalProperty(PSTFile, HeapOnNode, ref SubnodeBTree, Stream, long,
    CancellationToken)` (`internal`): INSERT-only streaming store path. Routes small payloads
    (≤ `HeapOnNode.MaximumAllocationLength`) through the existing heap path after buffering them
    from the stream; routes large payloads into a fresh `DataTree` via
    `AppendData(stream, length, cancellationToken)` followed by a new subnode entry. No streaming
    UPDATE overload is provided — the converter writes `PidTagAttachData` exactly once per
    attachment, so an UPDATE path is never reached.
  - `PropertyContext.SetExternalProperty(PropertyID, PropertyTypeName, Stream, long,
    CancellationToken)`: INSERT-only streaming seam on the property context. Delegates to
    `NodeStorageHelper.StoreExternalProperty(…, Stream, long, CancellationToken)`. Throws
    `NotSupportedException` if a record for `propertyID` already exists (UPDATE not supported);
    the production write path never triggers this because `PidTagAttachData` is written once.

- Phase-2 Target-C measurement — read-only residency snapshot APIs added for durable-memory
  measurement (measurement-only; no production behaviour change; each accessor is a property or
  method returning current in-memory counts/sizes and is never called on the hot write path)
  (ContinuMail, 2026):
  - `BufferedBlockStore.BlockBufferResidencyForTest(ulong? liveRootBid)`: returns
    `(int count, long payload, long pending, long evictable)` summarising the resident block
    buffer (count of buffered blocks, payload bytes, pending-write bytes, and evictable-leaf
    bytes). Read-only snapshot; does not mutate buffer state.
  - `BufferedBTreePageStore.PageBufferResidencyForTest()`: returns `(int count, long bytes,
    long pending)` for the resident BTree-page buffer. Read-only snapshot.
  - `HeapOnNode.HeapResidencyForTest()`: returns `(int bufferCount, long decodedBytes,
    int freeIndexEntries, int blockAvailEntries)` for the decoded-heap-block cache and
    free-space index entry counts. Read-only snapshot; does not mutate heap state.
  - `PSTFile.AMapResidencyForTest()`: returns `(int pageCount, long bytes,
    int maxFreeGeneral, int maxFreeAligned)` for in-memory AMap pages and free-index list
    lengths cached on the file object. Read-only snapshot.

- Cross-platform hygiene — removed the dead Windows-only Desktop-Search service probe
  (ContinuMail, 2026):
  - `SearchManagementQueue.cs`: deleted the unreachable static helpers
    `IsWindowsDesktopSearchIndexingEnabled()` and `FindService(string)` (the only
    `System.ServiceProcess.ServiceController` callers) and the `using System.ServiceProcess;`.
    They were reachable only from a commented-out ctor line; the ctor hardcodes
    `m_isWindowsDesktopSearchQueuing = false`, so WDS update-queuing is permanently disabled and
    the `SearchDomainObject.ContainsNode`-driven queue path is unchanged. This lets the
    Windows-only `System.ServiceProcess.ServiceController` NuGet ref be dropped from
    `Mail2Pst.Core.csproj` for the Linux/macOS port. The vestigial `System.ServiceProcess`
    `<Reference>` in the non-building upstream `PSTFileFormat.csproj` (the legacy old-style
    project file, not part of the Mail2Pst build) was also cleared for consistency.

- `PropertySetGuid.cs`: added `PSETID_Address = {00062004-0000-0000-C000-000000000046}` after
  `PSETID_Appointment` (ContinuMail addition 2026: contacts property set, required for
  `PidLidEmail1EmailAddress` / `PidLidEmail2EmailAddress` / `PidLidEmail3EmailAddress` and
  all other contact-address LIDs). Proved working by a write/reopen/read round-trip test
  via `PropertyNameToIDMap.ObtainIDFromName` + `PropertyContext.SetStringProperty` (see
  `tests/Mail2Pst.Core.Tests/PSTFileFormat/NumericNamedPropertyTests.cs`).

- `Messaging/Messages/ContactMessage.cs` (new file): `IPM.Contact` item factory mirroring `Note.cs`.
  `ContactMessage.CreateNewContact(PSTFile, NodeID)` calls `CreateNewMessage(file,
  FolderItemTypeName.Contact, parentNodeID, searchKey)` (sets `PidTagMessageClass` to `"IPM.Contact"`
  via `GetMessageClass(Contact)`), wraps the result via `ContactMessage(PSTNode)`, and sets
  `MSGFLAG_READ`, `InternetCodepage=65001` (UTF-8 — not Note's 1255 Hebrew default), and normal
  importance/priority. Also exposes `GetContact(PSTFile, NodeID)` for round-trip reads (ContinuMail,
  2026).

- `ListsTablesAndProperties/Enums/PropertyID.cs`: added 25 tagged contact `PidTag*` constants
  (ContinuMail addition 2026: contact props) — `PidTagGivenName`, `PidTagSurname`,
  `PidTagMiddleName`, `PidTagNickname`, `PidTagCompanyName`, `PidTagTitle`,
  `PidTagDepartmentName`, `PidTagBusinessTelephoneNumber`, `PidTagHomeTelephoneNumber`,
  `PidTagMobileTelephoneNumber`, `PidTagBusinessFaxNumber`, `PidTagPagerTelephoneNumber`,
  `PidTagBusinessHomePage`, `PidTagBirthday`, `PidTagHomeAddressStreet`,
  `PidTagHomeAddressCity`, `PidTagHomeAddressStateOrProvince`, `PidTagHomeAddressPostalCode`,
  `PidTagHomeAddressCountry`, `PidTagBusinessAddressStreet`, `PidTagBusinessAddressCity`,
  `PidTagBusinessAddressStateOrProvince`, `PidTagBusinessAddressPostalCode`,
  `PidTagBusinessAddressCountry`. All were absent from the upstream enum; none conflict with
  existing names or values (ContinuMail, 2026).

- `Messaging/Enums/PropertyLongID.cs`: added 12 email-address named-property LID constants
  (ContinuMail addition 2026: contact props) — `PidLidEmail1DisplayName`,
  `PidLidEmail1AddressType`, `PidLidEmail1EmailAddress`, `PidLidEmail1OriginalDisplayName`,
  `PidLidEmail2DisplayName`, `PidLidEmail2AddressType`, `PidLidEmail2EmailAddress`,
  `PidLidEmail2OriginalDisplayName`, `PidLidEmail3DisplayName`, `PidLidEmail3AddressType`,
  `PidLidEmail3EmailAddress`, `PidLidEmail3OriginalDisplayName`. All were absent from the
  upstream enum; none conflict with existing names or values. Used with `PSETID_Address` (already
  added, see previous entry) to write contact email addresses as MAPI named properties
  (ContinuMail, 2026).

See the project git history (`git log -- vendor/PSTFileFormat`) for the full diffs.
