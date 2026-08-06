import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildPaymentIntentBody,
  buildParkingSessionResolveBody,
  buildStatutoryDiscountPendingLifecycleRediscoveryBody,
  buildStatutoryDiscountDecisionBody,
  applyStatutoryDiscountPayableBasis,
  createPaymentIntent,
  createStatutoryApplicationIdempotencyKey,
  extractPaymentIntentContext,
  getResumeUrl,
  normalizeTicketReference,
  PayableBasisRefreshRequiredError,
  ReceiptPresentationError,
  StatutoryDiscountDecisionError,
  retrieveReceiptPresentation,
  retrievePaymentStatus,
  rediscoverStatutoryDiscountPendingLifecycle,
  retrieveStatutoryDiscountAvailability,
  retrieveStatutoryDiscountDecision,
  resolveParkingSession,
  submitStatutoryDiscountDecision,
  toStatutoryDiscountMessage,
  toStatutoryDiscountAvailabilityMessage,
  toStatutoryPendingLifecycleRediscoveryMessage,
  toReceiptPresentationMessage,
  toFriendlyError
} from "./webpay";

afterEach(() => {
  vi.unstubAllEnvs();
});

describe("WebPay QR and payment intent helpers", () => {
  it("WebPay_WhenQrDecoded_PopulatesTicketReference", () => {
    expect(normalizeTicketReference("https://pay.exitpass.test?ticker=no&ticketReference=TICKET-QR-001")).toBe(
      "TICKET-QR-001"
    );
    expect(normalizeTicketReference('{"ticketReference":"TICKET-JSON-001"}')).toBe("TICKET-JSON-001");
  });

  it("WebPay_WhenManualTicketEntered_SubmitsTicketReference", () => {
    expect(buildPaymentIntentBody({ ticketReference: " TICKET-001 ", paymentMethod: "QRPH" })).toMatchObject({
      ticketReference: "TICKET-001",
      paymentMethod: "QRPH"
    });
  });

  it("WebPay_WhenPlateEntered_SubmitsPlateNumber", () => {
    expect(buildPaymentIntentBody({ plateNumber: " abc 1234 ", paymentMethod: "QRPH" })).toMatchObject({
      plateNumber: "ABC 1234",
      paymentMethod: "QRPH"
    });
  });

  it("WebPay_WhenResolvingParkingSession_SubmitsLookupWithoutPaymentMethod", () => {
    const body = buildParkingSessionResolveBody({ ticketReference: " TICKET-001 " });

    expect(body).toMatchObject({
      ticketReference: "TICKET-001"
    });
    expect(body).not.toHaveProperty("paymentMethod");
  });

  it.each(["QRPH", "GCASH", "MAYA", "CARD"] as const)(
    "WebPay_WhenAllowedPaymentMethodSelected_Submits%s",
    async (paymentMethod) => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ status: "PENDING_PROVIDER" })
    });

    await createPaymentIntent(
      { ticketReference: "TICKET-001", paymentMethod, vendorSystemId: "HIKCENTRAL" },
      fetchMock as never
    );

    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    const headers = fetchMock.mock.calls[0][1].headers as Record<string, string>;
    expect(body.paymentMethod).toBe(paymentMethod);
    expect(body.ticketReference).toBe("TICKET-001");
    expect(headers["X-Correlation-Id"]).toBe(body.correlationId);
    expect(body.correlationId).toBeTruthy();
  });

  it("WebPay_WhenApprovedPayableBasisProvided_IncludesExpectedTariffAndAmount", () => {
    const body = buildPaymentIntentBody({
      ticketReference: "TICKET-001",
      paymentMethod: "QRPH",
      tariffSnapshotId: "77777777-7777-7777-7777-777777777777",
      expectedAmountMinorUnits: 7500,
      expectedCurrency: "php"
    });

    expect(body.tariffSnapshotId).toBe("77777777-7777-7777-7777-777777777777");
    expect(body.expectedAmountMinorUnits).toBe(7500);
    expect(body.expectedCurrency).toBe("PHP");
  });

  it("WebPay_WhenAppliedStatutoryBasisProvided_IncludesOnlyCanonicalPaymentGateFacts", () => {
    const body = buildPaymentIntentBody({
      ticketReference: "TICKET-001",
      paymentMethod: "GCASH",
      tariffSnapshotId: "99999999-9999-4999-8999-999999999999",
      expectedAmountMinorUnits: 4000,
      expectedCurrency: "php",
      statutoryDiscountDecisionCommandId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
    });

    expect(body.tariffSnapshotId).toBe("99999999-9999-4999-8999-999999999999");
    expect(body.expectedAmountMinorUnits).toBe(4000);
    expect(body.expectedCurrency).toBe("PHP");
    expect(body.statutoryDiscountDecisionCommandId).toBe("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    expect(body.statutoryDiscountPayableBasisApplicationCommandId).toBe("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    expect(body).not.toHaveProperty("statutoryDiscountAmountMinorUnits");
    expect(body).not.toHaveProperty("vatAmountMinorUnits");
    expect(body).not.toHaveProperty("vatExclusiveBasisAmountMinorUnits");
    expect(body).not.toHaveProperty("sourceChannel");
    expect(body).not.toHaveProperty("reviewerUserId");
  });

  it("WebPay_WhenStatutoryDecisionSubmitted_UsesWebPayProxyRouteWithIdempotencyAndCorrelation", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => statutoryDecisionResponse({ payableBasisReadinessStatus: "AWAITING_REVIEW" })
    });

    await submitStatutoryDiscountDecision(
      statutoryDecisionRequest(),
      "statutory-decision:webpay:test",
      "77777777-7777-7777-7777-777777777777",
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/statutory-discounts/decisions");
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(request.method).toBe("POST");
    const headers = request.headers as Record<string, string>;
    expect(headers["Idempotency-Key"]).toBe("statutory-decision:webpay:test");
    expect(headers["X-Correlation-Id"]).toBe("77777777-7777-7777-7777-777777777777");
    const body = JSON.parse(request.body as string);
    expect(body.entitlementType).toBe("SENIOR_CITIZEN");
    expect(body.maskedIdReference).toBe("SC-****-1234");
    expect(body.evidenceCaptureRequested).toBe(false);
    expect(body).not.toHaveProperty("sourceChannel");
    expect(body).not.toHaveProperty("reviewerUserId");
    expect(body).not.toHaveProperty("reviewerAttestation");
    expect(body).not.toHaveProperty("operatorDeviceBindingId");
    expect(body).not.toHaveProperty("operatorShiftId");
    expect(body).not.toHaveProperty("statutoryDiscountAmountMinorUnits");
    expect(body).not.toHaveProperty("vatAmountMinorUnits");
    expect(body).not.toHaveProperty("finalPayableAmountMinorUnits");
    expect(body).not.toHaveProperty("appliedTariffSnapshotId");
  });

  it("WebPay_WhenStatutoryAvailabilityRequested_UsesSameOriginProxyWithSafeSessionScope", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => statutoryAvailabilityResponse()
    });

    const result = await retrieveStatutoryDiscountAvailability(
      {
        parkingSessionId: "55555555-5555-5555-5555-555555555555",
        siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
        siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"
      },
      "PWD",
      "77777777-7777-7777-7777-777777777777",
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/statutory-discounts/availability");
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(request.method).toBe("POST");
    const headers = request.headers as Record<string, string>;
    expect(headers["X-Correlation-Id"]).toBe("77777777-7777-7777-7777-777777777777");
    expect(headers).not.toHaveProperty("X-ExitPass-Service-Identity-Id");
    expect(headers).not.toHaveProperty("X-ExitPass-Permissions");
    expect(headers).not.toHaveProperty("Authorization");
    const body = JSON.parse(request.body as string);
    expect(body.parkingSessionId).toBe("55555555-5555-5555-5555-555555555555");
    expect(body.siteId).toBe("93bd3cb3-e806-4c5c-ac8c-df6c4addff14");
    expect(body.siteGroupId).toBe("29b8b4f4-40dd-447b-ac06-dd52e6ad51c5");
    expect(body.requestedEntitlementType).toBe("PWD");
    expect(body).not.toHaveProperty("sourceChannel");
    expect(body).not.toHaveProperty("reviewerUserId");
    expect(body).not.toHaveProperty("evidenceReferences");
    expect(result.coveredEntitlementTypes).toEqual(["SENIOR_CITIZEN", "PWD"]);
  });

  it("WebPay_WhenStatutoryAvailabilityIsUnavailable_MapsToSafeRetryGuidance", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => ({
        errorCode: "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE",
        message: "HttpClient connection refused http://central-pms.internal",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      retrieveStatutoryDiscountAvailability(
        {
          parkingSessionId: "55555555-5555-5555-5555-555555555555",
          siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
          siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"
        },
        undefined,
        "77777777-7777-7777-7777-777777777777",
        fetchMock as never
      )
    ).rejects.toMatchObject({
      name: "StatutoryDiscountDecisionError",
      message: "Parking privilege availability is temporarily unavailable. You may continue with the regular parking amount or try again shortly.",
      retryable: true
    } satisfies Partial<StatutoryDiscountDecisionError>);
  });

  it("WebPay_WhenPendingLifecycleRediscoveryUsesParkingSession_SubmitsExactlyOneLookupMode", async () => {
    const body = buildStatutoryDiscountPendingLifecycleRediscoveryBody({
      lookupMode: "PARKING_SESSION_ID",
      parkingSessionId: " 55555555-5555-5555-5555-555555555555 ",
      ticketReference: "TICKET-SHOULD-NOT-BE-SENT",
      plateNumber: "ABC1234",
      siteId: " 93bd3cb3-e806-4c5c-ac8c-df6c4addff14 ",
      siteGroupId: " 29b8b4f4-40dd-447b-ac06-dd52e6ad51c5 ",
      vendorSystemId: " WEBPAY_LOCAL_MOCK_PMS ",
      entitlementType: "SENIOR_CITIZEN"
    });

    expect(body).toEqual({
      lookupMode: "PARKING_SESSION_ID",
      parkingSessionId: "55555555-5555-5555-5555-555555555555",
      siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
      siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
      vendorSystemId: "WEBPAY_LOCAL_MOCK_PMS",
      entitlementType: "SENIOR_CITIZEN"
    });
    expect(body).not.toHaveProperty("ticketReference");
    expect(body).not.toHaveProperty("plateNumber");
  });

  it("WebPay_WhenPendingLifecycleRediscoveryUsesTicket_SubmitsSameOriginProxyWithoutPrivilegedHeaders", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => statutoryPendingLifecycleRediscoveryResponse({ classification: "FOUND" })
    });

    const result = await rediscoverStatutoryDiscountPendingLifecycle(
      {
        lookupMode: "TICKET_REFERENCE",
        ticketReference: " WEBPAY-STAT-001 ",
        parkingSessionId: "55555555-5555-5555-5555-555555555555",
        siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
        siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
        vendorSystemId: "WEBPAY_LOCAL_MOCK_PMS"
      },
      "77777777-7777-7777-7777-777777777777",
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/statutory-discounts/pending-lifecycle/rediscover");
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(request.method).toBe("POST");
    const headers = request.headers as Record<string, string>;
    expect(headers["X-Correlation-Id"]).toBe("77777777-7777-7777-7777-777777777777");
    expect(headers).not.toHaveProperty("X-ExitPass-Service-Identity-Id");
    expect(headers).not.toHaveProperty("X-ExitPass-Permissions");
    expect(headers).not.toHaveProperty("Authorization");
    const body = JSON.parse(request.body as string);
    expect(body).toMatchObject({
      lookupMode: "TICKET_REFERENCE",
      ticketReference: "WEBPAY-STAT-001",
      siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
      siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"
    });
    expect(body).not.toHaveProperty("parkingSessionId");
    expect(result.classification).toBe("FOUND");
  });

  it("WebPay_WhenPendingLifecycleRediscoveryUsesPlate_NormalizesPlateAndKeepsTicketOut", () => {
    const body = buildStatutoryDiscountPendingLifecycleRediscoveryBody({
      lookupMode: "PLATE_NUMBER",
      plateNumber: " abc 1234 ",
      ticketReference: "TICKET-SHOULD-NOT-BE-SENT",
      siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
      siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"
    });

    expect(body).toMatchObject({
      lookupMode: "PLATE_NUMBER",
      plateNumber: "ABC 1234"
    });
    expect(body).not.toHaveProperty("ticketReference");
    expect(body).not.toHaveProperty("parkingSessionId");
  });

  it.each([
    ["NOT_FOUND", "not found"],
    ["NO_ACTIVE_LIFECYCLE", "no active lifecycle"],
    ["AMBIGUOUS_SESSION", "ambiguous"],
    ["SOURCE_UNAVAILABLE", "temporarily unavailable"],
    ["MALFORMED_AUTHORITATIVE_STATE", "malformed"],
    ["ACCESS_DENIED", "authenticated Central PMS service identity required"],
    ["UNEXPECTED_FAILURE", "SocketException http://central-pms.internal"]
  ])("WebPay_WhenPendingLifecycleRediscoveryReturns%s_ConsumesSafeClassification", async (classification, unsafeMessage) => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => statutoryPendingLifecycleRediscoveryResponse({
        classification,
        safeMessage: unsafeMessage
      })
    });

    const result = await rediscoverStatutoryDiscountPendingLifecycle(
      {
        lookupMode: "PARKING_SESSION_ID",
        parkingSessionId: "55555555-5555-5555-5555-555555555555",
        siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
        siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"
      },
      undefined,
      fetchMock as never
    );

    expect(result.classification).toBe(classification);
    expect(fetchMock.mock.calls).toHaveLength(1);
  });

  it.each([
    ["WEBPAY_STATUTORY_PENDING_LIFECYCLE_REDISCOVERY_REQUEST_INVALID", "HttpClient should not leak", "could not be checked for this parking session"],
    ["WEBPAY_STATUTORY_SERVICE_UNAVAILABLE", "authenticated Central PMS operator/service identity required", "could not be checked right now"],
    ["WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE", "SocketException http://central-pms.internal", "could not be checked right now"],
    ["ACCESS_DENIED", "permission statutory-discounts.pending-lifecycle.rediscover.webpay denied", "temporarily unavailable"]
  ])("WebPay_WhenPendingLifecycleRediscoveryFailsWith%s_ReturnsSafeMessage", async (errorCode, unsafeMessage, expectedMessage) => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => ({
        errorCode,
        message: unsafeMessage,
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      rediscoverStatutoryDiscountPendingLifecycle(
        {
          lookupMode: "PARKING_SESSION_ID",
          parkingSessionId: "55555555-5555-5555-5555-555555555555",
          siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
          siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"
        },
        "77777777-7777-7777-7777-777777777777",
        fetchMock as never
      )
    ).rejects.toMatchObject({
      name: "StatutoryDiscountDecisionError",
      message: expect.stringContaining(expectedMessage),
      retryable: true,
      correlationId: "77777777-7777-7777-7777-777777777777"
    } satisfies Partial<StatutoryDiscountDecisionError>);
  });

  it("WebPay_WhenStatutoryDecisionReadbackRequested_UsesGetOnlyWebPayProxyRoute", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => statutoryDecisionResponse({ payableBasisReadinessStatus: "AWAITING_REVIEW" })
    });

    const result = await retrieveStatutoryDiscountDecision(
      "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      "77777777-7777-7777-7777-777777777777",
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/statutory-discounts/decisions/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    expect((fetchMock.mock.calls[0][1] as RequestInit).method).toBe("GET");
    expect(result.payableBasisReadinessStatus).toBe("AWAITING_REVIEW");
  });

  it("WebPay_WhenStatutoryServiceAuthFails_ShowsCustomerSafeMessage", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      json: async () => ({
        errorCode: "WEBPAY_STATUTORY_SERVICE_UNAVAILABLE",
        message:
          "Authenticated Central PMS service identity required by CentralPmsStatutoryDiscountDecisionSubmit policy.",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      submitStatutoryDiscountDecision(
        statutoryDecisionRequest(),
        "statutory-decision:webpay:test",
        "77777777-7777-7777-7777-777777777777",
        fetchMock as never
      )
    ).rejects.toThrow("Parking-privilege requests are temporarily unavailable");
  });

  it("WebPay_WhenStatutoryServiceUnavailable_ShowsSafeRetryGuidance", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      json: async () => ({
        errorCode: "WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE",
        message: "Central PMS timeout at http://central-pms.internal.",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      retrieveStatutoryDiscountDecision(
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        "77777777-7777-7777-7777-777777777777",
        fetchMock as never
      )
    ).rejects.toThrow("Statutory discount status is temporarily unavailable");
  });

  it("WebPay_WhenApplicationIntentKeyCreated_UsesCanonicalDecisionIdentifier", () => {
    expect(createStatutoryApplicationIdempotencyKey(" aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa ")).toBe(
      "webpay-statutory-discount-application:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    );

    expect(() => createStatutoryApplicationIdempotencyKey(" ")).toThrow(/request reference/i);
  });

  it("WebPay_WhenApprovedDiscountApplied_UsesWebPayProxyRouteWithOriginalKeyAndSafeRequest", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () =>
        statutoryDecisionResponse({
          decisionCommandStatus: "COMPLETED",
          decisionResultStatus: "APPROVED",
          applicationCommandStatus: "PROCESSING",
          applicationResultClassification: "PROCESSING",
          payableBasisReadinessStatus: "APPLICATION_PROCESSING",
          payableBasisReadinessAction: "POLL_READBACK"
        })
    });

    const result = await applyStatutoryDiscountPayableBasis(
      "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      statutoryDecisionRequest(),
      "statutory-application:webpay:original",
      "77777777-7777-7777-7777-777777777777",
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/v1/webpay/statutory-discounts/decisions/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/apply-payable-basis"
    );
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(request.method).toBe("POST");
    const headers = request.headers as Record<string, string>;
    expect(headers["Idempotency-Key"]).toBe("statutory-application:webpay:original");
    expect(headers["X-Correlation-Id"]).toBe("77777777-7777-7777-7777-777777777777");
    const body = JSON.parse(request.body as string);
    expect(body.entitlementType).toBe("SENIOR_CITIZEN");
    expect(body.requestReference).toBe("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    expect(body).not.toHaveProperty("sourceChannel");
    expect(body).not.toHaveProperty("reviewerUserId");
    expect(body).not.toHaveProperty("reviewerDecision");
    expect(body).not.toHaveProperty("reviewerAttestation");
    expect(body).not.toHaveProperty("operatorDeviceBindingId");
    expect(body).not.toHaveProperty("operatorShiftId");
    expect(body).not.toHaveProperty("statutoryDiscountAmountMinorUnits");
    expect(body).not.toHaveProperty("vatAmountMinorUnits");
    expect(body).not.toHaveProperty("finalPayableAmountMinorUnits");
    expect(body).not.toHaveProperty("appliedTariffSnapshotId");
    expect(result.applicationCommandStatus).toBe("PROCESSING");
  });

  it("WebPay_WhenApplicationIntentInputIsMissing_RejectsBeforeApiCall", async () => {
    const fetchMock = vi.fn();

    await expect(
      applyStatutoryDiscountPayableBasis(
        " ",
        statutoryDecisionRequest(),
        "statutory-application:webpay:original",
        undefined,
        fetchMock as never
      )
    ).rejects.toThrow(/request reference/i);
    await expect(
      applyStatutoryDiscountPayableBasis(
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        statutoryDecisionRequest(),
        " ",
        undefined,
        fetchMock as never
      )
    ).rejects.toThrow(/request key/i);

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    [
      "processing",
      statutoryDecisionResponse({
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "PROCESSING",
        applicationResultClassification: "PROCESSING",
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "POLL_READBACK"
      }),
      "APPLICATION_PROCESSING"
    ],
    [
      "applied",
      statutoryDecisionResponse({
        decisionCommandStatus: "COMPLETED",
        decisionResultStatus: "APPROVED",
        applicationCommandStatus: "APPLIED",
        applicationResultClassification: "APPLIED",
        payableBasisReady: true,
        payableBasisReadinessStatus: "READY",
        payableBasisReadinessAction: null,
        appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
        finalPayableAmountMinorUnits: 10000,
        currency: "PHP"
      }),
      "READY"
    ],
    [
      "retryable",
      statutoryDecisionResponse({
        retryable: true,
        safeErrorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
        payableBasisReadinessStatus: "APPLICATION_PROCESSING",
        payableBasisReadinessAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY",
        recoveryAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY"
      }),
      "APPLICATION_PROCESSING"
    ],
    [
      "semantic conflict",
      statutoryDecisionResponse({
        safeErrorCode: "STATUTORY_DISCOUNT_APPLICATION_SEMANTIC_CONFLICT",
        payableBasisReadinessStatus: "APPLICATION_SEMANTIC_CONFLICT",
        payableBasisReadinessAction: "DO_NOT_RETRY",
        overallResultClassification: "TERMINAL_FAILURE"
      }),
      "APPLICATION_SEMANTIC_CONFLICT"
    ],
    [
      "terminal",
      statutoryDecisionResponse({
        safeErrorCode: "STATUTORY_DISCOUNT_APPLICATION_FAILED",
        payableBasisReadinessStatus: "FAILED",
        payableBasisReadinessAction: "DO_NOT_RETRY",
        overallResultClassification: "TERMINAL_FAILURE"
      }),
      "FAILED"
    ]
  ])("WebPay_WhenApplicationIntentReturns%s_MapsSafeResponse", async (_caseName, payload, expectedStatus) => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => payload
    });

    const result = await applyStatutoryDiscountPayableBasis(
      "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      statutoryDecisionRequest(),
      "statutory-application:webpay:original",
      undefined,
      fetchMock as never
    );

    expect(result.payableBasisReadinessStatus).toBe(expectedStatus);
  });

  it("WebPay_WhenApplicationIntentFails_DoesNotExposeRawDownstreamBody", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => ({
        errorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
        message: "http://central-pms.internal stack trace raw body",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      applyStatutoryDiscountPayableBasis(
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        statutoryDecisionRequest(),
        "statutory-application:webpay:original",
        "77777777-7777-7777-7777-777777777777",
        fetchMock as never
      )
    ).rejects.toMatchObject({
      name: "StatutoryDiscountDecisionError",
      message: "Statutory discount status is temporarily unavailable. Refresh status shortly.",
      retryable: true
    } satisfies Partial<StatutoryDiscountDecisionError>);
  });

  it("WebPay_WhenStatutoryRequestIsBuilt_RejectsUnsafeFullIdAndUnsupportedFields", () => {
    expect(() =>
      buildStatutoryDiscountDecisionBody({
        ...statutoryDecisionRequest(),
        maskedIdReference: "123456789012"
      })
    ).toThrow(/let WebPay mask it automatically/i);

    const body = buildStatutoryDiscountDecisionBody({
      ...statutoryDecisionRequest(),
      entitlementType: "PWD",
      attestationNotes: " Needs review "
    });

    expect(body.entitlementType).toBe("PWD");
    expect(body.attestationNotes).toBe("Needs review");
    expect(JSON.stringify(body)).not.toContain("sourceChannel");
    expect(JSON.stringify(body)).not.toContain("reviewer");
  });

  it.each([
    ["AWAITING_REVIEW", "Your statutory discount request is awaiting review."],
    ["DECISION_APPROVED_APPLICATION_NOT_REQUESTED", "Entitlement was approved. Discount application is pending and payment is not ready yet."],
    ["DECISION_REJECTED", "The statutory discount request was not approved."],
    ["STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE", "temporarily unavailable"],
    ["TERMINAL_FAILURE", "could not be completed"]
  ])("WebPay_WhenStatutoryErrorIs%s_MapsSafeMessage", (code, expected) => {
    expect(toStatutoryDiscountMessage(code)).toContain(expected);
  });

  it("WebPay_WhenStatutoryReadbackFails_DoesNotExposeRawDownstreamBody", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => ({
        errorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
        message: "http://central-pms.internal stack trace raw body",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      retrieveStatutoryDiscountDecision(
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        "77777777-7777-7777-7777-777777777777",
        fetchMock as never
      )
    ).rejects.toMatchObject({
      name: "StatutoryDiscountDecisionError",
      message: "Statutory discount status is temporarily unavailable. Refresh status shortly.",
      retryable: true
    } satisfies Partial<StatutoryDiscountDecisionError>);
  });

  it("WebPay_WhenCorrelationIdIsProvided_PreservesItInPaymentIntentBodyAndHeader", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ status: "PENDING_PROVIDER" })
    });

    await createPaymentIntent(
      {
        ticketReference: "TICKET-001",
        paymentMethod: "QRPH",
        vendorSystemId: "HIKCENTRAL",
        correlationId: "77777777-7777-7777-7777-777777777777"
      },
      fetchMock as never
    );

    const request = fetchMock.mock.calls[0][1] as RequestInit;
    const body = JSON.parse(request.body as string);
    const headers = request.headers as Record<string, string>;

    expect(body.correlationId).toBe("77777777-7777-7777-7777-777777777777");
    expect(headers["X-Correlation-Id"]).toBe("77777777-7777-7777-7777-777777777777");
  });

  it("WebPay_WhenUnsupportedPaymentMethodIsForced_RejectsBeforeApiCall", async () => {
    const fetchMock = vi.fn();

    await expect(
      createPaymentIntent(
        { ticketReference: "TICKET-001", paymentMethod: "BANK_TRANSFER" as never, vendorSystemId: "HIKCENTRAL" },
        fetchMock as never
      )
    ).rejects.toThrow("Only QRPh, GCash, Maya, and Card payment through PayMongo Checkout are available right now.");

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("WebPay_WhenApiBaseUrlIsUnset_SubmitsPaymentIntentToSameOriginV1Path", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ status: "PENDING_PROVIDER" })
    });

    await createPaymentIntent(
      { ticketReference: "TICKET-001", paymentMethod: "QRPH", vendorSystemId: "HIKCENTRAL" },
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/payment-intents");
  });

  it("WebPay_WhenParkingSessionResolveSucceeds_MapsSummaryFields", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
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
        parkingStatus: "PAYABLE",
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    const result = await resolveParkingSession(
      { ticketReference: "TICKET-TEST-023", vendorSystemId: "HIKCENTRAL" },
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/parking-session");
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    const body = JSON.parse(request.body as string);
    const headers = request.headers as Record<string, string>;
    expect(headers["X-Correlation-Id"]).toBe(body.correlationId);
    expect(body.correlationId).toBeTruthy();
    expect(result.siteName).toBe("Mactan Newtown Parking");
    expect(result.siteGroupId).toBe("29b8b4f4-40dd-447b-ac06-dd52e6ad51c5");
    expect(result.siteId).toBe("93bd3cb3-e806-4c5c-ac8c-df6c4addff14");
    expect(result.vendorSystemId).toBe("45a625de-9034-4fb6-b527-0950d384e51f");
    expect(result.siteGroupName).toBe("WebPay Test Site Group 2026-05-19");
    expect(result.parkingStatus).toBe("PAYABLE");
    expect(result.amountMinorUnits).toBe(12500);
  });

  it("WebPay_WhenRetrievingPaymentStatus_UsesReadOnlyParkingSessionStatusPath", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        parkingSessionId: "55555555-5555-5555-5555-555555555555",
        tariffSnapshotId: "66666666-6666-6666-6666-666666666666",
        amountMinorUnits: 12500,
        currency: "PHP",
        paymentStatus: "Paid",
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    const result = await retrievePaymentStatus(
      { ticketReference: "TICKET-TEST-023", vendorSystemId: "HIKCENTRAL" },
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/parking-session");
    expect((fetchMock.mock.calls[0][1] as RequestInit).method).toBe("POST");
    expect(result.paymentStatus).toBe("Paid");
  });

  it("WebPay_WhenRetrievingReceiptPresentation_UsesReadOnlyPaymentAttemptPresentationPath", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        paymentAttemptId: "44444444-4444-4444-4444-444444444444",
        paymentConfirmationId: "11111111-1111-1111-1111-111111111111",
        fiscalIssuanceReferenceId: "22222222-2222-2222-2222-222222222222",
        fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
        posFiscalDocumentId: "33333333-3333-3333-3333-333333333333",
        fiscalDocumentNumber: "SI-20260523-000001",
        receiptAvailabilityState: "AVAILABLE",
        authoritativePresentation: {
          presentation: {
            documentTitle: "Sales Invoice",
            sections: []
          }
        },
        createdAt: "2026-05-23T13:00:00+08:00",
        updatedAt: "2026-05-23T13:01:00+08:00",
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    const result = await retrieveReceiptPresentation(
      "44444444-4444-4444-4444-444444444444",
      "77777777-7777-7777-7777-777777777777",
      fetchMock as never
    );

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/v1/webpay/payment-attempts/44444444-4444-4444-4444-444444444444/receipt-presentation"
    );
    expect((fetchMock.mock.calls[0][1] as RequestInit).method).toBe("GET");
    expect(((fetchMock.mock.calls[0][1] as RequestInit).headers as Record<string, string>)["X-Correlation-Id"]).toBe(
      "77777777-7777-7777-7777-777777777777"
    );
    expect(result.authoritativePresentation.presentation?.documentTitle).toBe("Sales Invoice");
  });

  it("WebPay_WhenReceiptPresentationIsPending_ThrowsRetryableSafeError", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        errorCode: "WEBPAY_RECEIPT_PRESENTATION_NOT_READY",
        message: "Fiscal issuance is not recorded.",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      retrieveReceiptPresentation(
        "44444444-4444-4444-4444-444444444444",
        "77777777-7777-7777-7777-777777777777",
        fetchMock as never
      )
    ).rejects.toMatchObject({
      name: "ReceiptPresentationError",
      message: "Your payment is recorded. The Sales Invoice is still being prepared.",
      retryable: true,
      correlationId: "77777777-7777-7777-7777-777777777777"
    } satisfies Partial<ReceiptPresentationError>);
  });

  it("WebPay_WhenActivePaymentAttemptConflictReturned_ThrowsActivePaymentAttemptError", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        errorCode: "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
        message: "An active payment attempt already exists for parking session.",
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      createPaymentIntent(
        { ticketReference: "TICKET-001", paymentMethod: "QRPH", vendorSystemId: "HIKCENTRAL" },
        fetchMock as never
      )
    ).rejects.toMatchObject({
        name: "ActivePaymentAttemptError",
        activePaymentAttempt: {
          correlationId: "77777777-7777-7777-7777-777777777777"
        }
      });
  });

  it("WebPay_WhenActivePaymentAttemptIncludesResumeUrl_MapsHandoffAndSupportFields", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        errorCode: "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
        message: "Payment already started.",
        correlationId: "77777777-7777-7777-7777-777777777777",
        handoffUrl: "https://payments.test/handoff",
        resumePaymentUrl: "https://payments.test/resume",
        paymentMethod: "QRPH",
        amountMinorUnits: 12500,
        currency: "PHP",
        siteName: "Mactan Newtown Parking",
        ticketReference: "TICKET-TEST-023",
        plateNumber: "ABC 1234"
      })
    });

    await expect(
      createPaymentIntent(
        { ticketReference: "TICKET-001", paymentMethod: "QRPH", vendorSystemId: "HIKCENTRAL" },
        fetchMock as never
      )
    ).rejects.toMatchObject({
      name: "ActivePaymentAttemptError",
      activePaymentAttempt: {
        correlationId: "77777777-7777-7777-7777-777777777777",
        handoff: {
          resumePaymentUrl: "https://payments.test/resume",
          handoffUrl: "https://payments.test/handoff"
        },
        amountMinorUnits: 12500,
        currency: "PHP",
        siteName: "Mactan Newtown Parking",
        ticketReference: "TICKET-TEST-023",
        plateNumber: "ABC 1234"
      }
    });
  });

  it("WebPay_WhenPayableBasisRefreshRequired_ReturnsTypedRefreshError", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        errorCode: "PAYABLE_BASIS_REFRESH_REQUIRED",
        message: "Tariff snapshot has expired. Refresh the payable basis before retrying payment.",
        retryable: true,
        correlationId: "77777777-7777-7777-7777-777777777777"
      })
    });

    await expect(
      createPaymentIntent(
        { ticketReference: "TICKET-001", paymentMethod: "QRPH", vendorSystemId: "HIKCENTRAL" },
        fetchMock as never
      )
    ).rejects.toBeInstanceOf(PayableBasisRefreshRequiredError);
  });

  it("WebPay_GetResumeUrl_PrefersResumeThenHandoffThenCheckout", () => {
    expect(
      getResumeUrl({
        resumePaymentUrl: "https://payments.test/resume",
        handoffUrl: "https://payments.test/handoff",
        checkoutUrl: "https://payments.test/checkout"
      })
    ).toBe("https://payments.test/resume");
    expect(getResumeUrl({ handoffUrl: "https://payments.test/handoff", checkoutUrl: "https://payments.test/checkout" })).toBe(
      "https://payments.test/handoff"
    );
    expect(getResumeUrl({ checkoutUrl: "https://payments.test/checkout" })).toBe("https://payments.test/checkout");
  });

  it("WebPay_WhenDefaultSiteGroupIdIsConfigured_IncludesSiteGroupId", () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_GROUP_ID", "11111111-1111-1111-1111-111111111111");

    const body = buildPaymentIntentBody({ ticketReference: "TICKET-001", paymentMethod: "QRPH" });

    expect(body.siteGroupId).toBe("11111111-1111-1111-1111-111111111111");
  });

  it("WebPay_WhenDefaultSiteIdIsConfigured_IncludesSiteId", () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_ID", "22222222-2222-2222-2222-222222222222");

    const body = buildPaymentIntentBody({ ticketReference: "TICKET-001", paymentMethod: "QRPH" });

    expect(body.siteId).toBe("22222222-2222-2222-2222-222222222222");
  });

  it("WebPay_WhenDefaultVendorSystemIdIsConfigured_IncludesVendorSystemId", () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "HIKCENTRAL");

    const body = buildPaymentIntentBody({ ticketReference: "TICKET-001", paymentMethod: "QRPH" });

    expect(body.vendorSystemId).toBe("HIKCENTRAL");
  });

  it("WebPay_WhenVendorSystemIdIsMissing_ReturnsFriendlyConfigurationErrorBeforeSubmit", async () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "");
    const fetchMock = vi.fn();

    await expect(
      createPaymentIntent({ ticketReference: "TICKET-001", paymentMethod: "QRPH" }, fetchMock as never)
    ).rejects.toThrow("WebPay is missing vendor configuration");

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("WebPay_WhenQrUrlIncludesContext_ExtractsContextWithoutChangingTicketReference", () => {
    const qrUrl =
      "https://pay.exitpass.test?ticker=no&ticketReference=TICKET-QR-001&siteGroupId=11111111-1111-1111-1111-111111111111&siteId=22222222-2222-2222-2222-222222222222&vendorSystemId=HIKCENTRAL";

    expect(normalizeTicketReference(qrUrl)).toBe("TICKET-QR-001");
    expect(extractPaymentIntentContext(qrUrl)).toEqual({
      siteGroupId: "11111111-1111-1111-1111-111111111111",
      siteId: "22222222-2222-2222-2222-222222222222",
      vendorSystemId: "HIKCENTRAL"
    });
  });

  it("WebPay_DoesNotSubmitSelectedProviderCodeAsUserChoice", () => {
    const body = buildPaymentIntentBody({ ticketReference: "TICKET-001", paymentMethod: "QRPH" });

    expect(body).not.toHaveProperty("selectedProviderCode");
    expect(body).not.toHaveProperty("fallbackProviderCode");
    expect(body).not.toHaveProperty("preferredProviderCode");
  });

  it("WebPay_WhenApiReturnsError_DisplaysFriendlyError", () => {
    expect(toFriendlyError("SESSION_NOT_FOUND")).toContain("could not find");
    expect(toFriendlyError("VENDOR_UNAVAILABLE")).toContain("temporarily unavailable");
    expect(toFriendlyError("NO_PAYMENT_ROUTE")).toContain("not available");
    expect(toFriendlyError("WEBPAY_PAYMENT_INTENT_FAILED")).toContain("could not start payment");
    expect(toFriendlyError("PAYABLE_BASIS_REFRESH_REQUIRED")).toContain("expired");
    expect(toFriendlyError("PAYABLE_BASIS_LOCKED")).toContain("payment has already started");
  });

  it("WebPay_WhenUnknownBackendErrorsContainTechnicalIdentifiers_DoesNotReflectThem", () => {
    const raw = "HttpRequestException for 11111111-1111-4111-8111-111111111111 at http://central-pms.internal/v1";
    const messages = [
      toFriendlyError("UNEXPECTED", raw),
      toStatutoryDiscountMessage("UNEXPECTED", raw),
      toStatutoryDiscountAvailabilityMessage("UNEXPECTED", raw),
      toStatutoryPendingLifecycleRediscoveryMessage("UNEXPECTED", raw),
      toReceiptPresentationMessage("UNEXPECTED", raw)
    ];

    for (const message of messages) {
      expect(message).not.toContain("11111111-1111-4111-8111-111111111111");
      expect(message).not.toContain("central-pms.internal");
      expect(message).not.toContain("HttpRequestException");
    }
  });
});

