import { expect, test, type Page, type Request, type Route } from "@playwright/test";

const baseFixtureUrl = `http://127.0.0.1:${process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5196}`;
const ids = {
  available: "965a9700-fb9d-4f4c-be0a-5b3cbf8d6357",
  pending: "10000000-0000-4000-8000-000000000002",
  temporarilyUnavailable: "10000000-0000-4000-8000-000000000003",
  terminalFailure: "10000000-0000-4000-8000-000000000004",
  refreshPending: "10000000-0000-4000-8000-000000000005",
  expired: "10000000-0000-4000-8000-000000000006"
};
const statutoryRecoveryStorageKey = "exitpass:webpay:statutory-discount-recovery:v1";
const statutoryDecisionCommandId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const statutoryDiscountDecisionCommandId = statutoryDecisionCommandId;
const statutoryDiscountPayableBasisApplicationCommandId = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";

type FixtureState = {
  requestLog: Array<{ method: string; path: string; headers: Record<string, string | undefined>; body: unknown }>;
  receiptAttempts: Record<string, number>;
  statutoryReadAttempts: Record<string, number>;
  statutoryApplyAttempts: Record<string, number>;
  paymentIntentAttempts: Record<string, number>;
  ambiguousDecisionAttempts: Record<string, number>;
  providerHandoffCount: number;
  latestStatutoryDecisionSubmissionResponse: RecordedFixtureResponse | null;
  latestStatutoryDecisionReadResponse: RecordedFixtureResponse | null;
  latestPendingLifecycleRediscoveryResponse: RecordedFixtureResponse | null;
  latestPaymentIntentRequest: Record<string, unknown> | null;
  latestPaymentIntentResponse: RecordedFixtureResponse | null;
  latestValidationPaymentIntentReplay: {
    route: string;
    reusedRecordedBody: boolean;
    reusedIdempotencyKey: boolean;
    originalIdempotencyKeyPresent: boolean;
    replayIdempotencyKeyPresent: boolean;
    idempotencyKeyValuesMatch: boolean;
    idempotencyKeyDisposition: string;
    idempotencyKeyPreservationPassed: boolean;
    reusedCorrelationId: boolean;
    reusedCanonicalRequestIdentity: boolean;
    statusCode: number;
    body: Record<string, unknown>;
  } | null;
  validationPaymentIntentReplayCount: number;
  paymentIntentRequestLog: Array<Record<string, unknown>>;
  paymentIntentResponseLog: RecordedFixtureResponse[];
  observedPaymentAttemptIds: string[];
  observedProviderHandoffIdentities: string[];
  paymentIntentReplayResult: {
    requestCount: number;
    responseCount: number;
    successfulResponseCount: number;
    observedPaymentAttemptIds: string[];
    uniquePaymentAttemptCount: number;
    observedProviderHandoffIdentities: string[];
    uniqueProviderHandoffCount: number;
    samePaymentAttemptId: boolean;
    sameHandoffIdentity: boolean;
    semanticallyEquivalent: boolean;
  };
  fixtureLifecycleState: Record<string, unknown> | null;
};

type RecordedFixtureResponse = {
  statusCode: number;
  body: Record<string, unknown>;
};

const consoleMessagesByTest = new Map<string, string[]>();

test.beforeEach(async ({ page }, testInfo) => {
  await fetch(`${baseFixtureUrl}/__fixture/reset`, { method: "POST", body: "{}" });

  const consoleMessages: string[] = [];
  consoleMessagesByTest.set(testInfo.testId, consoleMessages);
  page.on("console", (message) => {
    consoleMessages.push(message.text());
  });
});

test.afterEach(async ({}, testInfo) => {
  const serialized = JSON.stringify(consoleMessagesByTest.get(testInfo.testId) ?? []);
  consoleMessagesByTest.delete(testInfo.testId);
  expect(serialized).not.toContain("TIN");
  expect(serialized).not.toContain("POS SERVER AUTHORITATIVE PRESENTATION");
  expect(serialized).not.toContain("SIA-00000002-A");
  expect(serialized).not.toContain("Authorization");
  expect(serialized).not.toContain("AppSecret");
  expect(serialized).not.toContain("api key");
  expect(serialized).not.toContain("SC-****-1234");
  expect(serialized).not.toContain("PWD-****-5678");
  expect(serialized).not.toContain("123456789012");
});

