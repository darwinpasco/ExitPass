import { expect, test, type Page, type Request } from "@playwright/test";

const baseFixtureUrl = `http://127.0.0.1:${process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5196}`;
const ticketReference = "WEBPAY-EVIDENCE-G006";

type EvidenceFixtureState = {
  requestLog: Array<{ method: string; path: string; headers: Record<string, string | undefined>; body: unknown }>;
  evidence: {
    scenario: string;
    lifecycleClassification: string;
    bootstrapCount: number;
    statusCount: number;
    uploadSessionCount: number;
    uploadCount: number;
    finalizeCount: number;
    uploadedByteCount: number;
    lastDeclaredContentType: string | null;
    lastDeclaredContentLength: number | null;
  };
};

test.beforeEach(async () => {
  await fetch(`${baseFixtureUrl}/__fixture/reset`, { method: "POST", body: "{}" });
});

test.describe("WebPay statutory evidence I-016 browser consumer", () => {
  test("manual harness rejects missing and mismatched deterministic vendor configuration", async ({ request }) => {
    const missing = await request.post(`${baseFixtureUrl}/v1/webpay/parking-session`, {
      data: { ticketReference, correlationId: "g006-missing-vendor" }
    });
    expect(missing.status()).toBe(400);
    expect((await missing.json()).errorCode).toBe("WEBPAY_FIXTURE_VENDOR_CONFIGURATION_MISSING");

    const mismatched = await request.post(`${baseFixtureUrl}/v1/webpay/parking-session`, {
      data: {
        ticketReference,
        vendorSystemId: "60000000-0000-4000-8000-000000000099",
        correlationId: "g006-mismatched-vendor"
      }
    });
    expect(mismatched.status()).toBe(409);
    expect((await mismatched.json()).errorCode).toBe("WEBPAY_FIXTURE_VENDOR_CONFIGURATION_MISMATCH");
  });

  test("manual harness deterministic configuration resolves the synthetic ticket and reaches evidence capture", async ({ page }) => {
    await setEvidenceScenario("validation-pending");
    await submitStatutoryRequest(page);

    await expect(page.getByText(/choose a clear JPEG or PNG photo/i).first()).toBeVisible();
    await expect(page.getByLabel(/choose or take a clear photo/i)).toBeVisible();
    await expect(page.getByText(/missing vendor configuration/i)).toHaveCount(0);

    const state = await getFixtureState();
    const parkingRequest = state.requestLog.find(
      (entry) => entry.method === "POST" && entry.path === "/v1/webpay/parking-session"
    );
    expect(parkingRequest?.body).toMatchObject({
      ticketReference,
      vendorSystemId: "60000000-0000-4000-8000-000000000001"
    });
    expect(state.evidence.bootstrapCount).toBe(1);
  });

  test("evidence not required remains authoritative and exposes no capture control", async ({ page }) => {
    await setEvidenceScenario("not-required");
    const requests = collectApiRequests(page);

    await submitStatutoryRequest(page);

    await expect(page.getByText("No evidence photo is required for this request.")).toBeVisible();
    await expect(page.getByLabel(/choose or take a clear photo/i)).toHaveCount(0);
    await expect(page.getByRole("button", { name: /pay regular amount/i })).toBeVisible();
    await expectSafeBrowserBoundary(requests);
  });

  for (const file of [
    { type: "image/jpeg", name: "synthetic-g006.jpg" },
    { type: "image/png", name: "synthetic-g006.png" }
  ]) {
    test(`${file.type} uploads through the opaque route and finalizes separately`, async ({ page }) => {
      await setEvidenceScenario("validation-pending");
      const requests = collectApiRequests(page);
      await submitStatutoryRequest(page);
      await uploadFile(page, file.type, file.name, Buffer.from([1, 2, 3, 4, 5]));

      await expect(page.getByText("Verification pending").first()).toBeVisible();
      await expect(page.getByText(/photo verification is pending/i)).toBeVisible();
      await expect(page.getByText(/^Approved$/i)).toHaveCount(0);
      const state = await getFixtureState();
      expect(state.evidence).toMatchObject({
        uploadSessionCount: 1,
        uploadCount: 1,
        finalizeCount: 1,
        uploadedByteCount: 5,
        lastDeclaredContentType: file.type,
        lastDeclaredContentLength: 5,
        lifecycleClassification: "VALIDATION_PENDING"
      });
      expect(state.requestLog.filter((entry) => entry.path.includes("/evidence/upload-sessions"))).toHaveLength(3);
      await expectEvidenceStorageSafe(page);
      await expectSafeBrowserBoundary(requests);
    });
  }

  test("PDF and oversized files are rejected before authorization", async ({ page }) => {
    await setEvidenceScenario("validation-pending");
    await submitStatutoryRequest(page);
    const input = page.getByLabel(/choose or take a clear photo/i);

    await input.setInputFiles({ name: "proof.pdf", mimeType: "application/pdf", buffer: Buffer.from("pdf") });
    await expect(page.getByRole("alert")).toContainText(/JPEG or PNG/i);
    await input.setInputFiles({ name: "large.jpg", mimeType: "image/jpeg", buffer: Buffer.alloc(1_048_577, 1) });
    await expect(page.getByRole("alert")).toContainText(/too large/i);

    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(0);
    expect(state.evidence.uploadCount).toBe(0);
    expect(state.evidence.finalizeCount).toBe(0);
  });

  test("provider interruption is safe and never finalizes locally", async ({ page }) => {
    await setEvidenceScenario("provider-unavailable");
    await submitStatutoryRequest(page);
    await uploadFile(page, "image/jpeg", "interrupted.jpg", Buffer.from([1, 2, 3]));

    await expect(page.getByRole("alert")).toContainText(/could not process the photo|interrupted/i);
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(1);
    expect(state.evidence.uploadCount).toBe(1);
    expect(state.evidence.finalizeCount).toBe(0);
  });

  test("expired authorization is rejected safely and is never finalized", async ({ page }) => {
    await setEvidenceScenario("expired-session");
    await submitStatutoryRequest(page);
    await uploadFile(page, "image/jpeg", "expired.jpg", Buffer.from([1, 2, 3]));

    await expect(page.getByRole("alert")).toContainText(/upload expired|request a new upload/i);
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(1);
    expect(state.evidence.uploadCount).toBe(1);
    expect(state.evidence.finalizeCount).toBe(0);
  });

  test("cancellation reconciles server state and never finalizes the interrupted upload", async ({ page }) => {
    await setEvidenceScenario("upload-delayed");
    await submitStatutoryRequest(page);
    await page.getByLabel(/choose or take a clear photo/i).setInputFiles({
      name: "cancelled.jpg",
      mimeType: "image/jpeg",
      buffer: Buffer.from([1, 2, 3])
    });
    await page.getByRole("button", { name: /upload photo/i }).click();
    await page.getByRole("button", { name: /cancel upload/i }).click();

    await expect(page.getByRole("alert")).toContainText(/upload was cancelled/i);
    await expect(page.getByText("Upload incomplete")).toBeVisible();
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(1);
    expect(state.evidence.uploadCount).toBe(1);
    expect(state.evidence.finalizeCount).toBe(0);
    expect(state.evidence.statusCount).toBeGreaterThan(0);
  });

  for (const scenario of ["service-unavailable", "access-denied", "malformed-response"] as const) {
    test(`${scenario} fails closed without fabricating evidence-not-required`, async ({ page }) => {
      await setEvidenceScenario(scenario);
      await submitStatutoryRequest(page);

      await expect(page.getByRole("alert")).toContainText(/temporarily unavailable|refresh and try again/i);
      await expect(page.getByText(/No evidence photo is required/i)).toHaveCount(0);
      await expect(page.getByLabel(/choose or take a clear photo/i)).toHaveCount(0);
      const state = await getFixtureState();
      expect(state.evidence.uploadSessionCount).toBe(0);
      expect(state.evidence.finalizeCount).toBe(0);
    });
  }

  test("refresh rediscovers finalized validation-pending evidence without browser authority", async ({ page }) => {
    await setEvidenceScenario("validation-pending");
    await submitStatutoryRequest(page);
    await uploadFile(page, "image/png", "restart.png", Buffer.from([1, 2, 3]));
    await expect(page.getByText("Verification pending").first()).toBeVisible();
    const before = await getFixtureState();

    await page.reload();

    await page.getByLabel(/ticket reference/i).fill(ticketReference);
    await page.getByRole("button", { name: /^continue$/i }).click();
    await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
    await expect(page.getByText("Verification pending").first()).toBeVisible();
    const after = await getFixtureState();
    expect(after.evidence.uploadSessionCount).toBe(before.evidence.uploadSessionCount);
    expect(after.evidence.uploadCount).toBe(before.evidence.uploadCount);
    expect(after.evidence.finalizeCount).toBe(before.evidence.finalizeCount);
    expect(after.evidence.bootstrapCount).toBeGreaterThan(before.evidence.bootstrapCount);
    await expectEvidenceStorageSafe(page);
  });

  for (const [scenario, label, message] of [
    ["reviewable", "Ready for review", /does not mean.*approved/i],
    ["malware", "Unsafe file detected", /cannot be used/i],
    ["validation-failed", "Photo not accepted", /could not be verified/i],
    ["scan-pending", "Verification pending", /still being checked/i],
    ["scan-retryable", "Processing delayed", /temporarily delayed/i],
    ["review-pending", "Awaiting review", /photo was received.*awaiting review/i],
    ["approved", "Approved", /payment-time flow/i],
    ["rejected", "Not approved", /regular parking payment/i],
    ["applied", "Applied", /authoritative payable basis/i]
  ] as const) {
    test(`${scenario} lifecycle remains distinct from approval`, async ({ page }) => {
      await setEvidenceScenario(scenario);
      await submitStatutoryRequest(page);
      await uploadFile(page, "image/jpeg", `${scenario}.jpg`, Buffer.from([1, 2, 3]));

      await expect(page.getByText(label).first()).toBeVisible();
      await expect(page.getByText(message)).toBeVisible();
      await expect(page.getByText(/^Statutory discount applied$/i)).toHaveCount(0);
    });
  }

  test("replacement lock disables capture and preserves regular payment", async ({ page }) => {
    await setEvidenceScenario("replacement-denied");
    await submitStatutoryRequest(page);

    await expect(page.getByText(/cannot be replaced/i)).toBeVisible();
    await expect(page.getByLabel(/choose or take a clear photo/i)).toHaveCount(0);
    await expect(page.getByRole("button", { name: /pay regular amount/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(0);
  });

  test("narrow layout and keyboard controls remain usable without hidden authority", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await setEvidenceScenario("validation-pending");
    await submitStatutoryRequest(page);

    const input = page.getByLabel(/choose or take a clear photo/i);
    await input.setInputFiles({ name: "keyboard.jpg", mimeType: "image/jpeg", buffer: Buffer.from([1, 2, 3]) });
    await input.focus();
    await expect(input).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: /upload photo/i })).toBeFocused();
    await expect(page.locator(".statutory-evidence")).toBeVisible();
    await expectEvidenceStorageSafe(page);
  });
});

