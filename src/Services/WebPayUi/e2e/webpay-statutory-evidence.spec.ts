import { devices, expect, test, type Page, type Request } from "@playwright/test";

const baseFixtureUrl = `http://127.0.0.1:${process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5196}`;
const ticketReference = "WEBPAY-EVIDENCE-G006";

test.use({
  ...devices["Pixel 7"],
  permissions: ["camera"],
  launchOptions: {
    args: ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"]
  }
});

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

    await expect(page.getByText(/photo verification is pending/i)).toBeVisible();
    await expect(page.getByRole("button", { name: /^take photo$/i })).toBeVisible();
    await expect(page.locator('input[type="file"]')).toHaveCount(0);
    await expect(page.getByText(/missing vendor configuration/i)).toHaveCount(0);

    const state = await getFixtureState();
    const parkingRequest = state.requestLog.find(
      (entry) => entry.method === "POST" && entry.path === "/v1/webpay/parking-session"
    );
    expect(parkingRequest?.body).toMatchObject({
      ticketReference,
      vendorSystemId: "60000000-0000-4000-8000-000000000001"
    });
    expect(state.evidence.bootstrapCount).toBe(2);
    await expectCustomerVisibleReferencesSafe(page);
  });

  test("a contradictory not-required result fails closed without uploading captured evidence", async ({ page }) => {
    await setEvidenceScenario("not-required");
    const requests = collectApiRequests(page);

    await prepareStatutoryRequest(page);
    await page.getByRole("button", { name: /submit for review/i }).click();

    await expect(page.getByRole("alert")).toContainText(/required photo could not be attached/i);
    await expect(page.locator('input[type="file"]')).toHaveCount(0);
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(0);
    expect(state.evidence.uploadCount).toBe(0);
    expect(state.evidence.finalizeCount).toBe(0);
    await expectSafeBrowserBoundary(requests);
  });

  test("a rear-camera JPEG uploads through the opaque route and finalizes separately", async ({ page }) => {
    await setEvidenceScenario("validation-pending");
    const requests = collectApiRequests(page);
    await submitStatutoryRequest(page);

    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(1);
    expect(state.evidence.uploadCount).toBe(1);
    expect(state.evidence.finalizeCount).toBe(1);
    expect(state.evidence.uploadedByteCount).toBeGreaterThan(0);
    expect(state.evidence.lastDeclaredContentType).toBe("image/jpeg");
    expect(state.evidence.lastDeclaredContentLength).toBe(state.evidence.uploadedByteCount);
    expect(state.evidence.lifecycleClassification).toBe("VALIDATION_PENDING");
    expect(state.requestLog.filter((entry) => entry.path.includes("/evidence/upload-sessions"))).toHaveLength(3);
    await expect(page.locator('input[type="file"]')).toHaveCount(0);
    await expectEvidenceStorageSafe(page);
    await expectSafeBrowserBoundary(requests);
    await expectCustomerVisibleReferencesSafe(page);
  });

  test("the customer evidence surface exposes no file or gallery picker", async ({ page }) => {
    await setEvidenceScenario("validation-pending");
    await prepareStatutoryRequest(page);

    await expect(page.getByText("Photo captured")).toBeVisible();
    await expect(page.locator('input[type="file"]')).toHaveCount(0);
    await expect(page.getByRole("button", { name: /choose file|open gallery|select image/i })).toHaveCount(0);
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(0);
  });

  test("provider interruption is safe and never finalizes locally", async ({ page }) => {
    await setEvidenceScenario("provider-unavailable");
    await prepareStatutoryRequest(page);
    await page.getByRole("button", { name: /submit for review/i }).click();

    await expect(page.getByRole("alert")).toContainText(/could not process the photo|interrupted/i);
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(1);
    expect(state.evidence.uploadCount).toBe(1);
    expect(state.evidence.finalizeCount).toBe(0);
  });

  test("expired authorization is rejected safely and is never finalized", async ({ page }) => {
    await setEvidenceScenario("expired-session");
    await prepareStatutoryRequest(page);
    await page.getByRole("button", { name: /submit for review/i }).click();

    await expect(page.getByRole("alert")).toContainText(/upload expired|request a new upload/i);
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(1);
    expect(state.evidence.uploadCount).toBe(1);
    expect(state.evidence.finalizeCount).toBe(0);
  });

  test("cancellation reconciles server state and never finalizes the interrupted upload", async ({ page }) => {
    await setEvidenceScenario("validation-pending");
    await submitStatutoryRequest(page);
    await setEvidenceScenario("upload-delayed");
    await capturePhonePhoto(page);
    await page.getByRole("button", { name: /upload replacement photo/i }).click();
    await page.getByRole("button", { name: /cancel upload/i }).click();

    await expect(page.getByRole("alert")).toContainText(/upload was cancelled/i);
    await expect(page.getByText("Upload incomplete")).toBeVisible();
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(2);
    expect(state.evidence.uploadCount).toBe(2);
    expect(state.evidence.finalizeCount).toBe(1);
    expect(state.evidence.statusCount).toBeGreaterThan(0);
  });

  for (const scenario of ["service-unavailable", "access-denied", "malformed-response"] as const) {
    test(`${scenario} fails closed without fabricating evidence-not-required`, async ({ page }) => {
      await setEvidenceScenario(scenario);
      await prepareStatutoryRequest(page);
      await page.getByRole("button", { name: /submit for review/i }).click();

      await expect(page.getByRole("alert")).toContainText(/temporarily unavailable|refresh and try again/i);
      await expect(page.getByText(/No evidence photo is required/i)).toHaveCount(0);
      await expect(page.locator('input[type="file"]')).toHaveCount(0);
      const state = await getFixtureState();
      expect(state.evidence.uploadSessionCount).toBe(0);
      expect(state.evidence.finalizeCount).toBe(0);
    });
  }

  test("refresh rediscovers finalized validation-pending evidence without browser authority", async ({ page }) => {
    await setEvidenceScenario("validation-pending");
    await submitStatutoryRequest(page);
    await expect(page.getByText("Verification pending").first()).toBeVisible();
    const before = await getFixtureState();

    await page.reload();

    if (await page.getByLabel(/ticket reference/i).count() === 0) {
      await page.getByRole("button", { name: /^ticket$/i }).click();
    }
    await page.getByLabel(/ticket reference/i).fill(ticketReference);
    await page.getByRole("button", { name: /^continue$/i }).click();
    await expect(page.getByRole("heading", { name: /awaiting review|evidence processing/i })).toBeVisible();
    await expect(page.getByText("Verification pending").first()).toBeVisible();
    const after = await getFixtureState();
    expect(after.evidence.uploadSessionCount).toBe(before.evidence.uploadSessionCount);
    expect(after.evidence.uploadCount).toBe(before.evidence.uploadCount);
    expect(after.evidence.finalizeCount).toBe(before.evidence.finalizeCount);
    expect(after.evidence.bootstrapCount).toBeGreaterThan(before.evidence.bootstrapCount);
    await expectEvidenceStorageSafe(page);
  });

  for (const [scenario, label, message] of [
    ["reviewable", "Photo submitted", /does not mean.*approved/i],
    ["malware", "Unsafe file detected", /cannot be used/i],
    ["validation-failed", "Photo not accepted", /could not be verified/i],
    ["scan-pending", "Verification pending", /still being checked/i],
    ["scan-retryable", "Processing delayed", /temporarily delayed/i],
    ["review-pending", "Awaiting review", /photo was received.*awaiting review/i],
    ["approved", "Approved", /applying.*automatically/i],
    ["rejected", "Not approved", /regular parking payment/i],
    ["applied", "Applied", /included in the amount due/i]
  ] as const) {
    test(`${scenario} lifecycle remains distinct from approval`, async ({ page }) => {
      await setEvidenceScenario(scenario);
      await submitStatutoryRequest(page);

      await expect(page.getByText(label).first()).toBeVisible();
      await expect(page.getByText(message)).toBeVisible();
      await expect(page.getByText(/^Statutory discount applied$/i)).toHaveCount(0);
      await expectCustomerVisibleReferencesSafe(page);
    });
  }

  test("replacement lock disables capture and preserves regular payment", async ({ page }) => {
    await setEvidenceScenario("replacement-denied");
    await submitStatutoryRequest(page);

    await expect(page.getByText(/cannot be replaced/i)).toBeVisible();
    await expect(page.getByRole("button", { name: /^take photo$/i })).toHaveCount(0);
    await expect(page.getByRole("button", { name: /pay regular amount/i })).toBeVisible();
    const state = await getFixtureState();
    expect(state.evidence.uploadSessionCount).toBe(0);
    await expectCustomerVisibleReferencesSafe(page);
  });

  test("narrow layout and keyboard controls remain usable without hidden authority", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await setEvidenceScenario("validation-pending");
    await submitStatutoryRequest(page);
    await capturePhonePhoto(page);

    const cameraButton = page.getByRole("button", { name: /^retake photo$/i });
    await cameraButton.focus();
    await expect(cameraButton).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: /upload replacement photo/i })).toBeFocused();
    await expect(page.locator(".statutory-evidence")).toBeVisible();
    await expect(page.locator('input[type="file"]')).toHaveCount(0);
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
  await prepareStatutoryRequest(page);
  await page.getByRole("button", { name: /submit for review/i }).click();
  await expect(page.getByRole("heading", { name: /awaiting review|evidence processing/i })).toBeVisible();
}

