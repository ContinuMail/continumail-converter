// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

import { useEffect, useRef, useState } from "react";
import { windowedRate, easeToward, etaFromWindowedRate, type RateSample } from "./progressStats";
import type { ConvertState } from "./useConvert";

const SAMPLE_MIN_INTERVAL_MS = 150;
const SAMPLE_RETAIN_MS = 4000;
const RATE_WINDOW_MS = 3000;
const TEXT_REFRESH_MS = 1000;
const EASE_K = 0.15;

export interface SmoothedProgress {
  displayConverted: number;
  /** Eased fraction 0..1 for the progress bar. Advances in fine steps (see
   * RATIO_STEPS) so the bar moves smoothly without a main-thread CSS transition. */
  displayRatio: number;
  mbPerSec: number | null;
  etaSec: number | null;
}

// Bar-width re-render granularity: the bar re-renders when the eased ratio crosses
// one of this many buckets across the whole run (~sub-pixel steps → smooth motion).
const RATIO_STEPS = 2000;

/** Display-only smoothing over raw convert state: eased count (~30 fps) and a
 * windowed MB/s + ETA refreshed ~1x/sec. Mounted only while converting (App
 * renders ConvertView only in the "running" phase), so the rAF loop stops on
 * completion automatically. */
export function useSmoothedProgress(raw: ConvertState): SmoothedProgress {
  const rawRef = useRef(raw);
  rawRef.current = raw;

  const [smoothed, setSmoothed] = useState<SmoothedProgress>({
    displayConverted: raw.converted,
    displayRatio: raw.total > 0 ? Math.min(raw.converted, raw.total) / raw.total : 0,
    mbPerSec: null,
    etaSec: null,
  });

  const samplesRef = useRef<RateSample[]>([]);
  const displayRef = useRef(raw.converted);
  const lastSampleAtRef = useRef(0);
  const lastTextAtRef = useRef(0);
  const seenStartRef = useRef<number | null>(raw.startedAtMs);
  const rateRef = useRef<{ mbPerSec: number | null; etaSec: number | null }>({ mbPerSec: null, etaSec: null });
  const pushedRef = useRef<SmoothedProgress>({ displayConverted: -1, displayRatio: -1, mbPerSec: null, etaSec: null });

  useEffect(() => {
    let frame = 0;

    const tick = () => {
      const r = rawRef.current;
      const now = Date.now();

      // New-run reset, keyed on startedAtMs (not phase), so stale samples from a
      // previous run never affect the next.
      if (r.startedAtMs !== seenStartRef.current) {
        seenStartRef.current = r.startedAtMs;
        samplesRef.current = [];
        displayRef.current = r.converted;
        lastSampleAtRef.current = 0;
        lastTextAtRef.current = 0;
        rateRef.current = { mbPerSec: null, etaSec: null };
      }

      // Sample ONLY when bytes/converted changed (throttled), so the rate
      // reflects real progress, not animation frames.
      const buf = samplesRef.current;
      const last = buf[buf.length - 1];
      const changed = !last || last.bytes !== r.bytes || last.converted !== r.converted;
      if (changed && now - lastSampleAtRef.current >= SAMPLE_MIN_INTERVAL_MS) {
        buf.push({ ms: now, bytes: r.bytes, converted: r.converted });
        lastSampleAtRef.current = now;
        const cutoff = now - SAMPLE_RETAIN_MS;
        while (buf.length > 2 && buf[0].ms < cutoff) buf.shift();
      }

      // Ease + clamp so the display never exceeds the raw truth.
      displayRef.current = Math.min(easeToward(displayRef.current, r.converted, EASE_K), r.converted);

      // Recompute MB/s + ETA text ~1x/sec.
      if (now - lastTextAtRef.current >= TEXT_REFRESH_MS) {
        lastTextAtRef.current = now;
        const wr = windowedRate(buf, RATE_WINDOW_MS);
        const spanMs = buf.length >= 2 ? buf[buf.length - 1].ms - buf[0].ms : 0;
        rateRef.current = {
          mbPerSec: wr.bytesPerSec,
          etaSec: etaFromWindowedRate(r.converted, r.total, wr.msgPerSec, spanMs),
        };
      }

      // Re-render only when something visible changed (integer count, bar bucket, or rate/eta).
      const r2 = rawRef.current;
      const displayRatio = r2.total > 0 ? Math.min(displayRef.current, r2.converted) / r2.total : 0;
      const next: SmoothedProgress = {
        displayConverted: displayRef.current,
        displayRatio,
        mbPerSec: rateRef.current.mbPerSec,
        etaSec: rateRef.current.etaSec,
      };
      const p = pushedRef.current;
      if (
        Math.floor(next.displayConverted) !== Math.floor(p.displayConverted) ||
        Math.round(next.displayRatio * RATIO_STEPS) !== Math.round(p.displayRatio * RATIO_STEPS) ||
        next.mbPerSec !== p.mbPerSec ||
        next.etaSec !== p.etaSec
      ) {
        pushedRef.current = next;
        setSmoothed(next);
      }

      frame = requestAnimationFrame(tick);
    };

    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, []);

  return smoothed;
}
