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
        Assert.Equal(AppliedTariffSnapshotId, command.TariffSnapshotId);
        Assert.Equal(GateAuthorizationConsumptionId, command.GateAuthorizationConsumptionId);
        Assert.NotNull(command.StartedAt);
        Assert.NotNull(command.CompletedAt);
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
        Assert.Equal("GATE_HANDOFF_ADAPTER_FAILED", failedCommand.FailureCode);

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
        Assert.Equal(failedCommand.CommandId, processedCommand!.CommandId);
        Assert.Equal("SUCCEEDED", processedCommand.Status);
        Assert.Equal(2, processedCommand.AttemptCount);
        Assert.Null(processedCommand.FailureCode);
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
        const string commandSql = """
            DELETE FROM gates.gate_commands
            WHERE source_processing_id = ANY(@processing_keys);
            """;

        const string sql = """
            DELETE FROM gates.gate_authorization_consumed_processing
            WHERE processing_key = ANY(@processing_keys);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        var processingKeys = new[]
        {
            EventId,
            Guid.Parse("c1000000-0000-0000-0000-000000000002"),
            Guid.Parse("c1000000-0000-0000-0000-000000000003"),
            Guid.Parse("c1000000-0000-0000-0000-000000000004"),
            Guid.Parse("c1000000-0000-0000-0000-000000000005")
        };
        await using var commandDelete = new NpgsqlCommand(commandSql, connection);
        commandDelete.Parameters.Add("processing_keys", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = processingKeys;
        await commandDelete.ExecuteNonQueryAsync();

        await using var processingDelete = new NpgsqlCommand(sql, connection);
        processingDelete.Parameters.Add("processing_keys", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = processingKeys;
        await processingDelete.ExecuteNonQueryAsync();
    }

    private async Task<ProcessingRow?> ReadProcessingRowAsync(Guid processingKey)
    {
        const string sql = """
            SELECT
                gate_authorization_consumption_id,
                tariff_snapshot_id,
                processing_status,
                attempt_count,
                processed_at,
                last_failure_code
            FROM gates.gate_authorization_consumed_processing
            WHERE processing_key = @processing_key
              AND event_type = 'GateAuthorizationConsumed'
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = processingKey;

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
                command_id,
                gate_authorization_consumption_id,
                tariff_snapshot_id,
                command_status,
                attempt_count,
                started_at,
                completed_at,
                failure_code
            FROM gates.gate_commands
            WHERE source_processing_id = @source_processing_id
              AND command_type = 'GateAuthorizationConsumed'
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = sourceProcessingId;

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
            reader.IsDBNull(reader.GetOrdinal("started_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at")),
            reader.IsDBNull(reader.GetOrdinal("completed_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("completed_at")),
            reader.IsDBNull(reader.GetOrdinal("failure_code"))
                ? null
                : reader.GetString(reader.GetOrdinal("failure_code")));
    }

    private sealed record CommandRow(
        Guid CommandId,
        Guid GateAuthorizationConsumptionId,
        Guid TariffSnapshotId,
        string Status,
        int AttemptCount,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        string? FailureCode);

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
