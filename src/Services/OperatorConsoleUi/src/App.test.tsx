import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import {
  createHttpOperatorConsoleApiClient,
  createMockOperatorConsoleApiClient,
  mapApiError,
  type OperatorConsoleApiClient
} from "./apiClient";
import type {
  AccessReadinessResponse,
  ProductionPolicyImportDryRunResult,
  ProductionPolicyImportReviewResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountQueueItem
} from "./types";

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
      evaluateAccessReadiness: vi.fn(async () => readyReadiness()),
      listAuditReport: vi.fn(),
      listStatutoryDiscountDrafts: vi.fn(
        () =>
          new Promise<StatutoryDiscountQueueItem[]>((resolve) => {
            resolveQueue = resolve;
          })
      ),
      getStatutoryDiscountDraft: vi.fn(),
      listStatutoryDiscountEvidence: vi.fn(),
      captureStatutoryDiscountEvidence: vi.fn(),
      submitStatutoryDiscountDecision: vi.fn(),
      applyStatutoryDiscountPayableBasis: vi.fn(),
      dryRunProductionPolicyImport: vi.fn(),
      submitProductionPolicyImportReview: vi.fn(),
      decideProductionPolicyImportReview: vi.fn()
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
    expect(screen.getByText("Production-ready")).toBeInTheDocument();
    expect(screen.getAllByText("READY_VERIFIED").length).toBeGreaterThan(0);
    expect(screen.getByText("Compatibility policy references")).toBeInTheDocument();
    expect(screen.getByText("Policy readiness is not the same as payment approval.")).toBeInTheDocument();
    expect(screen.getByText("No raw evidence or ID numbers are displayed here.")).toBeInTheDocument();
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
    expect(screen.getByText("Dedicated registry")).toBeInTheDocument();
    expect(screen.getAllByText("Manual review").length).toBeGreaterThan(0);
    expect(screen.getAllByText("READY_WITH_MANUAL_REVIEW").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/manual review required before production use/i).length).toBeGreaterThan(0);
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
    expect(screen.getAllByText("CONFIGURED_BUT_UNVERIFIED").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/automatic production application is not allowed/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Blocked").length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_RendersSandboxOnlyPolicyReadinessWarning", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [sandboxOnlyDraft()] })}
        initialPath="/operator-console/statutory-discounts/47000000-0000-0000-0000-000000000099"
      />
    );

    expect(await screen.findByRole("heading", { name: "Sandbox/test policy warning" })).toBeInTheDocument();
    expect(screen.getAllByText("SANDBOX_ONLY").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Sandbox/test").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/sandbox\/test policies are not production-ready/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/automatic production application is not allowed/i)).toBeInTheDocument();
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

  it("StatutoryDiscountDetail_DisablesApplyPayableBasisBeforeApproval", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Apply payable basis" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Apply payable basis" })).toBeDisabled();
    expect(screen.getAllByText(/payable basis can be applied only after approval/i).length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_EnablesApplyPayableBasisAfterApprovalAndDisablesOnceApplied", async () => {
    const onPayableBasisApply = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onPayableBasisApply })}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision actions" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect(await screen.findByText("Decision approved.")).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Apply payable basis" })).toBeEnabled();

    await userEvent.click(screen.getByRole("button", { name: "Apply payable basis" }));

    expect(await screen.findByText("Payable basis applied.")).toBeInTheDocument();
    expect(onPayableBasisApply).toHaveBeenCalledWith(
      expect.objectContaining({
        draftId: firstDraftId,
        originalTariffSnapshotId: "23100000-0000-0000-0000-000000000004"
      })
    );
    expect(await screen.findByText(/this did not create payment, exit authorization, coupon, or gate records/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Apply payable basis" })).toBeDisabled();
    expect(screen.getAllByText(/payable basis has already been applied/i).length).toBeGreaterThan(0);
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

  it("OperatorConsoleReadiness_RendersPanelDimensionsAndSandboxIndicator", async () => {
    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/statutory-discounts" />);

    expect(await screen.findByRole("heading", { name: "Operator readiness state" })).toBeInTheDocument();
    expect(await screen.findByText("Sandbox/local validation context is active. This is not production trust.")).toBeInTheDocument();
    expect(await screen.findByText("Overall readiness")).toBeInTheDocument();
    expect(screen.getAllByText("READY").length).toBeGreaterThan(0);
    expect(screen.getByLabelText("Readiness dimensions")).toHaveTextContent("OPERATOR");
    expect(screen.getByLabelText("Readiness dimensions")).toHaveTextContent("DEVICE");
    expect(screen.getByLabelText("Readiness dimensions")).toHaveTextContent("SHIFT");
    expect(screen.getByLabelText("Readiness dimensions")).toHaveTextContent("SITE");
    expect(screen.getByLabelText("Readiness dimensions")).toHaveTextContent("WORKFLOW");
  });

  it("OperatorConsoleReadiness_DenialShowsReasonsNextActionAndCorrelation", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ readiness: blockedReadiness() })}
        initialPath="/operator-console/statutory-discounts"
      />
    );

    expect(await screen.findByText(/this device, shift, or site is not ready/i)).toBeInTheDocument();
    expect(screen.getByText(/contact a supervisor or support and provide the correlation id/i)).toBeInTheDocument();
    expect(screen.getAllByText("LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION").length).toBeGreaterThan(0);
    expect(screen.getByText(/local\/dev fallback context is not accepted as production trust/i)).toBeInTheDocument();
    expect(screen.getByText("00000000-0000-0000-0000-00000000feed")).toBeInTheDocument();
    expect(screen.getByText(/next action: enroll and activate a production device, shift, and site assignment/i)).toBeInTheDocument();
    expect(screen.getByText("Retryable: No")).toBeInTheDocument();
  });

  it("OperatorConsoleReadiness_BlocksControlledActionsWhenAccessIsDenied", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ readiness: blockedReadiness() })}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision actions" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeDisabled();
    expect(await screen.findByRole("button", { name: "Capture evidence" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Apply payable basis" })).toBeDisabled();
    expect(screen.getAllByText(/readiness check is blocking controlled operator console actions/i).length).toBeGreaterThan(0);
  });

  it("OperatorConsoleReadiness_AllowsControlledActionsWhenReadyAndWorkflowAllows", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision actions" })).toBeInTheDocument();
    expect((await screen.findAllByText("READY")).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeEnabled();
    expect(await screen.findByRole("button", { name: "Capture evidence" })).toBeEnabled();
  });

  it("AuditReporting_RendersReadOnlyPanelRowsAndGuardrails", async () => {
    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/audit" />);

    expect(await screen.findByRole("heading", { name: "Statutory discount audit report" })).toBeInTheDocument();
    expect(screen.getByText("Read-only audit/reporting view.")).toBeInTheDocument();
    expect(screen.getByText(/no payment, gate, coupon, reconciliation, or evidence-file action is performed here/i)).toBeInTheDocument();
    expect(screen.getByText(/raw id numbers and raw evidence files are not displayed/i)).toBeInTheDocument();
    expect(await screen.findByText("STAT-OP-SESSION-0001")).toBeInTheDocument();
    expect(screen.getAllByText("SESSION_LOOKUP / SUCCESS")).not.toHaveLength(0);
    expect(screen.getByRole("columnheader", { name: "Policy Readiness" })).toBeInTheDocument();
    expect(screen.getAllByText("Production-ready").length).toBeGreaterThan(0);
    expect(screen.getAllByText("READY_VERIFIED").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Compatibility policy references").length).toBeGreaterThan(0);
    expect(screen.getByRole("columnheader", { name: "Final Payable" })).toBeInTheDocument();
    expect(document.body.innerHTML).not.toMatch(/1234-5678-9012/i);
    expect(screen.queryByRole("button", { name: /pay/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /open gate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /apply coupon/i })).not.toBeInTheDocument();
  });

  it("AuditReporting_RendersEmptyAndErrorStates", async () => {
    const { rerender } = render(
      <App apiClient={createMockOperatorConsoleApiClient({ empty: true })} initialPath="/operator-console/audit" />
    );

    expect(await screen.findByText("No report rows")).toBeInTheDocument();

    rerender(
      <App
        apiClient={{
          ...createMockOperatorConsoleApiClient(),
          listAuditReport: vi.fn(async () => {
            throw { status: "error", message: "Audit report unavailable." };
          })
        }}
        initialPath="/operator-console/audit"
      />
    );

    expect(await screen.findByText("Unable to load audit report")).toBeInTheDocument();
    expect(screen.getByText("Audit report unavailable.")).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_EvidencePanelShowsMetadataOnlyAndMaskedReferenceGuidance", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Evidence" })).toBeInTheDocument();
    expect(screen.getByText(/metadata-only evidence capture/i)).toBeInTheDocument();
    expect(screen.getByText(/do not upload or enter raw id numbers/i)).toBeInTheDocument();

    await userEvent.selectOptions(await screen.findByLabelText(/capture method/i), "MANUAL_REFERENCE");

    expect(screen.getByLabelText(/masked id reference \/ last 4 only/i)).toBeInTheDocument();
    expect(screen.getByText("Do not enter the full ID number.")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("****1234")).toBeInTheDocument();
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
            verificationStatus: "VERIFIED_OFFICIAL",
            registrySource: "DEDICATED_REGISTRY",
            policyReadinessClassification: "READY_VERIFIED",
            requiresManualReview: false,
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
        verificationStatus: "VERIFIED_OFFICIAL",
        registrySource: "DEDICATED_REGISTRY",
        policyReadinessClassification: "READY_VERIFIED",
        requiresManualReview: false,
        activity: ["Draft requested."]
      }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const queue = await client.listStatutoryDiscountDrafts();
    const detail = await client.getStatutoryDiscountDraft(firstDraftId);

    expect(queue[0].ticketReference).toBe("REAL-QUEUE-001");
    expect(queue[0].policyContext.registrySource).toBe("DEDICATED_REGISTRY");
    expect(queue[0].policyContext.policyReadinessClassification).toBe("READY_VERIFIED");
    expect(detail.policyContext.nationalLawReference).toBe("RA 9994");
    expect(detail.policyContext.productionAutoApplicationEligible).toBe(true);
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

  it("OperatorConsoleApi_EvaluatesAccessReadinessThroughFetch", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(readyReadiness()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const readiness = await client.evaluateAccessReadiness({
      siteId: "77000000-0000-0000-0000-000000000002",
      siteGroupId: "77000000-0000-0000-0000-000000000001",
      requestedAction: "SESSION_LOOKUP",
      clientContext: {
        uiModule: "OperatorConsoleUi",
        screenState: "/operator-console/statutory-discounts"
      },
      devModeContext: {
        usesLocalDevFallbackContext: true,
        environmentName: "test"
      }
    });

    expect(readiness.accessAllowed).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/access/readiness/evaluate",
      expect.objectContaining({ method: "POST" })
    );

    const requestOptions = fetchMock.mock.calls[0][1];
    expectOperatorContextHeaders(requestOptions?.headers);
    expect(requestOptions?.headers).toEqual(expect.objectContaining({ "Content-Type": "application/json" }));

    const requestBody = JSON.parse(requestOptions?.body as string);
    expect(requestBody).toEqual(expect.objectContaining({
      operatorUserId: "77000000-0000-0000-0000-000000000010",
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000030",
      operatorShiftId: "77000000-0000-0000-0000-000000000050",
      requestedAction: "SESSION_LOOKUP",
      devModeContext: expect.objectContaining({
        usesLocalDevFallbackContext: true,
        environmentName: "test"
      })
    }));
  });

  it("OperatorConsoleApi_LoadsAuditReportThroughFetch", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse({
      items: [
        {
          statutoryDiscountValidationId: firstDraftId,
          draftId: firstDraftId,
          parkingSessionId: "25000000-0000-0000-0000-000000000001",
          ticketReference: "AUDIT-001",
          plateNumber: "ABC 1234",
          siteId: "77000000-0000-0000-0000-000000000002",
          siteGroupId: "77000000-0000-0000-0000-000000000001",
          entitlementType: "SENIOR_CITIZEN",
          validationStatus: "REQUESTED",
          evidenceRequired: true,
          evidenceCaptured: false,
          evidenceRequiredSatisfied: false,
          evidenceCount: 0,
          latestEvidenceStatus: null,
          payableBasisApplicationStatus: null,
          originalAmountMinorUnits: 12500,
          statutoryDiscountAmountMinorUnits: 2232,
          finalPayableAmountMinorUnits: 8929,
          currencyCode: "PHP",
          requestedAt: "2026-06-01T08:15:00+08:00",
          validatedAt: null,
          correlationId: "00000000-0000-0000-0000-00000000abcd",
          registrySource: "DEDICATED_REGISTRY",
          policyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
          verificationStatus: "VERIFIED_OFFICIAL",
          policyReadinessClassification: "READY_VERIFIED",
          requiresManualReview: false
        }
      ],
      totalCount: 1,
      limit: 25,
      offset: 0,
      correlationId: "00000000-0000-0000-0000-00000000abcd"
    }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const report = await client.listAuditReport({
      siteId: "77000000-0000-0000-0000-000000000002",
      validationStatus: "REQUESTED"
    });

    expect(report.items[0].ticketReference).toBe("AUDIT-001");
    expect(report.items[0].registrySource).toBe("DEDICATED_REGISTRY");
    expect(report.items[0].policyReadinessClassification).toBe("READY_VERIFIED");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/v1/ops/operator-console/audit/statutory-discounts?"),
      expect.any(Object)
    );
    expect(fetchMock.mock.calls[0][0]).toContain("siteId=77000000-0000-0000-0000-000000000002");
    expect(fetchMock.mock.calls[0][0]).toContain("validationStatus=REQUESTED");
    expectOperatorContextHeaders(fetchMock.mock.calls[0][1]?.headers);
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
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000030",
      operatorShiftId: "77000000-0000-0000-0000-000000000050",
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
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000030",
      operatorShiftId: "77000000-0000-0000-0000-000000000050",
      decision: "APPROVE"
    }));
    expect(requestBody.idempotencyKey).toMatch(`operator-console-ui-approve-${firstDraftId}-`);
  });

  it("ProductionPolicyImportReview_SubmitsDryRunForReviewAndDisplaysReviewOnlyState", async () => {
    const onDryRun = vi.fn();
    const onSubmitReview = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          onProductionPolicyDryRun: onDryRun,
          onProductionPolicyReviewSubmit: onSubmitReview
        })}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    expect(await screen.findByRole("heading", { name: "DB-backed review queue" })).toBeInTheDocument();
    expect(screen.getByText("This screen does not execute production import.")).toBeInTheDocument();
    expect(screen.getByText("This screen does not activate production policies.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Run dry-run" }));
    expect(await screen.findByRole("heading", { name: "Dry-run result" })).toBeInTheDocument();
    expect(screen.getByText("Dry run completed. No policies were imported.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Submit for review" }));
    expect(await screen.findByRole("heading", { name: "Persisted review" })).toBeInTheDocument();
    expect(screen.getAllByText("LEGAL_REVIEW_PENDING").length).toBeGreaterThan(0);
    expect(screen.getByText("Production policy activation blocked")).toBeInTheDocument();
    expect(screen.getByText("DB repo alignment only")).toBeInTheDocument();
    const reviewPanel = screen.getByRole("heading", { name: "Persisted review" }).closest("section");
    expect(reviewPanel).not.toBeNull();
    expect(within(reviewPanel as HTMLElement).getByText("false")).toBeInTheDocument();
    expect(within(reviewPanel as HTMLElement).getByText("true")).toBeInTheDocument();
    expect(screen.getByText("Final approved state is APPROVED_FOR_DB_REPO_ALIGNMENT, not production active.")).toBeInTheDocument();
    expect(document.body.innerHTML).not.toMatch(/production active<\/dt><dd>true/i);
    expect(document.body.innerHTML).not.toMatch(/activated<\/dt><dd>true/i);
    expect(onDryRun).toHaveBeenCalled();
    expect(onSubmitReview).toHaveBeenCalledWith(expect.objectContaining({
      fileName: "production-policy-candidate.csv",
      dryRunResult: expect.objectContaining({ imported: false, dryRunOnly: true })
    }));
  });

  it("ProductionPolicyImportReview_DecisionControlsRecordApprovalForDbRepoAlignmentOnly", async () => {
    const onDecision = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onProductionPolicyReviewDecision: onDecision })}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    await userEvent.click(await screen.findByRole("button", { name: "Run dry-run" }));
    await userEvent.click(await screen.findByRole("button", { name: "Submit for review" }));
    await userEvent.selectOptions(await screen.findByLabelText(/reviewer role for approve/i), "DB");
    await userEvent.click(await screen.findByRole("button", { name: "Approve" }));

    expect(await screen.findByText("APPROVED_FOR_DB_REPO_ALIGNMENT")).toBeInTheDocument();
    expect(screen.getByText("Review decision recorded. No policies were imported or activated.")).toBeInTheDocument();
    const reviewPanel = screen.getByRole("heading", { name: "Persisted review" }).closest("section");
    expect(reviewPanel).not.toBeNull();
    expect(within(reviewPanel as HTMLElement).getByText("false")).toBeInTheDocument();
    expect(within(reviewPanel as HTMLElement).getByText("true")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /activate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /execute import/i })).not.toBeInTheDocument();
    expect(onDecision).toHaveBeenCalledWith(expect.objectContaining({
      reviewId: "99000000-0000-0000-0000-000000000001",
      action: "APPROVE_DB"
    }));
  });

  it("ProductionPolicyImportReview_RejectRequestChangesAndEscalateRequireReason", async () => {
    const onDecision = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onProductionPolicyReviewDecision: onDecision })}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    await userEvent.click(await screen.findByRole("button", { name: "Run dry-run" }));
    await userEvent.click(await screen.findByRole("button", { name: "Submit for review" }));
    await userEvent.click(await screen.findByRole("button", { name: "Reject" }));

    expect(screen.getByText("REJECT requires a reason.")).toBeInTheDocument();
    expect(onDecision).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText(/decision reason/i), "Needs source correction");
    await userEvent.click(screen.getByRole("button", { name: "Request changes" }));
    await waitFor(() => expect(onDecision).toHaveBeenCalledWith(expect.objectContaining({
      action: "REQUEST_CHANGES",
      reason: "Needs source correction"
    })));
    await userEvent.click(screen.getByRole("button", { name: "Escalate" }));

    await waitFor(() => expect(onDecision).toHaveBeenCalledWith(expect.objectContaining({
      action: "ESCALATE",
      reason: "Needs source correction"
    })));
  });

  it("OperatorConsoleApi_ProductionPolicyReviewEndpointsUseExpectedUrlsAndNeverCallActivation", async () => {
    const dryRunResult = productionPolicyDryRunResponse();
    const reviewResponse = productionPolicyReviewResponse("LEGAL_REVIEW_PENDING");
    const approvedReviewResponse = productionPolicyReviewResponse("APPROVED_FOR_DB_REPO_ALIGNMENT", "APPROVE_DB");
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(dryRunResult))
      .mockResolvedValueOnce(jsonResponse(reviewResponse))
      .mockResolvedValueOnce(jsonResponse(approvedReviewResponse));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const dryRun = await client.dryRunProductionPolicyImport({
      csvContent: "policy_code\nPH_VALID_SC_IMPORT_001",
      fileName: "candidate.csv"
    });
    const submitted = await client.submitProductionPolicyImportReview({
      dryRunResult: dryRun,
      fileName: "candidate.csv"
    });
    const decided = await client.decideProductionPolicyImportReview({
      reviewId: submitted.submission.reviewId,
      action: "APPROVE_DB",
      reason: "Approved for DB repo alignment."
    });

    expect(dryRun.imported).toBe(false);
    expect(submitted.productionPolicyActivationBlocked).toBe(true);
    expect(decided.submission.status).toBe("APPROVED_FOR_DB_REPO_ALIGNMENT");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/statutory-discounts/policies/import/dry-run",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/statutory-discounts/policies/import/reviews",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      `http://central-pms.test/v1/ops/operator-console/statutory-discounts/policies/import/reviews/${submitted.submission.reviewId}/decision`,
      expect.objectContaining({ method: "POST" })
    );

    const submitBody = JSON.parse(fetchMock.mock.calls[1][1]?.body as string);
    const decisionBody = JSON.parse(fetchMock.mock.calls[2][1]?.body as string);
    expect(submitBody).toEqual(expect.objectContaining({
      fileName: "candidate.csv",
      dryRunResult: expect.objectContaining({ imported: false, dryRunOnly: true })
    }));
    expect(decisionBody).toEqual(expect.objectContaining({
      action: "APPROVE_DB",
      reason: "Approved for DB repo alignment."
    }));
    expectOperatorContextHeaders(fetchMock.mock.calls[1][1]?.headers);
    expectOperatorContextHeaders(fetchMock.mock.calls[2][1]?.headers);

    const calledUrls = fetchMock.mock.calls.map((call) => String(call[0])).join("\n");
    expect(calledUrls).not.toMatch(/activate/i);
    expect(calledUrls).not.toMatch(/execution/i);
    expect(calledUrls).not.toMatch(/execute/i);
    expect(calledUrls).not.toMatch(/import\/run/i);
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

