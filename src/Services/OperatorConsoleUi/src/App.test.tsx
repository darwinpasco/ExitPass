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
  FiscalVoidActionAuditReportResponse,
  FiscalStatusViewAuditReportResponse,
  FiscalIssuanceStatus,
  OperatorTicketLookupResult,
  ProductionPolicyImportDryRunResult,
  ProductionPolicyImportReviewListResult,
  ProductionPolicyImportReviewResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountGoverningPolicy,
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
      lookupFiscalIssuanceStatus: vi.fn(),
      voidFiscalIssuanceReference: vi.fn(),
      listFiscalVoidActionAuditReport: vi.fn(),
      listFiscalStatusViewAuditReport: vi.fn(),
      listAuditReport: vi.fn(),
      createStatutoryDiscountDraft: vi.fn(),
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

    expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
    expect(screen.queryByText(firstDraftId)).not.toBeInTheDocument();
    expect(screen.getByText("Location eligibility")).toBeInTheDocument();
    expect(screen.queryByText("Policy context")).not.toBeInTheDocument();
  });

  it("TicketLookup_CanStartMetadataOnlyStatutoryDiscountDraftFromEligibleSession", async () => {
    const onDraftCreate = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          ticketLookupResults: [
            {
              sessionFound: true,
              accessAllowed: true,
              sessionEligible: true,
              parkingSessionId: "23100000-0000-0000-0000-000000000003",
              siteId: "77000000-0000-0000-0000-000000000002",
              siteGroupId: "77000000-0000-0000-0000-000000000001",
              ticketNumber: "E2E-231-SESSION-001",
              cardNum: "E2E-231-SESSION-001",
              plateLicense: "UAT 231",
              parkingInTime: "2026-07-12T08:00:00+08:00",
              feeMinorUnits: 12500,
              currencyCode: "PHP",
              paymentStatus: "Not Started",
              correlationId: "77000000-0000-0000-0000-000000000092"
            }
          ],
          drafts: [],
          onDraftCreate
        })}
        initialPath="/operator-console/ticket-lookup"
      />
    );

    await userEvent.type(screen.getByPlaceholderText("Scan or enter HikCentral ticket number"), "E2E-231-SESSION-001");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));

    expect(await screen.findByRole("heading", { name: "Start statutory discount review" })).toBeInTheDocument();
    expect(screen.getByText("Current payable minor units")).toBeInTheDocument();
    expect(screen.getByText("12500")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Start statutory discount review" })).toBeInTheDocument();

    await userEvent.click(
      screen.getByLabelText(/Operator confirms the entitlement information was reviewed/i)
    );
    await userEvent.click(screen.getByRole("button", { name: "Create review draft" }));

    await waitFor(() =>
      expect(onDraftCreate).toHaveBeenCalledWith(expect.objectContaining({
        parkingSessionId: "23100000-0000-0000-0000-000000000003",
        entitlementType: "SENIOR_CITIZEN",
        evidenceCaptureRequested: true,
        operatorAttestation: true
      }))
    );
    expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
    expect(screen.getAllByText("E2E-231-SESSION-001").length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_RendersOperationalEligibilityChecklistWithoutAuditMetadata", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
    expect(screen.getAllByText("Parking discount").length).toBeGreaterThan(0);
    expect(screen.getByText("Location eligibility")).toBeInTheDocument();
    expect(screen.getAllByText("Confirmed").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Valid Senior Citizen ID").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Proof of residency").length).toBeGreaterThan(0);
    expect(screen.queryByText("PARANAQUE_SC_OPERATIONAL")).not.toBeInTheDocument();
    expect(screen.queryByText("PH-137604000")).not.toBeInTheDocument();
    expect(screen.queryByText("8a000000")).not.toBeInTheDocument();
    expect(screen.queryByText("VERIFIED_ACTIVE_OPERATIONAL")).not.toBeInTheDocument();
    expect(screen.queryByText("ACTIVE_FOR_TRANSACTION_USE")).not.toBeInTheDocument();
    expect(screen.queryByText(/automatically unverified/i)).not.toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_RendersRequiredEvidenceAndResidencyWithoutOrdinanceMetadata", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { level: 2, name: "PWD Parking Privilege" })).toBeInTheDocument();
    expect(screen.getAllByText("Parking discount").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Valid PWD ID").length).toBeGreaterThan(0);
    expect(screen.queryByText("QC Ordinance 2026-04")).not.toBeInTheDocument();
    expect(screen.queryByText("QC_PWD_PARKING_2026")).not.toBeInTheDocument();
    expect(screen.queryByText("READY_WITH_MANUAL_REVIEW")).not.toBeInTheDocument();
    expect(screen.getAllByText(/required documents are missing or need review/i).length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_RendersBlockedUnverifiedLocalPolicyState", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${blockedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
    expect(screen.getAllByText("Blocked").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/location eligibility record is incomplete/i).length).toBeGreaterThan(0);
    expect(screen.queryByText("LOCAL_POLICY_BLOCKED")).not.toBeInTheDocument();
    expect(screen.queryByText("CONFIGURED_BUT_UNVERIFIED")).not.toBeInTheDocument();
    expect(screen.getAllByText("Blocked").length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_RendersSandboxOnlyPolicyReadinessWarning", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [sandboxOnlyDraft()] })}
        initialPath="/operator-console/statutory-discounts/47000000-0000-0000-0000-000000000099"
      />
    );

    expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
    expect(screen.getAllByText("Blocked").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/location eligibility record is incomplete/i).length).toBeGreaterThan(0);
    expect(screen.queryByText("SANDBOX_ONLY")).not.toBeInTheDocument();
    expect(screen.queryByText(/sandbox\/test policies are not production-ready/i)).not.toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_ApproveActionCallsDecisionEndpointAndRefreshes", async () => {
    const onDecision = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onDecision })}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText(/I verified the required beneficiary documents/i));
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

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Reject" }));
    expect(screen.getByText("Select a rejection reason.")).toBeInTheDocument();
    expect(onDecision).not.toHaveBeenCalled();

    await userEvent.selectOptions(screen.getByLabelText(/reason for rejection/i), "ID_NOT_VALID");
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

  it("StatutoryDiscountDetail_HidesDecisionControlsForRequesterViewingOwnValidation", async () => {
    const onDecision = vi.fn();
    const ownDraft = decisionEligibleDraft({
      draftId: "47000000-0000-0000-0000-000000000201",
      requestedBy: "77000000-0000-0000-0000-000000000010"
    });

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [ownDraft], onDecision })}
        initialPath={`/operator-console/statutory-discounts/${ownDraft.draftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    expect(screen.getByText("You cannot approve or reject your own statutory discount request.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reject" })).not.toBeInTheDocument();
    expect(onDecision).not.toHaveBeenCalled();
  });

  it("StatutoryDiscountDetail_HidesDecisionControlsWhenOperatorLacksDecisionPermission", async () => {
    const onDecision = vi.fn();
    const draft = decisionEligibleDraft({ draftId: "47000000-0000-0000-0000-000000000202" });

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          drafts: [draft],
          onDecision,
          statutoryDiscountDecisionAuthorized: false
        })}
        initialPath={`/operator-console/statutory-discounts/${draft.draftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    expect(screen.getByText("Decision requires an authorized reviewer.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reject" })).not.toBeInTheDocument();
    expect(onDecision).not.toHaveBeenCalled();
  });

  it("StatutoryDiscountDetail_ShowsDecisionControlsForAuthorizedReviewerWhenEligible", async () => {
    const draft = decisionEligibleDraft({ draftId: "47000000-0000-0000-0000-000000000203" });

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [draft] })}
        initialPath={`/operator-console/statutory-discounts/${draft.draftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(within(screen.getByLabelText("Decision")).getByText("Blocked")).toBeInTheDocument();
    expect(screen.getByText(/approval requires reviewer attestation/i)).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText(/I verified the required beneficiary documents/i));
    expect(within(screen.getByLabelText("Decision")).getByText("Ready")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeEnabled();
  });

  it("StatutoryDiscountDetail_AllowsRepresentativeTransactionWhenPresenceIsOptionalNotRequiredOrUnspecified", async () => {
    const presenceStatuses = ["OPTIONAL", "NOT_REQUIRED", "UNSPECIFIED"];

    for (const presenceStatus of presenceStatuses) {
      const draft = decisionEligibleDraft({
        draftId: `47000000-0000-0000-0000-0000000003${presenceStatuses.indexOf(presenceStatus)}1`,
        governingPolicy: governingPolicy({
          beneficiaryResidencyScope: "UNRESTRICTED_VALID_ID",
          requiredEvidenceTypes: [
            {
              evidenceType: "SENIOR_CITIZEN_ID",
              requirementStatus: "REQUIRED",
              safeRequirementLabel: "Masked statutory ID reference"
            },
            {
              evidenceType: "BENEFICIARY_PRESENCE",
              requirementStatus: presenceStatus,
              safeRequirementLabel: "Beneficiary presence"
            }
          ]
        })
      });

      const { unmount } = render(
        <App
          apiClient={createMockOperatorConsoleApiClient({ drafts: [draft] })}
          initialPath={`/operator-console/statutory-discounts/${draft.draftId}`}
        />
      );

      expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
      expect(screen.getAllByText("Valid Senior Citizen ID").length).toBeGreaterThan(0);
      expect(screen.queryByText("Beneficiary must be present")).not.toBeInTheDocument();
      await userEvent.click(screen.getByLabelText(/I verified the required beneficiary documents/i));
      expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
      unmount();
    }
  });

  it("StatutoryDiscountDetail_BlocksApprovalForExplicitDriverOrPassengerRequirementsUntilEvidenceIsSatisfied", async () => {
    const driverDraft = decisionEligibleDraft({
      draftId: "47000000-0000-0000-0000-000000000321",
      evidenceRequiredSatisfied: false,
      governingPolicy: governingPolicy({
        beneficiaryResidencyScope: "UNRESTRICTED_VALID_ID",
        requiredEvidenceTypes: [
          {
            evidenceType: "BENEFICIARY_DRIVER",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Beneficiary is the driver"
          }
        ]
      })
    });
    const passengerDraft = decisionEligibleDraft({
      draftId: "47000000-0000-0000-0000-000000000322",
      evidenceRequiredSatisfied: false,
      governingPolicy: governingPolicy({
        beneficiaryResidencyScope: "UNRESTRICTED_VALID_ID",
        requiredEvidenceTypes: [
          {
            evidenceType: "BENEFICIARY_PASSENGER",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Beneficiary is a passenger"
          }
        ]
      })
    });

    const driverRender = render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [driverDraft] })}
        initialPath={`/operator-console/statutory-discounts/${driverDraft.draftId}`}
      />
    );

    await waitFor(() => expect(screen.getAllByText("Beneficiary must be the driver").length).toBeGreaterThan(0));
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(within(screen.getByLabelText("Decision")).getByText("Blocked")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).queryByText("Ready")).not.toBeInTheDocument();
    expect(screen.getByText(/required driver condition is verified/i)).toBeInTheDocument();
    driverRender.unmount();

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [passengerDraft] })}
        initialPath={`/operator-console/statutory-discounts/${passengerDraft.draftId}`}
      />
    );

    await waitFor(() => expect(screen.getAllByText("Beneficiary must be a passenger").length).toBeGreaterThan(0));
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(within(screen.getByLabelText("Decision")).getByText("Blocked")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).queryByText("Ready")).not.toBeInTheDocument();
    expect(screen.getByText(/required passenger condition is verified/i)).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_DisablesApprovalWhenFrozenGoverningPolicyAuthorityIsMissing", async () => {
    const draft = decisionEligibleDraft({
      draftId: "47000000-0000-0000-0000-000000000205",
      governingPolicy: undefined
    });

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [draft] })}
        initialPath={`/operator-console/statutory-discounts/${draft.draftId}`}
      />
    );

    expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getAllByText(/location eligibility record is incomplete/i).length).toBeGreaterThan(0);
    expect(screen.getByText("Parking-location eligibility could not be confirmed.")).toBeInTheDocument();
    expect(screen.getByText("Reject the request or ask support to refresh the parking-location record.")).toBeInTheDocument();
    expect(screen.queryByText("This parking location is eligible for the requested privilege.")).not.toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).getByText("Blocked")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).queryByText("Ready")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reject" })).toBeEnabled();
  });

  it("StatutoryDiscountDetail_DisablesApprovalWhenGoverningPolicyReadbackIsMalformed", async () => {
    const draft = decisionEligibleDraft({
      draftId: "47000000-0000-0000-0000-000000000206",
      governingPolicy: {
        ...governingPolicy(),
        policyCode: ""
      }
    });

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [draft] })}
        initialPath={`/operator-console/statutory-discounts/${draft.draftId}`}
      />
    );

    expect(await screen.findByRole("heading", { level: 2, name: "Senior Citizen Parking Privilege" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getAllByText(/location eligibility record is incomplete/i).length).toBeGreaterThan(0);
    expect(screen.getByText("Parking-location eligibility could not be confirmed.")).toBeInTheDocument();
    expect(screen.queryByText("This parking location is eligible for the requested privilege.")).not.toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).getByText("Blocked")).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_DisablesApprovalWhenBenefitEffectIsUnsupported", async () => {
    const draft = decisionEligibleDraft({
      draftId: "47000000-0000-0000-0000-000000000207",
      governingPolicy: {
        ...governingPolicy(),
        benefitType: "FULL_FEE_EXEMPTION"
      }
    });

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [draft] })}
        initialPath={`/operator-console/statutory-discounts/${draft.draftId}`}
      />
    );

    expect(await screen.findByText("Free parking")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getAllByText(/parking benefit is not supported/i).length).toBeGreaterThan(0);
    expect(within(screen.getByLabelText("Decision")).getByText("Blocked")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).queryByText("Ready")).not.toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_HidesDecisionControlsAfterApprovalAndPayableBasisApplication", async () => {
    const appliedDraft: StatutoryDiscountDraftDetail = {
      ...createApprovedDraft(),
      draftId: "47000000-0000-0000-0000-000000000204",
      payableBasisApplicationStatus: "APPLIED",
      payableBasisApplicationId: "54128dcc-dfd5-4ec4-9377-5759f202269c",
      appliedTariffSnapshotId: "5c2a9ad0-84e0-47fb-9f78-4deaa9990396"
    };

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [appliedDraft] })}
        initialPath={`/operator-console/statutory-discounts/${appliedDraft.draftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    expect(screen.getByText("Parking privilege approved")).toBeInTheDocument();
    expect(screen.getByText(/will be applied when the customer proceeds with payment through WebPay or the Cashier-Assisted Terminal/i)).toBeInTheDocument();
    expect(screen.queryByText("Ready for review")).not.toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).queryByText("Ready")).not.toBeInTheDocument();
    expect(screen.getAllByText("Approved").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reject" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Update parking amount" })).not.toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_ShowsRejectedRequestAsReadOnlyWithoutReadyBadgesOrCanonicalReasonCode", async () => {
    const rejectedDraft: StatutoryDiscountDraftDetail = {
      ...createApprovedDraft(),
      draftId: "47000000-0000-0000-0000-000000000208",
      status: "Rejected",
      decisionReasonCode: "ID_NOT_VALID",
      auditActivity: ["Evidence captured.", "Decision rejected."]
    };

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ drafts: [rejectedDraft] })}
        initialPath={`/operator-console/statutory-discounts/${rejectedDraft.draftId}`}
      />
    );

    expect(await screen.findByText("Parking privilege rejected")).toBeInTheDocument();
    expect(screen.getByText("Document is invalid")).toBeInTheDocument();
    expect(screen.queryByText("Ready for review")).not.toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).queryByText("Ready")).not.toBeInTheDocument();
    expect(screen.getAllByText("Rejected").length).toBeGreaterThan(0);
    expect(screen.queryByText("ID_NOT_VALID")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reject" })).not.toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_ShowsEvidencePanelAndBlocksApprovalWhenEvidenceRequired", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Document review" })).toBeInTheDocument();
    expect(screen.getAllByText("Valid PWD ID").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Missing").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(within(screen.getByLabelText("Decision")).getByText("Blocked")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Decision")).queryByText("Ready")).not.toBeInTheDocument();
    expect(screen.getAllByText(/required documents are missing or need review/i).length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_HidesParkingAmountUpdateBeforeApproval", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Amount update" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Update parking amount" })).not.toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_ApprovesDecisionOnlyAndShowsLaterPaymentApplicationGuidance", async () => {
    const onPayableBasisApply = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onPayableBasisApply })}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText(/I verified the required beneficiary documents/i));
    await userEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect(await screen.findByText("Decision approved.")).toBeInTheDocument();
    expect(await screen.findByText("Parking privilege approved")).toBeInTheDocument();
    expect(screen.getByText(/will be applied when the customer proceeds with payment through WebPay or the Cashier-Assisted Terminal/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Update parking amount" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Amount update" })).not.toBeInTheDocument();
    expect(screen.queryByText("Original tariff snapshot")).not.toBeInTheDocument();
    expect(screen.queryByText("Application ID")).not.toBeInTheDocument();
    expect(onPayableBasisApply).not.toHaveBeenCalled();
  });

  it("StatutoryDiscountDetail_DoesNotExposeAmountApplicationForApprovedDecisionWithoutSnapshot", async () => {
    const onPayableBasisApply = vi.fn();
    const approvedDraftWithoutSnapshot: StatutoryDiscountDraftDetail = {
      ...createApprovedDraft(),
      originalTariffSnapshotId: undefined,
      payableBasisApplicationStatus: undefined,
      payableBasisApplicationId: undefined,
      appliedTariffSnapshotId: undefined
    };

    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          drafts: [approvedDraftWithoutSnapshot],
          onPayableBasisApply
        })}
        initialPath={`/operator-console/statutory-discounts/${approvedDraftWithoutSnapshot.draftId}`}
      />
    );

    expect(await screen.findByText("Parking privilege approved")).toBeInTheDocument();
    expect(screen.getByText(/will be applied when the customer proceeds with payment through WebPay or the Cashier-Assisted Terminal/i)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Amount update" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Update parking amount" })).not.toBeInTheDocument();
    expect(screen.queryByText("Original tariff snapshot")).not.toBeInTheDocument();
    expect(onPayableBasisApply).not.toHaveBeenCalled();
  });

  it("StatutoryDiscountDetail_CapturesEvidenceAndEnablesApprovalAfterRefresh", async () => {
    const onEvidenceCapture = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onEvidenceCapture })}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Document review" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();

    await userEvent.click(await screen.findByLabelText(/document is verified/i));
    await userEvent.click(await screen.findByRole("button", { name: "Mark as verified" }));

    expect(await screen.findByText("Document marked as verified.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByText("Verified").length).toBeGreaterThan(0));
    await userEvent.click(screen.getByLabelText(/I verified the required beneficiary documents/i));
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

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Operator readiness state" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeDisabled();
    expect(await screen.findByRole("button", { name: "Mark as verified" })).toBeDisabled();
    expect(screen.getAllByText(/readiness check is blocking controlled operator console actions/i).length).toBeGreaterThan(0);
  });

  it("OperatorConsoleReadiness_AllowsControlledActionsWhenReadyAndWorkflowAllows", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Operator readiness state" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    await userEvent.click(screen.getByLabelText(/I verified the required beneficiary documents/i));
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeEnabled();
    const markReviewedButton = await screen.findByRole("button", { name: "Mark as verified" });
    expect(markReviewedButton).toBeDisabled();
    await userEvent.click(screen.getByLabelText(/document is verified/i));
    expect(markReviewedButton).toBeEnabled();
  });

  it("TicketLookup_LooksUpByTicketOnlyAndShowsConfirmedVendorExitInstruction", async () => {
    const onTicketLookup = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ onTicketLookup })}
        initialPath="/operator-console/ticket-lookup"
      />
    );

    await userEvent.type(await screen.findByPlaceholderText("Scan or enter HikCentral ticket number"), "STAT-OP-SESSION-0001");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));

    expect(await screen.findByText("Vendor confirmation complete.")).toBeInTheDocument();
    expect(screen.getByText("Proceed to ticket exit validator.")).toBeInTheDocument();
    expect(screen.getByText("Sales Invoice number")).toBeInTheDocument();
    expect(screen.getAllByText("Not available").length).toBeGreaterThan(0);
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

    await userEvent.type(await screen.findByPlaceholderText("Scan or enter HikCentral ticket number"), "STAT-OP-SESSION-0002");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));
    expect((await screen.findAllByText("Vendor confirmation unavailable")).length).toBeGreaterThan(0);

    rerender(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/ticket-lookup" />);
    await userEvent.clear(await screen.findByPlaceholderText("Scan or enter HikCentral ticket number"));
    await userEvent.type(screen.getByPlaceholderText("Scan or enter HikCentral ticket number"), "STAT-OP-SESSION-PENDING");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));
    expect(await screen.findByText("Payment confirmed in ExitPass. Vendor confirmation pending.")).toBeInTheDocument();

    rerender(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/ticket-lookup" />);
    await userEvent.clear(await screen.findByPlaceholderText("Scan or enter HikCentral ticket number"));
    await userEvent.type(screen.getByPlaceholderText("Scan or enter HikCentral ticket number"), "STAT-OP-SESSION-VENDOR-FAILED");
    await userEvent.click(screen.getByRole("button", { name: "Lookup" }));
    expect((await screen.findAllByText("Vendor confirmation failed.")).length).toBeGreaterThan(0);
    expect(screen.getByText("Escalate to supervisor.")).toBeInTheDocument();

    rerender(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/ticket-lookup" />);
    await userEvent.clear(await screen.findByPlaceholderText("Scan or enter HikCentral ticket number"));
    await userEvent.type(screen.getByPlaceholderText("Scan or enter HikCentral ticket number"), "MISSING-TICKET");
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

    expect(await screen.findByRole("heading", { name: "Sales Invoice status view audit report" })).toBeInTheDocument();
    expect(screen.getByText("View logs are observational only.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not prove payment.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not prove fiscal issuance.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not authorize exit.")).toBeInTheDocument();
    expect(screen.getByText("View logs do not imply gate action.")).toBeInTheDocument();
    expect((await screen.findAllByText("Sales Invoice status viewed")).length).toBeGreaterThan(0);
    expect(screen.getByText("SI-OCVOID-0001-UAT")).toBeInTheDocument();
    expect(screen.getByText("Ops Supervisor")).toBeInTheDocument();
    expect(screen.getAllByText("ATC Development Site").length).toBeGreaterThan(0);
    expect(screen.getAllByText("ATC Site Group").length).toBeGreaterThan(0);
    expect(screen.queryByText("VIEW_FISCAL_ISSUANCE_STATUS")).not.toBeInTheDocument();
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

    expect(screen.getByLabelText("Date from")).toBeInTheDocument();
    expect(screen.getByLabelText("Date to")).toBeInTheDocument();
    expect(await screen.findByText("SI-OCVOID-0001-UAT")).toBeInTheDocument();
    expect(screen.queryByLabelText("Site ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Site group ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Operator/support user ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Sales Invoice reference ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Correlation ID")).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText("Result class"), "FAILED_SAFELY");
    await userEvent.selectOptions(screen.getByLabelText("Limit"), "50");
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() => {
      expect(onFiscalStatusViewAuditReport).toHaveBeenLastCalledWith(
        expect.objectContaining({
          resultClass: "FAILED_SAFELY",
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

    expect(await screen.findByText("SI-OCVOID-0001-UAT")).toBeInTheDocument();
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
    expect(screen.queryByText("SI-OCVOID-0001-UAT")).not.toBeInTheDocument();
  });

  it("FiscalStatusViewAuditReport_DetailsCollapsedAndNoUnsafeFieldsOrActions", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ fiscalStatusViewAuditReport: fiscalStatusViewAuditReportResponse() })}
        initialPath="/operator-console/audit/fiscal-status-views"
      />
    );

    expect(await screen.findByText("SI-OCVOID-0001-UAT")).toBeInTheDocument();
    const detailToggle = screen.getAllByRole("button", { name: "View support/audit details" })[0];
    expect(detailToggle).toHaveAttribute("aria-expanded", "false");
    await userEvent.click(detailToggle);
    expect(screen.getByText("Read-only metadata for the selected Sales Invoice status view row.")).toBeInTheDocument();
    expect(screen.getAllByText("Sales Invoice status viewed").length).toBeGreaterThan(0);
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

  it("FiscalVoidActionAuditReport_LoadsRouteRowsFiltersAndReadOnlyGuardrail", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ fiscalVoidActionAuditReport: fiscalVoidActionAuditReportResponse() })}
        initialPath="/operator-console/audit/fiscal-void-actions"
      />
    );

    expect(await screen.findByRole("heading", { name: "Sales Invoice void action audit review" })).toBeInTheDocument();
    expect(screen.getByText("This page reviews Sales Invoice void action-log metadata only.")).toBeInTheDocument();
    expect(screen.getByText(/It does not perform Sales Invoice void/)).toBeInTheDocument();
    expect(screen.getByLabelText("Sales Invoice number")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("e.g. SI-OCVOID-0001-UAT")).toBeInTheDocument();
    expect(screen.getByLabelText("Result class")).toBeInTheDocument();
    expect(screen.queryByLabelText("Sales Invoice reference ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Correlation ID")).not.toBeInTheDocument();
    expect(screen.queryByText("Fiscal document number")).not.toBeInTheDocument();
    expect(screen.queryByText("Fiscal issuance reference ID")).not.toBeInTheDocument();
    expect(await screen.findAllByText("SI-OCVOID-0001-UAT")).not.toHaveLength(0);
    expect(screen.getByText("TICKET-ATC-001")).toBeInTheDocument();
    expect(screen.getByText("Ops Supervisor")).toBeInTheDocument();

    const detailsToggle = screen.getByRole("button", { name: "View support/audit details" });
    expect(detailsToggle).toHaveAttribute("aria-expanded", "false");

    await userEvent.click(detailsToggle);

    expect(screen.getByRole("button", { name: "Hide support/audit details" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("Read-only metadata for the selected Sales Invoice void action row.")).toBeInTheDocument();
    expect(screen.getAllByText("operator_error")).not.toHaveLength(0);
    expect(screen.getByText("No unsafe side effects recorded")).toBeInTheDocument();
    expect(screen.getByText("Sales Invoice void")).toBeInTheDocument();
    expect(screen.getAllByText("ATC Development Site").length).toBeGreaterThan(0);
    expect(screen.getAllByText("ATC Site Group").length).toBeGreaterThan(0);
    expect(screen.queryByText("VOID_FISCAL_DOCUMENT")).not.toBeInTheDocument();
    expect(screen.queryByText("7f4a7d36-2e6e-4f2c-aad6-2d98e8e1b501")).not.toBeInTheDocument();
    expect(screen.queryByText("3cddbc8e-28f8-49d2-93cf-b4a28a947501")).not.toBeInTheDocument();
    expect(screen.queryByText("b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df")).not.toBeInTheDocument();
  });

  it("FiscalVoidActionAuditReport_FiltersSubmitExpectedQuery", async () => {
    const onFiscalVoidActionAuditReport = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalVoidActionAuditReport: fiscalVoidActionAuditReportResponse(),
          onFiscalVoidActionAuditReport
        })}
        initialPath="/operator-console/audit/fiscal-void-actions"
      />
    );

    expect(await screen.findAllByText("SI-OCVOID-0001-UAT")).not.toHaveLength(0);
    expect(screen.queryByLabelText("Site ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Site group ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Operator/user ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Sales Invoice reference ID")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Correlation ID")).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Sales Invoice number"), "SI-OCVOID-0001-UAT");
    await userEvent.selectOptions(screen.getByLabelText("Result class"), "CONFLICT");
    await userEvent.selectOptions(screen.getByLabelText("Limit"), "50");
    await userEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() => {
      expect(onFiscalVoidActionAuditReport).toHaveBeenLastCalledWith(
        expect.objectContaining({
          fiscalDocumentNumber: "SI-OCVOID-0001-UAT",
          resultClass: "CONFLICT",
          limit: 50,
          offset: 0
        })
      );
    });
  });

  it("FiscalVoidActionAuditReport_EmptyAccessDeniedAndNoUnsafeActionButtons", async () => {
    const { rerender } = render(
      <App apiClient={createMockOperatorConsoleApiClient({ empty: true })} initialPath="/operator-console/audit/fiscal-void-actions" />
    );

    expect(await screen.findByText("No Sales Invoice void action rows")).toBeInTheDocument();

    rerender(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalVoidActionAuditReportError: { status: "access-denied", message: "Access denied." }
        })}
        initialPath="/operator-console/audit/fiscal-void-actions"
      />
    );

    expect(await screen.findByText("Access denied")).toBeInTheDocument();
    expect(screen.getByText("Access denied.")).toBeInTheDocument();
    expect(screen.queryByText("SI-OCVOID-0001-UAT")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /void Sales Invoice/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /refund/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /gate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /HikCentral/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /replacement/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /PDF|HTML|QR/i })).not.toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_ShowsCompactDocumentReviewWithoutCaptureInternals", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Document review" })).toBeInTheDocument();
    expect(screen.getAllByText("Valid PWD ID").length).toBeGreaterThan(0);
    expect(screen.getByRole("checkbox", { name: /document is verified/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Mark as verified" })).toBeDisabled();
    expect(screen.queryByText(/metadata-only evidence capture/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/do not upload or enter raw id numbers/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/masked id reference/i)).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("****1234")).not.toBeInTheDocument();
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
    expect(screen.getByText("Sales Invoice number")).toBeInTheDocument();
    expect(screen.getByText("POS Server Sales Invoice ID")).toBeInTheDocument();
  });

  it("FiscalStatusViewer_UsesOperatorFriendlyLookupCopyAndControlledActionWording", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({ fiscalStatuses: [fiscalStatus()] })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    expect(screen.getByRole("heading", { name: "Fiscal issuance status and controlled fiscal actions" })).toBeInTheDocument();
    expect(screen.getByLabelText(/search by Sales Invoice number or reference ID/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/SI-00000001-UAT or Sales Invoice reference ID/i)).toBeInTheDocument();
    expect(screen.getByText(/Operators can search by Sales Invoice number/i)).toBeInTheDocument();
    expect(screen.getByText("Controlled actions")).toBeInTheDocument();
    expect(screen.queryByText("Read-only fiscal issuance status by Central PMS fiscal issuance reference.")).not.toBeInTheDocument();
    expect(screen.queryByText("Read-only")).not.toBeInTheDocument();
  });

  it("FiscalStatusViewer_SearchesByFiscalDocumentNumberAndDisplaysResolvedReferenceInDetails", async () => {
    const onFiscalStatusLookup = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [fiscalStatus({ fiscalDocumentNumber: "SI-OCVOID-0001-UAT" })],
          onFiscalStatusLookup
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus("SI-OCVOID-0001-UAT");

    await screen.findByText("Requested reference");
    expect(screen.getAllByText("SI-OCVOID-0001-UAT").length).toBeGreaterThan(0);
    expect(onFiscalStatusLookup).toHaveBeenCalledWith("SI-OCVOID-0001-UAT");
    expect(screen.getByText("Requested reference")).toBeInTheDocument();
    expect(screen.getByText("Sales Invoice reference ID")).toBeInTheDocument();
    expect(screen.getByText(fiscalReferenceId)).toBeInTheDocument();
  });

  it("FiscalStatusViewer_GuidLookupStillWorksThroughFriendlyLookup", async () => {
    const onFiscalStatusLookup = vi.fn();
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient({
          fiscalStatuses: [fiscalStatus()],
          onFiscalStatusLookup
        })}
        initialPath="/operator-console/fiscal-issuance-status"
      />
    );

    await lookupFiscalStatus(fiscalReferenceId);

    expect(await screen.findByRole("heading", { name: "Issued" })).toBeInTheDocument();
    expect(onFiscalStatusLookup).toHaveBeenCalledWith(fiscalReferenceId);
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

    expect(await screen.findByRole("heading", { name: "Sales Invoice voided" })).toBeInTheDocument();
    expect(screen.getByText("SI-00000002-UAT")).toBeInTheDocument();
    expect(screen.getAllByText("POS Server document read status").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Available").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Sales Invoice status").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Voided").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Void status").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Recorded").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: /retry|reissue|replacement|payment|gate|refund/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /void Sales Invoice/i })).not.toBeInTheDocument();
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

    expect(await screen.findByText("Sales Invoice void permission is required.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /void Sales Invoice/i })).not.toBeInTheDocument();

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

    expect(await screen.findByRole("button", { name: /void Sales Invoice/i })).toBeInTheDocument();
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
    await userEvent.click(await screen.findByRole("button", { name: /void Sales Invoice/i }));

    const submit = screen.getByRole("button", { name: /submit Sales Invoice void request/i });
    expect(submit).toBeDisabled();
    expect(screen.getByText("This does not refund payment.")).toBeInTheDocument();
    expect(screen.getByText("This does not open gate.")).toBeInTheDocument();
    expect(screen.getByText("This does not call HikCentral.")).toBeInTheDocument();
    expect(screen.getByText("This does not create a replacement Sales Invoice.")).toBeInTheDocument();
    expect(screen.getByText("This does not render final BIR receipt/report.")).toBeInTheDocument();
    expect(screen.getAllByText("This only requests Sales Invoice void/cancellation in POS Server.").length).toBeGreaterThan(0);

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
    await userEvent.click(await screen.findByRole("button", { name: /void Sales Invoice/i }));
    await userEvent.type(screen.getByLabelText(/reason text/i), "Incorrect operator entry.");
    await userEvent.type(screen.getByLabelText(/confirmation text/i), "VOID FISCAL DOCUMENT");
    await userEvent.click(screen.getByRole("button", { name: /submit Sales Invoice void request/i }));

    expect(await screen.findByRole("heading", { name: "Sales Invoice void recorded" })).toBeInTheDocument();
    await waitFor(() => expect(onFiscalVoid).toHaveBeenCalledTimes(1));
    expect(onFiscalVoid.mock.calls[0][0]).toEqual(expect.objectContaining({
      fiscalIssuanceReferenceId: fiscalReferenceId,
      reasonCode: "operator_error",
      reasonText: "Incorrect operator entry.",
      confirmationText: "VOID FISCAL DOCUMENT"
    }));
    expect(await screen.findByRole("heading", { name: "Sales Invoice voided" })).toBeInTheDocument();
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
    await userEvent.click(await screen.findByRole("button", { name: /void Sales Invoice/i }));
    await userEvent.type(screen.getByLabelText(/reason text/i), "Incorrect operator entry.");
    await userEvent.type(screen.getByLabelText(/confirmation text/i), "VOID FISCAL DOCUMENT");
    await userEvent.click(screen.getByRole("button", { name: /submit Sales Invoice void request/i }));

    expect(await screen.findByRole("heading", { name: "Sales Invoice void failed closed" })).toBeInTheDocument();
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
    expect(screen.queryByText("Sales Invoice number")).not.toBeInTheDocument();
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

    expect(await screen.findByRole("heading", { name: "Sales Invoice reference not found" })).toBeInTheDocument();
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
    expect(result.feeMinorUnits).toBe(12500);
    expect(result.paymentStatus).toBe("CONFIRMED");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/sessions/lookup",
      expect.objectContaining({ method: "POST" })
    );
    const requestOptions = fetchMock.mock.calls[0][1];
    expectOperatorContextHeaders(requestOptions?.headers);
    expect(JSON.parse(requestOptions?.body as string)).toEqual(expect.objectContaining({
      ticketReference: "REAL-TICKET-001",
      parkingSessionId: null,
      lookupMode: "TICKET_REFERENCE"
    }));

    const calledUrls = fetchMock.mock.calls.map((call) => String(call[0])).join("\n");
    expect(calledUrls).not.toMatch(/parkingfee/i);
    expect(calledUrls).not.toMatch(/confirm/i);
    expect(calledUrls).not.toMatch(/hikcentral/i);
    expect(calledUrls).not.toMatch(/gate/i);
  });

  it("OperatorConsoleApi_CreatesStatutoryDiscountDraftThroughSingularEndpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse({
      accessAllowed: true,
      accessDecision: "ALLOWED",
      accessDenialReasons: [],
      draftAccepted: true,
      draftPersisted: true,
      draftId: firstDraftId,
      parkingSessionId: "23100000-0000-0000-0000-000000000003",
      entitlementType: "SENIOR_CITIZEN",
      validationStatus: "REQUESTED",
      evidenceCaptureRequired: true,
      evidenceRequired: true,
      evidenceReferenceCreated: true,
      reusedExistingDraft: false,
      correlationId: "0c000000-0000-0000-0000-000000000001"
    }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const result = await client.createStatutoryDiscountDraft({
      parkingSessionId: "23100000-0000-0000-0000-000000000003",
      ticketReference: "E2E-231-SESSION-001",
      entitlementType: "SENIOR_CITIZEN",
      idDocumentType: "SENIOR_CITIZEN_ID",
      issuingAuthority: "OSCA",
      maskedIdReference: "SC-UAT-****-0001",
      evidenceCaptureRequested: true,
      operatorAttestation: true,
      attestationNotes: "Manual UAT smoke."
    });

    expect(result.accepted).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/statutory-discounts/draft",
      expect.objectContaining({ method: "POST" })
    );
    const requestOptions = fetchMock.mock.calls[0][1];
    expectOperatorContextHeaders(requestOptions?.headers);
    expect(JSON.parse(requestOptions?.body as string)).toEqual(expect.objectContaining({
      parkingSessionId: "23100000-0000-0000-0000-000000000003",
      ticketReference: "E2E-231-SESSION-001",
      entitlementType: "SENIOR_CITIZEN",
      idDocumentType: "SENIOR_CITIZEN_ID",
      issuingAuthority: "OSCA",
      maskedIdReference: "SC-UAT-****-0001",
      evidenceAccessIntent: "METADATA_ONLY",
      operatorAttestation: true
    }));
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

  it("OperatorConsoleApi_LooksUpFiscalStatusThroughFriendlyLookupEndpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(fiscalStatus()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const result = await client.lookupFiscalIssuanceStatus("SI-OCVOID-0001-UAT");

    expect(result.fiscalDocumentNumber).toBe("SI-00000001-UAT");
    expect(fetchMock).toHaveBeenCalledWith(
      "http://central-pms.test/v1/ops/operator-console/fiscal-issuance/lookup?query=SI-OCVOID-0001-UAT",
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

  it("OperatorConsoleApi_LoadsFiscalVoidActionAuditReportWithFilters", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(fiscalVoidActionAuditReportResponse()));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ baseUrl: "http://central-pms.test" });

    const report = await client.listFiscalVoidActionAuditReport({
      from: "2026-07-01T00:00:00+08:00",
      to: "2026-07-09T23:59:59+08:00",
      siteId: "77000000-0000-0000-0000-000000000002",
      siteGroupId: "77000000-0000-0000-0000-000000000001",
      operatorUserId: "77000000-0000-0000-0000-000000000010",
      fiscalIssuanceReferenceId: "7f4a7d36-2e6e-4f2c-aad6-2d98e8e1b501",
      fiscalDocumentNumber: "SI-OCVOID-0001-UAT",
      resultClass: "CONFLICT",
      correlationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
      limit: 50,
      offset: 25
    });

    expect(report.items[0].actionCode).toBe("VOID_FISCAL_DOCUMENT");
    expect(report.items[0].fiscalDocumentNumber).toBe("SI-OCVOID-0001-UAT");
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/v1/ops/operator-console/audit/fiscal-void-actions?"),
      expect.any(Object)
    );
    const calledUrl = new URL(String(fetchMock.mock.calls[0][0]));
    expect(calledUrl.searchParams.get("fiscalDocumentNumber")).toBe("SI-OCVOID-0001-UAT");
    expect(calledUrl.searchParams.get("resultClass")).toBe("CONFLICT");
    expect(calledUrl.searchParams.get("offset")).toBe("25");
    const requestOptions = fetchMock.mock.calls[0][1];
    expect(requestOptions?.method).toBeUndefined();
    expect(requestOptions?.body).toBeUndefined();
    expectOperatorContextHeaders(requestOptions?.headers);
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

