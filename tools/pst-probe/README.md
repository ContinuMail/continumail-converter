# pst-probe

Dev-only diagnostic that opens a `.pst` through the vendored **PSTFileFormat** reader and dumps the
structures relevant to creating an empty store from scratch (see
[tools/pst-probe/README.md](README.md)):

- the PST **header** (magic, versions, crypt method, `fAMapValid`, root BREFs, `bidNext*`),
- the **Message Store node** (`NID 0x21`) property set, including the raw bytes of the
  `PidTagIpmSubTreeEntryId` / `…Wastebasket` / `…Finder` EntryIDs,
- the **folder tree** (NIDs + display names) from both the Root Folder and `TopOfPersonalFolders`,
- the **Table-Context column sets** of the templates (`0x60D/0x60E/0x60F/0x671/0x692`).

It is **not** part of the shipped application (`IsPackable=false`) and is not built by the solution.

## Usage

```bash
# Dump any PST (pass the path explicitly — no default)
dotnet run --project tools/pst-probe -- path/to/oracle.pst

# Example: diff a from-scratch CreateEmptyStore() output against an Outlook-made oracle
dotnet run --project tools/pst-probe -- path/to/oracle.pst    > oracle.txt
dotnet run --project tools/pst-probe -- path/to/candidate.pst > candidate.txt
diff oracle.txt candidate.txt
```

The intended workflow is **oracle diffing**: run it on a real Outlook-made empty PST and on a candidate
`CreateEmptyStore()` result, and compare field-by-field until they match where they must.

## How it works

It reuses the vendored types exactly as the app compiles them, by `ProjectReference`-ing
`src/Mail2Pst.Core` (the legacy `vendor/*.csproj` files don't build standalone). Private structures
(`m_header`, `m_tcInfo.rgTCOLDESC`, …) are read via reflection — this is a throwaway inspector, not
production code.
