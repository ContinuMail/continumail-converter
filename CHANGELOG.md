# Changelog

All notable changes to ContinuMail Converter are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Thunderbird `.msf` enrichment now preserves **message priority** set inside Thunderbird. A
  priority a user or filter applied in Thunderbird is stored only in the `.msf` (not in the mbox
  headers), so it was previously lost; it is now read and written to Outlook's importance. Priority
  that arrived on the message's own `X-Priority`/`Importance` header was already preserved.

### Changed
- Writer: PST files are now built from scratch via `PSTFile.CreateEmptyStore()` instead of
  copying a pre-seeded blank seed file. The blank-seed asset, the seed-extraction helper,
  and the dev-only seed-regeneration tool have been retired; `PstWriter`, `PstPartManager`,
  and `ConversionRunner` no longer accept or require a seed file path.
- Writer: large folders convert dramatically faster. Adding a message was effectively
  O(n²) in the folder's message count — the vendored heap allocator located free space with
  a linear block scan that grew as a folder filled — so very large single folders slowed down
  progressively. The allocator now uses a maintained best-fit free-space index, keeping
  per-message cost flat. A ~16,000-message folder that took ~70 s now takes ~40 s; output
  validity is unchanged (verified against an independent MS-PST reader).

### Fixed
- Writer: a failed split (e.g. the next part can't be created mid-conversion) no longer
  deletes the already-completed previous part or leaves a stray blank part behind; the
  failure still surfaces as fatal, but completed output is preserved.
- Writer: a message carrying an attachment larger than a PST attachment can store
  (PidTagAttachSize is a 32-bit value) is now skipped and reported instead of writing a
  wrapped size or risking an out-of-memory load of the whole attachment.
- Desktop: the per-run temp config file is now removed if the conversion engine fails to
  launch (sidecar missing or spawn error), instead of being left behind in the temp directory.

## [0.2.0] — 2026-06-23

**Major feature enrichments — and a hard-won grudge against Mozilla Mork — ship with 0.2.0. The
headline is Thunderbird profile conversion.**

ContinuMail Converter now reads Thunderbird's `.msf` / **Mork** metadata. Point it at a profile and
your full folder and subfolder tree comes across — and the per-message state the mbox alone throws
away (**read/unread, stars, replied/forwarded flags, junk, and tags**) is translated into native
**MAPI properties (PID tags)** and shows up natively in Outlook. Tags become Outlook **categories**
under your real Thunderbird tag names. It's paired with a **scan-for-profiles** step that
automatically matches every mail account and local archive with its `.msf` sidecar — no hunting for
files. This resolves the v0.1.2 "Thunderbird flags/starred not preserved" and "tags unsupported"
limitations for live-profile conversions.

### New in 0.2.0 (CLI + GUI)
- **Thunderbird `.msf` / Mork enrichment** — read/unread, replied, forwarded, starred (→ follow-up
  flag), junk, and tags, recovered from a live profile and written as native MAPI properties.
  Degrades gracefully: an unreadable or mismatched `.msf` is skipped per-source and reported, never
  fatal.
- **Tags → Outlook categories** using your real tag display-names from `prefs.js` (non-ASCII included).
- **Scan for profiles** — auto-discovers Thunderbird profiles and pairs every mail account and local
  archive with its `.msf` sidecar; per-account detection (email / host) included.
- **Multi-account → one PST per account** by default (each account a top-level folder), with a
  "Combine into a single PST" toggle.
- **Junk handling** — leave / tag as a "Junk" category / move to a Junk Email folder.
- **Drop expunged messages** — optionally skip mail Thunderbird marked deleted but hasn't compacted
  out yet.
- **Outlook category colours** — `import-colours` (CLI) and a one-click card on the Done screen (GUI)
  push your Thunderbird tag colours into Outlook so the categories match.

**On category colours and COM:** category *assignments* live in the PST as MAPI properties (PID tags)
and need no Outlook. The **Master Category List** — the names-and-colours registry Outlook draws from
— does **not** live in the PST and can't be written from PID tags, so colour import drives Outlook
over COM to edit the CategoryList FAI message atomically (Windows only; Outlook installed and
**closed**). A `--plan-file` mode applies a previewed plan without re-scanning.

### Hardening, security & other changes
- **Mork reader — nested folders no longer read as empty.** Real `.msf` files reuse a small numeric
  table id across scopes; tables are now keyed by the composite (scope, id), so enrichment no longer
  silently finds zero messages on real profiles.
- **Mork reader — wider charset support** (windows-1252, Shift-JIS, Big5, EUC-JP, via the runtime
  code-page provider) and more robust column-dictionary detection.
- **`import-colours` fixes** — correct bytes for non-ASCII categories (e.g. "ÆØÅ"); only shuts down an
  Outlook instance it actually started; excludes the `NonJunk` pseudo-tag from candidates.
- **More mbox `X-Mozilla-Status` flags even without a profile** — replied / forwarded / starred added
  to read/unread (POP / older Local Folders stores; no effect on flag-less IMAP/EWS exports) (#7).
- **Faster large-folder writes** — per-folder `SaveChanges` is batched at checkpoints instead of after
  every message. No behaviour change.
- **GUI polish** — real source count on the Scanning screen (was "0 files"); one box per account on
  the Source step (was "first@x +N more"); full-height rail with no sideways scroll; multi-account
  defaults to split; stepper Back-navigation reaches the Accounts step.
- **Security** — the vendored multi-string deserializer validates its item count with unsigned
  arithmetic against the buffer length before casting, turning a malformed/oversized count into a
  clean `InvalidDataException` instead of `OverflowException`/`OutOfMemoryException`.

### Known limitations
- **`.msf` enrichment needs a live profile** (the mbox plus its sibling `.msf`). A bare exported
  `.mbox` still imports as before — only inline `X-Mozilla-Status` flags are recoverable, not
  tags / junk / expunged state.
- **`import-colours` is Windows-only and needs Outlook installed and closed.** Conversion itself still
  requires no Outlook — only tag *colours* do.

### Internal
- New from-scratch, read-only **Mork (`.msf`) reader** (`src/Mail2Pst.Core/Mork/`) with enrichment
  built on top (`src/Mail2Pst.Core/Msf/`); extensive unit + env-gated real-corpus tests.
- Pure, COM-free, unit-tested category-colour XML/stream helpers extracted from the colour-import path.
- Desktop helper extractions (account grouping, PST-name sanitization/de-dup, output-path join,
  discovery parsing) and the single public-primary repo migration with a `leak-check` safety net.

## [0.1.2] — 2026-06-18

A feature + hardening release. The headline is **Thunderbird subfolder (`.sbd`) support** — a new
CLI `discover` command that reconstructs a nested folder tree from a Thunderbird mail directory and
converts it to nested PST folders. **This is currently CLI-only; the desktop app does not yet expose
it.** The rest of the release is correctness, security, and robustness work that benefits everyone,
desktop users included.

### Added
- **Thunderbird `.sbd` nested-folder support (CLI).** A new `discover` command walks a mail-files
  directory (e.g. a Thunderbird "Local Folders" or account directory), reconstructs the nested tree
  from Thunderbird's `<name>` + sibling `<name>.sbd/` layout, and emits a JSON source list with an
  explicit nested `targetFolderPath` per source. Feeding that into `convert` produces a PST with the
  folder hierarchy preserved. A plain directory of `.mbox` files still works as before.
- **Nested folder paths in config.** `convert` sources accept a `targetFolderPath` array (e.g.
  `["Inbox","Archive","2026"]`) to place mail into nested PST folders; the engine creates the full
  path on demand. The existing `targetFolder` string remains single-segment shorthand.
- **`schemaVersion` on every CLI JSON event** so consumers can detect contract changes; the desktop
  app checks it permissively.

### Changed
- **CLI project/binary renamed `mbox2pst` → `mail2pst`** (sidecar `mail2pst-cli`). The product name
  (ContinuMail Converter) is unchanged; this only affects anyone invoking the CLI by its old name.
- **PST size-splitting is now predictive** — a part splits *before* a message would cross the cap,
  rather than only at a periodic checkpoint (which could overshoot a small custom cap).
- **Desktop app now uses a restrictive, local-only Content-Security-Policy** (previously unset).

### Fixed
- **NDR/bounce phantom attachment (KB-001).** Bounce notifications no longer surface a phantom
  embedded-message attachment in Outlook, regardless of how deeply the `multipart/report` is nested.
- **Stricter, cross-platform output-name validation** — reserved names, trailing space/period, and
  platform-invalid characters are rejected up front (#17).
- **Envelope-postmark validation** — the `From ` boundary validates the day-of-week and month, so a
  body line that merely looks like a postmark can't cause a mis-split (#2).
- **More accurate (component-aware) split-cap size estimate**, so very large messages split correctly (#4).
- A permission-denied source is **skipped and reported** instead of crashing the run; an empty sender
  address no longer fails a write; a config/output-dir setup failure emits a fatal JSON error instead
  of a bare crash; the mbox parse enumerator is disposed promptly on early exit.

### Known limitations
- **Thunderbird read/unread & starred state is not preserved.** Thunderbird stores per-message flag
  state in its `.msf` index, not the mbox, so an mbox-based conversion cannot recover it (messages
  generally import as read & unflagged). Read/unread *is* preserved for archives whose mbox carries
  real `X-Mozilla-Status` flags. (KB-002.)

### Internal
- New format-agnostic **round-trip integration test harness** (parse → write → PST read-back →
  compare), **CI** on every push/PR, and **Dependabot**. The PST writer and MIME mapping were
  decomposed for maintainability. No behaviour change.

## [0.1.1] — 2026-06-16

A post-launch correctness and robustness release. The headline is a **data-loss fix**:
some mbox files could silently merge messages. Upgrading is recommended.

### Fixed
- **Silent message loss on mbox files without blank separators.** A message boundary was
  previously only detected after a blank line, so two messages joined without one were merged
  into a single message — losing the second. Boundary detection now also recognizes the
  envelope ("From ") postmark line by its asctime shape. Validated against a real corpus of
  45,000+ messages with zero over-splitting.
- **Body corruption on mboxrd archives (e.g. Gmail Takeout).** Body lines beginning with
  `From ` are stored escaped as `>From `; these are now un-escaped so message text round-trips
  exactly.
- **Desktop app could hang at the end of a conversion.** The terminal `done` event could be
  dropped when the engine exited quickly; the convert output is now buffered with an exit
  fallback so the UI always finishes.

### Changed
- **Write failures are no longer silently skipped.** A PST write error — an engine bug, a
  vendored-library failure, or invalid internal state — is now **fatal**: the converter stops
  and reports it instead of producing a quietly-incomplete PST. A failed run deletes its
  in-progress PST part rather than leaving a half-written file that looks usable. (Malformed
  *input* is still skipped/warned, as before.)
- **Folder names are now validated by the engine,** not only the desktop UI — a hand-written
  config or direct CLI use can no longer place an unsafe folder name into the PST.
- **PST size-splitting no longer overshoots the cap.** When an output PST is split by size, the
  writer now splits *predictively* — before a message would cross the configured maximum — rather
  than only checking at a periodic interval (which could overshoot a small custom split size by up
  to ~500 messages).
- **Review screen:** sort the folder list by clicking column headers (▲/▼ for direction); a note
  on the Options screen clarifies that renaming sets a folder's name, not Outlook's display order.
- Documentation now describes large-attachment spill-to-disk accurately as bounding *queued*
  memory rather than implying full end-to-end streaming.

### Security
- **Upgraded MimeKit 4.9.0 → 4.17.0,** clearing the moderate `GHSA-g7hc-96xr-gvvx` (`NU1902`)
  advisory.
- **Added a restrictive Content-Security-Policy to the desktop app** (previously unset): it is
  limited to its own bundled assets and local IPC — no external network origins.

### Unchanged
- The CLI JSON Lines event contract (`scan` / `convert` / `version`) is unchanged.

## [0.1.0] — 2026-06-16

Initial public release. Convert mbox mail archives into Outlook PST files **without Outlook or
any Microsoft library installed** — a CLI engine plus a desktop GUI (Windows).

- mbox input (eml/msg deferred to a later release).
- Mirror (one folder per source file) or flatten folder mapping; size-based PST splitting.
- Metadata fidelity: To/Cc/Bcc, read/unread, importance, Message-ID/threading, HTML + plain-text
  bodies, attachments (inline CID handled).
- Free and open source (GPL-3.0-or-later); bundles the LGPLv3 PSTFileFormat reader/writer.