function decisionEligibleDraft(overrides: Partial<StatutoryDiscountDraftDetail> = {}): StatutoryDiscountDraftDetail {
  return {
    ...createApprovedDraft(),
    draftId: "47000000-0000-0000-0000-000000000200",
    parkingSessionId: "25000000-0000-0000-0000-000000000200",
    ticketReference: "STAT-OP-SESSION-0200",
    status: "Requested",
    requestedBy: "77000000-0000-0000-0000-000000000011",
    evidenceCaptured: true,
    evidenceRequiredSatisfied: true,
    evidenceCount: 1,
    latestEvidenceStatus: "CAPTURED",
    payableBasisApplicationStatus: undefined,
    payableBasisApplicationId: undefined,
    appliedTariffSnapshotId: undefined,
    auditActivity: ["Evidence captured.", "Awaiting reviewer decision."],
    ...overrides
  };
}

function governingPolicy(overrides: Partial<StatutoryDiscountGoverningPolicy> = {}): StatutoryDiscountGoverningPolicy {
  return {
    statutoryDiscountPolicyVersionId: "8a000000-0000-0000-0000-000000000101",
    jurisdictionId: "8a000000-0000-0000-0000-000000000102",
    jurisdictionCode: "PH-137604000",
    jurisdictionDisplayName: "Para\u00f1aque City",
    policyCode: "PARANAQUE_SC_OPERATIONAL",
    policyVersion: "v1",
    ordinanceNumber: undefined,
    ordinanceTitle: undefined,
    sourceVerificationStatus: "VERIFIED_ACTIVE_OPERATIONAL",
    transactionPublicationStatus: "ACTIVE_FOR_TRANSACTION_USE",
    detailedRuleVerificationStatus: "PARTIALLY_VERIFIED",
    parkingServiceApplicability: "COVERED",
    benefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
    beneficiaryResidencyScope: "RESIDENT_ONLY",
    officialSourceAvailable: false,
    ordinanceTextAvailable: false,
    ordinanceNumberAvailable: false,
    effectiveFrom: "2026-01-01T00:00:00+08:00",
    effectiveTo: undefined,
    requiredEvidenceTypes: [
      {
        evidenceType: "SENIOR_CITIZEN_ID",
        requirementStatus: "REQUIRED",
        safeRequirementLabel: "Masked statutory ID reference"
      },
      {
        evidenceType: "RESIDENCY_EVIDENCE",
        requirementStatus: "REQUIRED",
        safeRequirementLabel: "Residency evidence"
      }
    ],
    legalApprovabilityReason: "Central PMS resolved an active verified operational local parking policy before review creation.",
    ...overrides
  };
}

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
    governingPolicy: undefined,
    auditActivity: ["Sandbox policy detected.", "Decision blocked pending production policy readiness."]
  };
}

