// Screenshot driver — captures the v0.3.0 wizard at 1000×700 logical @ 3x DPR
// (3000×2100 PNGs). Requires the mock dev server:
//   npx vite --config vite.screenshots.config.ts
// Then: node screenshots/shoot.mjs [baseUrl]
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const BASE = process.argv[2] ?? "http://localhost:1425";
const OUT = path.join(path.dirname(fileURLToPath(import.meta.url)), "out");
mkdirSync(OUT, { recursive: true });

const browser = await chromium.launch();
const context = await browser.newContext({
  viewport: { width: 1000, height: 700 },
  deviceScaleFactor: 3,
  locale: "en-US",
});

async function fresh(shotParam = "") {
  const page = await context.newPage();
  const url = shotParam ? `${BASE}/?shot=${shotParam}` : `${BASE}/`;
  await page.goto(url, { waitUntil: "networkidle" });
  return page;
}

async function shoot(page, name) {
  await page.waitForTimeout(400); // settle animations
  await page.screenshot({ path: path.join(OUT, `${name}.png`) });
  console.log(`SHOT ${name}.png`);
  await page.close();
}

const btn = (page, name) => page.getByRole("button", { name });

// Source (profile tab) → discovered profile card visible.
async function selectProfile(page) {
  await btn(page, "Thunderbird profile").click();
  await btn(page, /Scan for Thunderbird profiles/).click();
  await page.getByText("default-release").first().waitFor({ timeout: 8000 });
}

// Source → Scanning → Accounts (waits for the accounts screen).
async function toAccounts(page) {
  await btn(page, /Continue ›/).click();
  await page.getByText("Accounts found").waitFor({ timeout: 15000 });
}

// Accounts → output chosen → Review (waits for the folder table).
async function toReview(page) {
  await btn(page, /Choose output folder/).click();
  await page.waitForTimeout(250);
  await btn(page, /Review folders ›/).click();
  await page.getByText("Mail folders found").waitFor({ timeout: 8000 });
}

// Review → Options (waits for the Also convert group).
async function toOptions(page) {
  await btn(page, /Continue to Options ›/).click();
  await page.getByText("Also convert").waitFor({ timeout: 8000 });
}

try {
  // ── 1a. Source — Thunderbird profile ────────────────────────────────
  {
    const page = await fresh();
    await selectProfile(page);
    await shoot(page, "v030-1a-source-thunderbird");
  }

  // ── 1b. Source — .mbox files (Gmail Takeout) ────────────────────────
  {
    const page = await fresh();
    await page.getByRole("button", { name: ".mbox files", exact: true }).click();
    await btn(page, /Choose \.mbox files/).click();
    await page.getByText(/Inbox\.mbox/i).first().waitFor({ timeout: 5000 });
    await btn(page, /Choose output/).click();
    await page.waitForTimeout(250);
    await shoot(page, "v030-1b-source-mbox");
  }

  // ── 2. Scanning (frozen at ~57%) ────────────────────────────────────
  {
    const page = await fresh("scanning");
    await selectProfile(page);
    await btn(page, /Continue ›/).click();
    await page.waitForTimeout(2400); // ramp reaches its hold point
    await shoot(page, "v030-2-scanning");
  }

  // ── 3. Accounts ─────────────────────────────────────────────────────
  {
    const page = await fresh();
    await selectProfile(page);
    await toAccounts(page);
    await shoot(page, "v030-3-accounts");
  }

  // ── 4. Review ───────────────────────────────────────────────────────
  {
    const page = await fresh();
    await selectProfile(page);
    await toAccounts(page);
    await toReview(page);
    await shoot(page, "v030-4-review");
  }

  // ── 5. Options (the money shot) ─────────────────────────────────────
  {
    const page = await fresh();
    await selectProfile(page);
    await toAccounts(page);
    await toReview(page);
    await toOptions(page);
    await shoot(page, "v030-5-options");
  }

  // ── 6. Convert (frozen at ~62%) ─────────────────────────────────────
  {
    const page = await fresh("convert");
    await selectProfile(page);
    await toAccounts(page);
    await toReview(page);
    await toOptions(page);
    await btn(page, /Start conversion ›/).click();
    // Timeline: started+scan (~0.5s) + coarse ramp (~0.6s) + fine ramp (9×350ms
    // ≈ 3.2s) ≈ 4.3s, then it freezes. Capture ~1s after the freeze so the bar
    // has settled but the rate samples are still within the retention window.
    await page.waitForTimeout(5300);
    await shoot(page, "v030-6-convert");
  }

  // ── 7. Done ─────────────────────────────────────────────────────────
  {
    const page = await fresh();
    await selectProfile(page);
    await toAccounts(page);
    await toReview(page);
    await toOptions(page);
    await btn(page, /Start conversion ›/).click();
    await page.getByText(/conversion complete|all done|done!/i).first().waitFor({ timeout: 25000 }).catch(async () => {
      // Fall back: wait for the terminal screen's output list.
      await page.getByText(/\.pst/).first().waitFor({ timeout: 10000 });
    });
    await page.waitForTimeout(800);
    await shoot(page, "v030-7-done");
  }

  console.log("ALL SHOTS CAPTURED →", OUT);
} finally {
  await browser.close();
}
