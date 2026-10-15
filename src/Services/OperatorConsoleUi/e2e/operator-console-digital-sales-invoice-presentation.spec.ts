import { expect, test } from "@playwright/test";

const fiscalReferenceId = "5f000000-0000-0000-0000-000000000105";
const canonicalText = [
  "  Professional Parking Management",
  "            Corporation",
  " Unit 2103A-B, East Tower, Tektite",
  "   Towers, Ortigas Center, Pasig",
  "        City, Metro Manila",
  "VAT REG TIN         999-999-999-000",
  "MIN            MIN-TEST-PITX-L3-001",
  "S/N             SN-TEST-PITX-L3-001",
  "BRANCH / SITE          PITX Level 3",
  "PARKING LOCATION       PITX Level 3",
  "",
  "           SALES INVOICE",
  "             ORIGINAL",
  "",
  "SI NO                   SI-00000105",
  "ISSUED             2026-10-15 18:54",
  "BUSINESS DATE            2026-10-15",
  "CASHIER                      System",
  "",
  "TICKET                1474119573245",
  "PLATE                       ABC1245",
  "ENTRY              2026-10-15 09:40",
  "PAYMENT            2026-10-15 18:54",
  "DURATION                   09:14:01",
  "",
  "--------------------------------",
  "PARKING FEE",
  "1 x PHP 44.64                 50.00",
  "--------------------------------",
  "SUBTOTAL                      44.64",
  "DISCOUNT                       0.00",
  "VATABLE SALES                 44.64",
  "VAT AMOUNT                     5.36",
  "VAT EXEMPT SALES               0.00",
  "ZERO RATED SALES               0.00",
  "TOTAL                         50.00",
  "",
  "PAYMENT",
  "TYPE                           QRPH",
  "AMOUNT DUE                    50.00",
  "TOTAL PAID                    50.00",
  "",
  "--------------------------------",
  " THIS SERVES AS YOUR SALES INVOICE",
  "PRINT DATE         2026-10-15 18:54",
  "",
  "CUSTOMER INFORMATION",
  "NAME          Darwin Pasco",
  "ADDRESS       123 Doña Juana",
  "              Townhomes, Doña",
  "              Juana Subd. Brgy.",
  "              Rosario, Pasig City",
  "              1609",
  "TIN           171459612",
  "BUS. STYLE    Wendy's Food Corp",
  "Customer Sign ____________________",
  "",
  "POS SOFTWARE SUPPLIER / DEVELOPER",
  "  Professional Parking Management",
  "            Corporation",
  " Unit 2103A-B, East Tower, Tektite",
  "   Towers, Ortigas Center, Pasig",
  "        City, Metro Manila",
  "TIN                 999-999-999-000",
  "ACCR NO          ACC-TEST-2026-0001",
  "DATE ISSUED              2026-01-01",
  "PTU           PTU-TEST-PITX-L3-0001",
  "PTU DATE                 2026-01-01",
  "THANK YOU FOR CHOOSING OUR SERVICE",
  "    ===== NOTHING FOLLOWS ====="
].join("\r\n") + "\r\n";

