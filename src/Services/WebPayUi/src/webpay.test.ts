import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildPaymentIntentBody,
  createPaymentIntent,
  extractPaymentIntentContext,
  normalizeTicketReference,
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
    expect(buildPaymentIntentBody({ ticketReference: " TICKET-001 ", paymentMethod: "QRPH" })).toEqual({
      ticketReference: "TICKET-001",
      paymentMethod: "QRPH"
    });
  });

  it("WebPay_WhenPlateEntered_SubmitsPlateNumber", () => {
    expect(buildPaymentIntentBody({ plateNumber: " abc 1234 ", paymentMethod: "GCASH" })).toEqual({
      plateNumber: "ABC 1234",
      paymentMethod: "GCASH"
    });
  });

  it("WebPay_WhenPaymentMethodSelected_SubmitsPaymentMethodOnly", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ status: "PENDING_PROVIDER" })
    });

    await createPaymentIntent(
      { ticketReference: "TICKET-001", paymentMethod: "MAYA", vendorSystemId: "HIKCENTRAL" },
      fetchMock as never
    );

    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body.paymentMethod).toBe("MAYA");
    expect(body.ticketReference).toBe("TICKET-001");
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
    const body = buildPaymentIntentBody({ ticketReference: "TICKET-001", paymentMethod: "CARD" });

    expect(body).not.toHaveProperty("selectedProviderCode");
    expect(body).not.toHaveProperty("fallbackProviderCode");
    expect(body).not.toHaveProperty("preferredProviderCode");
  });

  it("WebPay_WhenApiReturnsError_DisplaysFriendlyError", () => {
    expect(toFriendlyError("SESSION_NOT_FOUND")).toContain("could not find");
    expect(toFriendlyError("VENDOR_UNAVAILABLE")).toContain("temporarily unavailable");
    expect(toFriendlyError("NO_PAYMENT_ROUTE")).toContain("not available");
    expect(toFriendlyError("WEBPAY_PAYMENT_INTENT_FAILED")).toContain("could not start payment");
  });
});
