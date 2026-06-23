import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildPaymentIntentBody,
  buildParkingSessionResolveBody,
  createPaymentIntent,
  extractPaymentIntentContext,
  getResumeUrl,
  normalizeTicketReference,
  PayableBasisRefreshRequiredError,
  retrievePaymentStatus,
  resolveParkingSession,
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
      expectedAmountMinorUnits: 7500
    });

    expect(body.tariffSnapshotId).toBe("77777777-7777-7777-7777-777777777777");
    expect(body.expectedAmountMinorUnits).toBe(7500);
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
});