test.describe("WebPay authoritative Sales Invoice browser smoke", () => {
  test("confirmed payment displays the authoritative POS Server presentation and no local receipt fallback", async ({ page, browser }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.available);

    await expect(page.getByRole("heading", { name: /^payment confirmation$/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: /^sales invoice$/i })).toBeVisible();
    await expect(page.getByText("Payment reference number:", { exact: true })).toBeVisible();
    await expect(page.getByText("pay_eTN1CLQY5o9Dbv41Gj9vDAMs", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /copy payment reference number/i })).toBeVisible();
    await expect(page.getByText("SIA-00000002-A").first()).toBeVisible();
    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toHaveCount(0);
    await expect(page.getByText("CASHLESS")).toHaveCount(0);
    await expect(page.getByRole("heading", { name: /^exit instruction$/i })).toBeVisible();
    await expect(page.getByText("Proceed to exit")).toBeVisible();
    await expect(page.getByText("Additional parking charges will apply if you do not exit by the expiry time.")).toBeVisible();
    await expect(page.getByRole("heading", { name: /^exit qr code$/i })).toBeVisible();
    await expect(page.getByText("Parking Session Summary")).toHaveCount(0);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);

    const invoiceText = await page.locator(".sales-invoice-panel").innerText();
    expect(invoiceText).toContain("R35-TICKET-PREVIEW-0002");
    expect(invoiceText).not.toContain("PHP 129.00");
    expect(await browser.version()).toBeTruthy();
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("fiscal issuance pending stays pending without fabricating a receipt", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.pending);

    await expect(page.getByRole("heading", { name: /^payment confirmation$/i })).toBeVisible();
    await expect(page.getByText("Your payment is recorded. The Sales Invoice is still being prepared.")).toBeVisible();
    await expect(page.getByText("SIA-00000002-A")).toHaveCount(0);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
    await expect.poll(async () => (await getFixtureState()).receiptAttempts[ids.pending] ?? 0).toBe(3);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("temporary presentation unavailability retries within the bounded read loop and later displays the authoritative presentation", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.temporarilyUnavailable);

    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toHaveCount(0);
    await expect(page.getByText("SIA-00000002-A").first()).toBeVisible();
    await expect.poll(async () => (await getFixtureState()).receiptAttempts[ids.temporarilyUnavailable] ?? 0).toBe(3);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("terminal fiscal failure shows a safe customer-facing failure and no fallback receipt", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.terminalFailure);

    await expect(page.getByText("Sales Invoice unavailable")).toBeVisible();
    await expect(page.getByText("Sales Invoice issuance failed. Please contact support.")).toBeVisible();
    await expect(page.getByText("SIA-00000002-A")).toHaveCount(0);
    await expect(page.getByText(/stack trace|sql|connection string|internal exception/i)).toHaveCount(0);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("browser refresh while pending resumes readback from the durable payment attempt reference", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.refreshPending);
    await expect(page.getByText("Your payment is recorded. The Sales Invoice is still being prepared.")).toBeVisible();
    await expect.poll(async () => (await getFixtureState()).receiptAttempts[ids.refreshPending] ?? 0).toBe(3);

    await page.reload();

    await expect(page.getByText("Your payment is recorded. The Sales Invoice is still being prepared.")).toBeVisible();
    await expect.poll(async () => (await getFixtureState()).receiptAttempts[ids.refreshPending] ?? 0).toBe(6);
    await expectBrowserStorageSafe(page);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("later retrieval after temporary unavailability preserves one fiscal document identity", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.temporarilyUnavailable);
    await expect(page.getByText("SIA-00000002-A").first()).toBeVisible();

    await page.reload();

    await expect(page.getByText("SIA-00000002-A").first()).toBeVisible();
    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toHaveCount(0);
    const fiscalNumberOccurrences = await page.getByText("SIA-00000002-A").count();
    expect(fiscalNumberOccurrences).toBeGreaterThan(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("presentation viewing uses returned content and does not expose a competing WebPay fiscal document", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.available);

    await expect(page.locator(".sales-invoice-panel")).not.toContainText("POS SERVER AUTHORITATIVE PRESENTATION");
    await expect(page.locator(".sales-invoice-panel")).not.toContainText("CASHLESS");
    await expect(page.getByRole("button", { name: /download sales invoice/i })).toBeVisible();
    const printableInvoice = page.getByLabel("Printable Sales Invoice");
    await expect(printableInvoice).toHaveAttribute("data-paper-width", "80mm");
    await expect(page.getByRole("group", { name: /receipt paper width/i })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "57 mm" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "58 mm" })).toHaveCount(0);
    await expect(printableInvoice).toContainText("Ticket Number");
    await expect(printableInvoice).toContainText("PAYMENT DETAILS");
    await expect(printableInvoice).toContainText("QRPH");
    await expect(printableInvoice).toContainText("PayMongo");
    await expect(printableInvoice).toContainText("VAT BREAKDOWN");
    await expect(printableInvoice).toContainText("Customer Information");
    await expect(printableInvoice).toContainText("===== NOTHING FOLLOWS =====");
    await expect(printableInvoice).not.toContainText("Total Paid");
    await expect(printableInvoice).not.toContainText("Tendered Amount");
    await expect(printableInvoice).not.toContainText("Change");
    await expect(page.getByRole("button", { name: /retrieve sales invoice/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: /^exit qr code$/i })).toBeVisible();
    await expect(page.getByRole("img", { name: /^exit qr code$/i })).toBeVisible();
    await expect(page.getByText("Present this QR code to the scanner at the exit validator.")).toBeVisible();
    await expect(page.locator(".return-panel > section")).toHaveCount(4);
    await expect(page.getByText("Parking Session Summary")).toHaveCount(0);
    await expect(page.getByText(/WebPay issued|WebPay Sales Invoice|generated by WebPay/i)).toHaveCount(0);

    const invoiceDownloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: /download sales invoice/i }).click();
    const invoiceDownload = await invoiceDownloadPromise;
    expect(invoiceDownload.suggestedFilename()).toBe("SIA-00000002-A.pdf");

    const qrDownloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: /download exit qr code/i }).click();
    const qrDownload = await qrDownloadPromise;
    expect(qrDownload.suggestedFilename()).toBe("ExitPass-Ticket-R35-TICKET-PREVIEW-0002.png");
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("expired transaction reference is safe and does not create a fallback receipt", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, ids.expired);

    await expect(page.getByRole("alert")).toContainText("Payment reference was not found");
    await expect(page.getByText("Sales Invoice Number")).toHaveCount(0);
    await expect(page.getByText("SIA-00000002-A")).toHaveCount(0);
    await expect(page.getByText(/stack trace|connection string|authorization|api key|internal hostname/i)).toHaveCount(0);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });
});

