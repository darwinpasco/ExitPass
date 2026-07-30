import { expect, test, type Page, type Request } from "@playwright/test";

const validationPort = Number(process.env.WEBPAY_ORDINANCE_VALIDATION_PORT ?? process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5206);
const validationNonce = process.env.WEBPAY_ORDINANCE_VALIDATION_NONCE ?? "";
const validationBaseUrl = `http://127.0.0.1:${validationPort}`;
const ticketReference = "WEBPAY-ORD-G004-001";
const expectedAmount = "PHP 137.50";

type ValidationRequest = {
  method: string;
  path: string;
  statusCode: number;
  classification: string | null;
  body: Record<string, unknown>;
  headers: Record<string, string | undefined>;
};

type ValidationState = {
  scenario: string;
  requestLog: ValidationRequest[];
  decisionCreatedCount: number;
  continuationCreatedCount: number;
  applicationCreatedCount: number;
  paymentIntentCount: number;
};

test.beforeAll(() => {
  expect(validationNonce, "validation nonce must be provided by the harness runner").toHaveLength(48);
});

test.beforeEach(async ({ page }) => {
  await setScenario("bothCovered");
});

test.describe("WebPay statutory ordinance eligibility validation harness controls", () => {
  test("validation control requires the process nonce", async () => {
    const missing = await fetch(`${validationBaseUrl}/__validation/scenario`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: "bothCovered" })
    });
    expect(missing.status).toBe(403);

    const invalid = await fetch(`${validationBaseUrl}/__validation/scenario`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Validation-Nonce": "invalid" },
      body: JSON.stringify({ name: "bothCovered" })
    });
    expect(invalid.status).toBe(403);
  });
});

