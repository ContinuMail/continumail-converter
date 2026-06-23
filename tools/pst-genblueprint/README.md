# pst-genblueprint (dev-only)

Regenerates `vendor/PSTFileFormat/DefaultStoreTemplates.Blueprint.g.cs` — the 43-node empty-store
skeleton that `DefaultStoreTemplates.Build` replays so Outlook accepts a from-scratch `.pst`.

**Not part of normal builds.** `IsPackable=false`, not in `Mail2Pst.sln`, never shipped.

## Source of truth

The generated `.Blueprint.g.cs` **is** the source of truth for the empty-store skeleton. The real
Outlook-made empty PST it was originally dumped from has been **retired** (the converter now builds
stores from scratch via `PSTFile.CreateEmptyStore`). The blueprint is therefore **frozen** and should
not normally change.

## Regeneration (only if the skeleton must ever change)

Requires an **external reference** empty Unicode PST (e.g. one freshly created by Outlook) — there is
no longer an in-repo seed asset to read:

    dotnet run --project tools/pst-genblueprint -- <reference-empty.pst> vendor/PSTFileFormat/DefaultStoreTemplates.Blueprint.g.cs

Then re-run the full test suite incl. the independent-reader gate before committing the regenerated file.
