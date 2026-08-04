using System.Data;
using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class ManagementPlatformStatutoryEvidenceGovernanceRepository
    : IManagementPlatformStatutoryEvidenceGovernanceRepository
{
    private readonly string _connectionString;

    public ManagementPlatformStatutoryEvidenceGovernanceRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult> ResolveScopeAsync(
        ManagementPlatformStatutoryEvidenceGovernanceActor actor,
        string? scopeType,
        Guid? scopeReference,
        CancellationToken cancellationToken)
    {
        if (actor.UserId is null && actor.ServiceIdentityId is null)
        {
            return new ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult(
                ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Denied,
                Sites: []);
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var sites = await ReadAuthorizedSitesAsync(connection, actor, scopeType, scopeReference, cancellationToken);

            if (sites.Count == 0)
            {
                return new ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult(
                    scopeType is null
                        ? ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Empty
                        : ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Denied,
                    Sites: []);
            }

            return new ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult(
                ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Resolved,
                sites);
        }
        catch (PostgresException)
        {
            return new ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult(
                ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.SourceUnavailable,
                Sites: []);
        }
        catch (NpgsqlException)
        {
            return new ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult(
                ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.SourceUnavailable,
                Sites: []);
        }
    }

    public async Task<ManagementPlatformStatutoryEvidenceGovernanceConfiguration> ReadConfigurationAsync(
        IReadOnlyList<ManagementPlatformStatutoryEvidenceGovernanceScopeSite> sites,
        CancellationToken cancellationToken)
    {
        if (sites.Count == 0)
        {
            return new ManagementPlatformStatutoryEvidenceGovernanceConfiguration([], new HashSet<Guid>(), false, false, null);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var capabilities = await ReadCapabilitiesAsync(connection, cancellationToken);
        if (!capabilities.HasRetentionPolicies || !capabilities.HasScopeGrants)
        {
            return new ManagementPlatformStatutoryEvidenceGovernanceConfiguration(
                RetentionPolicies: [],
                CaptureEnabledSiteIds: new HashSet<Guid>(),
                capabilities.HasEvidenceSets && capabilities.HasEvidenceItems,
                capabilities.HasUploadAuthorizations,
                LastConfigurationUpdatedAt: null);
        }

        var siteIds = sites.Select(site => site.SiteId).Distinct().ToArray();
        var retentionPolicies = await ReadRetentionPoliciesAsync(connection, cancellationToken);
        var captureEnabledSiteIds = await ReadCaptureEnabledSitesAsync(connection, siteIds, cancellationToken);
        var lastUpdated = retentionPolicies
            .Select(policy => policy.UpdatedAt)
            .Concat(captureEnabledSiteIds.LastUpdatedAt is null ? [] : [captureEnabledSiteIds.LastUpdatedAt.Value])
            .DefaultIfEmpty()
            .Max();

        return new ManagementPlatformStatutoryEvidenceGovernanceConfiguration(
            retentionPolicies.Select(policy => policy.Profile).ToArray(),
            captureEnabledSiteIds.SiteIds,
            capabilities.HasEvidenceSets && capabilities.HasEvidenceItems,
            capabilities.HasUploadAuthorizations,
            lastUpdated == default ? null : lastUpdated);
    }

    private static async Task<IReadOnlyList<ManagementPlatformStatutoryEvidenceGovernanceScopeSite>> ReadAuthorizedSitesAsync(
        NpgsqlConnection connection,
        ManagementPlatformStatutoryEvidenceGovernanceActor actor,
        string? scopeType,
        Guid? scopeReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH authorized_site_ids AS (
                SELECT shift_site.site_id
                FROM operator_console.operator_shifts AS shift_scope
                JOIN sites.sites AS shift_site ON shift_site.site_id = shift_scope.site_id
                WHERE @actor_user_id IS NOT NULL
                  AND shift_scope.operator_user_id = @actor_user_id
                  AND shift_scope.operational_status = 'ACTIVE'
                  AND shift_scope.site_id IS NOT NULL

                UNION

                SELECT group_site.site_id
                FROM operator_console.operator_shifts AS shift_scope
                JOIN sites.sites AS group_site ON group_site.site_group_id = shift_scope.site_group_id
                WHERE @actor_user_id IS NOT NULL
                  AND shift_scope.operator_user_id = @actor_user_id
                  AND shift_scope.operational_status = 'ACTIVE'
                  AND shift_scope.site_id IS NULL
                  AND shift_scope.site_group_id IS NOT NULL

                UNION

                SELECT grant_site.site_id
                FROM discounts.statutory_evidence_principal_scope_grants AS grant_scope
                JOIN sites.sites AS grant_site
                  ON grant_site.site_id = grant_scope.site_id
                  OR (grant_scope.site_id IS NULL AND grant_site.site_group_id = grant_scope.site_group_id)
                WHERE @actor_service_identity_id IS NOT NULL
                  AND grant_scope.actor_service_identity_id = @actor_service_identity_id
                  AND grant_scope.grant_status = 'ACTIVE'
                  AND grant_scope.effective_from <= now()
                  AND (grant_scope.effective_to IS NULL OR grant_scope.effective_to > now())
            )
            SELECT DISTINCT
                site.site_id,
                COALESCE(NULLIF(site.site_name, ''), NULLIF(site.site_code, ''), site.site_id::text) AS site_name,
                site.site_group_id,
                COALESCE(NULLIF(site_group.site_group_name, ''), NULLIF(site_group.site_group_code, ''), site.site_group_id::text) AS site_group_name
            FROM authorized_site_ids AS authorized
            JOIN sites.sites AS site ON site.site_id = authorized.site_id
            LEFT JOIN sites.site_groups AS site_group ON site_group.site_group_id = site.site_group_id
            WHERE (
                    @scope_type IS NULL
                    OR (@scope_type = 'SITE' AND site.site_id = @scope_reference)
                    OR (@scope_type = 'SITE_GROUP' AND site.site_group_id = @scope_reference)
                  )
            ORDER BY site_name, site.site_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = (object?)actor.UserId ?? DBNull.Value;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = (object?)actor.ServiceIdentityId ?? DBNull.Value;
        command.Parameters.Add("scope_type", NpgsqlDbType.Text).Value = (object?)scopeType ?? DBNull.Value;
        command.Parameters.Add("scope_reference", NpgsqlDbType.Uuid).Value = (object?)scopeReference ?? DBNull.Value;

        var sites = new List<ManagementPlatformStatutoryEvidenceGovernanceScopeSite>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sites.Add(new ManagementPlatformStatutoryEvidenceGovernanceScopeSite(
                reader.GetGuid(reader.GetOrdinal("site_id")),
                reader.GetString(reader.GetOrdinal("site_name")),
                reader.GetGuid(reader.GetOrdinal("site_group_id")),
                reader.GetString(reader.GetOrdinal("site_group_name"))));
        }

        return sites;
    }

    private static async Task<EvidenceGovernanceCapabilities> ReadCapabilitiesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'discounts' AND table_name = 'statutory_evidence_retention_policies') AS has_retention_policies,
                EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'discounts' AND table_name = 'statutory_evidence_principal_scope_grants') AS has_scope_grants,
                EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'discounts' AND table_name = 'statutory_evidence_sets') AS has_evidence_sets,
                EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'discounts' AND table_name = 'statutory_evidence_items') AS has_evidence_items,
                EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'discounts' AND table_name = 'statutory_evidence_upload_authorizations') AS has_upload_authorizations;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new EvidenceGovernanceCapabilities(false, false, false, false, false);
        }

        return new EvidenceGovernanceCapabilities(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4));
    }

    private static async Task<IReadOnlyList<RetentionPolicyRow>> ReadRetentionPoliciesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                retention_class_code,
                retention_policy_version,
                policy_status,
                updated_at
            FROM discounts.statutory_evidence_retention_policies
            WHERE effective_from <= now()
              AND (effective_to IS NULL OR effective_to > now())
            ORDER BY retention_class_code, retention_policy_version;
            """;

        var policies = new List<RetentionPolicyRow>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var retentionClassCode = reader.GetString(0);
            var version = reader.GetString(1);
            var status = reader.GetString(2);
            policies.Add(new RetentionPolicyRow(
                new ManagementPlatformStatutoryEvidenceDocumentProfile(
                    ProfileCode: retentionClassCode,
                    ProfileVersion: version,
                    RetentionClassCode: retentionClassCode,
                    RetentionPolicyVersion: version,
                    RetentionPolicyStatus: status,
                    RetentionPolicyApproved: string.Equals(status, "APPROVED_ENABLED", StringComparison.OrdinalIgnoreCase)),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return policies;
    }

    private static async Task<CaptureEnabledSites> ReadCaptureEnabledSitesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<Guid> siteIds,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT grant_site.site_id, max(grant_scope.updated_at) OVER () AS last_updated
            FROM discounts.statutory_evidence_principal_scope_grants AS grant_scope
            JOIN sites.sites AS grant_site
              ON grant_site.site_id = grant_scope.site_id
              OR (grant_scope.site_id IS NULL AND grant_site.site_group_id = grant_scope.site_group_id)
            WHERE grant_scope.grant_status = 'ACTIVE'
              AND grant_scope.capture_allowed
              AND grant_scope.effective_from <= now()
              AND (grant_scope.effective_to IS NULL OR grant_scope.effective_to > now())
              AND grant_site.site_id = ANY(@site_ids);
            """;

        var enabled = new HashSet<Guid>();
        DateTimeOffset? lastUpdated = null;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = siteIds.ToArray();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            enabled.Add(reader.GetGuid(0));
            if (!reader.IsDBNull(1))
            {
                lastUpdated = reader.GetFieldValue<DateTimeOffset>(1);
            }
        }

        return new CaptureEnabledSites(enabled, lastUpdated);
    }

    private sealed record EvidenceGovernanceCapabilities(
        bool HasRetentionPolicies,
        bool HasScopeGrants,
        bool HasEvidenceSets,
        bool HasEvidenceItems,
        bool HasUploadAuthorizations);

    private sealed record RetentionPolicyRow(
        ManagementPlatformStatutoryEvidenceDocumentProfile Profile,
        DateTimeOffset UpdatedAt);

    private sealed record CaptureEnabledSites(
        IReadOnlySet<Guid> SiteIds,
        DateTimeOffset? LastUpdatedAt);
}
