namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Computes the narrow statutory discount payable-basis contract currently supported by Operator Console.
/// </summary>
public static class OperatorConsoleStatutoryDiscountComputationContract
{
    public const decimal VatRate = 0.12m;
    public const decimal StatutoryDiscountRate = 0.20m;
    public const string SupportedBenefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT";
    public const string SupportedDiscountBaseScope = "VAT_EXCLUSIVE";
    public const string RoundingMode = "HALF_AWAY_FROM_ZERO";

    /// <summary>
    /// Computes the VAT-exclusive statutory discount amounts for supported Senior Citizen and PWD parking cases.
    /// </summary>
    public static OperatorConsoleStatutoryDiscountComputationResult Compute(
        OperatorConsoleStatutoryDiscountComputationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.GrossAmountMinorUnits <= 0)
        {
            return Rejected(request, "PAYABLE_BASIS_COMPONENTS_MISSING");
        }

        if (!IsSupportedEntitlementType(request.EntitlementType))
        {
            return Rejected(request, "STATUTORY_DISCOUNT_ENTITLEMENT_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
        }

        if (!string.Equals(request.BenefitType, SupportedBenefitType, StringComparison.Ordinal))
        {
            return Rejected(request, "POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
        }

        if (!string.Equals(request.DiscountBaseScope, SupportedDiscountBaseScope, StringComparison.Ordinal))
        {
            return Rejected(request, "POLICY_DISCOUNT_BASE_SCOPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
        }

        var vatExclusiveMinorUnits = decimal.ToInt64(decimal.Round(
            request.GrossAmountMinorUnits / (1m + VatRate),
            0,
            MidpointRounding.AwayFromZero));
        var vatMinorUnits = request.GrossAmountMinorUnits - vatExclusiveMinorUnits;
        var statutoryDiscountMinorUnits = decimal.ToInt64(decimal.Round(
            vatExclusiveMinorUnits * StatutoryDiscountRate,
            0,
            MidpointRounding.AwayFromZero));
        var finalPayableMinorUnits = vatExclusiveMinorUnits - statutoryDiscountMinorUnits;

        return new OperatorConsoleStatutoryDiscountComputationResult(
            Accepted: true,
            request.GrossAmountMinorUnits,
            vatMinorUnits,
            vatExclusiveMinorUnits,
            statutoryDiscountMinorUnits,
            finalPayableMinorUnits,
            request.EntitlementType,
            request.BenefitType,
            request.DiscountBaseScope,
            VatRate,
            StatutoryDiscountRate,
            RoundingMode,
            ErrorCode: null);
    }

    private static bool IsSupportedEntitlementType(string entitlementType) =>
        string.Equals(entitlementType, "SENIOR_CITIZEN", StringComparison.Ordinal) ||
        string.Equals(entitlementType, "PWD", StringComparison.Ordinal);

    private static OperatorConsoleStatutoryDiscountComputationResult Rejected(
        OperatorConsoleStatutoryDiscountComputationRequest request,
        string errorCode) =>
        new(
            Accepted: false,
            request.GrossAmountMinorUnits,
            VatAmountMinorUnits: null,
            VatExclusiveAmountMinorUnits: null,
            StatutoryDiscountAmountMinorUnits: null,
            FinalPayableAmountMinorUnits: null,
            request.EntitlementType,
            request.BenefitType,
            request.DiscountBaseScope,
            VatRate,
            StatutoryDiscountRate,
            RoundingMode,
            errorCode);
}

/// <summary>
/// Input to the supported statutory discount payable-basis computation.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountComputationRequest(
    long GrossAmountMinorUnits,
    string EntitlementType,
    string BenefitType,
    string DiscountBaseScope);

/// <summary>
/// Safe result of statutory discount payable-basis computation.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountComputationResult(
    bool Accepted,
    long GrossAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string EntitlementType,
    string BenefitType,
    string DiscountBaseScope,
    decimal VatRate,
    decimal StatutoryDiscountRate,
    string RoundingMode,
    string? ErrorCode);