function sandboxOnlyDraft(): StatutoryDiscountDraftDetail {
  return {
    draftId: "47000000-0000-0000-0000-000000000099",
    parkingSessionId: "25000000-0000-0000-0000-000000000099",
    ticketReference: "STAT-OP-SANDBOX-0099",
    plateNumber: "SAN 0099",
    siteId: "77000000-0000-0000-0000-000000000002",
    siteGroupId: "77000000-0000-0000-0000-000000000001",
    siteName: "Sandbox Parking / Exit",
    laneName: "Sandbox Exit Lane",
    entitlementType: "Senior Citizen",
    status: "Blocked",
    requestedAt: "2026-06-01T09:30:00+08:00",
    requestedBy: "operator.sandbox",
    parkingStartedAt: "2026-06-01T08:30:00+08:00",
    originalTariffAmount: "PHP 90.00",
    payableBasisPreview: "Blocked",
    currentPaymentStatus: "Read-only in this module",
    maskedIdReference: "Evidence metadata only",
    issuingAuthority: "OSCA",
    evidenceCaptured: false,
    evidenceRequiredSatisfied: false,
    evidenceCount: 0,
    requiredEvidenceTypes: ["SENIOR_CITIZEN_ID"],
    originalTariffSnapshotId: "23100000-0000-0000-0000-000000000099",
    originalAmountMinorUnits: 9000,
    currencyCode: "PHP",
    policyContext: {
      kind: "blocked-unverified-local",
      title: "Sandbox/test policy warning",
      operatorSummary: "Sandbox/test policies are visible for validation only and are not production-ready.",
      registrySource: "DEDICATED_REGISTRY",
      policyResolutionBasis: "LOCAL_POLICY_BLOCKED",
      policyCode: "TEST_OC_POLICY_SC_SANDBOX_ONLY",
      policyName: "Sandbox Operator Console Senior Citizen Policy",
      legalBasisReference: "TEST-LEGAL-ONLY",
      ordinanceReference: "TEST-ORD-ONLY",
      verificationStatus: "VERIFIED_OFFICIAL",
      policyReadinessClassification: "SANDBOX_ONLY",
      requiresManualReview: true,
      policyReadinessReason: "SANDBOX_ONLY",
      operatorMessage: "Sandbox/test policies are not production-ready.",
      productionAutoApplicationEligible: false,
      benefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
      discountBaseScope: "VAT_EXCLUSIVE",
      evidenceRequired: true,
      requiredEvidenceType: "SENIOR_CITIZEN_ID",
      ineligibilityReason: "Sandbox policy is not production-ready."
    },
    auditActivity: ["Sandbox policy detected.", "Decision blocked pending production policy readiness."]
  };
}

