using System.Data;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

public sealed class PostgresOperatorConsoleOperatingContextRepository : IOperatorConsoleOperatingContextRepository
{
    private readonly string _connectionString;

    public PostgresOperatorConsoleOperatingContextRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<OperatorConsoleDeviceBindingCandidate?> FindDeviceByProofAsync(
        string proofThumbprint,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH matching_devices AS (
                SELECT d.*,
                       count(*) OVER ()::integer AS matching_proof_count
                FROM operator_console.operator_device_bindings d
                WHERE d.browser_key_thumbprint = @proof_thumbprint
            ), assignments AS (
                SELECT a.operator_device_binding_id,
                       count(*)::integer AS active_assignment_count,
                       (array_agg(a.site_id ORDER BY a.effective_from DESC))[1] AS site_id,
                       (array_agg(a.site_group_id ORDER BY a.effective_from DESC))[1] AS site_group_id
                FROM operator_console.operator_device_assignment_history a
                WHERE a.effective_from <= @now
                  AND (a.effective_to IS NULL OR a.effective_to > @now)
                  AND a.ended_at IS NULL
                  AND a.assignment_status_code = 'ACTIVE'
                GROUP BY a.operator_device_binding_id
            )
            SELECT d.operator_device_binding_id,
                   d.device_status::text,
                   d.trust_level::text,
                   d.site_id,
                   d.site_group_id,
                   (site_scope.site_id IS NOT NULL),
                   CASE
                       WHEN d.trust_level = 'BROWSER_KEY_ONLY' AND d.last_seen_at IS NOT NULL
                           THEN d.last_seen_at + interval '12 hours'
                       WHEN d.trust_level = 'BROWSER_KEY_AND_MTLS'
                           THEN LEAST(d.last_seen_at + interval '12 hours', d.mtls_certificate_expires_at)
                       WHEN d.trust_level = 'MTLS_ONLY' THEN d.mtls_certificate_expires_at
                       ELSE NULL
                   END,
                   d.matching_proof_count,
                   COALESCE(a.active_assignment_count, 0),
                   a.site_id,
                   a.site_group_id
            FROM matching_devices d
            LEFT JOIN sites.sites site_scope
              ON site_scope.site_id = d.site_id
             AND site_scope.site_group_id = d.site_group_id
            LEFT JOIN assignments a ON a.operator_device_binding_id = d.operator_device_binding_id
            ORDER BY CASE WHEN d.device_status = 'ACTIVE' THEN 0 ELSE 1 END, d.updated_at DESC
            LIMIT 1;
            """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("proof_thumbprint", NpgsqlDbType.Char).Value = proofThumbprint;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorConsoleDeviceBindingCandidate(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetBoolean(5),
            GetNullableDateTime(reader, 6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            GetNullableGuid(reader, 9),
            GetNullableGuid(reader, 10));
    }

    public async Task<OperatorConsoleShiftResolution> ResolveShiftAsync(
        Guid userId,
        Guid siteId,
        Guid siteGroupId,
        IReadOnlyList<Guid> authorizedSiteIds,
        IReadOnlyList<Guid> authorizedSiteGroupIds,
        bool hasGlobalScope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT operator_shift_id, site_id, site_group_id, operational_status::text,
                   active_from, active_to, revoked_at,
                   EXISTS (
                       SELECT 1 FROM sites.sites site_scope
                       WHERE site_scope.site_id = operator_shifts.site_id
                         AND site_scope.site_group_id = operator_shifts.site_group_id
                   ) AS has_canonical_site_group_relationship
            FROM operator_console.operator_shifts
            WHERE operator_user_id = @user_id;
            """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var compatible = new List<Guid>();
        var closedOrExpired = false;
        var outsideDevice = false;
        var outsideScope = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            var shiftId = reader.GetGuid(0);
            var shiftSiteId = reader.GetGuid(1);
            var shiftSiteGroupId = reader.GetGuid(2);
            var status = reader.GetString(3);
            var activeFrom = GetNullableDateTime(reader, 4);
            var activeTo = GetNullableDateTime(reader, 5);
            var revokedAt = GetNullableDateTime(reader, 6);
            var active = status == "ACTIVE" && !revokedAt.HasValue && activeFrom.HasValue &&
                activeFrom <= now && (!activeTo.HasValue || activeTo > now);
            var matchesDevice = shiftSiteId == siteId && shiftSiteGroupId == siteGroupId && reader.GetBoolean(7);
            var inScope = hasGlobalScope || authorizedSiteIds.Contains(shiftSiteId) || authorizedSiteGroupIds.Contains(shiftSiteGroupId);

            if (active && matchesDevice && inScope)
            {
                compatible.Add(shiftId);
            }
            else if (active && !inScope)
            {
                outsideScope = true;
            }
            else if (active && !matchesDevice)
            {
                outsideDevice = true;
            }
            else if (matchesDevice)
            {
                closedOrExpired = true;
            }
        }