test.describe("WebPay statutory ordinance eligibility browser validation", () => {
  test("both covered shows both entitlements and preserves ordinary payment", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("bothCovered");
    await resolveTicket(page);

    await expect(page.getByText("Parking privilege request available")).toBeVisible();
    await expect(page.getByRole("button", { name: /request statutory discount/i })).toBeVisible();
    await page.getByRole("button", { name: /request statutory discount/i }).click();
    await expectEntitlementOptions(page, ["Senior Citizen", "PWD"]);
    await expectOrdinaryPaymentAvailable(page);

    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  test("Senior Citizen only hides PWD and crafted PWD submission is rejected server-side", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("seniorOnly");
    await resolveTicket(page);

    await page.getByRole("button", { name: /request statutory discount/i }).click();
    await expectEntitlementOptions(page, ["Senior Citizen"]);
    await expect(page.getByLabel(/entitlement type/i)).not.toHaveText(/PWD/);
    await expectOrdinaryPaymentAvailable(page);

    const crafted = await postCraftedDecision("PWD");
    expect(crafted.status).toBe(409);
    expect((await crafted.json()).errorCode).toBe("WEBPAY_STATUTORY_PRIVILEGE_NOT_AVAILABLE");
    const state = await getValidationState();
    expect(state.decisionCreatedCount).toBe(0);
    expect(state.applicationCreatedCount).toBe(0);

    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  test("PWD only hides Senior Citizen and crafted Senior Citizen submission is rejected server-side", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("pwdOnly");
    await resolveTicket(page);

    await page.getByRole("button", { name: /request statutory discount/i }).click();
    await expectEntitlementOptions(page, ["PWD"]);
    await expect(page.getByLabel(/entitlement type/i)).not.toHaveText(/Senior Citizen/);
    await expectOrdinaryPaymentAvailable(page);

    const crafted = await postCraftedDecision("SENIOR_CITIZEN");
    expect(crafted.status).toBe(409);
    expect((await crafted.json()).errorCode).toBe("WEBPAY_STATUTORY_PRIVILEGE_NOT_AVAILABLE");
    const state = await getValidationState();
    expect(state.decisionCreatedCount).toBe(0);
    expect(state.applicationCreatedCount).toBe(0);

    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  for (const scenario of [
    ["noCoverage", "NO_APPLICABLE_LOCAL_ORDINANCE"],
    ["futureEffective", "POLICY_NOT_YET_EFFECTIVE"],
    ["expired", "POLICY_EXPIRED"],
    ["inactive", "POLICY_SUSPENDED"],
    ["incomplete", "REQUIRED_POLICY_FACTS_INCOMPLETE"]
  ] as const) {
    test(`${scenario[0]} hides statutory controls and keeps ordinary payment`, async ({ page }) => {
      const apiRequests = collectApiRequests(page);
      await setScenario(scenario[0]);
      await resolveTicket(page);

      await expect(page.getByText("Parking privilege request not available")).toBeVisible();
      await expect(page.getByRole("button", { name: /request statutory discount/i })).toHaveCount(0);
      await expectNoStatutoryFormFields(page);
      await expectOrdinaryPaymentAvailable(page);

      const state = await getValidationState();
      expect(state.requestLog.some((request) => request.classification === scenario[1])).toBe(true);
      expect(state.decisionCreatedCount).toBe(0);
      expect(state.continuationCreatedCount).toBe(0);

      await expectApiBoundary(apiRequests);
      await expectAvailabilityNotPersisted(page);
    });
  }

  for (const scenario of [
    "unavailable",
    "timeout",
    "authorizationFailure",
    "malformed",
    "unknown",
    "unsupportedVersion"
  ] as const) {
    test(`${scenario} shows temporary guidance, retry, and ordinary payment`, async ({ page }) => {
      const apiRequests = collectApiRequests(page);
      await setScenario(scenario);
      await resolveTicket(page);

      await expect(page.getByText("Parking privilege availability unavailable")).toBeVisible();
      await expect(page.getByText(/regular parking amount|try again shortly|temporarily unavailable/i)).toBeVisible();
      await expect(page.getByRole("button", { name: /request statutory discount/i })).toHaveCount(0);
      await expectNoStatutoryFormFields(page);
      await expectOrdinaryPaymentAvailable(page);

      const state = await getValidationState();
      expect(state.decisionCreatedCount).toBe(0);
      expect(state.continuationCreatedCount).toBe(0);

      await expectApiBoundary(apiRequests);
      await expectAvailabilityNotPersisted(page);
    });
  }

  test("coverage removed between display and submission is rejected before decision creation", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("displayThenNoCoverage");
    await resolveTicket(page);
    await submitVisibleStatutoryRequest(page, "Senior Citizen", "OSCA", "SC-****-0001");

    await expect(page.getByText(/Parking privilege requests are not available/i)).toBeVisible();
    const state = await getValidationState();
    expect(state.requestLog.filter((request) => request.path === "/v1/webpay/statutory-discounts/availability")).toHaveLength(1);
    expect(state.requestLog.filter((request) => request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(1);
    expect(state.decisionCreatedCount).toBe(0);
    expect(state.applicationCreatedCount).toBe(0);
    await expectOrdinaryPaymentAvailable(page);

    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  test("selected entitlement removed before submission is rejected before decision creation", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("selectedRemovedBeforeSubmit");
    await resolveTicket(page);
    await submitVisibleStatutoryRequest(page, "PWD", "PWD ID", "PWD-****-0002");

    await expect(page.getByText(/Parking privilege requests are not available/i)).toBeVisible();
    const state = await getValidationState();
    expect(state.decisionCreatedCount).toBe(0);
    expect(state.applicationCreatedCount).toBe(0);
    await expectOrdinaryPaymentAvailable(page);

    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  test("mismatched Site, Site Group, and stale parking-session references are rejected", async ({ page }) => {
    await setScenario("bothCovered");
    await resolveTicket(page);

    const siteMismatch = await postCraftedDecision("SENIOR_CITIZEN", { siteId: "51000000-0000-4000-8000-000000000099" });
    const siteGroupMismatch = await postCraftedDecision("SENIOR_CITIZEN", { siteGroupId: "41000000-0000-4000-8000-000000000099" });
    const staleSession = await postCraftedDecision("SENIOR_CITIZEN", { parkingSessionId: "21000000-0000-4000-8000-000000000099" });

    expect(siteMismatch.status).toBe(409);
    expect(siteGroupMismatch.status).toBe(409);
    expect(staleSession.status).toBe(409);

    const state = await getValidationState();
    expect(state.decisionCreatedCount).toBe(0);
    expect(state.applicationCreatedCount).toBe(0);
    await expectOrdinaryPaymentAvailable(page);
    await expectAvailabilityNotPersisted(page);
  });

  test("pending-review submission creates durable recovery metadata", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("pendingReview");
    await resolveTicket(page);
    await submitVisibleStatutoryRequest(page, "Senior Citizen", "OSCA", "SC-****-0003");

    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await expectSavedDecisionRecovery(page);
    await expect(page.getByText(expectedAmount).first()).toBeVisible();

    const state = await getValidationState();
    expect(state.decisionCreatedCount).toBe(1);
    expect(state.continuationCreatedCount).toBe(1);
    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  test("pending-review browser restart recovery uses GET readback without repeat submission", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("pendingReview");
    await page.addInitScript(() => {
      const now = new Date();
      localStorage.setItem("exitpass:webpay:statutory-discount-recovery:v1", JSON.stringify({
        schemaVersion: 1,
        parkingSessionId: "21000000-0000-4000-8000-000000000001",
        entitlementType: "SENIOR_CITIZEN",
        statutoryDiscountDecisionCommandId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        decisionIdempotencyKey: "webpay-statutory-discount-decision:g004:SENIOR_CITIZEN:original",
        requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        correlationId: "33000000-0000-4000-8000-000000000001",
        stage: "DECISION_PENDING",
        createdAt: now.toISOString(),
        updatedAt: now.toISOString(),
        expiresAt: new Date(now.getTime() + 60 * 60 * 1000).toISOString()
      }));
    });

    await page.goto("/");
    await expect(page.getByText(/Existing statutory discount request restored/i)).toBeVisible();

    const state = await getValidationState();
    expect(state.requestLog.some((request) => request.method === "GET" && request.path.includes("/v1/webpay/statutory-discounts/decisions/"))).toBe(true);
    expect(state.requestLog.filter((request) => request.method === "POST" && request.path === "/v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
    expect(state.decisionCreatedCount).toBe(0);
    await expectApiBoundary(apiRequests);
  });

  test("rejected statutory request remains read-only and ordinary payment remains available", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("rejected");
    await resolveTicket(page);
    await submitVisibleStatutoryRequest(page, "Senior Citizen", "OSCA", "SC-****-0004");

    await expect(page.getByRole("heading", { name: /entitlement not approved/i })).toBeVisible();
    await expect(page.getByText(expectedAmount).first()).toBeVisible();
    const state = await getValidationState();
    expect(state.decisionCreatedCount).toBe(1);
    expect(state.applicationCreatedCount).toBe(0);

    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  test("ordinary payment remains available and creates one provider-neutral handoff", async ({ page }) => {
    const apiRequests = collectApiRequests(page);
    await setScenario("noCoverage");
    await resolveTicket(page);
    await page.getByRole("button", { name: /continue to payment/i }).first().click();

    await expect(page.getByRole("link", { name: /continue to payment/i })).toBeVisible();
    const state = await getValidationState();
    expect(state.paymentIntentCount).toBe(1);
    expect(state.decisionCreatedCount).toBe(0);
    expect(state.applicationCreatedCount).toBe(0);

    await expectApiBoundary(apiRequests);
    await expectAvailabilityNotPersisted(page);
  });

  test("desktop and narrow layouts keep hidden controls out of keyboard flow", async ({ page }) => {
    await setScenario("noCoverage");
    await page.setViewportSize({ width: 1366, height: 768 });
    await resolveTicket(page);
    await expectNoStatutoryFormFields(page);
    await page.keyboard.press("Tab");
    await expect(page.locator(":focus")).toBeVisible();

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.getByText(expectedAmount).first()).toBeVisible();
    await expect(page.getByRole("button", { name: /request statutory discount/i })).toHaveCount(0);
    await expectOrdinaryPaymentAvailable(page);
    await expectAvailabilityNotPersisted(page);
  });
});

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

async function setScenario(name: string) {
  const response = await fetch(`${validationBaseUrl}/__validation/scenario`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Validation-Nonce": validationNonce },
    body: JSON.stringify({ name })
  });
  expect(response.status).toBe(200);
}

async function getValidationState(): Promise<ValidationState> {
  const response = await fetch(`${validationBaseUrl}/__validation/state`, {
    headers: { "X-Validation-Nonce": validationNonce }
  });
  expect(response.status).toBe(200);
  return (await response.json()) as ValidationState;
}

async function resolveTicket(page: Page) {
  await page.goto(`/?webpayStatutoryRecoveryReset=1`);
  await page.getByLabel(/ticket reference/i).fill(ticketReference);
  await page.getByRole("button", { name: /continue/i }).click();
  await expect(page.getByText("Parking Session Summary")).toBeVisible();
  await expect(page.getByText(expectedAmount).first()).toBeVisible();
}

async function expectEntitlementOptions(page: Page, labels: string[]) {
  const entitlementSelect = page.getByLabel(/entitlement type/i);
  await expect(entitlementSelect).toBeVisible();
  for (const label of labels) {
    await expect(entitlementSelect).toHaveText(new RegExp(label));
  }
}

async function expectOrdinaryPaymentAvailable(page: Page) {
  await expect(page.getByText(expectedAmount).first()).toBeVisible();
  await expect(page.getByRole("button", { name: /continue to payment/i }).first()).toBeVisible();
}

async function expectNoStatutoryFormFields(page: Page) {
  await expect(page.getByLabel(/entitlement type/i)).toHaveCount(0);
  await expect(page.getByLabel(/ID document type/i)).toHaveCount(0);
  await expect(page.getByLabel(/Masked ID reference/i)).toHaveCount(0);
  await expect(page.getByText(/evidence|capture|upload/i)).toHaveCount(0);
}

async function submitVisibleStatutoryRequest(page: Page, entitlementLabel: string, documentType: string, maskedIdReference: string) {
  await page.getByRole("button", { name: /request statutory discount/i }).click();
  await page.getByLabel(/Entitlement type/i).selectOption({ label: entitlementLabel });
  await page.getByLabel(/ID document type/i).fill(documentType);
  await page.getByLabel(/Issuing authority/i).fill("Quezon City");
  await page.getByLabel(/Masked ID reference/i).fill(maskedIdReference);
  await page.getByLabel(/I confirm these entitlement details/i).check();
  await page.getByRole("button", { name: /submit for review/i }).click();
}

async function postCraftedDecision(entitlementType: "SENIOR_CITIZEN" | "PWD", overrides: Record<string, string> = {}) {
  return fetch(`${validationBaseUrl}/v1/webpay/statutory-discounts/decisions`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": `g004-crafted-${entitlementType}-${crypto.randomUUID()}`,
      "X-Correlation-Id": "33000000-0000-4000-8000-000000000001"
    },
    body: JSON.stringify({
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      parkingSessionId: "21000000-0000-4000-8000-000000000001",
      siteId: "51000000-0000-4000-8000-000000000001",
      siteGroupId: "41000000-0000-4000-8000-000000000001",
      ticketReference,
      plateNumber: "ORDG004",
      entitlementType,
      idDocumentType: entitlementType === "PWD" ? "PWD ID" : "OSCA",
      issuingAuthority: "Quezon City",
      maskedIdReference: entitlementType === "PWD" ? "PWD-****-9999" : "SC-****-9999",
      evidenceCaptureRequested: false,
      evidenceReferences: [],
      requesterAttestation: true,
      attestationNotes: "Synthetic validation request.",
      originalTariffSnapshotId: "31000000-0000-4000-8000-000000000001",
      ...overrides
    })
  });
}

async function expectApiBoundary(requests: Request[]) {
  expect(requests.length).toBeGreaterThan(0);
  for (const request of requests) {
    const url = new URL(request.url());
    expect(url.hostname).toBe("127.0.0.1");
    expect(url.pathname).toMatch(/^\/v1\/webpay\//);
    expect(url.href).not.toContain("central-pms");
    const headers = await request.allHeaders();
    expect(headers["x-exitpass-service-identity-id"]).toBeUndefined();
    expect(headers["x-exitpass-permissions"]).toBeUndefined();
    expect(headers.authorization).toBeUndefined();
    expect(headers["x-correlation-id"]).toBeTruthy();
  }
}

async function expectAvailabilityNotPersisted(page: Page) {
  const storage = await page.evaluate(async () => {
    const indexedDbNames =
      "databases" in indexedDB
        ? await indexedDB.databases().then((databases) => databases.map((database) => database.name ?? ""))
        : [];
    const cacheNames = "caches" in window ? await caches.keys() : [];
    return {
      localStorage: { ...localStorage },
      sessionStorage: { ...sessionStorage },
      indexedDbNames,
      cacheNames
    };
  });

  const serialized = JSON.stringify(storage);
  expect(serialized).not.toContain("coveredEntitlementTypes");
  expect(serialized).not.toContain("availabilityStatus");
  expect(serialized).not.toContain("NO_APPLICABLE_LOCAL_ORDINANCE");
  expect(serialized).not.toContain("POLICY_NOT_YET_EFFECTIVE");
  expect(serialized).not.toContain("POLICY_EXPIRED");
  expect(serialized).not.toContain("POLICY_SUSPENDED");
  expect(serialized).not.toContain("REQUIRED_POLICY_FACTS_INCOMPLETE");
}

async function expectSavedDecisionRecovery(page: Page) {
  const recovery = await page.evaluate(() => localStorage.getItem("exitpass:webpay:statutory-discount-recovery:v1"));
  expect(recovery).toBeTruthy();
  const parsed = JSON.parse(recovery ?? "{}") as Record<string, unknown>;
  expect(parsed.statutoryDiscountDecisionCommandId).toBe("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
  expect(parsed.parkingSessionId).toBe("21000000-0000-4000-8000-000000000001");
}
