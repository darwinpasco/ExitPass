import { describe, expect, it } from "vitest";
import { isAutomaticallyMaskedStatutoryIdReference, maskStatutoryIdReference } from "./statutoryIdMasking";

describe("statutory ID masking", () => {
  it.each([
    ["12345", "*2345"],
    ["SC12345678", "******5678"],
    ["PWD-123456789", "*********6789"],
    ["ABCDEFGH", "****EFGH"],
    ["ZX-123456789012345", "**************2345"]
  ])("masks only the leading characters in %s", (rawValue, expected) => {
    expect(maskStatutoryIdReference(rawValue)).toEqual({ ok: true, maskedValue: expected });
    expect(isAutomaticallyMaskedStatutoryIdReference(expected)).toBe(true);
  });

  it("keeps exactly four characters visible and rejects shorter values", () => {
    expect(maskStatutoryIdReference("1234")).toEqual({ ok: true, maskedValue: "1234" });
    expect(maskStatutoryIdReference("ABCD")).toEqual({ ok: true, maskedValue: "ABCD" });
    expect(isAutomaticallyMaskedStatutoryIdReference("1234")).toBe(true);
    expect(isAutomaticallyMaskedStatutoryIdReference("ABCD")).toBe(true);
    expect(maskStatutoryIdReference("AB1")).toEqual({
      ok: false,
      message: "Enter at least 4 characters for the ID No. / Control No."
    });
  });

  it("rejects manual asterisks and unsupported characters", () => {
    expect(maskStatutoryIdReference("******5678")).toMatchObject({ ok: false });
    expect(maskStatutoryIdReference("SC1234ñ5678")).toEqual({
      ok: false,
      message: "Use letters, numbers, and hyphens only."
    });
  });
});
