import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createHttpOperatorConsoleApiClient } from "./apiClient";
import {
  CanonicalStatutoryReviewDetailPage,
  CanonicalStatutoryReviewQueuePage,
  defaultCanonicalStatutoryReviewFilters
} from "./CanonicalStatutoryReview";
import type { OperatorConsoleApiClient } from "./apiClient";
import type { CanonicalStatutoryReviewFilters } from "./types";

const decisionId = "47000000-0000-0000-0000-000000000008";
const correlationId = "99000000-0000-0000-0000-000000000001";

afterEach(() => vi.unstubAllGlobals());

describe("canonical statutory-review decision response contract", () => {
  it.each([
    ["APPROVE", "APPROVED"],
    ["REJECT", "REJECTED"]
  ] as const)("parses the Central PMS %s response field", async (decision, currentValidationStatus) => {
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse(decisionResponse(decision, currentValidationStatus))));

    const result = await client().submitCanonicalStatutoryReviewDecision({
      statutoryDiscountDecisionCommandId: decisionId,
      decision,
      reasonCode: decision === "REJECT" ? "EVIDENCE_INVALID" : undefined,
      reviewerAttestation: true,
      idempotencyKey: `contract-${decision.toLowerCase()}`
    });

    expect(result.currentValidationStatus).toBe(currentValidationStatus);
    expect(result).not.toHaveProperty("currentDecisionResultStatus");
  });

  it.each([
    ["missing canonical status", decisionResponse("APPROVE", undefined)],
    ["obsolete status only", { ...decisionResponse("APPROVE", undefined), currentDecisionResultStatus: "APPROVED" }]
  ])("rejects an accepted response with %s", async (_case, responseBody) => {
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse(responseBody)));

    await expect(client().submitCanonicalStatutoryReviewDecision({
      statutoryDiscountDecisionCommandId: decisionId,
      decision: "APPROVE",
      reviewerAttestation: true,
      idempotencyKey: "contract-invalid"
    })).rejects.toEqual(expect.objectContaining({
      status: "error",
      errorCode: "OPERATOR_CONSOLE_CANONICAL_DECISION_RESPONSE_INVALID",
      message: "Central PMS returned an invalid decision response. Refresh the request before trying again."
    }));
  });

  it.each([
    ["APPROVE", "APPROVED"],
    ["REJECT", "REJECTED"]
  ] as const)("passes the actual %s JSON shape through the HTTP client into an accessible %s announcement and refreshes detail and queue", async (decision, status) => {
    const runtime = runtimeFetch(decisionResponse(decision, status));
    vi.stubGlobal("fetch", runtime.fetchMock);
    const user = userEvent.setup();
    render(<ContractHarness client={client()} />);

    expect(await screen.findByRole("heading", { name: "Person with Disability" })).toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: /reviewed the required evidence/i }));
    if (decision === "REJECT") await user.selectOptions(screen.getByLabelText("Rejection reason"), "EVIDENCE_INVALID");
    await user.click(screen.getByRole("button", { name: decision === "APPROVE" ? "Approve" : "Reject" }));
    await user.click(screen.getByRole("button", { name: "Confirm decision" }));

    expect(await screen.findByRole("status")).toHaveTextContent(`Decision recorded: ${status}.`);
    expect(screen.queryByText(/undefined/i)).not.toBeInTheDocument();
    await waitFor(() => expect(runtime.detailCalls()).toBeGreaterThanOrEqual(2));
    await user.click(screen.getByRole("button", { name: "Back to filtered queue" }));
    expect(await screen.findByRole("heading", { name: "Review queue" })).toBeInTheDocument();
    await waitFor(() => expect(runtime.queueCalls()).toBe(1));
  });

  it("announces a controlled contract error instead of success when canonical status is absent", async () => {
    const runtime = runtimeFetch(decisionResponse("APPROVE", undefined));
    vi.stubGlobal("fetch", runtime.fetchMock);
    const user = userEvent.setup();
    render(<ContractHarness client={client()} />);

    expect(await screen.findByRole("heading", { name: "Person with Disability" })).toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: /reviewed the required evidence/i }));
    await user.click(screen.getByRole("button", { name: "Approve" }));
    await user.click(screen.getByRole("button", { name: "Confirm decision" }));

    expect(await screen.findByText(/Central PMS returned an invalid decision response/)).toHaveAttribute("role", "alert");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.queryByText(/Decision recorded|undefined/i)).not.toBeInTheDocument();
  });

  it("preserves controlled concurrency conflict handling and refreshes canonical detail", async () => {
    const runtime = runtimeFetch({ errorCode: "STATUTORY_DISCOUNT_ALREADY_COMPLETED" }, 409);
    vi.stubGlobal("fetch", runtime.fetchMock);
    const user = userEvent.setup();
    render(<ContractHarness client={client()} />);

    expect(await screen.findByRole("heading", { name: "Person with Disability" })).toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: /reviewed the required evidence/i }));
    await user.click(screen.getByRole("button", { name: "Approve" }));
    await user.click(screen.getByRole("button", { name: "Confirm decision" }));

    expect(await screen.findByText(/Another reviewer already decided this request/)).toHaveAttribute("role", "alert");
    await waitFor(() => expect(runtime.detailCalls()).toBeGreaterThanOrEqual(2));
    expect(screen.queryByText(/Decision recorded|undefined/i)).not.toBeInTheDocument();
  });
});

