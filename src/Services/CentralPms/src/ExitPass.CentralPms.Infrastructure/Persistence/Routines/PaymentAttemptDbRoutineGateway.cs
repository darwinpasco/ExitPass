using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Persistence.Routines;

public sealed class PaymentAttemptDbRoutineGateway : IPaymentAttemptDbRoutineGateway
{
    private readonly string _connectionString;

    public PaymentAttemptDbRoutineGateway(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// BRD:
    /// - 9.9 Payment Initiation
    /// - 10.7.4 One Active Payment Attempt Per Session
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    /// - 9.6 Integrity Constraints and Concurrency Rules
    ///
    /// Invariants Enforced:
    /// - create-or-reuse behavior is delegated to the authoritative DB routine
    /// - application code must not bypass storage-level conflict checks
    /// - replay and active-attempt outcomes remain deterministic under concurrency
    /// </summary>
    public async Task<CreateOrReusePaymentAttemptDbResult> CreateOrReusePaymentAttemptAsync(
        CreateOrReusePaymentAttemptDbRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = BuildCommand(connection, request);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The database routine returned no rows.");
        }

        return Map(reader, request.IdempotencyKey);
    }

    private static NpgsqlCommand BuildCommand(NpgsqlConnection connection, CreateOrReusePaymentAttemptDbRequest request)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT *
            FROM core.create_or_reuse_payment_attempt(
                @p_parking_session_id,
                @p_tariff_snapshot_id,
                @p_payment_provider_code,
                @p_idempotency_key,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );";

        command.Parameters.AddWithValue("p_parking_session_id", request.ParkingSessionId);
        command.Parameters.AddWithValue("p_tariff_snapshot_id", request.TariffSnapshotId);
        command.Parameters.AddWithValue("p_payment_provider_code", request.PaymentProviderCode);
        command.Parameters.AddWithValue("p_idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("p_requested_by", request.RequestedBy);
        command.Parameters.AddWithValue("p_correlation_id", request.CorrelationId);
        command.Parameters.AddWithValue("p_now", request.RequestedAt);

        return command;
    }

    private static CreateOrReusePaymentAttemptDbResult Map(IDataRecord record, string idempotencyKey)
    {
        return new CreateOrReusePaymentAttemptDbResult
        {
            PaymentAttemptId = record["payment_attempt_id"] is DBNull ? Guid.Empty : (Guid)record["payment_attempt_id"],
            ParkingSessionId = record["parking_session_id"] is DBNull ? Guid.Empty : (Guid)record["parking_session_id"],
            TariffSnapshotId = record["tariff_snapshot_id"] is DBNull ? Guid.Empty : (Guid)record["tariff_snapshot_id"],
            AttemptStatus = record["attempt_status"] as string ?? string.Empty,
            PaymentProviderCode = record["payment_provider_code"] as string ?? string.Empty,
            WasReused = record["was_reused"] is bool wasReused && wasReused,
            OutcomeCode = record["outcome_code"] as string ?? string.Empty,
            FailureCode = record["failure_code"] as string,
            GrossAmountSnapshot = record["gross_amount_snapshot"] is DBNull ? 0m : (decimal)record["gross_amount_snapshot"],
            StatutoryDiscountSnapshot = record["statutory_discount_snapshot"] is DBNull ? 0m : (decimal)record["statutory_discount_snapshot"],
            CouponDiscountSnapshot = record["coupon_discount_snapshot"] is DBNull ? 0m : (decimal)record["coupon_discount_snapshot"],
            NetAmountDueSnapshot = record["net_amount_due_snapshot"] is DBNull ? 0m : (decimal)record["net_amount_due_snapshot"],
            CurrencyCode = record["currency_code"] as string ?? string.Empty,
            TariffVersionReference = record["tariff_version_reference"] as string,
            IdempotencyKey = idempotencyKey
        };
    }
}