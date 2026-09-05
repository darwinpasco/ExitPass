using System.Data;
using System.Text.Json;
using ExitPass.CentralPms.Application.ShiftManagement;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ShiftManagement;

public sealed class PostgresShiftManagementRepository(string connectionString) : IShiftManagementRepository
{
    private const string ShiftProjection = """
        SELECT s.operator_shift_id, s.shift_reference, s.operator_user_id,
               u.username, u.display_name, u.user_type::text,
               COALESCE(roles.role_codes, ARRAY[]::varchar[]),
               s.site_id, s.site_group_id, site.site_code, site.site_name,
               site_group.site_group_code, site_group.site_group_name,
               s.operator_device_binding_id, device.device_name, s.terminal_reference,
               s.opened_at, s.closed_at, s.operational_status::text,
               s.cash_custody_status, s.opening_cash_minor_units,
               COALESCE(cash.cash_transaction_count, 0), cash.cash_collected_minor_units,
               s.close_type, s.closed_by_user_id, closer.display_name, s.close_reason,
               s.created_at, s.updated_at
        FROM operator_console.operator_shifts s
        JOIN identity.users u ON u.user_id = s.operator_user_id
        JOIN sites.sites site ON site.site_id = s.site_id
        JOIN sites.site_groups site_group ON site_group.site_group_id = s.site_group_id
        LEFT JOIN operator_console.operator_device_bindings device
          ON device.operator_device_binding_id = s.operator_device_binding_id
        LEFT JOIN identity.users closer ON closer.user_id = s.closed_by_user_id
        LEFT JOIN LATERAL (
            SELECT array_agg(DISTINCT r.role_code ORDER BY r.role_code) AS role_codes
            FROM identity.user_roles ur
            JOIN identity.roles r ON r.role_id = ur.role_id
            WHERE ur.user_id = s.operator_user_id
              AND ur.assignment_status = 'ACTIVE' AND ur.revoked_at IS NULL
              AND ur.effective_from <= now() AND (ur.effective_to IS NULL OR ur.effective_to > now())
              AND r.role_status = 'ACTIVE'
              AND r.effective_from <= now() AND (r.effective_to IS NULL OR r.effective_to > now())
        ) roles ON true
        LEFT JOIN LATERAL (
            SELECT count(*)::integer AS cash_transaction_count,
                   sum(c.amount_due_minor_units)::bigint AS cash_collected_minor_units
            FROM core.terminal_cash_payment_commands c
            WHERE c.site_id = s.site_id
              AND c.cashier_shift_id IN (s.shift_reference, s.operator_shift_id::text)
        ) cash ON true
        """;

