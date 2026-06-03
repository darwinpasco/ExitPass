import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import {
  createHttpOperatorConsoleApiClient,
  createMockOperatorConsoleApiClient,
  mapApiError,
  type OperatorConsoleApiClient
} from "./apiClient";
import type { StatutoryDiscountQueueItem } from "./types";

const firstDraftId = "47000000-0000-0000-0000-000000000008";
const verifiedLocalDraftId = "47000000-0000-0000-0000-000000000009";
const blockedLocalDraftId = "47000000-0000-0000-0000-000000000010";

describe("ExitPass Operator Console statutory discount foundation", () => {
  it("OperatorConsole_RendersShellAndRoutes", async () => {
    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console" />);

    expect(screen.getByRole("heading", { name: "ExitPass Operator Console" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Operator Console routes" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /statutory discount validation foundation/i })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /open work queue/i }));

    expect(await screen.findByRole("heading", { name: "Work queue" })).toBeInTheDocument();
    expect(await screen.findByText("STAT-OP-SESSION-0001")).toBeInTheDocument();
  });

  it("StatutoryDiscountQueue_RendersLoadingEmptyAndDataStates", async () => {
    let resolveQueue: (items: StatutoryDiscountQueueItem[]) => void = () => undefined;
    const apiClient: OperatorConsoleApiClient = {
      listStatutoryDiscountDrafts: vi.fn(
        () =>
          new Promise<StatutoryDiscountQueueItem[]>((resolve) => {
            resolveQueue = resolve;
          })
      ),
      getStatutoryDiscountDraft: vi.fn(),
      listStatutoryDiscountEvidence: vi.fn(),
      captureStatutoryDiscountEvidence: vi.fn(),
      submitStatutoryDiscountDecision: vi.fn()
    };

    const { rerender } = render(
      <App apiClient={apiClient} initialPath="/operator-console/statutory-discounts" />
    );

    expect(screen.getByText("Loading queue")).toBeInTheDocument();

    resolveQueue([]);
    expect(await screen.findByText("No drafts")).toBeInTheDocument();

    rerender(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath="/operator-console/statutory-discounts"
      />
    );

    expect(await screen.findByText("STAT-OP-SESSION-0001")).toBeInTheDocument();
    expect(screen.getByText("ABC 1234")).toBeInTheDocument();
    expect(screen.getByText("RA 9994 / RA 10754 national fallback")).toBeInTheDocument();
  });

  it("StatutoryDiscountQueue_ViewActionNavigatesToDraftDetail", async () => {
    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/statutory-discounts" />);

    await userEvent.click(await screen.findByRole("button", { name: /view STAT-OP-SESSION-0001/i }));

    expect(await screen.findByRole("heading", { name: "STAT-OP-SESSION-0001" })).toBeInTheDocument();
    expect(screen.getByText(firstDraftId)).toBeInTheDocument();
    expect(screen.getByText("Policy context")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "National fallback policy" })).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_RendersNationalFallbackPolicyContext", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "National fallback policy" })).toBeInTheDocument();
    expect(screen.getByText("Republic Act No. 9994")).toBeInTheDocument();
    expect(screen.getAllByText("RA 9994").length).toBeGreaterThan(0);
    expect(screen.getByText(/no verified local ordinance overrides it/i)).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_RendersVerifiedLocalPolicyContext", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Verified local policy" })).toBeInTheDocument();
    expect(screen.getByText("QC Ordinance 2026-04")).toBeInTheDocument();
    expect(screen.getByText("RA 10754")).toBeInTheDocument();
    expect(screen.getAllByText(/approval is blocked until required evidence is captured/i).length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_RendersBlockedUnverifiedLocalPolicyState", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${blockedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Unverified local policy blocked" })).toBeInTheDocument();
    expect(screen.getByText("LOCAL_POLICY_BLOCKED")).toBeInTheDocument();
    expect(screen.getByText("Local policy is not verified for operator use.")).toBeInTheDocument();
    expect(screen.getAllByText("Blocked").length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_ApproveActionCallsDecisionEndpointAndRefreshes", async () => {
    const onDecision = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onDecision })}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision actions" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect(await screen.findByText("Decision approved.")).toBeInTheDocument();
    expect(onDecision).toHaveBeenCalledWith(
      expect.objectContaining({
        draftId: firstDraftId,
        decision: "APPROVE"
      })
    );
  });

  it("StatutoryDiscountDetail_RejectRequiresReasonBeforeCallingDecisionEndpoint", async () => {
    const onDecision = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onDecision })}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision actions" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Reject" }));
    expect(screen.getByText("Reject requires a reason code.")).toBeInTheDocument();
    expect(onDecision).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText(/reject reason code/i), "ID_NOT_VALID");
    await userEvent.click(screen.getByRole("button", { name: "Reject" }));

    expect(await screen.findByText("Decision rejected.")).toBeInTheDocument();
    expect(onDecision).toHaveBeenCalledWith(
      expect.objectContaining({
        draftId: firstDraftId,
        decision: "REJECT",
        reasonCode: "ID_NOT_VALID"
      })
    );
  });

  it("StatutoryDiscountDetail_ShowsEvidencePanelAndBlocksApprovalWhenEvidenceRequired", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Evidence" })).toBeInTheDocument();
    expect(await screen.findByText(/no evidence metadata has been captured/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getAllByText(/approval is blocked until required evidence is captured/i).length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_CapturesEvidenceAndEnablesApprovalAfterRefresh", async () => {
    const onEvidenceCapture = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onEvidenceCapture })}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Evidence" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();

    await userEvent.click(await screen.findByLabelText(/operator confirms evidence was reviewed/i));
    await userEvent.click(await screen.findByRole("button", { name: "Capture evidence" }));

    expect(await screen.findByText("Evidence metadata captured.")).toBeInTheDocument();
    expect(await screen.findByText("Required evidence is captured.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
    expect(onEvidenceCapture).toHaveBeenCalledWith(
      expect.objectContaining({
        draftId: verifiedLocalDraftId,
        evidenceType: "PWD_ID",
        captureMethod: "OPERATOR_CONFIRMED",
        operatorConfirmation: true
      })
    );
  });

  it("OperatorConsoleApi_MapsBackendErrorsIntoUiErrors", () => {
    expect(mapApiError({ status: "access-denied", message: "Access blocked", errorCode: "ACCESS_BLOCKED" })).toEqual({
      status: "access-denied",
      message: "Access blocked",
      errorCode: "ACCESS_BLOCKED"
    });

    expect(mapApiError(new Error("network"))).toEqual({
      status: "error",
      message: "Operator Console statutory discount data could not be loaded."
    });
  });

  it("OperatorConsoleApi_LoadsQueueAndDetailThroughFetch", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({
        items: [
          {
            draftId: firstDraftId,
            parkingSessionId: "25000000-0000-0000-0000-000000000001",
            ticketReference: "REAL-QUEUE-001",
            plateNumber: "ABC 1234",
            siteId: "77000000-0000-0000-0000-000000000002",
            siteName: "Terminal Parking",
            entitlementType: "SENIOR_CITIZEN",
            validationStatus: "REQUESTED",
            evidenceRequired: false,
            evidenceRequiredSatisfied: false,
            evidenceCount: 0,
            policyResolutionBasis: "NATIONAL_LAW_FALLBACK",
            policyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            requestedAt: "2026-06-01T08:15:00+08:00"
          }
        ]
      }))
      .mockResolvedValueOnce(jsonResponse({
        draftId: firstDraftId,
        parkingSessionId: "25000000-0000-0000-0000-000000000001",
        ticketReference: "REAL-QUEUE-001",
        plateNumber: "ABC 1234",
        siteId: "77000000-0000-0000-0000-000000000002",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        siteName: "Terminal Parking",
        entitlementType: "SENIOR_CITIZEN",
        validationStatus: "REQUESTED",
        evidenceRequired: false,
        evidenceCaptured: false,
        evidenceRequiredSatisfied: false,
        evidenceCount: 0,
        requiredEvidenceTypes: [],
        requestedAt: "2026-06-01T08:15:00+08:00",
        policyResolutionBasis: "NATIONAL_LAW_FALLBACK",
        policyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
        nationalLawReference: "RA 9994",
        activity: ["Draft requested."]
      }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const queue = await client.listStatutoryDiscountDrafts();
    const detail = await client.getStatutoryDiscountDraft(firstDraftId);

    expect(queue[0].ticketReference).toBe("REAL-QUEUE-001");
    expect(detail.policyContext.nationalLawReference).toBe("RA 9994");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/v1/ops/operator-console/statutory-discounts/drafts?correlationId="),
      expect.any(Object)
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/v1/ops/operator-console/statutory-discounts/drafts/${firstDraftId}?correlationId=`),
      expect.any(Object)
    );
    expectOperatorContextHeaders(fetchMock.mock.calls[0][1]?.headers);
    expectOperatorContextHeaders(fetchMock.mock.calls[1][1]?.headers);
  });

  it("OperatorConsoleApi_EvidenceEndpointsUseOperatorHeaders", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({
        draftId: firstDraftId,
        evidenceRequired: true,
        evidenceRequiredSatisfied: false,
        requiredEvidenceTypes: ["SENIOR_CITIZEN_ID"],
        evidenceCount: 0,
        latestEvidenceStatus: null,
        items: []
      }))
      .mockResolvedValueOnce(jsonResponse({
        evidenceId: "66000000-0000-0000-0000-000000000001",
        draftId: firstDraftId,
        evidenceType: "SENIOR_CITIZEN_ID",
        captureMethod: "OPERATOR_CONFIRMED",
        verificationStatus: "CAPTURED",
        evidenceRequiredSatisfied: true,
        currentDraftStatus: "REQUESTED",
        accessAllowed: true
      }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const list = await client.listStatutoryDiscountEvidence(firstDraftId);
    const capture = await client.captureStatutoryDiscountEvidence({
      draftId: firstDraftId,
      siteId: "77000000-0000-0000-0000-000000000002",
      siteGroupId: "77000000-0000-0000-0000-000000000001",
      evidenceType: "SENIOR_CITIZEN_ID",
      captureMethod: "OPERATOR_CONFIRMED",
      operatorConfirmation: true
    });

    expect(list.requiredEvidenceTypes).toContain("SENIOR_CITIZEN_ID");
    expect(capture.evidenceRequiredSatisfied).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/v1/ops/operator-console/statutory-discounts/${firstDraftId}/evidence?correlationId=`),
      expect.any(Object)
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/v1/ops/operator-console/statutory-discounts/${firstDraftId}/evidence`),
      expect.objectContaining({ method: "POST" })
    );
    expectOperatorContextHeaders(fetchMock.mock.calls[0][1]?.headers);
    expectOperatorContextHeaders(fetchMock.mock.calls[1][1]?.headers);

    const requestBody = JSON.parse(fetchMock.mock.calls[1][1]?.body as string);
    expect(requestBody).toEqual(expect.objectContaining({
      userId: "77000000-0000-0000-0000-000000000010",
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000011",
      operatorShiftId: "77000000-0000-0000-0000-000000000012",
      evidenceType: "SENIOR_CITIZEN_ID",
      captureMethod: "OPERATOR_CONFIRMED"
    }));
  });

  it("OperatorConsoleApi_SubmitsDecisionThroughFetchWithOperatorHeadersAndIdempotencyKey", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse({
      accessAllowed: true,
      accessDecision: "ALLOW",
      accessDenialReasons: [],
      decisionAccepted: true,
      decisionPersisted: true,
      currentValidationStatus: "APPROVED"
    }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const result = await client.submitStatutoryDiscountDecision({
      draftId: firstDraftId,
      siteId: "77000000-0000-0000-0000-000000000002",
      siteGroupId: "77000000-0000-0000-0000-000000000001",
      decision: "APPROVE"
    });

    expect(result.currentStatus).toBe("Approved");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/v1/ops/operator-console/statutory-discounts/${firstDraftId}/decision`),
      expect.objectContaining({ method: "POST" })
    );

    const requestOptions = fetchMock.mock.calls[0][1];
    expectOperatorContextHeaders(requestOptions?.headers);
    expect(requestOptions?.headers).toEqual(expect.objectContaining({ "Content-Type": "application/json" }));

    const requestBody = JSON.parse(requestOptions?.body as string);
    expect(requestBody).toEqual(expect.objectContaining({
      userId: "77000000-0000-0000-0000-000000000010",
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000011",
      operatorShiftId: "77000000-0000-0000-0000-000000000012",
      decision: "APPROVE"
    }));
    expect(requestBody.idempotencyKey).toMatch(`operator-console-ui-approve-${firstDraftId}-`);
  });

  it("OperatorConsole_DoesNotExposeOutOfScopeControls", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/statutory-discounts" />);

    expect(await screen.findByText("STAT-OP-SESSION-0001")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /pay/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /apply coupon/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /open gate/i })).not.toBeInTheDocument();
    expect(document.body.innerHTML).not.toMatch(/aub/i);
    expect(document.body.innerHTML).not.toMatch(/hikcentral/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/webpay/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/public\/coupons/i);
  });
});

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

function expectOperatorContextHeaders(headers: unknown) {
  expect(headers).toEqual(expect.objectContaining({
    "X-Correlation-Id": expect.any(String),
    "X-Operator-User-Id": "77000000-0000-0000-0000-000000000010",
    "X-Operator-Device-Binding-Id": "77000000-0000-0000-0000-000000000011",
    "X-Operator-Shift-Id": "77000000-0000-0000-0000-000000000012"
  }));
}