function statutoryDecisionRequest() {
  return {
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    parkingSessionId: "55555555-5555-4555-8555-555555555555",
    siteId: "22222222-2222-4222-8222-222222222222",
    siteGroupId: "11111111-1111-4111-8111-111111111111",
    ticketReference: "TICKET-STAT-001",
    plateNumber: "ABC1234",
    entitlementType: "SENIOR_CITIZEN" as const,
    idDocumentType: "OSCA",
    issuingAuthority: "QUEZON_CITY",
    expiryDate: "2030-12-31",
    maskedIdReference: "SC-****-1234",
    evidenceCaptureRequested: false,
    requesterAttestation: true,
    originalTariffSnapshotId: "66666666-6666-4666-8666-666666666666"
  };
}

function statutoryDecisionResponse(overrides?: Record<string, unknown>) {
  return {
    statutoryDiscountDecisionCommandId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    statutoryDiscountPayableBasisApplicationCommandId: null,
    statutoryDiscountValidationId: null,
    parkingSessionId: "55555555-5555-4555-8555-555555555555",
    siteId: "22222222-2222-4222-8222-222222222222",
    siteGroupId: "11111111-1111-4111-8111-111111111111",
    entitlementType: "SENIOR_CITIZEN",
    decisionCommandStatus: "AWAITING_REVIEW",
    decisionResultStatus: "NOT_DECIDED",
    applicationCommandStatus: "NOT_REQUESTED",
    applicationResultClassification: "NOT_REQUESTED",
    payableBasisReady: false,
    payableBasisReadinessStatus: "AWAITING_REVIEW",
    payableBasisReadinessAction: "POLL_READBACK",
    originalTariffSnapshotId: "66666666-6666-4666-8666-666666666666",
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
    parkingSessionId: "55555555-5555-5555-5555-555555555555",
    siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
    siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
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
    statutoryDecisionCommandId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    entitlementType: "SENIOR_CITIZEN",
    decisionStatus: "AWAITING_REVIEW",
    payableBasisStatus: "AWAITING_REVIEW",
    parkingSessionId: "55555555-5555-5555-5555-555555555555",
    siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
    siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
    opaqueContinuationReference: "continuation:test:existing",
    opaqueContinuationUrl: "https://pay.example.test/privilege-review/opaque-existing",
    lifecycleState: "PENDING_REVIEW",
    retryable: true,
    correlationId: "77777777-7777-7777-7777-777777777777",
    createdAt: "2026-07-27T10:00:00+08:00",
    updatedAt: "2026-07-27T10:01:00+08:00",
    submittedAt: "2026-07-27T10:00:00+08:00",
    decidedAt: null,
    reviewedAt: null,
    safeMessage: null,
    ...overrides
  };
}
