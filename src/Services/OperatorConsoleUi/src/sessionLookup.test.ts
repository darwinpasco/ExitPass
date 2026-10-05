import { describe, expect, it } from "vitest";
import { normalizeScannedTicketReference } from "./sessionLookup";

describe("normalizeScannedTicketReference", () => {
  it("uses plain ticket QR text unchanged", () => {
    expect(normalizeScannedTicketReference(" 1474119573131 ")).toBe("1474119573131");
  });

  it("extracts the canonical ticket from supported URL and JSON payloads", () => {
    expect(normalizeScannedTicketReference("https://pay.exitpass.test/?ticketReference=1474119573132"))
      .toBe("1474119573132");
    expect(normalizeScannedTicketReference('{"ticket_reference":"1474119573133"}'))
      .toBe("1474119573133");
  });
});
