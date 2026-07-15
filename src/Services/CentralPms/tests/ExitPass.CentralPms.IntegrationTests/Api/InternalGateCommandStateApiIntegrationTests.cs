using System.Net;
using System.Text.Json;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the internal read-only inventory for canonical gate command state records.
/// </summary>
public sealed class InternalGateCommandStateApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    public InternalGateCommandStateApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCommandState_WhenConsumptionHasNoDownstreamRows_ReturnsConsumptionWithNullDownstreamState()
    {
        var fixture = GateCommandStateFixture.Create("consumption-only");
        await PrepareFixtureAsync(fixture);

        try
        {
            using var client = _factory.CreateClient();
            using var response = await GetCommandStateAsync(client, fixture.ConsumptionId, fixture.CorrelationId);
            using var document = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(fixture.ConsumptionId, GetGuid(document.RootElement, "consumption", "gateAuthorizationConsumptionId"));
            Assert.Equal("CONSUMED", GetString(document.RootElement, "consumption", "consumeStatus"));
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("consumedProcessing").ValueKind);
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("gateCommand").ValueKind);
            Assert.Empty(document.RootElement.GetProperty("hikCentralActionAttempts").EnumerateArray());
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GetCommandState_WhenProcessingExists_ReturnsConsumedProcessingRow()
    {
        var fixture = GateCommandStateFixture.Create("processing");
        await PrepareFixtureAsync(fixture);
        await InsertProcessingAsync(fixture, "PROCESSING", "CLAIMED", processedAt: null);

        try
        {
            using var client = _factory.CreateClient();
            using var response = await GetCommandStateAsync(client, fixture.ConsumptionId, fixture.CorrelationId);
            using var document = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var processing = document.RootElement.GetProperty("consumedProcessing");
            Assert.Equal(fixture.ProcessingId, GetGuid(processing, "processingId"));
            Assert.Equal(fixture.ProcessingKey, GetGuid(processing, "processingKey"));
            Assert.Equal(fixture.EventId, GetGuid(processing, "eventId"));
            Assert.Equal("GateAuthorizationConsumed", GetString(processing, "eventType"));
            Assert.Equal("PROCESSING", GetString(processing, "processingStatus"));
            Assert.Equal("CLAIMED", GetString(processing, "processingResult"));
            Assert.Equal(1, processing.GetProperty("attemptCount").GetInt32());
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GetCommandState_WhenRetryableCommandExists_ReturnsRetryFields()
    {
        var fixture = GateCommandStateFixture.Create("retryable");
        await PrepareFixtureAsync(fixture);
        await InsertProcessingAsync(fixture, "PROCESSING", "COMMAND_REQUESTED", processedAt: null);
        await InsertCommandAsync(
            fixture,
            commandStatus: "RETRYABLE",
            attemptCount: 2,
            maxAttempts: 4,
            retryPolicyCode: "GATE_COMMAND_RETRY_TEST",
            nextAttemptAt: fixture.Now.AddMinutes(5),
            terminalFailureAt: null,
            failureCode: "HIKCENTRAL_TIMEOUT",
            failureReason: "Timeout waiting for HikCentral response.",
            lastFailureCode: "TIMEOUT",
            lastFailureReason: "Last attempt timed out.");

        try
        {
            using var client = _factory.CreateClient();
            var before = await ReadCountsAsync(fixture);
            using var response = await GetCommandStateAsync(client, fixture.ConsumptionId, fixture.CorrelationId);
            var after = await ReadCountsAsync(fixture);
            using var document = await ReadJsonAsync(response);

            Assert.Equal(before, after);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var command = document.RootElement.GetProperty("gateCommand");
            Assert.Equal(fixture.CommandId, GetGuid(command, "commandId"));
            Assert.Equal("OPEN_GATE", GetString(command, "commandType"));
            Assert.Equal("RETRYABLE", GetString(command, "commandStatus"));
            Assert.Equal(2, command.GetProperty("attemptCount").GetInt32());
            Assert.Equal(4, command.GetProperty("maxAttempts").GetInt32());
            Assert.Equal("GATE_COMMAND_RETRY_TEST", GetString(command, "retryPolicyCode"));
            Assert.NotEqual(JsonValueKind.Null, command.GetProperty("nextAttemptAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, command.GetProperty("terminalFailureAt").ValueKind);
            Assert.Equal("HIKCENTRAL_TIMEOUT", GetString(command, "failureCode"));
            Assert.Equal("TIMEOUT", GetString(command, "lastFailureCode"));
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GetCommandState_WhenTerminalFailureCommandExists_ReturnsTerminalFailureFields()
    {
        var fixture = GateCommandStateFixture.Create("terminal-failure");
        await PrepareFixtureAsync(fixture);
        await InsertProcessingAsync(fixture, "FAILED", "COMMAND_TERMINAL_FAILURE", processedAt: null, failureCode: "COMMAND_FAILED");
        await InsertCommandAsync(
            fixture,
            commandStatus: "TERMINAL_FAILURE",
            attemptCount: 3,
            maxAttempts: 3,
            retryPolicyCode: "GATE_COMMAND_RETRY_TEST",
            nextAttemptAt: null,
            terminalFailureAt: fixture.Now.AddMinutes(3),
            failureCode: "MAX_ATTEMPTS_EXHAUSTED",
            failureReason: "Command exhausted all retry attempts.",
            lastFailureCode: "VENDOR_UNAVAILABLE",
            lastFailureReason: "HikCentral unavailable.");

        try
        {
            using var client = _factory.CreateClient();
            using var response = await GetCommandStateAsync(client, fixture.ConsumptionId, fixture.CorrelationId);
            using var document = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var command = document.RootElement.GetProperty("gateCommand");
            Assert.Equal("TERMINAL_FAILURE", GetString(command, "commandStatus"));
            Assert.Equal(JsonValueKind.Null, command.GetProperty("nextAttemptAt").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, command.GetProperty("terminalFailureAt").ValueKind);
            Assert.Equal("MAX_ATTEMPTS_EXHAUSTED", GetString(command, "failureCode"));
            Assert.Equal("VENDOR_UNAVAILABLE", GetString(command, "lastFailureCode"));
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GetCommandState_WhenHikCentralAttemptsExist_ReturnsNewestFirstAndNoSecretFields()
    {
        var fixture = GateCommandStateFixture.Create("hikcentral-attempts");
        var olderAuditId = DeterministicGuid(fixture.CorrelationId, 20);
        var newerAuditId = DeterministicGuid(fixture.CorrelationId, 21);
        await PrepareFixtureAsync(fixture);
        await InsertProcessingAsync(fixture, "PROCESSING", "COMMAND_REQUESTED", processedAt: null);
        await InsertCommandAsync(
            fixture,
            commandStatus: "RETRYABLE",
            attemptCount: 1,
            maxAttempts: 3,
            retryPolicyCode: "GATE_COMMAND_RETRY_TEST",
            nextAttemptAt: fixture.Now.AddMinutes(10),
            terminalFailureAt: null,
            failureCode: "RETRYABLE_VENDOR_FAILURE",
            failureReason: "Vendor request will be retried.",
            lastFailureCode: "TIMEOUT",
            lastFailureReason: "Request timed out.");
        await InsertHikCentralAuditAsync(fixture, olderAuditId, fixture.Now.AddSeconds(10), "TIMEOUT", retryable: true);
        await InsertHikCentralAuditAsync(fixture, newerAuditId, fixture.Now.AddSeconds(20), "VENDOR_UNAVAILABLE", retryable: true);

        try
        {
            using var client = _factory.CreateClient();
            using var response = await GetCommandStateAsync(client, fixture.ConsumptionId, fixture.CorrelationId);
            using var document = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var attempts = document.RootElement.GetProperty("hikCentralActionAttempts").EnumerateArray().ToArray();
            Assert.Equal(2, attempts.Length);
            Assert.Equal(newerAuditId, GetGuid(attempts[0], "hikCentralGateActionAuditId"));
            Assert.Equal(olderAuditId, GetGuid(attempts[1], "hikCentralGateActionAuditId"));
            Assert.Equal("HIKCENTRAL", GetString(attempts[0], "vendorCode"));
            Assert.Equal("POST", GetString(attempts[0], "requestMethod"));
            Assert.Equal("VENDOR_UNAVAILABLE", GetString(attempts[0], "actionOutcome"));
            Assert.True(attempts[0].GetProperty("retryable").GetBoolean());
            AssertResponseDoesNotContainSecretFields(document.RootElement);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GetCommandState_WhenConsumptionDoesNotExist_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        using var response = await GetCommandStateAsync(client, Guid.NewGuid(), Guid.NewGuid());
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("GATE_AUTHORIZATION_CONSUMPTION_NOT_FOUND", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> GetCommandStateAsync(
        HttpClient client,
        Guid gateAuthorizationConsumptionId,
        Guid correlationId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/internal/gates/authorization-consumptions/{gateAuthorizationConsumptionId}/command-state");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task PrepareFixtureAsync(GateCommandStateFixture fixture)
    {
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            fixture.PaymentContext,
            "Seed data for gate command state read-only inventory tests");

        var attempt = await CreateAttemptAsync(
            ConnectionString,
            fixture.PaymentContext,
            $"gate-command-state-read-{fixture.CorrelationId:N}",
            "gate-command-state-read-test");

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"gate-state-{fixture.CorrelationId:N}",
            "gate-command-state-read-test",
            fixture.CorrelationId);

        Assert.NotNull(confirmation);

        await EnsureExitAuthorizationAsync(fixture, attempt.PaymentAttemptId, confirmation!.PaymentConfirmationId);
        await InsertConsumptionAsync(fixture);
    }

    private static async Task InsertConsumptionAsync(GateCommandStateFixture fixture)
    {
        const string sql = """
            INSERT INTO gates.gate_authorization_consumptions (
                gate_authorization_consumption_id,
                exit_authorization_id,
                gate_device_id,
                site_id,
                lane_id,
                consume_status,
                requested_at,
                validated_at,
                consumed_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id
            )
            VALUES (
                @consumption_id,
                @exit_authorization_id,
                @gate_device_id,
                @site_id,
                @lane_id,
                'CONSUMED'::gates.gate_authorization_consumption_status_enum,
                @now,
                @now,
                @now,
                @correlation_id,
                @now,
                @service_identity_id,
                @now,
                @service_identity_id
            );
            """;

        await ExecuteAsync(sql, command =>
        {
            AddCommonParameters(command, fixture);
            command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
        });
    }

    private static async Task EnsureExitAuthorizationAsync(
        GateCommandStateFixture fixture,
        Guid paymentAttemptId,
        Guid paymentConfirmationId)
    {
        const string sql = """
            INSERT INTO core.exit_authorizations (
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                payment_confirmation_id,
                authorization_token_hash,
                authorization_status,
                issued_at,
                expires_at,
                invalidated_at,
                invalidation_reason_code,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @exit_authorization_id,
                @parking_session_id,
                @exit_authorization_payment_attempt_id,
                @payment_confirmation_id,
                @authorization_token_hash,
                'ISSUED'::core.exit_authorization_status_enum,
                @now,
                @expires_at,
                NULL,
                NULL,
                @correlation_id,
                @now,
                @service_identity_id,
                @now,
                @service_identity_id,
                1
            )
            ON CONFLICT (exit_authorization_id) DO UPDATE
            SET
                parking_session_id = EXCLUDED.parking_session_id,
                payment_attempt_id = EXCLUDED.payment_attempt_id,
                payment_confirmation_id = EXCLUDED.payment_confirmation_id,
                authorization_status = EXCLUDED.authorization_status,
                expires_at = EXCLUDED.expires_at,
                correlation_id = EXCLUDED.correlation_id,
                updated_at = EXCLUDED.updated_at,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                row_version = core.exit_authorizations.row_version + 1;
            """;

        await ExecuteAsync(sql, command =>
        {
            AddCommonParameters(command, fixture);
            command.Parameters.Add("exit_authorization_payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;
            command.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = paymentConfirmationId;
            command.Parameters.Add("authorization_token_hash", NpgsqlDbType.Char).Value =
                $"{fixture.ExitAuthorizationId:N}{fixture.CorrelationId:N}";
            command.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value = fixture.Now.AddMinutes(15);
        });
    }

    private static async Task InsertProcessingAsync(
        GateCommandStateFixture fixture,
        string processingStatus,
        string processingResult,
        DateTimeOffset? processedAt,
        string? failureCode = null)
    {
        const string sql = """
            INSERT INTO gates.gate_authorization_consumed_processing (
                processing_id,
                processing_key,
                event_id,
                event_type,
                event_ref,
                gate_authorization_consumption_id,
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                service_identity_id,
                lane_id,
                site_id,
                vendor_system_id,
                consumed_at,
                correlation_id,
                processing_status,
                processing_result,
                attempt_count,
                first_attempted_at,
                last_attempted_at,
                processed_at,
                failure_code,
                failure_reason,
                created_at,
                updated_at
            )
            VALUES (
                @processing_id,
                @processing_key,
                @event_id,
                'GateAuthorizationConsumed',
                @event_ref,
                @consumption_id,
                @exit_authorization_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                @gate_device_id,
                @service_identity_id,
                @lane_id,
                @site_id,
                @vendor_system_id,
                @now,
                @correlation_id,
                @processing_status,
                @processing_result,
                1,
                @now,
                @last_attempted_at,
                @processed_at,
                @failure_code,
                @failure_reason,
                @now,
                @now
            );
            """;

        await ExecuteAsync(sql, command =>
        {
            AddCommonParameters(command, fixture);
            command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
            command.Parameters.Add("processing_id", NpgsqlDbType.Uuid).Value = fixture.ProcessingId;
            command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = fixture.ProcessingKey;
            command.Parameters.Add("event_id", NpgsqlDbType.Uuid).Value = fixture.EventId;
            command.Parameters.Add("event_ref", NpgsqlDbType.Varchar).Value = $"event/{fixture.EventId:N}";
            command.Parameters.Add("processing_status", NpgsqlDbType.Varchar).Value = processingStatus;
            command.Parameters.Add("processing_result", NpgsqlDbType.Varchar).Value = processingResult;
            command.Parameters.Add("last_attempted_at", NpgsqlDbType.TimestampTz).Value = fixture.Now.AddMinutes(1);
            command.Parameters.Add("processed_at", NpgsqlDbType.TimestampTz).Value = (object?)processedAt ?? DBNull.Value;
            command.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value = (object?)failureCode ?? DBNull.Value;
            command.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value =
                failureCode is null ? DBNull.Value : "Processing failed for test fixture.";
        });
    }

    private static async Task InsertCommandAsync(
        GateCommandStateFixture fixture,
        string commandStatus,
        int attemptCount,
        int maxAttempts,
        string retryPolicyCode,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset? terminalFailureAt,
        string? failureCode,
        string? failureReason,
        string? lastFailureCode,
        string? lastFailureReason)
    {
        const string sql = """
            INSERT INTO gates.gate_commands (
                command_id,
                command_type,
                source_processing_id,
                source_event_id,
                source_event_ref,
                gate_authorization_consumption_id,
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                service_identity_id,
                lane_id,
                site_id,
                vendor_system_id,
                command_status,
                attempt_count,
                max_attempts,
                retry_policy_code,
                requested_at,
                started_at,
                last_attempted_at,
                next_attempt_at,
                completed_at,
                terminal_failure_at,
                failure_code,
                failure_reason,
                last_failure_code,
                last_failure_reason,
                correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @command_id,
                'OPEN_GATE',
                @processing_id,
                @event_id,
                @event_ref,
                @consumption_id,
                @exit_authorization_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                @gate_device_id,
                @service_identity_id,
                @lane_id,
                @site_id,
                @vendor_system_id,
                @command_status,
                @attempt_count,
                @max_attempts,
                @retry_policy_code,
                @now,
                @started_at,
                @last_attempted_at,
                @next_attempt_at,
                @completed_at,
                @terminal_failure_at,
                @failure_code,
                @failure_reason,
                @last_failure_code,
                @last_failure_reason,
                @correlation_id,
                @now,
                @now
            );
            """;

        await ExecuteAsync(sql, command =>
        {
            AddCommonParameters(command, fixture);
            command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = fixture.CommandId;
            command.Parameters.Add("processing_id", NpgsqlDbType.Uuid).Value = fixture.ProcessingId;
            command.Parameters.Add("event_id", NpgsqlDbType.Uuid).Value = fixture.EventId;
            command.Parameters.Add("event_ref", NpgsqlDbType.Varchar).Value = $"event/{fixture.EventId:N}";
            command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
            command.Parameters.Add("command_status", NpgsqlDbType.Varchar).Value = commandStatus;
            command.Parameters.Add("attempt_count", NpgsqlDbType.Integer).Value = attemptCount;
            command.Parameters.Add("max_attempts", NpgsqlDbType.Integer).Value = maxAttempts;
            command.Parameters.Add("retry_policy_code", NpgsqlDbType.Varchar).Value = retryPolicyCode;
            command.Parameters.Add("started_at", NpgsqlDbType.TimestampTz).Value = fixture.Now.AddMinutes(1);
            command.Parameters.Add("last_attempted_at", NpgsqlDbType.TimestampTz).Value = fixture.Now.AddMinutes(2);
            command.Parameters.Add("next_attempt_at", NpgsqlDbType.TimestampTz).Value = (object?)nextAttemptAt ?? DBNull.Value;
            command.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value = fixture.Now.AddMinutes(3);
            command.Parameters.Add("terminal_failure_at", NpgsqlDbType.TimestampTz).Value = (object?)terminalFailureAt ?? DBNull.Value;
            command.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value = (object?)failureCode ?? DBNull.Value;
            command.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value = (object?)failureReason ?? DBNull.Value;
            command.Parameters.Add("last_failure_code", NpgsqlDbType.Varchar).Value = (object?)lastFailureCode ?? DBNull.Value;
            command.Parameters.Add("last_failure_reason", NpgsqlDbType.Text).Value = (object?)lastFailureReason ?? DBNull.Value;
        });
    }

    private static async Task InsertHikCentralAuditAsync(
        GateCommandStateFixture fixture,
        Guid auditId,
        DateTimeOffset requestedAt,
        string actionOutcome,
        bool retryable)
    {
        const string sql = """
            INSERT INTO gates.hikcentral_gate_action_audits (
                hikcentral_gate_action_audit_id,
                gate_command_id,
                source_processing_id,
                gate_authorization_consumption_id,
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                service_identity_id,
                lane_id,
                site_id,
                vendor_system_id,
                vendor_code,
                vendor_operation,
                door_index_code,
                request_method,
                request_path,
                request_hash,
                signed_header_names,
                request_correlation_id,
                vendor_correlation_id,
                http_status_code,
                vendor_result_code,
                vendor_result_message,
                action_outcome,
                retryable,
                failure_recorded,
                duration_ms,
                timed_out,
                vendor_unavailable,
                transport_failure,
                requested_at,
                responded_at,
                created_at
            )
            VALUES (
                @audit_id,
                @command_id,
                @processing_id,
                @consumption_id,
                @exit_authorization_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                @gate_device_id,
                @service_identity_id,
                @lane_id,
                @site_id,
                @vendor_system_id,
                'HIKCENTRAL',
                'OPEN_GATE',
                'door-test-001',
                'POST',
                '/artemis/api/resource/v1/test-gate-action',
                'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                'accept,content-type,x-ca-timestamp',
                @correlation_id,
                @vendor_correlation_id,
                503,
                @vendor_result_code,
                'Test vendor response metadata',
                @action_outcome,
                @retryable,
                true,
                250,
                @timed_out,
                @vendor_unavailable,
                @transport_failure,
                @requested_at,
                @responded_at,
                @requested_at
            );
            """;

        await ExecuteAsync(sql, command =>
        {
            AddCommonParameters(command, fixture);
            command.Parameters.Add("audit_id", NpgsqlDbType.Uuid).Value = auditId;
            command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = fixture.CommandId;
            command.Parameters.Add("processing_id", NpgsqlDbType.Uuid).Value = fixture.ProcessingId;
            command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
            command.Parameters.Add("vendor_correlation_id", NpgsqlDbType.Varchar).Value = $"vendor-{auditId:N}";
            command.Parameters.Add("vendor_result_code", NpgsqlDbType.Varchar).Value = actionOutcome;
            command.Parameters.Add("action_outcome", NpgsqlDbType.Varchar).Value = actionOutcome;
            command.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = retryable;
            command.Parameters.Add("timed_out", NpgsqlDbType.Boolean).Value = actionOutcome == "TIMEOUT";
            command.Parameters.Add("vendor_unavailable", NpgsqlDbType.Boolean).Value = actionOutcome == "VENDOR_UNAVAILABLE";
            command.Parameters.Add("transport_failure", NpgsqlDbType.Boolean).Value = actionOutcome == "TRANSPORT_FAILURE";
            command.Parameters.Add("requested_at", NpgsqlDbType.TimestampTz).Value = requestedAt;
            command.Parameters.Add("responded_at", NpgsqlDbType.TimestampTz).Value = requestedAt.AddMilliseconds(250);
        });
    }

    private static void AddCommonParameters(NpgsqlCommand command, GateCommandStateFixture fixture)
    {
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = fixture.ExitAuthorizationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = fixture.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = fixture.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = fixture.TariffSnapshotId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = fixture.GateDeviceId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = fixture.ServiceIdentityId;
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = fixture.LaneId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = fixture.SiteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = fixture.VendorSystemId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = fixture.CorrelationId;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = fixture.Now;
    }

    private static async Task<GateCommandStateCounts> ReadCountsAsync(GateCommandStateFixture fixture)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM gates.gate_authorization_consumptions WHERE gate_authorization_consumption_id = @consumption_id) AS consumption_count,
                (SELECT COUNT(*) FROM gates.gate_authorization_consumed_processing WHERE gate_authorization_consumption_id = @consumption_id) AS processing_count,
                (SELECT COUNT(*) FROM gates.gate_commands WHERE gate_authorization_consumption_id = @consumption_id) AS command_count,
                (SELECT COUNT(*) FROM gates.hikcentral_gate_action_audits WHERE gate_authorization_consumption_id = @consumption_id) AS audit_count;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new GateCommandStateCounts(
            reader.GetInt64(reader.GetOrdinal("consumption_count")),
            reader.GetInt64(reader.GetOrdinal("processing_count")),
            reader.GetInt64(reader.GetOrdinal("command_count")),
            reader.GetInt64(reader.GetOrdinal("audit_count")));
    }

    private static async Task CleanupAsync(GateCommandStateFixture fixture)
    {
        const string sql = """
            DELETE FROM gates.hikcentral_gate_action_audits WHERE gate_authorization_consumption_id = @consumption_id;
            DELETE FROM gates.gate_commands WHERE gate_authorization_consumption_id = @consumption_id;
            DELETE FROM gates.gate_authorization_consumed_processing WHERE gate_authorization_consumption_id = @consumption_id;
            DELETE FROM gates.gate_authorization_consumptions WHERE gate_authorization_consumption_id = @consumption_id;
            DELETE FROM core.exit_authorizations WHERE exit_authorization_id = @exit_authorization_id;
            """;

        await ExecuteAsync(sql, command =>
        {
            command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
            command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = fixture.ExitAuthorizationId;
        });

        await PaymentTestDataHelper.CleanupAsync(ConnectionString, fixture.PaymentContext);
    }

    private static async Task ExecuteAsync(string sql, Action<NpgsqlCommand> configure)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);
        await command.ExecuteNonQueryAsync();
    }

    private static Guid GetGuid(JsonElement parent, string objectName, string propertyName) =>
        GetGuid(parent.GetProperty(objectName), propertyName);

    private static Guid GetGuid(JsonElement parent, string propertyName) =>
        parent.GetProperty(propertyName).GetGuid();

    private static string GetString(JsonElement parent, string objectName, string propertyName) =>
        GetString(parent.GetProperty(objectName), propertyName);

    private static string GetString(JsonElement parent, string propertyName) =>
        parent.GetProperty(propertyName).GetString() ?? string.Empty;

    private static void AssertResponseDoesNotContainSecretFields(JsonElement element)
    {
        var forbiddenNames = new[]
        {
            "appKey",
            "appSecret",
            "credential",
            "credentials",
            "signature",
            "authorization",
            "requestHeaders",
            "responseHeaders",
            "requestBody",
            "responseBody",
            "payloadJson"
        };

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(
                    forbiddenNames,
                    forbidden => string.Equals(forbidden, property.Name, StringComparison.OrdinalIgnoreCase));
                AssertResponseDoesNotContainSecretFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertResponseDoesNotContainSecretFields(item);
            }
        }
    }

    private static Guid DeterministicGuid(Guid baseId, int suffix)
    {
        var bytes = baseId.ToByteArray();
        bytes[15] = (byte)suffix;
        return new Guid(bytes);
    }

    private sealed record GateCommandStateFixture(
        Guid ConsumptionId,
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        Guid PaymentConfirmationId,
        Guid TariffSnapshotId,
        Guid GateDeviceId,
        Guid ServiceIdentityId,
        Guid LaneId,
        Guid SiteId,
        Guid VendorSystemId,
        Guid CorrelationId,
        Guid ProcessingId,
        Guid ProcessingKey,
        Guid EventId,
        Guid CommandId,
        DateTimeOffset Now,
        PaymentTestContext PaymentContext)
    {
        public static GateCommandStateFixture Create(string scope)
        {
            var context = PaymentTestContext.Create(scope);
            var correlationId = context.CorrelationId;
            var now = DateTimeOffset.UtcNow.AddSeconds(-10);
            return new GateCommandStateFixture(
                DeterministicGuid(correlationId, 1),
                DeterministicGuid(correlationId, 2),
                context.ParkingSessionId,
                DeterministicGuid(correlationId, 4),
                DeterministicGuid(correlationId, 5),
                context.TariffSnapshotId,
                DeterministicGuid(context.RequestedByUserId, 2),
                context.RequestedByUserId,
                DeterministicGuid(context.SiteId, 1),
                context.SiteId,
                DeterministicGuid(correlationId, 9),
                correlationId,
                DeterministicGuid(correlationId, 10),
                DeterministicGuid(correlationId, 11),
                DeterministicGuid(correlationId, 12),
                DeterministicGuid(correlationId, 13),
                now.AddMilliseconds(Math.Abs(scope.GetHashCode(StringComparison.Ordinal)) % 1000),
                context);
        }
    }

    private sealed record GateCommandStateCounts(
        long ConsumptionCount,
        long ProcessingCount,
        long CommandCount,
        long AuditCount);
}