function readyReadiness(): AccessReadinessResponse {
  return {
    accessEvaluationId: undefined,
    accessAllowed: true,
    accessDecision: "ALLOWED",
    requestedAction: "SESSION_LOOKUP",
    readinessStatus: "READY",
    readinessDimensions: [
      { dimension: "OPERATOR", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "DEVICE", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "SHIFT", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "SITE", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "WORKFLOW", status: "READY", required: true, denialReasonCodes: [] }
    ],
    denialReasons: [],
    operatorReadiness: {
      operatorUserId: "77000000-0000-0000-0000-000000000010",
      status: "READY",
      ready: true
    },
    deviceReadiness: {
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000030",
      status: "READY",
      ready: true
    },
    shiftReadiness: {
      operatorShiftId: "77000000-0000-0000-0000-000000000050",
      status: "READY",
      ready: true
    },
    siteReadiness: {
      siteId: "77000000-0000-0000-0000-000000000002",
      siteGroupId: "77000000-0000-0000-0000-000000000001",
      status: "READY",
      ready: true
    },
    workflowReadiness: {
      requestedAction: "SESSION_LOOKUP",
      workflowState: "QUEUE",
      status: "READY",
      ready: true
    },
    auditPersisted: false,
    evaluatedAt: "2026-06-08T08:00:00+08:00",
    correlationId: "00000000-0000-0000-0000-00000000cafe",
    retryable: false,
    nextOperatorAction: undefined
  };
}

