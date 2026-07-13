using System.Data;
using System.Data.Common;
using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class ManagementPlatformIdentityRbacInventoryRepository : IManagementPlatformIdentityRbacInventoryRepository
{
    private readonly string _connectionString;

    public ManagementPlatformIdentityRbacInventoryRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<ManagementPlatformIdentityRbacPersistenceInventory> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var gaps = new List<ManagementPlatformInventoryGap>();
        var users = await ReadUsersAsync(connection, gaps, cancellationToken);
        var userRoleAssignments = await ReadUserRoleAssignmentsAsync(connection, gaps, cancellationToken);
        var userSiteScopes = await ReadUserSiteScopesAsync(connection, gaps, cancellationToken);
        var deviceBindings = await ReadDeviceBindingsAsync(connection, gaps, cancellationToken);
        var shifts = await ReadShiftsAsync(connection, gaps, cancellationToken);

        return new ManagementPlatformIdentityRbacPersistenceInventory(
            users,
            userRoleAssignments,
            userSiteScopes,
            deviceBindings,
            shifts,
            gaps);
    }

    private static async Task<IReadOnlyList<ManagementPlatformIdentityUser>> ReadUsersAsync(
        NpgsqlConnection connection,
        List<ManagementPlatformInventoryGap> gaps,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "identity", "users", cancellationToken))
        {
            gaps.Add(MissingTable("identity-users-table-missing", "identity.users"));
            return [];
        }

        const string sql = """
            SELECT
                user_id,
                COALESCE(NULLIF(username, ''), 'Not available') AS username,
                COALESCE(NULLIF(display_name, ''), NULLIF(username, ''), 'Not available') AS display_name,
                NULLIF(email, '') AS email,
                COALESCE(user_status::text, 'UNKNOWN') AS user_status,
                COALESCE(user_type::text, 'Not available') AS source_system,
                created_at,
                updated_at
            FROM identity.users
            ORDER BY created_at DESC NULLS LAST, user_id
            LIMIT 500;
            """;

        var users = new List<ManagementPlatformIdentityUser>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new ManagementPlatformIdentityUser(
                reader.GetGuid("user_id"),
                reader.GetString("username"),
                reader.GetString("display_name"),
                GetNullableString(reader, "email"),
                reader.GetString("user_status"),
                reader.GetString("source_system"),
                GetNullableDateTimeOffset(reader, "created_at"),
                GetNullableDateTimeOffset(reader, "updated_at")));
        }

        return users;
    }

    private static async Task<IReadOnlyList<ManagementPlatformUserRoleAssignment>> ReadUserRoleAssignmentsAsync(
        NpgsqlConnection connection,
        List<ManagementPlatformInventoryGap> gaps,
        CancellationToken cancellationToken)
    {
        var requiredTables = new[]
        {
            ("identity", "user_roles"),
            ("identity", "roles")
        };

        if (!await TablesExistAsync(connection, requiredTables, cancellationToken))
        {
            gaps.Add(MissingTable("identity-role-assignment-tables-missing", "identity.user_roles or identity.roles"));
            return [];
        }

        const string sql = """
            SELECT
                ur.user_id,
                ur.role_id,
                COALESCE(NULLIF(r.role_code, ''), r.role_id::text) AS role_key,
                COALESCE(NULLIF(r.role_name, ''), NULLIF(r.role_code, ''), 'Not available') AS role_name,
                COALESCE(r.role_status::text, 'UNKNOWN') AS role_status,
                COALESCE(ur.assignment_status::text, 'UNKNOWN') AS assignment_status,
                ur.effective_from,
                ur.effective_to
            FROM identity.user_roles ur
            LEFT JOIN identity.roles r ON r.role_id = ur.role_id
            ORDER BY ur.effective_from DESC NULLS LAST, ur.user_id
            LIMIT 1000;
            """;

        var assignments = new List<ManagementPlatformUserRoleAssignment>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assignments.Add(new ManagementPlatformUserRoleAssignment(
                reader.GetGuid("user_id"),
                GetNullableGuid(reader, "role_id"),
                reader.GetString("role_key"),
                reader.GetString("role_name"),
                reader.GetString("role_status"),
                reader.GetString("assignment_status"),
                GetNullableDateTimeOffset(reader, "effective_from"),
                GetNullableDateTimeOffset(reader, "effective_to")));
        }

        return assignments;
    }

    private static async Task<IReadOnlyList<ManagementPlatformUserSiteScope>> ReadUserSiteScopesAsync(
        NpgsqlConnection connection,
        List<ManagementPlatformInventoryGap> gaps,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "operator_console", "operator_shifts", cancellationToken))
        {
            gaps.Add(MissingTable("operator-shifts-table-missing", "operator_console.operator_shifts"));
            return [];
        }

        const string sql = """
            SELECT DISTINCT
                s.operator_user_id AS user_id,
                s.site_group_id,
                s.site_id,
                'Not available' AS site_group_name,
                'Not available' AS site_name,
                'operator_console.operator_shifts' AS source,
                COALESCE(s.operational_status::text, 'UNKNOWN') AS status
            FROM operator_console.operator_shifts s
            ORDER BY s.operator_user_id, s.site_group_id, s.site_id
            LIMIT 1000;
            """;

        var scopes = new List<ManagementPlatformUserSiteScope>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            scopes.Add(new ManagementPlatformUserSiteScope(
                reader.GetGuid("user_id"),
                GetNullableGuid(reader, "site_group_id"),
                GetNullableGuid(reader, "site_id"),
                reader.GetString("site_group_name"),
                reader.GetString("site_name"),
                reader.GetString("source"),
                reader.GetString("status")));
        }

        return scopes;
    }

    private static async Task<IReadOnlyList<ManagementPlatformDeviceBinding>> ReadDeviceBindingsAsync(
        NpgsqlConnection connection,
        List<ManagementPlatformInventoryGap> gaps,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "operator_console", "operator_device_bindings", cancellationToken))
        {
            gaps.Add(MissingTable("operator-device-bindings-table-missing", "operator_console.operator_device_bindings"));
            return [];
        }

        const string sql = """
            SELECT
                operator_device_binding_id,
                COALESCE(NULLIF(device_name, ''), NULLIF(device_binding_code, ''), 'Not available') AS device_label,
                created_by_user_id AS assigned_user_id,
                site_group_id,
                site_id,
                COALESCE(device_status::text, 'UNKNOWN') AS device_status,
                COALESCE(trust_level::text, 'UNKNOWN') AS trust_status,
                last_seen_at
            FROM operator_console.operator_device_bindings
            ORDER BY last_seen_at DESC NULLS LAST, operator_device_binding_id
            LIMIT 500;
            """;

        var bindings = new List<ManagementPlatformDeviceBinding>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bindings.Add(new ManagementPlatformDeviceBinding(
                reader.GetGuid("operator_device_binding_id"),
                reader.GetString("device_label"),
                GetNullableGuid(reader, "assigned_user_id"),
                GetNullableGuid(reader, "site_group_id"),
                GetNullableGuid(reader, "site_id"),
                reader.GetString("device_status"),
                reader.GetString("trust_status"),
                GetNullableDateTimeOffset(reader, "last_seen_at")));
        }

        return bindings;
    }

    private static async Task<IReadOnlyList<ManagementPlatformShift>> ReadShiftsAsync(
        NpgsqlConnection connection,
        List<ManagementPlatformInventoryGap> gaps,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "operator_console", "operator_shifts", cancellationToken))
        {
            gaps.Add(MissingTable("operator-shift-inventory-table-missing", "operator_console.operator_shifts"));
            return [];
        }

        const string sql = """
            SELECT
                operator_shift_id,
                operator_user_id,
                site_group_id,
                site_id,
                COALESCE(operational_status::text, 'UNKNOWN') AS operational_status,
                active_from,
                active_to
            FROM operator_console.operator_shifts
            ORDER BY active_from DESC NULLS LAST, operator_shift_id
            LIMIT 500;
            """;

        var shifts = new List<ManagementPlatformShift>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            shifts.Add(new ManagementPlatformShift(
                reader.GetGuid("operator_shift_id"),
                GetNullableGuid(reader, "operator_user_id"),
                GetNullableGuid(reader, "site_group_id"),
                GetNullableGuid(reader, "site_id"),
                reader.GetString("operational_status"),
                GetNullableDateTimeOffset(reader, "active_from"),
                GetNullableDateTimeOffset(reader, "active_to")));
        }

        return shifts;
    }

    private static async Task<bool> TablesExistAsync(
        NpgsqlConnection connection,
        IReadOnlyList<(string Schema, string Table)> tables,
        CancellationToken cancellationToken)
    {
        foreach (var (schema, table) in tables)
        {
            if (!await TableExistsAsync(connection, schema, table, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = @table
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("schema", NpgsqlDbType.Text).Value = schema;
        command.Parameters.Add("table", NpgsqlDbType.Text).Value = table;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static ManagementPlatformInventoryGap MissingTable(string gapKey, string tableName) =>
        new(
            gapKey,
            "Medium",
            $"The {tableName} table was not found, so this inventory section is reported as unavailable.");

    private static string? GetNullableString(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
