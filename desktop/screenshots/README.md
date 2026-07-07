# Screenshot harness

Generates high-resolution marketing/app screenshots of the desktop wizard by
running the **real** React UI in a plain browser with the Tauri layer swapped
for mocks that feed curated, PII-free demo data. No Tauri, no sidecar, no real
mail needed.

Output is written to `screenshots/out/` (gitignored). Everything else here is
source and is tracked.

## Why it works this way

- The app is a fixed **1000×700** non-resizable Tauri window, so shots are
  captured at that logical size with `deviceScaleFactor: 3` → crisp **3000×2100**
  PNGs of the UI exactly as it ships (not a stretched 4K viewport).
- Every screen's data comes from Tauri `invoke()`/event calls, which fail in a
  plain browser. `mock-engine.ts` and `mock-event.ts` replace those so the UI
  renders with realistic data.
- `vite.screenshots.config.ts` aliases `src/lib/engine.ts` → `mock-engine.ts`
  and `@tauri-apps/api/event` → `mock-event.ts` at build time. The app code is
  untouched.

## Files

| File | Role |
|------|------|
| `demo-data.ts` | The fictional profile: accounts, nested folders, `.msf` pairing, calendars/contacts/tasks, counts, dates. **Edit this to change what the shots show.** |
| `mock-engine.ts` | Drop-in replacement for `src/lib/engine.ts`. Paces scan/convert via a `?shot=` param so those screens can be frozen mid-progress. |
| `mock-event.ts` | Drop-in replacement for `@tauri-apps/api/event` (a window-level event bus). |
| `shoot.mjs` | Playwright driver: walks the whole wizard and captures each screen. |
| `../vite.screenshots.config.ts` | Vite config that wires the mocks in. |

## Running it

Playwright is not a project dependency (it's heavy and dev-only). Install it
on demand:

```bash
cd desktop
npm i -D playwright
npx playwright install chromium
```

Then, in two terminals (or background the first):

```bash
# 1. serve the app with the mock engine
npx vite --config vite.screenshots.config.ts      # → http://localhost:1425

# 2. capture all screens
node screenshots/shoot.mjs
```

Shots land in `screenshots/out/` as `v0xx-*.png`. Copy the ones you want into
the website repo's `src/assets/` and update the imports.

## Updating for a new release

1. Bump the version in `mock-engine.ts` (`checkEngineVersion`).
2. Adjust `demo-data.ts` if the data model changed.
3. If the wizard's buttons/labels changed, update the selectors in `shoot.mjs`
   (it prints a `MISS` with the visible buttons when a selector doesn't match).
4. Re-run, eyeball `out/`, copy into the site.
