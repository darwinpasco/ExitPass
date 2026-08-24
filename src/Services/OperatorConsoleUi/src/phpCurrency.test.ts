import { describe, expect, it } from "vitest";
import {
  formatPhpMoney,
  OperatorConsoleCurrencyError,
  requirePhpCurrencyForAmounts
} from "./phpCurrency";

describe("Operator Console PHP-only currency", () => {
  it("renders valid PHP minor units with the peso symbol", () => {
    expect(formatPhpMoney(12500, "PHP")).toBe("₱125.00");
    expect(formatPhpMoney(0, "PHP")).toBe("₱0.00");
  });

  it.each([undefined, null, ""])("fails closed when monetary currency is missing or blank: %s", (currency) => {
    expect(() => formatPhpMoney(12500, currency)).toThrow(OperatorConsoleCurrencyError);
  });

  it.each(["USD", "EUR", "php", "$", "₱"])("fails closed for unsupported currency value %s", (currency) => {
    expect(() => formatPhpMoney(12500, currency)).toThrow(OperatorConsoleCurrencyError);
  });

  it("does not require currency when no monetary value exists", () => {
    expect(formatPhpMoney(undefined, undefined)).toBe("Not available");
    expect(requirePhpCurrencyForAmounts(undefined, undefined, null)).toBeUndefined();
  });
});
