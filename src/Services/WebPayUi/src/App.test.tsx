import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { formatCustomerSupportReference } from "./customerSafeReference";
import { createStatutoryRecoveryRecord, statutoryRecoveryStorageKey } from "./statutoryRecovery";

vi.mock("@zxing/browser", () => ({
  BrowserQRCodeReader: vi.fn().mockImplementation(() => ({
    decodeFromVideoDevice: vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"))
  }))
}));

const successResponse = {
  paymentAttemptId: "44444444-4444-4444-4444-444444444444",
  parkingSessionId: "55555555-5555-5555-5555-555555555555",
  tariffSnapshotId: "66666666-6666-6666-6666-666666666666",
  siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
  siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
  vendorSystemId: "45a625de-9034-4fb6-b527-0950d384e51f",
  siteGroupName: "WebPay Test Site Group 2026-05-19",
  amountMinorUnits: 12500,
  currency: "PHP",
  siteName: "Mactan Newtown Parking",
  ticketReference: "TICKET-TEST-023",
  plateNumber: "ABC 1234",
  entryTime: "2026-05-18T10:42:00+08:00",
  currentFeeCalculationTime: "2026-05-18T12:57:00+08:00",
  durationParked: "2h 15m",
  tariffName: "Weekend Rate",
  feeValidUntil: "2026-05-18T13:15:00+08:00",
  parkingStatus: "PaymentRequired",
  paymentStatus: "Not Started",
  paymentMethod: "QRPH",
  selectedProviderCode: "PAYMONGO",
  fallbackProviderCode: null,
  routingReason: "PRIMARY_PROVIDER",
  status: "PENDING_PROVIDER",
  handoff: {
    type: "Redirect",
    handoffUrl: "https://payments.test/handoff",
    qrCodeUrl: "https://payments.test/qr.png"
  },
  correlationId: "77777777-7777-7777-7777-777777777777"
};

const activePaymentAttemptConflict = {
  errorCode: "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
  message: "An active payment attempt already exists for parking session '55555555-5555-5555-5555-555555555555'.",
  retryable: false,
  correlationId: "77777777-7777-7777-7777-777777777777"
};

