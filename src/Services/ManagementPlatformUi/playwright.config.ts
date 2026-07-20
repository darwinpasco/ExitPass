import { defineConfig, devices } from "@playwright/test";

const e2ePort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PORT ?? 5177);
const productionPort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT ?? 5178);
const e2eEnv = {
  ...process.env,
  VITE_MANAGEMENT_PLATFORM_PERMISSIONS: "management-platform.overview.read,sales-invoice-profile.read,sales-invoice-profile.manage"
};

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
  webServer: [
    {
      command: `npx vite --host 127.0.0.1 --port ${e2ePort} --strictPort`,
      url: `http://127.0.0.1:${e2ePort}/management-platform/`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: e2eEnv
    },
    {
      command: `npm run build && npx vite preview --host 127.0.0.1 --port ${productionPort} --strictPort`,
      url: `http://127.0.0.1:${productionPort}/management-platform/`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: e2eEnv
    }
  ],
  metadata: {
    productionBaseURL: `http://127.0.0.1:${productionPort}`
  }
});
