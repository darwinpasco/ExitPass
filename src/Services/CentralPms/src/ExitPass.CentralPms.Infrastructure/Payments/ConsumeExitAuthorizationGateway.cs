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
        _connectionString = connectionString;
        _logger = logger;
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

            await using var dbCommand = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = 30
            };

            dbCommand.Parameters.AddWithValue("p_exit_authorization_id", request.ExitAuthorizationId);
            dbCommand.Parameters.AddWithValue("p_requested_by", request.RequestedByUserId);
            dbCommand.Parameters.AddWithValue("p_correlation_id", request.CorrelationId);
            dbCommand.Parameters.AddWithValue("p_now", request.RequestedAt);

            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("consume_exit_authorization() returned no rows.");
            }

            var result = new ConsumeExitAuthorizationDbResult(
                ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
                AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
                ConsumedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("consumed_at")));

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
        catch (Exception ex)
        {
            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("db.duration_ms", duration.TotalMilliseconds);

            _logger.LogError(
                ex,
                "DB ConsumeExitAuthorization failed. exit_authorization_id={ExitAuthorizationId}",
                request.ExitAuthorizationId);

            throw;
        }
    }
}
