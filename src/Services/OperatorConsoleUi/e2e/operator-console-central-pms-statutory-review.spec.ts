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
    test(`${viewport.name} queue and detail remain usable without client-to-client requests`, async ({ page }) => {
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
      await expect(page.getByText("Payable-basis status (read-only)")).toBeVisible();
      await expect(page.getByText("₱500.00")).toBeVisible();
      await expect(page.getByRole("button", { name: /^(void|issue|reverse) sales invoice$|^retry fiscal issuance$/i })).toHaveCount(0);

      const documentWidth = await page.evaluate(() => document.documentElement.scrollWidth);
      expect(documentWidth).toBeLessThanOrEqual(viewport.width);
      expect(observedRequests.some((url) => /webpay|assisted-payment|management-platform/i.test(new URL(url).hostname))).toBe(false);
    });
  }

  test("approval sends only decision data with CSRF and refreshes canonical state", async ({ page }) => {
    let decisionBody: Record<string, unknown> | undefined;
    let decisionCsrf: string | undefined;
    await installCanonicalRoutes(page, async (route) => {
      decisionBody = route.request().postDataJSON() as Record<string, unknown>;
      decisionCsrf = route.request().headers()["x-csrf-token"];
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          decisionAccepted: true,
          decisionPersisted: true,
          currentDecisionResultStatus: "APPROVED",
          decision: "APPROVE",
          alreadyDecided: false,
          decisionChanged: true,
          correlationId: "85000000-0000-0000-0000-000000000001"
        })
      });
    });

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
  });
});

async function installCanonicalRoutes(
  page: Page,
  decide?: Parameters<Page["route"]>[1]
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
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(detail()) });
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

function detail() {
  return {
    ...queueItem(),
    evidenceReferences: [{ evidenceType: "SENIOR_CITIZEN_ID", captureMethod: "UPLOAD", referenceNumberMasked: "***1234", verificationStatus: "RECORDED" }],
    requesterAttestation: true,
    originalAmountMinorUnits: 50000,
    finalPayableAmountMinorUnits: 40000,
    currency: "PHP",
    payableBasisApplicationStatus: "PENDING_DECISION",
    correlationId: "87000000-0000-0000-0000-000000000001"
  };
}
