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

async function openReturnPage(page: Page, ticketReference: string, paymentAttemptId: string) {
  const query = new URLSearchParams({
    ticketReference,
    paymentAttemptId,
    correlationId: "77777777-7777-4777-8777-777777777777",
    result: "success"
  });
  await page.goto(`/webpay/payment-return?${query.toString()}`);
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