test("Fiscal Issuance Status displays readable canonical text and customer WebPay QR", async ({ page }) => {
  await page.route("**/v1/operator-console/device-binding/status", async (route) => route.fulfill({ status: 204 }));
  await page.route("**/v1/human-authentication/session", async (route) => route.fulfill({
    status: 200,
    headers: { "Content-Type": "application/json", "X-CSRF-Token": "operator-console-fixture-csrf" },
    body: JSON.stringify({
      outcome: "AUTHENTICATED",
      authenticated: true,
      aptSessionToken: null,
      errorCode: null,
      retryable: false,
      correlationId: "correlation",
      session: {
        sessionReference: "session",
        userReference: "operator",
        username: "operator",
        displayName: "Operations Supervisor",
        audience: "OPERATOR_CONSOLE",
        assurance: "PASSWORD",
        privilegedAccount: true,
        passwordChangeRequired: false,
        mfaRequired: false,
        mfaSatisfied: true,
        authenticatedAt: "2026-10-15T00:00:00Z",
        lastSeenAt: "2026-10-15T00:00:00Z",
        idleExpiresAt: "2099-01-01T00:00:00Z",
        absoluteExpiresAt: "2099-01-01T00:00:00Z",
        permissions: ["fiscal-issuance.status.read"],
        roleCodes: ["OPERATIONS_SUPERVISOR"],
        siteReferences: ["2d1dcdf8-f563-537c-8542-0bde7cc9da97"],
        siteGroupReferences: [],
        hasGlobalScope: false,
        operatorDeviceBindingReference: "5f000000-0000-0000-0000-000000000120",
        operatorShiftReference: null,
        effectiveSiteReference: "2d1dcdf8-f563-537c-8542-0bde7cc9da97",
        effectiveSiteGroupReference: "5f000000-0000-0000-0000-000000000121",
        correlationId: "correlation"
      }
    })
  }));
  await page.route("**/v1/access-readiness/evaluations", async (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({ accessAllowed: true, denialReasons: [] })
  }));
  await page.route("**/v1/ops/operator-console/fiscal-issuance/lookup?*", async (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      fiscalIssuanceReferenceId: fiscalReferenceId,
      fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
      resultClassification: "NEWLY_CREATED",
      fiscalIssuanceEvidenceStatus: "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
      fiscalNumberAssignmentState: "ASSIGNED",
      upstreamFinalityReference: "SI-00000105-finality",
      parkingSessionId: "5f000000-0000-0000-0000-000000000115",
      siteId: "2d1dcdf8-f563-537c-8542-0bde7cc9da97",
      sitePosServerId: "5f000000-0000-0000-0000-000000000112",
      sitePosServerRef: "PITX-L3-POS",
      fiscalDocumentTypeCodeId: "5f000000-0000-0000-0000-000000000113",
      fiscalDocumentTypeCodeKey: "sales_invoice",
      posServerFiscalDocumentId: "5f000000-0000-0000-0000-000000000114",
      posServerFiscalDocumentReadStatus: "AVAILABLE",
      fiscalDocumentNumber: "SI-00000105",
      fiscalSequenceValue: 105,
      firstRecordedAt: "2026-10-15T08:00:00Z",
      lastUpdatedAt: "2026-10-15T08:00:00Z",
      correlationId: "5f000000-0000-0000-0000-000000000108"
    })
  }));
  await page.route(`**/v1/ops/operator-console/fiscal-issuance/references/${fiscalReferenceId}/digital-sales-invoice`, async (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      presentation: {
        documentTitle: "Digital Sales Invoice",
        presentationVersion: "digital-sales-invoice-presentation-json-v1",
        sourceTemplateContractVersion: "digital-sales-invoice-json-v1",
        renderFormat: "application/json",
        sections: [],
        notices: []
      },
      canonicalText,
      customerDigitalSalesInvoicePath: "/webpay/sales-invoice#access=fixture-opaque-capability",
      capabilityExpiresAt: "2026-10-16T08:00:00Z"
    })
  }));

  await page.goto("/operator-console/fiscal-issuance-status");
  await page.getByLabel(/Ticket, Plate or SI Number/i).fill("SI-00000105");
  await page.getByRole("button", { name: "View status" }).click();
  await page.getByRole("button", { name: "View Sales Invoice" }).click();

  const receipt = page.locator(".canonicalSalesInvoiceReceipt");
  const text = receipt.locator(".canonicalSalesInvoiceText");
  await expect(receipt).toBeVisible();
  await expect(text).toHaveText(canonicalText);

  const screen = await receipt.evaluate((element) => {
    const receiptStyle = getComputedStyle(element);
    const textStyle = getComputedStyle(element.querySelector(".canonicalSalesInvoiceText")!);
    return {
      width: receiptStyle.width,
      maxWidth: receiptStyle.maxWidth,
      fontFamily: receiptStyle.fontFamily,
      fontSize: receiptStyle.fontSize,
      lineHeight: receiptStyle.lineHeight,
      overflowX: receiptStyle.overflowX,
      whiteSpace: textStyle.whiteSpace,
      overflowWrap: textStyle.overflowWrap,
      wordBreak: textStyle.wordBreak
    };
  });
  console.log(`SCREEN_COMPUTED=${JSON.stringify(screen)}`);
  expect(screen.fontSize).toBe("16px");
  expect(screen.lineHeight).toBe("20.8px");
  expect(screen.fontFamily).toContain("Courier New");
  expect(screen.maxWidth).toBe("100%");
  expect(screen.overflowX).toBe("auto");
  expect(screen.whiteSpace).toBe("pre");
  expect(screen.overflowWrap).toBe("normal");
  expect(screen.wordBreak).toBe("normal");
  expect(Number.parseFloat(screen.width)).toBeGreaterThan(350);
  await expect(page.getByRole("button", { name: "View Sales Invoice" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Print Sales Invoice" })).toHaveCount(0);
  await expect(page.getByAltText("QR code for the customer Digital Sales Invoice URL")).toBeVisible();
  await expect(page.getByText("Scan to view or download your Sales Invoice.")).toBeVisible();
});
