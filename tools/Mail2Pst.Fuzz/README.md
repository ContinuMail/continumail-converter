<!-- SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail) -->
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# mail2pst-fuzz — parser fuzzing (dev/test tool, NOT shipped)

Coverage-guided fuzzing of the two byte-facing parsers (`MboxParser`, `MorkReader`) via
[SharpFuzz](https://github.com/Metalnem/sharpfuzz) + libFuzzer. Not in `Mail2Pst.sln`, never
linked into the product, never shipped. Same "independent dev tool" status as `tools/pst-validate`.

## What counts as a crash

Each target runs the parser and swallows only the **whitelisted graceful exceptions**
(`MorkFormatException`, `FormatException`, `IOException` — the documented "degrade to a skip"
path). Any other escaping exception, a hang (libFuzzer `-timeout`), or an OOM (libFuzzer
`-rss_limit_mb`) is recorded as a crashing input.

## One-time setup

```bash
dotnet tool install --global SharpFuzz.CommandLine   # provides the `sharpfuzz` instrumenter
dotnet build tools/Mail2Pst.Fuzz/Mail2Pst.Fuzz.csproj -c Release
# Instrument the assembly under test (in place), pointing at the build output:
sharpfuzz tools/Mail2Pst.Fuzz/bin/Release/net8.0/Mail2Pst.Core.dll
```

You also need the `libfuzzer-dotnet` driver for your OS. Follow the current SharpFuzz README
(<https://github.com/Metalnem/sharpfuzz#how-to-use>) to obtain/build it — on Linux it is built
once with clang; on Windows use the project's `libfuzzer-dotnet-windows` driver. (Pinning a binary
URL here would rot; the SharpFuzz README is the source of truth for the driver.)

## Run

```bash
# seed corpus lives OUTSIDE the repo (may contain real-mail fragments) — see PII policy below
# fuzz the Mork reader:
libfuzzer-dotnet --target_path=tools/Mail2Pst.Fuzz/bin/Release/net8.0/mail2pst-fuzz \
                 --target_arg=mork  testdata/fuzz/mork-corpus
# fuzz the mbox splitter:
libfuzzer-dotnet --target_path=tools/Mail2Pst.Fuzz/bin/Release/net8.0/mail2pst-fuzz \
                 --target_arg=mbox  testdata/fuzz/mbox-corpus
```

## Deterministic replay (no libFuzzer — CI-independent smoke)

```bash
dotnet run --project tools/Mail2Pst.Fuzz -- mork-replay testdata/fuzz/mork-corpus
dotnet run --project tools/Mail2Pst.Fuzz -- mbox-replay fixtures
```
Exit 0 iff no input produced an un-whitelisted exception. Use this to sanity-check the harness and
to re-run a saved corpus without the libFuzzer toolchain.

## Seed corpus & PII policy (READ THIS)

- Seeds and raw crash artifacts live under gitignored `testdata/fuzz/` — **never committed**
  (real `.mbox`/`.mab`/`.msf` fragments are PII). Seed the mbox corpus from `fixtures/*.mbox`
  plus any local real files; seed the mork corpus from local `.msf`/`.mab` files.
- When a crash is found: minimize it, then **hand-author a synthetic, PII-free repro** (or verify
  the minimized bytes contain no real mail) and add it as an xUnit regression under
  `tests/Mail2Pst.Core.Tests/Fuzzing/`. Real bytes never enter the public repo; never `git add -f`.
