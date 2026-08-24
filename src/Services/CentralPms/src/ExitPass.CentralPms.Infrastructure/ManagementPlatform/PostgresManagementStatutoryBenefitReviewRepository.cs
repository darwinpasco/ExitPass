using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class PostgresManagementStatutoryBenefitReviewRepository : IManagementStatutoryBenefitReviewRepository
{
    private readonly string _connectionString;

    public PostgresManagementStatutoryBenefitReviewRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<ManagementStatutoryBenefitAuthorizedSites?> ResolveAuthorizedSitesAsync(
        IdentityAdministrationActor actor,
        string permission,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH valid_actor AS (
                SELECT 1
                FROM identity.human_sessions hs
                JOIN identity.users u ON u.user_id = hs.user_id
                WHERE hs.human_session_id = @human_session_id
                  AND hs.user_id = @actor_user_id
                  AND hs.session_audience = 'MANAGEMENT_PLATFORM'
                  AND hs.session_status = 'ACTIVE'
                  AND hs.idle_expires_at > now()
                  AND hs.absolute_expires_at > now()
                  AND hs.authorization_epoch_snapshot = u.authorization_epoch
                  AND hs.credential_version_snapshot = u.credential_version
                  AND u.user_status = 'ACTIVE'
                  AND u.effective_from <= now()
                  AND (u.effective_to IS NULL OR u.effective_to > now())
            ), permitted_roles AS (
                SELECT ur.user_role_id
                FROM valid_actor
                JOIN identity.user_roles ur ON ur.user_id = @actor_user_id
                JOIN identity.roles r ON r.role_id = ur.role_id
                JOIN identity.role_permissions rp ON rp.role_id = r.role_id
                JOIN identity.permissions p ON p.permission_id = rp.permission_id
                WHERE ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now()
                  AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND ur.revoked_at IS NULL
                  AND r.role_status = 'ACTIVE'
                  AND r.effective_from <= now()
                  AND (r.effective_to IS NULL OR r.effective_to > now())
                  AND rp.binding_status = 'ACTIVE'
                  AND rp.effective_from <= now()
                  AND (rp.effective_to IS NULL OR rp.effective_to > now())
                  AND rp.revoked_at IS NULL
                  AND p.permission_status = 'ACTIVE'
                  AND p.permission_code = @permission_code
            ), grants AS (
                SELECT g.scope_type::text AS scope_type, g.site_id, g.site_group_id
                FROM permitted_roles pr
                JOIN identity.user_role_scope_grants g ON g.user_role_id = pr.user_role_id
                WHERE g.grant_status = 'ACTIVE'
                  AND g.effective_from <= now()
                  AND (g.effective_to IS NULL OR g.effective_to > now())
                  AND g.revoked_at IS NULL
            ), authorized_sites AS (
                SELECT DISTINCT s.site_id
                FROM sites.sites s
                JOIN grants g ON g.scope_type = 'GLOBAL'
                              OR (g.scope_type = 'SITE' AND g.site_id = s.site_id)
                              OR (g.scope_type = 'SITE_GROUP' AND g.site_group_id = s.site_group_id)
            )
            SELECT site_id, EXISTS (SELECT 1 FROM grants WHERE scope_type = 'GLOBAL') AS has_global
            FROM authorized_sites
            ORDER BY site_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("human_session_id", actor.HumanSessionId);
        command.Parameters.AddWithValue("actor_user_id", actor.UserId);
        command.Parameters.AddWithValue("permission_code", permission);

        var sites = new HashSet<Guid>();
        var global = false;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sites.Add(reader.GetGuid(0));
            global |= reader.GetBoolean(1);
        }

        return sites.Count == 0 ? null : new ManagementStatutoryBenefitAuthorizedSites(sites, global);
    }

    public async Task<ManagementStatutoryBenefitReviewQueue> ListAsync(
        ManagementStatutoryBenefitReviewQuery query,
        IReadOnlySet<Guid> authorizedSites,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                r.request_reference,
                r.statutory_discount_decision_command_id,
                r.parking_session_id,
                r.ticket_reference,
                r.site_id,
                s.site_code,
                s.site_name,
                r.source_channel,
                r.entitlement_type,
                r.review_status,
                d.evidence_required,
                d.evidence_recorded,
                r.submitted_at,
                reviewer.display_name AS reviewer_display_name,
                r.reviewed_at,
                COUNT(*) OVER() AS total_count
            FROM operator_console.statutory_discount_service_channel_reviews r
            JOIN discounts.statutory_discount_decision_commands d
              ON d.statutory_discount_decision_command_id = r.statutory_discount_decision_command_id
            JOIN sites.sites s ON s.site_id = r.site_id
            LEFT JOIN identity.users reviewer ON reviewer.user_id = r.reviewer_user_id
            WHERE r.source_channel IN ('WEBPAY', 'ASSISTED_PAYMENT_TERMINAL')
              AND r.site_id = ANY(@authorized_sites)
              AND (@site_reference IS NULL OR r.site_id = @site_reference)
              AND (@status = 'ALL' OR r.review_status = @status)
              AND (@source_channel IS NULL OR r.source_channel = @source_channel)
              AND (@benefit_type IS NULL OR r.entitlement_type = @benefit_type)
              AND (@submitted_from IS NULL OR r.submitted_at >= @submitted_from)
              AND (@submitted_to IS NULL OR r.submitted_at < @submitted_to)
              AND (@search IS NULL
                   OR lower(r.request_reference::text) = @search
                   OR lower(r.parking_session_id::text) = @search
                   OR lower(COALESCE(r.ticket_reference, '')) = @search)
            ORDER BY
                CASE WHEN r.review_status = 'PENDING_REVIEW' THEN 0 ELSE 1 END,
                r.submitted_at DESC,
                r.statutory_discount_decision_command_id
            LIMIT @limit OFFSET @offset;
            """;

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var items = new List<ManagementStatutoryBenefitReviewQueueItem>();
        long total = 0;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("authorized_sites", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = authorizedSites.ToArray();
        AddNullable(command, "site_reference", NpgsqlDbType.Uuid, query.SiteReference);
        command.Parameters.AddWithValue("status", query.Status);
        AddNullable(command, "source_channel", NpgsqlDbType.Varchar, query.SourceChannel);
        AddNullable(command, "benefit_type", NpgsqlDbType.Varchar, query.BenefitType);
        AddNullable(command, "submitted_from", NpgsqlDbType.TimestampTz, query.SubmittedFrom);
        AddNullable(command, "submitted_to", NpgsqlDbType.TimestampTz, query.SubmittedTo);
        AddNullable(command, "search", NpgsqlDbType.Varchar, query.Search?.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(reader.GetOrdinal("total_count"));
            items.Add(new ManagementStatutoryBenefitReviewQueueItem(
                reader.GetGuid(reader.GetOrdinal("request_reference")),
                reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
                reader.GetGuid(reader.GetOrdinal("parking_session_id")),
                GetNullableString(reader, "ticket_reference"),
                reader.GetGuid(reader.GetOrdinal("site_id")),
                reader.GetString(reader.GetOrdinal("site_code")),
                reader.GetString(reader.GetOrdinal("site_name")),
                reader.GetString(reader.GetOrdinal("source_channel")),
                reader.GetString(reader.GetOrdinal("entitlement_type")),
                reader.GetString(reader.GetOrdinal("review_status")),
                reader.GetBoolean(reader.GetOrdinal("evidence_required")),
                reader.GetBoolean(reader.GetOrdinal("evidence_recorded")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("submitted_at")),
                GetNullableString(reader, "reviewer_display_name"),
                GetNullableDateTimeOffset(reader, "reviewed_at")));
        }

        return new ManagementStatutoryBenefitReviewQueue(
            ManagementStatutoryBenefitReviewValues.ContractVersion,
            items,
            page,
            pageSize,
            total,
            offset + items.Count < total,
            query.CorrelationId);
    }

    public async Task<ManagementStatutoryBenefitReviewMetadata?> GetMetadataAsync(
        Guid decisionCommandReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.site_id, s.site_code, s.site_name, reviewer.display_name,
                   r.xmin::text::bigint AS version
            FROM operator_console.statutory_discount_service_channel_reviews r
            JOIN sites.sites s ON s.site_id = r.site_id
            LEFT JOIN identity.users reviewer ON reviewer.user_id = r.reviewer_user_id
            WHERE r.statutory_discount_decision_command_id = @reference;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("reference", decisionCommandReference);
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ManagementStatutoryBenefitReviewMetadata(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt64(4))
            : null;
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
