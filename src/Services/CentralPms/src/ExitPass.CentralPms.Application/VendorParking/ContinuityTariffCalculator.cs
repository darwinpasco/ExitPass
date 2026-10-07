namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>Evaluates the active governed Site + Vehicle Type tariff using integer minor units.</summary>
public sealed class ContinuityTariffCalculator(IContinuityTariffRepository repository) : IContinuityTariffCalculator
{
    public const string TariffVersionPrefix = "EXITPASS-CONTINUITY:";
    private static readonly TimeSpan FutureEntryClockTolerance = TimeSpan.FromMinutes(2);

    public async Task<ContinuityTariffCalculationResult> CalculateAsync(
        Guid siteId,
        string? vehicleTypeCode,
        DateTimeOffset entryTimestamp,
        DateTimeOffset calculationTimestamp,
        CancellationToken cancellationToken)
    {
        var vehicleType = Normalize(vehicleTypeCode);
        if (vehicleType is null || vehicleType == ContinuityVehicleTypes.Unknown)
            return ContinuityTariffCalculationResult.Failed("CONTINUITY_VEHICLE_TYPE_UNRESOLVED", vehicleType);
        if (entryTimestamp == default || calculationTimestamp == default ||
            entryTimestamp > calculationTimestamp.Add(FutureEntryClockTolerance))
            return ContinuityTariffCalculationResult.Failed("CONTINUITY_ENTRY_TIME_INVALID", vehicleType);

        var definition = await repository.FindActiveAsync(siteId, vehicleType, calculationTimestamp, cancellationToken);
        if (definition is null)
            return ContinuityTariffCalculationResult.Failed("CONTINUITY_TARIFF_NOT_CONFIGURED", vehicleType);
        return await PreviewAsync(definition, entryTimestamp, calculationTimestamp, cancellationToken);
    }

