using System.Data;
using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read-only repository for statutory discount policy resolution.
///
/// ExitPass v1.3 Invariants Enforced:
/// - Reads only site and statutory discount policy reference state.
/// - Does not create drafts, mutate payable basis, create payment attempts, call providers, open gates,
///   create coupons, or create reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository
    : IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository
{
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

        var capabilities = await ReadPolicyRegistryCapabilitiesAsync(connection, cancellationToken);
        if (!capabilities.HasDedicatedRegistry &&
            !capabilities.HasCompatibilityTable &&
            !capabilities.HasVersionedRegistry)
        {
            return NotResolved(
                "STATUTORY_DISCOUNT_POLICY_NOT_RESOLVED",
                "STATUTORY_DISCOUNT_POLICY_NOT_RESOLVED",
                site.SiteId,
                site.SiteGroupId,
                site.JurisdictionId);
        }

        var versionedAuthority = capabilities.HasVersionedRegistry
            ? await ReadVersionedPolicyAuthorityAsync(
                connection,
                site,
                request.EntitlementType,
                request.EffectiveDate,
                cancellationToken)
            : VersionedPolicyAuthority.None;
        if (versionedAuthority.Policy is not null)
        {
            return Resolved(versionedAuthority.Policy);
        }

        if (versionedAuthority.HasCandidate)
        {
            return NotResolved(
                "STATUTORY_DISCOUNT_POLICY_UNVERIFIED",
                "STATUTORY_DISCOUNT_POLICY_UNVERIFIED",
                site.SiteId,
                site.SiteGroupId,
                versionedAuthority.JurisdictionId);
        }

        var verifiedLocalPolicy = capabilities.HasDedicatedRegistry
            ? await ReadDedicatedLocalPolicyAsync(connection, site, request.EntitlementType, request.EffectiveDate, cancellationToken)
            : null;
        verifiedLocalPolicy ??= capabilities.HasCompatibilityTable
            ? await ReadCompatibilityLocalPolicyAsync(connection, site, request.EntitlementType, request.EffectiveDate, cancellationToken)
            : null;
        if (verifiedLocalPolicy is not null)
        {
            return Resolved(verifiedLocalPolicy);
        }

        var hasUnverifiedLocalPolicy =
            (capabilities.HasDedicatedRegistry &&
                await HasDedicatedUnreadyLocalPolicyAsync(connection, site, request.EntitlementType, request.EffectiveDate, cancellationToken)) ||
            (capabilities.HasCompatibilityTable &&
                await HasCompatibilityUnverifiedLocalPolicyAsync(connection, site, request.EntitlementType, request.EffectiveDate, cancellationToken));
        if (hasUnverifiedLocalPolicy)
        {
            return NotResolved(
                "STATUTORY_DISCOUNT_POLICY_UNVERIFIED",
                "STATUTORY_DISCOUNT_POLICY_UNVERIFIED",
                site.SiteId,
                site.SiteGroupId,
                site.JurisdictionId);
        }

        return NotResolved(
            "LOCAL_PARKING_ORDINANCE_NOT_APPLICABLE",
            "LOCAL_PARKING_ORDINANCE_NOT_APPLICABLE",
            site.SiteId,
            site.SiteGroupId,
            site.JurisdictionId);
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

    private static async Task<PolicyRegistryCapabilities> ReadPolicyRegistryCapabilitiesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'discounts'
                      AND table_name = 'statutory_discount_policy_registry'
                ) AS has_dedicated_registry,
                EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'discounts'
                      AND table_name = 'discount_policy_references'
                ) AS has_compatibility_table,
                EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'discounts'
                      AND table_name = 'statutory_discount_policy_versions'
                ) AS has_versioned_registry;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new PolicyRegistryCapabilities(
                HasDedicatedRegistry: false,
                HasCompatibilityTable: false,
                HasVersionedRegistry: false);
        }

        return new PolicyRegistryCapabilities(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private static async Task<VersionedPolicyAuthority> ReadVersionedPolicyAuthorityAsync(
        NpgsqlConnection connection,
        SiteRow site,
        string entitlementType,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string assignmentSql = """
            SELECT a.jurisdiction_id
            FROM sites.site_jurisdiction_assignments AS a
            JOIN sites.jurisdictions AS j
              ON j.jurisdiction_id = a.jurisdiction_id
            WHERE a.site_id = @site_id
              AND a.assignment_status = 'ACTIVE'
              AND a.effective_from <= now()
              AND (a.effective_to IS NULL OR a.effective_to > now())
              AND j.jurisdiction_status = 'ACTIVE'
              AND j.jurisdiction_type IN ('CITY', 'MUNICIPALITY')
            ORDER BY a.effective_from DESC, a.site_jurisdiction_assignment_id
            LIMIT 2;
            """;

        var jurisdictionIds = new List<Guid>();
        await using (var assignmentCommand = new NpgsqlCommand(assignmentSql, connection))
        {
            assignmentCommand.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = site.SiteId;
            await using var assignmentReader = await assignmentCommand.ExecuteReaderAsync(cancellationToken);
            while (await assignmentReader.ReadAsync(cancellationToken))
            {
                jurisdictionIds.Add(assignmentReader.GetGuid(0));
            }
        }

        if (jurisdictionIds.Count != 1)
        {
            return new VersionedPolicyAuthority(
                HasCandidate: true,
                JurisdictionId: null,
                Policy: null);
        }

        var jurisdictionId = jurisdictionIds[0];
        const string candidateSql = """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.statutory_discount_policy_versions AS version
                WHERE version.jurisdiction_id = @jurisdiction_id
                  AND version.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
                  AND (
                        (version.policy_scope_type = 'JURISDICTION' AND version.site_group_id IS NULL AND version.site_id IS NULL)
                     OR (version.policy_scope_type = 'SITE_GROUP' AND version.site_group_id = @site_group_id AND version.site_id IS NULL)
                     OR (version.policy_scope_type = 'SITE' AND version.site_id = @site_id)
                  )
            );
            """;

        bool hasCandidate;
        await using (var candidateCommand = new NpgsqlCommand(candidateSql, connection))
        {
            candidateCommand.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = jurisdictionId;
            candidateCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
            candidateCommand.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = site.SiteGroupId;
            candidateCommand.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = site.SiteId;
            hasCandidate = (bool)(await candidateCommand.ExecuteScalarAsync(cancellationToken) ?? false);
        }

        if (!hasCandidate)
        {
            return VersionedPolicyAuthority.None;
        }

        const string policySql = """
            WITH eligible AS (
                SELECT
                    registry.statutory_discount_policy_registry_id,
                    version.policy_code,
                    COALESCE(version.policy_version_label, registry.policy_name) AS policy_version_label,
                    version.entitlement_type::text AS entitlement_type,
                    version.policy_resolution_basis::text AS policy_resolution_basis,
                    version.policy_level::text AS policy_level,
                    version.policy_type::text AS policy_type,
                    COALESCE(version.legal_basis_reference, version.ordinance_number, registry.legal_basis_reference, registry.ordinance_reference) AS legal_basis_reference,
                    COALESCE(version.ordinance_number, registry.ordinance_reference) AS ordinance_reference,
                    registry.national_law_reference,
                    version.source_verification_status::text AS source_verification_status,
                    version.beneficiary_residency_scope::text AS beneficiary_residency_scope,
                    version.benefit_type::text AS benefit_type,
                    version.free_duration_minutes,
                    registry.initial_rate_exempt,
                    version.full_fee_exempt,
                    registry.overnight_excluded,
                    registry.valet_excluded,
                    registry.standalone_parking_excluded,
                    registry.driver_or_passenger_required,
                    'NOT_APPLICABLE' AS free_period_application,
                    CASE
                        WHEN version.free_duration_minutes IS NOT NULL THEN 'APPLY_LOCAL_FREE_DURATION'
                        ELSE 'APPLY_NATIONAL_STATUTORY_DISCOUNT'
                    END AS succeeding_hours_discount_rule,
                    version.discount_base_scope::text AS discount_base_scope,
                    'STATUTORY_FIRST' AS stacking_policy,
                    COALESCE(version.legal_basis_reference, version.ordinance_number, version.source_reference, version.policy_code) AS legal_basis_priority,
                    registry.requires_operator_validation,
                    EXISTS (
                        SELECT 1
                        FROM discounts.statutory_discount_policy_version_evidence_requirements AS evidence
                        WHERE evidence.statutory_discount_policy_version_id = version.statutory_discount_policy_version_id
                          AND evidence.requirement_status = 'REQUIRED'
                    ) AS requires_evidence,
                    COALESCE(version.transaction_use_effective_from, registry.effective_from) AS effective_from,
                    COALESCE(version.transaction_use_effective_to, registry.effective_to) AS effective_to,
                    version.source_reference,
                    CASE version.policy_scope_type
                        WHEN 'SITE' THEN 3
                        WHEN 'SITE_GROUP' THEN 2
                        ELSE 1
                    END AS scope_weight,
                    version.precedence_rank
                FROM discounts.statutory_discount_policy_versions AS version
                JOIN discounts.statutory_discount_policy_registry AS registry
                  ON registry.statutory_discount_policy_registry_id = version.statutory_discount_policy_registry_id
                WHERE version.jurisdiction_id = @jurisdiction_id
                  AND version.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
                  AND registry.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
                  AND version.source_verification_status IN (
                        'VERIFIED_OFFICIAL'::discounts.policy_verification_status_enum,
                        'VERIFIED_ACTIVE_OPERATIONAL'::discounts.policy_verification_status_enum,
                        'ACTIVE_APPROVED'::discounts.policy_verification_status_enum
                  )
                  AND version.transaction_publication_status = 'ACTIVE_FOR_TRANSACTION_USE'
                  AND version.parking_service_applicability = 'COVERED'
                  AND version.policy_effect_support_status = 'SUPPORTED_BY_CURRENT_CALCULATION'
                  AND (
                        (version.policy_scope_type = 'JURISDICTION' AND version.site_group_id IS NULL AND version.site_id IS NULL)
                     OR (version.policy_scope_type = 'SITE_GROUP' AND version.site_group_id = @site_group_id AND version.site_id IS NULL)
                     OR (version.policy_scope_type = 'SITE' AND version.site_id = @site_id)
                  )
                  AND (version.transaction_use_effective_from IS NULL OR version.transaction_use_effective_from < (@effective_date::date + interval '1 day'))
                  AND (version.transaction_use_effective_to IS NULL OR version.transaction_use_effective_to >= @effective_date::date)
                  AND NOT (
                        version.suspension_starts_at IS NOT NULL
                    AND version.suspension_starts_at < (@effective_date::date + interval '1 day')
                    AND (version.suspension_ends_at IS NULL OR version.suspension_ends_at >= @effective_date::date)
                  )
                  AND version.withdrawn_at IS NULL
                  AND version.retired_at IS NULL
                  AND version.superseded_by_policy_version_id IS NULL
            ),
            highest_scope AS (
                SELECT MAX(scope_weight) AS scope_weight
                FROM eligible
            ),
            highest_precedence AS (
                SELECT MIN(eligible.precedence_rank) AS precedence_rank
                FROM eligible
                JOIN highest_scope
                  ON highest_scope.scope_weight = eligible.scope_weight
            ),
            selected AS (
                SELECT eligible.*
                FROM eligible
                JOIN highest_scope
                  ON highest_scope.scope_weight = eligible.scope_weight
                JOIN highest_precedence
                  ON highest_precedence.precedence_rank = eligible.precedence_rank
            )
            SELECT
                statutory_discount_policy_registry_id,
                policy_code,
                policy_version_label,
                entitlement_type,
                policy_resolution_basis,
                policy_level,
                policy_type,
                legal_basis_reference,
                ordinance_reference,
                national_law_reference,
                source_verification_status,
                beneficiary_residency_scope,
                benefit_type,
                free_duration_minutes,
                initial_rate_exempt,
                full_fee_exempt,
                overnight_excluded,
                valet_excluded,
                standalone_parking_excluded,
                driver_or_passenger_required,
                free_period_application,
                succeeding_hours_discount_rule,
                discount_base_scope,
                stacking_policy,
                legal_basis_priority,
                requires_operator_validation,
                requires_evidence,
                effective_from,
                effective_to,
                source_reference
            FROM selected
            WHERE (SELECT COUNT(*) FROM selected) = 1
            ORDER BY
                effective_from DESC NULLS LAST,
                policy_code,
                policy_version_label
            LIMIT 1;
            """;

        await using var policyCommand = new NpgsqlCommand(policySql, connection);
        policyCommand.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = jurisdictionId;
        policyCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        policyCommand.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = site.SiteGroupId;
        policyCommand.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = site.SiteId;
        policyCommand.Parameters.Add("effective_date", NpgsqlDbType.Date).Value = effectiveDate;

        var policy = await ReadPolicyAsync(policyCommand, site, cancellationToken);
        return new VersionedPolicyAuthority(
            HasCandidate: true,
            JurisdictionId: jurisdictionId,
            Policy: policy is null ? null : policy with { JurisdictionId = jurisdictionId });
    }

    private static async Task<OperatorConsoleResolvedStatutoryDiscountPolicy?> ReadDedicatedLocalPolicyAsync(
        NpgsqlConnection connection,
        SiteRow site,
        string entitlementType,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                p.statutory_discount_policy_registry_id,
                p.policy_code,
                p.policy_name,
                p.entitlement_type::text,
                p.policy_resolution_basis::text,
                p.policy_level::text,
                p.policy_type::text,
                COALESCE(p.legal_basis_reference, p.ordinance_reference, p.national_law_reference) AS legal_basis_reference,
                p.ordinance_reference,
                p.national_law_reference,
                p.verification_status::text AS verification_status,
                p.beneficiary_residency_scope::text,
                p.benefit_type::text,
                p.free_duration_minutes,
                p.initial_rate_exempt,
                p.full_fee_exempt,
                p.overnight_excluded,
                p.valet_excluded,
                p.standalone_parking_excluded,
                p.driver_or_passenger_required,
                'NOT_APPLICABLE' AS free_period_application,
                CASE
                    WHEN p.free_duration_minutes IS NOT NULL THEN 'APPLY_LOCAL_FREE_DURATION'
                    ELSE 'APPLY_NATIONAL_STATUTORY_DISCOUNT'
                END AS succeeding_hours_discount_rule,
                p.discount_base_scope::text,
                'STATUTORY_FIRST' AS stacking_policy,
                COALESCE(p.legal_basis_reference, p.ordinance_reference, p.national_law_reference, p.policy_code) AS legal_basis_priority,
                p.requires_operator_validation,
                (p.requires_evidence AND p.required_evidence_type IS NOT NULL) AS requires_evidence,
                p.effective_from,
                p.effective_to,
                p.source_reference
            FROM discounts.statutory_discount_policy_registry AS p
            WHERE (
                    p.site_id = @site_id
                    OR p.site_group_id = @site_group_id
                    OR p.jurisdiction_code = @lgu_code
                    OR EXISTS (
                        SELECT 1
                        FROM discounts.statutory_parking_site_policy_coverage AS coverage
                        WHERE coverage.site_id = @site_id
                          AND coverage.entitlement_type = p.entitlement_type
                          AND coverage.statutory_discount_policy_registry_id = p.statutory_discount_policy_registry_id
                          AND coverage.coverage_available
                          AND coverage.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
                    )
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
                CASE
                    WHEN p.site_id = @site_id THEN 0
                    WHEN p.site_group_id = @site_group_id THEN 1
                    WHEN p.jurisdiction_code = @lgu_code THEN 2
                    ELSE 3
                END,
                CASE p.verification_status
                    WHEN 'ACTIVE_APPROVED'::discounts.policy_verification_status_enum THEN 0
                    WHEN 'VERIFIED_OFFICIAL'::discounts.policy_verification_status_enum THEN 1
                    WHEN 'APPROVED_FOR_PILOT'::discounts.policy_verification_status_enum THEN 2
                    WHEN 'VERIFIED_SECONDARY'::discounts.policy_verification_status_enum THEN 3
                    ELSE 4
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

    private static async Task<OperatorConsoleResolvedStatutoryDiscountPolicy?> ReadCompatibilityLocalPolicyAsync(
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

    private static async Task<bool> HasDedicatedUnreadyLocalPolicyAsync(
        NpgsqlConnection connection,
        SiteRow site,
        string entitlementType,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.statutory_discount_policy_registry AS p
                WHERE (
                        p.site_id = @site_id
                        OR p.site_group_id = @site_group_id
                        OR p.jurisdiction_code = @lgu_code
                      )
                  AND p.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
                  AND p.policy_level IN (
                        'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                        'SITE_POLICY'::discounts.discount_policy_level_enum,
                        'OPERATIONAL_POLICY'::discounts.discount_policy_level_enum
                      )
                  AND p.effective_from <= @effective_date
                  AND (p.effective_to IS NULL OR p.effective_to >= @effective_date)
                  AND (
                        p.policy_status <> 'ACTIVE'::discounts.discount_policy_status_enum
                        OR p.verification_status IN (
                            'LEAD_UNVERIFIED'::discounts.policy_verification_status_enum,
                            'VERIFIED_SECONDARY'::discounts.policy_verification_status_enum,
                            'PROPOSED_ONLY'::discounts.policy_verification_status_enum,
                            'REJECTED'::discounts.policy_verification_status_enum
                        )
                        OR btrim(COALESCE(p.source_reference, '')) = ''
                      )
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

    private static async Task<bool> HasCompatibilityUnverifiedLocalPolicyAsync(
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

    private sealed record PolicyRegistryCapabilities(
        bool HasDedicatedRegistry,
        bool HasCompatibilityTable,
        bool HasVersionedRegistry);

    private sealed record VersionedPolicyAuthority(
        bool HasCandidate,
        Guid? JurisdictionId,
        OperatorConsoleResolvedStatutoryDiscountPolicy? Policy)
    {
        public static VersionedPolicyAuthority None { get; } = new(false, null, null);
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
