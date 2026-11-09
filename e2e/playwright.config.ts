import { defineConfig, devices } from "@playwright/test";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

// Each run gets an empty SQLite file, so tests start from a known state.
const dbDir = mkdtempSync(join(tmpdir(), "task-assistant-e2e-"));
const apiPort = 5099;
const webPort = 4173;

export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://127.0.0.1:${webPort}`,
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      // API in demo mode: no ANTHROPIC_API_KEY, so it never calls a model.
      command: "dotnet run --project ../api/TaskAssistant.Api --no-launch-profile",
      url: `http://127.0.0.1:${apiPort}/healthz`,
      env: {
        ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
        ASPNETCORE_ENVIRONMENT: "Development",
        ConnectionStrings__Tasks: `Data Source=${join(dbDir, "tasks.db")}`,
        ANTHROPIC_API_KEY: "",
      },
      timeout: 180_000,
      reuseExistingServer: false,
    },
    {
      // The built web app, served by `vite preview` with /api proxied to the API above.
      command: "npm --prefix ../web run preview -- --host 127.0.0.1",
      url: `http://127.0.0.1:${webPort}`,
      env: { API_URL: `http://127.0.0.1:${apiPort}` },
      timeout: 60_000,
      reuseExistingServer: false,
    },
  ],
});
