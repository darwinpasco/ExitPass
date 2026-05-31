using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// Infrastructure gateway that consumes exit authorizations through the canonical database routine.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 9.7 Recommended Database Functions
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - ExitAuthorization is consumed only through the canonical DB routine
/// - Application code does not mutate authorization state outside the DB control path
/// </summary>
public sealed class ConsumeExitAuthorizationGateway : IConsumeExitAuthorizationGateway
{
    /// <summary>
    /// Activity source for payment infrastructure spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Infrastructure.Payments");

    private readonly string _connectionString;
    private readonly ILogger<ConsumeExitAuthorizationGateway> _logger;

    /// <summary>
    /// Creates a gateway for consuming exit authorizations against the primary database.
    /// </summary>
    /// <param name="connectionString">Database connection string for Central PMS persistence.</param>
    /// <param name="logger">Application logger.</param>
    public ConsumeExitAuthorizationGateway(
        string connectionString,
        ILogger<ConsumeExitAuthorizationGateway> logger)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Consumes an exit authorization by calling the canonical database routine.
    /// </summary>
    /// <param name="request">Consume request metadata and identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative consume result.</returns>
    public async Task<ConsumeExitAuthorizationDbResult> ConsumeAsync(
        ConsumeExitAuthorizationDbRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        const string sql = """
            SELECT
                exit_authorization_id,
                authorization_status,
                consumed_at
            FROM core.consume_exit_authorization(
                @p_exit_authorization_id,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        using var activity = ActivitySource.StartActivity("DB ConsumeExitAuthorization", ActivityKind.Client);

        activity?.SetTag("db.system", "postgresql");
        activity?.SetTag("db.operation", "consume_exit_authorization");
        activity?.SetTag("db.statement.name", "core.consume_exit_authorization");
        activity?.SetTag("exit_authorization_id", request.ExitAuthorizationId);
        activity?.SetTag("requested_by_user_id", request.RequestedByUserId);
        activity?.SetTag("correlation_id", request.CorrelationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["exit_authorization_id"] = request.ExitAuthorizationId,
            ["requested_by_user_id"] = request.RequestedByUserId,
            ["correlation_id"] = request.CorrelationId
        });

        _logger.LogInformation("DB ConsumeExitAuthorization started.");

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await ValidateIssuedAuthorizationPaidChainAsync(
                connection,
                transaction,
                request,
                cancellationToken);

            await using var dbCommand = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 30
            };

            dbCommand.Parameters.AddWithValue("p_exit_authorization_id", request.ExitAuthorizationId);
            dbCommand.Parameters.AddWithValue("p_requested_by", request.RequestedByUserId);
            dbCommand.Parameters.AddWithValue("p_correlation_id", request.CorrelationId);
            dbCommand.Parameters.AddWithValue("p_now", request.RequestedAt);

            ConsumeExitAuthorizationDbResult result;

            await using (var reader = await dbCommand.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("consume_exit_authorization() returned no rows.");
                }

                result = new ConsumeExitAuthorizationDbResult(
                    ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
                    AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
                    ConsumedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("consumed_at")));
            }

            await transaction.CommitAsync(cancellationToken);

            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);
            activity?.SetTag("authorization_status", result.AuthorizationStatus);
            activity?.SetTag("consumed_at", result.ConsumedAt);

            _logger.LogInformation(
                "DB ConsumeExitAuthorization succeeded. exit_authorization_id={ExitAuthorizationId} authorization_status={AuthorizationStatus}",
                result.ExitAuthorizationId,
                result.AuthorizationStatus);

            return result;
        }
        catch (PostgresException ex) when (IsDeterministicBusinessRejection(ex))
        {
            var duration = DateTimeOffset.UtcNow - startedAt;
            var rejectionCode = ResolveBusinessRejectionCode(ex);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("rejection_reason", rejectionCode);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogWarning(
                ex,
                "DB ConsumeExitAuthorization rejected by deterministic business rules. exit_authorization_id={ExitAuthorizationId} rejection_reason={RejectionReason}",
                request.ExitAuthorizationId,
                rejectionCode);

            throw;
        }
        catch (KeyNotFoundException ex)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("rejection_reason", "EXIT_AUTHORIZATION_NOT_FOUND");
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogWarning(
                ex,
                "DB ConsumeExitAuthorization rejected before consume persistence. exit_authorization_id={ExitAuthorizationId} rejection_reason=EXIT_AUTHORIZATION_NOT_FOUND",
                request.ExitAuthorizationId);

            throw;
        }
        catch (ExitAuthorizationConsumeConflictException ex)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("rejection_reason", ex.ErrorCode);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogWarning(
                ex,
                "DB ConsumeExitAuthorization rejected before consume persistence. exit_authorization_id={ExitAuthorizationId} rejection_reason={RejectionReason}",
                request.ExitAuthorizationId,
                ex.ErrorCode);

            throw;
        }
        catch (Exception ex)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("failure_class", "SYSTEM_FAILURE");
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogError(
                ex,
                "DB ConsumeExitAuthorization failed. exit_authorization_id={ExitAuthorizationId}",
                request.ExitAuthorizationId);

            throw;
        }
    }

    private static async Task ValidateIssuedAuthorizationPaidChainAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ConsumeExitAuthorizationDbRequest request,
        CancellationToken cancellationToken)
    {
        const string validationSql = """
            SELECT
                ea.exit_authorization_id,
                ea.parking_session_id AS authorization_parking_session_id,
                ea.payment_attempt_id,
                ea.payment_confirmation_id AS authorization_payment_confirmation_id,
                ea.authorization_status::text AS authorization_status,
                ea.expires_at,
                ps.parking_session_id AS persisted_parking_session_id,
                ps.session_status::text AS parking_session_status,
                pa.payment_attempt_id AS persisted_payment_attempt_id,
                pa.parking_session_id AS attempt_parking_session_id,
                pa.tariff_snapshot_id,
                pa.amount AS attempt_amount,
                pa.currency_code::text AS attempt_currency_code,
                pa.attempt_status::text AS attempt_status,
                pa.finalized_at,
                ts.tariff_snapshot_id AS persisted_tariff_snapshot_id,
                ts.parking_session_id AS tariff_parking_session_id,
                ts.net_amount AS tariff_net_amount,
                ts.currency_code::text AS tariff_currency_code,
                pc.payment_confirmation_id AS persisted_payment_confirmation_id,
                pc.confirmed_amount,
                pc.currency_code::text AS confirmation_currency_code,
                applied.original_tariff_snapshot_id,
                applied.applied_tariff_snapshot_id,
                consumed.gate_authorization_consumption_id AS consumed_gate_authorization_consumption_id
            FROM core.exit_authorizations AS ea
            LEFT JOIN core.parking_sessions AS ps
                ON ps.parking_session_id = ea.parking_session_id
            LEFT JOIN core.payment_attempts AS pa
                ON pa.payment_attempt_id = ea.payment_attempt_id
            LEFT JOIN core.tariff_snapshots AS ts
                ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
            LEFT JOIN core.payment_confirmations AS pc
                ON pc.payment_confirmation_id = ea.payment_confirmation_id
               AND pc.payment_attempt_id = pa.payment_attempt_id
               AND pc.confirmation_status = 'RECORDED'
            LEFT JOIN LATERAL (
                SELECT
                    sdpa.original_tariff_snapshot_id,
                    sdpa.applied_tariff_snapshot_id
                FROM discounts.statutory_discount_payable_basis_applications AS sdpa
                WHERE sdpa.parking_session_id = ea.parking_session_id
                  AND sdpa.application_status = 'APPLIED'
                  AND sdpa.applied_tariff_snapshot_id IS NOT NULL
                ORDER BY sdpa.applied_at DESC NULLS LAST, sdpa.created_at DESC
                LIMIT 1
            ) AS applied ON TRUE
            LEFT JOIN LATERAL (
                SELECT gac.gate_authorization_consumption_id
                FROM gates.gate_authorization_consumptions AS gac
                WHERE gac.exit_authorization_id = ea.exit_authorization_id
                  AND gac.consume_status = 'CONSUMED'
                ORDER BY gac.consumed_at DESC NULLS LAST, gac.created_at DESC
                LIMIT 1
            ) AS consumed ON TRUE
            WHERE ea.exit_authorization_id = @exit_authorization_id
            FOR UPDATE OF ea;
            """;

        await using var command = new NpgsqlCommand(validationSql, connection, transaction)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("exit_authorization_id", request.ExitAuthorizationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException($"exit authorization {request.ExitAuthorizationId} was not found");
        }

        if (!reader.IsDBNull(reader.GetOrdinal("consumed_gate_authorization_consumption_id")))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
                "Exit authorization has already been consumed.");
        }

        var authorizationStatus = reader.GetString(reader.GetOrdinal("authorization_status"));
        if (!string.Equals(authorizationStatus, "ISSUED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "EXIT_AUTHORIZATION_NOT_ISSUED",
                "Exit authorization is not in an issued state.");
        }

        var expiresAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expires_at"));
        if (expiresAt <= request.RequestedAt)
        {
            throw new ExitAuthorizationConsumeConflictException(
                "EXIT_AUTHORIZATION_EXPIRED",
                "Exit authorization is expired.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("persisted_parking_session_id")))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PARKING_SESSION_NOT_ELIGIBLE_FOR_EXIT",
                "Exit authorization parking session was not found.");
        }

        var parkingSessionStatus = reader.GetString(reader.GetOrdinal("parking_session_status"));
        if (!string.Equals(parkingSessionStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PARKING_SESSION_NOT_ELIGIBLE_FOR_EXIT",
                "Parking session is not eligible for gate consume.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("persisted_payment_attempt_id")))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_ATTEMPT_NOT_FOUND",
                "Exit authorization payment attempt was not found.");
        }

        var authorizationParkingSessionId = reader.GetGuid(reader.GetOrdinal("authorization_parking_session_id"));
        var attemptParkingSessionId = reader.GetGuid(reader.GetOrdinal("attempt_parking_session_id"));
        if (authorizationParkingSessionId != attemptParkingSessionId)
        {
            throw new ExitAuthorizationConsumeConflictException(
                "EXIT_AUTHORIZATION_SCOPE_MISMATCH",
                "Exit authorization payment attempt belongs to a different parking session.");
        }

        var attemptStatus = reader.GetString(reader.GetOrdinal("attempt_status"));
        if (!string.Equals(attemptStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_ATTEMPT_NOT_CONFIRMED",
                "Payment attempt is not confirmed and cannot be consumed at the gate.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("finalized_at")))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_ATTEMPT_NOT_FINALIZED",
                "Payment attempt is not finalized and cannot be consumed at the gate.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("persisted_tariff_snapshot_id")))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_ATTEMPT_TARIFF_SNAPSHOT_MISSING",
                "Payment attempt does not have a persisted tariff snapshot.");
        }

        var tariffParkingSessionId = reader.GetGuid(reader.GetOrdinal("tariff_parking_session_id"));
        if (tariffParkingSessionId != authorizationParkingSessionId)
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_ATTEMPT_TARIFF_SNAPSHOT_MISMATCH",
                "Payment attempt tariff snapshot belongs to a different parking session.");
        }

        var paymentAttemptTariffSnapshotId = reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id"));
        var appliedTariffOrdinal = reader.GetOrdinal("applied_tariff_snapshot_id");
        if (!reader.IsDBNull(appliedTariffOrdinal) &&
            paymentAttemptTariffSnapshotId != reader.GetGuid(appliedTariffOrdinal))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_ATTEMPT_TARIFF_SNAPSHOT_MISMATCH",
                "Payment attempt tariff snapshot does not match the applied effective payable basis.");
        }

        var attemptAmount = reader.GetDecimal(reader.GetOrdinal("attempt_amount"));
        var tariffAmount = reader.GetDecimal(reader.GetOrdinal("tariff_net_amount"));
        if (attemptAmount != tariffAmount)
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_AMOUNT_MISMATCH",
                "Payment attempt amount does not match its persisted tariff snapshot payable amount.");
        }

        var attemptCurrency = reader.GetString(reader.GetOrdinal("attempt_currency_code")).Trim();
        var tariffCurrency = reader.GetString(reader.GetOrdinal("tariff_currency_code")).Trim();
        if (!string.Equals(attemptCurrency, tariffCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_CURRENCY_MISMATCH",
                "Payment attempt currency does not match its persisted tariff snapshot currency.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("persisted_payment_confirmation_id")))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_ATTEMPT_NOT_CONFIRMED",
                "Exit authorization does not have recorded payment confirmation evidence.");
        }

        var confirmedAmount = reader.GetDecimal(reader.GetOrdinal("confirmed_amount"));
        if (confirmedAmount != attemptAmount)
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_AMOUNT_MISMATCH",
                "Payment confirmation amount does not match the payment attempt amount.");
        }

        var confirmationCurrency = reader.GetString(reader.GetOrdinal("confirmation_currency_code")).Trim();
        if (!string.Equals(confirmationCurrency, attemptCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationConsumeConflictException(
                "PAYMENT_CURRENCY_MISMATCH",
                "Payment confirmation currency does not match the payment attempt currency.");
        }
    }

    /// <summary>
    /// Determines whether the PostgreSQL exception represents a deterministic business rejection.
    /// </summary>
    /// <param name="exception">The PostgreSQL exception.</param>
    /// <returns><see langword="true"/> when the exception is a business rejection.</returns>
    private static bool IsDeterministicBusinessRejection(PostgresException exception)
    {
        return exception.SqlState is PostgresErrorCodes.RaiseException or PostgresErrorCodes.NoDataFound;
    }

    /// <summary>
    /// Resolves the business rejection code used in telemetry and structured logging.
    /// </summary>
    /// <param name="exception">The PostgreSQL exception.</param>
    /// <returns>The deterministic rejection code.</returns>
    private static string ResolveBusinessRejectionCode(PostgresException exception)
    {
        if (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            return "EXIT_AUTHORIZATION_NOT_FOUND";
        }

        if (exception.MessageText.Contains("already been consumed", StringComparison.OrdinalIgnoreCase))
        {
            return "EXIT_AUTHORIZATION_ALREADY_CONSUMED";
        }

        if (exception.MessageText.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return "EXIT_AUTHORIZATION_EXPIRED";
        }

        return "EXIT_AUTHORIZATION_REJECTED";
    }
}
