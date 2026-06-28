# Changelog

All notable changes to ContinuMail Converter are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Thunderbird contacts now convert to PST.** Address books become Outlook contacts (real contact
  cards, not mail items), carrying names, company/department, job title, all phone types, home/business
  addresses, birthday, websites, notes, custom fields — **and contact photos**. Both modern Thunderbird
  (the `abook*.sqlite` address books, read via their stored vCard) and legacy `.mab` address books are
  supported. When you convert a Thunderbird profile, contacts are included automatically; a
  `--no-contacts` flag (and a future GUI toggle) opts out. Contacts land in an `IPF.Contact` folder per
  address book and validate cleanly in Outlook and `scanpst.exe`.

### Internal
- New contact pipeline (`ContactRecord` model, SQLite + Mork readers, a shared vCard mapper, and an
  `IPM.Contact` writer) added as a distinct write phase that reuses the existing PST size-split,
  checkpoint, and reporting machinery. Reading is **vCard-first** (modern Thunderbird stores rich contact
  detail only in a per-card vCard blob) with the denormalized index rows as a fallback, so a malformed or
  sparse card still yields a usable contact. Adds two pinned, GPL-compatible NuGet dependencies —
  `Microsoft.Data.Sqlite` and `FolkerKinzel.VCards` (both MIT-family) — recorded in `NOTICE`. The
  independent `pst-validate` round-trip gate now covers contact folders.

## [0.2.3] — 2026-06-28

### Added
- **The command-line converter now runs on Linux and macOS**, not just Windows. The engine is fully
  cross-platform; prebuilt single-file CLI binaries are published for Windows, Linux, and macOS. (The
  desktop app remains Windows-only for now.)

### Changed
- The desktop app's conversion step is slightly faster to start: it reuses the message total from the
  scan it just ran instead of counting every mailbox a second time before converting. Behaviour and
  progress reporting are otherwise unchanged. (Direct CLI users can do the same with a new
  `convert --expected-total <n>` flag.)

### Fixed
- **Inline images that are also real attachments stay visible.** A part that was both referenced inline
  (CID) and present as an explicit attachment was being hidden, so it could go missing from the
  attachment list. Such parts are now kept visible; purely-inline images remain hidden as before.
- **More accurate size/count estimates for forwarded-as-attachment messages.** The `scan` estimate no
  longer decodes the body of an embedded (`message/rfc822`) message just to measure it — improving both
  accuracy and scan memory use.
- **Cancelling a conversion no longer leaves a stray temp file.** Cancelling while an attachment was
  mid-flight could leak one temporary file; it is now disposed on cancel.

### Internal
- CI runs the desktop Rust unit and sidecar-integration tests (a Windows `cargo test` job), and builds +
  tests the engine on a Windows/Linux/macOS matrix (each leg also publishing the CLI single-file and
  smoke-running `scan`). Removed the unused Windows-only `System.ServiceProcess.ServiceController`
  dependency.

## [0.2.2] — 2026-06-28

### Changed
- Scanning is now **parallel and dramatically faster**. The dry-run that counts messages and estimates
  output size — the CLI `scan` step and the desktop app's pre-conversion scan — now parses each mailbox
  as several message-aligned byte ranges across CPU cores instead of one sequential pass. On a large
  multi-GB archive this is roughly **3–4× faster** (a 4.5 GB Gmail export scans in ~13 s instead of
  ~47 s), with no slowdown on a single very large folder. Peak memory during a scan is now **bounded and
  independent of mailbox or message size** (large messages spill to a temp file rather than residing in
  memory), so scanning a multi-GB archive no longer grows memory with the file. Scan results — counts,
  size estimates, dates, and warnings — are byte-for-byte identical to the previous sequential scan, and
  a scan-only parse never materialises attachment bytes or writes attachment temp files. (CLI scan JSON
  output and the GUI scan contract are unchanged.)

### Fixed
- The optional **"Import category colours"** step no longer hangs when the categories are already in
  Outlook. Re-importing colours that already existed in Outlook's master list left the brief background
  Outlook running and the import spinner stuck indefinitely. The importer now always shuts that temporary
  Outlook down (and finishes in a few seconds when there's nothing new to add), and the desktop app caps
  the step so it can't get stuck. No effect on the colours themselves.
- Thunderbird `.msf` reading no longer fails on a **reparsed folder**. A folder that Thunderbird had
  reparsed left a table-rebuild fragment in its `.msf` that the reader mistook for a second message
  table, so it gave up on that folder's metadata ("found 2 msgs tables") and fell back to mbox flags.
  The reader now recognises the rebuild marker and folds it into the real table, so tags, read/unread,
  and flags come across for those folders too. Affected only metadata fidelity — message content and
  folder structure were never at risk.

## [0.2.1] — 2026-06-27

### Added
- Thunderbird `.msf` enrichment now preserves **message priority** set inside Thunderbird. A
  priority a user or filter applied in Thunderbird is stored only in the `.msf` (not in the mbox
  headers), so it was previously lost; it is now read and written to Outlook's importance. Priority
  that arrived on the message's own `X-Priority`/`Importance` header was already preserved.
- Thunderbird `.msf` enrichment now drops **uncompacted dead copies**. An un-compacted Thunderbird
  folder can still hold old physical copies of messages it no longer shows; the converter now reads
  each `.msf` row's live mbox byte offset (`storeToken`, falling back to `msgOffset`) and exports
  only the messages Thunderbird still lists, so the PST matches the folder you see. Filtering
  activates only when every live row maps to a real mbox message boundary — on any uncertainty it
  keeps every message, so unique mail is never dropped.

### Changed
- Writer: large attachments (≥ 16 MB) are now written by streaming. A single large attachment
  previously caused writer-side peak memory to spike to a multiple of its size — the vendored
  layer materialised the full payload in memory while building the data blocks. The attachment
  data is now fed through a batched, bounded-residency pipeline, so the **writer-side** contribution
  to peak memory stays bounded regardless of attachment size (verified end-to-end: resident block
  count stays flat from 9 MB to 512 MB attachments). Small and medium attachments (< 16 MB) keep the
  existing `byte[]` path; output is byte-identical and is structure-validated by the independent
  MS-PST reader (`tools/pst-validate`). Note: the mbox parser still materialises one message at a
  time, so total process memory for a single multi-GB attachment remains parser-bound — removing
  that end-to-end ceiling is separate, future work.
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
- Thunderbird enrichment is far quieter on real profiles. Two harmless internal conditions no
  longer raise per-folder warnings: (1) an **empty folder** (whose `.msf` has no message table) is
  now treated as "no messages" instead of an unreadable-`.msf` warning; (2) the dead-copy
  **live-offset filter declining to run** — the common, expected case for IMAP accounts, whose `.msf`
  rows usually carry no usable byte offset — is now silent. In both cases behaviour is unchanged
  (every message is still exported and flags/tags still applied); only the noise is gone. A genuinely
  corrupt `.msf`, and an ambiguous multi-table `.msf`, still warn. The aggregate filter-disabled count
  remains in the conversion report for diagnostics.

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