async function setEvidenceScenario(scenario: string) {
  const response = await fetch(`${baseFixtureUrl}/__fixture/evidence-scenario`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ scenario })
  });
  expect(response.ok).toBe(true);
}

async function submitStatutoryRequest(page: Page) {
  await page.goto("/");
  await page.getByLabel(/ticket reference/i).fill(ticketReference);
  await page.getByRole("button", { name: /^continue$/i }).click();
  await expect(page.getByText("Parking Session Summary")).toBeVisible();
  await page.getByRole("button", { name: /request statutory discount/i }).click();
  await page.getByLabel(/ID document type/i).fill("OSCA");
  await page.getByLabel(/Issuing authority/i).fill("Synthetic City");
  await page.getByLabel(/Masked ID reference/i).fill("SC-****-0001");
  await page.getByLabel(/I confirm these entitlement details/i).check();
  await page.getByRole("button", { name: /submit for review/i }).click();
  await expect(page.getByRole("heading", { name: /awaiting review/i })).toBeVisible();
}

async function uploadFile(page: Page, mimeType: string, name: string, buffer: Buffer) {
  await page.getByLabel(/choose or take a clear photo/i).setInputFiles({ name, mimeType, buffer });
  await page.getByRole("button", { name: /upload photo|upload replacement photo/i }).click();
}

