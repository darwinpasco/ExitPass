import { describe, expect, it, vi } from "vitest";
import { buildPaymentIntentBody, createPaymentIntent, normalizeTicketReference, toFriendlyError } from "./webpay";

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

    await createPaymentIntent({ ticketReference: "TICKET-001", paymentMethod: "MAYA" }, fetchMock as never);

    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body.paymentMethod).toBe("MAYA");
    expect(body.ticketReference).toBe("TICKET-001");
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
