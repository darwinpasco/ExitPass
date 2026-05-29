using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Integration tests for persisted Operator Console access evaluation audit evidence.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationPersistenceIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/access/evaluate";

    /// <summary>
    /// Verifies allowed evaluations persist one evaluation row and no denial reason rows.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenAllowed_PersistsEvaluationWithoutReasons()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        var context = await SeedAllowedContextAsync();
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, context.CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessEvaluationResponse>();
        body.Should().NotBeNull();
        body!.Allowed.Should().BeTrue();
        body.Decision.Should().Be("ALLOWED");
        body.Persisted.Should().BeTrue();
        body.EvaluationId.Should().NotBe(Guid.Empty);
        body.CorrelationId.Should().Be(context.CorrelationId);

        var persisted = await ReadPersistedEvaluationAsync(body.EvaluationId);
        persisted.Should().NotBeNull();
        persisted!.CorrelationId.Should().Be(context.CorrelationId);
        persisted.RequestedAction.Should().Be("START_WORKFLOW");
        persisted.EvaluationStatus.Should().Be("ALLOWED");
        persisted.OperatorUserId.Should().Be(context.UserId);
        persisted.HrIdentityMappingId.Should().Be(context.HrIdentityMappingId);
        persisted.OperatorDeviceBindingId.Should().Be(context.OperatorDeviceBindingId);
        persisted.OperatorShiftId.Should().Be(context.OperatorShiftId);
        persisted.SiteGroupId.Should().Be(context.SiteGroupId);
        persisted.SiteId.Should().Be(context.SiteId);
        persisted.Reasons.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies denied evaluations persist deterministic denial reason rows.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenDenied_PersistsEvaluationAndReasons()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        var context = await SeedDeniedContextAsync();
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, context.CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessEvaluationResponse>();
        body.Should().NotBeNull();
        body!.Allowed.Should().BeFalse();
        body.Decision.Should().Be("DENIED");
        body.Persisted.Should().BeTrue();
        body.EvaluationId.Should().NotBe(Guid.Empty);
        body.DenialReasons.Should().ContainInOrder(
            "HR_IDENTITY_MAPPING_NOT_FOUND",
            "DEVICE_BINDING_NOT_FOUND",
            "DEVICE_SITE_ASSIGNMENT_NOT_FOUND",
            "NO_ACTIVE_SHIFT");

        var persisted = await ReadPersistedEvaluationAsync(body.EvaluationId);
        persisted.Should().NotBeNull();
        persisted!.CorrelationId.Should().Be(context.CorrelationId);
        persisted.EvaluationStatus.Should().Be("DENIED");
        persisted.OperatorUserId.Should().Be(context.UserId);
        persisted.HrIdentityMappingId.Should().BeNull();
        persisted.OperatorDeviceBindingId.Should().BeNull();
        persisted.OperatorShiftId.Should().BeNull();
        persisted.SiteGroupId.Should().Be(context.SiteGroupId);
        persisted.SiteId.Should().Be(context.SiteId);
        persisted.Reasons.Select(reason => reason.ReasonCode).Should().ContainInOrder(body.DenialReasons);
        persisted.Reasons.Select(reason => reason.DisplayOrder).Should().Equal(0, 1, 2, 3);
    }

    private static async Task<TestAccessEvaluationContext> SeedAllowedContextAsync()
    {
        var site = await ReadActiveSiteAsync();
        var context = TestAccessEvaluationContext.Create(site.SiteGroupId, site.SiteId);
        await SeedIdentityUserAsync(context.UserId, context.CorrelationId);

        const string sql = """
            INSERT INTO operator_console.hr_identity_mappings (
                hr_identity_mapping_id,
                user_id,
                hr_provider_code,
                external_person_id_hash,
                external_person_id_masked,
                mapping_status,
                effective_from,
                effective_to,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @hr_identity_mapping_id,
                @user_id,
                'TEST_HR',
                @external_person_id_hash,
                'EMP-****',
                'ACTIVE',
                @effective_from,
                @effective_to,
                @correlation_id,
                @user_id,
                @user_id
            );

            INSERT INTO operator_console.operator_device_bindings (
                operator_device_binding_id,
                device_binding_code,
                device_name,
                site_group_id,
                site_id,
                browser_key_thumbprint,
                device_status,
                trust_level,
                binding_source,
                last_seen_at,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @operator_device_binding_id,
                @device_binding_code,
                'Operator Console Persistence Test Device',
                @site_group_id,
                @site_id,
                @browser_key_thumbprint,
                'ACTIVE',
                'BROWSER_KEY_AND_MTLS',
                'INTEGRATION_TEST',
                @now,
                @correlation_id,
                @user_id,
                @user_id
            );

            INSERT INTO operator_console.operator_device_assignment_history (
                operator_device_assignment_history_id,
                operator_device_binding_id,
                site_group_id,
                site_id,
                assignment_status_code,
                assignment_source_code,
                assignment_reason_code,
                assigned_at,
                assigned_by_user_id,
                effective_from,
                effective_to,
                correlation_id,
                created_by_user_id
            )
            VALUES (
                @operator_device_assignment_history_id,
                @operator_device_binding_id,
                @site_group_id,
                @site_id,
                'ACTIVE',
                'INTEGRATION_TEST',
                'ACCESS_EVALUATION_TEST',
                @now,
                @user_id,
                @effective_from,
                @effective_to,
                @correlation_id,
                @user_id
            );

            INSERT INTO operator_console.operator_shifts (
                operator_shift_id,
                hr_provider_code,
                external_shift_id_hash,
                external_shift_id_masked,
                hr_identity_mapping_id,
                operator_user_id,
                site_group_id,
                site_id,
                scheduled_start_at,
                scheduled_end_at,
                source_imported_at,
                import_status_code,
                source_system_code,
                source_status_code,
                operational_status,
                active_from,
                active_to,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @operator_shift_id,
                'TEST_HR',
                @external_shift_id_hash,
                'SHIFT-****',
                @hr_identity_mapping_id,
                @user_id,
                @site_group_id,
                @site_id,
                @effective_from,
                @effective_to,
                @now,
                'IMPORTED',
                'INTEGRATION_TEST',
                'ACTIVE',
                'ACTIVE',
                @effective_from,
                @effective_to,
                @correlation_id,
                @user_id,
                @user_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddContextParameters(command, context);
        await command.ExecuteNonQueryAsync();
        return context;
    }

    private static async Task<TestAccessEvaluationContext> SeedDeniedContextAsync()
    {
        var site = await ReadActiveSiteAsync();
        var context = TestAccessEvaluationContext.Create(site.SiteGroupId, site.SiteId) with
        {
            OperatorDeviceBindingId = null,
            OperatorShiftId = null
        };

        await SeedIdentityUserAsync(context.UserId, context.CorrelationId);
        return context;
    }

    private static async Task SeedIdentityUserAsync(Guid userId, Guid correlationId)
    {
        const string sql = """
            INSERT INTO identity.users (
                user_id,
                username,
                email,
                email_normalized,
                display_name,
                user_type,
                user_status,
                effective_from,
                effective_to,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @user_id,
                @username,
                @email,
                @email_normalized,
                @display_name,
                'SITE_OPERATOR',
                'ACTIVE',
                @effective_from,
                @effective_to,
                @user_id,
                @user_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
        command.Parameters.Add("username", NpgsqlDbType.Varchar).Value = $"op-access-{correlationId:N}";
        command.Parameters.Add("email", NpgsqlDbType.Varchar).Value = $"op-access-{correlationId:N}@example.test";
        command.Parameters.Add("email_normalized", NpgsqlDbType.Varchar).Value = $"OP-ACCESS-{correlationId:N}@EXAMPLE.TEST";
        command.Parameters.Add("display_name", NpgsqlDbType.Varchar).Value = "Operator Access Evaluation Test User";
        command.Parameters.Add("effective_from", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        command.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2035-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(Guid SiteGroupId, Guid SiteId)> ReadActiveSiteAsync()
    {
        const string sql = """
            SELECT sg.site_group_id, s.site_id
            FROM sites.sites s
            JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
            WHERE s.site_status = 'ACTIVE'
              AND sg.site_group_status = 'ACTIVE'
            ORDER BY s.site_code
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("No active site seed row is available for Operator Console access evaluation persistence tests.");
        }

        return (reader.GetGuid(0), reader.GetGuid(1));
    }

    private static async Task<PersistedEvaluation?> ReadPersistedEvaluationAsync(Guid evaluationId)
    {
        const string evaluationSql = """
            SELECT
                operator_access_evaluation_id,
                correlation_id,
                requested_action,
                evaluation_status::text AS evaluation_status,
                operator_user_id,
                hr_identity_mapping_id,
                operator_device_binding_id,
                operator_shift_id,
                site_group_id,
                site_id,
                evaluated_at
            FROM operator_console.operator_access_evaluations
            WHERE operator_access_evaluation_id = @operator_access_evaluation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(evaluationSql, connection);
        command.Parameters.Add("operator_access_evaluation_id", NpgsqlDbType.Uuid).Value = evaluationId;

        PersistedEvaluation? evaluation = null;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                evaluation = new PersistedEvaluation(
                    reader.GetGuid("operator_access_evaluation_id"),
                    reader.GetNullableGuid("correlation_id"),
                    reader.GetString("requested_action"),
                    reader.GetString("evaluation_status"),
                    reader.GetGuid("operator_user_id"),
                    reader.GetNullableGuid("hr_identity_mapping_id"),
                    reader.GetNullableGuid("operator_device_binding_id"),
                    reader.GetNullableGuid("operator_shift_id"),
                    reader.GetNullableGuid("site_group_id"),
                    reader.GetNullableGuid("site_id"),
                    Array.Empty<PersistedReason>());
            }
        }

        if (evaluation is null)
        {
            return null;
        }

        const string reasonsSql = """
            SELECT reason_code, display_order
            FROM operator_console.operator_access_evaluation_reasons
            WHERE operator_access_evaluation_id = @operator_access_evaluation_id
            ORDER BY display_order, operator_access_evaluation_reason_id;
            """;

        await using var reasonsCommand = new NpgsqlCommand(reasonsSql, connection);
        reasonsCommand.Parameters.Add("operator_access_evaluation_id", NpgsqlDbType.Uuid).Value = evaluationId;
        var reasons = new List<PersistedReason>();
        await using (var reader = await reasonsCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                reasons.Add(new PersistedReason(reader.GetString("reason_code"), reader.GetInt32("display_order")));
            }
        }

        return evaluation with { Reasons = reasons };
    }

    private static void AddContextParameters(NpgsqlCommand command, TestAccessEvaluationContext context)
    {
        var now = DateTimeOffset.UtcNow;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = context.UserId;
        command.Parameters.Add("hr_identity_mapping_id", NpgsqlDbType.Uuid).Value = context.HrIdentityMappingId!.Value;
        command.Parameters.Add("operator_device_binding_id", NpgsqlDbType.Uuid).Value = context.OperatorDeviceBindingId!.Value;
        command.Parameters.Add("operator_device_assignment_history_id", NpgsqlDbType.Uuid).Value = context.OperatorDeviceAssignmentHistoryId!.Value;
        command.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = context.OperatorShiftId!.Value;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = context.SiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = context.SiteId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = context.CorrelationId;
        command.Parameters.Add("external_person_id_hash", NpgsqlDbType.Char).Value = Hash64(context.HrIdentityMappingId.Value);
        command.Parameters.Add("browser_key_thumbprint", NpgsqlDbType.Char).Value = Hash64(context.OperatorDeviceBindingId.Value);
        command.Parameters.Add("external_shift_id_hash", NpgsqlDbType.Char).Value = Hash64(context.OperatorShiftId.Value);
        command.Parameters.Add("device_binding_code", NpgsqlDbType.Varchar).Value = $"OC-PERSIST-{context.OperatorDeviceBindingId.Value:N}";
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("effective_from", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        command.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2035-01-01T00:00:00Z");
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<bool> CanOpenDatabaseAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static string Hash64(Guid value) => value.ToString("N") + value.ToString("N");

    private sealed record TestAccessEvaluationContext(
        Guid UserId,
        Guid? HrIdentityMappingId,
        Guid? OperatorDeviceBindingId,
        Guid? OperatorDeviceAssignmentHistoryId,
        Guid? OperatorShiftId,
        Guid SiteGroupId,
        Guid SiteId,
        Guid ParkingSessionId,
        Guid CorrelationId)
    {
        public static TestAccessEvaluationContext Create(Guid siteGroupId, Guid siteId) =>
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                siteGroupId,
                siteId,
                Guid.NewGuid(),
                Guid.NewGuid());

        public OperatorConsoleAccessEvaluationRequest CreateRequest() =>
            new(
                UserId,
                OperatorDeviceBindingId,
                SiteId,
                SiteGroupId,
                OperatorShiftId,
                "STATUTORY_DISCOUNT_VALIDATION",
                "START_WORKFLOW",
                ParkingSessionId,
                "VIEW_EVIDENCE",
                $"op-access-eval-{CorrelationId:N}",
                CorrelationId);
    }

    private sealed record PersistedEvaluation(
        Guid OperatorAccessEvaluationId,
        Guid? CorrelationId,
        string RequestedAction,
        string EvaluationStatus,
        Guid OperatorUserId,
        Guid? HrIdentityMappingId,
        Guid? OperatorDeviceBindingId,
        Guid? OperatorShiftId,
        Guid? SiteGroupId,
        Guid? SiteId,
        IReadOnlyList<PersistedReason> Reasons);

    private sealed record PersistedReason(string ReasonCode, int DisplayOrder);
}

internal static class OperatorConsoleAccessEvaluationPersistenceDataReaderExtensions
{
    public static Guid GetGuid(this NpgsqlDataReader reader, string columnName) =>
        reader.GetGuid(reader.GetOrdinal(columnName));

    public static int GetInt32(this NpgsqlDataReader reader, string columnName) =>
        reader.GetInt32(reader.GetOrdinal(columnName));

    public static string GetString(this NpgsqlDataReader reader, string columnName) =>
        reader.GetString(reader.GetOrdinal(columnName));

    public static Guid? GetNullableGuid(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }
}
