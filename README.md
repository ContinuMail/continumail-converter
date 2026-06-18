# ContinuMail Converter

**Convert Gmail Takeout and other mbox mail archives into Outlook PST files — locally, with no upload and no Outlook installation required.**

[![License: GPL-3.0-or-later](https://img.shields.io/badge/License-GPL--3.0--or--later-blue.svg)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/ContinuMail/continumail-converter?label=release)](../../releases/latest)
[![Platform: Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)](#-download--install)
[![Website: continumail.com](https://img.shields.io/badge/web-continumail.com-b05a36)](https://www.continumail.com/)

<p align="center">
  <img src="media/options.png" width="860"
       alt="ContinuMail Converter — the Options screen: choose folder mapping and split size, with a live preview of the resulting PST folder tree">
</p>

## ✨ Highlights

- **No Outlook, no COM automation.** Writes `.pst` directly with a from-scratch PST engine — nothing to install, nothing hijacked.
- **Private & local-first.** Reads your archive on disk, makes **no network connections**, uploads nothing. Originals are only ever read.
- **Gmail-Takeout–aware.** Built and validated against real Google Takeout exports — including the quirks and a MimeKit mbox bug that truncates some archives (worked around here).
- **Faithful.** Preserves folder structure (including Thunderbird subfolders via the CLI), attachments (inline images + embedded `.eml`), HTML bodies, dates, To/Cc/Bcc, importance, and threading headers.
- **Free, open, and it stays free** — GPL-3.0-or-later, part of the [ContinuMail](#-license) family of honest mail tools.

> **Early release (0.x).** Covered by a comprehensive automated test suite and validated on real Gmail Takeout and Thunderbird/Exchange exports — but please keep backups and validate output before relying on it. See [`TESTING.md`](TESTING.md) for how releases are validated.

## Overview

mbox → PST is genuinely hard to get right, and most tools that do it are paid and closed-source. The maintainer is an MSP technician who hit this on migration after migration — mail stranded in old ISP mailboxes, ancient Thunderbird, POP3 — with no good free path to something modern like Microsoft 365. So this got built, and it's given away. It stands on excellent existing work (see [Credits](#-credits)); what's added is a free, open, local-first tool for the messy real-world archives that keep showing up.

Most free/open options write PST by remote-controlling a running copy of Outlook (COM automation) — Outlook must be installed and licensed, and it's locked for the entire run. ContinuMail is **100% independent of Outlook**: it writes PST directly via the vendored, genuinely from-scratch [PSTFileFormat](https://github.com/ROM-Knowledgeware/PSTFileFormat) engine. You can keep working while it runs.

## 📥 Download & install

1. Download the latest installer from the [**Releases**](../../releases/latest) page: `ContinuMail Converter_<version>_x64-setup.exe`.
2. Run it. ContinuMail installs for the current user (no admin needed) and adds a Start-menu shortcut. Windows 10/11, 64-bit.

> **"Windows protected your PC"?** This early release is **not yet code-signed**, so SmartScreen may warn about an "unknown publisher." If you trust this source, click **More info → Run anyway**. (Code signing is [on the roadmap](#-roadmap).)

Prefer the command line? See [Command-line interface](#-command-line-interface).

## 🚀 Using the app

The desktop app walks you through the whole conversion; your originals are never modified.

1. **Source** — pick `.mbox` files or a folder of them.
2. **Scanning** — a fast dry-run counts messages and estimates output size before anything is written.
3. **Review** — see per-folder counts; choose whether to include empty folders.
4. **Options** — pick folder mapping (mirror or flatten), set a split size, preview the resulting PST folder tree, and rename folders if you like.
5. **Convert** — watch live progress (count, MB/s, ETA); cancel any time.
6. **Done** — open the output folder, or review any warnings.

## 🎯 Features

- **mbox input** via a custom mbox splitter plus MimeKit entity parsing (robust to the Gmail Takeout / MimeKit EOF quirk).
- **Folder mapping:** mirror (one PST folder per source file), flatten, or per-source custom target folder. Empty source folders can be kept as empty PST folders.
- **Size-based splitting:** one PST per output group, auto-split into `Name-1.pst`, `Name-2.pst`, … when a size cap is exceeded (a single un-split output is just `Name.pst`).
- **HTML bodies** (`PidTagHtml` + `PidTagNativeBody`) with an always-present plain-text fallback.
- **Full attachments:** regular files, inline CID images (correctly hidden, so no phantom paperclip), and embedded `.eml` messages.
- **Metadata fidelity:** To/Cc/Bcc recipient types, importance/priority, Message-ID / In-Reply-To / References, conversation topic. (Read/unread, replied, forwarded, and starred state are preserved when the source mbox carries `X-Mozilla-Status` flags — typical of POP accounts and older Local Folders stores; see [Limitations](#-limitations) for when flags are absent.) Bcc recipients are preserved with their MAPI Bcc type and appear in Outlook's Bcc field (this is a faithful archive of mail you already sent — nothing to hide in a local store).
- **Memory-friendly:** large attachments (≥ 4 MB) spill to a temp file so many don't pile up in memory at once.
- **Conversion reports** (human-readable + JSON) listing skipped messages and warnings.

## ⚠️ Limitations

- **Non-UTF-8 / UTF-16 mbox envelopes.** The mbox envelope and headers are read as UTF-8/ASCII; archives whose *envelope framing* uses other encodings may not parse cleanly. (Message **bodies** honour their own MIME charset — this caveat is about the mbox framing, not body text.)
- **Plain-text body is a best-effort fallback.** Real HTML is preserved faithfully (`PidTagHtml`) and is what Outlook displays; the generated plain-text alternative is a lightweight stripper, not a full renderer.
- **Read/unread & starred state from Thunderbird is not preserved for IMAP/EWS accounts.** Thunderbird stores per-message flag state in its own index (`.msf`), not in the mbox, so an mbox-based conversion cannot recover it for modern server-backed stores — messages import as read and unflagged. When the mbox *does* carry `X-Mozilla-Status` (POP accounts, older Local Folders stores), the converter honors read/unread, replied (→ reply arrow), forwarded (→ forward arrow), and starred (→ follow-up flag). Tags (`X-Mozilla-Keys`) remain unsupported.

## ⌨️ Command-line interface

The same engine ships as a CLI (the desktop app bundles and drives it) — useful for automation and large batch jobs. It uses subcommands:

```bash
# Dry-run: per-source breakdown (counts, sizes, date range), writing nothing.
dotnet run --project src/Mail2Pst.Cli -- scan --input <path.mbox> [--input <path2.mbox> ...]

# Discover: walk a Thunderbird mail directory and emit a nested source list (no parsing).
dotnet run --project src/Mail2Pst.Cli -- discover --input <mail-dir>

# Convert: JSON Lines progress to stdout, PST + reports to the output directory.
dotnet run --project src/Mail2Pst.Cli -- convert --config <config.json> --output <output-dir>
```

A minimal working example lives in `fixtures/sample-config.json` + `fixtures/sample.mbox`. Send `cancel` on the process's **stdin** (or `SIGTERM` / `Ctrl+C`) to stop a running `convert` cleanly. Exit codes: **0** success, **1** fatal error, **2** cancelled.

### 🗂️ Thunderbird subfolders (`discover`)

Point `discover` at a Thunderbird mail directory — a file `Inbox` sitting beside a sibling `Inbox.sbd/` that holds its subfolders — and it reconstructs the nested tree, emitting one source per folder with a nested `targetFolderPath` (parse-free and fast). Feed those into a `convert` config and the resulting PST preserves the folder hierarchy, including parent folders that have both their own mail and subfolders. A plain directory of `.mbox` files is handled too (each becomes a top-level folder).

> **CLI-only for now.** This nested-folder/Thunderbird support is available through the `discover` command; the desktop app does not yet expose it (planned). The desktop app handles flat `.mbox` archives.

<details>
<summary><b>Configuration, scan output, and JSON Lines reference</b></summary>

A config describes one or more output groups (each becomes a PST file group):

```json
{
  "outputs": [
    {
      "name": "Personal",
      "maxSizeMB": 45000,
      "folderMapping": "mirror",
      "includeEmptyFolders": true,
      "sources": [
        { "path": "Takeout/Mail/Inbox.mbox", "type": "mbox" }
      ]
    }
  ]
}
```

- `folderMapping`: `mirror` (one PST folder per source file, named after it) or `flatten` (everything into one folder unless a source sets `targetFolder`).
- `maxSizeMB`: split into `Name-1.pst`, `Name-2.pst`, … when exceeded; a single un-split output is just `Name.pst`.
- `includeEmptyFolders`: keep empty source folders as empty PST folders.
- `targetFolderPath` (per source): an array — e.g. `["Inbox", "Archive", "2024"]` — placing that source's mail into a **nested** PST folder; the engine creates the whole path on demand. `targetFolder` (string) is single-segment shorthand for the same thing. Set one or the other, not both. `discover` fills these in for you from a Thunderbird `.sbd` tree.

**`scan`** prints a single pretty-printed JSON object with run-wide `totals` and a per-source breakdown. `bytes` is the estimated PST content size; `sourceBytes` is the raw mbox size; `dateFrom`/`dateTo` are `null` when a source has no dated messages.

```json
{
  "type": "scan",
  "totals": { "messages": 17989, "bytes": 2007117000, "sourceBytes": 2400000000, "sources": 2 },
  "sources": [
    { "id": "inbox", "path": "Takeout/Mail/Inbox.mbox", "displayName": "Inbox",
      "messages": 12000, "bytes": 98765432, "sourceBytes": 123456789,
      "dateFrom": "2012-01-01T00:00:00Z", "dateTo": "2024-12-31T00:00:00Z",
      "warnings": 0, "skipped": 0 }
  ],
  "skipped": [],
  "warnings": []
}
```

**`discover`** prints a single JSON object describing the reconstructed tree — one `sources[]` entry per mail file with its nested `targetFolderPath` (ready to drop into a `convert` config). Folder-name issues surface as `warnings[]`; index/metadata files (`.msf`) are summarised in `skipped[]`.

```json
{
  "type": "discovery",
  "root": "C:/.../Mail/Local Folders",
  "layout": "thunderbird",
  "sources": [
    { "path": ".../Inbox", "type": "mbox", "targetFolderPath": ["Inbox"], "displayName": "Inbox", "sourceBytes": 85924142 },
    { "path": ".../Inbox.sbd/Archive", "type": "mbox", "targetFolderPath": ["Inbox", "Archive"], "displayName": "Archive", "sourceBytes": 1245184 }
  ],
  "warnings": [],
  "skipped": []
}
```

**`convert`** streams one JSON object per line on stdout, ending with exactly one terminal event (`done`, `error`, or `cancelled`):

```json
{"type":"started","input":"config.json","outputDirectory":"out/"}
{"type":"scan","totalMessages":17989}
{"type":"progress","converted":1000,"total":17989,"warnings":2,"skipped":0,"currentSource":"Inbox.mbox","currentFolder":"Inbox"}
{"type":"warning","source":"Inbox.mbox","identifier":"message #52","reason":"Dropped attachment 'bad.bin': invalid encoding"}
{"type":"done","converted":17989,"skipped":0,"warnings":1,"outputs":["out/Personal.pst"],"outputDirectory":"out/","report":"out/conversion-report.json","elapsedMs":132000}
```

The output directory also gets `conversion-report.txt` (human-readable) and `conversion-report.json` (same data, machine-readable).

</details>

## 🔧 Build from source

**Engine + CLI** — requires the [.NET 8 SDK](https://dotnet.microsoft.com/download). Windows is the primary target (the PST engine uses some Windows APIs).

```bash
dotnet build Mail2Pst.sln
dotnet test  Mail2Pst.sln
```

**Desktop app** — a [Tauri](https://tauri.app/) v2 + React (Vite) app that bundles the CLI as a sidecar and drives it via the JSON Lines contract above; produces an NSIS installer for Windows 10/11.

Prerequisites: [Node.js](https://nodejs.org/) (LTS) + npm, the [Rust toolchain](https://rustup.rs/) with the MSVC target (`rustup target add x86_64-pc-windows-msvc`), the [.NET 8 SDK](https://dotnet.microsoft.com/download) (for the sidecar), and WebView2 (present on Windows 11; the installer ensures it on Windows 10).

```bash
cd desktop
npm install
npm run tauri dev     # run the app in dev mode
npm test              # frontend unit tests (Vitest)
npm run tauri build   # release build + NSIS installer
```

> The CLI sidecar build is Windows / win-x64 only (matching the v1 Windows-first scope) and runs automatically before `tauri dev`/`build`. Cross-platform builds are deferred to a future release.

## 🔒 Privacy & disclaimer

ContinuMail Converter reads your archive locally and writes the PST locally. It makes **no network connections** and **uploads nothing**; your originals are only ever read, never modified. See [`SECURITY.md`](SECURITY.md) for the app's security posture and Content Security Policy.

It is free software, provided **"as is", without warranty of any kind** (see [`LICENSE`](LICENSE)). Email history matters, so please **keep a backup** of your originals, **validate** the output (open the PST, or import into a test Outlook profile), and **review the conversion report** before relying on converted data. The installer is not yet code-signed (see [Download & install](#-download--install)).

## 🗺️ Roadmap

ContinuMail Converter aims to be a genuinely **free, open alternative to the paid, closed email-conversion products** that dominate this space. mbox came first on purpose — in the field it's both the most common archive and the hardest to convert faithfully. Next up:

- **More formats** — EML and MSG input next (mbox-only today).
- **Code signing** of the installer (to remove the SmartScreen prompt).
- **Cross-platform** builds (Windows-only today).

The converter is free and open-source and stays that way. (ContinuMail follows an open-core model: this converter is free forever; any paid products are separate.)

## 🤝 Contributing & feedback

Found a bug, a quirky archive, or want a feature? [**Open an issue**](../../issues). Contributions are welcome under a lightweight CLA — see [`CONTRIBUTING.md`](CONTRIBUTING.md). To report a security issue privately, see [`SECURITY.md`](SECURITY.md).

**📚 Further reading:** [`TESTING.md`](TESTING.md) (how it's validated) · [`TEMPLATE-PROVENANCE.md`](TEMPLATE-PROVENANCE.md) (the bundled PST template) · [`SECURITY.md`](SECURITY.md) · [`TRADEMARKS.md`](TRADEMARKS.md) · [How it works](#-how-it-works-for-contributors).

## 📄 License

ContinuMail Converter's own code is licensed under the **GNU General Public License v3.0 or later** (**GPL-3.0-or-later**) — see [`LICENSE`](LICENSE). It bundles third-party components under their own GPL-compatible licenses (see [`NOTICE`](NOTICE)):

- **PSTFileFormat** (the MS-PST read/write engine) under **LGPLv3** — vendored under `vendor/PSTFileFormat/` with local modifications; the complete corresponding source is included, so it can be modified and the app rebuilt.
- **MimeKit** under the **MIT License**.

The **ContinuMail** name, logos, icons, and brand assets are **not** covered by the GPL and remain proprietary — see [`TRADEMARKS.md`](TRADEMARKS.md).

## 🙏 Credits

- **[ROM-Knowledgeware/PSTFileFormat](https://github.com/ROM-Knowledgeware/PSTFileFormat)** by **Tal Aloni** — the rare, from-scratch MS-PST reader/writer that makes ContinuMail's Outlook-independence possible. **ContinuMail would not exist without it.** (Vendored, with local modifications, per the LGPL.)
- **[MimeKit](https://github.com/jstedfast/MimeKit)** by **Jeffrey Stedfast** — the MIME parsing that reads every message.

## 🧑‍💻 How it works (for contributors)

Writing PST files is the hard part: `libpst`/`java-libpst` are read-only, and most "mbox to PST" tools drive Outlook via COM. PSTFileFormat is a real from-scratch writer, but it can only *open* an existing PST — so the tool ships a small blank Unicode PST template (`assets/template.pst`) and writes into a copy of it. (See [`TEMPLATE-PROVENANCE.md`](TEMPLATE-PROVENANCE.md) for how that template was generated and how to verify it.)

Key things to know when working on the code:

- `MboxParser` does **not** use MimeKit's `MimeFormat.Mbox` parser — it splits the file into per-message chunks (mboxrd convention) and parses each as a MIME entity, working around a MimeKit bug that truncates some real archives.
- Use `parentFolder.CreateChildFolder(name, type)` (not `CreateNewFolder()`), and call `folder.SaveChanges()` before `file.EndSavingChanges()`.
- The vendored allocation scan was made O(1) (AMap cache + free-space index) to keep large PST writes fast.

**Layout:** `src/Mail2Pst.Core/` (engine), `src/Mail2Pst.Cli/` (CLI), `desktop/` (Tauri + React app), `tests/Mail2Pst.Core.Tests/` (xUnit), `vendor/PSTFileFormat/` (vendored, LGPLv3), `assets/template.pst` (write seed), `tools/template-gen/` (dev-only template regenerator).
