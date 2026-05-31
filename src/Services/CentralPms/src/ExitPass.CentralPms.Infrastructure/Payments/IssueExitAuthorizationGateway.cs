using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// Infrastructure gateway that issues exit authorizations through the canonical database routine.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 9.7 Recommended Database Functions
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - ExitAuthorization is issued only through the canonical DB routine
/// - Application code does not mint or infer authorization state outside the DB control path
/// </summary>
public sealed class IssueExitAuthorizationGateway : IIssueExitAuthorizationGateway
{
    /// <summary>
    /// Activity source for payment infrastructure spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Infrastructure.Payments");

    private readonly string _connectionString;
    private readonly ILogger<IssueExitAuthorizationGateway> _logger;

    /// <summary>
    /// Creates a gateway for issuing exit authorizations against the primary database.
    /// </summary>
    /// <param name="connectionString">Database connection string for Central PMS persistence.</param>
    /// <param name="logger">Application logger.</param>
    public IssueExitAuthorizationGateway(
        string connectionString,
        ILogger<IssueExitAuthorizationGateway> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    /// <summary>
    /// Issues an exit authorization by calling the canonical database routine.
    /// </summary>
    /// <param name="request">Issuance request metadata and identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative issuance result.</returns>
    public async Task<IssueExitAuthorizationDbResult> IssueAsync(
        IssueExitAuthorizationDbRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                authorization_token,
                authorization_status,
                issued_at,
                expiration_timestamp
            FROM core.issue_exit_authorization(
                @p_parking_session_id,
                @p_payment_attempt_id,
                @p_requested_by_user_id,
                @p_correlation_id,
                @p_now
            );
            """;

        using var activity = ActivitySource.StartActivity("DB IssueExitAuthorization", ActivityKind.Client);

        activity?.SetTag("db.system", "postgresql");
        activity?.SetTag("db.operation", "issue_exit_authorization");
        activity?.SetTag("db.statement.name", "core.issue_exit_authorization");
        activity?.SetTag("parking_session_id", request.ParkingSessionId);
        activity?.SetTag("payment_attempt_id", request.PaymentAttemptId);
        activity?.SetTag("requested_by_user_id", request.RequestedByUserId);
        activity?.SetTag("correlation_id", request.CorrelationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["parking_session_id"] = request.ParkingSessionId,
            ["payment_attempt_id"] = request.PaymentAttemptId,
            ["requested_by_user_id"] = request.RequestedByUserId,
            ["correlation_id"] = request.CorrelationId
        });

        _logger.LogInformation("DB IssueExitAuthorization started.");

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await ValidateConfirmedPaymentAttemptPayableBasisAsync(connection, request, cancellationToken);

            await using var dbCommand = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = 30
            };

            dbCommand.Parameters.AddWithValue("p_parking_session_id", request.ParkingSessionId);
            dbCommand.Parameters.AddWithValue("p_payment_attempt_id", request.PaymentAttemptId);
            dbCommand.Parameters.AddWithValue("p_requested_by_user_id", request.RequestedByUserId);
            dbCommand.Parameters.AddWithValue("p_correlation_id", request.CorrelationId);
            dbCommand.Parameters.AddWithValue("p_now", request.RequestedAt);

            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("issue_exit_authorization() returned no rows.");
            }

            var result = new IssueExitAuthorizationDbResult(
                ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
                ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
                PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
                AuthorizationToken: reader.GetString(reader.GetOrdinal("authorization_token")),
                AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
                IssuedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("issued_at")),
                ExpirationTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expiration_timestamp")));

            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);
            activity?.SetTag("exit_authorization_id", result.ExitAuthorizationId);
            activity?.SetTag("authorization_status", result.AuthorizationStatus);

            _logger.LogInformation(
                "DB IssueExitAuthorization succeeded. exit_authorization_id={ExitAuthorizationId} authorization_status={AuthorizationStatus}",
                result.ExitAuthorizationId,
                result.AuthorizationStatus);

            return result;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ExitAuthorizationIssuanceConflictException)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogWarning(
                ex,
                "DB IssueExitAuthorization rejected by deterministic business rules. payment_attempt_id={PaymentAttemptId}",
                request.PaymentAttemptId);

            throw;
        }
        catch (Exception ex)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogError(
                ex,
                "DB IssueExitAuthorization failed. payment_attempt_id={PaymentAttemptId}",
                request.PaymentAttemptId);

            throw;
        }
    }

    private static async Task ValidateConfirmedPaymentAttemptPayableBasisAsync(
        NpgsqlConnection connection,
        IssueExitAuthorizationDbRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                pa.payment_attempt_id,
                pa.parking_session_id AS attempt_parking_session_id,
                pa.tariff_snapshot_id,
                pa.amount AS attempt_amount,
                pa.currency_code::text AS attempt_currency_code,
                pa.attempt_status::text AS attempt_status,
                pa.finalized_at,
                ps.session_status::text AS session_status,
                ts.tariff_snapshot_id AS persisted_tariff_snapshot_id,
                ts.parking_session_id AS tariff_parking_session_id,
                ts.net_amount AS tariff_net_amount,
                ts.currency_code::text AS tariff_currency_code,
                pc.payment_confirmation_id,
                pc.confirmed_amount,
                pc.currency_code::text AS confirmation_currency_code
            FROM core.payment_attempts AS pa
            LEFT JOIN core.parking_sessions AS ps
                ON ps.parking_session_id = pa.parking_session_id
            LEFT JOIN core.tariff_snapshots AS ts
                ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
            LEFT JOIN LATERAL (
                SELECT
                    payment_confirmation_id,
                    confirmed_amount,
                    currency_code
                FROM core.payment_confirmations
                WHERE payment_attempt_id = pa.payment_attempt_id
                  AND confirmation_status = 'RECORDED'
                ORDER BY confirmed_at DESC, created_at DESC
                LIMIT 1
            ) AS pc ON TRUE
            WHERE pa.payment_attempt_id = @payment_attempt_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("payment_attempt_id", request.PaymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException($"payment attempt {request.PaymentAttemptId} was not found");
        }

        var attemptParkingSessionId = reader.GetGuid(reader.GetOrdinal("attempt_parking_session_id"));
        if (attemptParkingSessionId != request.ParkingSessionId)
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_ATTEMPT_PARKING_SESSION_MISMATCH",
                "Payment attempt does not belong to the requested parking session.");
        }

        var attemptStatus = reader.GetString(reader.GetOrdinal("attempt_status"));
        if (!string.Equals(attemptStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_ATTEMPT_NOT_CONFIRMED",
                "Payment attempt is not confirmed and cannot issue exit authorization.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("finalized_at")))
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_ATTEMPT_NOT_FINALIZED",
                "Payment attempt is not finalized and cannot issue exit authorization.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("persisted_tariff_snapshot_id")))
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_ATTEMPT_TARIFF_SNAPSHOT_MISSING",
                "Payment attempt does not have a persisted tariff snapshot.");
        }

        var tariffParkingSessionId = reader.GetGuid(reader.GetOrdinal("tariff_parking_session_id"));
        if (attemptParkingSessionId != tariffParkingSessionId)
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_ATTEMPT_TARIFF_SNAPSHOT_MISMATCH",
                "Payment attempt tariff snapshot belongs to a different parking session.");
        }

        var sessionStatusOrdinal = reader.GetOrdinal("session_status");
        if (reader.IsDBNull(sessionStatusOrdinal) ||
            !string.Equals(reader.GetString(sessionStatusOrdinal), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PARKING_SESSION_NOT_ELIGIBLE_FOR_EXIT",
                "Parking session is not eligible for exit authorization.");
        }

        var attemptAmount = reader.GetDecimal(reader.GetOrdinal("attempt_amount"));
        var tariffAmount = reader.GetDecimal(reader.GetOrdinal("tariff_net_amount"));
        if (attemptAmount != tariffAmount)
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_AMOUNT_MISMATCH",
                "Payment attempt amount does not match its persisted tariff snapshot payable amount.");
        }

        var attemptCurrency = reader.GetString(reader.GetOrdinal("attempt_currency_code")).Trim();
        var tariffCurrency = reader.GetString(reader.GetOrdinal("tariff_currency_code")).Trim();
        if (!string.Equals(attemptCurrency, tariffCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_CURRENCY_MISMATCH",
                "Payment attempt currency does not match its persisted tariff snapshot currency.");
        }

        if (reader.IsDBNull(reader.GetOrdinal("payment_confirmation_id")))
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_ATTEMPT_NOT_CONFIRMED",
                "Payment attempt has no recorded payment confirmation.");
        }

        var confirmedAmount = reader.GetDecimal(reader.GetOrdinal("confirmed_amount"));
        if (confirmedAmount != attemptAmount)
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_AMOUNT_MISMATCH",
                "Payment confirmation amount does not match the payment attempt amount.");
        }

        var confirmationCurrency = reader.GetString(reader.GetOrdinal("confirmation_currency_code")).Trim();
        if (!string.Equals(confirmationCurrency, attemptCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ExitAuthorizationIssuanceConflictException(
                "PAYMENT_CURRENCY_MISMATCH",
                "Payment confirmation currency does not match the payment attempt currency.");
        }
    }
}
