import { expect, test } from "@playwright/test";

const route = "/management-platform/reports/fiscal-exceptions";

test.describe("Management Platform fiscal exception reporting", () => {
  test("shows authorized Site and Site Group aggregates without fiscal actions or direct downstream calls", async ({ page }) => {
    const externalRequests: string[] = [];
    page.on("request", (request) => {
      const url = new URL(request.url());
      if (!(["127.0.0.1", "localhost"] as string[]).includes(url.hostname)) externalRequests.push(request.url());
    });

    await page.goto(`${route}?mpScenario=multi-site&mpFiscalScenario=activity`);

    await expect(page.getByRole("heading", { name: "Sales Invoice fiscal exceptions" })).toBeVisible();
    await expect(page.getByText("PHP 2,425.50")).toBeVisible();
    await expect(page.getByText("USD 37.25")).toBeVisible();
    await expect(page.getByRole("cell", { name: "Other" })).toBeVisible();
    await expect(page.getByText(/POS Server remains the fiscal authority/i)).toBeVisible();
    await expect(page.getByRole("button", { name: /retry issuance|resolve exception|export/i })).toHaveCount(0);

    await page.getByRole("combobox", { name: "Report scope", exact: true }).selectOption("SITE_GROUP:71000000-0000-0000-0000-000000000201");
    await expect(page.getByRole("region", { name: "Report scope and freshness" }).getByText("Development Site Group (Site Group)")).toBeVisible();

    expect(externalRequests).toEqual([]);
    expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 });
  });

  test("keeps narrow-screen controls reachable and confines wide tables to their scrollers", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${route}?mpScenario=authenticated&mpFiscalScenario=activity`);
    await expect(page.getByRole("heading", { name: "Sales Invoice fiscal exceptions" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Refresh report" })).toBeVisible();

    const documentWidth = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth }));
    expect(documentWidth.scroll).toBeLessThanOrEqual(documentWidth.viewport);
    await expect(page.getByLabel("Fiscal exception table")).toBeVisible();
  });

  test("distinguishes no activity and disabled feature postures", async ({ page }) => {
    await page.goto(`${route}?mpScenario=authenticated&mpFiscalScenario=no-activity`);
    await expect(page.getByRole("status", { name: "No fiscal issuance activity" })).toBeVisible();
    await expect(page.getByText(/not an exception-free or no-payment certification/i)).toBeVisible();

    await page.goto(`${route}?mpScenario=authenticated&mpFiscalScenario=disabled`);
    await expect(page.getByRole("status", { name: "Fiscal reporting unavailable" })).toContainText("not enabled");
    await expect(page.getByText("PHP 2,425.50")).toHaveCount(0);
  });

  test("ignores development fiscal scenarios in the production bundle", async ({ browser }) => {
    const productionPort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT ?? 5180);
    const productionPage = await browser.newPage();
    try {
      await productionPage.goto(`http://127.0.0.1:${productionPort}${route}?mpScenario=authenticated&mpFiscalScenario=activity`);
      await expect(productionPage.getByRole("status", { name: "Development scenario" })).toHaveCount(0);
      await expect(productionPage.getByRole("heading", { name: "Sales Invoice fiscal exceptions" })).toHaveCount(0);
      await expect(productionPage.getByText("PHP 2,425.50")).toHaveCount(0);
    } finally {
      await productionPage.close();
    }
  });
});