async function prepareStatutoryRequest(page: Page) {
  await page.goto("/");
  if (await page.getByLabel(/ticket reference/i).count() === 0) {
    await page.getByRole("button", { name: /^ticket$/i }).click();
  }
  await page.getByLabel(/ticket reference/i).fill(ticketReference);
  await page.getByRole("button", { name: /^continue$/i }).click();
  await expect(page.getByText("Parking Session Summary")).toBeVisible();
  await page.getByRole("button", { name: /request statutory discount/i }).click();
  await page.getByLabel(/ID no\. \/ control no\.|ID reference/i).fill("SC00000001");
  await page.getByLabel(/I confirm (this request is accurate|these entitlement details)/i).check();
  await capturePhonePhoto(page);
}

async function capturePhonePhoto(page: Page) {
  await page.getByRole("button", { name: /^take photo$/i }).click();
  const video = page.getByLabel("Live rear camera preview");
  await expect(video).toBeVisible();
  await expect.poll(() => video.evaluate((element: HTMLVideoElement) => element.videoWidth * element.videoHeight)).toBeGreaterThan(0);
  await page.getByRole("button", { name: /^capture photo$/i }).click();
  await expect(page.getByText("Photo captured")).toBeVisible();
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

async function expectCustomerVisibleReferencesSafe(page: Page) {
  const customerDom = await page.locator("body").evaluate((element) => element.outerHTML);
  expect(customerDom).not.toMatch(/\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/i);

  const supportReferences = page.locator(".support-reference");
  await expect(supportReferences).toHaveCount(1);
  await expect(supportReferences).toHaveText(/Support reference:\s*[0-9A-F]{4}-[0-9A-F]{4}/);
  expect(await supportReferences.getAttribute("aria-label")).toBeNull();
}

async function getFixtureState(): Promise<EvidenceFixtureState> {
  const response = await fetch(`${baseFixtureUrl}/__fixture/state`);
  return response.json() as Promise<EvidenceFixtureState>;
}
