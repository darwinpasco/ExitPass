import { describe, expect, it } from "vitest";
import { isAutomaticallyMaskedStatutoryIdReference, maskStatutoryIdReference } from "./statutoryIdMasking";

describe("statutory ID masking", () => {
  it.each([
    ["SC12345678", "******5678"],
    ["PWD-123456789", "*********6789"],
    ["ABCD1234", "****1234"],
    ["AB12345", "***2345"],
    ["ZX-123456789012345", "**************2345"]
  ])("masks %s while preserving only the final four characters", (rawValue, expected) => {
    expect(maskStatutoryIdReference(rawValue)).toEqual({ ok: true, normalizedValue: rawValue, maskedValue: expected });
    expect(isAutomaticallyMaskedStatutoryIdReference(expected)).toBe(true);
  });

  it("shows an exact four-character value and masks only leading characters for longer values", () => {
    expect(maskStatutoryIdReference("AB12")).toEqual({ ok: true, normalizedValue: "AB12", maskedValue: "AB12" });
    expect(maskStatutoryIdReference("AB1234")).toEqual({ ok: true, normalizedValue: "AB1234", maskedValue: "**1234" });
    expect(isAutomaticallyMaskedStatutoryIdReference("**1234")).toBe(true);
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