function ContractHarness({ client }: { client: OperatorConsoleApiClient }) {
  const [showDetail, setShowDetail] = useState(true);
  const [filters, setFilters] = useState<CanonicalStatutoryReviewFilters>(defaultCanonicalStatutoryReviewFilters);
  return showDetail
    ? <CanonicalStatutoryReviewDetailPage client={client} decisionId={decisionId} onBack={() => setShowDetail(false)} />
    : <CanonicalStatutoryReviewQueuePage client={client} filters={filters} onFiltersChange={setFilters} onOpen={() => setShowDetail(true)} />;
}

function client() {
  return createHttpOperatorConsoleApiClient({
    csrfToken: () => "csrf-token",
    permissions: ["statutory-discounts.decision.approve", "statutory-discounts.decision.reject"]
  });
}

function runtimeFetch(decisionBody: unknown, decisionStatus = 200) {
  let detailCallCount = 0;
  let queueCallCount = 0;
  let canonicalStatus: "APPROVED" | "REJECTED" | undefined;
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/evidence")) return jsonResponse({ errorCode: "EVIDENCE_NOT_AVAILABLE" }, 403);
    if (url.includes("/decision") && init?.method === "POST") {
      if (decisionStatus === 200 && decisionBody && typeof decisionBody === "object") {
        const candidate = (decisionBody as { currentValidationStatus?: unknown }).currentValidationStatus;
        canonicalStatus = candidate === "APPROVED" || candidate === "REJECTED" ? candidate : undefined;
      }
      return jsonResponse(decisionBody, decisionStatus);
    }
    if (url.includes(`/reviews/${decisionId}`)) {
      detailCallCount += 1;
      return jsonResponse(detailResponse(canonicalStatus));
    }
    if (url.includes("/reviews?")) {
      queueCallCount += 1;
      return jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 25, hasMore: false, correlationId });
    }
    throw new Error(`Unexpected request: ${url}`);
  });
  return { fetchMock, detailCalls: () => detailCallCount, queueCalls: () => queueCallCount };
}

function decisionResponse(decision: "APPROVE" | "REJECT", currentValidationStatus: "APPROVED" | "REJECTED" | undefined) {
  return {
    decisionAccepted: true,
    decisionPersisted: true,
    ...(currentValidationStatus === undefined ? {} : { currentValidationStatus }),
    decision,
    alreadyDecided: false,
    decisionChanged: true,
    correlationId
  };
}

function detailResponse(canonicalStatus?: "APPROVED" | "REJECTED") {
  const decided = canonicalStatus !== undefined;
  return {
    statutoryDiscountDecisionCommandId: decisionId,
    requestReference: decisionId,
    parkingSessionId: "55000000-0000-0000-0000-000000000001",
    sourceChannel: "WEBPAY",
    entitlementType: "PWD",
    commandStatus: decided ? "COMPLETED" : "AWAITING_REVIEW",
    decisionResultStatus: canonicalStatus ?? "NOT_DECIDED",
    reviewStatus: canonicalStatus ?? "PENDING_REVIEW",
    evidenceRequired: true,
    evidenceRecorded: true,
    submittedAt: "2026-08-24T08:00:00+08:00",
    evidenceReferences: [],
    requesterAttestation: true,
    sessionEligibilityStatus: canonicalStatus === "APPROVED"
      ? "ELIGIBLE"
      : canonicalStatus === "REJECTED"
        ? "NOT_ELIGIBLE"
        : "PENDING_REVIEW",
    payableBasisStatus: "NOT_YET_CREATED",
    payableBasisApplicationStatus: null,
    correlationId
  };
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}
