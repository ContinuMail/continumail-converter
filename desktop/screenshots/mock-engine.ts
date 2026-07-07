// Screenshot harness — drop-in replacement for src/lib/engine.ts (via Vite alias).
// Behaviour is paced by the `?shot=` query param:
//   shot=scanning  → scan emits progress and never resolves (freeze ScanningView)
//   shot=convert   → convert holds at ~62% (freeze ConvertView)
//   anything else  → fast scan, convert runs to completion (DoneView)
import type { VersionResult, ScanResult } from "../src/lib/parse";
import type { ConversionConfig, FileStat, DiscoverResult, ProfileEntry } from "../src/lib/types";
import { emit } from "./mock-event";
import {
  demoProfiles, demoDiscover, demoScanResult, demoMboxFiles,
  demoOutputDir, demoOutputPst, demoTakeoutDir, totalMessages,
} from "./demo-data";

const shot = new URLSearchParams(window.location.search).get("shot") ?? "";
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

export async function checkEngineVersion(): Promise<VersionResult> {
  return { kind: "version", version: "0.3.0", engine: "Mail2Pst.Cli" } as VersionResult;
}

export async function scanSample(): Promise<ScanResult> {
  return demoScanResult([]);
}

export async function discoverProfile(_dir: string): Promise<DiscoverResult> {
  await sleep(150);
  return demoDiscover;
}

export async function scan(
  paths: string[],
  onProgress?: (p: { bytes: number; totalBytes: number }) => void,
): Promise<ScanResult> {
  const result = demoScanResult(paths);
  const totalBytes = result.totals.sourceBytes;
  if (shot === "scanning") {
    // Ramp to ~57% and hold forever.
    for (let f = 0.04; f <= 0.57; f += 0.045) {
      onProgress?.({ bytes: Math.round(totalBytes * f), totalBytes });
      await sleep(120);
    }
    return new Promise<ScanResult>(() => {}); // never resolves
  }
  for (const f of [0.3, 0.7]) {
    onProgress?.({ bytes: Math.round(totalBytes * f), totalBytes });
    await sleep(60);
  }
  return result;
}

export async function pickMboxFiles(): Promise<string[]> {
  return demoMboxFiles.map((f) => f.path);
}

export async function pickFolder(): Promise<string | null> {
  return demoTakeoutDir;
}

export async function listMboxInDir(_dir: string): Promise<string[]> {
  return demoMboxFiles.map((f) => f.path);
}

export async function statFiles(paths: string[]): Promise<FileStat[]> {
  const byPath = new Map(demoMboxFiles.map((f) => [f.path, f]));
  return paths.map((p) => byPath.get(p) ?? { path: p, size: 123_456_789 });
}

export async function pickOutputPst(): Promise<string | null> {
  return demoOutputPst;
}

export async function pickOutputFolder(): Promise<string | null> {
  return demoOutputDir;
}

export function buildStartConvertPayload(config: ConversionConfig, outputDir: string, expectedTotal?: number) {
  return expectedTotal === undefined ? { config, outputDir } : { config, outputDir, expectedTotal };
}

let converting = false;

export async function startConvert(
  _config: ConversionConfig,
  outputDir: string,
  expectedTotal?: number,
): Promise<void> {
  if (converting) return;
  converting = true;
  void runConvertTimeline(outputDir, expectedTotal ?? totalMessages);
}

const line = (obj: object) => emit("convert://line", JSON.stringify({ schemaVersion: 1, ...obj }));

