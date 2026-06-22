# ContinuMail Converter — desktop app

The Windows desktop GUI for [ContinuMail Converter](../README.md). A **Tauri v2 + React (Vite) +
TypeScript** app that bundles the `mail2pst` CLI engine as a **sidecar** and drives it over the
CLI's JSON-Lines contract — so the app never reimplements conversion logic, it orchestrates the
same engine the CLI uses.

## Develop

Prerequisites: [Node.js](https://nodejs.org/) (LTS) + npm, the [Rust toolchain](https://rustup.rs/)
with the MSVC target (`rustup target add x86_64-pc-windows-msvc`), and the
[.NET 8 SDK](https://dotnet.microsoft.com/download) (to build the sidecar). Windows 10/11.

```bash
npm install
npm run tauri dev     # run the app (the CLI sidecar auto-builds first, via the `pretauri` hook)
npm test              # frontend unit tests (Vitest)
npm run tauri build   # release build → NSIS installer under src-tauri/target/release/bundle/nsis/
npm run sidecar       # rebuild just the CLI sidecar (win-x64) into src-tauri/binaries/
```

`npm run tauri dev` serves Vite on `:1420` for development only; the built app embeds its assets and
uses no port.

## How it fits together

- **Sidecar:** `scripts/build-sidecar.ps1` publishes the CLI single-file self-contained (win-x64) to
  `src-tauri/binaries/`; Rust runs it via `tauri-plugin-shell`. `src-tauri/binaries/` and
  `src-tauri/resources/` are gitignored (rebuilt by the script).
- **Frontend ↔ engine:** the React UI calls narrow Rust commands that invoke the sidecar; pure
  parsing of the CLI's JSON-Lines events lives in `src/lib/` (unit-tested, no Tauri imports).
- **Flows:** flat `.mbox` files, and full **Thunderbird profile mode** (discovery, `.msf` flag/tag
  enrichment, multi-account → one PST per account, and an optional Outlook category-colour import).

See the [root README](../README.md) for the product overview, the JSON-Lines contract, and
build-from-source details for the engine.
