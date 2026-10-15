const customerSalesInvoicePath = "/webpay/sales-invoice";

export function buildCustomerDigitalSalesInvoiceUrl(path: string, publicBaseUrl: string | undefined): string {
  const configuredBase = publicBaseUrl?.trim();
  if (!configuredBase) {
    throw new Error("The public WebPay origin is not configured.");
  }

  const base = new URL(configuredBase);
  if (base.protocol !== "https:" && base.protocol !== "http:") {
    throw new Error("The public WebPay origin is invalid.");
  }

  const customerUrl = new URL(path, base);
  const fragment = new URLSearchParams(customerUrl.hash.slice(1));
  const accessToken = fragment.get("access")?.trim();
  if (customerUrl.origin !== base.origin || customerUrl.pathname !== customerSalesInvoicePath ||
      customerUrl.search || !accessToken || [...fragment.keys()].some((key) => key !== "access")) {
    throw new Error("The customer Sales Invoice capability path is invalid.");
  }

  return customerUrl.toString();
}
