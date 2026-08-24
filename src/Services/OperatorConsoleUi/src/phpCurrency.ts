export const operatorConsoleCurrencyCode = "PHP" as const;

export class OperatorConsoleCurrencyError extends Error {
  constructor(currencyCode: string | null | undefined) {
    super(
      currencyCode === undefined || currencyCode === null || currencyCode.length === 0
        ? "Operator Console monetary data requires currency code PHP."
        : `Operator Console does not support currency code ${currencyCode}.`
    );
    this.name = "OperatorConsoleCurrencyError";
  }
}

export function requirePhpCurrencyForAmounts(
  currencyCode: string | null | undefined,
  ...minorUnitValues: Array<number | null | undefined>
): typeof operatorConsoleCurrencyCode | undefined {
  const hasCurrency = currencyCode !== undefined && currencyCode !== null;
  const hasMonetaryValue = minorUnitValues.some((value) => value !== undefined && value !== null);

  if (!hasCurrency && !hasMonetaryValue) {
    return undefined;
  }

  if (currencyCode !== operatorConsoleCurrencyCode) {
    throw new OperatorConsoleCurrencyError(currencyCode);
  }

  return operatorConsoleCurrencyCode;
}

export function formatPhpMoney(
  minorUnits: number | null | undefined,
  currencyCode: string | null | undefined
): string {
  const validatedCurrency = requirePhpCurrencyForAmounts(currencyCode, minorUnits);
  if (minorUnits === undefined || minorUnits === null) {
    return "Not available";
  }

  if (validatedCurrency !== operatorConsoleCurrencyCode) {
    throw new OperatorConsoleCurrencyError(currencyCode);
  }

  return `₱${(minorUnits / 100).toFixed(2)}`;
}
