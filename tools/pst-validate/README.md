# pst-validate (developer/test tool — not shipped in releases)

An **independent** PST reader used only to validate ContinuMail Converter output in tests.
It opens a `.pst` with Microsoft's MIT-licensed [`outlook-pst`](https://crates.io/crates/outlook-pst)
crate (a clean-room MS-PST implementation, distinct from the converter's vendored writer/reader),
walks the folder tree, and prints one JSON object with per-folder message counts — or an error.

- This tool's **own source is GPL-3.0-or-later** (ContinuMail project code); it merely **depends on**
  the MIT `outlook-pst` crate. MIT is permissive, so there is no license conflict.
- **Not** part of the ContinuMail Converter product. Not linked into the engine, CLI, or desktop app.
- **Not** included in product **release artifacts** (the source lives in the repo; it is just never
  packaged/shipped).
- The MIT `outlook-pst` dependency is acknowledged in the repo `NOTICE`.

## Build / test (reproducible)

    cargo build --locked --release
    cargo test  --locked

## Use

    pst-validate <path-to.pst>

Prints one JSON object to stdout. Exit code 0 only when the file opened cleanly with no errors.
