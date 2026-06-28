# Testing & validation

ContinuMail Converter rewrites mail archives, so correctness matters. This document
describes how the project is tested — both the automated suites you can run, and the
real-world validation method used on actual mail (which, being private, is **not** shipped).

## Automated tests

The engine and the desktop app each have a unit-test suite (~615 engine, ~214 desktop tests
as of v0.2.0, plus a separate engine round-trip integration harness; the suites, not the exact
counts, are the contract):

```bash
# Engine + CLI (xUnit)
dotnet test Mail2Pst.sln

# Desktop frontend (Vitest)
cd desktop && npm test
```

These cover mbox boundary/parse behaviour (including a worked-around MimeKit mbox EOF bug),
PST writing and metadata fidelity, size-based splitting, output-name validation, the
CLI JSON-Lines contract — plus the v0.2.0 surface:
Thunderbird `.msf`/Mork parsing and flag/junk/tag enrichment, tag → Outlook-category mapping
and colour-plan building, profile discovery and multi-account routing, and junk/expunged
handling.

The repository contains only **synthetic fixtures** (`fixtures/`) — no real mail.

## Real-corpus validation (methodology, no corpus shipped)

Releases are validated against **real** archives — Google Takeout exports and
Thunderbird/Exchange `.mbox` files — held locally and **never committed** (`testdata/` is
gitignored; it contains private mail). For each corpus the following is checked:

- **Counts:** messages reported by `scan` vs reported by `convert` vs visible in Outlook.
- **Bodies:** HTML rendering and the plain-text fallback.
- **Attachments:** regular files, inline CID images (no phantom paperclip), embedded
  `.eml` messages.
- **Folder structure:** both `mirror` and `flatten` mappings.
- **Metadata:** To/Cc/Bcc recipient types, dates, threading headers, and (from a live
  Thunderbird profile) read/unread, replied/forwarded/starred flags, junk, and tags → categories.
- **Splitting:** size-capped output produces complete, non-overlapping parts.
- **End-to-end:** the output PST is opened in Outlook and imported into a test
  Microsoft 365 profile.

## Manual PST validation (release checklist only)

`scanpst.exe` (the Outlook Inbox Repair Tool) is the authority on PST validity but is GUI-only,
so it is a manual release step, not a per-commit gate:

1. Generate representative PSTs (a single-part output and a split output).
2. Open each in Outlook.
3. Run `scanpst.exe` against each PST.
4. **Pass:** scanpst reports no errors / no repair required. **Fail:** scanpst recommends or performs
   any repair.
5. Record the exact scanpst wording in the private release checklist.

For automated, independent (non-Outlook) validation, see the `MAIL2PST_PST_VALIDATOR` gate in
`tests/Mail2Pst.Integration.Tests` and the `tools/pst-validate` crate (a dev/test-only tool, not
shipped in release artifacts).

## Contacts + photos (manual gate — required before any release that includes contacts)

Convert a real Thunderbird profile, open the output PST in Outlook, and confirm a
fully-filled contact shows every field AND its photo on the contact card. `pst-validate`
proves structure and message/contact counts only, not photo rendering, so this manual
check is required before a release that includes contacts.

## Validating your own conversion

You can build the same confidence with your own mail:

1. Keep your original `.mbox` (the tool only ever reads it).
2. Run `scan`, then `convert`.
3. Compare the message counts and review `conversion-report.txt` for skips/warnings.
4. Open the resulting `.pst` in Outlook (or import into a test profile) and spot-check
   folders, bodies, and attachments before relying on the output.