test.describe("WebPay statutory discount pending-review browser smoke", () => {
  test("no-discount path remains available and uses the payment-intent route only after payment action", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await resolveTicketOnStartPage(page, "WEBPAY-STAT-NO-DISCOUNT");
    await expect(page.getByRole("button", { name: /request statutory discount/i })).toBeVisible();
    await page.getByRole("button", { name: /continue to payment/i }).click();
    await expect(page.getByRole("alert")).toContainText(/could not start payment|Payment intent creation failed|Browser smoke must not submit payment/i);

    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.path === "/v1/webpay/payment-intents")).toHaveLength(1);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
    await expectApiBoundary(apiRequests);
  });

  test("Senior Citizen request enters pending review, disables payment, and polls with GET", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await submitStatutoryRequest(page, "WEBPAY-STAT-PENDING-SC", "Senior Citizen", "SC00001234");

    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    await expect(page.getByText(/parking privilege request was received and is awaiting review/i).first()).toBeVisible();
    await expect(page.getByText(/status temporarily unavailable/i)).toHaveCount(0);
    await expectCustomerVisibleReferencesSafe(page);

    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(1);
    expect(state.requestLog.filter((request) => request.method === "GET" && request.path.includes("/statutory-discounts/decisions/")).length).toBeGreaterThan(0);
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
    await expectNoUnsafeStatutoryRequestFields();
    await expectApiBoundary(apiRequests);
  });

  test("PWD request enters pending review without raw identity or reviewer fields", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-PENDING-PWD", "PWD", "PW00005678");

    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    const state = await getFixtureState();
    const statutoryPost = state.requestLog.find((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions");
    expect(JSON.stringify(statutoryPost?.body)).toContain("PWD");
    expect(JSON.stringify(statutoryPost?.body)).not.toContain("sourceChannel");
    expect(JSON.stringify(statutorialPostSafeBody(statutoryPost?.body))).not.toContain("reviewer");
    expect(JSON.stringify(statutoryPost?.body)).not.toContain("123456789012");
  });

  test("polling transitions from pending to approved application-required without application intent", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-APP-REQ", "Senior Citizen", "SC-****-1234");

    await expect(page.getByRole("heading", { name: /entitlement approved/i })).toBeVisible();
    await expect(page.getByText(/Discount application is pending/i).first()).toBeVisible();
    await expect(page.getByRole("button", { name: /apply approved discount/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    await expectCustomerVisibleReferencesSafe(page);

    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });

  test("rejection, retryable, and terminal states remain safe and do not create payment", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-REJECTED", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /entitlement not approved/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    await expect(page.getByText(/reviewer|stack trace|internal exception/i)).toHaveCount(0);
    await expectCustomerVisibleReferencesSafe(page);

    await fetch(`${baseFixtureUrl}/__fixture/reset`, { method: "POST", body: "{}" });
    await submitStatutoryRequest(page, "WEBPAY-STAT-RETRYABLE", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /discount application temporarily unavailable/i })).toBeVisible();
    await expect(page.getByText(/Retry the same application request/i).first()).toBeVisible();

    await fetch(`${baseFixtureUrl}/__fixture/reset`, { method: "POST", body: "{}" });
    await submitStatutoryRequest(page, "WEBPAY-STAT-TERMINAL", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /statutory discount unavailable/i })).toBeVisible();
    await expect(page.getByText(/could not be completed/i).first()).toBeVisible();

    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
  });

  test("ready readback displays authoritative amounts and still does not submit application intent", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-READY", "Senior Citizen", "SC-****-1234");

    await expect(page.getByRole("heading", { name: /statutory discount applied/i })).toBeVisible();
    await expect(page.getByText("PHP 129.00").first()).toBeVisible();
    await expect(page.getByText("PHP 92.14")).toBeVisible();
    await expect(page.getByText("PHP 11.06")).toBeVisible();
    await expect(page.getByText("-PHP 25.80")).toBeVisible();
    await expect(page.getByText("PHP 103.20")).toBeVisible();
    await expectCustomerVisibleReferencesSafe(page);

    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });

  test("full ID entry is automatically masked before the statutory API call", async ({ page }) => {
    await resolveTicketOnStartPage(page, "WEBPAY-STAT-UNSAFE-ID");
    await page.getByRole("button", { name: /request statutory discount/i }).click();
    await page.getByLabel(/ID document type/i).fill("OSCA");
    await page.getByLabel(/Issuing authority/i).fill("Quezon City");
    await page.getByLabel(/^ID reference$/i).fill("123456789012");
    await page.getByLabel(/I confirm these entitlement details/i).check();
    await page.getByRole("button", { name: /submit for review/i }).click();

    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    const state = await getFixtureState();
    const decisionRequest = state.requestLog.find((request) => request.path.includes("/statutory-discounts/decisions"));
    expect(decisionRequest).toBeDefined();
    expect(decisionRequest?.body).toMatchObject({ maskedIdReference: "12******9012" });
    const customerMarkup = await page.locator("body").evaluate((body) => body.outerHTML);
    expect(customerMarkup).not.toContain("123456789012");
    expect(customerMarkup).not.toMatch(/(?:aria-label|aria-description|title)=["'][^"']*123456789012/i);
    const browserStorage = await page.evaluate(() => ({
      localStorage: JSON.stringify(localStorage),
      sessionStorage: JSON.stringify(sessionStorage)
    }));
    expect(browserStorage.localStorage).not.toContain("123456789012");
    expect(browserStorage.sessionStorage).not.toContain("123456789012");
  });

  test("ticket re-lookup with no browser recovery restores the same pending decision and continuation by rediscovery", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await resolveTicketOnStartPage(page, "WEBPAY-STAT-REDISCOVER-PENDING");

    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await expect(page.getByRole("link", { name: /this continuation link/i })).toHaveAttribute(
      "href",
      `${baseFixtureUrl}/privilege-review/g005-pending-review`
    );
    await expect(page.getByRole("button", { name: /pay regular amount/i })).toBeVisible();
    expect(await readStatutoryRecoveryStorage(page)).toContain(statutoryDecisionCommandId);

    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover")).toHaveLength(1);
    expect(state.requestLog.filter((request) => request.method === "GET" && request.path.includes("/statutory-discounts/decisions/"))).not.toHaveLength(0);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
    expect(state.requestLog.filter((request) => request.path.includes("/apply-payable-basis"))).toHaveLength(0);
    expect(state.requestLog.filter((request) => request.path === "/v1/webpay/payment-intents")).toHaveLength(0);
    expect(state.latestPendingLifecycleRediscoveryResponse).toMatchObject({
      statusCode: 200,
      body: {
        classification: "FOUND",
        statutoryDecisionId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
        statutoryDecisionCommandId,
        requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        opaqueContinuationReference: "continuation:g005:pending-review",
        opaqueContinuationUrl: `${baseFixtureUrl}/privilege-review/g005-pending-review`
      }
    });
    expect(state.fixtureLifecycleState).toMatchObject({
      statutoryDecisionId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
      statutoryDecisionCommandId,
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      opaqueContinuationReference: "continuation:g005:pending-review",
      opaqueContinuationUrl: `${baseFixtureUrl}/privilege-review/g005-pending-review`
    });
    await expectApiBoundary(apiRequests);
    await expectBrowserStorageSafe(page);
  });

  test("fresh browser restart and plate re-lookup recover the same pending decision without duplicate creation", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await resolveTicketOnStartPage(page, "WEBPAY-STAT-REDISCOVER-PENDING");
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();

    const restartedPage = await page.context().newPage();
    await restartedPage.goto("/?webpayStatutoryRecoveryReset=1");
    await restartedPage.getByRole("button", { name: /plate/i }).click();
    await restartedPage.getByLabel(/plate number/i).fill("G005PLATE");
    await restartedPage.getByRole("button", { name: /^continue$/i }).click();

    await expect(restartedPage.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await expect(restartedPage.getByRole("link", { name: /this continuation link/i })).toHaveAttribute(
      "href",
      `${baseFixtureUrl}/privilege-review/g005-pending-review`
    );

    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover")).toHaveLength(2);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
    expect(state.requestLog.filter((request) => request.path.includes("/apply-payable-basis"))).toHaveLength(0);
    expect(state.requestLog.filter((request) => request.path === "/v1/webpay/payment-intents")).toHaveLength(0);
    await expectApiBoundary(apiRequests);
    await expectBrowserStorageSafe(restartedPage);
    await restartedPage.close();
  });

  test("pending-review Pay regular amount requires warning and creates ordinary payment intent without statutory IDs", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await submitStatutoryRequest(page, "WEBPAY-STAT-PENDING-REGULAR", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await page.getByRole("button", { name: /pay regular amount/i }).click();

    await expect(page.getByRole("dialog", { name: /proceed without the parking privilege/i })).toBeVisible();
    await expect(page.getByText(/will not automatically refund or retroactively adjust/i)).toBeVisible();
    let state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents")).toHaveLength(0);
    expect(state.providerHandoffCount).toBe(0);
    await page.getByRole("button", { name: /keep waiting/i }).click();
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();

    await page.getByRole("button", { name: /pay regular amount/i }).click();
    await page.getByRole("button", { name: /continue with regular payment/i }).click();
    await expect(page.getByRole("link", { name: /continue to payment/i })).toHaveAttribute("href", "https://payments.test/handoff");

    state = await getFixtureState();
    await replayLatestPaymentIntent(page, state);
    state = await getFixtureState();
    const paymentRequests = state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents");
    expect(paymentRequests).toHaveLength(2);
    const body = paymentRequests[0].body as Record<string, unknown>;
    expect(body.expectedAmountMinorUnits).toBe(12900);
    expect(body.expectedCurrency).toBe("PHP");
    expect(body.tariffSnapshotId).toBe("30000000-0000-4000-8000-000000000001");
    expect(body).not.toHaveProperty("statutoryDiscountDecisionCommandId");
    expect(body).not.toHaveProperty("statutoryDiscountPayableBasisApplicationCommandId");
    expect(state.latestStatutoryDecisionSubmissionResponse?.body).toMatchObject({
      statutoryDiscountDecisionCommandId,
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    });
    expect(state.latestStatutoryDecisionReadResponse?.body).toMatchObject({
      statutoryDiscountDecisionCommandId,
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    });
    expect(state.latestPaymentIntentRequest).toEqual(body);
    expect(state.latestPaymentIntentResponse).toMatchObject({
      statusCode: 200,
      body: {
        amountMinorUnits: 12900,
        currency: "PHP"
      }
    });
    expect(state.paymentIntentRequestLog).toHaveLength(2);
    expect(state.paymentIntentResponseLog).toHaveLength(2);
    expect(state.observedPaymentAttemptIds).toEqual(["10000000-0000-4000-8000-000000000010"]);
    expect(state.observedProviderHandoffIdentities).toEqual(["https://payments.test/handoff"]);
    expect(state.paymentIntentReplayResult).toMatchObject({
      requestCount: 2,
      responseCount: 2,
      successfulResponseCount: 2,
      uniquePaymentAttemptCount: 1,
      uniqueProviderHandoffCount: 1,
      samePaymentAttemptId: true,
      sameHandoffIdentity: true,
      semanticallyEquivalent: true
    });
    expect(state.latestValidationPaymentIntentReplay).toMatchObject({
      route: "/v1/webpay/payment-intents",
      reusedRecordedBody: true,
      reusedIdempotencyKey: false,
      originalIdempotencyKeyPresent: false,
      replayIdempotencyKeyPresent: false,
      idempotencyKeyValuesMatch: true,
      idempotencyKeyDisposition: "ABSENT_IN_RECORDED_BROWSER_REQUEST",
      idempotencyKeyPreservationPassed: true,
      reusedCorrelationId: true,
      reusedCanonicalRequestIdentity: true,
      statusCode: 200
    });
    expect(state.validationPaymentIntentReplayCount).toBe(1);
    expect(state.providerHandoffCount).toBe(1);
    await expectApiBoundary(apiRequests);
  });

  test("changed regular amount before pending-review payment requires renewed confirmation", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-REGULAR-AMOUNT-CHANGED", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();

    await page.getByRole("button", { name: /pay regular amount/i }).click();
    await page.getByRole("button", { name: /continue with regular payment/i }).click();
    await expect(page.getByText(/regular parking amount changed before payment/i)).toBeVisible();
    await expect(page.getByText("PHP 159.00").first()).toBeVisible();
    let state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents")).toHaveLength(0);
    expect(state.providerHandoffCount).toBe(0);

    await page.getByRole("button", { name: /continue with regular payment/i }).click();
    await expect(page.getByRole("link", { name: /continue to payment/i })).toHaveAttribute("href", "https://payments.test/handoff");

    state = await getFixtureState();
    await replayLatestPaymentIntent(page, state);
    state = await getFixtureState();
    const paymentRequests = state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents");
    expect(paymentRequests).toHaveLength(2);
    const body = paymentRequests[0].body as Record<string, unknown>;
    expect(body.expectedAmountMinorUnits).toBe(15900);
    expect(body.tariffSnapshotId).toBe("30000000-0000-4000-8000-000000000099");
    expect(body).not.toHaveProperty("statutoryDiscountDecisionCommandId");
    expect(body).not.toHaveProperty("statutoryDiscountPayableBasisApplicationCommandId");
    expect(state.latestPaymentIntentRequest).toEqual(body);
    expect(state.latestPaymentIntentResponse).toMatchObject({
      statusCode: 200,
      body: {
        amountMinorUnits: 15900,
        currency: "PHP"
      }
    });
    expect(state.paymentIntentRequestLog).toHaveLength(2);
    expect(state.paymentIntentResponseLog).toHaveLength(2);
    expect(state.observedPaymentAttemptIds).toEqual(["10000000-0000-4000-8000-000000000010"]);
    expect(state.observedProviderHandoffIdentities).toEqual(["https://payments.test/handoff"]);
    expect(state.paymentIntentReplayResult).toMatchObject({
      requestCount: 2,
      responseCount: 2,
      successfulResponseCount: 2,
      uniquePaymentAttemptCount: 1,
      uniqueProviderHandoffCount: 1,
      samePaymentAttemptId: true,
      sameHandoffIdentity: true,
      semanticallyEquivalent: true
    });
    expect(state.latestValidationPaymentIntentReplay).toMatchObject({
      route: "/v1/webpay/payment-intents",
      reusedRecordedBody: true,
      reusedIdempotencyKey: false,
      originalIdempotencyKeyPresent: false,
      replayIdempotencyKeyPresent: false,
      idempotencyKeyValuesMatch: true,
      idempotencyKeyDisposition: "ABSENT_IN_RECORDED_BROWSER_REQUEST",
      idempotencyKeyPreservationPassed: true,
      reusedCorrelationId: true,
      reusedCanonicalRequestIdentity: true,
      statusCode: 200
    });
    expect(state.validationPaymentIntentReplayCount).toBe(1);
    expect(state.providerHandoffCount).toBe(1);
  });

  for (const scenario of [
    ["WEBPAY-STAT-REDISCOVER-NOT-FOUND", "NOT_FOUND"],
    ["WEBPAY-STAT-REDISCOVER-NO-ACTIVE", "NO_ACTIVE_LIFECYCLE"],
    ["WEBPAY-STAT-REDISCOVER-AMBIGUOUS", "AMBIGUOUS_SESSION"],
    ["WEBPAY-STAT-REDISCOVER-UNAVAILABLE", "SOURCE_UNAVAILABLE"],
    ["WEBPAY-STAT-REDISCOVER-MALFORMED", "MALFORMED_AUTHORITATIVE_STATE"],
    ["WEBPAY-STAT-REDISCOVER-DENIED", "ACCESS_DENIED"],
    ["WEBPAY-STAT-REDISCOVER-UNEXPECTED", "UNEXPECTED_FAILURE"]
  ] as const) {
    test(`${scenario[1]} rediscovery does not fabricate pending state or create durable side effects`, async ({ page }) => {
      const apiRequests = collectApiRequests(page);

      await resolveTicketOnStartPage(page, scenario[0]);

      await expect(page.getByRole("heading", { name: /awaiting review/i })).toHaveCount(0);
      await expect(page.getByRole("button", { name: /continue to payment/i }).first()).toBeVisible();
      const state = await getFixtureState();
      const rediscovery = state.requestLog.find((request) => request.path === "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover");
      expect(rediscovery).toBeTruthy();
      expect(JSON.stringify(rediscovery?.body)).toContain("PARKING_SESSION_ID");
      expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
      expect(state.requestLog.filter((request) => request.path.includes("/apply-payable-basis"))).toHaveLength(0);
      expect(state.requestLog.filter((request) => request.path === "/v1/webpay/payment-intents")).toHaveLength(0);
      await expectApiBoundary(apiRequests);
      await expectBrowserStorageSafe(page);
    });
  }
});

