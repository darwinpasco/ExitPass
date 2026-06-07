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
/// - Reads only site and statutory discount policy reference state.
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

        if (string.IsNullOrWhiteSpace(site.LguCode))
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

        if (await HasUnverifiedLocalPolicyAsync(connection, site, request.EntitlementType, request.EffectiveDate, cancellationToken))
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
            SELECT site_id, site_group_id, lgu_code
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
            reader.IsDBNull(2) ? null : reader.GetString(2));
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
                p.discount_policy_reference_id,
                p.policy_code,
                p.policy_name,
                p.entitlement_type::text,
                CASE
                    WHEN p.policy_level = 'SITE_POLICY'::discounts.discount_policy_level_enum THEN 'SITE_POLICY_OPERATIONAL_ONLY'
                    ELSE 'LOCAL_ORDINANCE_APPLIED'
                END AS policy_resolution_basis,
                p.policy_level::text,
                p.policy_type::text,
                COALESCE(p.local_ordinance_reference, p.national_law_reference) AS legal_basis_reference,
                p.local_ordinance_reference,
                p.national_law_reference,
                p.policy_status::text AS verification_status,
                'LOCKED_SCHEMA_POLICY_REFERENCE' AS beneficiary_residency_scope,
                'STATUTORY_DISCOUNT_VAT_EXEMPT' AS benefit_type,
                NULL::integer AS free_duration_minutes,
                false AS initial_rate_exempt_flag,
                false AS full_fee_exempt_flag,
                false AS overnight_excluded_flag,
                false AS valet_excluded_flag,
                false AS standalone_parking_excluded_flag,
                false AS driver_or_passenger_required_flag,
                'NOT_APPLICABLE' AS free_period_application,
                'APPLY_NATIONAL_STATUTORY_DISCOUNT' AS succeeding_hours_discount_rule,
                'VAT_EXCLUSIVE' AS discount_base_scope,
                'STATUTORY_FIRST' AS stacking_policy,
                COALESCE(p.local_ordinance_reference, p.national_law_reference, p.policy_code) AS legal_basis_priority,
                p.requires_operator_validation,
                p.requires_evidence_capture,
                p.effective_from,
                p.effective_to,
                p.policy_version AS source_reference
            FROM discounts.discount_policy_references AS p
            WHERE (
                    p.site_id = @site_id
                    OR p.site_group_id = @site_group_id
                    OR p.lgu_code = @lgu_code
                  )
              AND p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
              AND p.policy_level IN (
                    'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                    'SITE_POLICY'::discounts.discount_policy_level_enum,
                    'OPERATIONAL_POLICY'::discounts.discount_policy_level_enum
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
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = site.SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = site.SiteGroupId;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = site.LguCode!;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("effective_date", NpgsqlDbType.Date).Value = effectiveDate;

        return await ReadPolicyAsync(command, site, cancellationToken);
    }

    private static async Task<bool> HasUnverifiedLocalPolicyAsync(
        NpgsqlConnection connection,
        SiteRow site,
        string entitlementType,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.discount_policy_references AS p
                WHERE (
                        p.site_id = @site_id
                        OR p.site_group_id = @site_group_id
                        OR p.lgu_code = @lgu_code
                      )
                  AND p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
                  AND p.policy_status <> 'ACTIVE'::discounts.discount_policy_status_enum
                  AND p.policy_level IN (
                        'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                        'SITE_POLICY'::discounts.discount_policy_level_enum,
                        'OPERATIONAL_POLICY'::discounts.discount_policy_level_enum
                      )
                  AND p.effective_from <= @effective_date
                  AND (p.effective_to IS NULL OR p.effective_to >= @effective_date)
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = site.SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = site.SiteGroupId;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = site.LguCode!;
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
                p.discount_policy_reference_id,
                p.policy_code,
                p.policy_name,
                p.entitlement_type::text,
                'NATIONAL_LAW_FALLBACK' AS policy_resolution_basis,
                p.policy_level::text,
                p.policy_type::text,
                p.national_law_reference AS legal_basis_reference,
                p.local_ordinance_reference,
                p.national_law_reference,
                p.policy_status::text AS verification_status,
                'LOCKED_SCHEMA_POLICY_REFERENCE' AS beneficiary_residency_scope,
                'STATUTORY_DISCOUNT_VAT_EXEMPT' AS benefit_type,
                NULL::integer AS free_duration_minutes,
                false AS initial_rate_exempt_flag,
                false AS full_fee_exempt_flag,
                false AS overnight_excluded_flag,
                false AS valet_excluded_flag,
                false AS standalone_parking_excluded_flag,
                false AS driver_or_passenger_required_flag,
                'NOT_APPLICABLE' AS free_period_application,
                'APPLY_NATIONAL_STATUTORY_DISCOUNT' AS succeeding_hours_discount_rule,
                'VAT_EXCLUSIVE' AS discount_base_scope,
                'STATUTORY_FIRST' AS stacking_policy,
                COALESCE(p.national_law_reference, p.policy_code) AS legal_basis_priority,
                p.requires_operator_validation,
                p.requires_evidence_capture,
                p.effective_from,
                p.effective_to,
                p.policy_version AS source_reference
            FROM discounts.discount_policy_references AS p
            WHERE p.policy_code = @policy_code
              AND p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
              AND p.policy_level = 'NATIONAL_LAW'::discounts.discount_policy_level_enum
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
            site.JurisdictionId,
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            GetNullableString(reader, 7),
            GetNullableString(reader, 8),
            GetNullableString(reader, 9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.GetBoolean(14),
            reader.GetBoolean(15),
            reader.GetBoolean(16),
            reader.GetBoolean(17),
            reader.GetBoolean(18),
            reader.GetBoolean(19),
            reader.GetString(20),
            reader.GetString(21),
            reader.GetString(22),
            reader.GetString(23),
            reader.GetString(24),
            reader.GetBoolean(25),
            reader.GetBoolean(26),
            DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(27).UtcDateTime),
            reader.IsDBNull(28) ? null : DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(28).UtcDateTime),
            GetNullableString(reader, 29));

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

    private sealed record SiteRow(Guid SiteId, Guid SiteGroupId, string? LguCode)
    {
        public Guid? JurisdictionId => null;
    }

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
