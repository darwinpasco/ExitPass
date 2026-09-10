import { expect, test } from "@playwright/test";

const site = "10000000-0000-4000-8000-000000000001";

test.describe("Operator Console POS-authoritative fiscal reporting", () => {
  test.beforeEach(async ({ page }) => {
    await page.route("**/v1/human-authentication/session", async (route) => route.fulfill({
      status: 200,
      headers: { "Content-Type": "application/json", "X-CSRF-Token": "g4-csrf" },
      body: JSON.stringify({ outcome:"AUTHENTICATED",authenticated:true,aptSessionToken:null,errorCode:null,retryable:false,correlationId:"correlation",session: { sessionReference:"session",userReference:"operator",username:"operator",displayName:"G4 Operator",audience:"OPERATOR_CONSOLE",assurance:"PASSWORD",privilegedAccount:true,passwordChangeRequired:false,mfaRequired:false,mfaSatisfied:true,authenticatedAt:"2026-09-10T00:00:00Z",lastSeenAt:"2026-09-10T00:00:00Z",idleExpiresAt:"2099-01-01T00:00:00Z",absoluteExpiresAt:"2099-01-01T00:00:00Z",permissions:["fiscal-reporting.ej.read","fiscal-reporting.ej.export","fiscal-reporting.x.read","fiscal-reporting.x.generate","fiscal-reporting.z.read","fiscal-reporting.z.generate"],siteReferences:[site],siteGroupReferences:[],hasGlobalScope:false,correlationId:"correlation" } })
    }));
    await page.route("**/v1/access-readiness/evaluations", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ accessAllowed:true,denialReasons:[] }) }));
  });

  test("visually exposes site-scoped EJ, X, and confirmed privileged Z", async ({ page }, testInfo) => {
    await page.goto("/operator-console/fiscal-reporting?operatorFiscalReportingScenario=ready");
    await expect(page.getByRole("heading", { name: "Fiscal Reporting" })).toBeVisible();
    await expect(page.getByText("Customer Name      : Juan Dela Cruz", { exact: false })).toBeVisible();
    await expect(page.getByLabel("Authorized Site")).toHaveValue(site);
    await page.screenshot({ path: testInfo.outputPath("operator-fiscal-reporting-ej.png"), fullPage: true });

    await page.getByRole("tab", { name: "X Reading" }).click();
    await expect(page.getByRole("button", { name: "Generate X Reading" })).toBeVisible();
    await expect(page.getByText("Non-closing observation of the current reporting period.")).toBeVisible();

    await page.getByRole("tab", { name: "Z Reading" }).click();
    await page.getByRole("button", { name: "Generate Z Reading" }).click();
    await expect(page.getByRole("alertdialog", { name: "Confirm Z Reading close" })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("operator-fiscal-reporting-z-confirm.png"), fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    const widths = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, page: document.documentElement.scrollWidth }));
    expect(widths.page).toBeLessThanOrEqual(widths.viewport);
  });
});
