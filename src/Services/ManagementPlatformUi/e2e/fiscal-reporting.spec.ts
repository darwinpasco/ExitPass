import { expect, test } from "@playwright/test";

const route = "/management-platform/fiscal-reporting?mpScenario=multi-site&mpFiscalReportingScenario=ready";

test.describe("Management Platform POS-authoritative fiscal reporting", () => {
  test("visually exposes EJ, X, Z, governed Site scope, and a confined mobile table", async ({ page }, testInfo) => {
    await page.goto(route);
    await expect(page.getByRole("heading", { name: "Fiscal Reporting" })).toBeVisible();
    await expect(page.getByText("Customer Name      : Juan Dela Cruz", { exact: false })).toBeVisible();
    await expect(page.getByRole("link", { name: "Download authoritative .txt" })).toHaveAttribute("href", /electronic-journal\/download/);
    await page.screenshot({ path: testInfo.outputPath("management-fiscal-reporting-ej.png"), fullPage: true });

    await page.getByRole("tab", { name: "X Reading" }).click();
    await expect(page.getByText("Non-closing observation of the current reporting period.")).toBeVisible();
    await expect(page.getByText("cash: ₱112.00")).toBeVisible();

    await page.getByRole("tab", { name: "Z Reading" }).click();
    await page.getByRole("button", { name: "Generate Z Reading" }).click();
    await expect(page.getByRole("alertdialog", { name: "Confirm Z Reading close" })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("management-fiscal-reporting-z-confirm.png"), fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    const widths = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, page: document.documentElement.scrollWidth }));
    expect(widths.page).toBeLessThanOrEqual(widths.viewport);
  });

  test("does not activate the fiscal-reporting fixture in the production bundle", async ({ browser }) => {
    const productionPort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT ?? 5180);
    const productionPage = await browser.newPage();
    try {
      await productionPage.goto(`http://127.0.0.1:${productionPort}${route}`);
      await expect(productionPage.getByRole("status", { name: "Development scenario" })).toHaveCount(0);
      await expect(productionPage.getByRole("heading", { name: "Fiscal Reporting" })).toHaveCount(0);
      await expect(productionPage.getByText("Customer Name      : Juan Dela Cruz", { exact: false })).toHaveCount(0);
    } finally {
      await productionPage.close();
    }
  });
});