function collectApiRequests(page: Page): Request[] {
  const requests: Request[] = [];
  page.on("request", (request) => {
    if (new URL(request.url()).pathname.startsWith("/v1/")) {
      requests.push(request);
    }
  });
  return requests;
}

async function expectSafeBrowserBoundary(requests: Request[]) {
  expect(requests.length).toBeGreaterThan(0);
  for (const request of requests) {
    const url = new URL(request.url());
    expect(url.origin).toBe(baseFixtureUrl);
    expect(url.pathname).toMatch(/^\/v1\/webpay\//);
    const headers = await request.allHeaders();
    expect(headers["x-exitpass-service-identity-id"]).toBeUndefined();
    expect(headers["x-exitpass-permissions"]).toBeUndefined();
    expect(headers.authorization).toBeUndefined();
    expect(JSON.stringify(headers)).not.toMatch(/bucket|object.?key|storage.?endpoint|scanner/i);
  }
}

async function expectEvidenceStorageSafe(page: Page) {
  const storage = await page.evaluate(async () => ({
    localStorage: { ...localStorage },
    sessionStorage: { ...sessionStorage },
    indexedDbNames: "databases" in indexedDB ? (await indexedDB.databases()).map((entry) => entry.name ?? "") : [],
    cacheNames: "caches" in globalThis ? await caches.keys() : [],
    cookies: document.cookie
  }));
  const serialized = JSON.stringify(storage);
  expect(serialized).not.toMatch(/synthetic-g006|restart\.png|proof\.pdf|opaque|upload.?session|evidenceSetReference|evidenceItemReference|checksum|base64/i);
}

async function getFixtureState(): Promise<EvidenceFixtureState> {
  const response = await fetch(`${baseFixtureUrl}/__fixture/state`);
  return response.json() as Promise<EvidenceFixtureState>;
}
