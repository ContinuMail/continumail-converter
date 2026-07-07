// Screenshot harness — runs the app in a plain browser with the Tauri layer
// swapped for screenshots/mock-engine.ts + screenshots/mock-event.ts.
// Usage: npx vite --config vite.screenshots.config.ts
import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

const norm = (p: string) => p.replace(/\\/g, "/");
const REAL_ENGINE = norm(path.resolve(__dirname, "src/lib/engine.ts"));
const MOCK_ENGINE = norm(path.resolve(__dirname, "screenshots/mock-engine.ts"));
const MOCK_EVENT = norm(path.resolve(__dirname, "screenshots/mock-event.ts"));

function mockTauri(): Plugin {
  return {
    name: "screenshot-mock-tauri",
    enforce: "pre",
    async resolveId(source, importer) {
      if (source === "@tauri-apps/api/event") return MOCK_EVENT;
      // Never let the mocks' own imports recurse back into themselves.
      if (importer && norm(importer).startsWith(norm(path.resolve(__dirname, "screenshots")))) return null;
      const resolved = await this.resolve(source, importer, { skipSelf: true });
      if (resolved && norm(resolved.id) === REAL_ENGINE) return MOCK_ENGINE;
      return null;
    },
  };
}

export default defineConfig({
  plugins: [mockTauri(), react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  clearScreen: false,
  server: {
    port: 1425,
    strictPort: true,
  },
});
