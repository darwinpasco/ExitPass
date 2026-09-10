import type { InvoiceCustomerInformation } from "./types";

export const invoiceCustomerInformationLimits = {
  customerName: 160,
  address: 300,
  tin: 40,
  businessStyle: 160,
  statutoryIdNumber: 80
} as const;

export const emptyInvoiceCustomerInformation: InvoiceCustomerInformation = {
  customerName: "",
  address: "",
  tin: "",
  businessStyle: "",
  statutoryIdNumber: ""
};

export function normalizeInvoiceCustomerInformation(
  value: InvoiceCustomerInformation,
  includeStatutoryId: boolean
): InvoiceCustomerInformation {
  return {
    customerName: value.customerName.trim(),
    address: value.address.trim(),
    tin: value.tin.trim(),
    businessStyle: value.businessStyle.trim(),
    statutoryIdNumber: includeStatutoryId ? value.statutoryIdNumber.trim() : ""
  };
}

export function validateInvoiceCustomerInformation(
  value: InvoiceCustomerInformation,
  includeStatutoryId: boolean
): string {
  const normalized = normalizeInvoiceCustomerInformation(value, includeStatutoryId);
  const checks: Array<[string, string, number]> = [
    ["Customer Name", normalized.customerName, invoiceCustomerInformationLimits.customerName],
    ["Address", normalized.address, invoiceCustomerInformationLimits.address],
    ["TIN", normalized.tin, invoiceCustomerInformationLimits.tin],
    ["Business Style", normalized.businessStyle, invoiceCustomerInformationLimits.businessStyle]
  ];

  if (includeStatutoryId) {
    checks.push([
      "OSCA ID No. / PWD ID No.",
      normalized.statutoryIdNumber,
      invoiceCustomerInformationLimits.statutoryIdNumber
    ]);
  }

  const invalid = checks.find(([, fieldValue, maximumLength]) => fieldValue.length > maximumLength);
  return invalid ? `${invalid[0]} must be ${invalid[2]} characters or fewer.` : "";
}