function createApprovedDraft(): StatutoryDiscountDraftDetail {
  return {
    ...sandboxOnlyDraft(),
    draftId: "47000000-0000-0000-0000-000000000088",
    parkingSessionId: "25000000-0000-0000-0000-000000000088",
    ticketReference: "STAT-OP-SESSION-0088",
    status: "Approved",
    evidenceCaptured: true,
    evidenceRequiredSatisfied: true,
    evidenceCount: 1,
    latestEvidenceStatus: "CAPTURED",
    originalAmountMinorUnits: 12500,
    payableAmountMinorUnits: 12500,
    finalPayableAmountMinorUnits: undefined,
    statutoryDiscountAmountMinorUnits: undefined,
    vatAmountMinorUnits: undefined,
    vatExclusiveAmountMinorUnits: undefined,
    currencyCode: "PHP",
    policyContext: {
      ...sandboxOnlyDraft().policyContext,
      title: "Approved Senior Citizen statutory discount",
      policyReadinessClassification: "READY_FOR_CONTROLLED_UAT",
      policyReadinessReason: "CONTROLLED_UAT",
      operatorMessage: "Controlled UAT policy is ready for manual statutory discount smoke.",
      productionAutoApplicationEligible: false
    },
    governingPolicy: governingPolicy(),
    auditActivity: ["Evidence captured.", "Decision approved."]
  };
}

