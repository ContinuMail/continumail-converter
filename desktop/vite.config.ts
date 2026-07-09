// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

// defineConfig from vitest/config is vite's, extended with the typed `test` key below; it still
// produces a valid Vite config for `vite build` / Tauri.
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

// @ts-expect-error process is a nodejs global
const host = process.env.TAURI_DEV_HOST;

// https://vite.dev/config/
export default defineConfig(async () => ({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  test: {
    // The default 5s per-test timeout is too tight for a cold CI runner, where Vite charges the
    // first-hit transform/import cost to the first test in each file (observed ~7s in CI, vs a
    // ~2.5s whole-suite run locally). This headroom only matters on a cold first run.
    testTimeout: 15000,
    hookTimeout: 15000,
  },
  clearScreen: false,
  server: {
    port: 1420,
    strictPort: true,
    host: host || false,
    hmr: host ? { protocol: "ws", host, port: 1421 } : undefined,
    watch: { ignored: ["**/src-tauri/**"] },
  },
}));
