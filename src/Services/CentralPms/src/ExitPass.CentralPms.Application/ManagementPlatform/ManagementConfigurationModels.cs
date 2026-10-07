using ExitPass.CentralPms.Application.VendorParking;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementConfigurationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record ManagementSite(
    Guid SiteId, Guid SiteGroupId, string SiteCode, string SiteName, string SiteType,
    string TimezoneName, string? AddressLine1, string? AddressLine2, string? City,
    string? Province, string CountryCode, Guid? LocalGovernmentUnitId, string Status,
    bool PublicLookupEnabled, bool PaymentEnabled, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, long RowVersion);

public sealed record ManagementSiteGroup(
    Guid SiteGroupId, string SiteGroupCode, string SiteGroupName, string TimezoneName,
    string CurrencyCode, string Status);

public sealed record ManagementJurisdiction(
    Guid JurisdictionId, string JurisdictionCode, string JurisdictionType, string DisplayName,
    string? PsgcCode, string? ProvinceName, string? RegionName, string CountryCode,
    string Status, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, long RowVersion);

public sealed record ManagementStatutoryPolicy(
    Guid PolicyId, string PolicyCode, string PolicyName, string EntitlementType,
    Guid? LocalGovernmentUnitId, string BenefitType, string ResidencyScope,
    bool RequiresEvidence, string? RequiredEvidenceType, string VerificationStatus,
    string Status, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, long RowVersion,
    Guid? PolicyVersionId, string? PolicyVersion, string? PublicationStatus);

public sealed record ManagementStatutoryPolicyDraft(
    string PolicyCode, string PolicyName, string PolicyVersion, Guid LocalGovernmentUnitId,
    string EntitlementType, string BenefitType, string ResidencyScope, string DiscountBaseScope,
    bool RequiresEvidence, string? RequiredEvidenceType, string? OrdinanceReference,
    string SourceReference, bool FullFeeExempt, int? FreeDurationMinutes,
    int? DiscountPercentageBasisPoints, DateTimeOffset EffectiveFrom);

public sealed record ManagementTariffRule(
    Guid? RuleId, int Sequence, string RuleScope, string ChargeType,
    int? DurationStartMinutes, int? DurationEndMinutes,
    TimeOnly? ClockStartTime, TimeOnly? ClockEndTime,
    long AmountMinorUnits, int? BillingUnitMinutes, string? RoundingRule);

public sealed record ManagementTariff(
    Guid? TariffId, Guid SiteId, string? SiteName, string VehicleTypeCode,
    string TariffCode, string TariffName, string Version, string CurrencyCode,
    int ParkingGracePeriodMinutes, int? PostPaymentExitGraceMinutes,
    long? DailyMaxFeeMinorUnits, int QuoteValidityMinutes,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, string Status,
    bool Verified, long RowVersion, IReadOnlyList<ManagementTariffRule> Rules);

public sealed record ManagementTariffPreviewRequest(
    ManagementTariff Tariff, DateTimeOffset EntryTimestamp, DateTimeOffset CalculationTimestamp);

public sealed record ManagementTariffPreview(
    bool Success, string? FailureCode, long ElapsedMinutes, long ChargeableMinutes,
    long? AmountMinorUnits, string? Currency, string? TariffVersion,
    IReadOnlyList<Guid> AppliedRuleIds);

