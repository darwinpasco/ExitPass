using System.Data;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Infrastructure.GateExit;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.IntegrationTests.GateExit;

public sealed class GateAuthorizationConsumedProcessingInboxIntegrationTests : IAsyncLifetime
{
    private static readonly Guid EventId = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("c2000000-0000-0000-0000-000000000001");
    private static readonly Guid GateAuthorizationConsumptionId = Guid.Parse("c3000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("c4000000-0000-0000-0000-000000000001");
    private static readonly Guid PaymentAttemptId = Guid.Parse("c5000000-0000-0000-0000-000000000001");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("c6000000-0000-0000-0000-000000000001");
    private static readonly Guid OriginalSupersededTariffSnapshotId = Guid.Parse("c6000000-0000-0000-0000-000000000099");
    private static readonly Guid NoDiscountTariffSnapshotId = Guid.Parse("c6000000-0000-0000-0000-000000000002");
    private static readonly Guid GateDeviceId = Guid.Parse("c7000000-0000-0000-0000-000000000001");
    private static readonly Guid LaneId = Guid.Parse("c8000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("c9000000-0000-0000-0000-000000000001");
    private static readonly Guid VendorSystemId = Guid.Parse("ca000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("cb000000-0000-0000-0000-000000000001");

    private readonly string _connectionString = GetConnectionString();

    public async Task InitializeAsync()
    {
        await DeleteProcessingRowsAsync();
        await SeedLockedGateScopeAsync();
    }

    public async Task DisposeAsync()
    {
        await DeleteProcessingRowsAsync();
    }

    [Fact]
    public async Task ValidHandoff_IsProcessedDurably_AndDuplicateDoesNotInvokeAdapter()
    {
        var adapter = new CapturingAdapter();
        var handler = CreateHandler(adapter);
        var handoff = CreateHandoff(EventId, AppliedTariffSnapshotId);

        var first = await handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None);
        var second = await handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None);

        Assert.Equal("GATE_AUTHORIZATION_CONSUMED_PROCESSED", first.ResultCode);
        Assert.True(first.AdapterInvoked);
        Assert.Equal("GATE_AUTHORIZATION_CONSUMED_ALREADY_PROCESSED", second.ResultCode);
        Assert.False(second.AdapterInvoked);
        Assert.True(second.AlreadyProcessed);
        Assert.Equal(1, adapter.CallCount);

        var row = await ReadProcessingRowAsync(EventId);
        Assert.NotNull(row);
        Assert.Equal("PROCESSED", row!.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(AppliedTariffSnapshotId, row.TariffSnapshotId);
        Assert.Equal(GateAuthorizationConsumptionId, row.GateAuthorizationConsumptionId);
        Assert.Null(row.LastFailureCode);

        var command = await ReadCommandRowAsync(EventId);
        Assert.NotNull(command);
        Assert.Equal("SUCCEEDED", command!.Status);
        Assert.Equal(1, command.AttemptCount);
        Assert.Equal(GateCommandRetryPolicy.Default.MaxAttempts, command.MaxAttempts);
        Assert.Equal(GateCommandRetryPolicy.Default.PolicyCode, command.RetryPolicyCode);
        Assert.Equal(AppliedTariffSnapshotId, command.TariffSnapshotId);
        Assert.Equal(GateAuthorizationConsumptionId, command.GateAuthorizationConsumptionId);
        Assert.NotNull(command.StartedAt);
        Assert.NotNull(command.CompletedAt);
        Assert.Null(command.NextAttemptAt);
        Assert.Null(command.TerminalFailureAt);
        Assert.Null(command.FailureCode);
    }

    [Fact]
    public async Task AppliedEffectiveTariffIdentity_IsPersistedWithoutOriginalSnapshot()
    {
        var adapter = new CapturingAdapter();
        var handler = CreateHandler(adapter);
        var handoff = CreateHandoff(Guid.Parse("c1000000-0000-0000-0000-000000000002"), AppliedTariffSnapshotId);

        var result = await handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None);

        Assert.Equal(AppliedTariffSnapshotId, result.TariffSnapshotId);
        Assert.NotEqual(OriginalSupersededTariffSnapshotId, result.TariffSnapshotId);
        Assert.Equal(AppliedTariffSnapshotId, adapter.LastHandoff?.TariffSnapshotId);

        var row = await ReadProcessingRowAsync(handoff.EventId);
        Assert.NotNull(row);
        Assert.Equal(AppliedTariffSnapshotId, row!.TariffSnapshotId);
        Assert.NotEqual(OriginalSupersededTariffSnapshotId, row.TariffSnapshotId);

        var command = await ReadCommandRowAsync(handoff.EventId);
        Assert.NotNull(command);
        Assert.Equal(AppliedTariffSnapshotId, command!.TariffSnapshotId);
        Assert.NotEqual(OriginalSupersededTariffSnapshotId, command.TariffSnapshotId);
    }

    [Fact]
    public async Task NoDiscountHandoff_IsProcessedDurably()
    {
        var adapter = new CapturingAdapter();
        var handler = CreateHandler(adapter);
        var handoff = CreateHandoff(Guid.Parse("c1000000-0000-0000-0000-000000000003"), NoDiscountTariffSnapshotId);

        var result = await handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None);

        Assert.Equal("GATE_AUTHORIZATION_CONSUMED_PROCESSED", result.ResultCode);
        Assert.Equal(NoDiscountTariffSnapshotId, result.TariffSnapshotId);
        var row = await ReadProcessingRowAsync(handoff.EventId);
        Assert.NotNull(row);
        Assert.Equal("PROCESSED", row!.Status);
        Assert.Equal(NoDiscountTariffSnapshotId, row.TariffSnapshotId);
    }

