using System.Reflection;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Gates;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Infrastructure.Gates;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies controlled recovery for stale IN_PROGRESS gate commands.
/// </summary>
public sealed class GateCommandInProgressRecoveryServiceIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task RecoverAsync_WhenStaleInProgressHasAttemptsRemaining_MovesToRetryableWithoutIncrementingAttempt()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-retryable");
        await MarkInProgressAsync(fixture, attemptCount: 1, maxAttempts: 3, lastAttemptedAt: fixture.Now.AddMinutes(-10));
        var service = CreateService(fixture);

        try
        {
            var result = await service.RecoverAsync(
                fixture.CommandId,
                fixture.Now.AddMinutes(-5),
                TimeSpan.FromMinutes(7),
                CancellationToken.None);
            var state = await ReadStateAsync(fixture.CommandId);
            var inventory = await new GateCommandStateReadRepository(ConnectionString)
                .GetByConsumptionIdAsync(fixture.ConsumptionId, CancellationToken.None);

            Assert.Equal(GateCommandRecoveryOutcome.RecoveredRetryable, result.Outcome);
            Assert.True(result.Mutated);
            Assert.Equal("RETRYABLE", result.CommandStatus);
            AssertNearlyEqual(fixture.Now.AddMinutes(10), result.NextAttemptAt);
            Assert.Equal("RETRYABLE", state.CommandStatus);
            Assert.Equal(1, state.AttemptCount);
            AssertNearlyEqual(fixture.Now.AddMinutes(10), state.NextAttemptAt);
            Assert.Null(state.TerminalFailureAt);
            Assert.NotNull(state.CompletedAt);
            Assert.Equal(GateCommandInProgressRecoveryRepository.AbandonedInProgressFailureCode, state.LastFailureCode);
            Assert.Null(state.FailureCode);
            Assert.Equal(0, state.AuditCount);
            Assert.NotNull(inventory);
            Assert.Equal("RETRYABLE", inventory!.GateCommand!.CommandStatus);
            AssertNearlyEqual(fixture.Now.AddMinutes(10), inventory.GateCommand.NextAttemptAt);
            Assert.Empty(inventory.HikCentralActionAttempts);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task RecoverAsync_WhenStaleInProgressHasExhaustedAttempts_MovesToTerminalFailure()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-terminal");
        await MarkInProgressAsync(fixture, attemptCount: 3, maxAttempts: 3, lastAttemptedAt: fixture.Now.AddMinutes(-10));
        var service = CreateService(fixture);

        try
        {
            var result = await service.RecoverAsync(
                fixture.CommandId,
                fixture.Now.AddMinutes(-5),
                TimeSpan.FromMinutes(7),
                CancellationToken.None);
            var state = await ReadStateAsync(fixture.CommandId);
            var inventory = await new GateCommandStateReadRepository(ConnectionString)
                .GetByConsumptionIdAsync(fixture.ConsumptionId, CancellationToken.None);

            Assert.Equal(GateCommandRecoveryOutcome.RecoveredTerminalFailure, result.Outcome);
            Assert.True(result.Mutated);
            Assert.Equal("TERMINAL_FAILURE", result.CommandStatus);
            Assert.Equal("TERMINAL_FAILURE", state.CommandStatus);
            Assert.Equal(3, state.AttemptCount);
            Assert.Null(state.NextAttemptAt);
            AssertNearlyEqual(fixture.Now.AddMinutes(3), state.TerminalFailureAt);
            AssertNearlyEqual(fixture.Now.AddMinutes(3), state.CompletedAt);
            Assert.Equal(GateCommandInProgressRecoveryRepository.AbandonedInProgressFailureCode, state.FailureCode);
            Assert.Equal(GateCommandInProgressRecoveryRepository.AbandonedInProgressFailureCode, state.LastFailureCode);
            Assert.Equal(0, state.AuditCount);
            Assert.Equal("TERMINAL_FAILURE", inventory!.GateCommand!.CommandStatus);
            AssertNearlyEqual(fixture.Now.AddMinutes(3), inventory.GateCommand.TerminalFailureAt);
            Assert.Empty(inventory.HikCentralActionAttempts);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task RecoverAsync_WhenCommandIsMissing_RejectsWithoutMutation()
    {
        var service = CreateService(null);
        var commandId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

        var result = await service.RecoverAsync(
            commandId,
            DateTimeOffset.Parse("2026-07-16T00:00:00Z"),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.Equal(GateCommandRecoveryOutcome.Rejected, result.Outcome);
        Assert.False(result.Mutated);
        Assert.Equal("GATE_COMMAND_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task RecoverAsync_WhenCommandIsRequested_DoesNotRecover()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-requested");
        var service = CreateService(fixture);

        try
        {
            var result = await service.RecoverAsync(
                fixture.CommandId,
                fixture.Now.AddMinutes(5),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            var state = await ReadStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandRecoveryOutcome.Rejected, result.Outcome);
            Assert.Equal("GATE_COMMAND_STATUS_NOT_IN_PROGRESS", result.ErrorCode);
            Assert.Equal("REQUESTED", state.CommandStatus);
            Assert.Equal(0, state.AttemptCount);
            Assert.Equal(0, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task RecoverAsync_WhenInProgressIsFresh_DoesNotRecover()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-fresh");
        await MarkInProgressAsync(fixture, attemptCount: 1, maxAttempts: 3, lastAttemptedAt: fixture.Now.AddMinutes(-1));
        var service = CreateService(fixture);

        try
        {
            var result = await service.RecoverAsync(
                fixture.CommandId,
                fixture.Now.AddMinutes(-5),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            var state = await ReadStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandRecoveryOutcome.Rejected, result.Outcome);
            Assert.Equal("GATE_COMMAND_NOT_STALE", result.ErrorCode);
            Assert.Equal("IN_PROGRESS", state.CommandStatus);
            Assert.Equal(1, state.AttemptCount);
            Assert.Equal(0, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task RecoverAsync_WhenAlreadyRecovered_DoesNotMutateAgain()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-idempotent");
        await MarkInProgressAsync(fixture, attemptCount: 1, maxAttempts: 3, lastAttemptedAt: fixture.Now.AddMinutes(-10));
        var service = CreateService(fixture);

        try
        {
            var first = await service.RecoverAsync(
                fixture.CommandId,
                fixture.Now.AddMinutes(-5),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            var before = await ReadStateAsync(fixture.CommandId);
            var second = await service.RecoverAsync(
                fixture.CommandId,
                fixture.Now.AddMinutes(60),
                TimeSpan.FromMinutes(30),
                CancellationToken.None);
            var after = await ReadStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandRecoveryOutcome.RecoveredRetryable, first.Outcome);
            Assert.Equal(GateCommandRecoveryOutcome.AlreadyRecovered, second.Outcome);
            Assert.False(second.Mutated);
            Assert.Equal(before.NextAttemptAt, after.NextAttemptAt);
            Assert.Equal(before.TerminalFailureAt, after.TerminalFailureAt);
            Assert.Equal(before.AuditCount, after.AuditCount);
            Assert.Equal(1, after.AttemptCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task RecoverAsync_WhenHandledConcurrently_OnlyOneRecoveryMutates()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-concurrent");
        await MarkInProgressAsync(fixture, attemptCount: 1, maxAttempts: 3, lastAttemptedAt: fixture.Now.AddMinutes(-10));
        var service = CreateService(fixture);

        try
        {
            var tasks = Enumerable.Range(0, 6)
                .Select(_ => service.RecoverAsync(
                    fixture.CommandId,
                    fixture.Now.AddMinutes(-5),
                    TimeSpan.FromMinutes(5),
                    CancellationToken.None))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            var state = await ReadStateAsync(fixture.CommandId);

            Assert.Equal(1, results.Count(result => result.Mutated));
            Assert.Equal(5, results.Count(result => result.Outcome == GateCommandRecoveryOutcome.AlreadyRecovered));
            Assert.Equal("RETRYABLE", state.CommandStatus);
            Assert.Equal(1, state.AttemptCount);
            Assert.Equal(0, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task RecoverAsync_WhenCancelledBeforeMutation_MutatesNothing()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-cancel");
        await MarkInProgressAsync(fixture, attemptCount: 1, maxAttempts: 3, lastAttemptedAt: fixture.Now.AddMinutes(-10));
        var service = CreateService(fixture);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.RecoverAsync(
                    fixture.CommandId,
                    fixture.Now.AddMinutes(-5),
                    TimeSpan.FromMinutes(5),
                    cancellation.Token));
            var state = await ReadStateAsync(fixture.CommandId);

            Assert.Equal("IN_PROGRESS", state.CommandStatus);
            Assert.Equal(1, state.AttemptCount);
            Assert.Equal(0, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task RecoverAsync_WhenExistingAuditRowsExist_DoesNotChangeThemOrCreateMore()
    {
        var fixture = await PrepareFixtureWithCommandAsync("recover-existing-audit");
        await MarkInProgressAsync(fixture, attemptCount: 1, maxAttempts: 3, lastAttemptedAt: fixture.Now.AddMinutes(-10));
        var auditId = await InsertExistingAuditAsync(fixture);
        var service = CreateService(fixture);

        try
        {
            var before = await ReadStateAsync(fixture.CommandId);
            var result = await service.RecoverAsync(
                fixture.CommandId,
                fixture.Now.AddMinutes(-5),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            var after = await ReadStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandRecoveryOutcome.RecoveredRetryable, result.Outcome);
            Assert.Equal(1, before.AuditCount);
            Assert.Equal(1, after.AuditCount);
            Assert.NotNull(after.Audit);
            Assert.Equal(auditId, after.Audit!.HikCentralGateActionAuditId);
            Assert.Equal("SUCCEEDED", after.Audit.ActionOutcome);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public void RecoveryService_DoesNotDeclareAdapterNetworkOrPhysicalGateContract()
    {
        var constructorParameters = typeof(GateCommandInProgressRecoveryService)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        var resultPropertyNames = typeof(GateCommandRecoveryResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(typeof(IHikCentralGateActionAdapter), constructorParameters);
        Assert.DoesNotContain(constructorParameters, type => type.Name.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resultPropertyNames, name => name.Contains("Physical", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resultPropertyNames, name => name.Contains("Opened", StringComparison.OrdinalIgnoreCase));
    }

    private static GateCommandInProgressRecoveryService CreateService(GateCommandRecoveryFixture? fixture) =>
        new(
            new GateCommandInProgressRecoveryRepository(ConnectionString),
            new FixedClock(fixture?.Now.AddMinutes(3) ?? DateTimeOffset.Parse("2026-07-16T00:00:00Z")));

    private static async Task<GateCommandRecoveryFixture> PrepareFixtureWithCommandAsync(string scope)
    {
        var fixture = GateCommandRecoveryFixture.Create(scope);

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            fixture.PaymentContext,
            "Seed data for gate command recovery tests");

        var attempt = await CreateAttemptAsync(
            ConnectionString,
            fixture.PaymentContext,
            $"gate-command-recover-{fixture.CorrelationId:N}",
            "gate-command-recover-test");

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"gate-recover-{fixture.CorrelationId:N}",
            "gate-command-recover-test",
            fixture.CorrelationId);

        Assert.NotNull(confirmation);

        fixture.PaymentAttemptId = attempt.PaymentAttemptId;
        fixture.TariffSnapshotId = attempt.TariffSnapshotId;
        fixture.PaymentConfirmationId = confirmation!.PaymentConfirmationId;
        fixture.VendorSystemId = await ReadVendorSystemIdAsync(fixture.PaymentContext.VendorSystemCode);

        await InsertExitAuthorizationAsync(fixture);
        await InsertConsumptionAsync(fixture);

        var creationService = new GateCommandCreationService(
            new GateCommandCreationRepository(ConnectionString),
            new FixedClock(fixture.Now.AddMinutes(2)));
        var creation = await creationService.CreateFromConsumedEventAsync(CreateEnvelope(fixture), CancellationToken.None);
        fixture.ProcessingId = creation.ProcessingId;
        fixture.CommandId = creation.CommandId;

        return fixture;
    }

    private static async Task MarkInProgressAsync(
        GateCommandRecoveryFixture fixture,
        int attemptCount,
        int maxAttempts,
        DateTimeOffset lastAttemptedAt)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET
                command_status = 'IN_PROGRESS',
                attempt_count = @attempt_count,
                max_attempts = @max_attempts,
                started_at = @started_at,
                last_attempted_at = @last_attempted_at,
                next_attempt_at = NULL,
                completed_at = NULL,
                terminal_failure_at = NULL,
                failure_code = NULL,
                failure_reason = NULL,
                last_failure_code = NULL,
                last_failure_reason = NULL,
                updated_at = @last_attempted_at
            WHERE command_id = @command_id;
            """;

        await ExecuteAsync(sql, command =>
        {
            command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = fixture.CommandId;
            command.Parameters.Add("attempt_count", NpgsqlDbType.Integer).Value = attemptCount;
            command.Parameters.Add("max_attempts", NpgsqlDbType.Integer).Value = maxAttempts;
            command.Parameters.Add("started_at", NpgsqlDbType.TimestampTz).Value = lastAttemptedAt;
            command.Parameters.Add("last_attempted_at", NpgsqlDbType.TimestampTz).Value = lastAttemptedAt;
        });
    }

    private static async Task InsertExitAuthorizationAsync(GateCommandRecoveryFixture fixture)
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
                @payment_attempt_id,
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
            );
            """;

        await ExecuteAsync(sql, command =>
        {
            AddFixtureParameters(command, fixture);
            command.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = fixture.PaymentConfirmationId;
            command.Parameters.Add("authorization_token_hash", NpgsqlDbType.Char).Value =
                $"{fixture.ExitAuthorizationId:N}{fixture.CorrelationId:N}";
            command.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value = fixture.Now.AddMinutes(15);
        });
    }

    private static async Task InsertConsumptionAsync(GateCommandRecoveryFixture fixture)
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
            AddFixtureParameters(command, fixture);
            command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
        });
    }

    private static IntegrationEventEnvelope CreateEnvelope(GateCommandRecoveryFixture fixture)
    {
        return new IntegrationEventEnvelope
        {
            EventId = fixture.EventId,
            EventType = IntegrationEventTypes.GateAuthorizationConsumed,
            OccurredAtUtc = fixture.Now,
            CorrelationId = fixture.CorrelationId,
            AggregateId = fixture.ExitAuthorizationId.ToString(),
            AggregateType = "ExitAuthorization",
            Payload = new GateAuthorizationConsumedPayload
            {
                ExitAuthorizationId = fixture.ExitAuthorizationId,
                GateAuthorizationConsumptionId = fixture.ConsumptionId,
                ParkingSessionId = fixture.ParkingSessionId,
                PaymentAttemptId = fixture.PaymentAttemptId,
                TariffSnapshotId = fixture.TariffSnapshotId,
                GateDeviceId = fixture.GateDeviceId,
                GateDeviceIdentifier = $"gate-{fixture.GateDeviceId:N}",
                LaneId = fixture.LaneId,
                SiteId = fixture.SiteId,
                VendorSystemId = fixture.VendorSystemId,
                AuthorizationStatus = "CONSUMED",
                ConsumedAtUtc = fixture.Now,
                CorrelationId = fixture.CorrelationId
            }
        };
    }

    private static async Task<Guid> InsertExistingAuditAsync(GateCommandRecoveryFixture fixture)
    {
        const string sql = """
            INSERT INTO gates.hikcentral_gate_action_audits (
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
                'RECOVERY-EXISTING-AUDIT',
                'POST',
                '/__fake__/existing-audit',
                'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
                '',
                @correlation_id,
                'FAKE-HIKCENTRAL-EXISTING',
                200,
                '0',
                'Existing audit row.',
                'SUCCEEDED',
                false,
                false,
                25,
                false,
                false,
                false,
                @now,
                @responded_at,
                @now
            )
            RETURNING hikcentral_gate_action_audit_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddFixtureParameters(command, fixture);
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = fixture.CommandId;
        command.Parameters.Add("processing_id", NpgsqlDbType.Uuid).Value = fixture.ProcessingId;
        command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = fixture.ConsumptionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = fixture.VendorSystemId;
        command.Parameters.Add("responded_at", NpgsqlDbType.TimestampTz).Value = fixture.Now.AddMilliseconds(25);

        var result = await command.ExecuteScalarAsync();
        return (Guid)result!;
    }

    private static async Task<Guid> ReadVendorSystemIdAsync(string vendorSystemCode)
    {
        const string sql = """
            SELECT vendor_system_id
            FROM integration.vendor_systems
            WHERE vendor_code = @vendor_system_code
              AND environment_code = 'TEST';
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("vendor_system_code", NpgsqlDbType.Varchar).Value = vendorSystemCode;

        var result = await command.ExecuteScalarAsync();
        Assert.NotNull(result);
        return (Guid)result!;
    }

    private static async Task<GateCommandRecoveryState> ReadStateAsync(Guid commandId)
    {
        const string sql = """
            SELECT
                gc.command_status,
                gc.attempt_count,
                gc.next_attempt_at,
                gc.completed_at,
                gc.terminal_failure_at,
                gc.failure_code,
                gc.last_failure_code,
                (
                    SELECT COUNT(*)
                    FROM gates.hikcentral_gate_action_audits AS audit
                    WHERE audit.gate_command_id = gc.command_id
                ) AS audit_count,
                audit.hikcentral_gate_action_audit_id,
                audit.action_outcome
            FROM gates.gate_commands AS gc
            LEFT JOIN LATERAL (
                SELECT *
                FROM gates.hikcentral_gate_action_audits AS audit
                WHERE audit.gate_command_id = gc.command_id
                ORDER BY audit.requested_at DESC, audit.hikcentral_gate_action_audit_id DESC
                LIMIT 1
            ) AS audit ON true
            WHERE gc.command_id = @command_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        var auditIdOrdinal = reader.GetOrdinal("hikcentral_gate_action_audit_id");
        GateCommandRecoveryAuditState? audit = null;
        if (!reader.IsDBNull(auditIdOrdinal))
        {
            audit = new GateCommandRecoveryAuditState(
                reader.GetGuid(auditIdOrdinal),
                reader.GetString(reader.GetOrdinal("action_outcome")));
        }

        return new GateCommandRecoveryState(
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            GetNullableDateTimeOffset(reader, "next_attempt_at"),
            GetNullableDateTimeOffset(reader, "completed_at"),
            GetNullableDateTimeOffset(reader, "terminal_failure_at"),
            GetNullableString(reader, "failure_code"),
            GetNullableString(reader, "last_failure_code"),
            reader.GetInt64(reader.GetOrdinal("audit_count")),
            audit);
    }

    private static async Task CleanupAsync(GateCommandRecoveryFixture fixture)
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

    private static void AddFixtureParameters(NpgsqlCommand command, GateCommandRecoveryFixture fixture)
    {
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = fixture.ExitAuthorizationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = fixture.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = fixture.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = fixture.TariffSnapshotId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = fixture.GateDeviceId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = fixture.ServiceIdentityId;
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = fixture.LaneId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = fixture.SiteId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = fixture.CorrelationId;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = fixture.Now;
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static void AssertNearlyEqual(DateTimeOffset expected, DateTimeOffset? actual)
    {
        Assert.NotNull(actual);
        Assert.True(
            (actual.Value - expected).Duration() < TimeSpan.FromMilliseconds(1),
            $"Expected {expected:O}, actual {actual.Value:O}.");
    }

    private static Guid DeterministicGuid(Guid baseId, int suffix)
    {
        var bytes = baseId.ToByteArray();
        bytes[15] = (byte)suffix;
        return new Guid(bytes);
    }

    private sealed class FixedClock : ISystemClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class GateCommandRecoveryFixture
    {
        private GateCommandRecoveryFixture(
            Guid consumptionId,
            Guid exitAuthorizationId,
            Guid gateDeviceId,
            Guid serviceIdentityId,
            Guid laneId,
            Guid siteId,
            Guid correlationId,
            Guid eventId,
            DateTimeOffset now,
            PaymentTestContext paymentContext)
        {
            ConsumptionId = consumptionId;
            ExitAuthorizationId = exitAuthorizationId;
            GateDeviceId = gateDeviceId;
            ServiceIdentityId = serviceIdentityId;
            LaneId = laneId;
            SiteId = siteId;
            CorrelationId = correlationId;
            EventId = eventId;
            Now = now;
            PaymentContext = paymentContext;
            ParkingSessionId = paymentContext.ParkingSessionId;
        }

        public Guid ConsumptionId { get; }

        public Guid ExitAuthorizationId { get; }

        public Guid ParkingSessionId { get; }

        public Guid PaymentAttemptId { get; set; }

        public Guid PaymentConfirmationId { get; set; }

        public Guid TariffSnapshotId { get; set; }

        public Guid GateDeviceId { get; }

        public Guid ServiceIdentityId { get; }

        public Guid LaneId { get; }

        public Guid SiteId { get; }

        public Guid VendorSystemId { get; set; }

        public Guid CorrelationId { get; }

        public Guid EventId { get; }

        public Guid ProcessingId { get; set; }

        public Guid CommandId { get; set; }

        public DateTimeOffset Now { get; }

        public PaymentTestContext PaymentContext { get; }

        public static GateCommandRecoveryFixture Create(string scope)
        {
            var context = PaymentTestContext.Create(scope);
            var correlationId = context.CorrelationId;
            return new GateCommandRecoveryFixture(
                DeterministicGuid(correlationId, 1),
                DeterministicGuid(correlationId, 2),
                DeterministicGuid(context.RequestedByUserId, 2),
                context.RequestedByUserId,
                DeterministicGuid(context.SiteId, 1),
                context.SiteId,
                correlationId,
                DeterministicGuid(correlationId, 3),
                DateTimeOffset.UtcNow.AddSeconds(-20).AddMilliseconds(Math.Abs(scope.GetHashCode(StringComparison.Ordinal)) % 1000),
                context);
        }
    }

    private sealed record GateCommandRecoveryState(
        string CommandStatus,
        int AttemptCount,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? TerminalFailureAt,
        string? FailureCode,
        string? LastFailureCode,
        long AuditCount,
        GateCommandRecoveryAuditState? Audit);

    private sealed record GateCommandRecoveryAuditState(
        Guid HikCentralGateActionAuditId,
        string ActionOutcome);
}
