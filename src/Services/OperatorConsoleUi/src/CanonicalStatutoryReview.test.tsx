import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { CanonicalStatutoryReviewDetailPage } from "./CanonicalStatutoryReview";
import { createHttpOperatorConsoleApiClient, createMockOperatorConsoleApiClient } from "./apiClient";
import type { OperatorConsoleHumanSession } from "./humanAuthentication";
import type { CanonicalStatutoryReviewFilters } from "./types";

const decisionId = "47000000-0000-0000-0000-000000000008";

afterEach(() => vi.unstubAllGlobals());

describe("canonical Central PMS statutory review", () => {
  it("renders the canonical pending queue, safe row fields, responsive controls, and returns with in-memory filters", async () => {
    render(<App apiClient={createMockOperatorConsoleApiClient()} session={session()} initialPath="/operator-console/statutory-discounts" />);

    expect(await screen.findByRole("heading", { name: "Review queue" })).toBeInTheDocument();
    expect((await screen.findAllByRole("button", { name: "Review" })).length).toBeGreaterThan(0);
    expect(screen.getAllByText("WebPay").length).toBeGreaterThan(1);
    expect(screen.getAllByText("Pending Review").length).toBeGreaterThan(0);
    expect(screen.queryByText("ABC-1234")).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Safe search"), "NO-MATCH");
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));
    await waitFor(() => expect(screen.getByText("No requests match the current filters.")).toBeInTheDocument());

    await userEvent.clear(screen.getByLabelText("Safe search"));
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));
    await userEvent.click((await screen.findAllByRole("button", { name: "Review" }))[0]);
    expect(await screen.findByRole("heading", { name: "Request facts" })).toBeInTheDocument();
    expect(screen.getByText("Not yet created")).toBeInTheDocument();
    expect(screen.queryByText(/Original:/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /void sales invoice|refund|open gate/i })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Back to filtered queue" }));
    expect(await screen.findByRole("heading", { name: "Review queue" })).toBeInTheDocument();
    expect(screen.getByLabelText("Safe search")).toHaveValue("");
  });

  it("requires attestation and rejection reason, confirms a decision, and submits no browser-authored authority", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      decisionAccepted: true,
      decisionPersisted: true,
      currentValidationStatus: "REJECTED",
      decision: "REJECT",
      alreadyDecided: false,
      decisionChanged: true,
      correlationId: "99000000-0000-0000-0000-000000000001"
    }), { status: 200, headers: { "Content-Type": "application/json" } }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ csrfToken: () => "csrf-token" });

    await client.submitCanonicalStatutoryReviewDecision({
      statutoryDiscountDecisionCommandId: decisionId,
      decision: "REJECT",
      reasonCode: "EVIDENCE_INVALID",
      reviewerAttestation: true,
      idempotencyKey: "decision-key"
    });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`/v1/ops/operator-console/statutory-discounts/reviews/${decisionId}/decision`);
    expect(init.headers).toMatchObject({ "X-CSRF-Token": "csrf-token" });
    const body = JSON.parse(String(init.body));
    expect(body).toEqual({ decision: "REJECT", decisionReasonCode: "EVIDENCE_INVALID", reviewerAttestation: true, idempotencyKey: "decision-key" });
    expect(JSON.stringify(body)).not.toMatch(/user|reviewerId|siteId|siteGroup|permission|role|timestamp/i);
  });

  it("serializes bounded submission dates and safe filters through the Central PMS facade", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      items: [], totalCount: 0, page: 1, pageSize: 25, hasMore: false,
      correlationId: "99000000-0000-0000-0000-000000000001"
    }), { status: 200, headers: { "Content-Type": "application/json" } }));
    vi.stubGlobal("fetch", fetchMock);

    await createHttpOperatorConsoleApiClient().listCanonicalStatutoryReviews({
      status: "PENDING_REVIEW",
      sourceChannel: "WEBPAY",
      entitlementType: "PWD",
      submittedFrom: "2026-08-23T16:00:00.000Z",
      submittedTo: "2026-08-24T15:59:59.000Z",
      search: " TICKET-1001 ",
      page: 2,
      pageSize: 25
    });

    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://operator-console.local");
    expect(url.pathname).toBe("/v1/ops/operator-console/statutory-discounts/reviews");
    expect(Object.fromEntries(url.searchParams)).toMatchObject({
      status: "PENDING_REVIEW",
      sourceChannel: "WEBPAY",
      entitlementType: "PWD",
      submittedFrom: "2026-08-23T16:00:00.000Z",
      submittedTo: "2026-08-24T15:59:59.000Z",
      search: "TICKET-1001",
      page: "2",
      pageSize: "25"
    });
    const headers = fetchMock.mock.calls[0][1]?.headers as Record<string, string>;
    expect(url.searchParams.get("correlationId")).toBe(headers["X-Correlation-Id"]);
  });

  it("contains confirmation focus, returns it to the triggering action, and retains a timestamped queue on refresh failure", async () => {
    const user = userEvent.setup();
    const client = createMockOperatorConsoleApiClient();
    const originalList = client.listCanonicalStatutoryReviews;
    let calls = 0;
    client.listCanonicalStatutoryReviews = vi.fn(async (input: CanonicalStatutoryReviewFilters, signal?: AbortSignal) => {
      calls += 1;
      if (calls > 1) throw new TypeError("synthetic outage");
      return originalList(input, signal);
    });
    render(<App apiClient={client} session={session()} initialPath="/operator-console/statutory-discounts" />);

    expect((await screen.findAllByRole("button", { name: "Review" })).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: "Refresh" }));
    expect(await screen.findByText(/Showing retained results from the last successful load\. Loaded /)).toBeInTheDocument();

    await user.click((await screen.findAllByRole("button", { name: "Review" }))[0]);
    await user.click(await screen.findByRole("checkbox", { name: /reviewed the required evidence/i }));
    await user.click(await screen.findByRole("button", { name: "Approve" }));
    const dialog = screen.getByRole("alertdialog", { name: "Confirm approval" });
    const confirm = screen.getByRole("button", { name: "Confirm decision" });
    const cancel = screen.getByRole("button", { name: "Cancel" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(confirm).toHaveFocus();
    await user.tab({ shift: true });
    expect(cancel).toHaveFocus();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toHaveFocus();
  });

  it("fails PHP-only detail parsing closed for missing and non-PHP currency", async () => {
    for (const currency of [null, "USD"]) {
      vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify(detailResponse(currency)), { status: 200, headers: { "Content-Type": "application/json" } })));
      await expect(createHttpOperatorConsoleApiClient().getCanonicalStatutoryReview(decisionId)).rejects.toThrow();
    }
  });

  it("uses one correlation identity for canonical detail query and header", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(detailResponse("PHP")), { status: 200, headers: { "Content-Type": "application/json" } }));
    vi.stubGlobal("fetch", fetchMock);
    await createHttpOperatorConsoleApiClient().getCanonicalStatutoryReview(decisionId);
    const [input, init] = fetchMock.mock.calls[0] as unknown as [RequestInfo | URL, RequestInit];
    const url = new URL(String(input), "http://operator-console.local");
    const headers = init?.headers as Record<string, string>;
    expect(url.searchParams.get("correlationId")).toBe(headers["X-Correlation-Id"]);
  });

  it("presents approved eligibility without fabricating a payable-basis application or monetary result", async () => {
    const client = createMockOperatorConsoleApiClient();
    client.getCanonicalStatutoryReview = vi.fn(async () => ({
      ...detailResponse(null),
      commandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      reviewStatus: "APPROVED" as const,
      sessionEligibilityStatus: "ELIGIBLE" as const,
      payableBasisStatus: "NOT_YET_CREATED" as const,
      reviewerDecision: "APPROVE",
      reviewedAt: "2026-08-24T08:05:00+08:00",
      originalAmountMinorUnits: undefined,
      finalPayableAmountMinorUnits: undefined,
      currency: undefined
    }));

    render(<CanonicalStatutoryReviewDetailPage client={client} decisionId={decisionId} onBack={() => undefined} />);

    expect((await screen.findAllByText("Approved")).length).toBeGreaterThan(0);
    expect(screen.getByText("Eligible")).toBeInTheDocument();
    expect(screen.getByText("Not yet created")).toBeInTheDocument();
    expect(screen.getByText("Pending payable-basis creation")).toBeInTheDocument();
    expect(screen.getByText(/Central PMS will calculate and apply the benefit when the payable basis is created/)).toBeInTheDocument();
    expect(screen.queryByText(/Final payable:/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Not applied/i)).not.toBeInTheDocument();
  });

  it("preserves the explicit Central PMS application status and monetary result after basis creation", async () => {
    const client = createMockOperatorConsoleApiClient();
    client.getCanonicalStatutoryReview = vi.fn(async () => ({
      ...detailResponse("PHP"),
      commandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      reviewStatus: "APPROVED" as const,
      sessionEligibilityStatus: "ELIGIBLE" as const,
      payableBasisStatus: "CREATED" as const,
      payableBasisApplicationStatus: "APPLIED",
      statutoryDiscountAmountMinorUnits: 2000,
      currency: "PHP",
      reviewerDecision: "APPROVE",
      reviewedAt: "2026-08-24T08:05:00+08:00"
    }));

    render(<CanonicalStatutoryReviewDetailPage client={client} decisionId={decisionId} onBack={() => undefined} />);

    expect(await screen.findByText("Applied")).toBeInTheDocument();
    expect(screen.getByText(/Statutory benefit:/)).toHaveTextContent("Statutory benefit: \u20B120.00");
    expect(screen.getByText(/Final payable:/)).toHaveTextContent("Final payable: \u20B180.00");
  });
});