test.describe("WebPay statutory discount applied payment browser smoke", () => {
  test("applied statutory state enables payment using the authoritative applied basis", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLIED-PAYMENT", "Senior Citizen", "SC-****-1234");

    await expect(page.getByRole("heading", { name: /statutory discount applied/i })).toBeVisible();
    await expect(page.getByText("PHP 50.00")).toBeVisible();
    await expect(page.getByText("-PHP 10.00")).toBeVisible();
    await expect(page.getByText("PHP 40.00")).toBeVisible();
    await expect(page.getByText(/Payment is available using the Central PMS-approved statutory payable basis/i)).toBeVisible();
    await expectCustomerVisibleReferencesSafe(page);

    await page.getByRole("button", { name: /continue to payment/i }).click();

    await expect(page.getByRole("link", { name: /continue to payment/i })).toHaveAttribute("href", "https://payments.test/handoff");
    const state = await getFixtureState();
    const paymentRequests = state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents");
    expect(paymentRequests).toHaveLength(1);
    const body = paymentRequests[0].body as Record<string, unknown>;
    expect(body.tariffSnapshotId).toBe("99999999-9999-4999-8999-999999999999");
    expect(body.tariffSnapshotId).not.toBe("30000000-0000-4000-8000-000000000001");
    expect(body.expectedAmountMinorUnits).toBe(4000);
    expect(body.expectedCurrency).toBe("PHP");
    expect(body.statutoryDiscountDecisionCommandId).toBe("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    expect(body.statutoryDiscountPayableBasisApplicationCommandId).toBe("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    expect(JSON.stringify(body)).not.toContain("statutoryDiscountAmountMinorUnits");
    expect(JSON.stringify(body)).not.toContain("vatAmountMinorUnits");
    expect(JSON.stringify(body)).not.toContain("reviewer");
    await expectApiBoundary(apiRequests);
  });

  test("rapid payment clicks for applied statutory state create one payment intent", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLIED-DUPLICATE", "Senior Citizen", "SC-****-1234");

    await expect(page.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
    await page.getByRole("button", { name: /continue to payment/i }).dblclick();

    await expect(page.getByRole("link", { name: /continue to payment/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents")).toHaveLength(1);
    expect(state.paymentIntentAttempts["WEBPAY-STAT-APPLIED-DUPLICATE"]).toBe(1);
  });

  for (const scenario of [
    { name: "pending review", ticketReference: "WEBPAY-STAT-PENDING-SC", heading: /awaiting review/i },
    { name: "application processing", ticketReference: "WEBPAY-STAT-APPLY-PROCESSING", heading: /entitlement approved/i },
    { name: "rejected", ticketReference: "WEBPAY-STAT-REJECTED", heading: /entitlement not approved/i },
    { name: "retryable", ticketReference: "WEBPAY-STAT-APPLY-RETRYABLE", heading: /entitlement approved/i },
    { name: "terminal", ticketReference: "WEBPAY-STAT-TERMINAL", heading: /statutory discount unavailable/i },
    { name: "missing applied snapshot", ticketReference: "WEBPAY-STAT-MISSING-SNAPSHOT", heading: /payment basis incomplete/i },
    { name: "missing final amount", ticketReference: "WEBPAY-STAT-MISSING-AMOUNT", heading: /payment basis incomplete/i },
    { name: "missing currency", ticketReference: "WEBPAY-STAT-MISSING-CURRENCY", heading: /payment basis incomplete/i }
  ]) {
    test(`${scenario.name} cannot submit statutory payment`, async ({ page }) => {
      await submitStatutoryRequest(page, scenario.ticketReference, "Senior Citizen", "SC-****-1234");

      await expect(page.getByRole("heading", { name: scenario.heading })).toBeVisible();
      const pendingButton = page.getByRole("button", { name: /statutory discount pending/i });
      await expect(pendingButton).toBeDisabled();
      const state = await getFixtureState();
      expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents")).toHaveLength(0);
    });
  }

  test("payment return still displays authoritative Sales Invoice presentation", async ({ page }) => {
    await openReturnPage(page, ids.available);

    await expect(page.getByRole("heading", { name: /^sales invoice$/i })).toBeVisible();
    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toHaveCount(0);
    await expect(page.getByText("SIA-00000002-A").first()).toBeVisible();
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
  });
});

test.describe("WebPay statutory discount application-intent browser smoke", () => {
  test("approved decision displays application action without posting until clicked", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLY-PROCESSING", "Senior Citizen", "SC-****-1234");

    await expect(page.getByRole("heading", { name: /entitlement approved/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /apply approved discount/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    let state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);

    await page.getByRole("button", { name: /apply approved discount/i }).click();

    await expect(page.getByRole("heading", { name: /discount application processing/i })).toBeVisible();
    state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"))).toHaveLength(1);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/payment-intents"))).toHaveLength(0);
    expect((state.statutoryApplyAttempts["aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"] ?? 0)).toBe(1);
    await expectApiBoundary(apiRequests);
  });

  test("application polling reaches applied readback and displays authoritative amounts", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLY-READY", "Senior Citizen", "SC-****-1234");
    await page.getByRole("button", { name: /apply approved discount/i }).click();

    await expect(page.getByRole("heading", { name: /statutory discount applied/i })).toBeVisible();
    await expect(page.getByText("PHP 129.00").first()).toBeVisible();
    await expect(page.getByText("PHP 92.14")).toBeVisible();
    await expect(page.getByText("PHP 11.06")).toBeVisible();
    await expect(page.getByText("-PHP 25.80")).toBeVisible();
    await expect(page.getByText("PHP 103.20")).toBeVisible();
    await expect(page.getByRole("button", { name: /continue to payment/i })).toBeEnabled();

    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"))).toHaveLength(1);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });

  test("temporary application unavailability retries deliberately with the original application key", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLY-RETRYABLE", "Senior Citizen", "SC-****-1234");
    await page.getByRole("button", { name: /apply approved discount/i }).click();

    await expect(page.getByRole("heading", { name: /discount application temporarily unavailable/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /retry discount application/i })).toBeVisible();

    let state = await getFixtureState();
    const firstApply = state.requestLog.find((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"));
    const originalKey = firstApply?.headers["idempotency-key"] ?? firstApply?.headers["Idempotency-Key"];
    await page.getByRole("button", { name: /retry discount application/i }).click();

    await expect(page.getByRole("heading", { name: /statutory discount applied/i })).toBeVisible();
    state = await getFixtureState();
    const applyRequests = state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"));
    expect(applyRequests).toHaveLength(2);
    const retryKey = applyRequests[1].headers["idempotency-key"] ?? applyRequests[1].headers["Idempotency-Key"];
    expect(retryKey).toBe(originalKey);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });

  test("semantic conflict and terminal application outcomes stop safely", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLY-CONFLICT", "Senior Citizen", "SC-****-1234");
    await page.getByRole("button", { name: /apply approved discount/i }).click();
    await expect(page.getByRole("heading", { name: /statutory discount conflict/i })).toBeVisible();
    await expect(page.getByText(/canonical decision/i).first()).toBeVisible();
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();

    await fetch(`${baseFixtureUrl}/__fixture/reset`, { method: "POST", body: "{}" });
    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLY-TERMINAL", "Senior Citizen", "SC-****-1234");
    await page.getByRole("button", { name: /apply approved discount/i }).click();
    await expect(page.getByRole("heading", { name: /statutory discount unavailable/i })).toBeVisible();
    await expect(page.getByText(/could not be completed/i).first()).toBeVisible();

    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });

  test("pending and rejected decisions never expose application intent action", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-PENDING-SC", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /apply approved discount/i })).toHaveCount(0);

    await fetch(`${baseFixtureUrl}/__fixture/reset`, { method: "POST", body: "{}" });
    await submitStatutoryRequest(page, "WEBPAY-STAT-REJECTED", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /entitlement not approved/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /apply approved discount/i })).toHaveCount(0);

    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });
});

