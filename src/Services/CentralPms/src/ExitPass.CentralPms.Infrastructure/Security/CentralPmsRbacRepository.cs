using ExitPass.CentralPms.Application.Security;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Security;

/// <summary>
/// PostgreSQL-backed RBAC repository for Central PMS operational authorization.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - RBAC checks read identity-owned tables and never mutate payment, provider, exit, gate, or settlement truth.
/// - Denied privileged access is written only to audit-owned evidence where supported.
/// </summary>
public sealed class CentralPmsRbacRepository : ICentralPmsRbacRepository
{
    private readonly string _connectionString;

    public CentralPmsRbacRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<bool> UserHasAnyPermissionAsync(
        Guid userId,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        if (permissionCodes.Count == 0)
        {
            return false;
        }

        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.users u
                JOIN identity.user_roles ur ON ur.user_id = u.user_id
                JOIN identity.roles r ON r.role_id = ur.role_id
                JOIN identity.role_permissions rp ON rp.role_id = r.role_id
                JOIN identity.permissions p ON p.permission_id = rp.permission_id
                WHERE u.user_id = @user_id
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
                  AND p.permission_code = ANY(@permission_codes)
            );
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("permission_codes", permissionCodes.ToArray());

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> ServiceIdentityIsActiveAsync(
        Guid serviceIdentityId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.service_identities
                WHERE service_identity_id = @service_identity_id
                  AND identity_status = 'ACTIVE'
                  AND effective_from <= now()
                  AND (effective_to IS NULL OR effective_to > now())
                  AND revoked_at IS NULL
            );
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task RecordDeniedAsync(
        string policyName,
        Guid? userId,
        Guid? serviceIdentityId,
        Guid? correlationId,
        string requestPath,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit.audit_events (
                audit_event_id,
                event_type,
                event_category,
                event_result,
                event_reason_code,
                target_entity_type,
                source_schema,
                source_service_name,
                source_channel,
                actor_user_id,
                actor_service_identity_id,
                summary,
                occurred_at,
                recorded_at,
                correlation_id,
                created_at
            )
            VALUES (
                gen_random_uuid(),
                'CentralPmsRbacDenied',
                'SECURITY_RELEVANT',
                'DENIED',
                @policy_name,
                'CentralPmsEndpoint',
                'identity',
                'central-pms',
                'HTTP',
                @actor_user_id,
                @actor_service_identity_id,
                @summary,
                now(),
                now(),
                @correlation_id,
                now()
            );
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("policy_name", policyName);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = (object?)serviceIdentityId ?? DBNull.Value;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = (object?)correlationId ?? DBNull.Value;
        command.Parameters.AddWithValue("summary", $"Denied Central PMS request for policy {policyName} on {requestPath}.");

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordAuditEventAsync(
        string eventType,
        string eventResult,
        string eventReasonCode,
        string targetEntityType,
        Guid? targetEntityId,
        Guid? actorUserId,
        Guid? actorServiceIdentityId,
        Guid? correlationId,
        string summary,
        CancellationToken cancellationToken)
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
                source_schema,
                source_service_name,
                source_channel,
                actor_user_id,
                actor_service_identity_id,
                summary,
                occurred_at,
                recorded_at,
                correlation_id,
                created_at
            )
            VALUES (
                gen_random_uuid(),
                @event_type,
                'SECURITY_RELEVANT',
                @event_result::audit.audit_event_result_enum,
                @event_reason_code,
                @target_entity_type,
                @target_entity_id,
                'identity',
                'central-pms',
                'HTTP',
                @actor_user_id,
                @actor_service_identity_id,
                @summary,
                now(),
                now(),
                @correlation_id,
                now()
            );
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("event_result", eventResult);
        command.Parameters.AddWithValue("event_reason_code", eventReasonCode);
        command.Parameters.AddWithValue("target_entity_type", targetEntityType);
        command.Parameters.Add("target_entity_id", NpgsqlDbType.Uuid).Value = (object?)targetEntityId ?? DBNull.Value;
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = (object?)actorUserId ?? DBNull.Value;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = (object?)actorServiceIdentityId ?? DBNull.Value;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = (object?)correlationId ?? DBNull.Value;
        command.Parameters.AddWithValue("summary", summary.Length > 256 ? summary[..256] : summary);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