    public async Task<ShiftActorAccess?> ReadAccessAsync(Guid userId, string permission, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH user_facts AS (
                SELECT u.user_id, u.username, u.display_name, u.user_type::text,
                       (u.user_status = 'ACTIVE' AND u.effective_from <= @now
                        AND (u.effective_to IS NULL OR u.effective_to > @now)) AS user_active
                FROM identity.users u WHERE u.user_id = @user_id
            ), effective_roles AS (
                SELECT DISTINCT ur.user_role_id, r.role_code
                FROM identity.user_roles ur
                JOIN identity.roles r ON r.role_id = ur.role_id
                JOIN identity.role_permissions rp ON rp.role_id = r.role_id
                JOIN identity.permissions p ON p.permission_id = rp.permission_id
                WHERE ur.user_id = @user_id
                  AND ur.assignment_status = 'ACTIVE' AND ur.revoked_at IS NULL
                  AND ur.effective_from <= @now AND (ur.effective_to IS NULL OR ur.effective_to > @now)
                  AND r.role_status = 'ACTIVE'
                  AND r.effective_from <= @now AND (r.effective_to IS NULL OR r.effective_to > @now)
                  AND rp.binding_status = 'ACTIVE' AND rp.revoked_at IS NULL
                  AND rp.effective_from <= @now AND (rp.effective_to IS NULL OR rp.effective_to > @now)
                  AND p.permission_code = @permission AND p.permission_status = 'ACTIVE'
            ), authorized_sites AS (
                SELECT DISTINCT site.site_id, site.site_group_id, site.site_code, site.site_name,
                       sg.site_group_code, sg.site_group_name
                FROM effective_roles er
                JOIN identity.user_role_scope_grants grant_scope ON grant_scope.user_role_id = er.user_role_id
                JOIN sites.sites site ON
                    grant_scope.scope_type = 'GLOBAL'
                    OR (grant_scope.scope_type = 'SITE' AND grant_scope.site_id = site.site_id)
                    OR (grant_scope.scope_type = 'SITE_GROUP' AND grant_scope.site_group_id = site.site_group_id)
                JOIN sites.site_groups sg ON sg.site_group_id = site.site_group_id
                WHERE grant_scope.grant_status = 'ACTIVE' AND grant_scope.revoked_at IS NULL
                  AND grant_scope.effective_from <= @now
                  AND (grant_scope.effective_to IS NULL OR grant_scope.effective_to > @now)
                  AND site.site_status = 'ACTIVE' AND site.effective_from <= @now
                  AND (site.effective_to IS NULL OR site.effective_to > @now)
            )
            SELECT f.username, f.display_name, f.user_type, f.user_active,
                   COALESCE((SELECT array_agg(role_code ORDER BY role_code) FROM effective_roles), ARRAY[]::varchar[]),
                   site.site_id, site.site_group_id, site.site_code, site.site_name,
                   site.site_group_code, site.site_group_name
            FROM user_facts f
            LEFT JOIN authorized_sites site ON true
            ORDER BY site.site_name, site.site_id;
            """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
        command.Parameters.Add("permission", NpgsqlDbType.Varchar).Value = permission;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        ShiftActorAccess? result = null;
        var sites = new List<ShiftAuthorizedSite>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result ??= new ShiftActorAccess(reader.GetBoolean(3), reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<string[]>(4), sites);
            if (!reader.IsDBNull(5))
            {
                sites.Add(new ShiftAuthorizedSite(reader.GetGuid(5), reader.GetGuid(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10)));
            }
        }
        return result;
    }

    public async Task<bool> DeviceMatchesSiteAsync(ShiftManagementActor actor, Guid siteId, Guid siteGroupId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!actor.DeviceServiceIdentityId.HasValue && !actor.OperatorDeviceBindingId.HasValue) return true;
        const string sql = """
            SELECT
                CASE WHEN @service_identity_id IS NULL THEN true ELSE
                    (SELECT count(*) = 1 FROM sites.device_assignments a
                     WHERE a.service_identity_id = @service_identity_id
                       AND a.site_id = @site_id AND a.assignment_status = 'ACTIVE') END,
                CASE WHEN @operator_device_binding_id IS NULL THEN true ELSE
                    (SELECT count(*) = 1
                     FROM operator_console.operator_device_bindings d
                     JOIN operator_console.operator_device_assignment_history a
                       ON a.operator_device_binding_id = d.operator_device_binding_id
                     WHERE d.operator_device_binding_id = @operator_device_binding_id
                       AND d.device_status = 'ACTIVE'
                       AND a.site_id = @site_id AND a.site_group_id = @site_group_id
                       AND a.assignment_status_code = 'ACTIVE' AND a.ended_at IS NULL
                       AND a.effective_from <= @now
                       AND (a.effective_to IS NULL OR a.effective_to > @now)) END;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)actor.DeviceServiceIdentityId ?? DBNull.Value;
        command.Parameters.Add("operator_device_binding_id", NpgsqlDbType.Uuid).Value = (object?)actor.OperatorDeviceBindingId ?? DBNull.Value;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) && reader.GetBoolean(0) && reader.GetBoolean(1);
    }

    public Task<ShiftSummary?> ReadCurrentOwnAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadOneAsync($"{ShiftProjection} WHERE s.operator_user_id = @user_id AND s.operational_status = 'ACTIVE' AND s.revoked_at IS NULL ORDER BY s.opened_at DESC LIMIT 1", cancellationToken, ("user_id", userId));

    public Task<ShiftSummary?> ReadByIdAsync(Guid shiftId, CancellationToken cancellationToken) =>
        ReadOneAsync($"{ShiftProjection} WHERE s.operator_shift_id = @shift_id", cancellationToken, ("shift_id", shiftId));

    public async Task<ShiftSummary> InsertAsync(StartOwnShiftCommand command, ShiftAuthorizedSite site, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var shiftId = Guid.NewGuid();
        var shiftReference = $"SHIFT-{shiftId:N}".ToUpperInvariant();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO operator_console.operator_shifts (
                    operator_shift_id, shift_reference, shift_origin, operator_user_id,
                    site_group_id, site_id, operator_device_binding_id, terminal_reference,
                    operational_status, active_from, opened_at, opened_by_user_id,
                    cash_custody_status, correlation_id, created_at, created_by_user_id,
                    updated_at, updated_by_user_id)
                VALUES (@shift_id, @shift_reference, 'OPERATOR_STARTED', @user_id,
                    @site_group_id, @site_id, @device_id, @terminal_reference,
                    'ACTIVE', @now, @now, @user_id, 'NONE', @correlation_id,
                    @now, @user_id, @now, @user_id);
                """, connection, transaction);
            Add(insert, "shift_id", shiftId); Add(insert, "shift_reference", shiftReference);
            Add(insert, "user_id", command.Actor.UserId); Add(insert, "site_group_id", site.SiteGroupId);
            Add(insert, "site_id", site.SiteId); Add(insert, "device_id", command.Actor.OperatorDeviceBindingId);
            Add(insert, "terminal_reference", Normalize(command.TerminalReference)); Add(insert, "now", now);
            Add(insert, "correlation_id", command.Actor.CorrelationId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await InsertAuditAsync(connection, transaction, command.Actor.UserId, shiftId, site.SiteId, "SHIFT_START", "SUCCESS", null, "START", command.Actor.CorrelationId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ActiveShiftConflictException(await ReadCurrentOwnAsync(command.Actor.UserId, cancellationToken)
                ?? throw new InvalidOperationException("Active shift conflict could not be read back."));
        }
        return (await ReadByIdAsync(shiftId, cancellationToken))!;
    }

    public async Task<ShiftSummary?> RecordResumeAsync(Guid shiftId, Guid actorUserId, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var touch = new NpgsqlCommand("""
            UPDATE operator_console.operator_shifts SET updated_at=@now, updated_by_user_id=@actor, row_version=row_version+1
            WHERE operator_shift_id=@shift_id AND operator_user_id=@actor AND operational_status='ACTIVE' AND revoked_at IS NULL;
            """, connection, transaction);
        Add(touch, "shift_id", shiftId); Add(touch, "actor", actorUserId); Add(touch, "now", now);
        if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1) return null;
        var shift = await ReadOneAsync(connection, transaction, $"{ShiftProjection} WHERE s.operator_shift_id=@shift_id", cancellationToken, ("shift_id", shiftId));
        await InsertAuditAsync(connection, transaction, actorUserId, shiftId, shift!.SiteId, "SHIFT_RESUME", "SUCCESS", null, "RESUME", correlationId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return shift;
    }

    public async Task<ShiftSummary?> CloseAsync(Guid shiftId, Guid actorUserId, string closeType, string? reason, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var update = new NpgsqlCommand("""
            UPDATE operator_console.operator_shifts
            SET operational_status='ENDED', active_to=@now, closed_at=@now,
                closed_by_user_id=@actor, close_type=@close_type, close_reason=@reason,
                updated_at=@now, updated_by_user_id=@actor, row_version=row_version+1
            WHERE operator_shift_id=@shift_id AND operational_status='ACTIVE'
              AND revoked_at IS NULL AND cash_custody_status <> 'OPEN';
            """, connection, transaction);
        Add(update, "shift_id", shiftId); Add(update, "actor", actorUserId); Add(update, "close_type", closeType);
        Add(update, "reason", Normalize(reason)); Add(update, "now", now);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) return null;
        var shift = await ReadOneAsync(connection, transaction, $"{ShiftProjection} WHERE s.operator_shift_id=@shift_id", cancellationToken, ("shift_id", shiftId));
        await InsertAuditAsync(connection, transaction, actorUserId, shiftId, shift!.SiteId,
            closeType == "SUPERVISOR_EXCEPTION" ? "SHIFT_EXCEPTION_CLOSE" : "SHIFT_CLOSE",
            "SUCCESS", null, closeType, correlationId, now, cancellationToken, reason);
        await transaction.CommitAsync(cancellationToken);
        return shift;
    }

    public async Task<IReadOnlyList<ShiftSummary>> ListAsync(IReadOnlyList<Guid> authorizedSiteIds, string view, Guid? siteId, Guid? staffUserId, int limit, CancellationToken cancellationToken)
    {
        if (authorizedSiteIds.Count == 0) return [];
        var statusClause = string.Equals(view, "RECENTLY_CLOSED", StringComparison.OrdinalIgnoreCase)
            ? "s.operational_status <> 'ACTIVE'"
            : "s.operational_status = 'ACTIVE'";
        var sql = $"{ShiftProjection} WHERE s.site_id = ANY(@site_ids) AND {statusClause} AND (@site_id IS NULL OR s.site_id=@site_id) AND (@staff_user_id IS NULL OR s.operator_user_id=@staff_user_id) ORDER BY COALESCE(s.closed_at,s.opened_at) DESC LIMIT @limit";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = authorizedSiteIds.ToArray();
        Add(command, "site_id", siteId); Add(command, "staff_user_id", staffUserId); Add(command, "limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ShiftSummary>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Map(reader));
        return items;
    }

    public async Task RecordDenialAsync(Guid actorUserId, Guid? shiftId, Guid? siteId, string reasonCode, string action, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await InsertAuditAsync(connection, null, actorUserId, shiftId, siteId, "SHIFT_ACTION_DENIED", "DENIED", reasonCode, action, correlationId, now, cancellationToken);
    }

    private async Task<ShiftSummary?> ReadOneAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadOneAsync(connection, null, sql, cancellationToken, parameters);
    }

    private static async Task<ShiftSummary?> ReadOneAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters) Add(command, parameter.Name, parameter.Value);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private static ShiftSummary Map(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.GetFieldValue<string[]>(6), reader.GetGuid(7), reader.GetGuid(8), reader.GetString(9), reader.GetString(10),
        reader.GetString(11), reader.GetString(12), NullableGuid(reader, 13), NullableString(reader, 14), NullableString(reader, 15),
        reader.GetFieldValue<DateTimeOffset>(16), NullableDate(reader, 17), reader.GetString(18), reader.GetString(19),
        NullableLong(reader, 20), reader.GetInt32(21), NullableLong(reader, 22), NullableString(reader, 23), NullableGuid(reader, 24),
        NullableString(reader, 25), NullableString(reader, 26), reader.GetFieldValue<DateTimeOffset>(27), reader.GetFieldValue<DateTimeOffset>(28));

    private static async Task InsertAuditAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid actorUserId, Guid? shiftId, Guid? siteId, string eventType, string status, string? reasonCode, string action, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken, string? reason = null)
    {
        const string sql = """
            INSERT INTO operations.operator_action_logs (
                operator_action_log_id, operator_user_id, action_type, action_reason_code,
                target_entity_type, target_entity_id, site_id, action_status, action_notes,
                performed_at, correlation_id, created_at, created_by_user_id)
            VALUES (gen_random_uuid(), @actor, @event_type::operations.operator_action_type_enum, @reason_code,
                'OPERATOR_SHIFT', @shift_id, @site_id, @status::operations.operator_action_status_enum,
                @notes::jsonb::text, @now, @correlation_id, @now, @actor);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, "actor", actorUserId); Add(command, "event_type", eventType); Add(command, "reason_code", reasonCode);
        Add(command, "shift_id", shiftId); Add(command, "site_id", siteId); Add(command, "status", status);
        Add(command, "notes", JsonSerializer.Serialize(new { Action = action, Reason = Normalize(reason) }));
        Add(command, "now", now); Add(command, "correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void Add(NpgsqlCommand command, string name, object? value)
    {
        if (value is not null)
        {
            command.Parameters.AddWithValue(name, value);
            return;
        }

        var type = name.EndsWith("_id", StringComparison.Ordinal) || name is "shift_id" or "device_id" or "actor"
            ? NpgsqlDbType.Uuid
            : name is "terminal_reference" or "reason" or "reason_code"
                ? NpgsqlDbType.Text
                : NpgsqlDbType.Varchar;
        command.Parameters.Add(name, type).Value = DBNull.Value;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NullableString(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static Guid? NullableGuid(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetGuid(index);
    private static long? NullableLong(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index);
    private static DateTimeOffset? NullableDate(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetFieldValue<DateTimeOffset>(index);
}