test.describe("WebPay statutory discount browser recovery smoke", () => {
  test("refresh during awaiting review restores by GET readback and does not repeat decision POST", async ({ page }) => {
    await seedStatutoryScenario("pending", "WEBPAY-STAT-RECOVERY-PENDING");
    await seedRecoveryRecord(page, "DECISION_PENDING", {
      statutoryDiscountDecisionCommandId
    });

    await page.goto("/?ticketReference=WEBPAY-STAT-RECOVERY-PENDING");

    await expect(page.getByText(/Existing statutory discount request restored/i)).toBeVisible();
    await page.getByRole("button", { name: /^continue$/i }).click();
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "GET" && request.path.includes("/statutory-discounts/decisions/"))).not.toHaveLength(0);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
  });

  test("refresh after approval restores application-required state without repeating decision POST", async ({ page }) => {
    await seedStatutoryScenario("application-required", "WEBPAY-STAT-RECOVERY-APPROVED");
    await seedRecoveryRecord(page, "APPLICATION_AVAILABLE", {
      statutoryDiscountDecisionCommandId
    });

    await page.goto("/?ticketReference=WEBPAY-STAT-RECOVERY-APPROVED");

    await page.getByRole("button", { name: /^continue$/i }).click();
    await expect(page.getByRole("heading", { name: /entitlement approved/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /apply approved discount/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"))).toHaveLength(0);
  });

  test("refresh during application processing restores by GET and does not repeat application POST", async ({ page }) => {
    await seedStatutoryScenario("application-processing", "WEBPAY-STAT-RECOVERY-PROCESSING");
    await seedRecoveryRecord(page, "APPLICATION_PROCESSING", {
      statutoryDiscountDecisionCommandId,
      statutoryDiscountPayableBasisApplicationCommandId,
      applicationIdempotencyKey: "webpay-statutory-discount-application:browser-smoke"
    });

    await page.goto("/?ticketReference=WEBPAY-STAT-RECOVERY-PROCESSING");

    await page.getByRole("button", { name: /^continue$/i }).click();
    await expect(page.getByRole("heading", { name: /discount application processing/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "GET" && request.path.includes("/statutory-discounts/decisions/"))).not.toHaveLength(0);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"))).toHaveLength(0);
  });

  test("refresh after applied readback requires fresh GET before enabling payment", async ({ page }) => {
    await seedStatutoryScenario("applied-payment-ready", "WEBPAY-STAT-APPLIED-PAYMENT");
    await seedRecoveryRecord(page, "PAYABLE_READY", {
      statutoryDiscountDecisionCommandId,
      statutoryDiscountPayableBasisApplicationCommandId,
      applicationIdempotencyKey: "webpay-statutory-discount-application:browser-smoke"
    });

    await page.goto("/?ticketReference=WEBPAY-STAT-APPLIED-PAYMENT");
    await page.getByRole("button", { name: /^continue$/i }).click();
    await expect(page.getByRole("heading", { name: /statutory discount applied/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /continue to payment/i })).toBeEnabled();

    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "GET" && request.path.includes("/statutory-discounts/decisions/"))).not.toHaveLength(0);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/payment-intents"))).toHaveLength(0);
  });

  test("browser page restart restores safe decision reference without repeating POST", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-RECOVERY-RESTART", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();

    const restartedPage = await page.context().newPage();
    await restartedPage.goto("/?ticketReference=WEBPAY-STAT-RECOVERY-RESTART");

    await expect(restartedPage.getByText(/statutory discount workflow was found|Existing statutory discount request restored/i)).toBeVisible();
    await restartedPage.getByRole("button", { name: /^continue$/i }).click();
    await expect(restartedPage.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(1);
    await restartedPage.close();
  });

  test("malformed and expired recovery records fail closed", async ({ page }) => {
    await page.addInitScript((key) => localStorage.setItem(key, "{broken"), statutoryRecoveryStorageKey);
    await page.goto("/");
    await expect(page.getByText(/invalid statutory discount recovery record was cleared/i)).toBeVisible();
    await expect(page.getByRole("button", { name: /^continue$/i })).toBeEnabled();

    const expiredPage = await page.context().newPage();
    await seedRecoveryRecord(expiredPage, "PAYABLE_READY", {
      statutoryDiscountDecisionCommandId,
      expiresAt: "2026-01-01T00:00:00.000Z"
    });
    await expiredPage.goto("/");
    expect(await readStatutoryRecoveryStorage(expiredPage)).toBeNull();
    await expect(expiredPage.getByRole("button", { name: /^continue$/i })).toBeEnabled();
    await expiredPage.close();
  });

  test("storage unavailable continues safely in memory", async ({ page }) => {
    await page.addInitScript(() => {
      const blocked = () => {
        throw new DOMException("Browser storage blocked", "SecurityError");
      };
      Storage.prototype.getItem = blocked;
      Storage.prototype.setItem = blocked;
      Storage.prototype.removeItem = blocked;
    });

    await submitStatutoryRequest(page, "WEBPAY-STAT-STORAGE-UNAVAILABLE", "Senior Citizen", "SC-****-1234");

    await expect(page.getByText(/Durable statutory discount recovery is unavailable/i)).toBeVisible();
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(1);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/payment-intents"))).toHaveLength(0);
  });

  test("ambiguous decision response retries with original decision key", async ({ page }) => {
    await resolveTicketOnStartPage(page, "WEBPAY-STAT-AMBIGUOUS-DECISION");
    await page.getByRole("button", { name: /request statutory discount/i }).click();
    await page.getByLabel(/ID document type/i).fill("OSCA");
    await page.getByLabel(/Issuing authority/i).fill("Quezon City");
    await page.getByLabel(/^ID reference$/i).fill("SC00001234");
    await page.getByLabel(/I confirm these entitlement details/i).check();
    await page.getByRole("button", { name: /submit for review/i }).click();
    await expect(page.getByRole("alert")).toContainText(/temporarily unavailable/i);

    let state = await getFixtureState();
    const firstPost = state.requestLog.find((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions");
    const originalKey = firstPost?.headers["idempotency-key"];

    await page.getByRole("button", { name: /submit for review/i }).click();
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();

    state = await getFixtureState();
    const decisionPosts = state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions");
    expect(decisionPosts).toHaveLength(2);
    expect(decisionPosts[1].headers["idempotency-key"]).toBe(originalKey);
    expect(state.ambiguousDecisionAttempts[originalKey ?? ""]).toBe(2);
  });

  test("ambiguous application recovery retries with original key and no payment intent", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLY-RETRYABLE", "Senior Citizen", "SC-****-1234");
    await page.getByRole("button", { name: /apply approved discount/i }).click();
    await expect(page.getByRole("heading", { name: /discount application temporarily unavailable/i })).toBeVisible();

    let state = await getFixtureState();
    const firstApply = state.requestLog.find((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"));
    const originalKey = firstApply?.headers["idempotency-key"];

    await page.getByRole("button", { name: /retry discount application/i }).click();
    await expect(page.getByRole("heading", { name: /statutory discount applied/i })).toBeVisible();

    state = await getFixtureState();
    const applyPosts = state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/apply-payable-basis"));
    expect(applyPosts).toHaveLength(2);
    expect(applyPosts[1].headers["idempotency-key"]).toBe(originalKey);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path.includes("/payment-intents"))).toHaveLength(0);
  });

  test("ambiguous payment handoff preserves one payment key and does not create a second provider handoff", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-AMBIGUOUS-PAYMENT", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
    await page.getByRole("button", { name: /continue to payment/i }).dblclick();

    await expect(page.getByRole("heading", { name: /Payment already started/i })).toBeVisible();
    await expect(page.getByRole("link", { name: /continue existing payment/i })).toHaveAttribute("href", "https://payments.test/existing-handoff");

    const state = await getFixtureState();
    const paymentPosts = state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents");
    expect(paymentPosts).toHaveLength(1);
    const storage = await readStatutoryRecoveryStorage(page);
    expect(storage).toContain("10000000-0000-4000-8000-000000000011");
    expect(storage).toContain("paymentIntentCorrelationId");
  });

  test("two tabs observe recovery updates and stale tab cannot submit payment without refresh", async ({ page }) => {
    await seedStatutoryScenario("applied-payment-ready", "WEBPAY-STAT-APPLIED-PAYMENT");
    await page.goto("/");
    const record = buildRecoveryRecord("PAYABLE_READY", {
      statutoryDiscountDecisionCommandId,
      statutoryDiscountPayableBasisApplicationCommandId,
      applicationIdempotencyKey: "webpay-statutory-discount-application:browser-smoke",
      paymentIntentCorrelationId: "webpay-payment-intent-browser-smoke"
    });
    await page.evaluate(({ key, value }) => localStorage.setItem(key, JSON.stringify(value)), {
      key: statutoryRecoveryStorageKey,
      value: record
    });

    const secondPage = await page.context().newPage();
    await secondPage.goto("/?ticketReference=WEBPAY-STAT-APPLIED-PAYMENT");
    await expect(secondPage.getByText(/statutory discount workflow is ready for payment/i)).toBeVisible();
    await secondPage.getByRole("button", { name: /^continue$/i }).click();
    await expect(secondPage.getByRole("button", { name: /continue to payment/i })).toBeEnabled();

    let releaseAuthoritativeReadback = () => undefined;
    const authoritativeReadbackGate = new Promise<void>((resolve) => {
      releaseAuthoritativeReadback = resolve;
    });
    const decisionReadPattern = "**/v1/webpay/statutory-discounts/decisions/**";
    const gateDecisionRead = async (route: Route) => {
      await authoritativeReadbackGate;
      await route.continue();
    };
    await secondPage.route(decisionReadPattern, gateDecisionRead);

    await page.evaluate(({ key, value }) => {
      localStorage.setItem(key, JSON.stringify({ ...value, stage: "PAYMENT_SUBMITTING", updatedAt: new Date().toISOString() }));
    }, {
      key: statutoryRecoveryStorageKey,
      value: record
    });

    await expect(secondPage.getByText(/Another page may be starting payment/i)).toBeVisible();
    await expect(secondPage.getByRole("button", { name: /continue to payment/i })).toBeDisabled();
    releaseAuthoritativeReadback();
    await secondPage.unroute(decisionReadPattern, gateDecisionRead);
    await secondPage.reload();
    await expect(secondPage.getByText(/Another page may be starting payment/i)).toBeVisible();
    await secondPage.getByRole("button", { name: /^continue$/i }).click();
    await expect.poll(async () => {
      const state = await getFixtureState();
      return state.requestLog.filter(
        (request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover"
      ).length;
    }).toBe(2);
    await expect.poll(async () => {
      const state = await getFixtureState();
      return state.requestLog.filter(
        (request) => request.method === "GET" && request.path.includes("/v1/webpay/statutory-discounts/decisions/")
      ).length;
    }).toBeGreaterThan(0);
    await expect(secondPage.getByRole("button", { name: /continue to payment/i })).toBeDisabled();
    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents")).toHaveLength(0);
    expect(state.providerHandoffCount).toBe(0);
    const browserStorage = await secondPage.evaluate((key) => ({
      local: localStorage.getItem(key),
      session: sessionStorage.getItem(key)
    }), statutoryRecoveryStorageKey);
    expect(JSON.parse(browserStorage.local ?? "{}").stage).toBe("PAYMENT_SUBMITTING");
    expect(browserStorage.session).toBeNull();
    await secondPage.close();
  });

  test("payment attempt handoff metadata is preserved and terminal cleanup clears browser-only recovery", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-APPLIED-PAYMENT", "Senior Citizen", "SC-****-1234");
    await page.getByRole("button", { name: /continue to payment/i }).click();
    await expect(page.getByRole("link", { name: /continue to payment/i })).toBeVisible();

    let storage = await readStatutoryRecoveryStorage(page);
    expect(storage).toContain("10000000-0000-4000-8000-000000000010");
    expect(storage).toContain("PAYMENT_HANDOFF");

    await page.getByRole("button", { name: /clear browser recovery/i }).click();
    storage = await readStatutoryRecoveryStorage(page);
    expect(storage).toBeNull();
    await expect(page.getByText(/Browser recovery metadata was cleared/i)).toBeVisible();
  });

  test("no-discount path and authoritative Sales Invoice presentation remain unchanged", async ({ page }) => {
    await resolveTicketOnStartPage(page, "WEBPAY-STAT-NO-DISCOUNT-RECOVERY");
    await expect(page.getByRole("button", { name: /request statutory discount/i })).toBeVisible();
    expect(await readStatutoryRecoveryStorage(page)).toBeNull();

    await openReturnPage(page, ids.available);
    await expect(page.getByRole("heading", { name: /^sales invoice$/i })).toBeVisible();
    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toHaveCount(0);
    expect(await readStatutoryRecoveryStorage(page)).toBeNull();
  });
});

