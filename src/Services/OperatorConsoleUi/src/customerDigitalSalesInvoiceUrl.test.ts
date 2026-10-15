import { describe, expect, it } from "vitest";
import { buildCustomerDigitalSalesInvoiceUrl } from "./customerDigitalSalesInvoiceUrl";

describe("customer Digital Sales Invoice URL", () => {
  it("uses only the configured public WebPay origin and opaque capability", () => {
    const url = buildCustomerDigitalSalesInvoiceUrl(
      "/webpay/sales-invoice#access=opaque-capability",
      "https://webpay.example.test"
    );

    expect(url).toBe("https://webpay.example.test/webpay/sales-invoice#access=opaque-capability");
    expect(url).not.toContain("operator-console");
  });

  it("fails closed when the public WebPay origin is missing", () => {
    expect(() => buildCustomerDigitalSalesInvoiceUrl(
      "/webpay/sales-invoice#access=opaque-capability",
      undefined
    )).toThrow("public WebPay origin is not configured");
  });

  it.each([
    "https://attacker.example/webpay/sales-invoice#access=opaque-capability",
    "/webpay/sales-invoice?ticket=1474119573241#access=opaque-capability",
    "/operator-console/fiscal-issuance-status#access=opaque-capability",
    "/webpay/sales-invoice"
  ])("rejects a non-canonical capability path: %s", (path) => {
    expect(() => buildCustomerDigitalSalesInvoiceUrl(path, "https://webpay.example.test"))
      .toThrow("capability path is invalid");
  });
});
