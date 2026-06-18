# Changelog

All notable changes to ContinuMail Converter are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **More Thunderbird/mbox status flags preserved.** When a source mbox carries `X-Mozilla-Status`
  flags (typical of POP accounts and older Local Folders stores), the converter now also preserves
  **replied** (→ Outlook reply arrow), **forwarded** (→ forward arrow), and **starred/marked**
  (→ follow-up flag), in addition to read/unread. Modern IMAP/EWS exports carry no flags in the
  mbox, so this has no effect there; tags (`X-Mozilla-Keys`) remain unsupported.

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
