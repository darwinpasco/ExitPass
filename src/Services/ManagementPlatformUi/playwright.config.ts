import { defineConfig, devices } from "@playwright/test";

const e2ePort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PORT ?? 5179);
const productionPort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT ?? 5180);

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  expect: {
    timeout: 5_000
  },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  outputDir: "test-results",
  use: {
    ...devices["Desktop Chrome"],
    baseURL: `http://127.0.0.1:${e2ePort}`,
    headless: true,
    screenshot: "only-on-failure",
    trace: "on-first-retry",
    video: "retain-on-failure",
    viewport: { width: 1366, height: 768 }
  },
  projects: [
    {
      name: "chromium",
      use: { browserName: "chromium" }
    }
  ],
  metadata: {
    productionBaseURL: `http://127.0.0.1:${productionPort}`
  }
});
