using System.Data;
using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class ManagementPlatformStatutoryDiscountPolicyCoverageRepository
    : IManagementPlatformStatutoryDiscountPolicyCoverageRepository
{
    private readonly string _connectionString;

    public ManagementPlatformStatutoryDiscountPolicyCoverageRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveScopeAsync(
        Guid? actorUserId,
        string scopeType,
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        if (actorUserId is null)
        {
            return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Denied,
                ScopeDisplayName: null,
                Sites: []);
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sites = string.Equals(scopeType, ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite, StringComparison.OrdinalIgnoreCase)
                ? await ReadSiteScopeAsync(connection, scopeId, cancellationToken)
                : await ReadSiteGroupScopeAsync(connection, scopeId, cancellationToken);

            if (sites is null)
            {
                return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                    ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.NotFound,
                    ScopeDisplayName: null,
                    Sites: []);
            }

            if (sites.Count == 0)
            {
                return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                    ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Empty,
                    ScopeDisplayName: null,
                    Sites: []);
            }

            if (!await IsActorAllowedForScopeAsync(connection, actorUserId.Value, scopeType, scopeId, sites, cancellationToken))
            {
                return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                    ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Denied,
                    ScopeDisplayName: null,
                    Sites: []);
            }

            return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Resolved,
                string.Equals(scopeType, ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite, StringComparison.OrdinalIgnoreCase)
                    ? sites[0].SiteName
                    : sites[0].SiteGroupName,
                sites);
        }
        catch (PostgresException)
        {
            return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.SourceUnavailable,
                ScopeDisplayName: null,
                Sites: []);
        }
        catch (NpgsqlException)
        {
            return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.SourceUnavailable,
                ScopeDisplayName: null,
                Sites: []);
        }
    }

    public async Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>> ReadPolicyCandidatesAsync(
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
        IReadOnlyList<string> entitlementTypes,
        bool includeInactive,
        DateOnly evaluationDate,
        CancellationToken cancellationToken)
    {
        if (sites.Count == 0 || entitlementTypes.Count == 0)
        {
            return [];
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var candidates = new List<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>();
        var capabilities = await ReadPolicyRegistryCapabilitiesAsync(connection, cancellationToken);
        if (capabilities.HasDedicatedRegistry)
        {
            candidates.AddRange(await ReadDedicatedCandidatesAsync(connection, sites, entitlementTypes, includeInactive, evaluationDate, cancellationToken));
        }

        if (!capabilities.HasDedicatedRegistry && capabilities.HasCompatibilityTable)
        {
            candidates.AddRange(await ReadCompatibilityCandidatesAsync(connection, sites, entitlementTypes, includeInactive, evaluationDate, cancellationToken));
        }

        return candidates;
    }

    public async Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveServiceSiteScopeAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sites = await ReadSiteScopeAsync(connection, siteId, cancellationToken);
            if (sites is null)
            {
                return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                    ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.NotFound,
                    ScopeDisplayName: null,
                    Sites: []);
            }

            return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Resolved,
                sites[0].SiteName,
                sites);
        }
        catch (PostgresException)
        {
            return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.SourceUnavailable,
                ScopeDisplayName: null,
                Sites: []);
        }
        catch (NpgsqlException)
        {
            return new ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.SourceUnavailable,
                ScopeDisplayName: null,
                Sites: []);
        }
    }

    private static async Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite>?> ReadSiteScopeAsync(
        NpgsqlConnection connection,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.site_id,
                s.site_group_id,
                COALESCE(NULLIF(s.site_name, ''), NULLIF(s.site_code, ''), s.site_id::text) AS site_name,
                COALESCE(NULLIF(sg.site_group_name, ''), NULLIF(sg.site_group_code, ''), s.site_group_id::text) AS site_group_name,
                NULLIF(s.lgu_code, '') AS lgu_code
            FROM sites.sites AS s
            LEFT JOIN sites.site_groups AS sg ON sg.site_group_id = s.site_group_id
            WHERE s.site_id = @site_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return [ReadSite(reader)];
    }

    private static async Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite>?> ReadSiteGroupScopeAsync(
        NpgsqlConnection connection,
        Guid siteGroupId,
        CancellationToken cancellationToken)
    {
        const string groupSql = """
            SELECT COALESCE(NULLIF(site_group_name, ''), NULLIF(site_group_code, ''), site_group_id::text)
            FROM sites.site_groups
            WHERE site_group_id = @site_group_id;
            """;

        await using (var groupCommand = new NpgsqlCommand(groupSql, connection))
        {
            groupCommand.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
            if (await groupCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }
        }

        const string sql = """
            SELECT
                s.site_id,
                s.site_group_id,
                COALESCE(NULLIF(s.site_name, ''), NULLIF(s.site_code, ''), s.site_id::text) AS site_name,
                COALESCE(NULLIF(sg.site_group_name, ''), NULLIF(sg.site_group_code, ''), s.site_group_id::text) AS site_group_name,
                NULLIF(s.lgu_code, '') AS lgu_code
            FROM sites.sites AS s
            LEFT JOIN sites.site_groups AS sg ON sg.site_group_id = s.site_group_id
            WHERE s.site_group_id = @site_group_id
            ORDER BY site_name, s.site_id;
            """;

        var sites = new List<ManagementPlatformStatutoryDiscountPolicyCoverageSite>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sites.Add(ReadSite(reader));
        }

        return sites;
    }

    private static async Task<bool> IsActorAllowedForScopeAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        string scopeType,
        Guid scopeId,
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM operator_console.operator_shifts AS shift_scope
                WHERE shift_scope.operator_user_id = @actor_user_id
                  AND shift_scope.operational_status = 'ACTIVE'
                  AND (
                        (@scope_type = 'SITE' AND (
                            shift_scope.site_id = @scope_id
                            OR (shift_scope.site_id IS NULL AND shift_scope.site_group_id = @site_group_id)
                        ))
                        OR (@scope_type = 'SITE_GROUP' AND shift_scope.site_group_id = @scope_id AND shift_scope.site_id IS NULL)
                  )
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = actorUserId;
        command.Parameters.Add("scope_type", NpgsqlDbType.Text).Value = scopeType;
        command.Parameters.Add("scope_id", NpgsqlDbType.Uuid).Value = scopeId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = sites[0].SiteGroupId;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<PolicyRegistryCapabilities> ReadPolicyRegistryCapabilitiesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'discounts'
                      AND table_name = 'statutory_discount_policy_registry'
                ),
                EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'discounts'
                      AND table_name = 'discount_policy_references'
                );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new PolicyRegistryCapabilities(false, false);
        }

        return new PolicyRegistryCapabilities(reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static async Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>> ReadDedicatedCandidatesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
        IReadOnlyList<string> entitlementTypes,
        bool includeInactive,
        DateOnly evaluationDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                matched.site_id AS resolved_site_id,
                p.statutory_discount_policy_registry_id,
                p.policy_code,
                p.policy_name,
                p.entitlement_type::text,
                p.policy_status::text,
                p.verification_status::text,
                p.policy_level::text,
                p.policy_resolution_basis::text,
                COALESCE(p.legal_basis_reference, p.ordinance_reference, p.national_law_reference) AS legal_basis_reference,
                p.ordinance_reference,
                p.national_law_reference,
                p.effective_from,
                p.effective_to,
                p.source_reference,
                COALESCE(p.updated_at, p.created_at) AS updated_at
            FROM discounts.statutory_discount_policy_registry AS p
            JOIN unnest(@site_ids::uuid[], @site_group_ids::uuid[], @lgu_codes::text[]) AS matched(site_id, site_group_id, lgu_code)
              ON p.site_id = matched.site_id
              OR p.site_group_id = matched.site_group_id
              OR p.jurisdiction_code = matched.lgu_code
            WHERE p.entitlement_type::text = ANY(@entitlement_types)
              AND (@include_inactive OR p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum)
              AND (
                    p.effective_from <= @evaluation_date
                    OR p.effective_from > @evaluation_date
                    OR p.effective_to < @evaluation_date
                  )
            ORDER BY matched.site_id, p.entitlement_type::text, p.effective_from DESC, p.policy_code;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, sites, entitlementTypes, includeInactive, evaluationDate);
        return await ReadCandidatesAsync(command, "STATUTORY_POLICY_REGISTRY", cancellationToken);
    }

    private static async Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>> ReadCompatibilityCandidatesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
        IReadOnlyList<string> entitlementTypes,
        bool includeInactive,
        DateOnly evaluationDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                matched.site_id AS resolved_site_id,
                p.discount_policy_reference_id,
                p.policy_code,
                p.policy_name,
                p.entitlement_type::text,
                p.policy_status::text,
                p.policy_status::text AS verification_status,
                p.policy_level::text,
                CASE
                    WHEN p.policy_level = 'SITE_POLICY'::discounts.discount_policy_level_enum THEN 'SITE_POLICY_OPERATIONAL_ONLY'
                    ELSE 'LOCAL_ORDINANCE_APPLIED'
                END AS policy_resolution_basis,
                COALESCE(p.local_ordinance_reference, p.national_law_reference) AS legal_basis_reference,
                p.local_ordinance_reference AS ordinance_reference,
                p.national_law_reference,
                p.effective_from,
                p.effective_to,
                p.policy_version AS source_reference,
                COALESCE(p.updated_at, p.created_at) AS updated_at
            FROM discounts.discount_policy_references AS p
            JOIN unnest(@site_ids::uuid[], @site_group_ids::uuid[], @lgu_codes::text[]) AS matched(site_id, site_group_id, lgu_code)
              ON p.site_id = matched.site_id
              OR p.site_group_id = matched.site_group_id
              OR p.lgu_code = matched.lgu_code
            WHERE p.entitlement_type::text = ANY(@entitlement_types)
              AND (@include_inactive OR p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum)
            ORDER BY matched.site_id, p.entitlement_type::text, p.effective_from DESC, p.policy_code;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, sites, entitlementTypes, includeInactive, evaluationDate);
        return await ReadCandidatesAsync(command, "DISCOUNT_POLICY_REFERENCES_COMPATIBILITY", cancellationToken);
    }

    private static async Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>> ReadCandidatesAsync(
        NpgsqlCommand command,
        string sourceClassification,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new ManagementPlatformStatutoryDiscountPolicyCoverageCandidate(
                reader.GetGuid(0),
                reader.GetString(4),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                GetNullableString(reader, 2),
                GetNullableString(reader, 3),
                GetNullableString(reader, 5),
                GetNullableString(reader, 6),
                GetNullableString(reader, 7),
                GetNullableString(reader, 8),
                GetNullableString(reader, 9),
                GetNullableString(reader, 10),
                GetNullableString(reader, 11),
                GetNullableDateOnly(reader, 12),
                GetNullableDateOnly(reader, 13),
                GetNullableString(reader, 14),
                GetNullableDateTimeOffset(reader, 15),
                sourceClassification));
        }

        return candidates;
    }

    private static void AddScopeParameters(
        NpgsqlCommand command,
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
        IReadOnlyList<string> entitlementTypes,
        bool includeInactive,
        DateOnly evaluationDate)
    {
        command.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = sites.Select(site => site.SiteId).ToArray();
        command.Parameters.Add("site_group_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = sites.Select(site => site.SiteGroupId).ToArray();
        command.Parameters.Add("lgu_codes", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = sites.Select(site => site.LguCode ?? string.Empty).ToArray();
        command.Parameters.Add("entitlement_types", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = entitlementTypes.ToArray();
        command.Parameters.Add("include_inactive", NpgsqlDbType.Boolean).Value = includeInactive;
        command.Parameters.Add("evaluation_date", NpgsqlDbType.Date).Value = evaluationDate;
    }

    private static ManagementPlatformStatutoryDiscountPolicyCoverageSite ReadSite(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            GetNullableString(reader, 2),
            GetNullableString(reader, 3),
            GetNullableString(reader, 4));

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateOnly? GetNullableDateOnly(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateTimeOffset dateTimeOffset => DateOnly.FromDateTime(dateTimeOffset.UtcDateTime),
            _ => null
        };
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => null
        };
    }

    private sealed record PolicyRegistryCapabilities(bool HasDedicatedRegistry, bool HasCompatibilityTable);
}
