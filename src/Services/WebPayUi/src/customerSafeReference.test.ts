import { describe, expect, it } from "vitest";
import { formatCustomerSupportReference } from "./customerSafeReference";

describe("formatCustomerSupportReference", () => {
  it("formats a canonical UUID deterministically without exposing a UUID segment", () => {
    const canonical = "77777777-7777-4777-8777-777777777777";
    const first = formatCustomerSupportReference(canonical);

    expect(first).toMatch(/^[0-9A-F]{4}-[0-9A-F]{4}$/);
    expect(formatCustomerSupportReference(canonical.toUpperCase())).toBe(first);
    expect(canonical).not.toContain(first ?? "missing");
    expect(first).not.toBe("7777-7777");
  });

  it("uses the complete UUID so distinct references remain distinct", () => {
    expect(formatCustomerSupportReference("77777777-7777-4777-8777-777777777777"))
      .not.toBe(formatCustomerSupportReference("77777777-7777-4777-8777-777777777778"));
  });

  it.each([undefined, null, "", "support-reference", "77777777-7777-7777-7777"])(
    "does not manufacture a support reference from malformed input %s",
    (value) => {
      expect(formatCustomerSupportReference(value)).toBeNull();
    }
  );
});
