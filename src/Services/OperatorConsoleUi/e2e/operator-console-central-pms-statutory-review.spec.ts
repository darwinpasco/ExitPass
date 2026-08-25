import { expect, test, type Page } from "@playwright/test";

const decisionId = "81000000-0000-0000-0000-000000000001";
const siteId = "73000000-0000-0000-0000-000000000001";
const requestReference = "STAT-2026-000001";

test.describe("Operator Console canonical Central PMS statutory review", () => {
  for (const viewport of [
    { name: "desktop", width: 1440, height: 900 },
    { name: "tablet", width: 768, height: 1024 },
    { name: "mobile", width: 390, height: 844 }
  ]) {
    test(`${viewport.name} queue and detail remain usable without client-to-client requests`, async ({ page }, testInfo) => {
      await page.setViewportSize(viewport);
      const observedRequests: string[] = [];
      page.on("request", (request) => observedRequests.push(request.url()));
      await installCanonicalRoutes(page);

      await page.goto("/operator-console/statutory-discounts");
      await expect(page.getByRole("heading", { name: "Review queue" })).toBeVisible();
      await expect(page.getByText(requestReference.slice(0, 8), { exact: false })).toBeVisible();
      await expect(page.getByRole("cell", { name: "WebPay" })).toBeVisible();
      await expect(page.getByText("ABC-1234")).toHaveCount(0);

      const review = page.getByRole("button", { name: "Review", exact: true });
      await review.focus();
      await expect(review).toBeFocused();
      await page.keyboard.press("Enter");
      await expect(page.getByRole("heading", { name: "Senior Citizen" })).toBeVisible();
      await expect(page.getByText(/Evidence is sensitive personal information/i)).toBeVisible();
      await expect(page.getByText("Not yet created")).toBeVisible();
      await expect(page.getByText("Pending Review").first()).toBeVisible();
      await expect(page.getByText(/Central PMS will calculate and apply/i)).toHaveCount(0);
      await expect(page.getByText(/Final payable:/i)).toHaveCount(0);
      await expect(page.getByRole("button", { name: /^(void|issue|reverse) sales invoice$|^retry fiscal issuance$/i })).toHaveCount(0);

      const documentWidth = await page.evaluate(() => document.documentElement.scrollWidth);
      expect(documentWidth).toBeLessThanOrEqual(viewport.width);
      expect(observedRequests.some((url) => /webpay|assisted-payment|management-platform/i.test(new URL(url).hostname))).toBe(false);
      expect(observedRequests.every((url) => new URL(url).origin === new URL(page.url()).origin)).toBe(true);
      const storage = await page.evaluate(async () => ({
        localStorageItems: window.localStorage.length,
        sessionStorageItems: window.sessionStorage.length,
        indexedDbDatabases: "databases" in window.indexedDB
          ? (await window.indexedDB.databases()).length
          : 0
      }));
      expect(storage).toEqual({ localStorageItems: 0, sessionStorageItems: 0, indexedDbDatabases: 0 });
      await page.screenshot({ path: testInfo.outputPath(`${viewport.name}-pending-pre-basis.png`), fullPage: true });
    });
  }

  test("approval sends only decision data with CSRF and refreshes canonical state", async ({ page }, testInfo) => {
    let decisionBody: Record<string, unknown> | undefined;
    let decisionCsrf: string | undefined;
    let approved = false;
    await installCanonicalRoutes(page, async (route) => {
      decisionBody = route.request().postDataJSON() as Record<string, unknown>;
      decisionCsrf = route.request().headers()["x-csrf-token"];
      approved = true;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          decisionAccepted: true,
          decisionPersisted: true,
          currentValidationStatus: "APPROVED",
          decision: "APPROVE",
          alreadyDecided: false,
          decisionChanged: true,
          correlationId: "85000000-0000-0000-0000-000000000001"
        })
      });
    }, () => approved ? detail({
      commandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      reviewStatus: "APPROVED",
      reviewedAt: "2026-08-25T08:30:00+08:00",
      reviewerDecision: "APPROVE",
      sessionEligibilityStatus: "ELIGIBLE"
    }) : detail());

    await page.goto(`/operator-console/statutory-discounts/${decisionId}`);
    await expect(page.getByRole("heading", { name: "Senior Citizen" })).toBeVisible();
    await page.getByRole("checkbox", { name: /reviewed the required evidence/i }).check();
    await page.getByRole("button", { name: "Approve" }).click();
    await page.getByRole("button", { name: "Confirm decision" }).click();
    await expect.poll(() => decisionBody).toBeTruthy();
    expect(decisionCsrf).toBe("operator-console-fixture-csrf");
    expect(decisionBody).toMatchObject({ decision: "APPROVE", reviewerAttestation: true });
    expect(decisionBody).not.toHaveProperty("reviewerUserId");
    expect(decisionBody).not.toHaveProperty("siteId");
    expect(decisionBody).not.toHaveProperty("siteGroupId");
    expect(decisionBody).not.toHaveProperty("decisionTimestamp");
    await expect(page.getByText("Decision recorded: APPROVED.", { exact: true })).toHaveAttribute("role", "status");
    await expect(page.getByText("Approved eligibility")).toBeVisible();
    await expect(page.getByText("Not yet created")).toBeVisible();
    await expect(page.getByText("Pending payable-basis creation")).toBeVisible();
    await expect(page.getByText(/Final payable:/i)).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("desktop-approved-pre-basis.png"), fullPage: true });
  });
});