        return new OperatorConsoleShiftResolution(
            compatible.Count,
            compatible.Count == 1 ? compatible[0] : null,
            closedOrExpired,
            outsideDevice,
            outsideScope);
    }

    public async Task<bool> RotateDeviceProofAsync(
        Guid operatorDeviceBindingId,
        string expectedThumbprint,
        string replacementThumbprint,
        DateTimeOffset now,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE operator_console.operator_device_bindings
            SET browser_key_thumbprint = @replacement_thumbprint,
                last_seen_at = @now,
                correlation_id = @correlation_id,
                updated_at = @now,
                row_version = row_version + 1
            WHERE operator_device_binding_id = @device_id
              AND browser_key_thumbprint = @expected_thumbprint
              AND device_status = 'ACTIVE'
              AND trust_level IN ('BROWSER_KEY_ONLY', 'BROWSER_KEY_AND_MTLS')
              AND revoked_at IS NULL
              AND lost_reported_at IS NULL;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("device_id", NpgsqlDbType.Uuid).Value = operatorDeviceBindingId;
        command.Parameters.Add("expected_thumbprint", NpgsqlDbType.Char).Value = expectedThumbprint;
        command.Parameters.Add("replacement_thumbprint", NpgsqlDbType.Char).Value = replacementThumbprint;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<OperatorConsoleSessionBindingSnapshot?> ReadSessionBindingSnapshotAsync(
        Guid humanSessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT hs.authorization_epoch_snapshot,
                   hs.credential_version_snapshot,
                   hs.session_status::text,
                   hs.idle_expires_at,
                   hs.absolute_expires_at
            FROM identity.human_sessions hs
            WHERE hs.human_session_id = @human_session_id
              AND hs.user_id = @user_id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("human_session_id", NpgsqlDbType.Uuid).Value = humanSessionId;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new OperatorConsoleSessionBindingSnapshot(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4))
            : null;
    }

    public async Task<OperatorConsoleOperatingContext> BindSessionAsync(
        Guid humanSessionId,
        Guid userId,
        Guid operatorDeviceBindingId,
        Guid operatorShiftId,
        Guid siteId,
        Guid siteGroupId,
        long authorizationEpoch,
        long credentialVersion,
        DateTimeOffset now,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operator_console.operator_session_contexts (
                human_session_id, operator_user_id, operator_device_binding_id, operator_shift_id,
                site_id, site_group_id, authorization_epoch_snapshot, credential_version_snapshot,
                context_status, bound_at, last_validated_at, correlation_id, created_at, updated_at)
            VALUES (
                @human_session_id, @user_id, @device_id, @shift_id,
                @site_id, @site_group_id, @authorization_epoch, @credential_version,
                'ACTIVE', @now, @now, @correlation_id, @now, @now)
            ON CONFLICT (human_session_id) DO UPDATE SET
                operator_user_id = EXCLUDED.operator_user_id,
                operator_device_binding_id = EXCLUDED.operator_device_binding_id,
                operator_shift_id = EXCLUDED.operator_shift_id,
                site_id = EXCLUDED.site_id,
                site_group_id = EXCLUDED.site_group_id,
                authorization_epoch_snapshot = EXCLUDED.authorization_epoch_snapshot,
                credential_version_snapshot = EXCLUDED.credential_version_snapshot,
                context_status = 'ACTIVE',
                bound_at = EXCLUDED.bound_at,
                last_validated_at = EXCLUDED.last_validated_at,
                invalidated_at = NULL,
                invalidation_reason_code = NULL,
                correlation_id = EXCLUDED.correlation_id,
                updated_at = EXCLUDED.updated_at,
                row_version = operator_console.operator_session_contexts.row_version + 1
            RETURNING bound_at;
            """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddBindingParameters(command, humanSessionId, userId, operatorDeviceBindingId, operatorShiftId, siteId, siteGroupId, authorizationEpoch, credentialVersion, now, correlationId);
        var persistedBoundAt = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Operator Console session context was not persisted.");
        var boundAt = persistedBoundAt switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Operator Console session context returned an invalid binding timestamp.")
        };
        return new OperatorConsoleOperatingContext(humanSessionId, userId, operatorDeviceBindingId, operatorShiftId, siteId, siteGroupId, authorizationEpoch, credentialVersion, boundAt, correlationId);
    }

    public async Task<OperatorConsoleOperatingContextValidationFacts> ReadValidationFactsAsync(
        Guid humanSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.operator_user_id, c.operator_device_binding_id, c.operator_shift_id,
                   c.site_id, c.site_group_id, c.authorization_epoch_snapshot,
                   c.credential_version_snapshot, c.bound_at, c.correlation_id, c.context_status,
                   d.browser_key_thumbprint, d.device_status::text, d.trust_level::text,
                   CASE
                       WHEN d.trust_level = 'BROWSER_KEY_ONLY' AND d.last_seen_at IS NOT NULL
                           THEN d.last_seen_at + interval '12 hours'
                       WHEN d.trust_level = 'BROWSER_KEY_AND_MTLS'
                           THEN LEAST(d.last_seen_at + interval '12 hours', d.mtls_certificate_expires_at)
                       WHEN d.trust_level = 'MTLS_ONLY' THEN d.mtls_certificate_expires_at
                       ELSE NULL
                   END,
                   COALESCE(a.active_assignment_count, 0), a.site_id, a.site_group_id,
                   s.operational_status::text, s.operator_user_id, s.site_id, s.site_group_id,
                   s.active_from, s.active_to, s.revoked_at,
                   hs.session_status::text, hs.idle_expires_at, hs.absolute_expires_at,
                   u.authorization_epoch, u.credential_version,
                   EXISTS (
                       SELECT 1
                       FROM identity.user_roles ur
                       JOIN identity.roles r ON r.role_id = ur.role_id
                       JOIN identity.user_role_scope_grants g ON g.user_role_id = ur.user_role_id
                       WHERE ur.user_id = c.operator_user_id
                         AND ur.assignment_status = 'ACTIVE' AND ur.revoked_at IS NULL
                         AND ur.effective_from <= now() AND (ur.effective_to IS NULL OR ur.effective_to > now())
                         AND r.role_status = 'ACTIVE'
                         AND r.effective_from <= now() AND (r.effective_to IS NULL OR r.effective_to > now())
                         AND g.grant_status = 'ACTIVE' AND g.revoked_at IS NULL
                         AND g.effective_from <= now() AND (g.effective_to IS NULL OR g.effective_to > now())
                         AND (g.scope_type = 'GLOBAL'
                              OR (g.scope_type = 'SITE' AND g.site_id = c.site_id)
                              OR (g.scope_type = 'SITE_GROUP' AND g.site_group_id = c.site_group_id))
                   ) AS has_effective_site_scope,
                   EXISTS (
                       SELECT 1 FROM sites.sites site_scope
                       WHERE site_scope.site_id = c.site_id
                         AND site_scope.site_group_id = c.site_group_id
                   ) AS has_canonical_site_group_relationship
            FROM identity.human_sessions hs
            JOIN identity.users u ON u.user_id = hs.user_id
            LEFT JOIN operator_console.operator_session_contexts c ON c.human_session_id = hs.human_session_id
            LEFT JOIN operator_console.operator_device_bindings d ON d.operator_device_binding_id = c.operator_device_binding_id
            LEFT JOIN LATERAL (
                SELECT count(*)::integer AS active_assignment_count,
                       (array_agg(da.site_id ORDER BY da.effective_from DESC))[1] AS site_id,
                       (array_agg(da.site_group_id ORDER BY da.effective_from DESC))[1] AS site_group_id
                FROM operator_console.operator_device_assignment_history da
                WHERE da.operator_device_binding_id = c.operator_device_binding_id
                  AND da.assignment_status_code = 'ACTIVE'
                  AND da.effective_from <= now()
                  AND (da.effective_to IS NULL OR da.effective_to > now())
                  AND da.ended_at IS NULL
            ) a ON true
            LEFT JOIN operator_console.operator_shifts s ON s.operator_shift_id = c.operator_shift_id
            WHERE hs.human_session_id = @human_session_id;
            """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("human_session_id", NpgsqlDbType.Uuid).Value = humanSessionId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return EmptyValidationFacts();
        }

        OperatorConsoleOperatingContext? context = null;
        if (!reader.IsDBNull(0))
        {
            context = new OperatorConsoleOperatingContext(
                humanSessionId,
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetGuid(8));
        }

        return new OperatorConsoleOperatingContextValidationFacts(
            context,
            GetNullableString(reader, 9),
            GetNullableString(reader, 10),
            GetNullableString(reader, 11),
            GetNullableString(reader, 12),
            GetNullableDateTime(reader, 13),
            reader.GetInt32(14),
            GetNullableGuid(reader, 15),
            GetNullableGuid(reader, 16),
            GetNullableString(reader, 17),
            GetNullableGuid(reader, 18),
            GetNullableGuid(reader, 19),
            GetNullableGuid(reader, 20),
            GetNullableDateTime(reader, 21),
            GetNullableDateTime(reader, 22),
            GetNullableDateTime(reader, 23),
            reader.GetString(24),
            reader.GetFieldValue<DateTimeOffset>(25),
            reader.GetFieldValue<DateTimeOffset>(26),
            reader.GetInt64(27),
            reader.GetInt64(28),
            reader.GetBoolean(29),
            reader.GetBoolean(30));
    }

    public async Task InvalidateAsync(Guid humanSessionId, string reasonCode, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE operator_console.operator_session_contexts
            SET context_status = 'INVALIDATED', invalidated_at = @now,
                invalidation_reason_code = @reason_code, correlation_id = @correlation_id,
                updated_at = @now, row_version = row_version + 1
            WHERE human_session_id = @human_session_id AND context_status = 'ACTIVE';
            """;
        await ExecuteAsync(sql, humanSessionId, now, correlationId, cancellationToken, reasonCode);
    }

    public async Task TouchAsync(Guid humanSessionId, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE operator_console.operator_session_contexts
            SET last_validated_at = @now, correlation_id = @correlation_id,
                updated_at = @now, row_version = row_version + 1
            WHERE human_session_id = @human_session_id AND context_status = 'ACTIVE';
            """;
        await ExecuteAsync(sql, humanSessionId, now, correlationId, cancellationToken);
    }

    private async Task ExecuteAsync(string sql, Guid humanSessionId, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken, string? reasonCode = null)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("human_session_id", NpgsqlDbType.Uuid).Value = humanSessionId;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        if (reasonCode is not null)
        {
            command.Parameters.Add("reason_code", NpgsqlDbType.Varchar).Value = reasonCode;
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddBindingParameters(NpgsqlCommand command, Guid humanSessionId, Guid userId, Guid deviceId, Guid shiftId, Guid siteId, Guid siteGroupId, long authorizationEpoch, long credentialVersion, DateTimeOffset now, Guid correlationId)
    {
        command.Parameters.Add("human_session_id", NpgsqlDbType.Uuid).Value = humanSessionId;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
        command.Parameters.Add("device_id", NpgsqlDbType.Uuid).Value = deviceId;
        command.Parameters.Add("shift_id", NpgsqlDbType.Uuid).Value = shiftId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("authorization_epoch", NpgsqlDbType.Bigint).Value = authorizationEpoch;
        command.Parameters.Add("credential_version", NpgsqlDbType.Bigint).Value = credentialVersion;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
    }

    private static OperatorConsoleOperatingContextValidationFacts EmptyValidationFacts() =>
        new(null, null, null, null, null, null, 0, null, null, null, null, null, null, null, null, null, null, null, null, 0, 0, false, false);

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? GetNullableDateTime(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}
