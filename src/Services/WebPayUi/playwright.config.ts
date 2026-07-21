import { defineConfig, devices } from "@playwright/test";

const webPayBrowserSmokePort = Number(process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5196);

export default defineConfig({
  testDir: "./e2e",
  timeout: 45_000,
  expect: {
    timeout: 7_500
  },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: "../../../.local/webpay-browser-smoke/playwright-report" }]],
  outputDir: "../../../.local/webpay-browser-smoke/test-results",
  use: {
    ...devices["Desktop Chrome"],
    baseURL: `http://127.0.0.1:${webPayBrowserSmokePort}`,
    headless: true,
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
    video: "retain-on-failure",
    viewport: { width: 1366, height: 768 }
  },
  projects: [
    {
      name: "chromium",
      use: { browserName: "chromium" }
    }
  ]
});
