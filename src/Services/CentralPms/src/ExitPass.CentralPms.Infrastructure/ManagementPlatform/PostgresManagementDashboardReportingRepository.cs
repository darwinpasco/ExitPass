using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class PostgresManagementDashboardReportingRepository : IManagementDashboardReportingRepository
{
    private readonly string _connectionString;

    public PostgresManagementDashboardReportingRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
        ManagementDashboardActor actor,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.human_sessions session
                JOIN identity.users user_record ON user_record.user_id = session.user_id
                WHERE session.human_session_id = @human_session_id
                  AND session.user_id = @actor_user_id
                  AND session.session_audience = 'MANAGEMENT_PLATFORM'
                  AND session.session_status = 'ACTIVE'
                  AND session.idle_expires_at > now()
                  AND session.absolute_expires_at > now()
                  AND session.authorization_epoch_snapshot = user_record.authorization_epoch
                  AND session.credential_version_snapshot = user_record.credential_version
                  AND user_record.user_status = 'ACTIVE'
                  AND user_record.effective_from <= now()
                  AND (user_record.effective_to IS NULL OR user_record.effective_to > now())
            );
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("human_session_id", actor.HumanSessionId);
            command.Parameters.AddWithValue("actor_user_id", actor.UserId);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false)
                ? ManagementDashboardActorValidationStatus.Valid
                : ManagementDashboardActorValidationStatus.Invalid;
        }
        catch (NpgsqlException)
        {
            return ManagementDashboardActorValidationStatus.SourceUnavailable;
        }
    }

    public async Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
        ManagementDashboardActor actor,
        string scopeType,
        Guid scopeReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH effective_dashboard_roles AS (
                SELECT DISTINCT ur.user_role_id
                FROM identity.users u
                JOIN identity.user_roles ur ON ur.user_id = u.user_id
                JOIN identity.roles r ON r.role_id = ur.role_id
                JOIN identity.role_permissions rp ON rp.role_id = r.role_id
                JOIN identity.permissions p ON p.permission_id = rp.permission_id
                WHERE u.user_id = @actor_user_id
                  AND u.user_status = 'ACTIVE'
                  AND u.effective_from <= now()
                  AND (u.effective_to IS NULL OR u.effective_to > now())
                  AND ur.assignment_status = 'ACTIVE'
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
            ), effective_grants AS (
                SELECT g.scope_type::text AS scope_type, g.site_id, g.site_group_id
                FROM effective_dashboard_roles role_assignment
                JOIN identity.user_role_scope_grants g ON g.user_role_id = role_assignment.user_role_id
                WHERE g.grant_status = 'ACTIVE'
                  AND g.effective_from <= now()
                  AND (g.effective_to IS NULL OR g.effective_to > now())
                  AND g.revoked_at IS NULL
                  AND g.scope_type IN ('SITE', 'SITE_GROUP')
            ), requested_scope AS (
                SELECT
                    'SITE'::text AS scope_type,
                    s.site_id AS scope_reference,
                    s.site_name AS scope_name,
                    s.site_group_id,
                    s.updated_at AS scope_updated_at
                FROM sites.sites s
                WHERE @scope_type = 'SITE' AND s.site_id = @scope_reference

                UNION ALL

                SELECT
                    'SITE_GROUP'::text,
                    sg.site_group_id,
                    sg.site_group_name,
                    sg.site_group_id,
                    sg.updated_at
                FROM sites.site_groups sg
                WHERE @scope_type = 'SITE_GROUP' AND sg.site_group_id = @scope_reference
            ), authorized_scope AS (
                SELECT requested.*
                FROM requested_scope requested
                WHERE EXISTS (
                    SELECT 1
                    FROM effective_grants grant_scope
                    WHERE (requested.scope_type = 'SITE'
                           AND ((grant_scope.scope_type = 'SITE' AND grant_scope.site_id = requested.scope_reference)
                                OR (grant_scope.scope_type = 'SITE_GROUP' AND grant_scope.site_group_id = requested.site_group_id)))
                       OR (requested.scope_type = 'SITE_GROUP'
                           AND grant_scope.scope_type = 'SITE_GROUP'
                           AND grant_scope.site_group_id = requested.scope_reference)
                )
            )
            SELECT
                authorized.scope_type,
                authorized.scope_reference,
                authorized.scope_name,
                authorized.scope_updated_at,
                site.site_id,
                site.site_status::text AS site_status,
                site.payment_enabled,
                site.updated_at AS site_updated_at
            FROM authorized_scope authorized
            LEFT JOIN sites.sites site
              ON (authorized.scope_type = 'SITE' AND site.site_id = authorized.scope_reference)
              OR (authorized.scope_type = 'SITE_GROUP' AND site.site_group_id = authorized.scope_reference)
            ORDER BY site.site_name, site.site_id;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("actor_user_id", actor.UserId);
            command.Parameters.AddWithValue("permission_code", ManagementDashboardReportingValues.OverviewPermission);
            command.Parameters.AddWithValue("scope_type", scopeType);
            command.Parameters.AddWithValue("scope_reference", scopeReference);

            string? resolvedType = null;
            Guid resolvedReference = Guid.Empty;
            string? displayName = null;
            DateTimeOffset scopeUpdatedAt = default;
            var sites = new List<ManagementDashboardSiteSnapshot>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                resolvedType ??= reader.GetString(reader.GetOrdinal("scope_type"));
                resolvedReference = reader.GetGuid(reader.GetOrdinal("scope_reference"));
                displayName ??= reader.GetString(reader.GetOrdinal("scope_name"));
                scopeUpdatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("scope_updated_at"));
                if (!reader.IsDBNull(reader.GetOrdinal("site_id")))
                {
                    sites.Add(new ManagementDashboardSiteSnapshot(
                        reader.GetGuid(reader.GetOrdinal("site_id")),
                        reader.GetString(reader.GetOrdinal("site_status")),
                        reader.GetBoolean(reader.GetOrdinal("payment_enabled")),
                        reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("site_updated_at"))));
                }
            }

            if (resolvedType is null || displayName is null)
            {
                return new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.Denied, null);
            }

            var dataAsOf = sites.Select(site => site.UpdatedAt).Append(scopeUpdatedAt).Max();
            return new ManagementDashboardScopeReadResult(
                ManagementDashboardScopeReadStatus.Resolved,
                new ManagementDashboardScopeSnapshot(
                    resolvedType,
                    resolvedReference,
                    displayName,
                    dataAsOf,
                    sites));
        }
        catch (NpgsqlException)
        {
            return new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.SourceUnavailable, null);
        }
    }

    public async Task<ManagementDashboardProjectionReadResult> ReadProjectionHealthAsync(
        ManagementDashboardScopeSnapshot scope,
        CancellationToken cancellationToken)
    {
        if (scope.Sites.Count == 0)
        {
            return new ManagementDashboardProjectionReadResult(ManagementDashboardProjectionReadStatus.Resolved, []);
        }

        const string sql = """
            WITH projection_rollup AS (
                SELECT
                    site_id,
                    vendor_system_id,
                    parking_lot_index_code,
                    max(last_refreshed_at) AS latest_projection_at,
                    count(*) FILTER (WHERE projection_status = 'ACTIVE') AS active_projection_count
                FROM sessions.vendor_session_projections
                WHERE site_id = ANY(@site_ids)
                  AND projection_status <> 'INVALIDATED'
                GROUP BY site_id, vendor_system_id, parking_lot_index_code
            )
            SELECT
                target.enabled_flag,
                target.health_status::text,
                target.last_attempt_at,
                target.last_success_at,
                rollup.latest_projection_at,
                COALESCE(rollup.active_projection_count, 0) AS active_projection_count
            FROM sessions.vendor_session_projection_sync_targets target
            LEFT JOIN projection_rollup rollup
              ON rollup.site_id = target.site_id
             AND rollup.vendor_system_id = target.vendor_system_id
             AND rollup.parking_lot_index_code = target.parking_lot_index_code
            WHERE target.site_id = ANY(@site_ids)
            ORDER BY target.site_id, target.parking_lot_index_code;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value =
                scope.Sites.Select(site => site.SiteId).Distinct().ToArray();

            var targets = new List<ManagementDashboardProjectionTargetSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                targets.Add(new ManagementDashboardProjectionTargetSnapshot(
                    reader.GetBoolean(reader.GetOrdinal("enabled_flag")),
                    reader.GetString(reader.GetOrdinal("health_status")),
                    GetNullableTimestamp(reader, "last_attempt_at"),
                    GetNullableTimestamp(reader, "last_success_at"),
                    GetNullableTimestamp(reader, "latest_projection_at"),
                    reader.GetInt64(reader.GetOrdinal("active_projection_count"))));
            }

            return new ManagementDashboardProjectionReadResult(
                ManagementDashboardProjectionReadStatus.Resolved,
                targets);
        }
        catch (NpgsqlException)
        {
            return new ManagementDashboardProjectionReadResult(ManagementDashboardProjectionReadStatus.Unavailable, []);
        }
    }

    public async Task RecordAuditAsync(ManagementDashboardAuditRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit.audit_events (
                audit_event_id,
                event_type,
                event_category,
                event_result,
                event_reason_code,
                target_entity_type,
                target_entity_id,
                related_entity_type,
                related_entity_id,
                source_schema,
                source_service_name,
                source_channel,
                actor_user_id,
                summary,
                occurred_at,
                recorded_at,
                correlation_id,
                created_at)
            VALUES (
                gen_random_uuid(),
                @event_type,
                'SECURITY_RELEVANT',
                @event_result::audit.audit_event_result_enum,
                @reason_code,
                'ManagementDashboardReport',
                @scope_reference,
                'HumanSession',
                @human_session_id,
                'reporting',
                'central-pms',
                'MANAGEMENT_PLATFORM',
                @actor_user_id,
                @summary,
                @occurred_at,
                now(),
                @correlation_id,
                now());
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("event_type", record.EventType);
            command.Parameters.AddWithValue("event_result", record.Result);
            command.Parameters.AddWithValue("reason_code", record.ReasonCode);
            command.Parameters.Add("scope_reference", NpgsqlDbType.Uuid).Value = (object?)record.ScopeReference ?? DBNull.Value;
            command.Parameters.AddWithValue("human_session_id", record.HumanSessionId);
            command.Parameters.AddWithValue("actor_user_id", record.ActorUserId);
            command.Parameters.AddWithValue(
                "summary",
                $"Report {record.ReportId}; scope {record.ScopeType ?? "NONE"}; result {record.ResultClassification}; source {record.SourceClassification}.");
            command.Parameters.AddWithValue("occurred_at", record.OccurredAt);
            command.Parameters.AddWithValue("correlation_id", record.CorrelationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            throw new ManagementDashboardSourceUnavailableException("The dashboard audit source is unavailable.", ex);
        }
    }

    private static DateTimeOffset? GetNullableTimestamp(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
