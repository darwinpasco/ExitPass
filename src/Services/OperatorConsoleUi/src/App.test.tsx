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
  FiscalIssuanceVoidResult,
  FiscalStatusViewAuditReportResponse,
  FiscalIssuanceStatus,
  OperatorTicketLookupResult,
  ProductionPolicyImportDryRunResult,
  ProductionPolicyImportReviewListResult,
  ProductionPolicyImportReviewResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountQueueItem,
  VendorPaymentAcknowledgmentDetail,
  VendorPaymentAcknowledgmentSearchResult,
  VendorSessionProjectionHealthTargetDetail,
  VendorSessionProjectionHealthTargetsResponse,
  VendorSessionProjectionHealthSummary
} from "./types";

const firstDraftId = "47000000-0000-0000-0000-000000000008";
const verifiedLocalDraftId = "47000000-0000-0000-0000-000000000009";
const blockedLocalDraftId = "47000000-0000-0000-0000-000000000010";
const fiscalReferenceId = "5f000000-0000-0000-0000-000000000001";

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
      lookupSessionByTicket: vi.fn(),
      getFiscalIssuanceStatus: vi.fn(),
      listFiscalStatusViewAuditReport: vi.fn(),
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
      decideProductionPolicyImportReview: vi.fn(),
      listProductionPolicyImportReviews: vi.fn(async () => ({
        imported: false as const,
        productionPolicyActivationBlocked: true as const,
        items: [],
        totalCount: 0,
        limit: 50,
        offset: 0,
        correlationId: "99000000-0000-0000-0000-000000000099"
      })),
      getProductionPolicyImportReview: vi.fn(),
      searchVendorPaymentAcknowledgments: vi.fn(async () => vendorAcknowledgmentSearchResponse()),
      getVendorPaymentAcknowledgment: vi.fn(async () => vendorAcknowledgmentDetailResponse()),
      listVendorSessionProjectionHealthTargets: vi.fn(async () => vendorProjectionHealthTargetsResponse()),
      getVendorSessionProjectionHealthTarget: vi.fn(async () => vendorProjectionHealthDetailResponse()),
      getVendorSessionProjectionHealthSummary: vi.fn(async () => vendorProjectionHealthSummaryResponse())
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
    expect(screen.getByText(/read-only monitoring pages may still load when their rbac checks allow access/i)).toBeInTheDocument();
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

  it("TicketLookup_LooksUpByTicketOnlyAndShowsConfirmedVendorExitInstruction", async () => {
    const onTicketLookup = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onTicketLookup })}
        initialPath="/operator-console/ticket-lookup"
      />
    );

    await userEvent.type(await screen.findByPlaceholderText("Scan or enter ticket number"), "STAT-OP-SESSION-0001");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));

    expect(await screen.findByText("Vendor confirmation complete.")).toBeInTheDocument();
    expect(screen.getByText("Proceed to ticket exit validator.")).toBeInTheDocument();
    expect(screen.getByText("Unknown")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Session Information" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Tariff Information" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Payment Information" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Vendor Information" })).toBeInTheDocument();
    expect(screen.getByText("STD-001")).toBeInTheDocument();
    expect(screen.getByText("Standard Parking Fee")).toBeInTheDocument();
    await userEvent.click(screen.getByText("Diagnostics"));
    expect(screen.getAllByText("mock-ticket-lookup-confirmed").length).toBeGreaterThan(0);
    expect(screen.queryByRole("textbox", { name: /plate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /paid/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /pay/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /open gate/i })).not.toBeInTheDocument();
    expect(onTicketLookup).toHaveBeenCalledWith({ ticketNumber: "STAT-OP-SESSION-0001" });
  });

  it("TicketLookup_ShowsUnpaidVendorPendingVendorFailedAndNotFoundStates", async () => {
    const { rerender } = render(
      <App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/ticket-lookup" />
    );

    await userEvent.type(await screen.findByPlaceholderText("Scan or enter ticket number"), "STAT-OP-SESSION-0002");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));
    expect((await screen.findAllByText("Vendor confirmation unavailable")).length).toBeGreaterThan(0);

    rerender(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/ticket-lookup" />);
    await userEvent.clear(await screen.findByPlaceholderText("Scan or enter ticket number"));
    await userEvent.type(screen.getByPlaceholderText("Scan or enter ticket number"), "STAT-OP-SESSION-PENDING");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));
    expect(await screen.findByText("Payment confirmed in ExitPass. Vendor confirmation pending.")).toBeInTheDocument();

    rerender(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/ticket-lookup" />);
    await userEvent.clear(await screen.findByPlaceholderText("Scan or enter ticket number"));
    await userEvent.type(screen.getByPlaceholderText("Scan or enter ticket number"), "STAT-OP-SESSION-VENDOR-FAILED");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));
    expect((await screen.findAllByText("Vendor confirmation failed.")).length).toBeGreaterThan(0);
    expect(screen.getByText("Escalate to supervisor.")).toBeInTheDocument();

    rerender(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/ticket-lookup" />);
    await userEvent.clear(await screen.findByPlaceholderText("Scan or enter ticket number"));
    await userEvent.type(screen.getByPlaceholderText("Scan or enter ticket number"), "MISSING-TICKET");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));
    expect(await screen.findByText("Ticket not found")).toBeInTheDocument();
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

  it("FiscalStatusViewAuditReport_LoadsRouteRowsLabelsAndReadOnlyGuardrail", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ fiscalStatusViewAuditReport: fiscalStatusViewAuditReportResponse() })}
        initialPath="/operator-console/audit/fiscal-status-views"
      />
    );

    expect(await screen.findByRole("heading", { name: "Fiscal status view-audit report" })).toBeInTheDocument();
    expect(screen.getByText("View logs are observational only.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not prove payment.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not prove fiscal issuance.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not authorize exit.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not imply gate action.")).toBeInTheDocument();
    expect((await screen.findAllByText("VIEW_FISCAL_ISSUANCE_STATUS")).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Succeeded").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Denied").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Not found").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Failed safely").length).toBeGreaterThan(0);
  });

  it("FiscalStatusViewAuditReport_FiltersSubmitExpectedQueryAndRenderAllFilterControls", async () => {
    const onFiscalStatusViewAuditReport = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatusViewAuditReport: fiscalStatusViewAuditReportResponse(),
          onFiscalStatusViewAuditReport
        })}
        initialPath="/operator-console/audit/fiscal-status-views"
      />
    );

    expect((await screen.findAllByText("5f000000-0000-0000-0000-000000000101")).length).toBeGreaterThan(0);
    expect(screen.getByLabelText("Date from")).toBeInTheDocument();
    expect(screen.getByLabelText("Date to")).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Site ID"), "77000000-0000-0000-0000-000000000002");
    await userEvent.type(screen.getByLabelText("Site group ID"), "77000000-0000-0000-0000-000000000001");
    await userEvent.type(screen.getByLabelText("Operator/support user ID"), "77000000-0000-0000-0000-000000000010");
    await userEvent.type(screen.getByLabelText("Fiscal issuance reference ID"), "5f000000-0000-0000-0000-000000000104");
    await userEvent.selectOptions(screen.getByLabelText("Result class"), "FAILED_SAFELY");
    await userEvent.type(screen.getByLabelText("Correlation ID"), "6b000000-0000-0000-0000-000000000104");
    await userEvent.selectOptions(screen.getByLabelText("Limit"), "50");
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() => {
      expect(onFiscalStatusViewAuditReport).toHaveBeenLastCalledWith(
        expect.objectContaining({
          siteId: "77000000-0000-0000-0000-000000000002",
          siteGroupId: "77000000-0000-0000-0000-000000000001",
          operatorUserId: "77000000-0000-0000-0000-000000000010",
          fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000104",
          resultClass: "FAILED_SAFELY",
          correlationId: "6b000000-0000-0000-0000-000000000104",
          limit: 50,
          offset: 0
        })
      );
    });
  });

  it("FiscalStatusViewAuditReport_PaginationUpdatesOffset", async () => {
    const onFiscalStatusViewAuditReport = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatusViewAuditReport: fiscalStatusViewAuditReportResponse({ totalCount: 60 }),
          onFiscalStatusViewAuditReport
        })}
        initialPath="/operator-console/audit/fiscal-status-views"
      />
    );

    expect((await screen.findAllByText("5f000000-0000-0000-0000-000000000101")).length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole("button", { name: "Next page" }));

    await waitFor(() => {
      expect(onFiscalStatusViewAuditReport).toHaveBeenLastCalledWith(expect.objectContaining({ limit: 25, offset: 25 }));
    });

    await userEvent.click(screen.getByRole("button", { name: "Previous page" }));

    await waitFor(() => {
      expect(onFiscalStatusViewAuditReport).toHaveBeenLastCalledWith(expect.objectContaining({ limit: 25, offset: 0 }));
    });
  });

  it("FiscalStatusViewAuditReport_AccessDeniedHidesRowsAndShowsAccessDenied", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatusViewAuditReportError: { status: "access-denied", message: "Access denied." }
        })}
        initialPath="/operator-console/audit/fiscal-status-views"
      />
    );

    expect(await screen.findByText("Access denied")).toBeInTheDocument();
    expect(screen.getByText("Access denied.")).toBeInTheDocument();
    expect(screen.queryByText("5f000000-0000-0000-0000-000000000101")).not.toBeInTheDocument();
  });

  it("FiscalStatusViewAuditReport_DetailsCollapsedAndNoUnsafeFieldsOrActions", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ fiscalStatusViewAuditReport: fiscalStatusViewAuditReportResponse() })}
        initialPath="/operator-console/audit/fiscal-status-views"
      />
    );

    expect((await screen.findAllByText("5f000000-0000-0000-0000-000000000101")).length).toBeGreaterThan(0);
    const detailSummary = screen.getAllByText("Support/audit details")[0];
    expect(detailSummary.closest("details")).not.toHaveAttribute("open");
    expect(document.body.innerHTML).not.toMatch(
      /raw fiscal request|raw POS Server request|raw POS Server response|secret-token|stack trace|customer PII|payment provider raw payload|statutory evidence payload|raw payment callback|local environment variable|credential/i
    );
    expect(screen.queryByRole("button", { name: /retry/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /readback/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /writeback/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /POS Server action/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /payment confirmation/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /ExitAuthorization/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /gate opening/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /refund/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reversal/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /PDF|HTML|QR/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /raw evidence/i })).not.toBeInTheDocument();
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

  it("FiscalStatusViewer_RecordedWithFiscalDocumentNumberShowsIssuedAndNumber", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ fiscalStatuses: [fiscalStatus()] })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Issued" })).toBeInTheDocument();
    expect(screen.getByText("SI-00000001-UAT")).toBeInTheDocument();
    expect(screen.getByText("Sales Invoice / fiscal document number")).toBeInTheDocument();
    expect(screen.getByText("POS Server fiscal document ID")).toBeInTheDocument();
  });

  it("FiscalStatusViewer_VoidedPosDocumentShowsReadOnlyVoidPosture", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [
            fiscalStatus({
              fiscalDocumentNumber: "SI-00000002-UAT",
              fiscalSequenceValue: 2,
              posServerFiscalDocumentReadStatus: "AVAILABLE",
              posServerFiscalDocumentStatusCodeKey: "voided",
              posServerVoidStatus: "recorded",
              posServerVoidReasonCode: "operator_error",
              posServerVoidedAt: "2026-07-09T16:06:07.499917+00:00"
            })
          ]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Fiscal document voided" })).toBeInTheDocument();
    expect(screen.getByText("SI-00000002-UAT")).toBeInTheDocument();
    expect(screen.getAllByText("POS Server document read status").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Available").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Fiscal document status").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Voided").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Void status").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Recorded").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: /retry|reissue|replacement|payment|gate|refund/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /void fiscal document/i })).not.toBeInTheDocument();
    expect(screen.getByText(/already voided or has a recorded void status/i)).toBeInTheDocument();
  });

  it("FiscalStatusViewer_VoidButtonVisibleOnlyForVoidableDocumentWithPermission", async () => {
    const { rerender } = render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalVoidAuthorized: false,
          fiscalStatuses: [fiscalStatus({ posServerFiscalDocumentReadStatus: "AVAILABLE" })]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByText("Fiscal void permission is required.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /void fiscal document/i })).not.toBeInTheDocument();

    rerender(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalVoidAuthorized: true,
          fiscalStatuses: [fiscalStatus({ posServerFiscalDocumentReadStatus: "AVAILABLE" })]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("button", { name: /void fiscal document/i })).toBeInTheDocument();
  });

  it("FiscalStatusViewer_VoidConfirmationRequiresReasonAndExactPhrase", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [fiscalStatus({ posServerFiscalDocumentReadStatus: "AVAILABLE" })]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);
    await userEvent.click(await screen.findByRole("button", { name: /void fiscal document/i }));

    const submit = screen.getByRole("button", { name: /submit fiscal void request/i });
    expect(submit).toBeDisabled();
    expect(screen.getByText("This does not refund payment.")).toBeInTheDocument();
    expect(screen.getByText("This does not open gate.")).toBeInTheDocument();
    expect(screen.getByText("This does not call HikCentral.")).toBeInTheDocument();
    expect(screen.getByText("This does not create replacement fiscal document.")).toBeInTheDocument();
    expect(screen.getByText("This does not render final BIR receipt/report.")).toBeInTheDocument();
    expect(screen.getAllByText("This only requests fiscal void/cancellation in POS Server.").length).toBeGreaterThan(0);

    await userEvent.type(screen.getByLabelText(/reason text/i), "Incorrect operator entry.");
    await userEvent.type(screen.getByLabelText(/confirmation text/i), "VOID");

    expect(submit).toBeDisabled();

    await userEvent.clear(screen.getByLabelText(/confirmation text/i));
    await userEvent.type(screen.getByLabelText(/confirmation text/i), "VOID FISCAL DOCUMENT");

    expect(submit).toBeEnabled();
  });

  it("FiscalStatusViewer_SuccessfulVoidSubmitDisplaysSuccessAndRefreshesStatus", async () => {
    const onFiscalVoid = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [fiscalStatus({ posServerFiscalDocumentReadStatus: "AVAILABLE" })],
          onFiscalVoid
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);
    await userEvent.click(await screen.findByRole("button", { name: /void fiscal document/i }));
    await userEvent.type(screen.getByLabelText(/reason text/i), "Incorrect operator entry.");
    await userEvent.type(screen.getByLabelText(/confirmation text/i), "VOID FISCAL DOCUMENT");
    await userEvent.click(screen.getByRole("button", { name: /submit fiscal void request/i }));

    expect(await screen.findByRole("heading", { name: "Fiscal void recorded" })).toBeInTheDocument();
    await waitFor(() => expect(onFiscalVoid).toHaveBeenCalledTimes(1));
    expect(onFiscalVoid.mock.calls[0][0]).toEqual(expect.objectContaining({
      fiscalIssuanceReferenceId: fiscalReferenceId,
      reasonCode: "operator_error",
      reasonText: "Incorrect operator entry.",
      confirmationText: "VOID FISCAL DOCUMENT"
    }));
    expect(await screen.findByRole("heading", { name: "Fiscal document voided" })).toBeInTheDocument();
    expect(screen.getAllByText("Recorded").length).toBeGreaterThan(0);
  });

  it("FiscalStatusViewer_ConflictVoidResultDisplaysFailClosedMessageWithoutAutoRetry", async () => {
    const onFiscalVoid = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [fiscalStatus({ posServerFiscalDocumentReadStatus: "AVAILABLE" })],
          fiscalVoidResult: fiscalVoidResult({
            accepted: false,
            status: "pos_server_void_conflict",
            httpStatusCode: 409,
            errors: ["fiscal_document_void_idempotency_conflict"],
            errorPosture: "DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE",
            posServerResultClassification: "conflict"
          }),
          onFiscalVoid
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);
    await userEvent.click(await screen.findByRole("button", { name: /void fiscal document/i }));
    await userEvent.type(screen.getByLabelText(/reason text/i), "Incorrect operator entry.");
    await userEvent.type(screen.getByLabelText(/confirmation text/i), "VOID FISCAL DOCUMENT");
    await userEvent.click(screen.getByRole("button", { name: /submit fiscal void request/i }));

    expect(await screen.findByRole("heading", { name: "Fiscal void failed closed" })).toBeInTheDocument();
    expect(screen.getByText(/do not retry automatically/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /retry/i })).not.toBeInTheDocument();
    expect(onFiscalVoid).toHaveBeenCalledTimes(1);
  });

  it("FiscalStatusViewer_RecordedWithoutFiscalDocumentNumberDoesNotShowIssued", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [fiscalStatus({ fiscalDocumentNumber: undefined })]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Recorded - number not available" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Issued" })).not.toBeInTheDocument();
    expect(screen.queryByText("Sales Invoice / fiscal document number")).not.toBeInTheDocument();
  });

  it("FiscalStatusViewer_ReplayedShowsExistingIssuanceReused", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [
            fiscalStatus({
              fiscalIssuanceState: "FISCAL_ISSUANCE_REPLAYED",
              resultClassification: "IDEMPOTENT_REPLAY"
            })
          ]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Existing issuance reused" })).toBeInTheDocument();
    expect(screen.getByText("Existing fiscal issuance reused. No duplicate issuance was created.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /duplicate/i })).not.toBeInTheDocument();
  });

  it("FiscalStatusViewer_ConflictShowsEscalationAndNoRetryControl", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [
            fiscalStatus({
              fiscalIssuanceState: "FISCAL_ISSUANCE_CONFLICT",
              fiscalDocumentNumber: undefined,
              latestErrorCode: "fiscal_document_idempotency_conflict",
              latestErrorPosture: "DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE"
            })
          ]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Fiscal issuance conflict" })).toBeInTheDocument();
    expect(screen.getByText(/escalate for review; do not retry without corrected request details/i)).toBeInTheDocument();
    expect(screen.getByText("fiscal_document_idempotency_conflict")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /retry/i })).not.toBeInTheDocument();
  });

  it("FiscalStatusViewer_FailedServiceShowsSupportReviewAndNoUnsafeDetail", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [
            fiscalStatus({
              fiscalIssuanceState: "FISCAL_ISSUANCE_FAILED_SERVICE",
              fiscalDocumentNumber: undefined,
              latestErrorCode: "pos_server_unavailable",
              latestErrorPosture: "RETRY_AFTER_SERVICE_RECOVERY",
              latestExceptionReason: "POS_SERVER_UNAVAILABLE"
            })
          ]
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Fiscal service failed" })).toBeInTheDocument();
    expect(screen.getByText(/support review is required/i)).toBeInTheDocument();
    expect(screen.queryByText(/stack trace|raw payload|secret|statutory evidence payload/i)).not.toBeInTheDocument();
  });

  it("FiscalStatusViewer_NotFoundDoesNotImplyPaymentOrExitOutcomes", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatusError: {
            status: "not-found",
            message: "Fiscal issuance reference was not found.",
            errorCode: "FISCAL_ISSUANCE_REFERENCE_NOT_FOUND"
          }
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Fiscal reference not found" })).toBeInTheDocument();
    expect(screen.queryByText(/unpaid|authorized to exit|voided|reversed/i)).not.toBeInTheDocument();
  });

  it("FiscalStatusViewer_UnauthorizedAndForbiddenShowAccessDeniedWithoutFiscalDetail", async () => {
    const { rerender } = render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatusError: {
            status: "access-denied",
            message: "An authenticated Central PMS operator or service identity is required.",
            errorCode: "CENTRAL_PMS_RBAC_UNAUTHENTICATED"
          }
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Access denied" })).toBeInTheDocument();
    expect(screen.queryByText("SI-00000001-UAT")).not.toBeInTheDocument();

    rerender(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatusError: {
            status: "access-denied",
            message: "The caller does not have the required Central PMS permission.",
            errorCode: "CENTRAL_PMS_RBAC_FORBIDDEN"
          }
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByText("The caller does not have the required Central PMS permission.")).toBeInTheDocument();
    expect(screen.queryByText("SI-00000001-UAT")).not.toBeInTheDocument();
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

  it("OperatorConsoleApi_LooksUpTicketThroughOperatorConsoleEndpointOnly", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(ticketLookupResponse()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const result = await client.lookupSessionByTicket({ ticketNumber: "REAL-TICKET-001" });

    expect(result.sessionFound).toBe(true);
    expect(result.ticketNumber).toBe("REAL-TICKET-001");
    expect(result.cardNum).toBe("REAL-TICKET-001");
    expect(result.paymentStatus).toBe("CONFIRMED");
    expect(result.vendorConfirmationStatus).toBe("CONFIRMED");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/ticket-session-summary",
      expect.objectContaining({ method: "POST" })
    );
    const requestOptions = fetchMock.mock.calls[0][1];
    expectOperatorContextHeaders(requestOptions?.headers);
    expect(JSON.parse(requestOptions?.body as string)).toEqual(expect.objectContaining({
      ticketNumber: "REAL-TICKET-001",
      cardNum: null
    }));
    expect(JSON.parse(requestOptions?.body as string)).not.toEqual(expect.objectContaining({
      plateNumber: expect.anything(),
      parkingSessionId: expect.anything(),
      lookupMode: expect.anything()
    }));

    const calledUrls = fetchMock.mock.calls.map((call) => String(call[0])).join("\n");
    expect(calledUrls).not.toMatch(/sessions\/lookup/i);
    expect(calledUrls).not.toMatch(/parkingfee/i);
    expect(calledUrls).not.toMatch(/confirm/i);
    expect(calledUrls).not.toMatch(/hikcentral/i);
    expect(calledUrls).not.toMatch(/gate/i);
  });

  it("OperatorConsoleApi_GetsFiscalStatusThroughFacadeUsingGetHeadersAndEncodedReference", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(fiscalStatus()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const result = await client.getFiscalIssuanceStatus("reference/with space");

    expect(result.fiscalDocumentNumber).toBe("SI-00000001-UAT");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/fiscal-issuance/references/reference%2Fwith%20space",
      expect.objectContaining({ headers: expect.any(Object) })
    );

    const requestOptions = fetchMock.mock.calls[0][1];
    expect(requestOptions?.method).toBeUndefined();
    expect(requestOptions?.body).toBeUndefined();
    expectOperatorContextHeaders(requestOptions?.headers);
  });

  it("OperatorConsoleApi_VoidsFiscalStatusThroughFacadeWithEncodedReference", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(fiscalVoidResult()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const result = await client.voidFiscalIssuanceReference({
      fiscalIssuanceReferenceId: "reference/with space",
      operatorActionRequestId: "88000000-0000-0000-0000-000000000001",
      reasonCode: "operator_error",
      reasonText: "Incorrect operator entry.",
      confirmationText: "VOID FISCAL DOCUMENT",
      correlationId: "88000000-0000-0000-0000-000000000099"
    });

    expect(result.status).toBe("pos_server_void_recorded");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/fiscal-issuance/references/reference%2Fwith%20space/void",
      expect.objectContaining({ method: "POST" })
    );

    const requestOptions = fetchMock.mock.calls[0][1];
    expectOperatorContextHeaders(requestOptions?.headers);
    expect(requestOptions?.headers).toEqual(expect.objectContaining({ "Content-Type": "application/json" }));
    expect(JSON.parse(requestOptions?.body as string)).toEqual({
      operatorActionRequestId: "88000000-0000-0000-0000-000000000001",
      reasonCode: "operator_error",
      reasonText: "Incorrect operator entry.",
      confirmationText: "VOID FISCAL DOCUMENT",
      correlationId: "88000000-0000-0000-0000-000000000099"
    });
    expect(String(fetchMock.mock.calls[0][0])).not.toContain("/internal/");
  });

  it("OperatorConsoleApi_LoadsFiscalStatusViewAuditReportWithFiltersAndEncodedReference", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(fiscalStatusViewAuditReportResponse()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const report = await client.listFiscalStatusViewAuditReport({
      from: "2026-07-01T00:00:00+08:00",
      to: "2026-07-09T23:59:59+08:00",
      siteId: "77000000-0000-0000-0000-000000000002",
      siteGroupId: "77000000-0000-0000-0000-000000000001",
      operatorUserId: "77000000-0000-0000-0000-000000000010",
      fiscalIssuanceReferenceId: "reference/with space",
      resultClass: "NOT_FOUND",
      correlationId: "6b000000-0000-0000-0000-000000000104",
      limit: 50,
      offset: 25
    });

    expect(report.items[0].actionCode).toBe("VIEW_FISCAL_ISSUANCE_STATUS");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/v1/ops/operator-console/audit/fiscal-status-views?"),
      expect.any(Object)
    );
    const calledUrl = new URL(String(fetchMock.mock.calls[0][0]));
    expect(calledUrl.searchParams.get("from")).toBe("2026-07-01T00:00:00+08:00");
    expect(calledUrl.searchParams.get("to")).toBe("2026-07-09T23:59:59+08:00");
    expect(calledUrl.searchParams.get("siteId")).toBe("77000000-0000-0000-0000-000000000002");
    expect(calledUrl.searchParams.get("siteGroupId")).toBe("77000000-0000-0000-0000-000000000001");
    expect(calledUrl.searchParams.get("operatorUserId")).toBe("77000000-0000-0000-0000-000000000010");
    expect(calledUrl.searchParams.get("fiscalIssuanceReferenceId")).toBe("reference/with space");
    expect(calledUrl.searchParams.get("resultClass")).toBe("NOT_FOUND");
    expect(calledUrl.searchParams.get("correlationId")).toBe("6b000000-0000-0000-0000-000000000104");
    expect(calledUrl.searchParams.get("limit")).toBe("50");
    expect(calledUrl.searchParams.get("offset")).toBe("25");
    expect(String(fetchMock.mock.calls[0][0])).toContain("fiscalIssuanceReferenceId=reference%2Fwith+space");
    expectOperatorContextHeaders(fetchMock.mock.calls[0][1]?.headers);
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
    expect(screen.getAllByText("DB repo alignment only").length).toBeGreaterThan(0);
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

    expect((await screen.findAllByText("APPROVED_FOR_DB_REPO_ALIGNMENT")).length).toBeGreaterThan(0);
    expect(await screen.findByText("Review decision recorded. No policies were imported or activated.")).toBeInTheDocument();
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

  it("ProductionPolicyImportReview_LoadsPersistedQueueAndDetailAfterReload", async () => {
    const listReviews = vi.fn(async () => productionPolicyReviewListResponse("LEGAL_REVIEW_PENDING"));
    const getReview = vi.fn(async (reviewId: string) => ({
      ...productionPolicyReviewResponse("LEGAL_REVIEW_PENDING"),
      submission: {
        ...productionPolicyReviewResponse("LEGAL_REVIEW_PENDING").submission,
        reviewId
      },
      message: "Review submission loaded. No policies were imported or activated."
    }));

    render(
      <App
        apiClient={{
          ...createMockOperatorConsoleApiClient(),
          listProductionPolicyImportReviews: listReviews,
          getProductionPolicyImportReview: getReview
        }}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    expect(await screen.findByRole("heading", { name: "Review queue" })).toBeInTheDocument();
    expect(await screen.findByText("1 persisted")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Persisted review" })).toBeInTheDocument();
    expect(screen.getAllByText("LEGAL_REVIEW_PENDING").length).toBeGreaterThan(0);
    expect(screen.getByText("Created at")).toBeInTheDocument();
    expect(screen.getByText("Updated at")).toBeInTheDocument();
    expect(screen.getByText("Dry-run total rows")).toBeInTheDocument();
    expect(screen.getByText("Reviewer decisions")).toBeInTheDocument();
    expect(screen.getByText("Decision history")).toBeInTheDocument();
    expect(screen.getAllByText(/DB repo alignment only/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/imported=false/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/productionPolicyActivationBlocked=true/i).length).toBeGreaterThan(0);
    expect(listReviews).toHaveBeenCalledWith(expect.objectContaining({ limit: 50, offset: 0 }));
    expect(getReview).toHaveBeenCalledWith("99000000-0000-0000-0000-000000000001");
  });

  it("ProductionPolicyImportReview_RefreshButtonReloadsPersistedQueue", async () => {
    const listReviews = vi.fn(async () => productionPolicyReviewListResponse("LEGAL_REVIEW_PENDING"));

    render(
      <App
        apiClient={{
          ...createMockOperatorConsoleApiClient(),
          listProductionPolicyImportReviews: listReviews
        }}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    expect(await screen.findByText("1 persisted")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Refresh reviews" }));

    await waitFor(() => expect(listReviews).toHaveBeenCalledTimes(2));
  });

  it("ProductionPolicyImportReview_AfterDecisionRefreshesPersistedDetail", async () => {
    const reviewId = "99000000-0000-0000-0000-000000000001";
    const getReview = vi
      .fn()
      .mockResolvedValueOnce(productionPolicyReviewResponse("LEGAL_REVIEW_PENDING"))
      .mockResolvedValueOnce(productionPolicyReviewResponse("APPROVED_FOR_DB_REPO_ALIGNMENT", "APPROVE_DB"));
    const decideReview = vi.fn(async () => productionPolicyReviewResponse("APPROVED_FOR_DB_REPO_ALIGNMENT", "APPROVE_DB"));

    render(
      <App
        apiClient={{
          ...createMockOperatorConsoleApiClient({ empty: true }),
          listProductionPolicyImportReviews: vi.fn(async () => productionPolicyReviewListResponse("LEGAL_REVIEW_PENDING")),
          getProductionPolicyImportReview: getReview,
          decideProductionPolicyImportReview: decideReview
        }}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    await screen.findByRole("heading", { name: "Persisted review" });
    await userEvent.selectOptions(screen.getByLabelText(/reviewer role for approve/i), "DB");
    await userEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect((await screen.findAllByText("APPROVED_FOR_DB_REPO_ALIGNMENT")).length).toBeGreaterThan(0);
    expect(getReview).toHaveBeenLastCalledWith(reviewId);
    expect(decideReview).toHaveBeenCalledWith(expect.objectContaining({ reviewId, action: "APPROVE_DB" }));
    expect(screen.getByText("Review decision recorded. No policies were imported or activated.")).toBeInTheDocument();
    expect(document.body.innerHTML).not.toMatch(/production active<\/dt><dd>true/i);
    expect(document.body.innerHTML).not.toMatch(/activated<\/dt><dd>true/i);
  });

  it("ProductionPolicyImportReview_HidesDecisionControlsWhenOperatorIsUnauthorized", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ productionPolicyReviewDecisionAuthorized: false })}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    expect(await screen.findByRole("heading", { name: "Persisted review" })).toBeInTheDocument();
    expect(screen.getByText(/not authorized to record reviewer decisions/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reject" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/reviewer role for approve/i)).not.toBeInTheDocument();
  });

  it("ProductionPolicyImportReview_RendersAccessDeniedForReviewQueue", async () => {
    const listReviews = vi.fn(async () => {
      throw {
        status: "access-denied",
        message: "The operator is not authorized to list production policy import reviews.",
        errorCode: "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_FORBIDDEN"
      };
    });

    render(
      <App
        apiClient={{
          ...createMockOperatorConsoleApiClient(),
          listProductionPolicyImportReviews: listReviews
        }}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    expect(await screen.findByText("Access denied")).toBeInTheDocument();
    expect(screen.getByText("The operator is not authorized to list production policy import reviews.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
  });

  it("ProductionPolicyImportReview_AuthorizedReviewerSeesDecisionControls", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ productionPolicyReviewDecisionAuthorized: true })}
        initialPath="/operator-console/production-policy-import-review"
      />
    );

    expect(await screen.findByRole("heading", { name: "Persisted review" })).toBeInTheDocument();
    expect(screen.getByLabelText(/reviewer role for approve/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reject" })).toBeInTheDocument();
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

  it("OperatorConsoleApi_ProductionPolicyReviewListAndDetailUseExpectedUrls", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(productionPolicyReviewListResponse("LEGAL_REVIEW_PENDING")))
      .mockResolvedValueOnce(jsonResponse(productionPolicyReviewResponse("LEGAL_REVIEW_PENDING")));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const list = await client.listProductionPolicyImportReviews({ status: "LEGAL_REVIEW_PENDING", limit: 25, offset: 0 });
    const detail = await client.getProductionPolicyImportReview(list.items[0].submission.reviewId);

    expect(detail.submission.status).toBe("LEGAL_REVIEW_PENDING");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(
        /^http:\/\/central-pms\.test\/v1\/ops\/operator-console\/statutory-discounts\/policies\/import\/reviews\?/
      ),
      expect.objectContaining({ headers: expect.any(Object) })
    );
    expect(String(fetchMock.mock.calls[0][0])).toContain("status=LEGAL_REVIEW_PENDING");
    expect(String(fetchMock.mock.calls[0][0])).toContain("limit=25");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(
        `/v1/ops/operator-console/statutory-discounts/policies/import/reviews/${list.items[0].submission.reviewId}`
      ),
      expect.objectContaining({ headers: expect.any(Object) })
    );
    expectOperatorContextHeaders(fetchMock.mock.calls[0][1]?.headers);
    expectOperatorContextHeaders(fetchMock.mock.calls[1][1]?.headers);

    const calledUrls = fetchMock.mock.calls.map((call) => String(call[0])).join("\n");
    expect(calledUrls).not.toMatch(/activate/i);
    expect(calledUrls).not.toMatch(/execution/i);
    expect(calledUrls).not.toMatch(/execute/i);
  });

  it("VendorPaymentAcknowledgments_RendersRouteAndSearches", async () => {
    const onSearch = vi.fn();

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onVendorPaymentAcknowledgmentSearch: onSearch })}
        initialPath="/operator-console/vendor-acknowledgments"
      />
    );

    expect(await screen.findByRole("heading", { name: "Vendor payment acknowledgments" })).toBeInTheDocument();
    expect(await screen.findByText("VENDOR-TICKET-001")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh" })).toBeInTheDocument();
    expect(screen.getByText("Vendor PMS acknowledgment is not ExitPass payment finality.")).toBeInTheDocument();
    expect(onSearch).toHaveBeenCalledWith(expect.objectContaining({ pageIndex: 0, pageSize: 25 }));
  });

  it("VendorPaymentAcknowledgments_AppliesStatusAndRetryDueFilters", async () => {
    const onSearch = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onVendorPaymentAcknowledgmentSearch: onSearch })}
        initialPath="/operator-console/vendor-acknowledgments"
      />
    );

    await screen.findByText("VENDOR-TICKET-001");
    onSearch.mockClear();
    await userEvent.selectOptions(screen.getByLabelText("Status"), "FAILED");
    await userEvent.click(screen.getByLabelText("Next retry due only"));
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() =>
      expect(onSearch).toHaveBeenCalledWith(expect.objectContaining({
        acknowledgmentStatus: "FAILED",
        nextRetryDueOnly: true
      }))
    );
  });

  it("VendorPaymentAcknowledgments_FetchesDetailWhenRowIsSelected", async () => {
    const onDetail = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onVendorPaymentAcknowledgmentDetail: onDetail })}
        initialPath="/operator-console/vendor-acknowledgments"
      />
    );

    await screen.findByText("VENDOR-TICKET-001");
    await userEvent.click((await screen.findAllByRole("button", { name: "View details" }))[0]);

    await waitFor(() => expect(onDetail).toHaveBeenCalledWith("88000000-0000-0000-0000-000000000001"));
    expect(await screen.findByRole("heading", { name: "88000000-0000-0000-0000-000000000001" })).toBeInTheDocument();
    expect(screen.getByText("VENDOR_PAYMENT_ACKNOWLEDGMENT_RETRY_DUE")).toBeInTheDocument();
  });

  it("VendorPaymentAcknowledgments_DoesNotExposeMutationControls", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath="/operator-console/vendor-acknowledgments"
      />
    );

    expect(await screen.findByText("VENDOR-TICKET-001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^retry$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^confirm$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark confirmed/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^cancel$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /open gate/i })).not.toBeInTheDocument();
    expect(document.body.innerHTML).not.toMatch(/appsecret|raw payload/i);
  });

  it("VendorSessionProjectionHealth_RendersSummaryTargetsWarningsAndDetail", async () => {
    const onTargets = vi.fn();
    const onSummary = vi.fn();
    const onDetail = vi.fn();

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          onVendorSessionProjectionHealthTargets: onTargets,
          onVendorSessionProjectionHealthSummary: onSummary,
          onVendorSessionProjectionHealthTargetDetail: onDetail
        })}
        initialPath="/operator-console/vendor-session-projections/health"
      />
    );

    expect(await screen.findByRole("heading", { name: "HikCentral Projection Health" })).toBeInTheDocument();
    expect(screen.getByText("Projection data is continuity visibility only.")).toBeInTheDocument();
    expect(screen.getByText(/this page uses read-only projection-health rbac/i)).toBeInTheDocument();
    expect(await screen.findByText(/Degraded resolve fallback is currently enabled/i)).toBeInTheDocument();
    expect(await screen.findByText(/One or more projection targets are stale or failing/i)).toBeInTheDocument();
    expect(await screen.findByText("TEST SITE")).toBeInTheDocument();
    expect(await screen.findByText("STALE SITE")).toBeInTheDocument();
    expect(screen.getAllByText("Stale").length).toBeGreaterThan(0);
    expect(screen.getByText("HIKCENTRAL_UNAVAILABLE")).toBeInTheDocument();
    expect(screen.getByText("Active projections")).toBeInTheDocument();
    expect(screen.getByText("Exited projections")).toBeInTheDocument();

    await userEvent.click((await screen.findAllByRole("button", { name: "View details" }))[0]);

    await waitFor(() => expect(onDetail).toHaveBeenCalledWith("abe7da56-1198-4d51-901f-87e8fb7cd40d"));
    expect(await screen.findByText("3519278781100")).toBeInTheDocument();
    expect(screen.getByText("Limited safe fields")).toBeInTheDocument();
    expect(screen.getAllByText("Max projection age minutes").length).toBeGreaterThan(0);
    expect(onTargets).toHaveBeenCalled();
    expect(onSummary).toHaveBeenCalled();
  });

  it("VendorSessionProjectionHealth_RendersWhenControlledActionReadinessIsBlocked", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ readiness: blockedReadiness() })}
        initialPath="/operator-console/vendor-session-projections/health"
      />
    );

    expect(await screen.findByRole("heading", { name: "HikCentral Projection Health" })).toBeInTheDocument();
    expect(await screen.findByText(/read-only monitoring pages may still load when their rbac checks allow access/i)).toBeInTheDocument();
    expect(await screen.findByText("TEST SITE")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /sync now/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^enable$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^disable$/i })).not.toBeInTheDocument();
  });

  it("VendorSessionProjectionHealth_DoesNotExposeMutationControlsOrSecrets", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath="/operator-console/vendor-session-projections/health"
      />
    );

    expect(await screen.findByRole("heading", { name: "HikCentral Projection Health" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /sync now/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^enable$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^disable$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /fallback/i })).not.toBeInTheDocument();
    expect(document.body.innerHTML).not.toMatch(/appsecret|appkey|secret-value|raw payload json|credential reference/i);
  });

  it("OperatorConsoleApi_VendorSessionProjectionHealthEndpointsUseExpectedUrls", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(vendorProjectionHealthTargetsResponse()))
      .mockResolvedValueOnce(jsonResponse(vendorProjectionHealthDetailResponse()))
      .mockResolvedValueOnce(jsonResponse(vendorProjectionHealthSummaryResponse()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const targets = await client.listVendorSessionProjectionHealthTargets();
    const detail = await client.getVendorSessionProjectionHealthTarget(targets.targets[0].projectionSyncTargetId);
    const summary = await client.getVendorSessionProjectionHealthSummary();

    expect(detail.latestProjectedRecords[0].cardNum).toBe("3519278781100");
    expect(summary.staleTargets).toBe(1);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/vendor-session-projections/targets",
      expect.objectContaining({ headers: expect.any(Object) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/vendor-session-projections/targets/abe7da56-1198-4d51-901f-87e8fb7cd40d",
      expect.objectContaining({ headers: expect.any(Object) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/vendor-session-projections/summary",
      expect.objectContaining({ headers: expect.any(Object) })
    );

    const calledUrls = fetchMock.mock.calls.map((call) => String(call[0])).join("\n");
    expect(calledUrls).not.toMatch(/sync|enable|disable/i);
    expectOperatorContextHeaders(fetchMock.mock.calls[0][1]?.headers);
  });

  it("OperatorConsoleApi_VendorPaymentAcknowledgmentEndpointsUseExpectedUrls", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(vendorAcknowledgmentSearchResponse()))
      .mockResolvedValueOnce(jsonResponse(vendorAcknowledgmentDetailResponse()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const search = await client.searchVendorPaymentAcknowledgments({
      acknowledgmentStatus: "RETRY_PENDING",
      vendorSystemCode: "HIKCENTRAL",
      ticketNumber: "VENDOR-TICKET-001",
      cardNum: "VENDOR-CARD-001",
      nextRetryDueOnly: true,
      pageIndex: 1,
      pageSize: 10
    });
    const detail = await client.getVendorPaymentAcknowledgment(search.items[0].vendorPaymentAcknowledgmentId);

    expect(search.items[0].acknowledgmentStatus).toBe("RETRY_PENDING");
    expect(detail.diagnostics[0].code).toBe("VENDOR_PAYMENT_ACKNOWLEDGMENT_RETRY_DUE");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/vendor-payment-acknowledgments/search",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      `http://central-pms.test/v1/ops/vendor-payment-acknowledgments/${search.items[0].vendorPaymentAcknowledgmentId}`,
      expect.objectContaining({ headers: expect.any(Object) })
    );

    const requestOptions = fetchMock.mock.calls[0][1];
    expectOperatorContextHeaders(requestOptions?.headers);
    expect(requestOptions?.headers).toEqual(expect.objectContaining({ "Content-Type": "application/json" }));
    expect(JSON.parse(requestOptions?.body as string)).toEqual(expect.objectContaining({
      acknowledgmentStatus: "RETRY_PENDING",
      vendorSystemCode: "HIKCENTRAL",
      ticketNumber: "VENDOR-TICKET-001",
      cardNum: "VENDOR-CARD-001",
      nextRetryDueOnly: true,
      pageIndex: 1,
      pageSize: 10
    }));

    const calledUrls = fetchMock.mock.calls.map((call) => String(call[0])).join("\n");
    expect(calledUrls).not.toMatch(/parkingfee/i);
    expect(calledUrls).not.toMatch(/confirm/i);
    expect(calledUrls).not.toMatch(/hikcentral/i);
    expect(calledUrls).not.toMatch(/gate/i);
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

async function lookupFiscalStatus(referenceId: string) {
  await userEvent.clear(await screen.findByLabelText(/fiscal issuance reference id/i));
  await userEvent.type(screen.getByLabelText(/fiscal issuance reference id/i), referenceId);
  await userEvent.click(screen.getByRole("button", { name: "View status" }));
}

function fiscalStatusViewAuditReportResponse(
  overrides: Partial<FiscalStatusViewAuditReportResponse> = {}
): FiscalStatusViewAuditReportResponse {
  const response: FiscalStatusViewAuditReportResponse = {
    items: [
      {
        actionLogEntryId: "79000000-0000-0000-0000-000000000101",
        actionTimestamp: "2026-07-09T08:30:00+08:00",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "SUCCEEDED",
        operatorUserId: "77000000-0000-0000-0000-000000000010",
        siteId: "77000000-0000-0000-0000-000000000002",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000101",
        correlationId: "6b000000-0000-0000-0000-000000000101",
        sourceModule: "FiscalStatusViewer"
      },
      {
        actionLogEntryId: "79000000-0000-0000-0000-000000000102",
        actionTimestamp: "2026-07-09T08:20:00+08:00",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "DENIED",
        operatorUserId: "77000000-0000-0000-0000-000000000011",
        siteId: "77000000-0000-0000-0000-000000000002",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000102",
        correlationId: "6b000000-0000-0000-0000-000000000102",
        safeDenialOrErrorPosture: "Operator Console fiscal status view access was denied.",
        sourceModule: "FiscalStatusViewer"
      },
      {
        actionLogEntryId: "79000000-0000-0000-0000-000000000103",
        actionTimestamp: "2026-07-09T08:10:00+08:00",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "NOT_FOUND",
        operatorUserId: "77000000-0000-0000-0000-000000000012",
        siteId: "77000000-0000-0000-0000-000000000003",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000103",
        correlationId: "6b000000-0000-0000-0000-000000000103",
        safeDenialOrErrorPosture: "Fiscal issuance reference was not found.",
        sourceModule: "FiscalStatusViewer"
      },
      {
        actionLogEntryId: "79000000-0000-0000-0000-000000000104",
        actionTimestamp: "2026-07-09T08:00:00+08:00",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "FAILED_SAFELY",
        operatorUserId: "77000000-0000-0000-0000-000000000013",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000104",
        correlationId: "6b000000-0000-0000-0000-000000000104",
        safeDenialOrErrorPosture: "Fiscal status view failed safely.",
        sourceModule: "FiscalStatusViewer"
      }
    ],
    totalCount: 4,
    limit: 25,
    offset: 0,
    correlationId: "6b000000-0000-0000-0000-000000000199"
  };

  return { ...response, ...overrides };
}

function fiscalStatus(overrides: Partial<FiscalIssuanceStatus> = {}): FiscalIssuanceStatus {
  return {
    fiscalIssuanceReferenceId: fiscalReferenceId,
    fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
    resultClassification: "NEWLY_CREATED",
    fiscalIssuanceEvidenceStatus: "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
    fiscalNumberAssignmentState: "ASSIGNED",
    upstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001",
    paymentConfirmationId: "5f000000-0000-0000-0000-000000000009",
    paymentAttemptId: "5f000000-0000-0000-0000-000000000010",
    parkingSessionId: "5f000000-0000-0000-0000-000000000011",
    siteId: "5f000000-0000-0000-0000-000000000005",
    sitePosServerId: "5f000000-0000-0000-0000-000000000012",
    sitePosServerRef: "DEV-POS-SERVER-ATC-001",
    fiscalDocumentTypeCodeId: "5f000000-0000-0000-0000-000000000013",
    fiscalDocumentTypeCodeKey: "sales_invoice",
    posServerFiscalDocumentId: "5f000000-0000-0000-0000-000000000014",
    fiscalDocumentNumber: "SI-00000001-UAT",
    fiscalIdentityId: "5f000000-0000-0000-0000-000000000015",
    fiscalSequencePolicyId: "5f000000-0000-0000-0000-000000000016",
    fiscalSequenceValue: 1,
    fiscalSeries: "UAT-SI",
    fiscalNumberPrefixText: "SI-",
    fiscalNumberSuffixText: "-UAT",
    fiscalNumberAssignedAt: "2026-07-08T08:00:00Z",
    fiscalNumberAssignedByRef: "pos-server",
    semanticRequestHashValue: "hash-value",
    semanticRequestHashVersion: "sha256:v1",
    semanticRequestHashStatus: "AVAILABLE",
    semanticRequestHashAlgorithm: "SHA-256",
    semanticRequestHashSourceFactCount: 24,
    firstRecordedAt: "2026-07-08T08:00:00Z",
    lastUpdatedAt: "2026-07-08T08:00:00Z",
    correlationId: "5f000000-0000-0000-0000-000000000008",
    ...overrides
  };
}

function fiscalVoidResult(overrides: Partial<FiscalIssuanceVoidResult> = {}): FiscalIssuanceVoidResult {
  return {
    accessAllowed: true,
    accessDecision: "ALLOWED",
    accessDenialReasons: [],
    accessPersisted: true,
    accepted: true,
    status: "pos_server_void_recorded",
    httpStatusCode: 200,
    errors: [],
    fiscalIssuanceReferenceId: fiscalReferenceId,
    posServerFiscalDocumentId: "5f000000-0000-0000-0000-000000000014",
    fiscalDocumentNumber: "SI-00000001-UAT",
    fiscalSequenceValue: 1,
    fiscalDocumentStatusPosture: "voided",
    voidStatus: "recorded",
    voidReasonCode: "operator_error",
    voidedAt: "2026-07-10T00:00:00Z",
    posServerResultClassification: "newly_voided",
    correlationId: "5f000000-0000-0000-0000-000000000008",
    errorPosture: undefined,
    newFiscalNumberAllocated: false,
    paymentFinalityChanged: false,
    exitAuthorizationIssued: false,
    gateBehaviorTriggered: false,
    refundOrReversalCreated: false,
    hikCentralCalled: false,
    paymentProviderCalled: false,
    renderingGenerated: false,
    replacementFiscalDocumentCreated: false,
    fiscalSequenceChangedByCentralPms: false,
    idempotentReplay: false,
    ...overrides
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

function productionPolicyReviewListResponse(status: string): ProductionPolicyImportReviewListResult {
  const review = productionPolicyReviewResponse(status);
  return {
    imported: false,
    productionPolicyActivationBlocked: true,
    items: [
      {
        imported: false,
        productionPolicyActivationBlocked: true,
        submission: review.submission,
        findings: review.findings
      }
    ],
    totalCount: 1,
    limit: 50,
    offset: 0,
    correlationId: review.correlationId
  };
}

function ticketLookupResponse(): OperatorTicketLookupResult {
  return {
    sessionFound: true,
    ticketNumber: "REAL-TICKET-001",
    cardNum: "REAL-TICKET-001",
    plateLicense: "Unknown",
    parkingInTime: "2026-06-01T08:00:00+08:00",
    parkingDurationSeconds: 3600,
    feeMinorUnits: 12000,
    currencyCode: "PHP",
    feeRuleType: "STANDARD",
    feeRuleIndexCode: "STD-REAL",
    feeRuleName: "Standard Fee",
    paymentAttemptStatus: "CONFIRMED",
    paymentStatus: "CONFIRMED",
    paymentConfirmationStatus: "RECORDED",
    vendorSystemCode: "HIKCENTRAL",
    vendorConfirmationCode: "CONFIRMED",
    vendorConfirmationStatus: "CONFIRMED",
    vendorConfirmationTimestamp: "2026-06-01T09:00:00+08:00",
    vendorMessage: "Accepted",
    diagnostics: ["summary-only"],
    correlationId: "77000000-0000-0000-0000-000000000092"
  };
}

function vendorAcknowledgmentSearchResponse(): VendorPaymentAcknowledgmentSearchResult {
  const detail = vendorAcknowledgmentDetailResponse();
  return {
    items: [
      {
        vendorPaymentAcknowledgmentId: detail.vendorPaymentAcknowledgmentId,
        paymentAttemptId: detail.paymentAttemptId,
        paymentConfirmationId: detail.paymentConfirmationId,
        parkingSessionId: detail.parkingSessionId,
        vendorSystemCode: detail.vendorSystemCode,
        vendorSessionRef: detail.vendorSessionRef,
        ticketNumber: detail.ticketNumber,
        cardNum: detail.cardNum,
        acknowledgmentStatus: detail.acknowledgmentStatus,
        statusBucket: detail.statusBucket,
        vendorCode: detail.vendorCode,
        vendorMessage: detail.vendorMessage,
        requestFeeMinorUnits: detail.requestFeeMinorUnits,
        requestCurrencyCode: detail.requestCurrencyCode,
        confirmedFeeMinorUnits: detail.confirmedFeeMinorUnits,
        vendorConfirmedAt: detail.vendorConfirmedAt,
        attemptCount: detail.attemptCount,
        lastAttemptedAt: detail.lastAttemptedAt,
        nextRetryAt: detail.nextRetryAt,
        correlationId: detail.correlationId,
        createdAt: detail.createdAt,
        updatedAt: detail.updatedAt
      }
    ],
    statusBuckets: {
      pending: 0,
      retryPending: 1,
      failed: 0,
      confirmed: 0,
      skippedDisabled: 0,
      cancelled: 0
    },
    pageIndex: 0,
    pageSize: 25,
    hasMore: false
  };
}

function vendorProjectionHealthTargetsResponse(): VendorSessionProjectionHealthTargetsResponse {
  return {
    config: vendorProjectionHealthConfig(),
    targets: vendorProjectionHealthTargets()
  };
}

function vendorProjectionHealthSummaryResponse(): VendorSessionProjectionHealthSummary {
  const targets = vendorProjectionHealthTargets();
  return {
    totalTargets: targets.length,
    enabledTargets: 2,
    disabledTargets: 1,
    healthyTargets: 1,
    degradedTargets: 0,
    failingTargets: 1,
    unknownTargets: 0,
    staleTargets: 1,
    targetsWithLastFailure: 1,
    latestSuccessfulProjectionSyncAt: "2026-06-22T02:05:00Z",
    totalActiveProjections: 16,
    totalExitedProjections: 8,
    config: vendorProjectionHealthConfig()
  };
}

function vendorProjectionHealthDetailResponse(): VendorSessionProjectionHealthTargetDetail {
  return {
    target: vendorProjectionHealthTargets()[0],
    config: vendorProjectionHealthConfig(),
    latestProjectedRecords: [
      {
        vendorSessionProjectionId: "b1000000-0000-0000-0000-000000000001",
        vendorRecordGuid: "5BF30C478FE44C0D8432E549AF9FE0F7",
        cardNum: "3519278781100",
        plateLicense: null,
        enterTime: "2026-06-16T09:30:04Z",
        exitTime: null,
        projectionStatus: "ACTIVE",
        lastRefreshedAt: "2026-06-22T02:05:00Z",
        sourceEventAt: "2026-06-16T09:30:04Z",
        correlationId: "b2000000-0000-0000-0000-000000000001"
      }
    ]
  };
}

function vendorProjectionHealthConfig() {
  return {
    schedulerEnabled: true,
    degradedResolveFallbackEnabled: true,
    maxProjectionAgeMinutes: 1440,
    maxParallelSiteJobs: 4,
    schedulerScanIntervalSeconds: 60
  };
}

function vendorProjectionHealthTargets() {
  return [
    {
      projectionSyncTargetId: "abe7da56-1198-4d51-901f-87e8fb7cd40d",
      siteId: "c9000000-0000-0000-0000-000000000001",
      siteGroupId: "ce000000-0000-0000-0000-000000000001",
      vendorSystemId: "31bde78a-5dfc-45c3-a1f3-e48abaf90927",
      parkingLotIndexCode: "1",
      parkingLotName: "TEST SITE",
      enabledFlag: true,
      healthStatus: "HEALTHY",
      lastAttemptAt: "2026-06-22T02:05:00Z",
      lastSuccessAt: "2026-06-22T02:05:00Z",
      lastFailureAt: null,
      failureCount: 0,
      lastErrorCode: null,
      lastErrorMessage: null,
      pollIntervalSeconds: 60,
      lookbackWindowMinutes: 10080,
      pageSize: 50,
      latestProjectionLastRefreshedAt: "2026-06-22T02:05:00Z",
      freshnessAgeSeconds: 120,
      isStale: false,
      totalProjectionCount: 19,
      activeProjectionCount: 12,
      exitedProjectionCount: 7,
      cardNumProjectionCount: 15,
      plateLicenseProjectionCount: 2
    },
    {
      projectionSyncTargetId: "abe7da56-1198-4d51-901f-87e8fb7cd41d",
      siteId: "c9000000-0000-0000-0000-000000000002",
      siteGroupId: "ce000000-0000-0000-0000-000000000001",
      vendorSystemId: "31bde78a-5dfc-45c3-a1f3-e48abaf90927",
      parkingLotIndexCode: "2",
      parkingLotName: "STALE SITE",
      enabledFlag: true,
      healthStatus: "FAILING",
      lastAttemptAt: "2026-06-22T01:30:00Z",
      lastSuccessAt: "2026-06-20T01:30:00Z",
      lastFailureAt: "2026-06-22T01:30:00Z",
      failureCount: 3,
      lastErrorCode: "HIKCENTRAL_UNAVAILABLE",
      lastErrorMessage: "HikCentral connection timed out.",
      pollIntervalSeconds: 60,
      lookbackWindowMinutes: 1440,
      pageSize: 50,
      latestProjectionLastRefreshedAt: "2026-06-20T01:30:00Z",
      freshnessAgeSeconds: 172800,
      isStale: true,
      totalProjectionCount: 5,
      activeProjectionCount: 4,
      exitedProjectionCount: 1,
      cardNumProjectionCount: 3,
      plateLicenseProjectionCount: 1
    },
    {
      projectionSyncTargetId: "abe7da56-1198-4d51-901f-87e8fb7cd42d",
      siteId: "c9000000-0000-0000-0000-000000000003",
      siteGroupId: "ce000000-0000-0000-0000-000000000001",
      vendorSystemId: "31bde78a-5dfc-45c3-a1f3-e48abaf90927",
      parkingLotIndexCode: "3",
      parkingLotName: "DISABLED SITE",
      enabledFlag: false,
      healthStatus: "DISABLED",
      lastAttemptAt: null,
      lastSuccessAt: null,
      lastFailureAt: null,
      failureCount: 0,
      lastErrorCode: null,
      lastErrorMessage: null,
      pollIntervalSeconds: 300,
      lookbackWindowMinutes: 1440,
      pageSize: 25,
      latestProjectionLastRefreshedAt: null,
      freshnessAgeSeconds: null,
      isStale: false,
      totalProjectionCount: 0,
      activeProjectionCount: 0,
      exitedProjectionCount: 0,
      cardNumProjectionCount: 0,
      plateLicenseProjectionCount: 0
    }
  ];
}

function vendorAcknowledgmentDetailResponse(): VendorPaymentAcknowledgmentDetail {
  return {
    vendorPaymentAcknowledgmentId: "88000000-0000-0000-0000-000000000001",
    paymentAttemptId: "28000000-0000-0000-0000-000000000001",
    paymentConfirmationId: "38000000-0000-0000-0000-000000000001",
    parkingSessionId: "25000000-0000-0000-0000-000000000001",
    vendorSystemCode: "HIKCENTRAL",
    vendorSessionRef: "HC-SESSION-001",
    ticketNumber: "VENDOR-TICKET-001",
    cardNum: "VENDOR-CARD-001",
    acknowledgmentStatus: "RETRY_PENDING",
    statusBucket: "retry_pending",
    vendorCode: "TEMPORARY_FAILURE",
    vendorMessage: "Vendor acknowledgment retry is pending.",
    requestFeeMinorUnits: 12000,
    requestCurrencyCode: "PHP",
    confirmedFeeMinorUnits: undefined,
    vendorConfirmedAt: undefined,
    attemptCount: 2,
    lastAttemptedAt: "2026-06-18T09:10:00+08:00",
    nextRetryAt: "2026-06-18T09:12:00+08:00",
    correlationId: "88000000-0000-0000-0000-000000000099",
    createdAt: "2026-06-18T09:00:00+08:00",
    updatedAt: "2026-06-18T09:15:00+08:00",
    diagnostics: [
      {
        code: "VENDOR_PAYMENT_ACKNOWLEDGMENT_RETRY_DUE",
        message: "Retry-pending acknowledgment is due for dispatcher pickup.",
        source: "central-pms.vendor-payment-acknowledgments",
        retryable: true,
        correlationId: "88000000-0000-0000-0000-000000000099"
      }
    ]
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
    "X-ExitPass-User-Id": "77000000-0000-0000-0000-000000000010",
    "X-ExitPass-Permissions": expect.stringContaining("operator-console.policy-import-review.view-own"),
    "X-Operator-Device-Binding-Id": "77000000-0000-0000-0000-000000000030",
    "X-Operator-Shift-Id": "77000000-0000-0000-0000-000000000050"
  }));
  expect((headers as Record<string, string>)["X-ExitPass-Permissions"]).toContain("fiscal-issuance.status.read");
}