    public Task<ContinuityTariffCalculationResult> PreviewAsync(
        ContinuityTariffDefinition definition,
        DateTimeOffset entryTimestamp,
        DateTimeOffset calculationTimestamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vehicleType = Normalize(definition.VehicleTypeCode);
        if (vehicleType is null || vehicleType == ContinuityVehicleTypes.Unknown)
            return Task.FromResult(ContinuityTariffCalculationResult.Failed("CONTINUITY_VEHICLE_TYPE_UNRESOLVED", vehicleType));
        if (entryTimestamp == default || calculationTimestamp == default ||
            entryTimestamp > calculationTimestamp.Add(FutureEntryClockTolerance))
            return Task.FromResult(ContinuityTariffCalculationResult.Failed("CONTINUITY_ENTRY_TIME_INVALID", vehicleType));
        if (definition.Rules.Count == 0)
            return Task.FromResult(ContinuityTariffCalculationResult.Failed("CONTINUITY_TARIFF_RULE_INVALID", vehicleType));

        var elapsed = calculationTimestamp - entryTimestamp;
        var grace = TimeSpan.FromMinutes(definition.ParkingGracePeriodMinutes);
        var chargeable = elapsed <= grace ? TimeSpan.Zero : elapsed - grace;

        try
        {
            var applied = new List<Guid>();
            long amount = 0;
            if (chargeable > TimeSpan.Zero)
            {
                var durationRules = definition.Rules.Where(rule => rule.RuleScope == "DURATION").OrderBy(rule => rule.Sequence).ToArray();
                var clockRules = definition.Rules.Where(rule => rule.RuleScope == "CLOCK_TIME").OrderBy(rule => rule.Sequence).ToArray();
                if (durationRules.Length > 0 && clockRules.Length > 0)
                    return Task.FromResult(ContinuityTariffCalculationResult.Failed("CONTINUITY_TARIFF_RULE_AMBIGUOUS", vehicleType));
                if (durationRules.Length > 0)
                {
                    var result = CalculateDurationRules(durationRules, checked((long)Math.Ceiling(chargeable.TotalMinutes)), applied);
                    if (!result.Success) return Task.FromResult(ContinuityTariffCalculationResult.Failed(result.ErrorCode!, vehicleType));
                    amount = result.Amount;
                }
                else if (clockRules.Length > 0)
                {
                    var result = CalculateClockRules(clockRules, definition.SiteTimezone, calculationTimestamp, applied);
                    if (!result.Success) return Task.FromResult(ContinuityTariffCalculationResult.Failed(result.ErrorCode!, vehicleType));
                    amount = result.Amount;
                }
                else return Task.FromResult(ContinuityTariffCalculationResult.Failed("CONTINUITY_TARIFF_RULE_INVALID", vehicleType));
            }

            if (definition.DailyMaxFeeMinorUnits.HasValue)
                amount = Math.Min(amount, definition.DailyMaxFeeMinorUnits.Value);

            return Task.FromResult(new ContinuityTariffCalculationResult(
                true, null, definition.SiteTariffDefinitionId, vehicleType, elapsed, chargeable,
                amount, definition.CurrencyCode, TariffVersionPrefix + definition.Version,
                calculationTimestamp, calculationTimestamp.AddMinutes(definition.QuoteValidityMinutes), applied));
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        {
            return Task.FromResult(ContinuityTariffCalculationResult.Failed(
                exception is OverflowException ? "CONTINUITY_TARIFF_ARITHMETIC_ERROR" : "CONTINUITY_TARIFF_RULE_INVALID",
                vehicleType));
        }
    }

    private static RuleCalculation CalculateDurationRules(
        IReadOnlyList<ContinuityTariffRule> rules, long chargeableMinutes, ICollection<Guid> applied)
    {
        long amount = 0;
        long coveredUntil = 0;
        foreach (var rule in rules)
        {
            if (rule.DurationStartMinutes is null || rule.DurationStartMinutes.Value != coveredUntil)
                return RuleCalculation.Failed("CONTINUITY_TARIFF_RULE_GAP");
            var end = rule.DurationEndMinutes.HasValue
                ? Math.Min(chargeableMinutes, rule.DurationEndMinutes.Value)
                : chargeableMinutes;
            var covered = Math.Max(0, end - rule.DurationStartMinutes.Value);
            if (covered > 0)
            {
                amount = checked(amount + CalculateRuleAmount(rule, covered));
                applied.Add(rule.SiteTariffRuleId);
            }
            coveredUntil = rule.DurationEndMinutes ?? chargeableMinutes;
            if (chargeableMinutes <= coveredUntil) return RuleCalculation.Succeeded(amount);
        }
        return coveredUntil >= chargeableMinutes
            ? RuleCalculation.Succeeded(amount)
            : RuleCalculation.Failed("CONTINUITY_TARIFF_RULE_GAP");
    }

    private static RuleCalculation CalculateClockRules(
        IReadOnlyList<ContinuityTariffRule> rules, string timezone,
        DateTimeOffset calculationTimestamp, ICollection<Guid> applied)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException) { return RuleCalculation.Failed("CONTINUITY_TARIFF_RULE_INVALID"); }
        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(calculationTimestamp, zone).DateTime);
        var matches = rules.Where(rule => MatchesClockBand(rule, localTime)).ToArray();
        if (matches.Length == 0) return RuleCalculation.Failed("CONTINUITY_TARIFF_RULE_GAP");
        if (matches.Length > 1) return RuleCalculation.Failed("CONTINUITY_TARIFF_RULE_AMBIGUOUS");
        var selected = matches[0];
        applied.Add(selected.SiteTariffRuleId);
        return RuleCalculation.Succeeded(selected.AmountMinorUnits);
    }

    private static bool MatchesClockBand(ContinuityTariffRule rule, TimeOnly time)
    {
        if (rule.ClockStartTime is null || rule.ClockEndTime is null) return false;
        return rule.ClockStartTime < rule.ClockEndTime
            ? time >= rule.ClockStartTime && time < rule.ClockEndTime
            : time >= rule.ClockStartTime || time < rule.ClockEndTime;
    }

    private static long CalculateRuleAmount(ContinuityTariffRule rule, long coveredMinutes) => rule.ChargeType switch
    {
        "FLAT_RATE" or "SESSION" => rule.AmountMinorUnits,
        "UNIT_DURATION" when rule.BillingUnitMinutes is > 0 && rule.RoundingRule == "WHOLE_STARTED_HOUR" =>
            checked(((coveredMinutes + rule.BillingUnitMinutes.Value - 1) / rule.BillingUnitMinutes.Value) * rule.AmountMinorUnits),
        _ => throw new InvalidOperationException("Unsupported continuity tariff rule.")
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private readonly record struct RuleCalculation(bool Success, long Amount, string? ErrorCode)
    {
        public static RuleCalculation Succeeded(long amount) => new(true, amount, null);
        public static RuleCalculation Failed(string code) => new(false, 0, code);
    }
}