function stubWebPayFetch(options?: {
  resolvePayload?: unknown;
  resolveOk?: boolean;
  resolveStatus?: number;
  intentPayload?: unknown;
  intentOk?: boolean;
  intentStatus?: number;
  receiptPayload?: unknown;
  receiptOk?: boolean;
  receiptStatus?: number;
  statutorySubmitPayload?: unknown;
  statutorySubmitOk?: boolean;
  statutorySubmitStatus?: number;
  statutoryAvailabilityPayload?: unknown;
  statutoryAvailabilityOk?: boolean;
  statutoryAvailabilityStatus?: number;
  statutoryRediscoveryPayload?: unknown;
  statutoryRediscoveryOk?: boolean;
  statutoryRediscoveryStatus?: number;
  statutoryReadPayload?: unknown;
  statutoryReadOk?: boolean;
  statutoryReadStatus?: number;
  statutoryApplyPayload?: unknown;
  statutoryApplyOk?: boolean;
  statutoryApplyStatus?: number;
  statutoryEvidencePayload?: unknown;
  statutoryEvidenceOk?: boolean;
  statutoryEvidenceStatus?: number;
}) {
  const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => {
    const isResolve = url.includes("/v1/webpay/parking-session");
    const isReceipt = url.includes("/v1/webpay/payment-attempts/") && url.includes("/receipt-presentation");
    const isStatutoryAvailability = url.endsWith("/v1/webpay/statutory-discounts/availability");
    const isStatutoryRediscovery = url.endsWith("/v1/webpay/statutory-discounts/pending-lifecycle/rediscover");
    const isStatutorySubmit = url.endsWith("/v1/webpay/statutory-discounts/decisions") && _init?.method === "POST";
    const isStatutoryApply =
      url.includes("/v1/webpay/statutory-discounts/decisions/") &&
      url.endsWith("/apply-payable-basis") &&
      _init?.method === "POST";
    const isStatutoryRead = url.includes("/v1/webpay/statutory-discounts/decisions/") && _init?.method === "GET";
    const isStatutoryEvidence = url.includes("/v1/webpay/statutory-discounts/evidence/");
    return {
      ok: isResolve
        ? options?.resolveOk ?? true
        : isReceipt
        ? options?.receiptOk ?? true
        : isStatutoryAvailability
          ? options?.statutoryAvailabilityOk ?? true
        : isStatutoryRediscovery
          ? options?.statutoryRediscoveryOk ?? true
        : isStatutorySubmit
          ? options?.statutorySubmitOk ?? true
        : isStatutoryApply
          ? options?.statutoryApplyOk ?? true
        : isStatutoryRead
          ? options?.statutoryReadOk ?? true
        : isStatutoryEvidence
          ? options?.statutoryEvidenceOk ?? true
        : options?.intentOk ?? true,
      status: isResolve
        ? options?.resolveStatus ?? 200
        : isReceipt
        ? options?.receiptStatus ?? 200
        : isStatutoryAvailability
          ? options?.statutoryAvailabilityStatus ?? 200
        : isStatutoryRediscovery
          ? options?.statutoryRediscoveryStatus ?? 200
        : isStatutorySubmit
          ? options?.statutorySubmitStatus ?? 200
        : isStatutoryApply
          ? options?.statutoryApplyStatus ?? 200
        : isStatutoryRead
          ? options?.statutoryReadStatus ?? 200
        : isStatutoryEvidence
          ? options?.statutoryEvidenceStatus ?? 200
        : options?.intentStatus ?? 200,
      json: async () => (
        isResolve
          ? options?.resolvePayload ?? successResponse
          : isReceipt
            ? options?.receiptPayload ?? salesInvoicePresentationResponse
          : isStatutoryAvailability
            ? options?.statutoryAvailabilityPayload ?? statutoryAvailabilityResponse()
          : isStatutoryRediscovery
            ? options?.statutoryRediscoveryPayload ?? statutoryPendingLifecycleRediscoveryResponse({ classification: "NO_ACTIVE_LIFECYCLE" })
          : isStatutorySubmit
            ? options?.statutorySubmitPayload ?? statutoryDecisionResponse()
          : isStatutoryApply
            ? options?.statutoryApplyPayload ?? statutoryDecisionResponse()
          : isStatutoryRead
            ? options?.statutoryReadPayload ?? statutoryDecisionResponse()
          : isStatutoryEvidence
            ? options?.statutoryEvidencePayload ?? statutoryEvidenceResponse()
          : options?.intentPayload ?? successResponse
      )
    };
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

const salesInvoicePresentationResponse = {
  paymentAttemptId: "44444444-4444-4444-4444-444444444444",
  paymentConfirmationId: "11111111-1111-1111-1111-111111111111",
  fiscalIssuanceReferenceId: "22222222-2222-2222-2222-222222222222",
  fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
  posFiscalDocumentId: "33333333-3333-3333-3333-333333333333",
  fiscalDocumentNumber: "SI-20260523-000001",
  fiscalDocumentStatus: "RECORDED",
  receiptAvailabilityState: "AVAILABLE",
  presentationVersion: "digital-sales-invoice-presentation-json-v1",
  templateVersion: "digital-sales-invoice-json-v1",
  contentType: "application/json",
  authoritativePresentation: {
    presentation: {
      documentTitle: "Sales Invoice",
      sections: [
        {
          key: "summary",
          title: "Document Summary",
          rows: [
            { key: "number", label: "Sales Invoice Number", displayValue: "SI-20260523-000001" },
            { key: "total", label: "Total Amount", displayValue: "PHP 125.00" }
          ]
        }
      ]
    }
  },
  createdAt: "2026-05-23T13:00:00+08:00",
  updatedAt: "2026-05-23T13:01:00+08:00",
  correlationId: "77777777-7777-7777-7777-777777777777"
};

const statutoryDecisionCommandId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

function statutoryDecisionResponse(overrides?: Record<string, unknown>) {
  return {
    statutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    statutoryDiscountPayableBasisApplicationCommandId: null,
    statutoryDiscountValidationId: null,
    parkingSessionId: successResponse.parkingSessionId,
    siteId: successResponse.siteId,
    siteGroupId: successResponse.siteGroupId,
    entitlementType: "SENIOR_CITIZEN",
    decisionCommandStatus: "AWAITING_REVIEW",
    decisionResultStatus: "NOT_DECIDED",
    applicationCommandStatus: "NOT_REQUESTED",
    applicationResultClassification: "NOT_REQUESTED",
    payableBasisReady: false,
    payableBasisReadinessStatus: "AWAITING_REVIEW",
    payableBasisReadinessAction: "POLL_READBACK",
    originalTariffSnapshotId: successResponse.tariffSnapshotId,
    appliedTariffSnapshotId: null,
    originalAmountMinorUnits: null,
    vatExclusiveBasisAmountMinorUnits: null,
    vatAmountMinorUnits: null,
    vatTreatment: null,
    statutoryDiscountAmountMinorUnits: null,
    finalPayableAmountMinorUnits: null,
    currency: null,
    retryable: false,
    recoveryClassification: "PENDING",
    recoveryAction: "POLL_READBACK",
    safeErrorCode: null,
    overallResultClassification: "PENDING",
    oneShotComplete: false,
    correlationId: "77777777-7777-7777-7777-777777777777",
    createdAt: "2026-07-27T10:00:00+08:00",
    decidedAt: null,
    appliedAt: null,
    ...overrides
  };
}

function statutoryAvailabilityResponse(overrides?: Record<string, unknown>) {
  return {
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    parkingSessionId: successResponse.parkingSessionId,
    siteId: successResponse.siteId,
    siteGroupId: successResponse.siteGroupId,
    availabilityStatus: "AVAILABLE",
    statutoryParkingBenefitAvailable: true,
    coveredEntitlementTypes: ["SENIOR_CITIZEN", "PWD"],
    requestedEntitlementType: null,
    safeReasonCode: null,
    retryable: false,
    remediationAction: "CONTINUE_WITH_ORDINARY_PAYMENT",
    requiredEvidenceTypes: [],
    correlationId: "77777777-7777-7777-7777-777777777777",
    ...overrides
  };
}

function statutoryPendingLifecycleRediscoveryResponse(overrides?: Record<string, unknown>) {
  return {
    classification: "FOUND",
    statutoryDecisionId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    statutoryDecisionCommandId,
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    entitlementType: "SENIOR_CITIZEN",
    decisionStatus: "AWAITING_REVIEW",
    payableBasisStatus: "AWAITING_REVIEW",
    parkingSessionId: successResponse.parkingSessionId,
    siteId: successResponse.siteId,
    siteGroupId: successResponse.siteGroupId,
    opaqueContinuationReference: "continuation:test:existing",
    opaqueContinuationUrl: "https://pay.example.test/privilege-review/opaque-existing",
    lifecycleState: "PENDING_REVIEW",
    retryable: true,
    correlationId: "77777777-7777-7777-7777-777777777777",
    createdAt: "2026-07-27T10:00:00+08:00",
    updatedAt: "2026-07-27T10:01:00+08:00",
    submittedAt: "2026-07-27T10:00:30+08:00",
    decidedAt: null,
    reviewedAt: null,
    ...overrides
  };
}

function statutoryEvidenceResponse(overrides?: Record<string, unknown>) {
  return {
    classification: "FOUND",
    retryable: false,
    errorCode: null,
    correlationId: "77777777-7777-7777-7777-777777777777",
    evidenceRequired: true,
    evidenceSetReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    evidenceItemReference: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    allowedContentTypes: ["image/jpeg", "image/png"],
    maximumContentLengthBytes: 5_000_000,
    maximumImageWidth: 1920,
    maximumImageHeight: 1080,
    maximumImagePixelCount: 2_073_600,
    requiredDocumentType: "STATUTORY_ID",
    requiredItemRole: "ENTITLEMENT_ID_FRONT",
    lifecycleClassification: "REQUIRED_NOT_STARTED",
    replacementPosture: "REPLACEMENT_ALLOWED",
    readyForReview: false,
    blockingReasonCode: "EVIDENCE_REQUIRED",
    evaluatedAt: "2026-08-05T09:00:00Z",
    ...overrides
  };
}

function routeCalls(fetchMock: ReturnType<typeof vi.fn>, path: string) {
  return fetchMock.mock.calls.filter(([url]) => String(url).includes(path));
}

function firstRouteCall(fetchMock: ReturnType<typeof vi.fn>, path: string) {
  return routeCalls(fetchMock, path)[0];
}

beforeEach(() => {
  vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_GROUP_ID", "11111111-1111-1111-1111-111111111111");
  vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_ID", "22222222-2222-2222-2222-222222222222");
  vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "HIKCENTRAL");
  localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllEnvs();
  localStorage.clear();
  window.history.pushState({}, "", "/");
});

describe("ExitPass WebPay UI", () => {
  async function resolveTicket(ticketReference = "TICKET-001", expectedAmount = "125.00") {
    await userEvent.type(screen.getByLabelText(/ticket reference/i), ticketReference);
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));
    expect((await screen.findAllByText(expectedAmount)).length).toBeGreaterThan(0);
  }

  async function continueToPayment() {
    await userEvent.click(screen.getByRole("button", { name: /continue to payment/i }));
  }

  async function submitBasicStatutoryRequest() {
    await resolveTicket("TICKET-STAT-104");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    await userEvent.type(screen.getByLabelText(/id document type/i), "OSCA");
    await userEvent.type(screen.getByLabelText(/issuing authority/i), "Quezon City");
    await userEvent.type(screen.getByLabelText(/^id reference$/i), "SC00001234");
    await userEvent.click(screen.getByLabelText(/i confirm these entitlement details/i));
    await userEvent.click(screen.getByRole("button", { name: /submit for review/i }));
  }

  it("WebPay_WhenStatutoryFormOpens_ExplainsAutomaticMaskingWithoutManualAsteriskInstructions", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-STAT-104");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));

    expect(screen.getByLabelText(/^id reference$/i)).toBeInTheDocument();
    expect(screen.getByText(/automatically shows only the first 2 and last 4 characters/i)).toBeInTheDocument();
    expect(screen.queryByText(/use a masked reference with asterisks/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenEnterPressedInTicketInput_ResolvesSessionOnly", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-TEST-027{Enter}");

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes("/v1/webpay/parking-session"))).toHaveLength(1);
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/v1/webpay/payment-intents"))).toBe(false);
  });

  it("WebPay_WhenEnterPressedInPlateInput_ResolvesSessionOnly", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /plate/i }));
    await userEvent.type(screen.getByLabelText(/plate number/i), "ABC 1234{Enter}");

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes("/v1/webpay/parking-session"))).toHaveLength(1);
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/v1/webpay/payment-intents"))).toBe(false);
  });

  it("WebPay_WhenInitialContinueClicked_ResolvesSessionBeforePaymentIntent", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-TEST-027");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.getAllByText("125.00").length).toBeGreaterThan(0);
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes("/v1/webpay/parking-session"))).toHaveLength(1);
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/v1/webpay/payment-intents"))).toBe(false);
  });

  it("WebPay_WhenBothEntitlementsAreCovered_ShowsStatutoryRequestAction", async () => {
    stubWebPayFetch({
      statutoryAvailabilityPayload: statutoryAvailabilityResponse({
        coveredEntitlementTypes: ["SENIOR_CITIZEN", "PWD"]
      })
    });

    render(<App />);
    await resolveTicket("TICKET-COVERED-BOTH");

    expect(await screen.findByText("Parking privilege request available")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    const entitlementSelect = screen.getByLabelText(/entitlement type/i);
    expect(entitlementSelect).toHaveTextContent("Senior Citizen");
    expect(entitlementSelect).toHaveTextContent("PWD");
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
  });

  it("WebPay_WhenOnlySeniorCitizenIsCovered_HidesPwdOption", async () => {
    stubWebPayFetch({
      statutoryAvailabilityPayload: statutoryAvailabilityResponse({
        coveredEntitlementTypes: ["SENIOR_CITIZEN"]
      })
    });

    render(<App />);
    await resolveTicket("TICKET-COVERED-SENIOR");
    await screen.findByText("Parking privilege request available");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));

    const entitlementSelect = screen.getByLabelText(/entitlement type/i);
    expect(entitlementSelect).toHaveTextContent("Senior Citizen");
    expect(entitlementSelect).not.toHaveTextContent("PWD");
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
  });

  it("WebPay_WhenOnlyPwdIsCovered_HidesSeniorCitizenOption", async () => {
    stubWebPayFetch({
      statutoryAvailabilityPayload: statutoryAvailabilityResponse({
        coveredEntitlementTypes: ["PWD"]
      })
    });

    render(<App />);
    await resolveTicket("TICKET-COVERED-PWD");
    await screen.findByText("Parking privilege request available");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));

    const entitlementSelect = screen.getByLabelText(/entitlement type/i);
    await waitFor(() => expect(entitlementSelect).toHaveValue("PWD"));
    expect(entitlementSelect).toHaveTextContent("PWD");
    expect(entitlementSelect).not.toHaveTextContent("Senior Citizen");
  });

  it("WebPay_WhenNoOrdinanceCoverage_HidesStatutoryControlsAndPreservesOrdinaryPayment", async () => {
    stubWebPayFetch({
      statutoryAvailabilityPayload: statutoryAvailabilityResponse({
        availabilityStatus: "NO_APPLICABLE_LOCAL_ORDINANCE",
        statutoryParkingBenefitAvailable: false,
        coveredEntitlementTypes: [],
        safeReasonCode: "NO_APPLICABLE_LOCAL_ORDINANCE"
      })
    });

    render(<App />);
    await resolveTicket("TICKET-NO-COVERAGE");

    expect(await screen.findByText("Parking privilege request not available")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /request statutory discount/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/entitlement type/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/id document type/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
  });

  it("WebPay_WhenAvailabilityIsTemporarilyUnavailable_HidesStatutoryControlsAndKeepsPaymentAvailable", async () => {
    stubWebPayFetch({
      statutoryAvailabilityOk: false,
      statutoryAvailabilityStatus: 503,
      statutoryAvailabilityPayload: {
        errorCode: "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE",
        message: "Parking privilege availability is temporarily unavailable. You may continue with the regular parking amount or try again shortly.",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      }
    });

    render(<App />);
    await resolveTicket("TICKET-AVAILABILITY-UNAVAILABLE");

    expect(await screen.findByText("Parking privilege availability unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /request statutory discount/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /check availability/i })).toBeEnabled();
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
    expect(screen.queryByText(/central pms/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/httpclient/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenLookupInputIsInvalid_RejectsClientSide", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "@@");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Enter a valid ticket reference.");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("WebPay_WhenSummaryResolved_ContinueToPaymentCreatesPaymentIntent", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-027");
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.getByText("PaymentRequired")).toBeInTheDocument();
    await continueToPayment();

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1));
    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const paymentIntentBody = JSON.parse(paymentIntentCall[1]?.body as string) as Record<string, unknown>;
    expect(paymentIntentBody.tariffSnapshotId).toBe(successResponse.tariffSnapshotId);
    expect(paymentIntentBody.expectedAmountMinorUnits).toBe(successResponse.amountMinorUnits);
  });

  it("WebPay_WhenDisplayedPayableBasisExists_ContinueToPaymentDoesNotResolveAgain", async () => {
    const displayedBasis = {
      ...successResponse,
      tariffSnapshotId: "99999999-9999-9999-9999-999999999999",
      amountMinorUnits: 9900
    };
    const fetchMock = stubWebPayFetch({ resolvePayload: displayedBasis });

    render(<App />);

    await resolveTicket("TICKET-DISPLAYED-001", "99.00");
    await continueToPayment();

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1));
    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const paymentIntentBody = JSON.parse(paymentIntentCall[1]?.body as string) as Record<string, unknown>;
    expect(paymentIntentBody.tariffSnapshotId).toBe(displayedBasis.tariffSnapshotId);
    expect(paymentIntentBody.expectedAmountMinorUnits).toBe(displayedBasis.amountMinorUnits);
  });

  it("WebPay_WhenSessionResolved_DoesNotRenderCouponEntryControls", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-COUPON-001");

    expect(screen.queryByLabelText(/coupon code/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /apply coupon/i })).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.map((call) => String(call[0]))).not.toContainEqual(expect.stringContaining("/v1/public/coupons/apply"));
  });

  it("WebPay_WhenStatutoryStatusPendingFromBackend_BlocksPaymentInitiation", async () => {
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        statutoryDiscountStatus: "PENDING_OPERATOR_VALIDATION"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-STAT-001");
    expect(screen.queryByLabelText(/evidence reference/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /request statutory discount/i })).not.toBeInTheDocument();
    expect(screen.getByText("Pending operator validation.")).toBeInTheDocument();
    expect(screen.getByText("Please ask the parking site operator to validate your Senior Citizen or PWD discount before payment.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /discount review pending/i })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: /discount review pending/i }));

    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(0);
    expect(fetchMock.mock.calls.map((call) => String(call[0]))).not.toContainEqual(expect.stringContaining("/v1/public/discounts/statutory/validate"));
  });

  it("WebPay_WhenSessionResolved_AllowsSeniorCitizenRequestThroughPaymentOrchestratorOnly", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-STAT-101");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    expect(screen.getByText(/Entitlement details for review/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/choose or take a clear photo/i)).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText(/entitlement type/i), "SENIOR_CITIZEN");
    await userEvent.type(screen.getByLabelText(/id document type/i), "OSCA");
    await userEvent.type(screen.getByLabelText(/issuing authority/i), "Quezon City");
    await userEvent.type(screen.getByLabelText(/^id reference$/i), "SC00001234");
    await userEvent.click(screen.getByLabelText(/i confirm these entitlement details/i));
    await userEvent.click(screen.getByRole("button", { name: /submit for review/i }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some((call) => String(call[0]).endsWith("/v1/webpay/statutory-discounts/decisions"))).toBe(true)
    );
    const statutoryCall = fetchMock.mock.calls.find((call) => String(call[0]).endsWith("/v1/webpay/statutory-discounts/decisions"))!;
    const body = JSON.parse((statutoryCall[1] as RequestInit).body as string);
    const headers = (statutoryCall[1] as RequestInit).headers as Record<string, string>;
    expect((statutoryCall[1] as RequestInit).method).toBe("POST");
    expect(headers["Idempotency-Key"]).toContain("webpay-statutory-discount-decision");
    expect(headers["X-Correlation-Id"]).toBeTruthy();
    expect(body.entitlementType).toBe("SENIOR_CITIZEN");
    expect(body.parkingSessionId).toBe(successResponse.parkingSessionId);
    expect(body.originalTariffSnapshotId).toBe(successResponse.tariffSnapshotId);
    expect(body.maskedIdReference).toBe("SC****1234");
    expect(body.evidenceCaptureRequested).toBe(false);
    expect(body).not.toHaveProperty("sourceChannel");
    expect(body).not.toHaveProperty("reviewerUserId");
    expect(body).not.toHaveProperty("reviewerAttestation");
    expect(body).not.toHaveProperty("operatorDeviceBindingId");
    expect(body).not.toHaveProperty("operatorShiftId");
    expect(body).not.toHaveProperty("statutoryDiscountAmountMinorUnits");
    expect(body).not.toHaveProperty("vatAmountMinorUnits");
    expect(body).not.toHaveProperty("finalPayableAmountMinorUnits");
    expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    expect(fetchMock.mock.calls.map((call) => String(call[0]))).not.toContainEqual(expect.stringContaining("/apply-payable-basis"));
    expect(fetchMock.mock.calls.map((call) => String(call[0]))).not.toContainEqual(expect.stringContaining("/v1/statutory-discounts/decisions"));
  });

  it("WebPay_WhenAuthoritativeAvailabilityRequiresEvidence_RequestsCaptureAndBootstrapsI016AfterDecision", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryAvailabilityPayload: statutoryAvailabilityResponse({
        requiredEvidenceTypes: [{
          evidenceType: "STATUTORY_ID",
          requirementStatus: "REQUIRED",
          safeRequirementLabel: "Entitlement photo",
          safeRequirementNotes: "Choose a clear photo."
        }]
      })
    });
    render(<App />);

    await submitBasicStatutoryRequest();

    const decisionCall = firstRouteCall(fetchMock, "/v1/webpay/statutory-discounts/decisions");
    const body = JSON.parse((decisionCall[1] as RequestInit).body as string);
    expect(body.evidenceCaptureRequested).toBe(true);
    expect(await screen.findByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();
    expect(routeCalls(fetchMock, "/v1/webpay/statutory-discounts/evidence/bootstrap")).toHaveLength(1);
    expect(screen.getByRole("button", { name: /pay regular amount/i })).toBeInTheDocument();
  });

  it("WebPay_WhenManualServiceAuthPendingReviewReadbackIsRetryable_RendersAwaitingReviewNotUnavailable", async () => {
    const manualPendingReviewPayload = statutoryDecisionResponse({
      statutoryDiscountDecisionCommandId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      decisionCommandStatus: "AWAITING_REVIEW",
      decisionResultStatus: "NOT_DECIDED",
      applicationCommandStatus: "NOT_REQUESTED",
      applicationResultClassification: "NOT_REQUESTED",
      payableBasisReady: false,
      payableBasisReadinessStatus: "AWAITING_REVIEW",
      payableBasisReadinessAction: "POLL_READBACK",
      retryable: true,
      recoveryClassification: "PENDING_REVIEW",
      recoveryAction: "POLL_READBACK",
      safeErrorCode: null,
      overallResultClassification: "PENDING_REVIEW",
      currency: "PHP",
      correlationId: "11111111-1111-4111-8111-111111111111"
    });
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: manualPendingReviewPayload,
      statutoryReadPayload: manualPendingReviewPayload
    });

    render(<App />);

    await submitBasicStatutoryRequest();

    expect(await screen.findByRole("heading", { name: /awaiting review/i })).toBeInTheDocument();
    expect(screen.getAllByText(/parking privilege request was received and is awaiting review/i).length).toBeGreaterThan(0);
    expect(document.body).not.toHaveTextContent("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    expect(document.body).not.toHaveTextContent("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    expect(document.body).not.toHaveTextContent("11111111-1111-4111-8111-111111111111");
    expect(document.body).not.toHaveTextContent(successResponse.parkingSessionId);
    expect(document.body).not.toHaveTextContent(successResponse.siteId);
    expect(document.body).not.toHaveTextContent(successResponse.siteGroupId);
    expect(screen.getByText(formatCustomerSupportReference("11111111-1111-4111-8111-111111111111")!)).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: /refresh status/i }, { timeout: 6000 })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /status temporarily unavailable/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/statutory discount status is temporarily unavailable/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/CentralPmsStatutoryDiscountDecisionSubmit/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/statutory-discounts\.decision\.submit\.webpay/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/stack trace/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: /refresh status/i }));
    await waitFor(() =>
      expect(fetchMock.mock.calls.some((call) => String(call[0]).includes(`/v1/webpay/statutory-discounts/decisions/${statutoryDecisionCommandId}`))).toBe(true)
    );
  });

  it("WebPay_WhenPendingLifecycleRediscoveryFindsExistingDecision_RestoresPendingPanelAndContinuationWithoutNewDecision", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryRediscoveryPayload: statutoryPendingLifecycleRediscoveryResponse(),
      statutoryReadPayload: statutoryDecisionResponse()
    });

    render(<App />);

    await resolveTicket("TICKET-STAT-REDISCOVER");

    expect(await screen.findByRole("heading", { name: /awaiting review/i })).toBeInTheDocument();
    expect(screen.getByText(/existing statutory discount request restored from central pms/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /this continuation link/i })).toHaveAttribute(
      "href",
      "https://pay.example.test/privilege-review/opaque-existing"
    );
    expect(screen.getByRole("button", { name: /pay regular amount/i })).toBeInTheDocument();

    const rediscoveryCall = firstRouteCall(fetchMock, "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover");
    const rediscoveryBody = JSON.parse((rediscoveryCall[1] as RequestInit).body as string);
    const rediscoveryHeaders = (rediscoveryCall[1] as RequestInit).headers as Record<string, string>;
    expect(rediscoveryBody.lookupMode).toBe("PARKING_SESSION_ID");
    expect(rediscoveryBody.parkingSessionId).toBe(successResponse.parkingSessionId);
    expect(rediscoveryBody.siteId).toBe(successResponse.siteId);
    expect(rediscoveryBody.siteGroupId).toBe(successResponse.siteGroupId);
    expect(rediscoveryBody).not.toHaveProperty("ticketReference");
    expect(rediscoveryBody).not.toHaveProperty("plateNumber");
    expect(rediscoveryHeaders["X-Correlation-Id"]).toBeTruthy();
    expect(rediscoveryHeaders).not.toHaveProperty("X-ExitPass-Service-Identity-Id");
    expect(rediscoveryHeaders).not.toHaveProperty("X-ExitPass-Permissions");
    expect(rediscoveryHeaders).not.toHaveProperty("Authorization");

    expect(routeCalls(fetchMock, `/v1/webpay/statutory-discounts/decisions/${statutoryDecisionCommandId}`).length).toBeGreaterThanOrEqual(1);
    expect(routeCalls(fetchMock, "/v1/webpay/statutory-discounts/decisions").filter((call) => (call[1] as RequestInit)?.method === "POST")).toHaveLength(0);
    expect(routeCalls(fetchMock, "/apply-payable-basis")).toHaveLength(0);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(0);
  });

  it("WebPay_WhenPendingLifecycleRediscoveryHasNoActiveLifecycle_DoesNotFabricatePendingState", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryRediscoveryPayload: statutoryPendingLifecycleRediscoveryResponse({
        classification: "NO_ACTIVE_LIFECYCLE",
        statutoryDecisionId: null,
        statutoryDecisionCommandId: null,
        requestReference: null,
        opaqueContinuationReference: null,
        opaqueContinuationUrl: null,
        retryable: false
      })
    });

    render(<App />);

    await resolveTicket("TICKET-NO-ACTIVE-LIFECYCLE");

    expect(await screen.findByRole("button", { name: /continue to payment/i })).toBeEnabled();
    expect(screen.queryByRole("heading", { name: /awaiting review/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/continuation link/i)).not.toBeInTheDocument();
    expect(routeCalls(fetchMock, "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover")).toHaveLength(1);
    expect(routeCalls(fetchMock, "/v1/webpay/statutory-discounts/decisions").filter((call) => (call[1] as RequestInit)?.method === "POST")).toHaveLength(0);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(0);
  });

  it("WebPay_WhenPendingLifecycleRediscoveryIsUnavailable_ShowsSafeRetryWithoutNewDecision", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryRediscoveryPayload: {
        errorCode: "WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE",
        message: "The parking privilege request could not be checked right now. Please try again.",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      },
      statutoryRediscoveryOk: false,
      statutoryRediscoveryStatus: 503
    });

    render(<App />);

    await resolveTicket("TICKET-REDISCOVERY-UNAVAILABLE");

    expect(await screen.findByText(/existing statutory discount request could not be checked/i)).toBeInTheDocument();
    expect(screen.queryByText(/authenticated Central PMS/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/X-ExitPass-Permissions/i)).not.toBeInTheDocument();
    expect(routeCalls(fetchMock, "/v1/webpay/statutory-discounts/decisions").filter((call) => (call[1] as RequestInit)?.method === "POST")).toHaveLength(0);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(0);
  });

  it("WebPay_WhenPendingReviewCustomerCancelsRegularPaymentWarning_KeepsReviewPendingWithoutPaymentIntent", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await submitBasicStatutoryRequest();
    const payRegular = await screen.findByRole("button", { name: /pay regular amount/i });
    await waitFor(() => expect(payRegular).toBeEnabled(), { timeout: 6000 });
    await userEvent.click(payRegular);

    expect(screen.getByRole("dialog", { name: /proceed without the parking privilege/i })).toBeInTheDocument();
    expect(screen.getByText(/approval after payment will not automatically refund or retroactively adjust/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /keep waiting/i }));

    expect(screen.queryByRole("dialog", { name: /proceed without the parking privilege/i })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /awaiting review/i })).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter((call) => String(call[0]).includes("/v1/webpay/payment-intents"))).toHaveLength(0);
  });

  it("WebPay_WhenPendingReviewCustomerConfirmsRegularPayment_RevalidatesAndCreatesOrdinaryIntentOnly", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await submitBasicStatutoryRequest();
    const payRegular = await screen.findByRole("button", { name: /pay regular amount/i });
    await waitFor(() => expect(payRegular).toBeEnabled(), { timeout: 6000 });
    await userEvent.click(payRegular);
    await userEvent.click(screen.getByRole("button", { name: /continue with regular payment/i }));

    await screen.findByText("Payment handoff ready");

    const decisionReads = routeCalls(fetchMock, `/v1/webpay/statutory-discounts/decisions/${statutoryDecisionCommandId}`);
    expect(decisionReads.length).toBeGreaterThan(0);
    expect(routeCalls(fetchMock, "/v1/webpay/parking-session").length).toBeGreaterThanOrEqual(2);

    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const body = JSON.parse((paymentIntentCall[1] as RequestInit).body as string);
    expect(body.expectedAmountMinorUnits).toBe(successResponse.amountMinorUnits);
    expect(body.expectedCurrency).toBe(successResponse.currency);
    expect(body.tariffSnapshotId).toBe(successResponse.tariffSnapshotId);
    expect(body).not.toHaveProperty("statutoryDiscountDecisionCommandId");
    expect(body).not.toHaveProperty("statutoryDiscountPayableBasisApplicationCommandId");
  });

  it("WebPay_WhenRegularAmountChangesBeforePendingReviewPayment_RequiresRenewedConfirmation", async () => {
    let resolveCount = 0;
    const changedResolve = {
      ...successResponse,
      tariffSnapshotId: "12121212-1212-4121-8121-121212121212",
      amountMinorUnits: 13000
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      const isResolve = url.includes("/v1/webpay/parking-session");
      const isAvailability = url.endsWith("/v1/webpay/statutory-discounts/availability");
      const isRediscovery = url.endsWith("/v1/webpay/statutory-discounts/pending-lifecycle/rediscover");
      const isStatutorySubmit = url.endsWith("/v1/webpay/statutory-discounts/decisions") && init?.method === "POST";
      const isStatutoryRead = url.includes("/v1/webpay/statutory-discounts/decisions/") && init?.method === "GET";
      return {
        ok: true,
        status: 200,
        json: async () => {
          if (isResolve) {
            resolveCount += 1;
            return resolveCount === 1 ? successResponse : changedResolve;
          }
          if (isAvailability) {
            return statutoryAvailabilityResponse();
          }
          if (isRediscovery) {
            return statutoryPendingLifecycleRediscoveryResponse({ classification: "NO_ACTIVE_LIFECYCLE" });
          }
          if (isStatutorySubmit || isStatutoryRead) {
            return statutoryDecisionResponse();
          }
          return { ...successResponse, ...changedResolve };
        }
      };
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await submitBasicStatutoryRequest();
    const payRegular = await screen.findByRole("button", { name: /pay regular amount/i });
    await waitFor(() => expect(payRegular).toBeEnabled(), { timeout: 6000 });
    await userEvent.click(payRegular);
    await userEvent.click(screen.getByRole("button", { name: /continue with regular payment/i }));

    expect(await screen.findByText(/regular parking amount changed before payment/i)).toBeInTheDocument();
    expect(screen.getAllByText(/PHP 130.00/i).length).toBeGreaterThan(0);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(0);

    await userEvent.click(screen.getByRole("button", { name: /continue with regular payment/i }));
    await screen.findByText("Payment handoff ready");

    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const body = JSON.parse((paymentIntentCall[1] as RequestInit).body as string);
    expect(body.expectedAmountMinorUnits).toBe(13000);
    expect(body.tariffSnapshotId).toBe(changedResolve.tariffSnapshotId);
    expect(body).not.toHaveProperty("statutoryDiscountDecisionCommandId");
  });

  it("WebPay_WhenLocalValidationRecoveryResetParamPresent_ClearsOnlyStatutoryRecoveryBeforeLoad", () => {
    const staleRecovery = createStatutoryRecoveryRecord({
      parkingSessionId: successResponse.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      stage: "DECISION_SUBMITTING",
      decisionIdempotencyKey: "stale-decision-key"
    });
    localStorage.setItem(statutoryRecoveryStorageKey, JSON.stringify(staleRecovery));
    localStorage.setItem("exitpass:webpay:unrelated-local-state", "keep");
    window.history.pushState({}, "", "/?ticketReference=WEBPAY-STAT-SERVICE-AUTH-001&webpayStatutoryRecoveryReset=1");
    stubWebPayFetch();

    render(<App />);

    expect(localStorage.getItem(statutoryRecoveryStorageKey)).toBeNull();
    expect(localStorage.getItem("exitpass:webpay:unrelated-local-state")).toBe("keep");
    expect(screen.queryByText(/another page may be submitting this statutory discount request/i)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/ticket reference/i)).toHaveValue("WEBPAY-STAT-SERVICE-AUTH-001");
  });

  it("WebPay_WhenPwdRequestSubmitted_EntersPendingReviewAndPreventsDuplicateSubmit", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-STAT-102");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    await userEvent.selectOptions(screen.getByLabelText(/entitlement type/i), "PWD");
    await userEvent.type(screen.getByLabelText(/id document type/i), "PWD ID");
    await userEvent.type(screen.getByLabelText(/issuing authority/i), "Cebu City");
    await userEvent.type(screen.getByLabelText(/^id reference$/i), "PW00005678");
    await userEvent.click(screen.getByLabelText(/i confirm these entitlement details/i));
    const submit = screen.getByRole("button", { name: /submit for review/i });
    await userEvent.dblClick(submit);

    await screen.findByRole("heading", { name: /awaiting review/i });
    const statutoryPosts = fetchMock.mock.calls.filter((call) => String(call[0]).endsWith("/v1/webpay/statutory-discounts/decisions"));
    expect(statutoryPosts).toHaveLength(1);
    const body = JSON.parse((statutoryPosts[0][1] as RequestInit).body as string);
    expect(body.entitlementType).toBe("PWD");
    expect(screen.getAllByText(/parking privilege request was received and is awaiting review/i).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
  });

  it("WebPay_WhenFullIdReferenceEntered_AutomaticallyMasksBeforeApiCall", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-STAT-103");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    await userEvent.type(screen.getByLabelText(/id document type/i), "OSCA");
    await userEvent.type(screen.getByLabelText(/issuing authority/i), "Quezon City");
    const idInput = screen.getByLabelText(/^id reference$/i);
    await userEvent.type(idInput, "123456789012");
    await userEvent.click(screen.getByLabelText(/i confirm these entitlement details/i));
    await userEvent.click(screen.getByRole("button", { name: /submit for review/i }));

    await screen.findByRole("heading", { name: /awaiting review/i });
    const statutoryPosts = fetchMock.mock.calls.filter((call) => String(call[0]).endsWith("/statutory-discounts/decisions"));
    expect(statutoryPosts).toHaveLength(1);
    const body = JSON.parse((statutoryPosts[0][1] as RequestInit).body as string);
    expect(body.maskedIdReference).toBe("12******9012");
    expect(document.body.innerHTML).not.toContain("123456789012");
    expect(JSON.stringify(localStorage)).not.toContain("123456789012");
  });

  it("WebPay_WhenIdReferenceTooShort_BlocksBeforeApiCallAndClearsTheValue", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-STAT-103");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    await userEvent.type(screen.getByLabelText(/id document type/i), "OSCA");
    await userEvent.type(screen.getByLabelText(/issuing authority/i), "Quezon City");
    const idInput = screen.getByLabelText(/^id reference$/i);
    await userEvent.type(idInput, "AB1234");
    await userEvent.tab();

    expect(idInput).toHaveValue("");
    expect(await screen.findByRole("alert")).toHaveTextContent(/at least 7 characters/i);
    expect(document.body.innerHTML).not.toContain("AB1234");
    expect(fetchMock.mock.calls.filter((call) => String(call[0]).includes("/statutory-discounts/decisions"))).toHaveLength(0);
  });

  it("WebPay_WhenPollingReadbackReturnsApplicationRequired_ShowsApplicationActionWithoutPostingAutomatically", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryReadPayload: statutoryDecisionResponse({
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        payableBasisReadinessStatus: "DECISION_APPROVED_APPLICATION_NOT_REQUESTED",
        payableBasisReadinessAction: "SUBMIT_APPLICATION_INTENT",
        overallResultClassification: "COMPLETED"
      })
    });

    render(<App />);

    await submitBasicStatutoryRequest();

    expect(await screen.findByRole("heading", { name: /entitlement approved/i })).toBeInTheDocument();
    expect(screen.getAllByText(/Discount application is pending/i).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: /apply approved discount/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    const paths = fetchMock.mock.calls.map((call) => String(call[0]));
    expect(paths.some((path) => path.includes("/apply-payable-basis"))).toBe(false);
    expect(paths.filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(0);
  });

  it("WebPay_WhenApplicationActionClicked_SubmitsOnceAndStartsReadbackPollingWithoutPaymentIntent", async () => {
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        payableBasisReadinessStatus: "DECISION_APPROVED_APPLICATION_NOT_REQUESTED",
        payableBasisReadinessAction: "SUBMIT_APPLICATION_INTENT",
        overallResultClassification: "COMPLETED"
      }),
      statutoryApplyPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "PROCESSING",
        applicationResultClassification: "PROCESSING",
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "POLL_READBACK"
      }),
      statutoryReadPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "PROCESSING",
        applicationResultClassification: "PROCESSING",
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "POLL_READBACK"
      })
    });

    render(<App />);

    await submitBasicStatutoryRequest();
    const applyButton = await screen.findByRole("button", { name: /apply approved discount/i });
    await userEvent.dblClick(applyButton);

    expect(await screen.findByRole("heading", { name: /discount application processing/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    const paths = fetchMock.mock.calls.map((call) => String(call[0]));
    expect(paths.filter((path) => path.includes("/apply-payable-basis"))).toHaveLength(1);
    expect(paths.filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(0);
    expect(paths.filter((path) => path.includes("/v1/webpay/statutory-discounts/decisions/") && !path.includes("/apply-payable-basis")).length).toBeGreaterThan(0);
  });

  it("WebPay_WhenApplicationActionClicked_UsesStableApplicationKeyAndSafeRequestShape", async () => {
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        payableBasisReadinessStatus: "DECISION_APPROVED_APPLICATION_NOT_REQUESTED",
        payableBasisReadinessAction: "SUBMIT_APPLICATION_INTENT",
        overallResultClassification: "COMPLETED"
      }),
      statutoryApplyPayload: statutoryDecisionResponse({
        retryable: true,
        safeErrorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY",
        recoveryAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY"
      })
    });

    render(<App />);

    await submitBasicStatutoryRequest();
    await userEvent.click(await screen.findByRole("button", { name: /apply approved discount/i }));
    await userEvent.click(await screen.findByRole("button", { name: /retry discount application/i }));

    const applyCalls = fetchMock.mock.calls.filter((call) => String(call[0]).includes("/apply-payable-basis"));
    expect(applyCalls).toHaveLength(2);
    const firstHeaders = applyCalls[0][1]?.headers as Record<string, string>;
    const secondHeaders = applyCalls[1][1]?.headers as Record<string, string>;
    expect(firstHeaders["Idempotency-Key"]).toBe(`webpay-statutory-discount-application:${statutoryDecisionCommandId}`);
    expect(secondHeaders["Idempotency-Key"]).toBe(firstHeaders["Idempotency-Key"]);
    const applyBody = JSON.parse((applyCalls[0][1] as RequestInit).body as string);
    expect(applyBody.entitlementType).toBe("SENIOR_CITIZEN");
    expect(applyBody).not.toHaveProperty("sourceChannel");
    expect(applyBody).not.toHaveProperty("reviewerUserId");
    expect(applyBody).not.toHaveProperty("reviewerAttestation");
    expect(applyBody).not.toHaveProperty("operatorDeviceBindingId");
    expect(applyBody).not.toHaveProperty("operatorShiftId");
    expect(applyBody).not.toHaveProperty("statutoryDiscountAmountMinorUnits");
    expect(applyBody).not.toHaveProperty("vatAmountMinorUnits");
    expect(applyBody).not.toHaveProperty("finalPayableAmountMinorUnits");
    expect(applyBody).not.toHaveProperty("appliedTariffSnapshotId");
  });

  it("WebPay_WhenApplicationReadbackBecomesApplied_DisplaysAuthoritativeAmountsUnchanged", async () => {
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        payableBasisReadinessStatus: "DECISION_APPROVED_APPLICATION_NOT_REQUESTED",
        payableBasisReadinessAction: "SUBMIT_APPLICATION_INTENT",
        overallResultClassification: "COMPLETED"
      }),
      statutoryApplyPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "PROCESSING",
        applicationResultClassification: "PROCESSING",
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "POLL_READBACK"
      }),
      statutoryReadPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        originalAmountMinorUnits: 12500,
        vatExclusiveBasisAmountMinorUnits: 8929,
        vatAmountMinorUnits: 1071,
        vatTreatment: "VAT_EXEMPT_WITH_DISCOUNT",
        statutoryDiscountAmountMinorUnits: 2500,
        finalPayableAmountMinorUnits: 10000,
        currency: "PHP"
      })
    });

    render(<App />);

    await submitBasicStatutoryRequest();
    await userEvent.click(await screen.findByRole("button", { name: /apply approved discount/i }));

    expect(await screen.findByRole("heading", { name: /statutory discount applied/i })).toBeInTheDocument();
    expect(screen.getByText("PHP 89.29")).toBeInTheDocument();
    expect(screen.getByText("PHP 10.71")).toBeInTheDocument();
    expect(screen.getByText("-PHP 25.00")).toBeInTheDocument();
    expect(screen.getByText("PHP 100.00")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
    expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(0);
  });

  it("WebPay_WhenApplicationConflictOrTerminalReturned_StopsSafelyAndKeepsPaymentDisabled", async () => {
    const cases = [
      {
        payload: statutoryDecisionResponse({
          safeErrorCode: "STATUTORY_DISCOUNT_APPLICATION_SEMANTIC_CONFLICT",
          payableBasisReadinessStatus: "APPLICATION_SEMANTIC_CONFLICT",
          payableBasisReadinessAction: "DO_NOT_RETRY",
          overallResultClassification: "TERMINAL_FAILURE"
        }),
        heading: /statutory discount conflict/i
      },
      {
        payload: statutoryDecisionResponse({
          safeErrorCode: "STATUTORY_DISCOUNT_APPLICATION_FAILED",
          payableBasisReadinessStatus: "FAILED",
          payableBasisReadinessAction: "DO_NOT_RETRY",
          overallResultClassification: "TERMINAL_FAILURE"
        }),
        heading: /statutory discount unavailable/i
      }
    ];

    for (const currentCase of cases) {
      const fetchMock = stubWebPayFetch({
        statutorySubmitPayload: statutoryDecisionResponse({
          decisionCommandStatus: "COMPLETED",
          decisionResultStatus: "APPROVED",
          payableBasisReadinessStatus: "DECISION_APPROVED_APPLICATION_NOT_REQUESTED",
          payableBasisReadinessAction: "SUBMIT_APPLICATION_INTENT",
          overallResultClassification: "COMPLETED"
        }),
        statutoryApplyPayload: currentCase.payload
      });

      render(<App />);
      await submitBasicStatutoryRequest();
      await userEvent.click(await screen.findByRole("button", { name: /apply approved discount/i }));

      expect(await screen.findByRole("heading", { name: currentCase.heading })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
      expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/apply-payable-basis"))).toHaveLength(1);
      expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(0);
      cleanup();
      vi.unstubAllGlobals();
    }
  });

  it.each([
    [
      "rejected",
      statutoryDecisionResponse({
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "REJECTED",
        payableBasisReadinessStatus: "DECISION_REJECTED",
        payableBasisReadinessAction: "DO_NOT_RETRY",
        overallResultClassification: "TERMINAL_FAILURE"
      }),
      /entitlement not approved/i
    ],
    [
      "retryable",
      statutoryDecisionResponse({
        retryable: true,
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY",
        safeErrorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE"
      }),
      /discount application temporarily unavailable/i
    ],
    [
      "terminal",
      statutoryDecisionResponse({
        retryable: false,
        payableBasisReadinessStatus: "FAILED",
        payableBasisReadinessAction: "DO_NOT_RETRY",
        overallResultClassification: "TERMINAL_FAILURE"
      }),
      /statutory discount unavailable/i
    ]
  ])("WebPay_WhenStatutoryReadbackIs%s_DisplaysSafeState", async (_caseName, readback, heading) => {
    stubWebPayFetch({ statutorySubmitPayload: readback });

    render(<App />);

    await submitBasicStatutoryRequest();

    expect(await screen.findByRole("heading", { name: heading })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    expect(screen.queryByText(/stack trace|http:\/\/central-pms|reviewer/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenReadyReadbackReturned_DisplaysAuthoritativeAmountsWithoutCalculatingThem", async () => {
    stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        originalAmountMinorUnits: 12500,
        vatExclusiveBasisAmountMinorUnits: 8929,
        vatAmountMinorUnits: 1071,
        vatTreatment: "VAT_EXEMPT_WITH_DISCOUNT",
        statutoryDiscountAmountMinorUnits: 2500,
        finalPayableAmountMinorUnits: 10000,
        currency: "PHP"
      })
    });

    render(<App />);

    await submitBasicStatutoryRequest();

    expect(await screen.findByRole("heading", { name: /statutory discount applied/i })).toBeInTheDocument();
    expect(screen.getAllByText("PHP 125.00").length).toBeGreaterThan(0);
    expect(screen.getByText("PHP 89.29")).toBeInTheDocument();
    expect(screen.getByText("PHP 10.71")).toBeInTheDocument();
    expect(screen.getByText("-PHP 25.00")).toBeInTheDocument();
    expect(screen.getByText("PHP 100.00")).toBeInTheDocument();
    expect(screen.getByText("VAT Treatment")).toBeInTheDocument();
    expect(screen.getByText(/Payment is available using the Central PMS-approved statutory payable basis/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeEnabled();
  });

  it("WebPay_WhenStatutoryReadbackIsReady_PaymentIntentUsesAppliedBasisAndCanonicalLinkage", async () => {
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        originalTariffSnapshotId: successResponse.tariffSnapshotId,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        originalAmountMinorUnits: 5000,
        vatExclusiveBasisAmountMinorUnits: 3571,
        vatAmountMinorUnits: 429,
        vatTreatment: "VAT_EXEMPT_WITH_DISCOUNT",
        statutoryDiscountAmountMinorUnits: 1000,
        finalPayableAmountMinorUnits: 4000,
        currency: "PHP"
      }),
      intentPayload: {
        ...successResponse,
        tariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        amountMinorUnits: 4000,
        currency: "PHP"
      }
    });

    render(<App />);

    await submitBasicStatutoryRequest();
    await continueToPayment();

    await waitFor(() =>
      expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(1)
    );
    const paymentCall = fetchMock.mock.calls.find((call) => String(call[0]).includes("/v1/webpay/payment-intents"));
    const body = JSON.parse((paymentCall?.[1] as RequestInit).body as string);
    expect(body.tariffSnapshotId).toBe("99999999-9999-4999-8999-999999999999");
    expect(body.tariffSnapshotId).not.toBe(successResponse.tariffSnapshotId);
    expect(body.expectedAmountMinorUnits).toBe(4000);
    expect(body.expectedCurrency).toBe("PHP");
    const persistedRecovery = JSON.parse(localStorage.getItem(statutoryRecoveryStorageKey) ?? "{}") as Record<string, unknown>;
    expect(body.correlationId).toBe(persistedRecovery.paymentIntentCorrelationId);
    expect(body.statutoryDiscountDecisionCommandId).toBe(statutoryDecisionCommandId);
    expect(body.statutoryDiscountPayableBasisApplicationCommandId).toBe("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    expect(body).not.toHaveProperty("statutoryDiscountAmountMinorUnits");
    expect(body).not.toHaveProperty("vatAmountMinorUnits");
    expect(body).not.toHaveProperty("vatExclusiveBasisAmountMinorUnits");
    expect(body).not.toHaveProperty("sourceChannel");
    expect(await screen.findByRole("link", { name: /continue to payment/i })).toBeInTheDocument();
  });

  it("WebPay_WhenRecoveryHasDecisionCommandId_ResumesWithGetReadbackAndDoesNotRepeatDecisionPost", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryReadPayload: statutoryDecisionResponse({
        decisionCommandStatus: "AWAITING_REVIEW",
        decisionResultStatus: "NOT_DECIDED"
      })
    });
    const recovery = createStatutoryRecoveryRecord({
      parkingSessionId: successResponse.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      statutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
      decisionIdempotencyKey: "webpay-statutory-discount-decision:original",
      applicationIdempotencyKey: "webpay-statutory-discount-application:original",
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      correlationId: "77777777-7777-7777-7777-777777777777",
      stage: "DECISION_PENDING"
    });
    localStorage.setItem(statutoryRecoveryStorageKey, JSON.stringify(recovery));

    render(<App />);

    expect(await screen.findByText(/Existing statutory discount request restored/i)).toBeInTheDocument();
    const paths = fetchMock.mock.calls.map((call) => `${(call[1] as RequestInit | undefined)?.method ?? "GET"} ${String(call[0])}`);
    expect(paths.some((path) => path.includes(`GET /v1/webpay/statutory-discounts/decisions/${statutoryDecisionCommandId}`))).toBe(true);
    expect(paths.filter((path) => path === "POST /v1/webpay/statutory-discounts/decisions")).toHaveLength(0);
  });

  it("WebPay_WhenRecoveredApplicationProcessing_UsesGetReadbackAndDoesNotRepeatApplicationPost", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryReadPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "PROCESSING",
        applicationResultClassification: "PROCESSING",
        payableBasisReady: false,
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "POLL_READBACK"
      })
    });
    const recovery = createStatutoryRecoveryRecord({
      parkingSessionId: successResponse.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      statutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionIdempotencyKey: "webpay-statutory-discount-decision:original",
      applicationIdempotencyKey: "webpay-statutory-discount-application:original",
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      correlationId: "77777777-7777-7777-7777-777777777777",
      stage: "APPLICATION_PROCESSING"
    });
    localStorage.setItem(statutoryRecoveryStorageKey, JSON.stringify(recovery));

    render(<App />);

    expect(await screen.findByText(/Existing statutory discount request restored/i)).toBeInTheDocument();
    const calls = fetchMock.mock.calls.map((call) => ({ url: String(call[0]), method: (call[1] as RequestInit | undefined)?.method ?? "GET" }));
    expect(calls.some((call) => call.method === "GET" && call.url.includes("/v1/webpay/statutory-discounts/decisions/"))).toBe(true);
    expect(calls.some((call) => call.method === "POST" && call.url.includes("/apply-payable-basis"))).toBe(false);
  });

  it("WebPay_WhenPersistedReadyStageExists_RequiresFreshReadbackBeforePaymentEnablement", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryReadPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        finalPayableAmountMinorUnits: 4000,
        currency: "PHP"
      }),
      intentPayload: {
        ...successResponse,
        tariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        amountMinorUnits: 4000,
        currency: "PHP"
      }
    });
    localStorage.setItem(statutoryRecoveryStorageKey, JSON.stringify(createStatutoryRecoveryRecord({
      parkingSessionId: successResponse.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      statutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
      decisionIdempotencyKey: "webpay-statutory-discount-decision:original",
      applicationIdempotencyKey: "webpay-statutory-discount-application:original",
      requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      correlationId: "77777777-7777-7777-7777-777777777777",
      stage: "PAYABLE_READY"
    })));

    window.history.pushState({}, "", "/?ticketReference=TICKET-001");
    render(<App />);

    expect(await screen.findByText(/Existing statutory discount request restored/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));
    expect((await screen.findAllByText("125.00")).length).toBeGreaterThan(0);
    await continueToPayment();

    await waitFor(() =>
      expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(1)
    );
    const paymentCall = fetchMock.mock.calls.find((call) => String(call[0]).includes("/v1/webpay/payment-intents"));
    const body = JSON.parse((paymentCall?.[1] as RequestInit).body as string);
    expect(body.tariffSnapshotId).toBe("99999999-9999-4999-8999-999999999999");
    expect(body.expectedAmountMinorUnits).toBe(4000);
  });

  it("WebPay_WhenRecoveryRecordIsMalformed_ClearsMetadataAndDoesNotEnableStatutoryPayment", async () => {
    const fetchMock = stubWebPayFetch();
    localStorage.setItem(statutoryRecoveryStorageKey, "{broken");

    render(<App />);

    expect(screen.getByText(/invalid statutory discount recovery record was cleared/i)).toBeInTheDocument();
    expect(localStorage.getItem(statutoryRecoveryStorageKey)).toBeNull();
    expect(fetchMock.mock.calls.some((call) => String(call[0]).includes("/statutory-discounts/decisions/"))).toBe(false);
  });

  it("WebPay_WhenAnotherTabRecordedPaymentSubmitting_DisablesPaymentAndCreatesNoPaymentIntent", async () => {
    const fetchMock = stubWebPayFetch();
    localStorage.setItem(statutoryRecoveryStorageKey, JSON.stringify(createStatutoryRecoveryRecord({
      parkingSessionId: successResponse.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      decisionIdempotencyKey: "webpay-statutory-discount-decision:original",
      paymentIntentCorrelationId: "99999999-9999-4999-8999-999999999999",
      stage: "PAYMENT_SUBMITTING"
    })));

    render(<App />);

    await resolveTicket();
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: /continue to payment/i }));
    expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(0);
  });

  it("WebPay_WhenRediscoveryReadsAppliedDecision_PreservesSameSessionPaymentSubmittingBlock", async () => {
    const appliedDecision = statutoryDecisionResponse({
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "APPLIED",
      applicationResultClassification: "APPLIED",
      payableBasisReady: true,
      payableBasisReadinessStatus: "READY",
      payableBasisReadinessAction: null,
      appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
      finalPayableAmountMinorUnits: 4000,
      currency: "PHP"
    });
    const fetchMock = stubWebPayFetch({
      statutoryRediscoveryPayload: statutoryPendingLifecycleRediscoveryResponse({
        decisionStatus: "APPROVED",
        payableBasisStatus: "READY",
        lifecycleState: "APPLIED",
        retryable: false
      }),
      statutoryReadPayload: appliedDecision
    });
    localStorage.setItem(statutoryRecoveryStorageKey, JSON.stringify(createStatutoryRecoveryRecord({
      parkingSessionId: successResponse.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      statutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      paymentIntentCorrelationId: "99999999-9999-4999-8999-999999999999",
      stage: "PAYMENT_SUBMITTING"
    })));

    render(<App />);
    await resolveTicket();

    await waitFor(() =>
      expect(routeCalls(fetchMock, "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover")).toHaveLength(1)
    );
    await waitFor(() =>
      expect(routeCalls(fetchMock, `/v1/webpay/statutory-discounts/decisions/${statutoryDecisionCommandId}`)).not.toHaveLength(0)
    );
    expect(screen.getByRole("button", { name: /continue to payment/i })).toBeDisabled();
    expect(JSON.parse(localStorage.getItem(statutoryRecoveryStorageKey) ?? "{}").stage).toBe("PAYMENT_SUBMITTING");
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(0);
  });

  it("WebPay_WhenClearRecoveryClicked_RemovesOnlyBrowserMetadata", async () => {
    localStorage.setItem(statutoryRecoveryStorageKey, JSON.stringify(createStatutoryRecoveryRecord({
      parkingSessionId: successResponse.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      statutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
      decisionIdempotencyKey: "webpay-statutory-discount-decision:original",
      stage: "DECISION_PENDING"
    })));
    stubWebPayFetch();

    render(<App />);

    await screen.findByRole("button", { name: /clear browser recovery/i });
    await userEvent.click(screen.getByRole("button", { name: /clear browser recovery/i }));

    expect(localStorage.getItem(statutoryRecoveryStorageKey)).toBeNull();
    expect(screen.getByText(/does not cancel any Central PMS statutory discount request/i)).toBeInTheDocument();
  });

  it("WebPay_WhenStatutoryReadyPaymentClickedRapidly_SubmitsOnePaymentIntent", async () => {
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        finalPayableAmountMinorUnits: 4000,
        currency: "PHP"
      }),
      intentPayload: {
        ...successResponse,
        tariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        amountMinorUnits: 4000,
        currency: "PHP"
      }
    });

    render(<App />);

    await submitBasicStatutoryRequest();
    const paymentButton = screen.getByRole("button", { name: /continue to payment/i });
    await userEvent.dblClick(paymentButton);

    await waitFor(() =>
      expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(1)
    );
  });

  it("WebPay_WhenBackendRejectsStaleAppliedBasis_ShowsSafeRefreshGuidanceWithoutHandoff", async () => {
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        finalPayableAmountMinorUnits: 4000,
        currency: "PHP"
      }),
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        errorCode: "STATUTORY_DISCOUNT_APPLIED_SNAPSHOT_MISMATCH",
        message: "Downstream mismatch detail should not be shown.",
        retryable: false,
        correlationId: "77777777-7777-7777-7777-777777777777"
      }
    });

    render(<App />);

    await submitBasicStatutoryRequest();
    await continueToPayment();

    expect(await screen.findByRole("alert")).toHaveTextContent("The payable amount changed or payment has already started. Please restart from lookup.");
    expect(screen.queryByRole("link", { name: /continue to payment/i })).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(1);
  });

  it.each([
    ["missing applied snapshot", { appliedTariffSnapshotId: null }],
    ["missing final amount", { finalPayableAmountMinorUnits: null }],
    ["missing currency", { currency: null }]
  ])("WebPay_WhenReadyReadbackHas%s_KeepsPaymentDisabled", async (_caseName, missingFact) => {
    const fetchMock = stubWebPayFetch({
      statutorySubmitPayload: statutoryDecisionResponse({
        statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        finalPayableAmountMinorUnits: 4000,
        currency: "PHP",
        ...missingFact
      })
    });

    render(<App />);

    await submitBasicStatutoryRequest();

    expect(await screen.findByRole("heading", { name: /payment basis incomplete/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /statutory discount pending/i })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: /statutory discount pending/i }));
    expect(fetchMock.mock.calls.map((call) => String(call[0])).filter((path) => path.includes("/v1/webpay/payment-intents"))).toHaveLength(0);
  });

  it("WebPay_WhenApprovedPayableBasisExists_DisplaysReadOnlyAdjustments", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        amountMinorUnits: 7500,
        originalAmountMinorUnits: 12500,
        couponAdjustmentMinorUnits: 5000,
        statutoryAdjustmentMinorUnits: 0,
        totalAdjustmentMinorUnits: 5000,
        couponStatus: "APPROVED",
        statutoryDiscountStatus: "APPROVED"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-COUPON-003", "75.00");

    expect(screen.getByText("Applied adjustment: -PHP 50.00")).toBeInTheDocument();
    expect(screen.getByText("Approved.")).toBeInTheDocument();
    expect(screen.getAllByText("-PHP 50.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PHP 75.00").length).toBeGreaterThan(0);
  });

  it.each([
    ["REJECTED", "Rejected."],
    ["EXPIRED", "Expired."],
    [undefined, "No approved statutory discount found."]
  ])("WebPay_WhenStatutoryStatusIs%s_DisplaysReadOnlyStatus", async (status, expectedLabel) => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        statutoryDiscountStatus: status
      }
    });

    render(<App />);

    await resolveTicket("TICKET-STAT-003");
    expect(screen.getByText(expectedLabel)).toBeInTheDocument();
  });

  it("WebPay_WhenBackendApprovedCouponBasisReturned_PaymentInitiationUsesFinalApprovedBasis", async () => {
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        tariffSnapshotId: "77777777-7777-7777-7777-777777777777",
        amountMinorUnits: 7500,
        originalAmountMinorUnits: 12500,
        couponAdjustmentMinorUnits: 5000,
        totalAdjustmentMinorUnits: 5000,
        couponStatus: "APPROVED"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-COUPON-004", "75.00");
    await continueToPayment();

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1));
    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const body = JSON.parse((paymentIntentCall[1] as RequestInit).body as string);
    expect(body.tariffSnapshotId).toBe("77777777-7777-7777-7777-777777777777");
    expect(body.expectedAmountMinorUnits).toBe(7500);
  });

  it("WebPay_WhenPaymentInitiated_DiscountAndCouponRemainReadOnly", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        tariffSnapshotId: "77777777-7777-7777-7777-777777777777",
        amountMinorUnits: 7500,
        originalAmountMinorUnits: 12500,
        couponAdjustmentMinorUnits: 5000,
        totalAdjustmentMinorUnits: 5000,
        couponStatus: "APPROVED"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-COUPON-005", "75.00");
    await continueToPayment();

    expect(await screen.findByRole("link", { name: /continue to payment/i })).toBeInTheDocument();
    expect(screen.queryByLabelText(/coupon code/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /apply coupon/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /request statutory discount/i })).not.toBeInTheDocument();
  });

  it("WebPay_WhenResolved_DoesNotRenderStatutoryEvidenceControls", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-STAT-004");

    expect(screen.getByText("No approved statutory discount found.")).toBeInTheDocument();
    expect(screen.queryByLabelText(/evidence reference/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /request statutory discount/i })).toBeInTheDocument();
  });

  it("WebPay_WhenResolvedSessionIsPaid_DoesNotCreatePaymentIntent", async () => {
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid"
      }
    });

    render(<App />);

    await resolveTicket("WEBPAY-20260521-FRESH-005");

    expect(await screen.findByRole("heading", { name: /payment completed/i })).toBeInTheDocument();
    expect(screen.getByText("Payment Status")).toBeInTheDocument();
    expect(screen.getByText("Paid")).toBeInTheDocument();
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.getByText("Payment Completed")).toBeInTheDocument();
    expect(screen.queryByText("PaymentRequired")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /payment completed/i })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: /payment completed/i }));

    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(0);
  });

  it("WebPay_WhenResolvedSessionHasContext_UsesResolvedContextForPaymentIntentInsteadOfDefaults", async () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_GROUP_ID", "00000000-0000-0000-0000-000000000001");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_ID", "00000000-0000-0000-0000-000000000002");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "00000000-0000-0000-0000-000000000003");
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("WEBPAY-20260519-FRESH-001");
    await continueToPayment();

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1));
    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const body = JSON.parse((paymentIntentCall[1] as RequestInit).body as string);
    expect(body.siteGroupId).toBe("29b8b4f4-40dd-447b-ac06-dd52e6ad51c5");
    expect(body.siteId).toBe("93bd3cb3-e806-4c5c-ac8c-df6c4addff14");
    expect(body.vendorSystemId).toBe("45a625de-9034-4fb6-b527-0950d384e51f");
    expect(body.siteGroupId).not.toBe("00000000-0000-0000-0000-000000000001");
    expect(body.siteId).not.toBe("00000000-0000-0000-0000-000000000002");
    expect(body.vendorSystemId).not.toBe("00000000-0000-0000-0000-000000000003");
  });

  it("WebPay_WhenMay21SessionResolved_DoesNotUseMockIdsOrRender2030Dates", async () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_GROUP_ID", "00000000-0000-0000-0000-000000000001");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_ID", "00000000-0000-0000-0000-000000000002");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "00000000-0000-0000-0000-000000000003");
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        siteGroupId: "d392e487-fba0-4281-bdf4-8d62f923d518",
        siteId: "6c4b92bd-ced4-44e3-a61a-4349aa81f91d",
        vendorSystemId: "25831de5-7144-4a34-a6ea-4ef2bd65c89c",
        siteGroupName: "WebPay Test Site Group 2026-05-21",
        siteName: "WebPay Test Site 2026-05-21",
        ticketReference: "WEBPAY-20260521-FRESH-001",
        plateNumber: "WEBPAY001",
        entryTime: "2026-05-20T18:01:00+00:00",
        currentFeeCalculationTime: "2026-05-21T00:00:00+00:00",
        feeValidUntil: "2026-05-21T15:59:59+00:00",
        amountMinorUnits: 10000,
        parkingStatus: "PaymentRequired",
        paymentStatus: "Not Started"
      },
      intentPayload: {
        ...successResponse,
        siteGroupId: "d392e487-fba0-4281-bdf4-8d62f923d518",
        siteId: "6c4b92bd-ced4-44e3-a61a-4349aa81f91d",
        vendorSystemId: "25831de5-7144-4a34-a6ea-4ef2bd65c89c",
        ticketReference: "WEBPAY-20260521-FRESH-001",
        entryTime: "2026-05-20T18:01:00+00:00",
        currentFeeCalculationTime: "2026-05-21T00:00:00+00:00",
        feeValidUntil: "2026-05-21T15:59:59+00:00",
        amountMinorUnits: 10000
      }
    });

    render(<App />);

    await resolveTicket("WEBPAY-20260521-FRESH-001", "100.00");

    expect(screen.getAllByText("WebPay Test Site 2026-05-21").length).toBeGreaterThan(0);
    expect(screen.getAllByText("WEBPAY-20260521-FRESH-001").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/May 21, 2026/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/Apr 1, 2030/i)).not.toBeInTheDocument();

    await continueToPayment();

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1));
    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const body = JSON.parse((paymentIntentCall[1] as RequestInit).body as string);
    expect(body.siteGroupId).toBe("d392e487-fba0-4281-bdf4-8d62f923d518");
    expect(body.siteId).toBe("6c4b92bd-ced4-44e3-a61a-4349aa81f91d");
    expect(body.vendorSystemId).toBe("25831de5-7144-4a34-a6ea-4ef2bd65c89c");
    expect(body.siteGroupId).not.toBe("00000000-0000-0000-0000-000000000001");
    expect(body.siteId).not.toBe("00000000-0000-0000-0000-000000000002");
    expect(body.vendorSystemId).not.toBe("00000000-0000-0000-0000-000000000003");
  });

  it("WebPay_WhenTicketChanges_ClearsStalePaymentIntentError", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 502,
      intentPayload: { errorCode: "PAYMENT_FAILED", message: "Payment intent creation failed. Please try again." }
    });

    render(<App />);

    await resolveTicket("TICKET-TEST-027");
    await continueToPayment();
    expect(await screen.findByRole("alert")).toHaveTextContent("Payment intent creation failed");

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "8");

    expect(screen.queryByText(/Payment intent creation failed/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenPayableBasisRefreshRequired_ShowsRecalculateFeeAction", async () => {
    const refreshedResponse = {
      ...successResponse,
      tariffSnapshotId: "88888888-8888-8888-8888-888888888888",
      amountMinorUnits: 12600
    };
    let resolveCount = 0;
    const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => {
      const isResolve = url.includes("/v1/webpay/parking-session");
      if (isResolve) {
        resolveCount += 1;
        return {
          ok: true,
          status: 200,
          json: async () => (resolveCount === 1 ? successResponse : refreshedResponse)
        };
      }

      return {
        ok: false,
        status: 409,
        json: async () => ({
          errorCode: "PAYABLE_BASIS_REFRESH_REQUIRED",
          message: "Tariff snapshot has expired. Refresh the payable basis before retrying payment.",
          retryable: true,
          correlationId: "77777777-7777-7777-7777-777777777777"
        })
      };
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await resolveTicket("TICKET-EXPIRED-001");
    await continueToPayment();

    expect(await screen.findByRole("alert")).toHaveTextContent("Your parking fee quote has expired. Please recalculate the fee to continue.");
    expect(screen.getByRole("button", { name: /recalculate fee/i })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment completed/i })).not.toBeInTheDocument();
    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1);
    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const paymentIntentBody = JSON.parse(paymentIntentCall[1]?.body as string) as Record<string, unknown>;
    expect(paymentIntentBody.tariffSnapshotId).toBe(successResponse.tariffSnapshotId);
    expect(paymentIntentBody.expectedAmountMinorUnits).toBe(successResponse.amountMinorUnits);

    await userEvent.click(screen.getByRole("button", { name: /recalculate fee/i }));

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(2));
    expect(screen.getAllByText("126.00").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: /recalculate fee/i })).not.toBeInTheDocument();
  });

  it("WebPay_WhenPayableBasisLocked_ShowsRestartOnlyMessage", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        errorCode: "PAYABLE_BASIS_LOCKED",
        message: "The payable basis changed before payment initiation. Restart from parking lookup.",
        retryable: false,
        correlationId: "77777777-7777-7777-7777-777777777777"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-LOCKED-001");
    await continueToPayment();

    expect(await screen.findByRole("alert")).toHaveTextContent("The payable amount changed or payment has already started. Please restart from lookup.");
    expect(screen.queryByRole("button", { name: /recalculate fee/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment completed/i })).not.toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsHandoff_DisplaysContinueToPayment", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("link", { name: /continue to payment/i })).toHaveAttribute(
      "href",
      "https://payments.test/handoff"
    );
    expect(screen.getAllByText("PHP").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PENDING_PROVIDER").length).toBeGreaterThan(0);
    expect(screen.getAllByText("125.00").length).toBeGreaterThan(0);
  });

  it("WebPay_WhenApiReturnsSessionSummary_DisplaysParkingSessionSummary", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(await screen.findByRole("heading", { name: /mactan newtown parking/i })).toBeInTheDocument();
    expect(screen.getByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.getByText("Site Group")).toBeInTheDocument();
    expect(screen.getByText("WebPay Test Site Group 2026-05-19")).toBeInTheDocument();
    expect(screen.getByText("Site Name")).toBeInTheDocument();
    expect(screen.getAllByText("Ticket").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Plate").length).toBeGreaterThan(0);
    expect(screen.getByText("Entry Time")).toBeInTheDocument();
    expect(screen.getByText("Duration")).toBeInTheDocument();
    expect(screen.getByText("Total Fee")).toBeInTheDocument();
    expect(screen.getAllByText("Amount Due").length).toBeGreaterThan(0);
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.getByText("Payment Status")).toBeInTheDocument();
    expect(screen.getByText("Fee Valid Until")).toBeInTheDocument();
    expect(screen.getByText("TICKET-TEST-023")).toBeInTheDocument();
    expect(screen.getByText("ABC 1234")).toBeInTheDocument();
    expect(screen.getAllByText(/May 18, 2026/).length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("125.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PHP 125.00").length).toBeGreaterThan(0);
    expect(screen.queryByText(/Apr 1, 2030/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenNewParkingSessionIsResolved_ClearsStalePaymentState", async () => {
    const fetchMock = stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        ...activePaymentAttemptConflict,
        handoff: {
          type: "Redirect",
          resumePaymentUrl: "https://payments.test/existing"
        }
      }
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();
    expect(await screen.findByRole("link", { name: /continue existing payment/i })).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText(/ticket reference/i));
    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-002");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /continue existing payment/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment already started/i })).not.toBeInTheDocument();
    expect(screen.queryByText("https://payments.test/existing")).not.toBeInTheDocument();
    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(2);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1);
  });

  it("WebPay_WhenSiteNameMissing_UsesSafeGenericSiteName", async () => {
    stubWebPayFetch({ resolvePayload: { ...successResponse, siteName: undefined } });

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(await screen.findByRole("heading", { name: /^parking site$/i })).toBeInTheDocument();
    expect(screen.getAllByText("Parking Site").length).toBeGreaterThan(0);
    expect(screen.queryByText("22222222-2222-2222-2222-222222222222")).not.toBeInTheDocument();
  });

  it("WebPay_WhenBackendReturnsGuidDerivedSiteNames_UsesSafeGenericNames", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        siteGroupName: "Site Group bca924a0a27f5b9dacca291bf1391b49",
        siteName: "Site a153da55e9895cdbafb8373eccf589e0"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(await screen.findByRole("heading", { name: /^parking site$/i })).toBeInTheDocument();
    expect(screen.getByText("Site Group")).toBeInTheDocument();
    expect(screen.getAllByText("Parking Group").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Parking Site").length).toBeGreaterThan(0);
    expect(screen.queryByText(/a153da55e9895cdbafb8373eccf589e0/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/bca924a0a27f5b9dacca291bf1391b49/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Site a153da55e9895cdbafb8373eccf589e0/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Site Group bca924a0a27f5b9dacca291bf1391b49/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenSummaryRenders_DoesNotExposeInternalUuidFieldsInNormalUi", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(screen.getByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.queryByText("55555555-5555-5555-5555-555555555555")).not.toBeInTheDocument();
    expect(screen.queryByText("66666666-6666-6666-6666-666666666666")).not.toBeInTheDocument();
    expect(screen.queryByText("44444444-4444-4444-4444-444444444444")).not.toBeInTheDocument();
  });

  it("WebPay_WhenPaymentHandoffRenders_UsesDistinctStatusLabels", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        parkingStatus: "PAYABLE"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-TEST-023");
    await continueToPayment();

    expect((await screen.findAllByText("Payment Status")).length).toBeGreaterThan(0);
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.queryByText(/^Status$/)).not.toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsQrCodeUrl_DisplaysQrPaymentInstructions", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByText(/QR payment instructions/i)).toBeInTheDocument();
    expect(screen.getByText("https://payments.test/qr.png")).toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsError_DisplaysFriendlyError", async () => {
    stubWebPayFetch({
      resolveOk: false,
      resolveStatus: 404,
      resolvePayload: { errorCode: "SESSION_NOT_FOUND", message: "Vendor parking session was not found." }
    });

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "UNKNOWN");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("could not find an active parking session");
  });

  it("WebPay_WhenActivePaymentAttemptConflict_ShowsPaymentAlreadyStarted", async () => {
    const fetchMock = stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("heading", { name: /payment already started/i })).toBeInTheDocument();
    expect(screen.getByText(/cannot be resumed directly/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1);
  });

  it("WebPay_WhenActivePaymentAttemptHasHandoff_ShowsContinueExistingPayment", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        ...activePaymentAttemptConflict,
        handoff: {
          type: "Redirect",
          resumePaymentUrl: "https://payments.test/existing"
        }
      }
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("link", { name: /continue existing payment/i })).toHaveAttribute(
      "href",
      "https://payments.test/existing"
    );
    expect(screen.getByRole("button", { name: /check status/i })).toBeInTheDocument();
  });

  it("WebPay_WhenActivePaymentAttemptHasNoHandoff_ShowsCheckStatusFallback", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect((await screen.findAllByRole("button", { name: /check status/i })).length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: /continue existing payment/i })).not.toBeInTheDocument();
    expect(screen.getByText(/cannot be resumed directly/i)).toBeInTheDocument();
  });

  it("WebPay_WhenActivePaymentAttemptVisible_OuterContinueDoesNotCreateDuplicateAttempt", async () => {
    const fetchMock = stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();
    await screen.findAllByRole("button", { name: /check status/i });
    await userEvent.click(screen.getAllByRole("button", { name: /check status/i }).at(-1)!);

    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1);
  });

  it("WebPay_WhenActivePaymentAttemptConflict_DoesNotExposeProviderChoice", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        ...activePaymentAttemptConflict,
        selectedProviderCode: "AUB",
        fallbackProviderCode: "PAYMONGO"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("heading", { name: /payment already started/i })).toBeInTheDocument();
    expect(screen.queryByText("AUB")).not.toBeInTheDocument();
    expect(screen.queryByText("PAYMONGO")).not.toBeInTheDocument();
  });

  it("WebPay_WhenActivePaymentAttemptConflict_ShowsOnlyCustomerSafeSupportReference", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();
    expect(await screen.findByText(formatCustomerSupportReference("77777777-7777-7777-7777-777777777777")!)).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("77777777-7777-7777-7777-777777777777");
    expect(document.body).not.toHaveTextContent("55555555-5555-5555-5555-555555555555");
    expect(document.body).not.toHaveTextContent("44444444-4444-4444-4444-444444444444");
  });

  it("WebPay_WhenCameraUnavailable_ShowsManualFallback", async () => {
    vi.stubGlobal("navigator", { mediaDevices: undefined });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /scan qr/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Camera is unavailable");
    expect(screen.getByLabelText(/ticket reference/i)).toBeInTheDocument();
  });

  it("WebPay_WhenCameraDenied_ShowsManualFallback", async () => {
    vi.stubGlobal("navigator", {
      mediaDevices: {
        getUserMedia: vi.fn()
      }
    });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /scan qr/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Camera permission was denied|Camera is unavailable/);
    expect(screen.getByLabelText(/ticket reference/i)).toBeInTheDocument();
  });

  it("WebPay_UsesExistingAssetPaths", () => {
    render(<App />);

    expect(screen.getByAltText("ExitPass")).toHaveAttribute("src", "/assets/logo/exitpass-logo.svg");
    expect(screen.getByAltText("Pro Parking")).toHaveAttribute("src", "/assets/logo/proparking-logo.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/qrph.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/cards-visa-mastercard.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/gcash.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/maya.png");
    expect(screen.getByLabelText(/qrph/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/gcash/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/maya/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/card/i)).toBeInTheDocument();
    expect(screen.getByText("Pay using any QRPh-supported bank or e-wallet")).toBeInTheDocument();
    expect(screen.getByText("Pay with GCash through PayMongo Checkout")).toBeInTheDocument();
    expect(screen.getByText("Pay with Maya through PayMongo Checkout")).toBeInTheDocument();
    expect(screen.getByText("Visa or Mastercard through PayMongo Checkout")).toBeInTheDocument();
    expect(screen.queryByText("AUB")).not.toBeInTheDocument();
  });

  it.each([
    ["QRPh", "QRPH"],
    ["GCash", "GCASH"],
    ["Maya", "MAYA"],
    ["Card", "CARD"]
  ])("WebPay_When%sSelected_InitiatesPaymentWithSelectedMethod", async (label, expectedMethod) => {
    const fetchMock = stubWebPayFetch({ intentPayload: { ...successResponse, paymentMethod: expectedMethod } });

    render(<App />);

    await resolveTicket("TICKET-001");
    await userEvent.click(screen.getByLabelText(new RegExp(label, "i")));
    await continueToPayment();

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1));
    const paymentIntentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    const body = JSON.parse((paymentIntentCall[1] as RequestInit).body as string);
    expect(body.paymentMethod).toBe(expectedMethod);
  });

  it("WebPay_WhenPlateEntered_SubmitsPlateNumber", async () => {
    const fetchMock = stubWebPayFetch({ intentPayload: { ...successResponse, paymentMethod: "QRPH" } });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /plate/i }));
    await userEvent.type(screen.getByLabelText(/plate number/i), "abc 1234");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));
    await screen.findByText("Parking Session Summary");
    await continueToPayment();

    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/payment-intents")).toHaveLength(1));
    const intentCall = firstRouteCall(fetchMock, "/v1/webpay/payment-intents");
    expect(intentCall).toBeDefined();
    const request = intentCall?.[1] as RequestInit;
    const body = JSON.parse(request.body as string);
    expect(body).toMatchObject({
      plateNumber: "ABC 1234",
      paymentMethod: "QRPH",
      siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
      siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
      vendorSystemId: "45a625de-9034-4fb6-b527-0950d384e51f",
      tariffSnapshotId: "66666666-6666-6666-6666-666666666666",
      expectedAmountMinorUnits: 12500
    });
    expect((request.headers as Record<string, string>)["X-Correlation-Id"]).toBe(body.correlationId);
    expect(body.correlationId).toBeTruthy();
  });

  it("WebPayReturnPage_LoadsStatusByTicketReference", async () => {
    const fetchMock = stubWebPayFetch();
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-FRESH-008");

    render(<App />);

    expect(screen.getAllByText("Checking payment status").length).toBeGreaterThan(0);
    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(1);
    const parkingSessionCall = firstRouteCall(fetchMock, "/v1/webpay/parking-session");
    const body = JSON.parse((parkingSessionCall[1] as RequestInit).body as string);
    expect(body.ticketReference).toBe("WEBPAY-20260523-FRESH-008");
  });

  it("WebPayReturnPage_DoesNotMarkPaidOnlyBecauseOfRedirect", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Not Started",
        parkingStatus: "PaymentRequired"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-FRESH-009&result=success");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment is still being verified/i })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment confirmed/i })).not.toBeInTheDocument();
    expect(screen.getByText("PaymentRequired")).toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenBackendConfirmsAndReceiptIsAvailable_DisplaysAuthoritativeSalesInvoice", async () => {
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid",
        parkingStatus: "PaymentRequired"
      }
    });
    window.history.pushState(
      {},
      "",
      "/webpay/payment-return?ticketReference=WEBPAY-20260523-FRESH-010&paymentAttemptId=44444444-4444-4444-4444-444444444444&correlationId=77777777-7777-7777-7777-777777777777"
    );

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment confirmed/i })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: /^sales invoice$/i })).toBeInTheDocument();
    expect(screen.getByText("Amount Paid")).toBeInTheDocument();
    expect(screen.getAllByText("PHP 125.00").length).toBeGreaterThan(0);
    expect(screen.getByText("Payment Completed")).toBeInTheDocument();
    expect(screen.getByText("Sales Invoice Number")).toBeInTheDocument();
    expect(screen.getAllByText("SI-20260523-000001").length).toBeGreaterThan(0);
    expect(screen.queryByRole("heading", { name: /payment receipt/i })).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some((call) => String(call[0]).includes("/receipt-presentation"))).toBe(true);
  });

  it("WebPayReturnPage_WhenReceiptIsPending_DoesNotFabricateFallbackReceipt", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid"
      },
      receiptOk: false,
      receiptStatus: 409,
      receiptPayload: {
        errorCode: "WEBPAY_RECEIPT_PRESENTATION_NOT_READY",
        message: "Fiscal issuance is not recorded; Sales Invoice presentation is not available yet.",
        retryable: false,
        correlationId: "77777777-7777-7777-7777-777777777777"
      }
    });
    window.history.pushState(
      {},
      "",
      "/webpay/payment-return?ticketReference=WEBPAY-20260523-PENDING&paymentAttemptId=44444444-4444-4444-4444-444444444444"
    );

    render(<App />);

    expect(await screen.findByText("Your payment is recorded. The Sales Invoice is still being prepared.")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment receipt/i })).not.toBeInTheDocument();
    expect(screen.queryByText("SI-20260523-000001")).not.toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenPaymentFailed_ShowsDeterministicFailure", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Failed"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-FAILED");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment failed/i })).toBeInTheDocument();
    expect(screen.getByText(/could not confirm this payment/i)).toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenPaymentExpired_ShowsDeterministicExpiry", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Expired"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-EXPIRED");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment expired/i })).toBeInTheDocument();
    expect(screen.getByText(/checkout session expired/i)).toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenConfirmedWithExitInstruction_RendersExitGuidance", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid",
        exitInstruction: {
          status: "ISSUED",
          message: "Proceed to the exit lane and present your ticket.",
          laneName: "Main Exit",
          exitBy: "2026-05-23T13:15:00+08:00"
        },
        providerRawPayload: {
          secret: "do-not-render"
        }
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-PAID");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /proceed to exit/i })).toBeInTheDocument();
    expect(screen.getByText("Proceed to the exit lane and present your ticket.")).toBeInTheDocument();
    expect(screen.getByText("Lane: Main Exit")).toBeInTheDocument();
    expect(screen.queryByText(/do-not-render/i)).not.toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenConfirmedWithoutExitAuthorization_RendersPreparingState", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid",
        exitInstruction: null,
        exitAuthorizationStatus: null
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-PREPARING");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /preparing exit authorization/i })).toBeInTheDocument();
    expect(screen.getByText(/check status again shortly/i)).toBeInTheDocument();
  });

  it("WebPayCancelledPage_AllowsRetryAndStatusRefresh", async () => {
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        ticketReference: "WEBPAY-20260523-FRESH-011",
        paymentStatus: "Not Started",
        parkingStatus: "PaymentRequired"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-cancelled?ticketReference=WEBPAY-20260523-FRESH-011");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment was cancelled/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /check status/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /retry payment/i })).toHaveAttribute(
      "href",
      "/?ticketReference=WEBPAY-20260523-FRESH-011"
    );

    await userEvent.click(screen.getByRole("button", { name: /check status/i }));
    await waitFor(() => expect(routeCalls(fetchMock, "/v1/webpay/parking-session")).toHaveLength(2));
  });
});