function blockedReadiness(): AccessReadinessResponse {
  return {
    ...readyReadiness(),
    accessAllowed: false,
    accessDecision: "DENIED",
    readinessStatus: "NOT_READY",
    readinessDimensions: [
      { dimension: "OPERATOR", status: "READY", required: true, denialReasonCodes: [] },
      {
        dimension: "DEVICE",
        status: "NOT_READY",
        required: true,
        denialReasonCodes: ["LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION"]
      },
      { dimension: "SHIFT", status: "NOT_READY", required: true, denialReasonCodes: ["SHIFT_NOT_ACTIVE"] },
      { dimension: "SITE", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "WORKFLOW", status: "READY", required: true, denialReasonCodes: [] }
    ],
    denialReasons: [
      {
        code: "LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION",
        severity: "HIGH",
        retryable: false,
        uxMessageCategory: "PRODUCTION_TRUST_REQUIRED"
      },
      {
        code: "SHIFT_NOT_ACTIVE",
        severity: "MEDIUM",
        retryable: true,
        uxMessageCategory: "SHIFT_READINESS"
      }
    ],
    deviceReadiness: {
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000030",
      status: "NOT_READY",
      ready: false
    },
    shiftReadiness: {
      operatorShiftId: "77000000-0000-0000-0000-000000000050",
      status: "NOT_READY",
      ready: false
    },
    correlationId: "00000000-0000-0000-0000-00000000feed",
    retryable: false,
    nextOperatorAction: "Enroll and activate a production device, shift, and site assignment."
  };
}