async function openReturnPage(page: Page, paymentAttemptId: string) {
  const query = new URLSearchParams({
    paymentAttemptId,
    correlationId: "77777777-7777-4777-8777-777777777777",
    result: "success"
  });
  await page.goto(`/webpay/payment-return?${query.toString()}`);
}

type StatutoryRecoveryStage =
  | "DECISION_SUBMITTING"
  | "DECISION_PENDING"
  | "APPLICATION_AVAILABLE"
  | "APPLICATION_SUBMITTING"
  | "APPLICATION_PROCESSING"
  | "PAYABLE_READY"
  | "PAYMENT_SUBMITTING"
  | "PAYMENT_HANDOFF"
  | "TERMINAL";

function buildRecoveryRecord(stage: StatutoryRecoveryStage, overrides: Record<string, unknown> = {}) {
  const now = new Date();
  return {
    schemaVersion: 1,
    parkingSessionId: "20000000-0000-4000-8000-000000000001",
    entitlementType: "SENIOR_CITIZEN",
    decisionIdempotencyKey: "webpay-statutory-discount-decision:browser-smoke",
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    correlationId: "77777777-7777-4777-8777-777777777777",
    stage,
    createdAt: now.toISOString(),
    updatedAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + 6 * 60 * 60 * 1000).toISOString(),
    ...overrides
  };
}