    [Fact]
    public async Task AdapterFailure_IsRecordedAndRetryCanProcess()
    {
        var adapter = new CapturingAdapter
        {
            Failure = new InvalidOperationException("no-op adapter failed")
        };
        var handler = CreateHandler(adapter);
        var handoff = CreateHandoff(Guid.Parse("c1000000-0000-0000-0000-000000000004"), AppliedTariffSnapshotId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None));

        Assert.Equal("no-op adapter failed", ex.Message);
        var failed = await ReadProcessingRowAsync(handoff.EventId);
        Assert.NotNull(failed);
        Assert.Equal("FAILED", failed!.Status);
        Assert.Equal("GATE_HANDOFF_ADAPTER_FAILED", failed.LastFailureCode);
        Assert.Null(failed.ProcessedAt);
        var failedCommand = await ReadCommandRowAsync(handoff.EventId);
        Assert.NotNull(failedCommand);
        Assert.Equal("RETRYABLE", failedCommand!.Status);
        Assert.Equal(1, failedCommand.AttemptCount);
        Assert.Equal(GateCommandRetryPolicy.Default.MaxAttempts, failedCommand.MaxAttempts);
        Assert.Equal("GATE_HANDOFF_ADAPTER_FAILED", failedCommand.FailureCode);
        Assert.Equal("GATE_HANDOFF_ADAPTER_FAILED", failedCommand.LastFailureCode);
        Assert.NotNull(failedCommand.NextAttemptAt);
        Assert.Null(failedCommand.TerminalFailureAt);