function session(): OperatorConsoleHumanSession {
  return {
    sessionReference: "11000000-0000-0000-0000-000000000001",
    userReference: "12000000-0000-0000-0000-000000000001",
    username: "reviewer",
    displayName: "Statutory Reviewer",
    audience: "OPERATOR_CONSOLE",
    assurance: "PASSWORD",
    privilegedAccount: false,
    passwordChangeRequired: false,
    mfaRequired: false,
    mfaSatisfied: true,
    authenticatedAt: "2026-08-24T08:00:00+08:00",
    lastSeenAt: "2026-08-24T08:00:00+08:00",
    idleExpiresAt: "2099-08-24T09:00:00+08:00",
    absoluteExpiresAt: "2099-08-24T16:00:00+08:00",
    permissions: ["statutory-discounts.review.read", "statutory-discounts.evidence.review.view", "statutory-discounts.decision.approve", "statutory-discounts.decision.reject"],
    siteReferences: ["77000000-0000-0000-0000-000000000002"],
    siteGroupReferences: ["88000000-0000-0000-0000-000000000003"],
    hasGlobalScope: false,
    correlationId: "99000000-0000-0000-0000-000000000001"
  };
}

function detailResponse(currency: string | null) {
  return {
    statutoryDiscountDecisionCommandId: decisionId,
    requestReference: decisionId,
    parkingSessionId: "55000000-0000-0000-0000-000000000001",
    sourceChannel: "WEBPAY",
    entitlementType: "PWD",
    commandStatus: "AWAITING_REVIEW",
    decisionResultStatus: "NOT_DECIDED",
    reviewStatus: "PENDING_REVIEW",
    sessionEligibilityStatus: "PENDING_REVIEW" as const,
    payableBasisStatus: "CREATED" as const,
    evidenceRequired: true,
    evidenceRecorded: true,
    submittedAt: "2026-08-24T08:00:00+08:00",
    evidenceReferences: [],
    requesterAttestation: true,
    originalAmountMinorUnits: 10000,
    finalPayableAmountMinorUnits: 8000,
    currency,
    correlationId: "99000000-0000-0000-0000-000000000001"
  };
}
