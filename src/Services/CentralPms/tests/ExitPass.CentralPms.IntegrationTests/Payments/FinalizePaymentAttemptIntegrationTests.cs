using System;
using System.Threading.Tasks;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies DB-backed terminal-state behavior for finalize_payment_attempt().
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.10 Idempotent Payment Confirmation Invariant
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 8.3 PaymentAttempt State Machine
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - A terminal PaymentAttempt must not transition again
/// - A confirmed PaymentAttempt must not be re-finalized to FAILED
/// </summary>
public sealed class FinalizePaymentAttemptIntegrationTests
{
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

    [Fact]
    public async Task FinalizePaymentAttempt_WhenAttemptIsInitiated_TransitionsToConfirmed()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenAttemptIsInitiated_TransitionsToConfirmed));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                context,
                idempotencyKey: "idem-finalize-success",
                requestedBy: "finalize-test");

            var finalized = await FinalizeAttemptAsync(
                paymentAttemptId: created.PaymentAttemptId,
                finalStatus: "CONFIRMED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            Assert.NotNull(finalized);
            Assert.Equal(created.PaymentAttemptId, finalized!.PaymentAttemptId);
            Assert.Equal("CONFIRMED", finalized.AttemptStatus);

            var row = await GetPaymentAttemptAsync(created.PaymentAttemptId);
            Assert.NotNull(row);
            Assert.Equal("CONFIRMED", row!.AttemptStatus);
            Assert.NotNull(row.FinalizedAt);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task FinalizePaymentAttempt_WhenAttemptAlreadyConfirmed_DoesNotTransitionAgain()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenAttemptAlreadyConfirmed_DoesNotTransitionAgain));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                context,
                idempotencyKey: "idem-finalize-terminal",
                requestedBy: "finalize-test");

            var firstFinalize = await FinalizeAttemptAsync(
                paymentAttemptId: created.PaymentAttemptId,
                finalStatus: "CONFIRMED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            Assert.NotNull(firstFinalize);
            Assert.Equal("CONFIRMED", firstFinalize!.AttemptStatus);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await FinalizeAttemptAsync(
                    paymentAttemptId: created.PaymentAttemptId,
                    finalStatus: "FAILED",
                    requestedBy: "central-pms-finalizer",
                    correlationId: context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var row = await GetPaymentAttemptAsync(created.PaymentAttemptId);
            Assert.NotNull(row);
            Assert.Equal("CONFIRMED", row!.AttemptStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task FinalizePaymentAttempt_WhenAttemptAlreadyFailed_DoesNotTransitionToConfirmed()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenAttemptAlreadyFailed_DoesNotTransitionToConfirmed));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                context,
                idempotencyKey: "idem-finalize-failed-first",
                requestedBy: "finalize-test");

            var firstFinalize = await FinalizeAttemptAsync(
                paymentAttemptId: created.PaymentAttemptId,
                finalStatus: "FAILED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            Assert.NotNull(firstFinalize);
            Assert.Equal("FAILED", firstFinalize!.AttemptStatus);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await FinalizeAttemptAsync(
                    paymentAttemptId: created.PaymentAttemptId,
                    finalStatus: "CONFIRMED",
                    requestedBy: "central-pms-finalizer",
                    correlationId: context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var row = await GetPaymentAttemptAsync(created.PaymentAttemptId);
            Assert.NotNull(row);
            Assert.Equal("FAILED", row!.AttemptStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact(Skip = "Enable after finalize_payment_attempt() contract is locked to idempotent same-status replay behavior.")]
    public async Task FinalizePaymentAttempt_WhenSameTerminalStatusIsReplayed_IsIdempotent()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenSameTerminalStatusIsReplayed_IsIdempotent));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                context,
                idempotencyKey: "idem-finalize-idempotent",
                requestedBy: "finalize-test");

            var firstFinalize = await FinalizeAttemptAsync(
                paymentAttemptId: created.PaymentAttemptId,
                finalStatus: "CONFIRMED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            var replayFinalize = await FinalizeAttemptAsync(
                paymentAttemptId: created.PaymentAttemptId,
                finalStatus: "CONFIRMED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            Assert.NotNull(firstFinalize);
            Assert.NotNull(replayFinalize);
            Assert.Equal(firstFinalize!.PaymentAttemptId, replayFinalize!.PaymentAttemptId);
            Assert.Equal("CONFIRMED", replayFinalize.AttemptStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Creates the canonical PaymentAttempt that later tests finalize.
    ///
    /// BRD:
    /// - 9.9 Payment Initiation
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    ///
    /// Invariants Enforced:
    /// - PaymentAttempt remains bound to one immutable TariffSnapshot
    /// </summary>
    private static async Task<CreateAttemptResult> CreateAttemptAsync(
        PaymentTestContext context,
        string idempotencyKey,
        string requestedBy)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                attempt_status,
                payment_provider_code
            FROM core.create_or_reuse_payment_attempt(
                @p_parking_session_id,
                @p_tariff_snapshot_id,
                @p_payment_provider_code,
                @p_idempotency_key,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("p_parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("p_tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("p_payment_provider_code", "GCASH");
        command.Parameters.AddWithValue("p_idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected create_or_reuse_payment_attempt() to return a row.");

        return new CreateAttemptResult(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")),
            PaymentProviderCode: reader.GetString(reader.GetOrdinal("payment_provider_code")));
    }

    /// <summary>
    /// Finalizes a PaymentAttempt through the DB routine under test.
    ///
    /// BRD:
    /// - 9.10 Payment Processing and Confirmation
    ///
    /// SDD:
    /// - 6.4 Finalize Payment
    /// - 8.3 PaymentAttempt State Machine
    ///
    /// Invariants Enforced:
    /// - Terminal state transition is controlled centrally
    /// - A terminal attempt must not change terminal meaning
    /// </summary>
    private static async Task<FinalizeAttemptResult?> FinalizeAttemptAsync(
        Guid paymentAttemptId,
        string finalStatus,
        string requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                attempt_status
            FROM core.finalize_payment_attempt(
                @p_payment_attempt_id,
                @p_final_attempt_status,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("p_payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("p_final_attempt_status", finalStatus);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new FinalizeAttemptResult(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")));
    }

    private static async Task<PaymentAttemptRow?> GetPaymentAttemptAsync(Guid paymentAttemptId)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                payment_provider_code,
                idempotency_key,
                attempt_status,
                created_at,
                updated_at,
                finalized_at
            FROM core.payment_attempts
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentAttemptRow(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            PaymentProviderCode: reader.GetString(reader.GetOrdinal("payment_provider_code")),
            IdempotencyKey: reader.GetString(reader.GetOrdinal("idempotency_key")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            UpdatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            FinalizedAt: ReadDateTimeOffsetNullable(reader, "finalized_at"));
    }

    private static DateTimeOffset? ReadDateTimeOffsetNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record CreateAttemptResult(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string AttemptStatus,
        string PaymentProviderCode);

    private sealed record FinalizeAttemptResult(
        Guid PaymentAttemptId,
        string AttemptStatus);

    private sealed record PaymentAttemptRow(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string PaymentProviderCode,
        string IdempotencyKey,
        string AttemptStatus,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? FinalizedAt);
}