async function seedRecoveryRecord(page: Page, stage: StatutoryRecoveryStage, overrides: Record<string, unknown> = {}) {
  const record = buildRecoveryRecord(stage, overrides);
  await page.addInitScript(({ key, value }) => localStorage.setItem(key, JSON.stringify(value)), {
    key: statutoryRecoveryStorageKey,
    value: record
  });
}

async function seedStatutoryScenario(scenario: string, ticketReference: string) {
  await fetch(`${baseFixtureUrl}/__fixture/statutory-scenario`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      decisionId: statutoryDecisionCommandId,
      scenario,
      ticketReference
    })
  });
}

async function readStatutoryRecoveryStorage(page: Page): Promise<string | null> {
  return page.evaluate((key) => localStorage.getItem(key), statutoryRecoveryStorageKey);
}

async function resolveTicketOnStartPage(page: Page, ticketReference: string) {
  await page.goto("/");
  await page.getByLabel(/ticket reference/i).fill(ticketReference);
  await page.getByRole("button", { name: /^continue$/i }).click();
  await expect(page.getByText("Parking Session Summary")).toBeVisible();
}

async function submitStatutoryRequest(page: Page, ticketReference: string, entitlementLabel: string, idReference: string) {
  await resolveTicketOnStartPage(page, ticketReference);
  await page.getByRole("button", { name: /request statutory discount/i }).click();
  await page.getByLabel(/Entitlement type/i).selectOption({ label: entitlementLabel });
  await page.getByLabel(/ID document type/i).fill(entitlementLabel === "PWD" ? "PWD ID" : "OSCA");
  await page.getByLabel(/Issuing authority/i).fill("Quezon City");
  await page.getByLabel(/^ID reference$/i).fill(idReference.replaceAll("*", "0"));
  await page.getByLabel(/I confirm these entitlement details/i).check();
  await page.getByRole("button", { name: /submit for review/i }).click();
}