async function runConvertTimeline(outputDir: string, total: number) {
  const appointmentsTotal = 1_698, tasksTotal = 129, contactsTotal = 1_375;
  const totalBytes = 9_517_000_000;
  await sleep(200);
  line({ type: "started", input: "profile", outputDirectory: outputDir });
  line({ type: "scan", totalMessages: total });
  await sleep(250);

  const hold = shot === "convert";
  const progressLine = (frac: number) =>
    line({
      type: "progress",
      converted: Math.round(total * frac),
      total,
      warnings: frac > 0.4 ? 2 : 0,
      skipped: 0,
      bytes: Math.round(totalBytes * frac),
      currentSource: "INBOX",
      currentFolder: frac < 0.35 ? "continumail@gmail.com/Archive" : frac < 0.75 ? "contact@continumail.com/Inbox/Clients" : "contact@continumail.com/Sent",
      phase: "mail",
    });

  if (hold) {
    // Establish the ~52% position with a single first sample (no rate yet),
    // then emit a UNIFORM ramp at a conservative, realistic byte rate so the
    // windowed MB/s + ETA settle on believable values (~60 MB/s). Every delta
    // is identical, so no coarse-step spike can leak into the rate readout.
    // Then STOP: with progress frozen the eased count converges and the bar's
    // width transition settles to the true percentage. The driver captures
    // ~0.7 s later — still inside the rate-retention window, so MB/s and ETA
    // remain on screen while the bar shows the correct fill.
    const TICK_MS = 350;
    const TARGET_MB_PER_SEC = 60;
    const bytesPerTick = TARGET_MB_PER_SEC * 1_000_000 * (TICK_MS / 1000); // 21 MB
    let frac = 0.52;
    progressLine(frac); // first sample: position only
    await sleep(TICK_MS);
    for (let i = 0; i < 11; i++) {
      frac += bytesPerTick / totalBytes;
      progressLine(frac);
      await sleep(TICK_MS);
    }
    return; // freeze so the bar settles for the capture
  }
  for (let f = 0.03; f <= 1 + 1e-9; f += 0.09) {
    progressLine(Math.min(f, 1));
    await sleep(120);
  }

  // PIM phases
  for (const [phase, key, keyTotal] of [
    ["appointments", "appointments", appointmentsTotal],
    ["tasks", "tasks", tasksTotal],
    ["contacts", "contacts", contactsTotal],
  ] as const) {
    for (const frac of [0.4, 1]) {
      line({
        type: "progress",
        converted: total, total, warnings: 2, skipped: 0, bytes: totalBytes,
        currentFolder: null, phase,
        [`${key}Converted`]: Math.round(keyTotal * frac),
        [`${key}Total`]: keyTotal,
      });
      await sleep(90);
    }
  }

  line({
    type: "warning",
    source: "Archive", identifier: "message #18422",
    reason: "Attachment filename missing; a name was generated",
  });
  await sleep(120);
  line({
    type: "done",
    converted: total, skipped: 0, warnings: 2,
    outputs: [
      `${outputDir}\\continumail@gmail.com.pst`,
      `${outputDir}\\contact@continumail.com.pst`,
      `${outputDir}\\Local Folders.pst`,
    ],
    outputDirectory: outputDir,
    report: `${outputDir}\\conversion-report.json`,
    elapsedMs: 754_000,
    enrichment: {
      matched: 76_460, skippedMissingId: 12, skippedDuplicateId: 3, noMsfMatch: 994,
      expungedMatched: 210, expungedDropped: 210,
      sourcesAttempted: 9, sourcesEnriched: 9, sourcesDegraded: 0,
    },
    appointmentsConverted: appointmentsTotal, appointmentsSkipped: 0, appointmentWarnings: 1,
    tasksConverted: tasksTotal, tasksSkipped: 0, taskWarnings: 0,
    contactsConverted: contactsTotal, contactsSkipped: 0, contactWarnings: 1,
  });
  emit("convert://exit", 0);
  converting = false;
}

export async function cancelConvert(): Promise<void> {}
export async function openFolder(_path: string): Promise<void> {}
export async function openJunkHelp(): Promise<void> {}

export async function listThunderbirdProfiles(): Promise<ProfileEntry[]> {
  await sleep(120);
  return demoProfiles;
}