        adapter.Failure = null;
        var retry = await handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None);

        Assert.True(retry.AdapterInvoked);
        Assert.Equal(2, adapter.CallCount);
        var processed = await ReadProcessingRowAsync(handoff.EventId);
        Assert.NotNull(processed);
        Assert.Equal("PROCESSED", processed!.Status);
        Assert.Equal(2, processed.AttemptCount);
        Assert.NotNull(processed.ProcessedAt);
        var processedCommand = await ReadCommandRowAsync(handoff.EventId);
        Assert.NotNull(processedCommand);
        Assert.Equal("SUCCEEDED", processedCommand!.Status);
        Assert.Equal(2, processedCommand.AttemptCount);
        Assert.Equal(AppliedTariffSnapshotId, processedCommand.TariffSnapshotId);
        Assert.Null(processedCommand.NextAttemptAt);
        Assert.Null(processedCommand.TerminalFailureAt);
        Assert.Null(processedCommand.FailureCode);
    }

    [Fact]
    public async Task AdapterFailure_WhenRetriesAreExhausted_PersistsTerminalFailureAndBlocksFurtherAdapterInvocation()
    {
        var adapter = new CapturingAdapter
        {
            Failure = new InvalidOperationException("no-op adapter failed")
        };
        var handler = CreateHandler(adapter);
        var handoff = CreateHandoff(Guid.Parse("c1000000-0000-0000-0000-000000000006"), AppliedTariffSnapshotId);

        for (var attempt = 1; attempt <= GateCommandRetryPolicy.Default.MaxAttempts; attempt++)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None));
            Assert.Equal("no-op adapter failed", ex.Message);
        }

        var terminal = await ReadCommandRowAsync(handoff.EventId);
        Assert.NotNull(terminal);
        Assert.Equal("TERMINAL_FAILURE", terminal!.Status);
        Assert.Equal(GateCommandRetryPolicy.Default.MaxAttempts, terminal.AttemptCount);
        Assert.Equal("GATE_HANDOFF_ADAPTER_FAILED", terminal.LastFailureCode);
        Assert.NotNull(terminal.TerminalFailureAt);
        Assert.Null(terminal.NextAttemptAt);
        Assert.Equal(AppliedTariffSnapshotId, terminal.TariffSnapshotId);

        adapter.Failure = null;
        var duplicate = await handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None);

        Assert.Equal("GATE_AUTHORIZATION_CONSUMED_COMMAND_TERMINAL_FAILURE", duplicate.ResultCode);
        Assert.False(duplicate.AdapterInvoked);
        Assert.Equal(GateCommandRetryPolicy.Default.MaxAttempts, adapter.CallCount);
        var afterDuplicate = await ReadCommandRowAsync(handoff.EventId);
        Assert.NotNull(afterDuplicate);
        Assert.Equal("TERMINAL_FAILURE", afterDuplicate!.Status);
    }

    [Fact]
    public async Task ScopeFailure_IsPersistedWithoutAdapterInvocation()
    {
        var adapter = new CapturingAdapter();
        var handler = CreateHandler(
            adapter,
            GateAuthorizationConsumedScopeValidationResult.Invalid(
                "GATE_DEVICE_NOT_FOUND",
                "Gate device was not found."));
        var handoff = CreateHandoff(Guid.Parse("c1000000-0000-0000-0000-000000000005"), AppliedTariffSnapshotId);

        var ex = await Assert.ThrowsAsync<GateAuthorizationConsumedHandoffException>(() =>
            handler.HandleAsync(new ProcessGateAuthorizationConsumedCommand(handoff), CancellationToken.None));

        Assert.Equal("GATE_DEVICE_NOT_FOUND", ex.ErrorCode);
        Assert.Equal(0, adapter.CallCount);

        var row = await ReadProcessingRowAsync(handoff.EventId);
        Assert.NotNull(row);
        Assert.Equal("FAILED", row!.Status);
        Assert.Equal("GATE_DEVICE_NOT_FOUND", row.LastFailureCode);
        Assert.Null(row.ProcessedAt);

        Assert.Null(await ReadCommandRowAsync(handoff.EventId));
    }

    private GateAuthorizationConsumedHandoffHandler CreateHandler(
        CapturingAdapter adapter,
        GateAuthorizationConsumedScopeValidationResult? scopeResult = null)
    {
        return new GateAuthorizationConsumedHandoffHandler(
            adapter,
            new PostgresGateCommandLifecycleRecorder(BuildConfiguration()),
            new PostgresGateAuthorizationConsumedProcessingRecorder(BuildConfiguration()),
            new StaticScopeValidator(scopeResult ?? GateAuthorizationConsumedScopeValidationResult.Valid()));
    }

    private IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = _connectionString
            })
            .Build();
    }

    private static GateAuthorizationConsumedHandoff CreateHandoff(Guid eventId, Guid tariffSnapshotId)
    {
        return new GateAuthorizationConsumedHandoff(
            eventId,
            SourceEventRef: $"central-pms://integration-events/{eventId}",
            ExitAuthorizationId,
            GateAuthorizationConsumptionId,
            ParkingSessionId,
            PaymentAttemptId,
            tariffSnapshotId,
            GateDeviceId,
            GateDeviceIdentifier: "exit-gate-01",
            LaneId,
            SiteId,
            VendorSystemId,
            ConsumedAtUtc: DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            CorrelationId);
    }

    private async Task DeleteProcessingRowsAsync()
    {
        const string sql = """
            DELETE FROM gates.gate_events
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id;

            DELETE FROM gates.gate_authorization_consumptions
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var processingDelete = new NpgsqlCommand(sql, connection);
        processingDelete.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            GateAuthorizationConsumptionId;
        await processingDelete.ExecuteNonQueryAsync();
    }

    private async Task SeedLockedGateScopeAsync()
    {
        const string sql = """
            INSERT INTO identity.service_identities (
                service_identity_id,
                service_identity_code,
                service_identity_name,
                identity_type,
                identity_status,
                owning_service_name,
                effective_from
            )
            VALUES (
                'cd000000-0000-0000-0000-000000000001',
                'gate-integration-service-test',
                'Gate Integration Service Test',
                'INTERNAL_SERVICE',
                'ACTIVE',
                'ExitPass.GateIntegrationService.Tests',
                '2026-01-01T00:00:00Z'
            )
            ON CONFLICT (service_identity_id) DO NOTHING;

            INSERT INTO sites.site_groups (
                site_group_id,
                site_group_code,
                site_group_name,
                timezone_name,
                default_currency_code,
                site_group_status,
                effective_from,
                created_by_service_identity_id
            )
            VALUES (
                'ce000000-0000-0000-0000-000000000001',
                'gate-test-group',
                'Gate Test Group',
                'Asia/Manila',
                'PHP',
                'ACTIVE',
                '2026-01-01T00:00:00Z',
                'cd000000-0000-0000-0000-000000000001'
            )
            ON CONFLICT (site_group_id) DO NOTHING;

            INSERT INTO sites.sites (
                site_id,
                site_group_id,
                site_code,
                site_name,
                site_type,
                timezone_name,
                country_code,
                site_status,
                effective_from,
                created_by_service_identity_id
            )
            VALUES (
                @site_id,
                'ce000000-0000-0000-0000-000000000001',
                'gate-test-site',
                'Gate Test Site',
                'MALL_PARKING',
                'Asia/Manila',
                'PH',
                'ACTIVE',
                '2026-01-01T00:00:00Z',
                'cd000000-0000-0000-0000-000000000001'
            )
            ON CONFLICT (site_id) DO NOTHING;

            INSERT INTO sites.lanes (
                lane_id,
                site_id,
                lane_code,
                lane_name,
                lane_type,
                lane_direction,
                lane_status,
                effective_from,
                created_by_service_identity_id
            )
            VALUES (
                @lane_id,
                @site_id,
                'exit-lane-01',
                'Exit Lane 01',
                'EXIT',
                'OUTBOUND',
                'ACTIVE',
                '2026-01-01T00:00:00Z',
                'cd000000-0000-0000-0000-000000000001'
            )
            ON CONFLICT (lane_id) DO NOTHING;

            INSERT INTO gates.gate_devices (
                gate_device_id,
                site_id,
                lane_id,
                device_code,
                device_name,
                device_type,
                device_status,
                created_by_service_identity_id
            )
            VALUES (
                @gate_device_id,
                @site_id,
                @lane_id,
                'exit-gate-01',
                'Exit Gate 01',
                'BARRIER_CONTROLLER',
                'ACTIVE',
                'cd000000-0000-0000-0000-000000000001'
            )
            ON CONFLICT (gate_device_id) DO NOTHING;

            INSERT INTO gates.gate_authorization_consumptions (
                gate_authorization_consumption_id,
                authorization_token_hash,
                gate_device_id,
                site_id,
                lane_id,
                consume_status,
                consume_reason_code,
                requested_at,
                validated_at,
                consumed_at,
                command_requested,
                command_result_status,
                correlation_id,
                created_by_service_identity_id
            )
            VALUES (
                @gate_authorization_consumption_id,
                repeat('a', 64),
                @gate_device_id,
                @site_id,
                @lane_id,
                'CONSUMED',
                'EXIT_AUTHORIZATION_CONSUMED',
                '2026-05-31T08:00:00Z',
                '2026-05-31T08:00:00Z',
                '2026-05-31T08:00:00Z',
                FALSE,
                NULL,
                @correlation_id,
                'cd000000-0000-0000-0000-000000000001'
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            GateAuthorizationConsumptionId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = GateDeviceId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = SiteId;
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = LaneId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = CorrelationId;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<ProcessingRow?> ReadProcessingRowAsync(Guid processingKey)
    {
        const string sql = """
            SELECT
                gac.gate_authorization_consumption_id,
                @tariff_snapshot_id::uuid AS tariff_snapshot_id,
                CASE
                    WHEN gac.command_result_status = 'OPENED' THEN 'PROCESSED'
                    WHEN gac.failure_detail IS NOT NULL OR gac.command_result_status IN ('FAILED', 'TIMEOUT', 'UNKNOWN') THEN 'FAILED'
                    ELSE 'PROCESSING'
                END AS processing_status,
                GREATEST(COALESCE(command_attempts.attempt_count, 0), 1)::integer AS attempt_count,
                CASE WHEN gac.command_result_status = 'OPENED' THEN gac.command_result_at ELSE NULL END AS processed_at,
                CASE
                    WHEN gac.failure_detail IS NULL THEN NULL
                    WHEN failed_events.event_reason_code IS NOT NULL THEN failed_events.event_reason_code
                    ELSE 'GATE_HANDOFF_ADAPTER_FAILED'
                END AS last_failure_code
            FROM gates.gate_authorization_consumptions AS gac
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::integer AS attempt_count
                FROM gates.gate_events AS ge
                WHERE ge.gate_authorization_consumption_id = gac.gate_authorization_consumption_id
                  AND ge.event_type = 'GATE_OPEN_COMMAND_REQUESTED'
            ) AS command_attempts ON TRUE
            LEFT JOIN LATERAL (
                SELECT ge.event_reason_code
                FROM gates.gate_events AS ge
                WHERE ge.gate_authorization_consumption_id = gac.gate_authorization_consumption_id
                  AND ge.event_status = 'FAILED'
                ORDER BY ge.created_at DESC
                LIMIT 1
            ) AS failed_events ON TRUE
            WHERE gac.gate_authorization_consumption_id = @gate_authorization_consumption_id
              AND (
                    @processing_key = @gate_authorization_consumption_id
                    OR EXISTS (
                        SELECT 1
                        FROM gates.gate_events AS ge
                        WHERE ge.gate_authorization_consumption_id = gac.gate_authorization_consumption_id
                          AND ge.source_event_ref = @source_event_ref
                    )
                    OR gac.command_requested
                  )
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = processingKey;
        command.Parameters.Add("source_event_ref", NpgsqlDbType.Varchar).Value =
            $"central-pms://integration-events/{processingKey}";
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            GateAuthorizationConsumptionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value =
            processingKey == Guid.Parse("c1000000-0000-0000-0000-000000000003")
                ? NoDiscountTariffSnapshotId
                : AppliedTariffSnapshotId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ProcessingRow(
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetString(reader.GetOrdinal("processing_status")),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            reader.IsDBNull(reader.GetOrdinal("processed_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("processed_at")),
            reader.IsDBNull(reader.GetOrdinal("last_failure_code"))
                ? null
                : reader.GetString(reader.GetOrdinal("last_failure_code")));
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("EXITPASS_INTEGRATION_DB")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__MainDatabase")
            ?? "Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me";
    }

    private sealed record ProcessingRow(
        Guid GateAuthorizationConsumptionId,
        Guid TariffSnapshotId,
        string Status,
        int AttemptCount,
        DateTimeOffset? ProcessedAt,
        string? LastFailureCode);

    private async Task<CommandRow?> ReadCommandRowAsync(Guid sourceProcessingId)
    {
        const string sql = """
            SELECT
                request_events.gate_event_id AS command_id,
                gac.gate_authorization_consumption_id,
                @tariff_snapshot_id::uuid AS tariff_snapshot_id,
                CASE
                    WHEN gac.command_result_status = 'OPENED' THEN 'SUCCEEDED'
                    WHEN gac.command_result_status = 'FAILED'
                         AND COALESCE(command_attempts.attempt_count, 0) >= @max_attempts THEN 'TERMINAL_FAILURE'
                    WHEN gac.command_result_status = 'FAILED' THEN 'RETRYABLE'
                    WHEN gac.command_result_status = 'REQUESTED' THEN 'IN_PROGRESS'
                    ELSE NULL
                END AS command_status,
                COALESCE(command_attempts.attempt_count, 0)::integer AS attempt_count,
                @max_attempts::integer AS max_attempts,
                @retry_policy_code::text AS retry_policy_code,
                request_events.occurred_at AS last_attempted_at,
                request_events.occurred_at AS started_at,
                CASE WHEN gac.command_result_status IN ('OPENED', 'FAILED') THEN gac.command_result_at ELSE NULL END AS completed_at,
                CASE
                    WHEN gac.command_result_status = 'FAILED'
                         AND COALESCE(command_attempts.attempt_count, 0) < @max_attempts THEN gac.command_result_at
                    ELSE NULL
                END AS next_attempt_at,
                CASE
                    WHEN gac.command_result_status = 'FAILED'
                         AND COALESCE(command_attempts.attempt_count, 0) >= @max_attempts THEN gac.command_result_at
                    ELSE NULL
                END AS terminal_failure_at,
                CASE WHEN gac.command_result_status = 'FAILED' THEN failed_events.event_reason_code ELSE NULL END AS failure_code,
                CASE WHEN gac.command_result_status = 'FAILED' THEN failed_events.event_reason_code ELSE NULL END AS last_failure_code
            FROM gates.gate_authorization_consumptions AS gac
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::integer AS attempt_count
                FROM gates.gate_events AS ge
                WHERE ge.gate_authorization_consumption_id = gac.gate_authorization_consumption_id
                  AND ge.event_type = 'GATE_OPEN_COMMAND_REQUESTED'
            ) AS command_attempts ON TRUE
            LEFT JOIN LATERAL (
                SELECT ge.gate_event_id, ge.occurred_at
                FROM gates.gate_events AS ge
                WHERE ge.gate_authorization_consumption_id = gac.gate_authorization_consumption_id
                  AND ge.event_type = 'GATE_OPEN_COMMAND_REQUESTED'
                  AND ge.source_event_ref = @source_event_ref
                ORDER BY ge.occurred_at
                LIMIT 1
            ) AS request_events ON TRUE
            LEFT JOIN LATERAL (
                SELECT ge.event_reason_code
                FROM gates.gate_events AS ge
                WHERE ge.gate_authorization_consumption_id = gac.gate_authorization_consumption_id
                  AND ge.event_status = 'FAILED'
                ORDER BY ge.created_at DESC
                LIMIT 1
            ) AS failed_events ON TRUE
            WHERE gac.gate_authorization_consumption_id = @gate_authorization_consumption_id
              AND request_events.gate_event_id IS NOT NULL
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = sourceProcessingId;
        command.Parameters.Add("source_event_ref", NpgsqlDbType.Varchar).Value =
            $"central-pms://integration-events/{sourceProcessingId}";
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            GateAuthorizationConsumptionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value =
            sourceProcessingId == Guid.Parse("c1000000-0000-0000-0000-000000000003")
                ? NoDiscountTariffSnapshotId
                : AppliedTariffSnapshotId;
        command.Parameters.Add("max_attempts", NpgsqlDbType.Integer).Value = GateCommandRetryPolicy.Default.MaxAttempts;
        command.Parameters.Add("retry_policy_code", NpgsqlDbType.Text).Value = GateCommandRetryPolicy.Default.PolicyCode;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new CommandRow(
            reader.GetGuid(reader.GetOrdinal("command_id")),
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            reader.GetInt32(reader.GetOrdinal("max_attempts")),
            reader.GetString(reader.GetOrdinal("retry_policy_code")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_attempted_at")),
            reader.IsDBNull(reader.GetOrdinal("started_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at")),
            reader.IsDBNull(reader.GetOrdinal("completed_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("completed_at")),
            reader.IsDBNull(reader.GetOrdinal("next_attempt_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("next_attempt_at")),
            reader.IsDBNull(reader.GetOrdinal("terminal_failure_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("terminal_failure_at")),
            reader.IsDBNull(reader.GetOrdinal("failure_code"))
                ? null
                : reader.GetString(reader.GetOrdinal("failure_code")),
            reader.IsDBNull(reader.GetOrdinal("last_failure_code"))
                ? null
                : reader.GetString(reader.GetOrdinal("last_failure_code")));
    }

    private sealed record CommandRow(
        Guid CommandId,
        Guid GateAuthorizationConsumptionId,
        Guid TariffSnapshotId,
        string Status,
        int AttemptCount,
        int MaxAttempts,
        string RetryPolicyCode,
        DateTimeOffset LastAttemptedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? TerminalFailureAt,
        string? FailureCode,
        string? LastFailureCode);

    private sealed class CapturingAdapter : IConsumedAuthorizationGateActionAdapter
    {
        public int CallCount { get; private set; }

        public GateAuthorizationConsumedHandoff? LastHandoff { get; private set; }

        public Exception? Failure { get; set; }

        public Task ProcessConsumedAuthorizationAsync(
            GateAuthorizationConsumedHandoff handoff,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastHandoff = handoff;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StaticScopeValidator(GateAuthorizationConsumedScopeValidationResult result)
        : IGateAuthorizationConsumedScopeValidator
    {
        public Task<GateAuthorizationConsumedScopeValidationResult> ValidateAsync(
            GateAuthorizationConsumedHandoff handoff,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}

#pragma warning restore CS1591
