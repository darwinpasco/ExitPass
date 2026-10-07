namespace ExitPass.CentralPms.Application.VendorParking;

public static class ContinuityVehicleTypes
{
    public const string Car = "CAR";
    public const string Motorcycle = "MOTORCYCLE";
    public const string SuvMpv = "SUV_MPV";
    public const string Van = "VAN";
    public const string Bus = "BUS";
    public const string Truck = "TRUCK";
    public const string LightTruck = "LIGHT_TRUCK";
    public const string Tricycle = "TRICYCLE";
    public const string Other = "OTHER";
    public const string Unknown = "UNKNOWN";
}

public sealed record ContinuityTariffDefinition(
    Guid SiteTariffDefinitionId,
    Guid SiteId,
    string SiteTimezone,
    string VehicleTypeCode,
    string TariffCode,
    string Version,
    string CurrencyCode,
    int ParkingGracePeriodMinutes,
    int? PostPaymentExitGraceMinutes,
    long? DailyMaxFeeMinorUnits,
    int QuoteValidityMinutes,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<ContinuityTariffRule> Rules);

public sealed record ContinuityTariffRule(
    Guid SiteTariffRuleId,
    int Sequence,
    string RuleScope,
    string ChargeType,
    int? DurationStartMinutes,
    int? DurationEndMinutes,
    TimeOnly? ClockStartTime,
    TimeOnly? ClockEndTime,
    long AmountMinorUnits,
    int? BillingUnitMinutes,
    string? RoundingRule);

public interface IContinuityTariffRepository
{
    Task<ContinuityTariffDefinition?> FindActiveAsync(
        Guid siteId,
        string vehicleTypeCode,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}

public interface IContinuityTariffCalculator
{
    Task<ContinuityTariffCalculationResult> CalculateAsync(
        Guid siteId,
        string? vehicleTypeCode,
        DateTimeOffset entryTimestamp,
        DateTimeOffset calculationTimestamp,
        CancellationToken cancellationToken);

    Task<ContinuityTariffCalculationResult> PreviewAsync(
        ContinuityTariffDefinition definition,
        DateTimeOffset entryTimestamp,
        DateTimeOffset calculationTimestamp,
        CancellationToken cancellationToken);
}

public sealed record ContinuityTariffCalculationResult(
    bool Success,
    string? FailureCode,
    Guid? SiteTariffDefinitionId,
    string? VehicleTypeCode,
    TimeSpan? ElapsedDuration,
    TimeSpan? ChargeableDuration,
    long? AmountMinorUnits,
    string? Currency,
    string? TariffVersionReference,
    DateTimeOffset? CalculatedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<Guid> AppliedRuleIds)
{
    public static ContinuityTariffCalculationResult Failed(string code, string? vehicleTypeCode = null) =>
        new(false, code, null, vehicleTypeCode, null, null, null, null, null, null, null, []);
}
