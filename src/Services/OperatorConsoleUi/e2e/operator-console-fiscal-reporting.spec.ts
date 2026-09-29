import { expect, test } from "@playwright/test";

const site = "10000000-0000-4000-8000-000000000001";
const period = {
  fiscalReportingPeriodId: "91000000-0000-4000-8000-000000000001",
  businessDayDate: "2026-09-10",
  periodStartAt: "2026-09-09T16:00:00Z",
  periodEndAt: "2026-09-10T16:00:00Z",
  currencyCode: "PHP",
  periodSequence: 91,
  status: "OPEN",
  expectedZStateVersion: 14
};
const amounts = {
  grossSalesAmountMinorUnits: 11200,
  netSalesAmountMinorUnits: 11200,
  vatableSalesAmountMinorUnits: 10000,
  vatAmountMinorUnits: 1200,
  vatExemptSalesAmountMinorUnits: 0,
  zeroRatedSalesAmountMinorUnits: 0,
  discountAmountMinorUnits: 0,
  voidAmountMinorUnits: 0,
  adjustmentAmountMinorUnits: 0
};
const reading = (kind: "X_READING" | "Z_READING") => ({
  reportReference: `${kind[0]}-20260910-001`,
  reportKind: kind,
  fiscalReportingPeriodId: period.fiscalReportingPeriodId,
  businessDayDate: period.businessDayDate,
  periodStartAt: period.periodStartAt,
  periodEndAt: period.periodEndAt,
  generatedAt: "2026-09-10T08:15:00Z",
  transactionCount: 1,
  amounts,
  currencyCode: "PHP",
  periodSequence: 91,
  reportStatus: "COMMITTED",
  tenders: [{ classification: "gcash", transactionCount: 1, amountMinorUnits: 11200, currencyCode: "PHP" }],
  discounts: []
});

test.describe("Operator Console POS-authoritative fiscal reporting", () => {
  test.beforeEach(async ({ page }) => {
    await page.route("**/v1/human-authentication/session", async (route) => route.fulfill({
      status: 200,
      headers: { "Content-Type": "application/json", "X-CSRF-Token": "g4-csrf" },
      body: JSON.stringify({ outcome:"AUTHENTICATED",authenticated:true,aptSessionToken:null,errorCode:null,retryable:false,correlationId:"correlation",session: { sessionReference:"session",userReference:"operator",username:"operator",displayName:"G4 Operator",audience:"OPERATOR_CONSOLE",assurance:"PASSWORD",privilegedAccount:true,passwordChangeRequired:false,mfaRequired:false,mfaSatisfied:true,authenticatedAt:"2026-09-10T00:00:00Z",lastSeenAt:"2026-09-10T00:00:00Z",idleExpiresAt:"2099-01-01T00:00:00Z",absoluteExpiresAt:"2099-01-01T00:00:00Z",permissions:["fiscal-reporting.ej.read","fiscal-reporting.ej.export","fiscal-reporting.x.read","fiscal-reporting.x.generate","fiscal-reporting.z.read"],roleCodes:["OPERATIONS_SUPERVISOR"],siteReferences:[site],siteGroupReferences:[],hasGlobalScope:false,correlationId:"correlation" } })
    }));
    await page.route("**/v1/access-readiness/evaluations", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ accessAllowed:true,denialReasons:[] }) }));
    await page.route("**/v1/ops/operator-console/fiscal-reporting/authorized-sites", async (route) => route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([{ siteId: site, siteCode: "PITX-L3", siteName: "PITX Level 3" }])
    }));
    await page.route("**/v1/ops/operator-console/fiscal-reporting/sites/*/electronic-journal?*", async (route) => route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ invoices: [{
        fiscalDocumentId: "92000000-0000-4000-8000-000000000001",
        fiscalDocumentNumber: "SI-00000001",
        businessDayDate: "2026-09-10",
        issuedAt: "2026-09-10T01:00:00Z",
        printableText: "SALES INVOICE\r\nPOS SOFTWARE SUPPLIER / DEVELOPER\r\n===== NOTHING FOLLOWS =====\r\n"
      }] })
    }));
    await page.route("**/v1/ops/operator-console/fiscal-reporting/sites/*/x-readings", async (route) => route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ currentPeriod: period, readings: [reading("X_READING")] })
    }));
    await page.route("**/v1/ops/operator-console/fiscal-reporting/sites/*/z-readings", async (route) => route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ currentPeriod: period, readings: [reading("Z_READING")] })
    }));
    await page.route("**/v1/ops/operator-console/fiscal-reporting/sites/*/z-readings/closeable-periods", async (route) => route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ fiscalBusinessDates: [] })
    }));
  });

  test("visually exposes site-scoped EJ, X, and read-only Z without exposing the Site UUID", async ({ page }, testInfo) => {
    await page.goto("/operator-console/fiscal-reporting");
    await expect(page.getByRole("heading", { name: "Fiscal Reporting" })).toBeVisible();
    const invoice = page.locator("article.invoiceText pre");
    await expect(invoice).toContainText("SALES INVOICE");
    await expect(invoice).toContainText("POS SOFTWARE SUPPLIER / DEVELOPER");
    await expect(invoice).toContainText("NOTHING FOLLOWS");
    await expect(page.getByLabel("Authorized Site")).toContainText("PITX Level 3");
    await expect(page.getByLabel("Authorized Site").locator("select")).toHaveCount(0);
    await expect(page.getByText(site, { exact: true })).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("operator-fiscal-reporting-ej.png"), fullPage: true });

    await page.getByRole("tab", { name: "X Reading" }).click();
    await expect(page.getByRole("button", { name: "Generate X Reading" })).toBeVisible();
    await expect(page.getByText("Non-closing observation of the current reporting period.")).toBeVisible();

    await page.getByRole("tab", { name: "Z Reading" }).click();
    await expect(page.getByText("Z-20260910-001")).toBeVisible();
    await expect(page.getByRole("button", { name: "Generate Z Reading" })).toHaveCount(0);

    await page.setViewportSize({ width: 390, height: 844 });
    const widths = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, page: document.documentElement.scrollWidth }));
    expect(widths.page).toBeLessThanOrEqual(widths.viewport);
  });
});
