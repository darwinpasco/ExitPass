import { describe, expect, it } from "vitest";
import { isAutomaticallyMaskedStatutoryIdReference, maskStatutoryIdReference } from "./statutoryIdMasking";

describe("statutory ID masking", () => {
  it.each([
    ["SC12345678", "SC****5678"],
    ["PWD-123456789", "PW*******6789"],
    ["ABCD1234", "AB**1234"],
    ["AB12345", "AB*2345"],
    ["ZX-123456789012345", "ZX************2345"]
  ])("masks %s using the first-2 and last-4 rule", (rawValue, expected) => {
    expect(maskStatutoryIdReference(rawValue)).toEqual({ ok: true, maskedValue: expected });
    expect(isAutomaticallyMaskedStatutoryIdReference(expected)).toBe(true);
  });

  it("fully masks four-to-six character values and rejects shorter values", () => {
    expect(maskStatutoryIdReference("AB12")).toEqual({ ok: true, maskedValue: "****" });
    expect(maskStatutoryIdReference("AB1234")).toEqual({ ok: true, maskedValue: "******" });
    expect(isAutomaticallyMaskedStatutoryIdReference("****")).toBe(true);
    expect(maskStatutoryIdReference("AB1")).toEqual({
      ok: false,
      message: "Enter at least 4 characters for the ID No. / Control No."
    });
  });

  it("rejects manual asterisks and unsupported characters", () => {
    expect(maskStatutoryIdReference("SC****5678")).toMatchObject({ ok: false });
    expect(maskStatutoryIdReference("SC1234ñ5678")).toEqual({
      ok: false,
      message: "Use letters, numbers, and hyphens only."
    });
  });
});
