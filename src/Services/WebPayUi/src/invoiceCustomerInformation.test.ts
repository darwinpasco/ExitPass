import { describe, expect, it } from "vitest";
import {
  invoiceCustomerInformationLimits,
  normalizeInvoiceCustomerInformation,
  validateInvoiceCustomerInformation
} from "./invoiceCustomerInformation";

describe("Sales Invoice customer information", () => {
  const input = {
    customerName: "  Juan Dela Cruz  ",
    address: "  Cebu City  ",
    tin: "  123-456-789-000  ",
    businessStyle: "  Juan Parking Services  ",
    statutoryIdNumber: "  OSCA-12345  "
  };

  it("trims safe surrounding whitespace without transforming customer-entered values", () => {
    expect(normalizeInvoiceCustomerInformation(input, true)).toEqual({
      customerName: "Juan Dela Cruz",
      address: "Cebu City",
      tin: "123-456-789-000",
      businessStyle: "Juan Parking Services",
      statutoryIdNumber: "OSCA-12345"
    });
  });

  it("removes a statutory ID when backend approval is absent", () => {
    expect(normalizeInvoiceCustomerInformation(input, false).statutoryIdNumber).toBe("");
  });

  it("supports all optional fields empty", () => {
    expect(validateInvoiceCustomerInformation({
      customerName: "",
      address: "",
      tin: "",
      businessStyle: "",
      statutoryIdNumber: ""
    }, false)).toBe("");
  });

  it("uses bounded schema-safe length validation without inventing a TIN format", () => {
    expect(validateInvoiceCustomerInformation({
      ...input,
      tin: "X".repeat(invoiceCustomerInformationLimits.tin + 1)
    }, true)).toMatch(/TIN must be 40 characters or fewer/i);
  });
});