async function installCanonicalRoutes(
  page: Page,
  decide?: Parameters<Page["route"]>[1],
  detailResponse?: () => ReturnType<typeof detail>
) {
  await page.route("**/v1/ops/operator-console/statutory-discounts/reviews?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [queueItem()],
        totalCount: 1,
        page: 1,
        pageSize: 25,
        hasMore: false,
        correlationId: "82000000-0000-0000-0000-000000000001"
      })
    });
  });
  await page.route(`**/v1/ops/operator-console/statutory-discounts/reviews/${decisionId}?**`, async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(detailResponse?.() ?? detail()) });
  });
  await page.route(`**/v1/ops/operator-console/statutory-discounts/reviews/${decisionId}/evidence`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        statutoryDiscountDecisionCommandId: decisionId,
        evidenceSetReference: "83000000-0000-0000-0000-000000000001",
        sourceChannel: "WEBPAY",
        decisionResultStatus: "PENDING_REVIEW",
        reviewStatus: "PENDING_REVIEW",
        evidenceRequired: true,
        evidenceRecorded: true,
        setStatus: "CURRENT",
        retentionStatus: "ACTIVE",
        deletionStatus: "NONE",
        holdActive: false,
        replacementPosture: "CURRENT",
        items: [{
          evidenceItemReference: "84000000-0000-0000-0000-000000000001",
          documentType: "SENIOR_CITIZEN_ID",
          itemRole: "PRIMARY_IDENTITY_DOCUMENT",
          declaredContentType: "image/png",
          authoritativeContentType: "image/png",
          contentLength: 100,
          uploadStatus: "FINALIZED",
          validationStatus: "VALID",
          scanStatus: "CLEAN",
          reviewabilityStatus: "REVIEWABLE",
          bindingStatus: "BOUND",
          retentionStatus: "ACTIVE",
          deletionStatus: "NONE",
          holdActive: false,
          previewPermitted: true
        }]
      })
    });
  });
  await page.route(`**/v1/ops/operator-console/statutory-discounts/reviews/${decisionId}/decision`, decide ?? (async (route) => {
    await route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ errorCode: "UNEXPECTED_DECISION" }) });
  }));
}

function queueItem() {
  return {
    statutoryDiscountDecisionCommandId: decisionId,
    requestReference,
    parkingSessionId: "86000000-0000-0000-0000-000000000001",
    sourceChannel: "WEBPAY",
    siteId,
    siteGroupId: "74000000-0000-0000-0000-000000000001",
    ticketReference: "TICKET-1001",
    entitlementType: "SENIOR_CITIZEN",
    commandStatus: "ACCEPTED",
    decisionResultStatus: "PENDING_REVIEW",
    reviewStatus: "PENDING_REVIEW",
    evidenceRequired: true,
    evidenceRecorded: true,
    submittedAt: "2026-08-24T08:00:00+08:00"
  };
}

function detail(overrides: Record<string, unknown> = {}) {
  return {
    ...queueItem(),
    evidenceReferences: [{ evidenceType: "SENIOR_CITIZEN_ID", captureMethod: "UPLOAD", referenceNumberMasked: "***1234", verificationStatus: "RECORDED" }],
    requesterAttestation: true,
    sessionEligibilityStatus: "PENDING_REVIEW",
    payableBasisStatus: "NOT_YET_CREATED",
    payableBasisApplicationStatus: null,
    correlationId: "87000000-0000-0000-0000-000000000001",
    ...overrides
  };
}
