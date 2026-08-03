using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ManagementPlatformStatutoryDiscountPolicyCoverageRepositoryIntegrationTests
{
    private static readonly Guid ActorUserId = Guid.Parse("81000000-0000-0000-0000-000000000010");
    private static readonly Guid CorrelationId = Guid.Parse("81000000-0000-0000-0000-000000000011");
    private static readonly Guid TestSiteGroupId = Guid.Parse("81000000-0000-0000-0000-000000000020");

    [Fact]
    public async Task ResolveScopeAsync_ForSite_ReturnsCanonicalLguJurisdictionMetadata()
    {
        var repository = CreateRepository();
        var site = await ReadSeededSiteAsync("PARANAQUE");
        await SeedActorShiftAsync(site);

        var result = await repository.ResolveScopeAsync(
            ActorUserId,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite,
            site.SiteId,
            CancellationToken.None);

        result.Status.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Resolved);
        var resolvedSite = result.Sites.Should().ContainSingle().Subject;
        resolvedSite.LocalGovernmentUnitId.Should().Be(site.LocalGovernmentUnitId);
        resolvedSite.CanonicalJurisdictionCode.Should().Be(site.JurisdictionCode);
        resolvedSite.CanonicalJurisdictionName.Should().Be(site.JurisdictionName);
        resolvedSite.CanonicalJurisdictionType.Should().Be("CITY");
        resolvedSite.ScopeJurisdictionClassification.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeJurisdictionSingleLgu);
        resolvedSite.LguCode.Should().Be(site.JurisdictionCode);
    }

    [Fact]
    public async Task ReadPolicyCandidatesAsync_UsesCanonicalSiteCoverageViewForParanaqueSeniorCoverage()
    {
        var repository = CreateRepository();
        var site = await ReadSeededSiteAsync("PARANAQUE");
        var scopeSite = ToScopeSite(site);

        var candidates = await repository.ReadPolicyCandidatesAsync(
            [scopeSite],
            [ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen],
            includeInactive: true,
            DateOnly.Parse("2026-07-30"),
            CancellationToken.None);

        var paranaqueSenior = candidates.Should().ContainSingle(candidate =>
            candidate.SiteId == site.SiteId &&
            candidate.EntitlementType == ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen &&
            candidate.PolicyCode == PolicyCodeFor("PARANAQUE")).Subject;
        paranaqueSenior.SourceClassification.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.CanonicalLguCoverageSource);
        paranaqueSenior.VerificationStatus.Should().Be("VERIFIED_ACTIVE_OPERATIONAL");
        paranaqueSenior.CoverageAvailable.Should().BeTrue();
        paranaqueSenior.AutoApplicationAllowed.Should().BeFalse();
        paranaqueSenior.SourceDocumentAvailable.Should().BeFalse();
        paranaqueSenior.BeneficiaryResidencyScope.Should().Be("RESIDENT_ONLY");
        paranaqueSenior.CoverageResolutionStatus.Should().Be("RESEARCH_COVERAGE_IDENTIFIED");
    }

    [Fact]
    public async Task ReadPolicyCandidatesAsync_DoesNotUseLegacyLguCodeAsAuthorityWhenCanonicalViewExists()
    {
        var repository = CreateRepository();
        var detachedSite = new ManagementPlatformStatutoryDiscountPolicyCoverageSite(
            Guid.Parse("81000000-0000-0000-0000-000000000099"),
            Guid.Parse("81000000-0000-0000-0000-000000000098"),
            "Detached legacy Site",
            "Detached legacy Site Group",
            "PARANAQUE",
            null,
            null,
            null,
            null,
            null,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeJurisdictionMissing);

        var candidates = await repository.ReadPolicyCandidatesAsync(
            [detachedSite],
            [ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen],
            includeInactive: true,
            DateOnly.Parse("2026-07-30"),
            CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadPolicyCandidatesAsync_KeepsMultiLguSiteGroupCoveragePerSite()
    {
        var repository = CreateRepository();
        var quezonCity = await ReadSeededSiteAsync("QUEZON_CITY");
        var paranaque = await ReadSeededSiteAsync("PARANAQUE");
        var sites = new[]
        {
            ToScopeSite(quezonCity) with { ScopeJurisdictionClassification = ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeJurisdictionMultiLgu },
            ToScopeSite(paranaque) with { ScopeJurisdictionClassification = ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeJurisdictionMultiLgu }
        };

        var candidates = await repository.ReadPolicyCandidatesAsync(
            sites,
            [ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen],
            includeInactive: true,
            DateOnly.Parse("2026-07-30"),
            CancellationToken.None);

        candidates.Should().Contain(candidate => candidate.SiteId == quezonCity.SiteId);
        candidates.Should().Contain(candidate => candidate.SiteId == paranaque.SiteId);
        candidates.Should().OnlyContain(candidate =>
            candidate.SiteId == quezonCity.SiteId ||
            candidate.SiteId == paranaque.SiteId);
        candidates.GroupBy(candidate => candidate.SiteId).Should().HaveCount(2);
    }

    private static ManagementPlatformStatutoryDiscountPolicyCoverageRepository CreateRepository() =>
        new(CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString());

    private static ManagementPlatformStatutoryDiscountPolicyCoverageSite ToScopeSite(SeededSite site) =>
        new(
            site.SiteId,
            site.SiteGroupId,
            site.SiteName,
            site.SiteGroupName,
            site.JurisdictionCode,
            site.LocalGovernmentUnitId,
            site.JurisdictionCode,
            site.JurisdictionName,
            site.JurisdictionType,
            site.MetropolitanAreaReferences,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeJurisdictionSingleLgu);

    private static async Task<SeededSite> ReadSeededSiteAsync(string jurisdictionCode)
    {
        await EnsureI010SeededSiteAsync(jurisdictionCode);

        const string sql = """
            SELECT
                site.site_id,
                site.site_group_id,
                COALESCE(NULLIF(site.site_name, ''), NULLIF(site.site_code, ''), site.site_id::text) AS site_name,
                COALESCE(NULLIF(site_group.site_group_name, ''), NULLIF(site_group.site_group_code, ''), site.site_group_id::text) AS site_group_name,
                jurisdiction.jurisdiction_id,
                jurisdiction.jurisdiction_code,
                jurisdiction.display_name,
                jurisdiction.jurisdiction_type::text,
                metro.metropolitan_area_references
            FROM sites.sites AS site
            JOIN sites.site_groups AS site_group ON site_group.site_group_id = site.site_group_id
            JOIN sites.jurisdictions AS jurisdiction ON jurisdiction.jurisdiction_id = site.local_government_unit_id
            LEFT JOIN LATERAL (
                SELECT string_agg(DISTINCT area.metropolitan_area_code, ',' ORDER BY area.metropolitan_area_code) AS metropolitan_area_references
                FROM sites.metropolitan_area_jurisdictions AS membership
                JOIN sites.metropolitan_areas AS area ON area.metropolitan_area_id = membership.metropolitan_area_id
                WHERE membership.jurisdiction_id = jurisdiction.jurisdiction_id
                  AND membership.membership_status = 'ACTIVE'::sites.jurisdiction_status_enum
                  AND area.metropolitan_area_status = 'ACTIVE'::sites.jurisdiction_status_enum
            ) AS metro ON true
            WHERE jurisdiction.psgc_code = @psgc_code
              AND site.site_code = @site_code
            ORDER BY site.site_code
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("jurisdiction_code", NpgsqlDbType.Text).Value = jurisdictionCode;
        command.Parameters.Add("psgc_code", NpgsqlDbType.Text).Value = PsgcCodeFor(jurisdictionCode);
        command.Parameters.Add("site_code", NpgsqlDbType.Text).Value = SiteCodeFor(jurisdictionCode);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Canonical seeded Site for jurisdiction '{jurisdictionCode}' was not found.");
        }

        return new SeededSite(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    private static async Task EnsureI010SeededSiteAsync(string jurisdictionCode)
    {
        const string sql = """
            INSERT INTO sites.jurisdictions (
                jurisdiction_id,
                jurisdiction_code,
                jurisdiction_type,
                display_name,
                short_display_name,
                province_name,
                region_name,
                country_code,
                psgc_code,
                jurisdiction_status,
                effective_from,
                source_reference
            )
            VALUES (
                @jurisdiction_id,
                @jurisdiction_code,
                'CITY'::sites.jurisdiction_type_enum,
                @jurisdiction_name,
                @jurisdiction_name,
                NULL,
                'National Capital Region',
                'PH',
                @psgc_code,
                'ACTIVE'::sites.jurisdiction_status_enum,
                @effective_from,
                'I-010 canonical LGU coverage integration test jurisdiction seed.'
            )
            ON CONFLICT ON CONSTRAINT uq_jurisdictions__psgc_code DO NOTHING;

            WITH selected_jurisdiction AS (
                SELECT jurisdiction_id, jurisdiction_code, display_name, province_name
                FROM sites.jurisdictions
                WHERE psgc_code = @psgc_code
                  AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum
                LIMIT 1
            )
            INSERT INTO sites.site_groups (
                site_group_id,
                site_group_code,
                site_group_name,
                business_label,
                description,
                operator_entity_name,
                timezone_name,
                default_currency_code,
                site_group_status,
                public_lookup_enabled,
                default_payment_enabled,
                effective_from
            )
            VALUES (
                @site_group_id,
                'I010-CANONICAL-LGU-COVERAGE',
                'I-010 Canonical LGU Coverage Test Group',
                'INTEGRATION_TEST',
                'Disabled integration-test Site Group for canonical LGU coverage repository proof.',
                'ExitPass Integration Tests',
                'Asia/Manila',
                'PHP',
                'ACTIVE',
                false,
                false,
                @effective_from
            )
            ON CONFLICT ON CONSTRAINT uq_site_groups__site_group_code DO UPDATE
            SET
                site_group_status = 'ACTIVE',
                public_lookup_enabled = false,
                default_payment_enabled = false,
                updated_at = now();

            WITH selected_jurisdiction AS (
                SELECT jurisdiction_id, jurisdiction_code, display_name, province_name
                FROM sites.jurisdictions
                WHERE psgc_code = @psgc_code
                  AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum
                LIMIT 1
            )
            INSERT INTO sites.sites (
                site_id,
                site_group_id,
                site_code,
                site_name,
                site_description,
                site_type,
                timezone_name,
                address_line1,
                city,
                province,
                country_code,
                lgu_code,
                local_government_unit_id,
                site_status,
                public_lookup_enabled,
                payment_enabled,
                effective_from
            )
            SELECT
                @site_id,
                @site_group_id,
                @site_code,
                'I-010 ' || selected_jurisdiction.display_name || ' Test Site',
                'Disabled integration-test Site for canonical LGU coverage repository proof.',
                'OPEN_LOT',
                'Asia/Manila',
                'Synthetic integration-test address only',
                selected_jurisdiction.display_name,
                selected_jurisdiction.province_name,
                'PH',
                selected_jurisdiction.jurisdiction_code,
                selected_jurisdiction.jurisdiction_id,
                'ACTIVE',
                false,
                false,
                @effective_from
            FROM selected_jurisdiction
            ON CONFLICT ON CONSTRAINT uq_sites__site_group_site_code DO UPDATE
            SET
                local_government_unit_id = EXCLUDED.local_government_unit_id,
                lgu_code = EXCLUDED.lgu_code,
                site_status = 'ACTIVE',
                public_lookup_enabled = false,
                payment_enabled = false,
                updated_at = now();

            WITH selected_jurisdiction AS (
                SELECT jurisdiction_id
                FROM sites.jurisdictions
                WHERE psgc_code = @psgc_code
                  AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum
                LIMIT 1
            )
            INSERT INTO sites.site_jurisdiction_assignments (
                site_jurisdiction_assignment_id,
                site_id,
                jurisdiction_id,
                assignment_status,
                effective_from,
                source_reference,
                approval_reference
            )
            SELECT
                @assignment_id,
                @site_id,
                selected_jurisdiction.jurisdiction_id,
                'ACTIVE',
                @effective_from,
                'I-010 canonical LGU coverage integration test.',
                'I-010 integration test seed'
            FROM selected_jurisdiction
            ON CONFLICT DO NOTHING;

            WITH selected_jurisdiction AS (
                SELECT jurisdiction_id, jurisdiction_code, display_name
                FROM sites.jurisdictions
                WHERE psgc_code = @psgc_code
                  AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum
                LIMIT 1
            )
            INSERT INTO discounts.statutory_discount_policy_registry (
                statutory_discount_policy_registry_id,
                policy_code,
                policy_name,
                policy_description,
                entitlement_type,
                policy_status,
                verification_status,
                policy_level,
                policy_type,
                policy_resolution_basis,
                benefit_type,
                discount_base_scope,
                jurisdiction_id,
                local_government_unit_id,
                jurisdiction_code,
                jurisdiction_name,
                beneficiary_residency_scope,
                requires_evidence,
                required_evidence_type,
                requires_operator_validation,
                ordinance_reference,
                source_reference,
                source_scan_date,
                source_document_available,
                coverage_available,
                auto_application_allowed,
                reviewed_by,
                reviewed_at,
                effective_from
            )
            SELECT
                @policy_registry_id,
                @policy_code,
                @policy_name,
                @policy_description,
                'SENIOR_CITIZEN',
                'ACTIVE',
                @verification_status::discounts.policy_verification_status_enum,
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE_APPLIED',
                @benefit_type::discounts.parking_benefit_type_enum,
                'NOT_APPLICABLE',
                selected_jurisdiction.jurisdiction_id,
                selected_jurisdiction.jurisdiction_id,
                selected_jurisdiction.jurisdiction_code,
                selected_jurisdiction.display_name,
                @beneficiary_residency_scope::discounts.beneficiary_residency_scope_enum,
                true,
                'SENIOR_CITIZEN_ID',
                true,
                @ordinance_reference,
                @source_reference,
                '2026-07-28',
                @source_document_available,
                true,
                false,
                'I-010 integration validation',
                @effective_from,
                @effective_from
            FROM selected_jurisdiction
            ON CONFLICT ON CONSTRAINT uq_sd_policy_registry__policy_code DO UPDATE
            SET
                policy_status = 'ACTIVE',
                verification_status = EXCLUDED.verification_status,
                benefit_type = EXCLUDED.benefit_type,
                jurisdiction_id = EXCLUDED.jurisdiction_id,
                local_government_unit_id = EXCLUDED.local_government_unit_id,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                jurisdiction_name = EXCLUDED.jurisdiction_name,
                beneficiary_residency_scope = EXCLUDED.beneficiary_residency_scope,
                ordinance_reference = EXCLUDED.ordinance_reference,
                source_reference = EXCLUDED.source_reference,
                source_document_available = EXCLUDED.source_document_available,
                coverage_available = true,
                auto_application_allowed = false,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = EXCLUDED.reviewed_at,
                updated_at = now();

            WITH selected_jurisdiction AS (
                SELECT jurisdiction_id
                FROM sites.jurisdictions
                WHERE psgc_code = @psgc_code
                  AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum
                LIMIT 1
            )
            INSERT INTO discounts.statutory_discount_policy_registry_lgu_scopes (
                statutory_discount_policy_registry_lgu_scope_id,
                statutory_discount_policy_registry_id,
                local_government_unit_id,
                coverage_available,
                auto_application_allowed,
                source_scan_date,
                source_reference,
                scope_status
            )
            SELECT
                @policy_scope_id,
                @policy_registry_id,
                selected_jurisdiction.jurisdiction_id,
                true,
                false,
                '2026-07-28',
                @source_reference,
                'ACTIVE'
            FROM selected_jurisdiction
            ON CONFLICT ON CONSTRAINT uq_sd_policy_registry_lgu_scopes__registry_lgu DO UPDATE
            SET
                coverage_available = true,
                auto_application_allowed = false,
                scope_status = 'ACTIVE',
                source_reference = EXCLUDED.source_reference,
                updated_at = now();
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("jurisdiction_code", NpgsqlDbType.Text).Value = jurisdictionCode;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = JurisdictionIdFor(jurisdictionCode);
        command.Parameters.Add("jurisdiction_name", NpgsqlDbType.Text).Value = JurisdictionNameFor(jurisdictionCode);
        command.Parameters.Add("psgc_code", NpgsqlDbType.Text).Value = PsgcCodeFor(jurisdictionCode);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = TestSiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = SiteIdFor(jurisdictionCode);
        command.Parameters.Add("site_code", NpgsqlDbType.Text).Value = SiteCodeFor(jurisdictionCode);
        command.Parameters.Add("assignment_id", NpgsqlDbType.Uuid).Value = AssignmentIdFor(jurisdictionCode);
        command.Parameters.Add("policy_registry_id", NpgsqlDbType.Uuid).Value = PolicyRegistryIdFor(jurisdictionCode);
        command.Parameters.Add("policy_scope_id", NpgsqlDbType.Uuid).Value = PolicyScopeIdFor(jurisdictionCode);
        command.Parameters.Add("policy_code", NpgsqlDbType.Text).Value = PolicyCodeFor(jurisdictionCode);
        command.Parameters.Add("policy_name", NpgsqlDbType.Text).Value = PolicyNameFor(jurisdictionCode);
        command.Parameters.Add("policy_description", NpgsqlDbType.Text).Value = PolicyDescriptionFor(jurisdictionCode);
        command.Parameters.Add("verification_status", NpgsqlDbType.Text).Value = string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "VERIFIED_ACTIVE_OPERATIONAL"
            : "VERIFIED_SECONDARY";
        command.Parameters.Add("benefit_type", NpgsqlDbType.Text).Value = string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "FULL_FEE_EXEMPTION"
            : "FREE_DURATION";
        command.Parameters.Add("beneficiary_residency_scope", NpgsqlDbType.Text).Value = string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "RESIDENT_ONLY"
            : "MIXED_OR_CONFLICTING";
        command.Parameters.Add("ordinance_reference", NpgsqlDbType.Text).Value = string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? DBNull.Value
            : "I010-QUEZON-REFERENCE";
        command.Parameters.Add("source_reference", NpgsqlDbType.Text).Value = string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "I-010 Paranaque operational coverage source unavailable online."
            : "I-010 Quezon City secondary source.";
        command.Parameters.Add("source_document_available", NpgsqlDbType.Boolean).Value = !string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase);
        command.Parameters.Add("effective_from", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2026-07-27T16:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedActorShiftAsync(SeededSite site)
    {
        const string sql = """
            INSERT INTO identity.users (
                user_id,
                username,
                email,
                email_normalized,
                display_name,
                user_type,
                user_status,
                effective_from,
                effective_to,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @actor_user_id,
                'i010-canonical-lgu-coverage-user',
                'i010-canonical-lgu-coverage-user@example.test',
                'I010-CANONICAL-LGU-COVERAGE-USER@EXAMPLE.TEST',
                'I-010 Canonical LGU Coverage User',
                'SITE_OPERATOR',
                'ACTIVE',
                @effective_from,
                @effective_to,
                @actor_user_id,
                @actor_user_id
            )
            ON CONFLICT (user_id) DO UPDATE
            SET
                user_status = 'ACTIVE',
                updated_by_user_id = EXCLUDED.updated_by_user_id,
                row_version = identity.users.row_version + 1;

            INSERT INTO operator_console.hr_identity_mappings (
                hr_identity_mapping_id,
                user_id,
                hr_provider_code,
                external_person_id_hash,
                external_person_id_masked,
                mapping_status,
                effective_from,
                effective_to,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @hr_identity_mapping_id,
                @actor_user_id,
                'I010_TEST_HR',
                @external_person_id_hash,
                'I010-****',
                'ACTIVE',
                @effective_from,
                @effective_to,
                @correlation_id,
                @actor_user_id,
                @actor_user_id
            )
            ON CONFLICT (hr_identity_mapping_id) DO NOTHING;

            INSERT INTO operator_console.operator_shifts (
                operator_shift_id,
                hr_provider_code,
                external_shift_id_hash,
                external_shift_id_masked,
                hr_identity_mapping_id,
                operator_user_id,
                site_group_id,
                site_id,
                scheduled_start_at,
                scheduled_end_at,
                source_imported_at,
                import_status_code,
                source_system_code,
                source_status_code,
                operational_status,
                active_from,
                active_to,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @operator_shift_id,
                'I010_TEST_HR',
                @external_shift_id_hash,
                'I010-SHIFT-****',
                @hr_identity_mapping_id,
                @actor_user_id,
                @site_group_id,
                @site_id,
                @effective_from,
                @effective_to,
                now(),
                'IMPORTED',
                'I010_INTEGRATION_TEST',
                'ACTIVE',
                'ACTIVE',
                @effective_from,
                @effective_to,
                @correlation_id,
                @actor_user_id,
                @actor_user_id
            )
            ON CONFLICT (operator_shift_id) DO NOTHING;
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = ActorUserId;
        command.Parameters.Add("hr_identity_mapping_id", NpgsqlDbType.Uuid).Value = Guid.Parse("81000000-0000-0000-0000-000000000012");
        command.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = Guid.Parse("81000000-0000-0000-0000-000000000013");
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = site.SiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = site.SiteId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = CorrelationId;
        command.Parameters.Add("external_person_id_hash", NpgsqlDbType.Char).Value = new string('a', 64);
        command.Parameters.Add("external_shift_id_hash", NpgsqlDbType.Char).Value = new string('b', 64);
        command.Parameters.Add("effective_from", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        command.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2035-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private sealed record SeededSite(
        Guid SiteId,
        Guid SiteGroupId,
        string SiteName,
        string SiteGroupName,
        Guid LocalGovernmentUnitId,
        string JurisdictionCode,
        string JurisdictionName,
        string JurisdictionType,
        string? MetropolitanAreaReferences);

    private static Guid SiteIdFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("81000000-0000-0000-0000-000000000021")
            : Guid.Parse("81000000-0000-0000-0000-000000000022");

    private static Guid AssignmentIdFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("81000000-0000-0000-0000-000000000031")
            : Guid.Parse("81000000-0000-0000-0000-000000000032");

    private static Guid JurisdictionIdFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("81000000-0000-0000-0000-000000000061")
            : Guid.Parse("81000000-0000-0000-0000-000000000062");

    private static string SiteCodeFor(string jurisdictionCode) =>
        $"I010-CANONICAL-{jurisdictionCode}";

    private static string JurisdictionNameFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "City of Paranaque"
            : "Quezon City";

    private static string PsgcCodeFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "1381000000"
            : "1381300000";

    private static Guid PolicyRegistryIdFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("81000000-0000-0000-0000-000000000041")
            : Guid.Parse("81000000-0000-0000-0000-000000000042");

    private static Guid PolicyScopeIdFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("81000000-0000-0000-0000-000000000051")
            : Guid.Parse("81000000-0000-0000-0000-000000000052");

    private static string PolicyCodeFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "I010_PARANAQUE_SC_CANONICAL"
            : "I010_QUEZON_SC_CANONICAL";

    private static string PolicyNameFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "I-010 Parañaque Senior Citizen Canonical Coverage"
            : "I-010 Quezon City Senior Citizen Canonical Coverage";

    private static string PolicyDescriptionFor(string jurisdictionCode) =>
        string.Equals(jurisdictionCode, "PARANAQUE", StringComparison.OrdinalIgnoreCase)
            ? "Paranaque Senior Citizen free parking coverage is verified operationally; source text remains unavailable online."
            : "Quezon City Senior Citizen coverage seed for canonical LGU coverage integration proof.";
}
