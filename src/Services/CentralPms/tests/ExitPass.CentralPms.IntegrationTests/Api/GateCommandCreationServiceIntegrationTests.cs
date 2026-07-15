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
/// Verifies deterministic consumed-event-to-gate-command creation against canonical gate tables.
/// </summary>
public sealed class GateCommandCreationServiceIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task CreateFromConsumedEvent_WhenNewEvent_CreatesProcessedInboxAndRequestedCommand()
    {
        var fixture = await PrepareFixtureAsync("create");
        var service = CreateService(fixture);

        try
        {
            var result = await service.CreateFromConsumedEventAsync(CreateEnvelope(fixture), CancellationToken.None);
            var counts = await ReadCountsAsync(fixture.ConsumptionId);
            var state = await new GateCommandStateReadRepository(ConnectionString)
                .GetByConsumptionIdAsync(fixture.ConsumptionId, CancellationToken.None);

            Assert.Equal(GateCommandCreationOutcome.Created, result.Outcome);
            Assert.Equal(fixture.ConsumptionId, result.ProcessingKey);
            Assert.Equal("OPEN_GATE", result.CommandType);
            Assert.Equal(1, counts.ProcessingCount);
            Assert.Equal(1, counts.CommandCount);
            Assert.Equal(0, counts.AuditCount);
            Assert.NotNull(state);
            Assert.Equal(result.ProcessingId, state!.ConsumedProcessing!.ProcessingId);
            Assert.Equal("PROCESSED", state.ConsumedProcessing.ProcessingStatus);
            Assert.Equal("COMMAND_REQUESTED", state.ConsumedProcessing.ProcessingResult);
            Assert.NotNull(state.ConsumedProcessing.ProcessedAt);
            Assert.Equal(result.CommandId, state.GateCommand!.CommandId);
            Assert.Equal("REQUESTED", state.GateCommand.CommandStatus);
            Assert.Equal(0, state.GateCommand.AttemptCount);
            Assert.Equal(3, state.GateCommand.MaxAttempts);
            Assert.Equal("GATE_COMMAND_RETRY_V1", state.GateCommand.RetryPolicyCode);
            Assert.Null(state.GateCommand.StartedAt);
            Assert.Null(state.GateCommand.CompletedAt);
            Assert.Null(state.GateCommand.NextAttemptAt);
            Assert.Null(state.GateCommand.TerminalFailureAt);
            Assert.Empty(state.HikCentralActionAttempts);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task CreateFromConsumedEvent_WhenSameEventReplays_ReturnsSameIdsAndLeavesCountsUnchanged()
    {
        var fixture = await PrepareFixtureAsync("replay");
        var service = CreateService(fixture);

        try
        {
            var first = await service.CreateFromConsumedEventAsync(CreateEnvelope(fixture), CancellationToken.None);
            var before = await ReadCountsAsync(fixture.ConsumptionId);
            var second = await service.CreateFromConsumedEventAsync(CreateEnvelope(fixture), CancellationToken.None);
            var after = await ReadCountsAsync(fixture.ConsumptionId);

            Assert.Equal(GateCommandCreationOutcome.Created, first.Outcome);
            Assert.Equal(GateCommandCreationOutcome.IdempotentReplay, second.Outcome);
            Assert.Equal(first.ProcessingId, second.ProcessingId);
            Assert.Equal(first.CommandId, second.CommandId);
            Assert.Equal(before, after);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task CreateFromConsumedEvent_WhenHandledConcurrently_CreatesOneProcessingAndOneCommand()
    {
        var fixture = await PrepareFixtureAsync("concurrent");
        var service = CreateService(fixture);

        try
        {
            var tasks = Enumerable.Range(0, 8)
                .Select(_ => service.CreateFromConsumedEventAsync(CreateEnvelope(fixture), CancellationToken.None))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            var counts = await ReadCountsAsync(fixture.ConsumptionId);

            Assert.Equal(1, results.Select(result => result.ProcessingId).Distinct().Count());
            Assert.Equal(1, results.Select(result => result.CommandId).Distinct().Count());
            Assert.Equal(1, results.Count(result => result.Outcome == GateCommandCreationOutcome.Created));
            Assert.Equal(7, results.Count(result => result.Outcome == GateCommandCreationOutcome.IdempotentReplay));
            Assert.Equal(1, counts.ProcessingCount);
            Assert.Equal(1, counts.CommandCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task CreateFromConsumedEvent_WhenEventIdConflictsWithDifferentConsumption_RejectsWithoutMutatingOriginal()
    {
        var original = await PrepareFixtureAsync("conflict-original");
        var conflicting = await PrepareFixtureAsync("conflict-new");
        var service = CreateService(original);

        try
        {
            var created = await service.CreateFromConsumedEventAsync(CreateEnvelope(original), CancellationToken.None);
            var exception = await Assert.ThrowsAsync<GateCommandCreationRejectedException>(() =>
                service.CreateFromConsumedEventAsync(CreateEnvelope(conflicting, original.EventId), CancellationToken.None));

            var originalCounts = await ReadCountsAsync(original.ConsumptionId);
            var conflictingCounts = await ReadCountsAsync(conflicting.ConsumptionId);

            Assert.Equal("GATE_COMMAND_CREATION_CONFLICT", exception.ErrorCode);
            Assert.Equal(1, originalCounts.ProcessingCount);
            Assert.Equal(1, originalCounts.CommandCount);
            Assert.Equal(0, conflictingCounts.ProcessingCount);
            Assert.Equal(0, conflictingCounts.CommandCount);
            Assert.NotEqual(Guid.Empty, created.CommandId);
        }
        finally
        {
            await CleanupAsync(conflicting);
            await CleanupAsync(original);
        }
    }

    [Fact]
    public async Task CreateFromConsumedEvent_WhenConsumptionIsMissing_FailsClosed()
    {
        var fixture = GateCommandCreationFixture.Create("missing-consumption");
        fixture.PaymentAttemptId = DeterministicGuid(fixture.CorrelationId, 20);
        fixture.TariffSnapshotId = DeterministicGuid(fixture.CorrelationId, 21);
        fixture.VendorSystemId = DeterministicGuid(fixture.CorrelationId, 22);
        var service = CreateService(fixture);

        var exception = await Assert.ThrowsAsync<GateCommandCreationRejectedException>(() =>
            service.CreateFromConsumedEventAsync(CreateEnvelope(fixture), CancellationToken.None));
        var counts = await ReadCountsAsync(fixture.ConsumptionId);

        Assert.Equal("GATE_AUTHORIZATION_CONSUMPTION_NOT_FOUND", exception.ErrorCode);
        Assert.Equal(0, counts.ProcessingCount);
        Assert.Equal(0, counts.CommandCount);
    }

    [Fact]
    public async Task CreateOrReuseAsync_WhenCommandInsertFails_RollsBackProcessingInsert()
    {
        var fixture = await PrepareFixtureAsync("rollback");
        var repository = new GateCommandCreationRepository(ConnectionString);
        var request = CreateRepositoryRequest(fixture) with
        {
            CommandType = new string('X', 129)
        };

        try
        {
            await Assert.ThrowsAsync<PostgresException>(() =>
                repository.CreateOrReuseAsync(request, CancellationToken.None));

            var counts = await ReadCountsAsync(fixture.ConsumptionId);
            Assert.Equal(0, counts.ProcessingCount);
            Assert.Equal(0, counts.CommandCount);
            Assert.Equal(0, counts.AuditCount);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    private static GateCommandCreationService CreateService(GateCommandCreationFixture fixture) =>
        new(
            new GateCommandCreationRepository(ConnectionString),
            new FixedClock(fixture.Now.AddMinutes(2)));

    private static IntegrationEventEnvelope CreateEnvelope(
        GateCommandCreationFixture fixture,
        Guid? eventId = null)
    {
        return new IntegrationEventEnvelope
        {
            EventId = eventId ?? fixture.EventId,
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

    private static GateCommandCreationRequest CreateRepositoryRequest(GateCommandCreationFixture fixture)
    {
        return new GateCommandCreationRequest(
            EventId: fixture.EventId,
            EventType: IntegrationEventTypes.GateAuthorizationConsumed,
            EventRef: $"central-pms://integration-events/{fixture.EventId:N}",
            ProcessingKey: fixture.ConsumptionId,
            GateAuthorizationConsumptionId: fixture.ConsumptionId,
            ExitAuthorizationId: fixture.ExitAuthorizationId,
            ParkingSessionId: fixture.ParkingSessionId,
            PaymentAttemptId: fixture.PaymentAttemptId,
            TariffSnapshotId: fixture.TariffSnapshotId,
            GateDeviceId: fixture.GateDeviceId,
            ServiceIdentityId: fixture.ServiceIdentityId,
            LaneId: fixture.LaneId,
            SiteId: fixture.SiteId,
            VendorSystemId: fixture.VendorSystemId,
            ConsumedAt: fixture.Now,
            CorrelationId: fixture.CorrelationId,
            CommandType: GateCommandCreationService.OpenGateCommandType,
            RequestedAt: fixture.Now.AddMinutes(2));
    }

    private static async Task<GateCommandCreationFixture> PrepareFixtureAsync(string scope)
    {
        var fixture = GateCommandCreationFixture.Create(scope);

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            fixture.PaymentContext,
            "Seed data for gate command creation tests");

        var attempt = await CreateAttemptAsync(
            ConnectionString,
            fixture.PaymentContext,
            $"gate-command-create-{fixture.CorrelationId:N}",
            "gate-command-create-test");

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"gate-create-{fixture.CorrelationId:N}",
            "gate-command-create-test",
            fixture.CorrelationId);

        Assert.NotNull(confirmation);

        fixture.PaymentAttemptId = attempt.PaymentAttemptId;
        fixture.TariffSnapshotId = attempt.TariffSnapshotId;
        fixture.PaymentConfirmationId = confirmation!.PaymentConfirmationId;
        fixture.VendorSystemId = await ReadVendorSystemIdAsync(fixture.PaymentContext.VendorSystemCode);

        await InsertExitAuthorizationAsync(fixture);
        await InsertConsumptionAsync(fixture);

        return fixture;
    }

    private static async Task InsertExitAuthorizationAsync(GateCommandCreationFixture fixture)
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

    private static async Task InsertConsumptionAsync(GateCommandCreationFixture fixture)
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

    private static async Task<GateCommandCreationCounts> ReadCountsAsync(Guid consumptionId)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM gates.gate_authorization_consumed_processing WHERE gate_authorization_consumption_id = @consumption_id) AS processing_count,
                (SELECT COUNT(*) FROM gates.gate_commands WHERE gate_authorization_consumption_id = @consumption_id) AS command_count,
                (SELECT COUNT(*) FROM gates.hikcentral_gate_action_audits WHERE gate_authorization_consumption_id = @consumption_id) AS audit_count;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("consumption_id", NpgsqlDbType.Uuid).Value = consumptionId;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new GateCommandCreationCounts(
            reader.GetInt64(reader.GetOrdinal("processing_count")),
            reader.GetInt64(reader.GetOrdinal("command_count")),
            reader.GetInt64(reader.GetOrdinal("audit_count")));
    }

    private static async Task CleanupAsync(GateCommandCreationFixture fixture)
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

    private static void AddFixtureParameters(NpgsqlCommand command, GateCommandCreationFixture fixture)
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

    private sealed class GateCommandCreationFixture
    {
        private GateCommandCreationFixture(
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

        public DateTimeOffset Now { get; }

        public PaymentTestContext PaymentContext { get; }

        public static GateCommandCreationFixture Create(string scope)
        {
            var context = PaymentTestContext.Create(scope);
            var correlationId = context.CorrelationId;
            return new GateCommandCreationFixture(
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

    private sealed record GateCommandCreationCounts(
        long ProcessingCount,
        long CommandCount,
        long AuditCount);
}