public interface IManagementConfigurationRepository
{
    Task<IReadOnlyList<ManagementSite>> ListSitesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagementSiteGroup>> ListSiteGroupsAsync(CancellationToken cancellationToken);
    Task<string?> GetSiteTimezoneAsync(Guid siteId, CancellationToken cancellationToken);
    Task<ManagementSite> SaveSiteAsync(ManagementSite site, Guid actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagementJurisdiction>> ListJurisdictionsAsync(CancellationToken cancellationToken);
    Task<ManagementJurisdiction> SaveJurisdictionAsync(ManagementJurisdiction jurisdiction, Guid actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagementStatutoryPolicy>> ListStatutoryPoliciesAsync(Guid? jurisdictionId, CancellationToken cancellationToken);
    Task<ManagementStatutoryPolicy> CreateStatutoryPolicyDraftAsync(ManagementStatutoryPolicyDraft draft, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task<ManagementStatutoryPolicy> ChangeStatutoryPolicyStatusAsync(Guid policyId, long expectedRowVersion, string status, Guid actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagementTariff>> ListTariffsAsync(Guid? siteId, string? vehicleTypeCode, CancellationToken cancellationToken);
    Task<ManagementTariff> SaveTariffDraftAsync(ManagementTariff tariff, Guid actorUserId, CancellationToken cancellationToken);
    Task<ManagementTariff> VerifyTariffAsync(Guid tariffId, long expectedRowVersion, Guid actorUserId, CancellationToken cancellationToken);
    Task<ManagementTariff> ActivateTariffAsync(Guid tariffId, long expectedRowVersion, Guid actorUserId, CancellationToken cancellationToken);
    Task<ManagementTariff> RetireTariffAsync(Guid tariffId, long expectedRowVersion, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class ManagementConfigurationService(
    IManagementConfigurationRepository repository,
    IContinuityTariffCalculator calculator)
{
    public Task<IReadOnlyList<ManagementSite>> ListSitesAsync(CancellationToken ct) => repository.ListSitesAsync(ct);
    public Task<IReadOnlyList<ManagementSiteGroup>> ListSiteGroupsAsync(CancellationToken ct) => repository.ListSiteGroupsAsync(ct);
    public Task<ManagementSite> SaveSiteAsync(ManagementSite site, Guid actor, CancellationToken ct) => repository.SaveSiteAsync(site, actor, ct);
    public Task<IReadOnlyList<ManagementJurisdiction>> ListJurisdictionsAsync(CancellationToken ct) => repository.ListJurisdictionsAsync(ct);
    public Task<ManagementJurisdiction> SaveJurisdictionAsync(ManagementJurisdiction item, Guid actor, CancellationToken ct) => repository.SaveJurisdictionAsync(item, actor, ct);
    public Task<IReadOnlyList<ManagementStatutoryPolicy>> ListStatutoryPoliciesAsync(Guid? id, CancellationToken ct) => repository.ListStatutoryPoliciesAsync(id, ct);
    public Task<ManagementStatutoryPolicy> CreateStatutoryPolicyDraftAsync(ManagementStatutoryPolicyDraft draft, Guid actor, Guid correlation, CancellationToken ct) => repository.CreateStatutoryPolicyDraftAsync(draft, actor, correlation, ct);
    public Task<ManagementStatutoryPolicy> ChangeStatutoryPolicyStatusAsync(Guid id, long version, string status, Guid actor, CancellationToken ct) => repository.ChangeStatutoryPolicyStatusAsync(id, version, status, actor, ct);
    public Task<IReadOnlyList<ManagementTariff>> ListTariffsAsync(Guid? siteId, string? vehicle, CancellationToken ct) => repository.ListTariffsAsync(siteId, vehicle, ct);
    public Task<ManagementTariff> SaveTariffDraftAsync(ManagementTariff tariff, Guid actor, CancellationToken ct) => repository.SaveTariffDraftAsync(tariff, actor, ct);
    public async Task<ManagementTariff> VerifyTariffAsync(Guid id, long version, DateTimeOffset entry, DateTimeOffset calculation, Guid actor, CancellationToken ct)
    {
        var tariff = (await repository.ListTariffsAsync(null, null, ct)).SingleOrDefault(x => x.TariffId == id);
        if (tariff is null || tariff.Status != "DRAFT" || tariff.RowVersion != version)
            throw new ManagementConfigurationException("SITE_TARIFF_VERIFY_CONFLICT");
        var preview = await PreviewAsync(new ManagementTariffPreviewRequest(tariff, entry, calculation), ct);
        if (!preview.Success)
            throw new ManagementConfigurationException(preview.FailureCode ?? "SITE_TARIFF_VERIFY_FAILED");
        return await repository.VerifyTariffAsync(id, version, actor, ct);
    }
    public Task<ManagementTariff> ActivateTariffAsync(Guid id, long version, Guid actor, CancellationToken ct) => repository.ActivateTariffAsync(id, version, actor, ct);
    public Task<ManagementTariff> RetireTariffAsync(Guid id, long version, Guid actor, CancellationToken ct) => repository.RetireTariffAsync(id, version, actor, ct);

    public async Task<ManagementTariffPreview> PreviewAsync(ManagementTariffPreviewRequest request, CancellationToken ct)
    {
        var tariff = request.Tariff;
        var timezone = await repository.GetSiteTimezoneAsync(tariff.SiteId, ct);
        if (timezone is null)
            return new ManagementTariffPreview(false, "CONTINUITY_TARIFF_SITE_NOT_FOUND", 0, 0, null, null, null, []);
        var definition = new ContinuityTariffDefinition(
            tariff.TariffId ?? Guid.NewGuid(), tariff.SiteId, timezone, tariff.VehicleTypeCode,
            tariff.TariffCode, tariff.Version, tariff.CurrencyCode, tariff.ParkingGracePeriodMinutes,
            tariff.PostPaymentExitGraceMinutes, tariff.DailyMaxFeeMinorUnits, tariff.QuoteValidityMinutes,
            tariff.EffectiveFrom, tariff.EffectiveTo,
            tariff.Rules.Select(rule => new ContinuityTariffRule(
                rule.RuleId ?? Guid.NewGuid(), rule.Sequence, rule.RuleScope, rule.ChargeType,
                rule.DurationStartMinutes, rule.DurationEndMinutes, rule.ClockStartTime,
                rule.ClockEndTime, rule.AmountMinorUnits, rule.BillingUnitMinutes, rule.RoundingRule)).ToArray());
        var result = await calculator.PreviewAsync(definition, request.EntryTimestamp, request.CalculationTimestamp, ct);
        return new ManagementTariffPreview(
            result.Success, result.FailureCode,
            (long)Math.Ceiling(result.ElapsedDuration?.TotalMinutes ?? 0),
            (long)Math.Ceiling(result.ChargeableDuration?.TotalMinutes ?? 0),
            result.AmountMinorUnits, result.Currency, result.TariffVersionReference, result.AppliedRuleIds);
    }
}
