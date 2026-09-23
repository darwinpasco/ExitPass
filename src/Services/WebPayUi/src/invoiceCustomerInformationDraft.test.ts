import { describe, expect, it } from "vitest";
import {
  clearInvoiceCustomerInformationDraft,
  loadInvoiceCustomerInformationDraft,
  saveInvoiceCustomerInformationDraft
} from "./invoiceCustomerInformationDraft";

function memoryStorage(): Storage {
  const values = new Map<string, string>();
  return {
    get length() { return values.size; },
    clear: () => values.clear(),
    getItem: (key) => values.get(key) ?? null,
    key: (index) => [...values.keys()][index] ?? null,
    removeItem: (key) => { values.delete(key); },
    setItem: (key, value) => { values.set(key, value); }
  };
}

const customerInformation = {
  customerName: "Juan Dela Cruz",
  address: "Paranaque City",
  tin: "123-456-789-000",
  businessStyle: "Juan Parking Services",
  statutoryIdNumber: ""
};

describe("invoice customer information session draft", () => {
  it("restores only the draft belonging to the same parking session", () => {
    const storage = memoryStorage();
    expect(saveInvoiceCustomerInformationDraft("session-a", customerInformation, storage)).toBe(true);

    expect(loadInvoiceCustomerInformationDraft("session-a", storage)).toEqual(customerInformation);
    expect(loadInvoiceCustomerInformationDraft("session-b", storage)).toBeNull();
  });

  it("clears the selected parking-session draft", () => {
    const storage = memoryStorage();
    saveInvoiceCustomerInformationDraft("session-a", customerInformation, storage);
    clearInvoiceCustomerInformationDraft("session-a", storage);

    expect(loadInvoiceCustomerInformationDraft("session-a", storage)).toBeNull();
  });
});
