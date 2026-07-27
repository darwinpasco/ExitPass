import { expect, test, type Page, type Request } from "@playwright/test";

const baseFixtureUrl = `http://127.0.0.1:${process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5196}`;
const ids = {
  available: "10000000-0000-4000-8000-000000000001",
  pending: "10000000-0000-4000-8000-000000000002",
  temporarilyUnavailable: "10000000-0000-4000-8000-000000000003",
  terminalFailure: "10000000-0000-4000-8000-000000000004",
  refreshPending: "10000000-0000-4000-8000-000000000005"
};

type FixtureState = {
  requestLog: Array<{ method: string; path: string; headers: Record<string, string | undefined>; body: unknown }>;
  receiptAttempts: Record<string, number>;
  statutoryReadAttempts: Record<string, number>;
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
  expect(serialized).not.toContain("SI-WEBPAY-BROWSER-SMOKE-0001");
  expect(serialized).not.toContain("Authorization");
  expect(serialized).not.toContain("AppSecret");
  expect(serialized).not.toContain("api key");
  expect(serialized).not.toContain("SC-****-1234");
  expect(serialized).not.toContain("PWD-****-5678");
});

test.describe("WebPay authoritative Sales Invoice browser smoke", () => {
  test("confirmed payment displays the authoritative POS Server presentation and no local receipt fallback", async ({ page, browser }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, "WEBPAY-ONLY-PAYMENT-MARKER", ids.available);

    await expect(page.getByRole("heading", { name: /payment confirmed/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: /^sales invoice$/i })).toBeVisible();
    await expect(page.getByText("Payment confirmation")).toBeVisible();
    await expect(page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001").first()).toBeVisible();
    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toBeVisible();
    await expect(page.getByText("CASHLESS")).toBeVisible();
    await expect(page.getByText("CENTRAL PMS EXIT AUTHORIZATION MARKER")).toBeVisible();
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);

    const invoiceText = await page.locator(".sales-invoice-panel").innerText();
    expect(invoiceText).not.toContain("WEBPAY-ONLY-PAYMENT-MARKER");
    expect(invoiceText).not.toContain("PHP 129.00");
    expect(await browser.version()).toBeTruthy();
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("fiscal issuance pending stays pending without fabricating a receipt", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, "WEBPAY-SMOKE-PENDING", ids.pending);

    await expect(page.getByRole("heading", { name: /payment confirmed/i })).toBeVisible();
    await expect(page.getByText("Your payment is recorded. The Sales Invoice is still being prepared.")).toBeVisible();
    await expect(page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001")).toHaveCount(0);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
    await expect.poll(async () => (await getFixtureState()).receiptAttempts[ids.pending] ?? 0).toBe(3);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("temporary presentation unavailability retries within the bounded read loop and later displays the authoritative presentation", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, "WEBPAY-SMOKE-TEMPORARY-UNAVAILABLE", ids.temporarilyUnavailable);

    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toBeVisible();
    await expect(page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001").first()).toBeVisible();
    await expect.poll(async () => (await getFixtureState()).receiptAttempts[ids.temporarilyUnavailable] ?? 0).toBe(3);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("terminal fiscal failure shows a safe customer-facing failure and no fallback receipt", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, "WEBPAY-SMOKE-TERMINAL-FAILURE", ids.terminalFailure);

    await expect(page.getByText("Sales Invoice unavailable")).toBeVisible();
    await expect(page.getByText("Sales Invoice issuance failed. Please contact support.")).toBeVisible();
    await expect(page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001")).toHaveCount(0);
    await expect(page.getByText(/stack trace|sql|connection string|internal exception/i)).toHaveCount(0);
    await expect(page.getByRole("heading", { name: /payment receipt/i })).toHaveCount(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("browser refresh while pending resumes readback from the durable payment attempt reference", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, "WEBPAY-SMOKE-REFRESH-PENDING", ids.refreshPending);
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

    await openReturnPage(page, "WEBPAY-SMOKE-LATER-RETRIEVAL", ids.temporarilyUnavailable);
    await expect(page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001").first()).toBeVisible();

    await page.reload();

    await expect(page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001").first()).toBeVisible();
    await expect(page.getByText("POS SERVER AUTHORITATIVE PRESENTATION")).toBeVisible();
    const fiscalNumberOccurrences = await page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001").count();
    expect(fiscalNumberOccurrences).toBeGreaterThan(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("presentation viewing uses returned content and does not expose a competing WebPay fiscal document", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, "WEBPAY-SMOKE-VIEW-PRESENTATION", ids.available);

    await expect(page.locator(".sales-invoice-panel")).toContainText("POS SERVER AUTHORITATIVE PRESENTATION");
    await expect(page.locator(".sales-invoice-panel")).toContainText("CASHLESS");
    await expect(page.getByRole("button", { name: /print/i })).toHaveCount(0);
    await expect(page.getByText(/WebPay issued|WebPay Sales Invoice|generated by WebPay/i)).toHaveCount(0);
    await expectNoDuplicatePaymentOrFiscalSubmission();
    await expectApiBoundary(apiRequests);
  });

  test("expired transaction reference is safe and does not create a fallback receipt", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await openReturnPage(page, "WEBPAY-SMOKE-EXPIRED", ids.available);

    await expect(page.getByRole("alert")).toContainText("could not find an active parking session");
    await expect(page.getByText("Sales Invoice Number")).toHaveCount(0);
    await expect(page.getByText("SI-WEBPAY-BROWSER-SMOKE-0001")).toHaveCount(0);
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
    expect(state.requestLog.some((request) => request.path.includes("/statutory-discounts"))).toBe(false);
    await expectApiBoundary(apiRequests);
  });

  test("Senior Citizen request enters pending review, disables payment, and polls with GET", async ({ page }) => {
    const apiRequests = collectApiRequests(page);

    await submitStatutoryRequest(page, "WEBPAY-STAT-PENDING-SC", "Senior Citizen", "SC-****-1234");

    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    await expect(page.getByText(/requires Operator Console review/i).first()).toBeVisible();

    const state = await getFixtureState();
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(1);
    expect(state.requestLog.filter((request) => request.method === "GET" && request.path.includes("/statutory-discounts/decisions/")).length).toBeGreaterThan(0);
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
    await expectNoUnsafeStatutoryRequestFields();
    await expectApiBoundary(apiRequests);
  });

  test("PWD request enters pending review without raw identity or reviewer fields", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-PENDING-PWD", "PWD", "PWD-****-5678");

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
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();

    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });

  test("rejection, retryable, and terminal states remain safe and do not create payment", async ({ page }) => {
    await submitStatutoryRequest(page, "WEBPAY-STAT-REJECTED", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /entitlement not approved/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    await expect(page.getByText(/reviewer|stack trace|internal exception/i)).toHaveCount(0);

    await fetch(`${baseFixtureUrl}/__fixture/reset`, { method: "POST", body: "{}" });
    await submitStatutoryRequest(page, "WEBPAY-STAT-RETRYABLE", "Senior Citizen", "SC-****-1234");
    await expect(page.getByRole("heading", { name: /discount application processing|status temporarily unavailable/i })).toBeVisible();
    await expect(page.getByText(/still being applied|Refresh status shortly/i).first()).toBeVisible();

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

    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/apply-payable-basis"))).toBe(false);
    expect(state.requestLog.some((request) => request.path.includes("/payment-intents"))).toBe(false);
  });

  test("unsafe full ID is blocked before statutory API call", async ({ page }) => {
    await resolveTicketOnStartPage(page, "WEBPAY-STAT-UNSAFE-ID");
    await page.getByRole("button", { name: /request statutory discount/i }).click();
    await page.getByLabel(/ID document type/i).fill("OSCA");
    await page.getByLabel(/Issuing authority/i).fill("Quezon City");
    await page.getByLabel(/Masked ID reference/i).fill("123456789012");
    await page.getByLabel(/I confirm these entitlement details/i).check();
    await page.getByRole("button", { name: /submit for review/i }).click();

    await expect(page.getByRole("alert")).toContainText(/masked ID reference/i);
    const state = await getFixtureState();
    expect(state.requestLog.some((request) => request.path.includes("/statutory-discounts/decisions"))).toBe(false);
  });
});

async function openReturnPage(page: Page, ticketReference: string, paymentAttemptId: string) {
  const query = new URLSearchParams({
    ticketReference,
    paymentAttemptId,
    correlationId: "77777777-7777-4777-8777-777777777777",
    result: "success"
  });
  await page.goto(`/webpay/payment-return?${query.toString()}`);
}

async function resolveTicketOnStartPage(page: Page, ticketReference: string) {
  await page.goto("/");
  await page.getByLabel(/ticket reference/i).fill(ticketReference);
  await page.getByRole("button", { name: /^continue$/i }).click();
  await expect(page.getByText("Parking Session Summary")).toBeVisible();
}

async function submitStatutoryRequest(page: Page, ticketReference: string, entitlementLabel: string, maskedIdReference: string) {
  await resolveTicketOnStartPage(page, ticketReference);
  await page.getByRole("button", { name: /request statutory discount/i }).click();
  await page.getByLabel(/Entitlement type/i).selectOption({ label: entitlementLabel });
  await page.getByLabel(/ID document type/i).fill(entitlementLabel === "PWD" ? "PWD ID" : "OSCA");
  await page.getByLabel(/Issuing authority/i).fill("Quezon City");
  await page.getByLabel(/Masked ID reference/i).fill(maskedIdReference);
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
    expect(headers.authorization).toBeUndefined();
    expect(headers["x-correlation-id"]).toBeTruthy();
  }
}

async function getFixtureState(): Promise<FixtureState> {
  const response = await fetch(`${baseFixtureUrl}/__fixture/state`);
  return (await response.json()) as FixtureState;
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
  expect(serialized).not.toContain("SI-WEBPAY-BROWSER-SMOKE-0001");
  expect(serialized).not.toContain("POS SERVER AUTHORITATIVE PRESENTATION");
  expect(serialized).not.toContain("WEBPAY-ONLY-PAYMENT-MARKER");
  expect(serialized).not.toContain("API key");
  expect(serialized).not.toContain("AppSecret");
}
