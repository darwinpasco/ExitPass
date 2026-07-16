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
/// Verifies controlled fake execution of one canonical gate command.
/// </summary>
public sealed class GateCommandExecutionServiceIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task ExecuteAsync_WhenCommandIsRequestedAndFakeSucceeds_FinalizesSucceededAndWritesOneAudit()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-success");
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var result = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);
            var inventory = await new GateCommandStateReadRepository(ConnectionString)
                .GetByConsumptionIdAsync(fixture.ConsumptionId, CancellationToken.None);

            Assert.Equal(GateCommandExecutionOutcome.Executed, result.Outcome);
            Assert.True(result.AdapterInvoked);
            Assert.Equal(1, adapter.CallCount);
            Assert.Equal("SUCCEEDED", result.CommandStatus);
            Assert.Equal("SUCCEEDED", state.CommandStatus);
            Assert.Equal(1, state.AttemptCount);
            Assert.NotNull(state.CompletedAt);
            Assert.Null(state.NextAttemptAt);
            Assert.Null(state.TerminalFailureAt);
            Assert.Equal(1, state.AuditCount);
            Assert.NotNull(state.Audit);
            Assert.Equal("SUCCEEDED", state.Audit!.ActionOutcome);
            Assert.Equal("HIKCENTRAL", state.Audit.VendorCode);
            Assert.Equal("POST", state.Audit.RequestMethod);
            Assert.Equal(fixture.TargetResourceCode, state.Audit.DoorIndexCode);
            Assert.Contains("__fake__", state.Audit.RequestPath, StringComparison.Ordinal);
            Assert.DoesNotContain("/artemis/api/", state.Audit.RequestPath, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[0-9a-f]{64}$", state.Audit.RequestHash);
            Assert.Equal(string.Empty, state.Audit.SignedHeaderNames);
            Assert.NotEqual("PHYSICAL_GATE_OPENED", state.Audit.ActionOutcome);

            Assert.NotNull(inventory);
            Assert.Equal("SUCCEEDED", inventory!.GateCommand!.CommandStatus);
            Assert.Single(inventory.HikCentralActionAttempts);
            Assert.Equal(state.Audit.HikCentralGateActionAuditId, inventory.HikCentralActionAttempts[0].HikCentralGateActionAuditId);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Theory]
    [InlineData(FakeHikCentralGateActionScenario.RetryableFailure, "RETRYABLE_FAILURE")]
    [InlineData(FakeHikCentralGateActionScenario.Timeout, "TIMEOUT")]
    [InlineData(FakeHikCentralGateActionScenario.VendorUnavailable, "VENDOR_UNAVAILABLE")]
    [InlineData(FakeHikCentralGateActionScenario.TransportFailure, "TRANSPORT_FAILURE")]
    public async Task ExecuteAsync_WhenFakeReturnsRetryableOutcome_FinalizesRetryableWithNextAttempt(
        FakeHikCentralGateActionScenario scenario,
        string expectedOutcome)
    {
        var fixture = await PrepareFixtureWithCommandAsync($"execute-retryable-{scenario}");
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(scenario));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var result = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);
            var inventory = await new GateCommandStateReadRepository(ConnectionString)
                .GetByConsumptionIdAsync(fixture.ConsumptionId, CancellationToken.None);

            Assert.Equal(GateCommandExecutionOutcome.Executed, result.Outcome);
            Assert.Equal("RETRYABLE", result.CommandStatus);
            Assert.Equal(1, adapter.CallCount);
            Assert.Equal("RETRYABLE", state.CommandStatus);
            Assert.NotNull(state.CompletedAt);
            Assert.NotNull(state.NextAttemptAt);
            Assert.Null(state.TerminalFailureAt);
            Assert.Equal(expectedOutcome, state.Audit!.ActionOutcome);
            Assert.True(state.Audit.Retryable);
            Assert.True(state.Audit.FailureRecorded);
            Assert.Equal("RETRYABLE", inventory!.GateCommand!.CommandStatus);
            Assert.NotNull(inventory.GateCommand.NextAttemptAt);
            Assert.Single(inventory.HikCentralActionAttempts);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenFakeReturnsTerminalFailure_FinalizesTerminalFailure()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-terminal");
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.TerminalFailure));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var result = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);
            var inventory = await new GateCommandStateReadRepository(ConnectionString)
                .GetByConsumptionIdAsync(fixture.ConsumptionId, CancellationToken.None);

            Assert.Equal(GateCommandExecutionOutcome.Executed, result.Outcome);
            Assert.Equal("TERMINAL_FAILURE", result.CommandStatus);
            Assert.Equal(1, adapter.CallCount);
            Assert.Equal("TERMINAL_FAILURE", state.CommandStatus);
            Assert.NotNull(state.CompletedAt);
            Assert.NotNull(state.TerminalFailureAt);
            Assert.Null(state.NextAttemptAt);
            Assert.Equal("SIM_TERMINAL_FAILURE", state.FailureCode);
            Assert.Equal("SIM_TERMINAL_FAILURE", state.LastFailureCode);
            Assert.Equal("TERMINAL_FAILURE", state.Audit!.ActionOutcome);
            Assert.Equal("TERMINAL_FAILURE", inventory!.GateCommand!.CommandStatus);
            Assert.NotNull(inventory.GateCommand.TerminalFailureAt);
            Assert.Single(inventory.HikCentralActionAttempts);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenRetryableOutcomeExhaustsAttempts_FinalizesTerminalFailure()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-exhausted");
        await SetCommandAttemptsAsync(fixture.CommandId, attemptCount: 2, maxAttempts: 3);
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Timeout));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var result = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandExecutionOutcome.Executed, result.Outcome);
            Assert.Equal("TERMINAL_FAILURE", result.CommandStatus);
            Assert.Equal(1, adapter.CallCount);
            Assert.Equal(3, state.AttemptCount);
            Assert.Equal("TERMINAL_FAILURE", state.CommandStatus);
            Assert.NotNull(state.TerminalFailureAt);
            Assert.Null(state.NextAttemptAt);
            Assert.Equal("TIMEOUT", state.Audit!.ActionOutcome);
            Assert.True(state.Audit.TimedOut);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandIsMissing_RejectsWithoutAdapterCall()
    {
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success));
        var service = CreateExecutionService(null, adapter);
        var commandId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

        var result = await service.ExecuteAsync(commandId, CancellationToken.None);

        Assert.Equal(GateCommandExecutionOutcome.Rejected, result.Outcome);
        Assert.Equal("GATE_COMMAND_NOT_FOUND", result.ErrorCode);
        Assert.Equal(0, adapter.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandTypeIsUnsupported_RejectsWithoutAdapterCall()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-unsupported-type");
        await UpdateCommandTypeAsync(fixture.CommandId, "CLOSE_GATE");
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var result = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandExecutionOutcome.Rejected, result.Outcome);
            Assert.Equal("GATE_COMMAND_TYPE_UNSUPPORTED", result.ErrorCode);
            Assert.Equal(0, adapter.CallCount);
            Assert.Equal("REQUESTED", state.CommandStatus);
            Assert.Equal(0, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandIsNotRequested_RejectsWithoutAdapterCall()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-non-requested");
        await SetCommandRetryableAsync(fixture.CommandId, fixture.Now.AddMinutes(3));
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var result = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandExecutionOutcome.Rejected, result.Outcome);
            Assert.Equal("GATE_COMMAND_STATUS_NOT_REQUESTED", result.ErrorCode);
            Assert.Equal(0, adapter.CallCount);
            Assert.Equal("RETRYABLE", state.CommandStatus);
            Assert.Equal(0, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSameRequestedCommandIsHandledConcurrently_InvokesAdapterOnce()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-concurrent");
        var adapter = new CountingAdapter(
            new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success),
            TimeSpan.FromMilliseconds(100));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var tasks = Enumerable.Range(0, 6)
                .Select(_ => service.ExecuteAsync(fixture.CommandId, CancellationToken.None))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            var state = await ReadExecutionStateAsync(fixture.CommandId);

            Assert.Equal(1, adapter.CallCount);
            Assert.Equal(1, results.Count(result => result.AdapterInvoked));
            Assert.Equal(1, state.AuditCount);
            Assert.Equal("SUCCEEDED", state.CommandStatus);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandAlreadySucceeded_DoesNotCreateSecondAudit()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-repeat");
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var first = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var second = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandExecutionOutcome.Executed, first.Outcome);
            Assert.Equal(GateCommandExecutionOutcome.AlreadyCompleted, second.Outcome);
            Assert.Equal(1, adapter.CallCount);
            Assert.False(second.AdapterInvoked);
            Assert.Equal(1, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task FinalizeAsync_WhenAuditInsertFails_RollsBackAuditAndLeavesClaimedCommandInProgress()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-finalize-rollback");
        var repository = new GateCommandExecutionRepository(ConnectionString);

        try
        {
            var claimResult = await repository.ClaimAsync(fixture.CommandId, fixture.Now.AddMinutes(3), CancellationToken.None);
            Assert.Equal(GateCommandClaimOutcome.Claimed, claimResult.Outcome);
            var invalidResult = new HikCentralGateActionResult(
                HikCentralGateActionConstants.VendorCode,
                HikCentralGateActionConstants.RequestMethod,
                HikCentralGateActionConstants.OpenGateOperation,
                fixture.TargetResourceCode,
                "INVALID_OUTCOME",
                Retryable: false,
                FailureRecorded: true,
                DurationMs: 1,
                TimedOut: false,
                VendorUnavailable: false,
                TransportFailure: false,
                HttpStatusCode: 500,
                VendorResultCode: "INVALID_OUTCOME",
                VendorResultMessage: "Invalid outcome for rollback proof.",
                fixture.CorrelationId,
                "FAKE-HIKCENTRAL-INVALID",
                fixture.Now.AddMinutes(3),
                fixture.Now.AddMinutes(3).AddMilliseconds(1));

            await Assert.ThrowsAsync<PostgresException>(() =>
                repository.FinalizeAsync(
                    claimResult.Claim!,
                    invalidResult,
                    fixture.Now.AddMinutes(4),
                    TimeSpan.FromMinutes(5),
                    CancellationToken.None));

            var state = await ReadExecutionStateAsync(fixture.CommandId);
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
    public async Task ExecuteAsync_WhenCancelledBeforeClaim_MutatesNothing()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-cancel-before");
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success));
        var service = CreateExecutionService(fixture, adapter);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.ExecuteAsync(fixture.CommandId, cancellation.Token));

            var state = await ReadExecutionStateAsync(fixture.CommandId);
            Assert.Equal("REQUESTED", state.CommandStatus);
            Assert.Equal(0, state.AttemptCount);
            Assert.Equal(0, state.AuditCount);
            Assert.Equal(0, adapter.CallCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledAfterClaim_LeavesInProgressWithoutAudit()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-cancel-after");
        var adapter = new CancellingAdapter();
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.ExecuteAsync(fixture.CommandId, CancellationToken.None));

            var state = await ReadExecutionStateAsync(fixture.CommandId);
            Assert.Equal(1, adapter.CallCount);
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
    public async Task ExecuteAsync_WhenGateDeviceHasNoVendorReference_RejectsWithoutAdapterCall()
    {
        var fixture = await PrepareFixtureWithCommandAsync("execute-missing-target", setVendorReference: false);
        var adapter = new CountingAdapter(new FakeHikCentralGateActionAdapter(FakeHikCentralGateActionScenario.Success));
        var service = CreateExecutionService(fixture, adapter);

        try
        {
            var result = await service.ExecuteAsync(fixture.CommandId, CancellationToken.None);
            var state = await ReadExecutionStateAsync(fixture.CommandId);

            Assert.Equal(GateCommandExecutionOutcome.Rejected, result.Outcome);
            Assert.Equal("GATE_COMMAND_TARGET_RESOURCE_MISSING", result.ErrorCode);
            Assert.Equal(0, adapter.CallCount);
            Assert.Equal("REQUESTED", state.CommandStatus);
            Assert.Equal(0, state.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    private static GateCommandExecutionService CreateExecutionService(
        GateCommandExecutionFixture? fixture,
        IHikCentralGateActionAdapter adapter) =>
        new(
            new GateCommandExecutionRepository(ConnectionString),
            adapter,
            new FixedClock(fixture?.Now.AddMinutes(3) ?? DateTimeOffset.Parse("2026-07-16T00:00:00Z")),
            new GateCommandExecutionOptions(TimeSpan.FromMinutes(5)));

    private static async Task<GateCommandExecutionFixture> PrepareFixtureWithCommandAsync(
        string scope,
        bool setVendorReference = true)
    {
        var fixture = GateCommandExecutionFixture.Create(scope);

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            fixture.PaymentContext,
            "Seed data for gate command execution tests");

        if (setVendorReference)
        {
            await SetGateDeviceVendorReferenceAsync(fixture);
        }

        var attempt = await CreateAttemptAsync(
            ConnectionString,
            fixture.PaymentContext,
            $"gate-command-execute-{fixture.CorrelationId:N}",
            "gate-command-execute-test");

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"gate-execute-{fixture.CorrelationId:N}",
            "gate-command-execute-test",
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

    private static async Task SetGateDeviceVendorReferenceAsync(GateCommandExecutionFixture fixture)
    {
        const string sql = """
            UPDATE gates.gate_devices
            SET vendor_device_ref = @target_resource_code,
                updated_at = @now
            WHERE gate_device_id = @gate_device_id;
            """;

        await ExecuteAsync(sql, command =>
        {
            command.Parameters.Add("target_resource_code", NpgsqlDbType.Varchar).Value = fixture.TargetResourceCode;
            command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = fixture.GateDeviceId;
            command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = fixture.Now;
        });
    }

    private static async Task InsertExitAuthorizationAsync(GateCommandExecutionFixture fixture)
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

    private static async Task InsertConsumptionAsync(GateCommandExecutionFixture fixture)
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

    private static IntegrationEventEnvelope CreateEnvelope(GateCommandExecutionFixture fixture)
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

    private static async Task SetCommandAttemptsAsync(Guid commandId, int attemptCount, int maxAttempts)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET attempt_count = @attempt_count,
                max_attempts = @max_attempts,
                updated_at = @now
            WHERE command_id = @command_id;
            """;

        await ExecuteAsync(sql, command =>
        {
            command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
            command.Parameters.Add("attempt_count", NpgsqlDbType.Integer).Value = attemptCount;
            command.Parameters.Add("max_attempts", NpgsqlDbType.Integer).Value = maxAttempts;
            command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.UtcNow;
        });
    }

    private static async Task UpdateCommandTypeAsync(Guid commandId, string commandType)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET command_type = @command_type,
                updated_at = @now
            WHERE command_id = @command_id;
            """;

        await ExecuteAsync(sql, command =>
        {
            command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
            command.Parameters.Add("command_type", NpgsqlDbType.Varchar).Value = commandType;
            command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.UtcNow;
        });
    }

    private static async Task SetCommandRetryableAsync(Guid commandId, DateTimeOffset at)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET command_status = 'RETRYABLE',
                attempt_count = 1,
                started_at = @at,
                last_attempted_at = @at,
                completed_at = @at,
                next_attempt_at = @next_attempt_at,
                last_failure_code = 'TEST_RETRYABLE',
                last_failure_reason = 'Test retryable state.',
                updated_at = @at
            WHERE command_id = @command_id;
            """;

        await ExecuteAsync(sql, command =>
        {
            command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
            command.Parameters.Add("at", NpgsqlDbType.TimestampTz).Value = at;
            command.Parameters.Add("next_attempt_at", NpgsqlDbType.TimestampTz).Value = at.AddMinutes(5);
        });
    }

    private static async Task<GateCommandExecutionState> ReadExecutionStateAsync(Guid commandId)
    {
        const string sql = """
            SELECT
                gc.command_status,
                gc.attempt_count,
                gc.max_attempts,
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
                audit.vendor_code,
                audit.door_index_code,
                audit.request_method,
                audit.request_path,
                audit.request_hash,
                audit.signed_header_names,
                audit.action_outcome,
                audit.retryable,
                audit.failure_recorded,
                audit.timed_out,
                audit.vendor_unavailable,
                audit.transport_failure
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
        GateCommandAuditState? audit = null;
        if (!reader.IsDBNull(auditIdOrdinal))
        {
            audit = new GateCommandAuditState(
                reader.GetGuid(auditIdOrdinal),
                reader.GetString(reader.GetOrdinal("vendor_code")),
                reader.GetString(reader.GetOrdinal("door_index_code")),
                reader.GetString(reader.GetOrdinal("request_method")),
                reader.GetString(reader.GetOrdinal("request_path")),
                reader.GetString(reader.GetOrdinal("request_hash")),
                reader.GetString(reader.GetOrdinal("signed_header_names")),
                reader.GetString(reader.GetOrdinal("action_outcome")),
                reader.GetBoolean(reader.GetOrdinal("retryable")),
                reader.GetBoolean(reader.GetOrdinal("failure_recorded")),
                reader.GetBoolean(reader.GetOrdinal("timed_out")),
                reader.GetBoolean(reader.GetOrdinal("vendor_unavailable")),
                reader.GetBoolean(reader.GetOrdinal("transport_failure")));
        }

        return new GateCommandExecutionState(
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            reader.GetInt32(reader.GetOrdinal("max_attempts")),
            GetNullableDateTimeOffset(reader, "next_attempt_at"),
            GetNullableDateTimeOffset(reader, "completed_at"),
            GetNullableDateTimeOffset(reader, "terminal_failure_at"),
            GetNullableString(reader, "failure_code"),
            GetNullableString(reader, "last_failure_code"),
            reader.GetInt64(reader.GetOrdinal("audit_count")),
            audit);
    }

    private static async Task CleanupAsync(GateCommandExecutionFixture fixture)
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

    private static void AddFixtureParameters(NpgsqlCommand command, GateCommandExecutionFixture fixture)
    {
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = fixture.ExitAuthorizationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = fixture.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = fixture.PaymentAttemptId;
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

    private sealed class CountingAdapter : IHikCentralGateActionAdapter
    {
        private readonly IHikCentralGateActionAdapter _inner;
        private readonly TimeSpan _delay;
        private int _callCount;

        public CountingAdapter(IHikCentralGateActionAdapter inner, TimeSpan delay = default)
        {
            _inner = inner;
            _delay = delay;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<HikCentralGateActionResult> ExecuteAsync(
            HikCentralGateActionRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return await _inner.ExecuteAsync(request, cancellationToken);
        }
    }

    private sealed class CancellingAdapter : IHikCentralGateActionAdapter
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<HikCentralGateActionResult> ExecuteAsync(
            HikCentralGateActionRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class GateCommandExecutionFixture
    {
        private GateCommandExecutionFixture(
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
            TargetResourceCode = $"HIK-DOOR-{correlationId:N}";
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

        public string TargetResourceCode { get; }

        public PaymentTestContext PaymentContext { get; }

        public static GateCommandExecutionFixture Create(string scope)
        {
            var context = PaymentTestContext.Create(scope);
            var correlationId = context.CorrelationId;
            return new GateCommandExecutionFixture(
                DeterministicGuid(correlationId, 1),
                DeterministicGuid(correlationId, 2),
                DeterministicGuid(context.RequestedByUserId, 2),
                context.RequestedByUserId,
                DeterministicGuid(context.SiteId, 1),
                context.SiteId,
                correlationId,
                DeterministicGuid(correlationId, 3),
                DateTimeOffset.UtcNow.AddSeconds(-10).AddMilliseconds(Math.Abs(scope.GetHashCode(StringComparison.Ordinal)) % 1000),
                context);
        }
    }

    private sealed record GateCommandExecutionState(
        string CommandStatus,
        int AttemptCount,
        int MaxAttempts,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? TerminalFailureAt,
        string? FailureCode,
        string? LastFailureCode,
        long AuditCount,
        GateCommandAuditState? Audit);

    private sealed record GateCommandAuditState(
        Guid HikCentralGateActionAuditId,
        string VendorCode,
        string DoorIndexCode,
        string RequestMethod,
        string RequestPath,
        string RequestHash,
        string SignedHeaderNames,
        string ActionOutcome,
        bool Retryable,
        bool FailureRecorded,
        bool TimedOut,
        bool VendorUnavailable,
        bool TransportFailure);
}