function collectApiRequests(page: Page): Request[] {
  const requests: Request[] = [];
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (url.pathname.startsWith("/v1/")) {
      requests.push(request);
    }
  });
  return requests;
}

async function expectCustomerVisibleReferencesSafe(page: Page) {
  const customerDom = await page.locator("body").evaluate((element) => element.outerHTML);
  expect(customerDom).not.toMatch(/\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/i);

  const supportReferences = page.locator(".support-reference");
  await expect(supportReferences).toHaveCount(1);
  await expect(supportReferences).toHaveText(/Support reference:\s*[0-9A-F]{4}-[0-9A-F]{4}/);
}

async function expectApiBoundary(requests: Request[]) {
  expect(requests.length).toBeGreaterThan(0);

  for (const request of requests) {
    const url = new URL(request.url());
    expect(url.pathname).toMatch(/^\/v1\/webpay\//);
    expect(url.pathname).not.toContain("/v1/admin/");
    expect(url.hostname).toBe("127.0.0.1");
    const headers = await request.allHeaders();
    expect(headers["x-posserver-admin-key"]).toBeUndefined();
    expect(headers["x-posserver-admin-permission"]).toBeUndefined();
    expect(headers["x-exitpass-service-identity-id"]).toBeUndefined();
    expect(headers["x-exitpass-permissions"]).toBeUndefined();
    expect(headers.authorization).toBeUndefined();
    expect(headers["x-correlation-id"]).toBeTruthy();
  }
}

async function getFixtureState(): Promise<FixtureState> {
  const response = await fetch(`${baseFixtureUrl}/__fixture/state`);
  return (await response.json()) as FixtureState;
}

async function replayLatestPaymentIntent(_page: Page, state: FixtureState): Promise<void> {
  const paymentRequests = state.requestLog.filter(
    (request) => request.method === "POST" && request.path === "/v1/webpay/payment-intents"
  );
  expect(paymentRequests).toHaveLength(1);
  expect(paymentRequests[0].headers["idempotency-key"]).toBeUndefined();
  expect(paymentRequests[0].headers["x-correlation-id"]).toBeTruthy();
  expect((paymentRequests[0].body as Record<string, unknown>).correlationId).toBe(
    paymentRequests[0].headers["x-correlation-id"]
  );

  const response = await fetch(`${baseFixtureUrl}/__fixture/replay-latest-payment-intent`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: "{}"
  });
  const result = (await response.json()) as {
    ok: boolean;
    route: string;
    reusedRecordedBody: boolean;
    reusedIdempotencyKey: boolean;
    originalIdempotencyKeyPresent: boolean;
    replayIdempotencyKeyPresent: boolean;
    idempotencyKeyValuesMatch: boolean;
    idempotencyKeyDisposition: string;
    idempotencyKeyPreservationPassed: boolean;
    reusedCorrelationId: boolean;
    reusedCanonicalRequestIdentity: boolean;
    statusCode: number;
  };

  expect(response.status).toBe(200);
  expect(result).toMatchObject({
    ok: true,
    route: "/v1/webpay/payment-intents",
    reusedRecordedBody: true,
    reusedIdempotencyKey: false,
    originalIdempotencyKeyPresent: false,
    replayIdempotencyKeyPresent: false,
    idempotencyKeyValuesMatch: true,
    idempotencyKeyDisposition: "ABSENT_IN_RECORDED_BROWSER_REQUEST",
    idempotencyKeyPreservationPassed: true,
    reusedCorrelationId: true,
    reusedCanonicalRequestIdentity: true,
    statusCode: 200
  });
}

async function expectNoDuplicatePaymentOrFiscalSubmission() {
  const state = await getFixtureState();
  const mutatingPaymentOrFiscalRequests = state.requestLog.filter(
    (request) =>
      request.path.includes("/payment-intents") ||
      request.path.toLowerCase().includes("fiscal-issuance") ||
      request.path.toLowerCase().includes("fiscal-documents")
  );
  expect(mutatingPaymentOrFiscalRequests).toHaveLength(0);
}

async function expectNoUnsafeStatutoryRequestFields() {
  const state = await getFixtureState();
  const statutoryPost = state.requestLog.find((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions");
  expect(statutoryPost).toBeTruthy();
  const serializedBody = JSON.stringify(statutorialPostSafeBody(statutoryPost?.body));
  expect(serializedBody).not.toContain("sourceChannel");
  expect(serializedBody).not.toContain("reviewerUserId");
  expect(serializedBody).not.toContain("reviewerAttestation");
  expect(serializedBody).not.toContain("operatorDeviceBindingId");
  expect(serializedBody).not.toContain("operatorShiftId");
  expect(serializedBody).not.toContain("statutoryDiscountAmountMinorUnits");
  expect(serializedBody).not.toContain("vatAmountMinorUnits");
  expect(serializedBody).not.toContain("finalPayableAmountMinorUnits");
  expect(serializedBody).not.toContain("appliedTariffSnapshotId");
  expect(serializedBody).not.toContain("123456789012");
}

function statutorialPostSafeBody(body: unknown): unknown {
  return body ?? {};
}

async function expectBrowserStorageSafe(page: Page) {
  const storage = await page.evaluate(async () => {
    const indexedDbNames =
      "databases" in indexedDB
        ? await indexedDB.databases().then((databases) => databases.map((database) => database.name ?? ""))
        : [];

    return {
      localStorage: { ...localStorage },
      sessionStorage: { ...sessionStorage },
      indexedDbNames
    };
  });

  const serialized = JSON.stringify(storage);
  expect(serialized).not.toContain("SIA-00000002-A");
  expect(serialized).not.toContain("POS SERVER AUTHORITATIVE PRESENTATION");
  expect(serialized).not.toContain("WEBPAY-ONLY-PAYMENT-MARKER");
  expect(serialized).not.toContain("API key");
  expect(serialized).not.toContain("AppSecret");
}
