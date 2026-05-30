using System.Data;
using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read-only repository for statutory discount policy resolution.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Reads only site jurisdiction and statutory discount policy registry state.
/// - Does not create drafts, mutate payable basis, create payment attempts, call providers, open gates,
///   create coupons, or create reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository
    : IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository
{
    private const string SeniorFallbackPolicyCode = "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK";
    private const string PwdFallbackPolicyCode = "PH_RA10754_PWD_NATIONAL_FALLBACK";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a statutory discount policy resolution read repository.
    /// </summary>
    public OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountPolicyResolutionReadResult> ResolveAsync(
        OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var site = await ReadSiteAsync(connection, request.SiteId, cancellationToken);
        if (site is null)
        {
            return NotResolved("SITE_NOT_FOUND", "SITE_NOT_FOUND", request.SiteId, request.SiteGroupId, jurisdictionId: null);
        }

        if (request.SiteGroupId.HasValue && site.SiteGroupId != request.SiteGroupId.Value)
        {
            return NotResolved("SITE_GROUP_MISMATCH", "SITE_GROUP_MISMATCH", site.SiteId, site.SiteGroupId, site.JurisdictionId);
        }

        if (!site.JurisdictionId.HasValue)
        {
            return NotResolved("SITE_JURISDICTION_NOT_CONFIGURED", "SITE_JURISDICTION_NOT_CONFIGURED", site.SiteId, site.SiteGroupId, jurisdictionId: null);
        }

        var verifiedLocalPolicy = await ReadVerifiedLocalPolicyAsync(
            connection,
            site,
            request.EntitlementType,
            request.EffectiveDate,
            cancellationToken);
        if (verifiedLocalPolicy is not null)
        {
            return Resolved(verifiedLocalPolicy);
        }

        if (await HasUnverifiedLocalPolicyAsync(connection, site.JurisdictionId.Value, request.EntitlementType, request.EffectiveDate, cancellationToken))
        {
            return NotResolved(
                "STATUTORY_DISCOUNT_POLICY_UNVERIFIED",
                "STATUTORY_DISCOUNT_POLICY_UNVERIFIED",
                site.SiteId,
                site.SiteGroupId,
                site.JurisdictionId);
        }

        var fallback = await ReadNationalFallbackPolicyAsync(
            connection,
            site,
            request.EntitlementType,
            request.EffectiveDate,
            cancellationToken);

        return fallback is null
            ? NotResolved("NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED", "NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED", site.SiteId, site.SiteGroupId, site.JurisdictionId)
            : Resolved(fallback);
    }

    private static OperatorConsoleStatutoryDiscountPolicyResolutionReadResult Resolved(
        OperatorConsoleResolvedStatutoryDiscountPolicy policy) =>
        new(
            Resolved: true,
            policy,
            policy.SiteId,
            policy.SiteGroupId,
            policy.JurisdictionId,
            IneligibilityReason: null,
            ErrorCode: null);

    private static OperatorConsoleStatutoryDiscountPolicyResolutionReadResult NotResolved(
        string ineligibilityReason,
        string errorCode,
        Guid? siteId,
        Guid? siteGroupId,
        Guid? jurisdictionId) =>
        new(
            Resolved: false,
            Policy: null,
            siteId,
            siteGroupId,
            jurisdictionId,
            ineligibilityReason,
            errorCode);

    private static async Task<SiteRow?> ReadSiteAsync(
        NpgsqlConnection connection,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT site_id, site_group_id, jurisdiction_id
            FROM sites.sites
            WHERE site_id = @site_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SiteRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2));
    }

    private static async Task<OperatorConsoleResolvedStatutoryDiscountPolicy?> ReadVerifiedLocalPolicyAsync(
        NpgsqlConnection connection,
        SiteRow site,
        string entitlementType,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                p.statutory_discount_policy_id,
                p.jurisdiction_id,
                p.policy_code,
                p.policy_name,
                p.entitlement_type::text,
                p.policy_resolution_basis::text,
                p.policy_level::text,
                p.policy_type::text,
                p.legal_basis_reference,
                p.ordinance_reference,
                p.national_law_reference,
                p.verification_status::text,
                p.beneficiary_residency_scope::text,
                p.benefit_type::text,
                p.free_duration_minutes,
                p.initial_rate_exempt_flag,
                p.full_fee_exempt_flag,
                p.overnight_excluded_flag,
                p.valet_excluded_flag,
                p.standalone_parking_excluded_flag,
                p.driver_or_passenger_required_flag,
                p.free_period_application::text,
                p.succeeding_hours_discount_rule::text,
                p.discount_base_scope::text,
                p.stacking_policy::text,
                p.legal_basis_priority::text,
                p.requires_operator_validation,
                p.requires_evidence,
                p.effective_from,
                p.effective_to,
                p.source_reference
            FROM discounts.statutory_discount_policy_registry AS p
            WHERE p.jurisdiction_id = @jurisdiction_id
              AND p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
              AND p.verification_status = 'VERIFIED_OFFICIAL'::discounts.policy_verification_status_enum
              AND p.policy_resolution_basis IN (
                    'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum,
                    'SITE_POLICY_OPERATIONAL_ONLY'::discounts.policy_resolution_basis_enum
              )
              AND p.effective_from <= @effective_date
              AND (p.effective_to IS NULL OR p.effective_to >= @effective_date)
            ORDER BY
                CASE p.policy_level
                    WHEN 'SITE_POLICY'::discounts.discount_policy_level_enum THEN 0
                    WHEN 'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum THEN 1
                    WHEN 'OPERATIONAL_POLICY'::discounts.discount_policy_level_enum THEN 2
                    ELSE 3
                END,
                p.effective_from DESC,
                p.policy_code
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = site.JurisdictionId!.Value;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("effective_date", NpgsqlDbType.Date).Value = effectiveDate;

        return await ReadPolicyAsync(command, site, cancellationToken);
    }

    private static async Task<bool> HasUnverifiedLocalPolicyAsync(
        NpgsqlConnection connection,
        Guid jurisdictionId,
        string entitlementType,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.statutory_discount_policy_registry AS p
                WHERE p.jurisdiction_id = @jurisdiction_id
                  AND p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
                  AND p.verification_status IN (
                        'VERIFIED_SECONDARY'::discounts.policy_verification_status_enum,
                        'LEAD_UNVERIFIED'::discounts.policy_verification_status_enum,
                        'PROPOSED'::discounts.policy_verification_status_enum
                  )
                  AND p.policy_resolution_basis IN (
                        'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum,
                        'SITE_POLICY_OPERATIONAL_ONLY'::discounts.policy_resolution_basis_enum
                  )
                  AND p.effective_from <= @effective_date
                  AND (p.effective_to IS NULL OR p.effective_to >= @effective_date)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = jurisdictionId;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("effective_date", NpgsqlDbType.Date).Value = effectiveDate;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<OperatorConsoleResolvedStatutoryDiscountPolicy?> ReadNationalFallbackPolicyAsync(
        NpgsqlConnection connection,
        SiteRow site,
        string entitlementType,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        var policyCode = entitlementType == "SENIOR_CITIZEN"
            ? SeniorFallbackPolicyCode
            : PwdFallbackPolicyCode;

        const string sql = """
            SELECT
                p.statutory_discount_policy_id,
                p.jurisdiction_id,
                p.policy_code,
                p.policy_name,
                p.entitlement_type::text,
                p.policy_resolution_basis::text,
                p.policy_level::text,
                p.policy_type::text,
                p.legal_basis_reference,
                p.ordinance_reference,
                p.national_law_reference,
                p.verification_status::text,
                p.beneficiary_residency_scope::text,
                p.benefit_type::text,
                p.free_duration_minutes,
                p.initial_rate_exempt_flag,
                p.full_fee_exempt_flag,
                p.overnight_excluded_flag,
                p.valet_excluded_flag,
                p.standalone_parking_excluded_flag,
                p.driver_or_passenger_required_flag,
                p.free_period_application::text,
                p.succeeding_hours_discount_rule::text,
                p.discount_base_scope::text,
                p.stacking_policy::text,
                p.legal_basis_priority::text,
                p.requires_operator_validation,
                p.requires_evidence,
                p.effective_from,
                p.effective_to,
                p.source_reference
            FROM discounts.statutory_discount_policy_registry AS p
            WHERE p.policy_code = @policy_code
              AND p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
              AND p.verification_status = 'VERIFIED_OFFICIAL'::discounts.policy_verification_status_enum
              AND p.policy_resolution_basis = 'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum
              AND p.benefit_type = 'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum
              AND p.free_duration_minutes IS NULL
              AND p.initial_rate_exempt_flag = false
              AND p.full_fee_exempt_flag = false
              AND p.effective_from <= @effective_date
              AND (p.effective_to IS NULL OR p.effective_to >= @effective_date)
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_code", NpgsqlDbType.Text).Value = policyCode;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("effective_date", NpgsqlDbType.Date).Value = effectiveDate;

        return await ReadPolicyAsync(command, site, cancellationToken);
    }

    private static async Task<OperatorConsoleResolvedStatutoryDiscountPolicy?> ReadPolicyAsync(
        NpgsqlCommand command,
        SiteRow site,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var policy = new PolicyRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            GetNullableString(reader, 8),
            GetNullableString(reader, 9),
            GetNullableString(reader, 10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetInt32(14),
            reader.GetBoolean(15),
            reader.GetBoolean(16),
            reader.GetBoolean(17),
            reader.GetBoolean(18),
            reader.GetBoolean(19),
            reader.GetBoolean(20),
            reader.GetString(21),
            reader.GetString(22),
            reader.GetString(23),
            reader.GetString(24),
            reader.GetString(25),
            reader.GetBoolean(26),
            reader.GetBoolean(27),
            reader.GetFieldValue<DateOnly>(28),
            reader.IsDBNull(29) ? null : reader.GetFieldValue<DateOnly>(29),
            GetNullableString(reader, 30));

        var snapshot = BuildPolicySnapshot(policy);
        return new OperatorConsoleResolvedStatutoryDiscountPolicy(
            policy.StatutoryDiscountPolicyId,
            policy.JurisdictionId,
            site.SiteId,
            site.SiteGroupId,
            policy.EntitlementType,
            policy.PolicyCode,
            policy.PolicyName,
            policy.PolicyResolutionBasis,
            policy.PolicyLevel,
            policy.PolicyType,
            policy.LegalBasisReference,
            policy.OrdinanceReference,
            policy.NationalLawReference,
            policy.VerificationStatus,
            policy.BeneficiaryResidencyScope,
            policy.BenefitType,
            policy.FreeDurationMinutes,
            policy.InitialRateExempt,
            policy.FullFeeExempt,
            policy.OvernightExcluded,
            policy.ValetExcluded,
            policy.StandaloneParkingExcluded,
            policy.DriverOrPassengerRequired,
            policy.FreePeriodApplication,
            policy.SucceedingHoursDiscountRule,
            policy.DiscountBaseScope,
            policy.StackingPolicy,
            policy.LegalBasisPriority,
            policy.RequiresOperatorValidation,
            policy.RequiresEvidence,
            policy.EffectiveFrom,
            policy.EffectiveTo,
            policy.SourceReference,
            snapshot);
    }

    private static JsonElement BuildPolicySnapshot(PolicyRow policy) =>
        JsonSerializer.SerializeToElement(new
        {
            statutoryDiscountPolicyId = policy.StatutoryDiscountPolicyId,
            policyCode = policy.PolicyCode,
            policyName = policy.PolicyName,
            entitlementType = policy.EntitlementType,
            policyResolutionBasis = policy.PolicyResolutionBasis,
            policyLevel = policy.PolicyLevel,
            policyType = policy.PolicyType,
            legalBasisReference = policy.LegalBasisReference,
            ordinanceReference = policy.OrdinanceReference,
            nationalLawReference = policy.NationalLawReference,
            verificationStatus = policy.VerificationStatus,
            beneficiaryResidencyScope = policy.BeneficiaryResidencyScope,
            benefitType = policy.BenefitType,
            freeDurationMinutes = policy.FreeDurationMinutes,
            initialRateExempt = policy.InitialRateExempt,
            fullFeeExempt = policy.FullFeeExempt,
            overnightExcluded = policy.OvernightExcluded,
            valetExcluded = policy.ValetExcluded,
            standaloneParkingExcluded = policy.StandaloneParkingExcluded,
            driverOrPassengerRequired = policy.DriverOrPassengerRequired,
            freePeriodApplication = policy.FreePeriodApplication,
            succeedingHoursDiscountRule = policy.SucceedingHoursDiscountRule,
            discountBaseScope = policy.DiscountBaseScope,
            stackingPolicy = policy.StackingPolicy,
            legalBasisPriority = policy.LegalBasisPriority,
            requiresOperatorValidation = policy.RequiresOperatorValidation,
            requiresEvidence = policy.RequiresEvidence,
            effectiveFrom = policy.EffectiveFrom,
            effectiveTo = policy.EffectiveTo,
            sourceReference = policy.SourceReference,
            resolvedAt = DateTimeOffset.UtcNow
        });

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private sealed record SiteRow(Guid SiteId, Guid SiteGroupId, Guid? JurisdictionId);

    private sealed record PolicyRow(
        Guid StatutoryDiscountPolicyId,
        Guid? JurisdictionId,
        string PolicyCode,
        string PolicyName,
        string EntitlementType,
        string PolicyResolutionBasis,
        string PolicyLevel,
        string PolicyType,
        string? LegalBasisReference,
        string? OrdinanceReference,
        string? NationalLawReference,
        string VerificationStatus,
        string BeneficiaryResidencyScope,
        string BenefitType,
        int? FreeDurationMinutes,
        bool InitialRateExempt,
        bool FullFeeExempt,
        bool OvernightExcluded,
        bool ValetExcluded,
        bool StandaloneParkingExcluded,
        bool DriverOrPassengerRequired,
        string FreePeriodApplication,
        string SucceedingHoursDiscountRule,
        string DiscountBaseScope,
        string StackingPolicy,
        string LegalBasisPriority,
        bool RequiresOperatorValidation,
        bool RequiresEvidence,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo,
        string? SourceReference);
}
