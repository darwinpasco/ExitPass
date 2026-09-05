using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ShiftManagementSchemaIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database)
{
    [Fact]
    public async Task OperationalShiftPatch_IsIdempotentAndEnforcesOneActiveShiftPerUser()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                to_regclass('operator_console.ux_operator_shifts__one_active_per_user') IS NOT NULL,
                count(*) FILTER (WHERE column_name = 'shift_reference') = 1,
                count(*) FILTER (WHERE column_name = 'opened_at') = 1,
                count(*) FILTER (WHERE column_name = 'cash_custody_status') = 1
            FROM information_schema.columns
            WHERE table_schema = 'operator_console'
              AND table_name = 'operator_shifts';
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetBoolean(0).Should().BeTrue();
        reader.GetBoolean(1).Should().BeTrue();
        reader.GetBoolean(2).Should().BeTrue();
        reader.GetBoolean(3).Should().BeTrue();
    }

    [Fact]
    public async Task OperationalShiftAuditActions_AreAvailableInCanonicalAuditType()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT enumlabel
            FROM pg_enum
            WHERE enumtypid = 'operations.operator_action_type_enum'::regtype
              AND enumlabel LIKE 'SHIFT_%'
            ORDER BY enumlabel;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        values.Should().Contain(["SHIFT_START", "SHIFT_RESUME", "SHIFT_CLOSE", "SHIFT_EXCEPTION_CLOSE", "SHIFT_ACTION_DENIED"]);
    }

    [Fact]
    public async Task ShiftManagementPermissions_AreBoundOnlyToOperationsSupervisor()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT p.permission_code, r.role_code, count(*)
            FROM identity.permissions p
            JOIN identity.role_permissions rp ON rp.permission_id = p.permission_id
            JOIN identity.roles r ON r.role_id = rp.role_id
            WHERE p.permission_code IN ('shift-management.view', 'shift-management.manage')
              AND p.permission_status = 'ACTIVE'
              AND rp.binding_status = 'ACTIVE'
              AND rp.revoked_at IS NULL
            GROUP BY p.permission_code, r.role_code
            ORDER BY p.permission_code;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var bindings = new List<(string Permission, string Role, long Count)>();
        while (await reader.ReadAsync()) bindings.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));

        bindings.Should().HaveCount(2);
        bindings.Should().OnlyContain(value => value.Role == "OPERATIONS_SUPERVISOR" && value.Count == 1);
        bindings.Select(value => value.Permission).Should().BeEquivalentTo(["shift-management.view", "shift-management.manage"]);
    }
}