async function lookupFiscalStatus(referenceId: string) {
  await userEvent.clear(await screen.findByLabelText(/search by Sales Invoice number or reference ID/i));
  await userEvent.type(screen.getByLabelText(/search by Sales Invoice number or reference ID/i), referenceId);
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
        operatorUsername: "ops.supervisor",
        operatorDisplayName: "Ops Supervisor",
        siteId: "77000000-0000-0000-0000-000000000002",
        siteName: "ATC Development Site",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        siteGroupName: "ATC Site Group",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000101",
        fiscalDocumentNumber: "SI-OCVOID-0001-UAT",
        ticketNumber: "TICKET-ATC-001",
        correlationId: "6b000000-0000-0000-0000-000000000101",
        sourceModule: "FiscalStatusViewer"
      },
      {
        actionLogEntryId: "79000000-0000-0000-0000-000000000102",
        actionTimestamp: "2026-07-09T08:20:00+08:00",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "DENIED",
        operatorUserId: "77000000-0000-0000-0000-000000000011",
        operatorUsername: "support.viewer",
        siteId: "77000000-0000-0000-0000-000000000002",
        siteName: "ATC Development Site",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        siteGroupName: "ATC Site Group",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000102",
        fiscalDocumentNumber: "SI-OCVOID-0002-UAT",
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

function fiscalVoidActionAuditReportResponse(
  overrides: Partial<FiscalVoidActionAuditReportResponse> = {}
): FiscalVoidActionAuditReportResponse {
  const response: FiscalVoidActionAuditReportResponse = {
    items: [
      {
        actionLogEntryId: "7a000000-0000-0000-0000-000000000101",
        actionTimestamp: "2026-07-10T06:30:00+08:00",
        actionCode: "VOID_FISCAL_DOCUMENT",
        resultClass: "SUCCEEDED",
        operatorUserId: "77000000-0000-0000-0000-000000000010",
        operatorUsername: "ops.supervisor",
        operatorDisplayName: "Ops Supervisor",
        siteId: "77000000-0000-0000-0000-000000000002",
        siteName: "ATC Development Site",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        siteGroupName: "ATC Site Group",
        fiscalIssuanceReferenceId: "7f4a7d36-2e6e-4f2c-aad6-2d98e8e1b501",
        fiscalDocumentNumber: "SI-OCVOID-0001-UAT",
        ticketNumber: "TICKET-ATC-001",
        posServerFiscalDocumentId: "3cddbc8e-28f8-49d2-93cf-b4a28a947501",
        reasonCode: "operator_error",
        correlationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
        sourceModule: "operator-console-fiscal-issuance-status",
        paymentFinalityChanged: false,
        exitAuthorizationIssued: false,
        gateBehaviorTriggered: false,
        refundOrReversalCreated: false,
        hikCentralCalled: false,
        paymentProviderCalled: false,
        renderingGenerated: false,
        replacementFiscalDocumentCreated: false,
        newFiscalNumberAllocated: false,
        fiscalSequenceChangedByCentralPms: false
      }
    ],
    totalCount: 1,
    limit: 25,
    offset: 0,
    correlationId: "7a000000-0000-0000-0000-000000000199"
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
    accessAllowed: true,
    sessionEligible: true,
    parkingSessionId: "23100000-0000-0000-0000-000000000003",
    siteId: "77000000-0000-0000-0000-000000000002",
    siteGroupId: "77000000-0000-0000-0000-000000000001",
    ticketReference: "REAL-TICKET-001",
    plateNumber: "Unknown",
    entryTime: "2026-06-01T08:00:00+08:00",
    currentPayableAmountMinorUnits: 12500,
    currencyCode: "PHP",
    paymentStatus: "CONFIRMED",
    alerts: [],
    correlationId: "77000000-0000-0000-0000-000000000092"
  } as unknown as OperatorTicketLookupResult;
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
  expect((headers as Record<string, string>)["X-ExitPass-Permissions"]).toContain("fiscal-issuance.void.audit.read");
}
