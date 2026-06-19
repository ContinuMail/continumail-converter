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

See the project git history (`git log -- vendor/PSTFileFormat`) for the full diffs.
