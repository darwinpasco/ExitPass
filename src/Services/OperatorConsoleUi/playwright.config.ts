import { defineConfig, devices } from "@playwright/test";

const operatorConsoleOrdinanceSmokePort = Number(process.env.OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_PORT ?? 5197);
const artifactRoot =
  process.env.OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_ARTIFACT_ROOT ??
  "../../../.local/operator-console-ordinance-browser-smoke";

export default defineConfig({
  testDir: "./e2e",
  timeout: 45_000,
  expect: {
    timeout: 7_500
  },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: `${artifactRoot}/playwright-report` }]],
  outputDir: `${artifactRoot}/test-results`,
  use: {
    ...devices["Desktop Chrome"],
    baseURL: `http://127.0.0.1:${operatorConsoleOrdinanceSmokePort}`,
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
