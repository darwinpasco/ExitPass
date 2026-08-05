using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class ManagementPlatformStatutoryEvidenceGovernanceDatabaseIntegrationTests
{
    private const string GovernancePath = "/v1/ops/management-platform/statutory-discounts/evidence-governance";
    private static readonly Guid ServiceIdentityId = Guid.Parse("91400000-0000-0000-0000-000000001001");
    private static readonly Guid SiteGroupId = Guid.Parse("91400000-0000-0000-0000-000000001101");
    private static readonly Guid SiteId = Guid.Parse("91400000-0000-0000-0000-000000001201");
    private static readonly Guid OtherSiteGroupId = Guid.Parse("91400000-0000-0000-0000-000000001102");
    private static readonly Guid OtherSiteId = Guid.Parse("91400000-0000-0000-0000-000000001202");
    private static readonly Guid CorrelationId = Guid.Parse("91400000-0000-0000-0000-000000001301");

    [Fact]
    public async Task GovernanceRead_UsesRealRepositoryAndDoesNotMutateBusinessState()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
        EnsureDisposableI014Database(connectionString);
        await SeedSyntheticGovernanceConfigurationAsync(connectionString);
        var before = await ReadManifestAsync(connectionString);

        using var factory = new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = connectionString,
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true",
                ["CentralPms:StatutoryEvidence:Upload:ProviderType"] = "S3_COMPATIBLE",
                ["CentralPms:StatutoryEvidence:Upload:Endpoint"] = "https://storage.internal",
                ["CentralPms:StatutoryEvidence:Upload:PublicUploadEndpoint"] = "https://upload.internal",
                ["CentralPms:StatutoryEvidence:Upload:Region"] = "ap-southeast-1",
                ["CentralPms:StatutoryEvidence:Upload:BucketName"] = "i014-private-evidence",
                ["CentralPms:StatutoryEvidence:Upload:BucketReference"] = "protected-evidence",
                ["CentralPms:StatutoryEvidence:Upload:AccessKeyId"] = "i014-test-access-key-id",
                ["CentralPms:StatutoryEvidence:Upload:SecretAccessKey"] = "i014-test-secret-signing-material",
                ["CentralPms:StatutoryEvidence:Upload:MaxContentLengthBytes"] = "1048576",
                ["CentralPms:StatutoryEvidence:Upload:AuthorizationTtlSeconds"] = "300",
                ["CentralPms:StatutoryEvidence:Upload:AllowedContentTypes:0"] = "image/jpeg",
                ["CentralPms:StatutoryEvidence:Upload:AllowedContentTypes:1"] = "image/png",
                ["CentralPms:StatutoryEvidence:Upload:RequireSha256Checksum"] = "true"
            });
        using var client = factory.CreateClient();
        AddServiceHeaders(client);

        using var siteResponse = await client.GetAsync($"{GovernancePath}/sites/{SiteId}");
        using var siteGroupResponse = await client.GetAsync($"{GovernancePath}/site-groups/{SiteGroupId}");
        using var unauthorizedResponse = await client.GetAsync($"{GovernancePath}/sites/{OtherSiteId}");
        using var collectionResponse = await client.GetAsync(GovernancePath);

        siteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        siteGroupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        unauthorizedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        collectionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await siteResponse.Content.ReadFromJsonAsync<ManagementPlatformStatutoryEvidenceGovernanceResponse>();
        body.Should().NotBeNull();
        var site = body!.Sites.Should().ContainSingle().Subject;
        site.SiteReference.Should().Be(SiteId);
        site.SiteGroupReference.Should().Be(SiteGroupId);
        site.EvidenceCaptureEnabled.Should().BeTrue();
        site.UploadAuthorizationReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.UploadFinalizationReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.MalwareScanningExecutionReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Disabled);
        site.SecurePreviewReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
        site.RetentionWorkerReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
        site.DeletionWorkerReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);

        var after = await ReadManifestAsync(connectionString);
        after.Should().BeEquivalentTo(before);
    }

    private static async Task SeedSyntheticGovernanceConfigurationAsync(string connectionString)
    {
        const string sql = """
            INSERT INTO identity.service_identities (
                service_identity_id,
                service_identity_code,
                service_identity_name,
                identity_type,
                identity_status,
                owning_service_name,
                credential_type,
                effective_from)
            VALUES (
                @service_identity_id,
                'I014-MANAGEMENT-PLATFORM-GOVERNANCE-READER',
                'I-014 Synthetic Management Platform Governance Reader',
                'INTERNAL_SERVICE'::identity.service_identity_type_enum,
                'ACTIVE'::identity.service_identity_status_enum,
                'Central PMS I-014',
                'NONE'::identity.service_credential_type_enum,
                TIMESTAMPTZ '2026-08-04 00:00:00+00')
            ON CONFLICT (service_identity_code) DO NOTHING;

            INSERT INTO sites.site_groups (
                site_group_id,
                site_group_code,
                site_group_name,
                timezone_name,
                default_currency_code,
                site_group_status,
                public_lookup_enabled,
                default_payment_enabled,
                effective_from)
            VALUES (
                @site_group_id,
                'I014-SYNTHETIC-GROUP',
                'I-014 Synthetic Evidence Governance Group',
                'Asia/Manila',
                'PHP',
                'ACTIVE'::sites.site_group_status_enum,
                false,
                false,
                TIMESTAMPTZ '2026-08-04 00:00:00+00'),
                (
                @other_site_group_id,
                'I014-SYNTHETIC-OTHER-GROUP',
                'I-014 Synthetic Other Evidence Governance Group',
                'Asia/Manila',
                'PHP',
                'ACTIVE'::sites.site_group_status_enum,
                false,
                false,
                TIMESTAMPTZ '2026-08-04 00:00:00+00')
            ON CONFLICT (site_group_id) DO NOTHING;

            INSERT INTO sites.sites (
                site_id,
                site_group_id,
                site_code,
                site_name,
                site_type,
                timezone_name,
                country_code,
                site_status,
                public_lookup_enabled,
                payment_enabled,
                effective_from)
            VALUES (
                @site_id,
                @site_group_id,
                'I014-SYNTHETIC-SITE',
                'I-014 Synthetic Evidence Governance Site',
                'OTHER'::sites.site_type_enum,
                'Asia/Manila',
                'PH',
                'ACTIVE'::sites.site_status_enum,
                false,
                false,
                TIMESTAMPTZ '2026-08-04 00:00:00+00'),
                (
                @other_site_id,
                @other_site_group_id,
                'I014-SYNTHETIC-OTHER-SITE',
                'I-014 Synthetic Other Evidence Governance Site',
                'OTHER'::sites.site_type_enum,
                'Asia/Manila',
                'PH',
                'ACTIVE'::sites.site_status_enum,
                false,
                false,
                TIMESTAMPTZ '2026-08-04 00:00:00+00')
            ON CONFLICT (site_id) DO NOTHING;

            INSERT INTO discounts.statutory_evidence_retention_policies (
                retention_class_code,
                retention_policy_version,
                policy_status,
                environment_scope,
                purpose_code,
                effective_from)
            VALUES (
                'I014_SYNTHETIC_RETENTION',
                'v1',
                'APPROVED_ENABLED',
                'LOCAL_TEST',
                'STATUTORY_EVIDENCE_GOVERNANCE_READ_PROOF',
                TIMESTAMPTZ '2026-08-04 00:00:00+00')
            ON CONFLICT (retention_class_code, retention_policy_version) DO NOTHING;

            INSERT INTO discounts.statutory_evidence_principal_scope_grants (
                statutory_evidence_principal_scope_grant_id,
                actor_service_identity_id,
                source_channel,
                site_id,
                site_group_id,
                capture_allowed,
                view_allowed,
                grant_status,
                effective_from,
                reason_code)
            VALUES (
                '91400000-0000-0000-0000-000000001401',
                @service_identity_id,
                'CENTRAL_PMS',
                @site_id,
                NULL,
                true,
                true,
                'ACTIVE',
                TIMESTAMPTZ '2026-08-04 00:00:00+00',
                'I014_SYNTHETIC_GOVERNANCE_READ_PROOF')
            ON CONFLICT (statutory_evidence_principal_scope_grant_id) DO NOTHING;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("service_identity_id", ServiceIdentityId);
        command.Parameters.AddWithValue("site_group_id", SiteGroupId);
        command.Parameters.AddWithValue("site_id", SiteId);
        command.Parameters.AddWithValue("other_site_group_id", OtherSiteGroupId);
        command.Parameters.AddWithValue("other_site_id", OtherSiteId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadManifestAsync(string connectionString)
    {
        var tables = new[]
        {
            "discounts.statutory_evidence_sets",
            "discounts.statutory_evidence_items",
            "discounts.statutory_evidence_upload_authorizations",
            "discounts.statutory_evidence_events",
            "discounts.statutory_evidence_operations",
            "discounts.statutory_evidence_principal_scope_grants",
            "discounts.statutory_evidence_retention_policies",
            "discounts.statutory_discount_decision_commands",
            "discounts.statutory_discount_payable_basis_application_commands",
            "sites.sites",
            "sites.site_groups"
        };

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var manifest = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table};", connection);
            manifest[table] = (long)(await command.ExecuteScalarAsync() ?? 0L);
        }

        return manifest;
    }

    private static void EnsureDisposableI014Database(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!builder.Database.StartsWith("exitpass_i014_", StringComparison.OrdinalIgnoreCase) &&
            !builder.Database.StartsWith("exitpass_statutory_fixture_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "I-014 database-backed governance tests require an explicitly disposable database name.");
        }
    }

    private static void AddServiceHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, ServiceIdentityId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, ManagementPlatformStatutoryEvidenceGovernanceValues.Permission);
    }
}