function productionPolicyDryRunResponse(): ProductionPolicyImportDryRunResult {
  return {
    imported: false,
    importedRowCount: 0,
    dryRunOnly: true,
    message: "Dry run completed. No policies were imported.",
    summary: {
      totalRows: 1,
      passCount: 1,
      warnCount: 0,
      failCount: 0,
      importableCount: 1,
      manualReviewCount: 0,
      notImportableCount: 0,
      dryRunOnlyCount: 1,
      duplicateCount: 0
    },
    rows: [
      {
        rowNumber: 2,
        policyCode: "PH_VALID_SC_IMPORT_001",
        entitlementType: "SENIOR_CITIZEN",
        decision: "IMPORTABLE_AFTER_APPROVAL",
        findings: []
      }
    ],
    correlationId: "99000000-0000-0000-0000-000000000099"
  };
}

function productionPolicyReviewResponse(status: string, action?: string): ProductionPolicyImportReviewResult {
  const now = "2026-06-10T08:00:00+08:00";
  return {
    imported: false,
    productionPolicyActivationBlocked: true,
    message: action
      ? "Review decision recorded. No policies were imported or activated."
      : "Review submission created. No policies were imported.",
    submission: {
      reviewId: "99000000-0000-0000-0000-000000000001",
      makerOperatorId: "77000000-0000-0000-0000-000000000010",
      fileName: "candidate.csv",
      status,
      dryRunSummary: productionPolicyDryRunResponse().summary,
      reviewerDecisions: action
        ? [
            {
              reviewerRole: "DB",
              action,
              reviewerOperatorId: "77000000-0000-0000-0000-000000000010",
              reason: "Approved for DB repo alignment.",
              decidedAt: now,
              correlationId: "99000000-0000-0000-0000-000000000099"
            }
          ]
        : [],
      history: [
        {
          action: action ?? "SUBMIT_FOR_REVIEW",
          status,
          actorOperatorId: "77000000-0000-0000-0000-000000000010",
          reviewerRole: action ? "DB" : undefined,
          reason: action ? "Approved for DB repo alignment." : undefined,
          occurredAt: now,
          correlationId: "99000000-0000-0000-0000-000000000099"
        }
      ],
      createdAt: now,
      updatedAt: now
    },
    findings: [
      {
        severity: "INFO",
        message: "Review-only path; production policy activation remains blocked."
      }
    ],
    correlationId: "99000000-0000-0000-0000-000000000099"
  };
}

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
    "X-Operator-Device-Binding-Id": "77000000-0000-0000-0000-000000000030",
    "X-Operator-Shift-Id": "77000000-0000-0000-0000-000000000050"
  }));
}
