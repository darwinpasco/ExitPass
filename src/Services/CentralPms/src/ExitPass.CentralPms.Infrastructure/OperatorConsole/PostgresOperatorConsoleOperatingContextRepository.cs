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
                   CASE
                       WHEN device.device_id IS NULL THEN 'PENDING'
                       WHEN device.lifecycle_status IN ('REVOKED', 'REPLACED') THEN 'REVOKED'
                       WHEN device.lifecycle_status = 'SUSPENDED' THEN 'SUSPENDED'
                       WHEN device.effective_from > @now
                           OR (device.effective_to IS NOT NULL AND device.effective_to <= @now) THEN 'REVOKED'
                       WHEN service.identity_status IS DISTINCT FROM 'ACTIVE' OR service.revoked_at IS NOT NULL
                           OR service.effective_from > @now
                           OR (service.effective_to IS NOT NULL AND service.effective_to <= @now)
                           OR (service.credential_expires_at IS NOT NULL AND service.credential_expires_at <= @now) THEN 'REVOKED'
                       WHEN credential.credential_status IS DISTINCT FROM 'ACTIVE' THEN 'REVOKED'
                       WHEN credential.expires_at IS NOT NULL AND credential.expires_at <= @now THEN 'EXPIRED'
                       WHEN device.lifecycle_status <> 'ACTIVE' THEN 'PENDING'
                       ELSE d.device_status::text
                   END,
                   d.trust_level::text,
                   d.site_id,
                   d.site_group_id,
                   (site_scope.site_id IS NOT NULL
                       AND site_group_scope.site_group_id IS NOT NULL
                       AND canonical_assignment.active_assignment_count = 1),
                   CASE
                       WHEN d.trust_level = 'BROWSER_KEY_ONLY' THEN credential.expires_at
                       WHEN d.trust_level = 'BROWSER_KEY_AND_MTLS'
                           THEN LEAST(credential.expires_at, d.mtls_certificate_expires_at)
                       WHEN d.trust_level = 'MTLS_ONLY' THEN d.mtls_certificate_expires_at
                       ELSE NULL
                   END,
                   d.matching_proof_count,
                   COALESCE(a.active_assignment_count, 0),
                   a.site_id,
                   a.site_group_id,
                   EXISTS (
                       SELECT 1
                       FROM identity.device_provisioning_credentials provisioning
                       WHERE provisioning.device_id = device.device_id
                         AND device.lifecycle_status = 'CREDENTIAL_ROTATION'
                         AND provisioning.provisioning_purpose = 'ROTATION'
                         AND provisioning.provisioning_status = 'ISSUED'
                         AND provisioning.consumed_at IS NULL
                         AND provisioning.cancelled_at IS NULL
                         AND provisioning.expires_at > @now
                         AND provisioning.intended_credential_version = device.credential_version + 1
                   ) AS rotation_enrollment_available,
                   EXISTS (
                       SELECT 1
                       FROM identity.devices successor
                       JOIN identity.device_provisioning_credentials provisioning
                         ON provisioning.device_id = successor.device_id
                       WHERE device.lifecycle_status = 'REPLACED'
                         AND successor.device_id = device.replacement_device_id
                         AND successor.predecessor_device_id = device.device_id
                         AND successor.device_type = 'OPERATOR_CONSOLE'
                         AND successor.lifecycle_status = 'PROVISIONING_PENDING'
                         AND provisioning.provisioning_purpose = 'REPLACEMENT'
                         AND provisioning.provisioning_status = 'ISSUED'
                         AND provisioning.consumed_at IS NULL
                         AND provisioning.cancelled_at IS NULL
                         AND provisioning.expires_at > @now
                         AND provisioning.intended_credential_version = successor.credential_version + 1
                   ) AS replacement_enrollment_available
            FROM matching_devices d
            LEFT JOIN identity.devices device
              ON device.device_id = d.operator_device_binding_id
             AND device.service_identity_id = d.service_identity_id
            LEFT JOIN identity.service_identities service
              ON service.service_identity_id = device.service_identity_id
            LEFT JOIN identity.device_credential_history credential
              ON credential.device_id = device.device_id
             AND credential.credential_version = device.credential_version
            LEFT JOIN sites.sites site_scope
              ON site_scope.site_id = d.site_id
             AND site_scope.site_group_id = d.site_group_id
             AND site_scope.site_status = 'ACTIVE'
             AND site_scope.effective_from <= @now
             AND (site_scope.effective_to IS NULL OR site_scope.effective_to > @now)
            LEFT JOIN sites.site_groups site_group_scope
              ON site_group_scope.site_group_id = d.site_group_id
             AND site_group_scope.site_group_status = 'ACTIVE'
             AND site_group_scope.effective_from <= @now
             AND (site_group_scope.effective_to IS NULL OR site_group_scope.effective_to > @now)
            LEFT JOIN LATERAL (
                SELECT count(*)::integer AS active_assignment_count
                FROM sites.device_assignments canonical
                WHERE canonical.service_identity_id = device.service_identity_id
                  AND canonical.site_id = d.site_id
                  AND canonical.assignment_type = 'SERVICE_PRINCIPAL'
                  AND canonical.assignment_status = 'ACTIVE'
                  AND canonical.unassigned_at IS NULL
            ) canonical_assignment ON true
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
            GetNullableGuid(reader, 10),
            reader.GetBoolean(11),
            reader.GetBoolean(12));
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
        AddBindingParameters(command, humanSessionId, userId, operatorDeviceBindingId, siteId, siteGroupId, authorizationEpoch, credentialVersion, now, correlationId);
        var persistedBoundAt = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Operator Console session context was not persisted.");
        var boundAt = persistedBoundAt switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Operator Console session context returned an invalid binding timestamp.")
        };
        return new OperatorConsoleOperatingContext(humanSessionId, userId, operatorDeviceBindingId, null, siteId, siteGroupId, authorizationEpoch, credentialVersion, boundAt, correlationId);
    }

    public async Task<OperatorConsoleOperatingContextValidationFacts> ReadValidationFactsAsync(
        Guid humanSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.operator_user_id, c.operator_device_binding_id, c.operator_shift_id,
                   c.site_id, c.site_group_id, c.authorization_epoch_snapshot,
                   c.credential_version_snapshot, c.bound_at, c.correlation_id, c.context_status,
                   d.browser_key_thumbprint,
                   CASE
                       WHEN device.device_id IS NULL THEN 'PENDING'
                       WHEN device.lifecycle_status IN ('REVOKED', 'REPLACED') THEN 'REVOKED'
                       WHEN device.lifecycle_status = 'SUSPENDED' THEN 'SUSPENDED'
                       WHEN device.effective_from > now()
                           OR (device.effective_to IS NOT NULL AND device.effective_to <= now()) THEN 'REVOKED'
                       WHEN service.identity_status IS DISTINCT FROM 'ACTIVE' OR service.revoked_at IS NOT NULL
                           OR service.effective_from > now()
                           OR (service.effective_to IS NOT NULL AND service.effective_to <= now())
                           OR (service.credential_expires_at IS NOT NULL AND service.credential_expires_at <= now()) THEN 'REVOKED'
                       WHEN credential.credential_status IS DISTINCT FROM 'ACTIVE' THEN 'REVOKED'
                       WHEN credential.expires_at IS NOT NULL AND credential.expires_at <= now() THEN 'EXPIRED'
                       WHEN device.lifecycle_status <> 'ACTIVE' THEN 'PENDING'
                       ELSE d.device_status::text
                   END,
                   d.trust_level::text,
                   CASE
                       WHEN d.trust_level = 'BROWSER_KEY_ONLY' THEN credential.expires_at
                       WHEN d.trust_level = 'BROWSER_KEY_AND_MTLS'
                           THEN LEAST(credential.expires_at, d.mtls_certificate_expires_at)
                       WHEN d.trust_level = 'MTLS_ONLY' THEN d.mtls_certificate_expires_at
                       ELSE NULL
                   END,
                   COALESCE(a.active_assignment_count, 0), a.site_id, a.site_group_id,
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
                   (
                       EXISTS (
                           SELECT 1 FROM sites.sites site_scope
                           WHERE site_scope.site_id = c.site_id
                             AND site_scope.site_group_id = c.site_group_id
                             AND site_scope.site_status = 'ACTIVE'
                             AND site_scope.effective_from <= now()
                             AND (site_scope.effective_to IS NULL OR site_scope.effective_to > now())
                       )
                       AND EXISTS (
                           SELECT 1 FROM sites.site_groups site_group_scope
                           WHERE site_group_scope.site_group_id = c.site_group_id
                             AND site_group_scope.site_group_status = 'ACTIVE'
                             AND site_group_scope.effective_from <= now()
                             AND (site_group_scope.effective_to IS NULL OR site_group_scope.effective_to > now())
                       )
                       AND EXISTS (
                           SELECT 1 FROM sites.device_assignments canonical
                           WHERE canonical.service_identity_id = device.service_identity_id
                             AND canonical.site_id = c.site_id
                             AND canonical.assignment_type = 'SERVICE_PRINCIPAL'
                             AND canonical.assignment_status = 'ACTIVE'
                             AND canonical.unassigned_at IS NULL
                       )
                   ) AS has_canonical_site_group_relationship
            FROM identity.human_sessions hs
            JOIN identity.users u ON u.user_id = hs.user_id
            LEFT JOIN operator_console.operator_session_contexts c ON c.human_session_id = hs.human_session_id
            LEFT JOIN operator_console.operator_device_bindings d ON d.operator_device_binding_id = c.operator_device_binding_id
            LEFT JOIN identity.devices device
              ON device.device_id = d.operator_device_binding_id
             AND device.service_identity_id = d.service_identity_id
            LEFT JOIN identity.service_identities service
              ON service.service_identity_id = device.service_identity_id
            LEFT JOIN identity.device_credential_history credential
              ON credential.device_id = device.device_id
             AND credential.credential_version = device.credential_version
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
                GetNullableGuid(reader, 2),
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
            reader.GetString(17),
            reader.GetFieldValue<DateTimeOffset>(18),
            reader.GetFieldValue<DateTimeOffset>(19),
            reader.GetInt64(20),
            reader.GetInt64(21),
            reader.GetBoolean(22),
            reader.GetBoolean(23));
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

    private static void AddBindingParameters(NpgsqlCommand command, Guid humanSessionId, Guid userId, Guid deviceId, Guid siteId, Guid siteGroupId, long authorizationEpoch, long credentialVersion, DateTimeOffset now, Guid correlationId)
    {
        command.Parameters.Add("human_session_id", NpgsqlDbType.Uuid).Value = humanSessionId;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
        command.Parameters.Add("device_id", NpgsqlDbType.Uuid).Value = deviceId;
        command.Parameters.Add("shift_id", NpgsqlDbType.Uuid).Value = DBNull.Value;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("authorization_epoch", NpgsqlDbType.Bigint).Value = authorizationEpoch;
        command.Parameters.Add("credential_version", NpgsqlDbType.Bigint).Value = credentialVersion;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
    }

    private static OperatorConsoleOperatingContextValidationFacts EmptyValidationFacts() =>
        new(null, null, null, null, null, null, 0, null, null, null, null, null, 0, 0, false, false);

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? GetNullableDateTime(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}
