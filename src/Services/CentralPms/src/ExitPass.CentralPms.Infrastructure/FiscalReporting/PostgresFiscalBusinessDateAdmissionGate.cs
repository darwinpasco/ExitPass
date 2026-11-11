using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalReporting;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.FiscalReporting;

public sealed class PostgresFiscalBusinessDateAdmissionGate(
    string connectionString,
    IFiscalReportingPosServerGateway posServerGateway) : IFiscalBusinessDateAdmissionGate
{
    public Task<IFiscalBusinessDateAdmissionLease> AcquireOpenAsync(
        Guid siteId,
        Guid correlationId,
        string actorReference,
        CancellationToken cancellationToken) =>
        AcquireAsync(siteId, correlationId, actorReference, requireOpen: true, cancellationToken);

    public Task<IFiscalBusinessDateAdmissionLease> AcquireTransitionAsync(
        Guid siteId,
        CancellationToken cancellationToken) =>
        AcquireAsync(siteId, Guid.NewGuid(), "central-pms-fiscal-lifecycle", requireOpen: false, cancellationToken);

    public async Task<IFiscalBusinessDateAdmissionLease> AcquirePaymentSettlementAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        if (paymentAttemptId == Guid.Empty)
            throw new FiscalBusinessDateAdmissionException(FiscalBusinessDateAdmissionCodes.Unavailable, "Payment attempt is required for settlement serialization.");

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var resolve = new NpgsqlCommand("""
                SELECT session.site_id
                FROM core.payment_attempts attempt
                JOIN core.parking_sessions session ON session.parking_session_id=attempt.parking_session_id
                WHERE attempt.payment_attempt_id=@attempt;
                """, connection);
            resolve.Parameters.AddWithValue("attempt", paymentAttemptId);
            var value = await resolve.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is not Guid siteId || siteId == Guid.Empty)
                throw new FiscalBusinessDateAdmissionException(FiscalBusinessDateAdmissionCodes.Unavailable, "The payment attempt Site could not be resolved for settlement serialization.");

            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_lock(hashtextextended(@scope, 0));",
                connection);
            command.Parameters.AddWithValue("scope", $"fiscal-business-date-admission:{siteId:N}");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(connection, siteId, null);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IFiscalBusinessDateAdmissionLease> AcquireAsync(
        Guid siteId,
        Guid correlationId,
        string actorReference,
        bool requireOpen,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            throw new FiscalBusinessDateAdmissionException(FiscalBusinessDateAdmissionCodes.Unavailable, "Site is required for fiscal business-date admission.");

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = new NpgsqlCommand(
                "SELECT pg_advisory_lock(hashtextextended(@scope, 0));",
                connection))
            {
                command.Parameters.AddWithValue("scope", $"fiscal-business-date-admission:{siteId:N}");
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var response = await posServerGateway.SendAsync(new(
                siteId,
                FiscalReportingGatewayAction.BusinessDateLifecycleRead,
                correlationId,
                string.IsNullOrWhiteSpace(actorReference) ? "central-pms" : actorReference.Trim()), cancellationToken).ConfigureAwait(false);
            if (response.HttpStatusCode is < 200 or >= 300)
            {
                throw new FiscalBusinessDateAdmissionException(
                    FiscalBusinessDateAdmissionCodes.Unavailable,
                    "Authoritative fiscal business-date readiness is unavailable.",
                    retryable: response.Retryable || response.HttpStatusCode >= 500);
            }

            DateTimeOffset? activeOpenedAt;
            string lifecycleState;
            try
            {
                using var document = JsonDocument.Parse(response.Body);
                if (!document.RootElement.TryGetProperty("lifecycle", out var lifecycle) ||
                    lifecycle.ValueKind != JsonValueKind.Object ||
                    !lifecycle.TryGetProperty("state", out var state) ||
                    state.ValueKind != JsonValueKind.String)
                {
                    throw new FiscalBusinessDateAdmissionException(
                        FiscalBusinessDateAdmissionCodes.Unavailable,
                        "Authoritative fiscal business-date readiness could not be verified.",
                        retryable: true);
                }
                lifecycleState = state.GetString()!;
                activeOpenedAt = TryReadActiveOpenedAt(lifecycle);
            }
            catch (JsonException)
            {
                throw new FiscalBusinessDateAdmissionException(
                    FiscalBusinessDateAdmissionCodes.Unavailable,
                    "Authoritative fiscal business-date readiness could not be verified.",
                    retryable: true);
            }

            if (requireOpen && (!string.Equals(lifecycleState, "OPEN", StringComparison.Ordinal) || !activeOpenedAt.HasValue))
            {
                throw new FiscalBusinessDateAdmissionException(
                    FiscalBusinessDateAdmissionCodes.NotOpen,
                    "Payment acceptance is unavailable because no fiscal business date is OPEN.");
            }

            var lease = new Lease(connection, siteId, activeOpenedAt);
            return lease;
        }
        catch
        {
            if (connection.FullState != System.Data.ConnectionState.Closed)
                await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static DateTimeOffset? TryReadActiveOpenedAt(JsonElement lifecycle)
    {
        if (!lifecycle.TryGetProperty("activePeriod", out var active) || active.ValueKind != JsonValueKind.Object ||
            !active.TryGetProperty("openedAt", out var openedAt) || openedAt.ValueKind != JsonValueKind.String ||
            !openedAt.TryGetDateTimeOffset(out var parsed))
        {
            return null;
        }
        return parsed;
    }

    private sealed class Lease(NpgsqlConnection connection, Guid siteId, DateTimeOffset? activeOpenedAt) : IFiscalBusinessDateAdmissionLease
    {
        private bool _disposed;

        public async Task EnsureSafeToCloseAsync(CancellationToken cancellationToken)
        {
            if (!activeOpenedAt.HasValue)
            {
                throw new FiscalBusinessDateAdmissionException(
                    FiscalBusinessDateAdmissionCodes.NotOpen,
                    "No OPEN fiscal business date is available to close.");
            }

            const string sql = """
                SELECT
                    (SELECT count(*)
                     FROM core.payment_attempts attempt
                     JOIN core.parking_sessions session ON session.parking_session_id=attempt.parking_session_id
                     WHERE session.site_id=@site_id
                       AND attempt.created_at >= @opened_at
                       AND attempt.attempt_status='CONFIRMED'::core.payment_attempt_status_enum
                       AND NOT EXISTS (
                           SELECT 1
                           FROM core.fiscal_issuance_references fiscal
                           WHERE fiscal.payment_attempt_id=attempt.payment_attempt_id
                             AND fiscal.fiscal_issuance_state IN (
                                 'FISCAL_ISSUANCE_RECORDED',
                                 'FISCAL_ISSUANCE_REPLAYED',
                                 'FISCAL_ISSUANCE_RECONCILED')))
                  + (SELECT count(*)
                     FROM core.fiscal_issuance_references fiscal
                     WHERE fiscal.site_id=@site_id
                       AND fiscal.first_recorded_at >= @opened_at
                       AND fiscal.fiscal_issuance_state NOT IN (
                           'NOT_REQUIRED',
                           'FISCAL_ISSUANCE_RECORDED',
                           'FISCAL_ISSUANCE_REPLAYED',
                           'FISCAL_ISSUANCE_RECONCILED'));
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("site_id", siteId);
            command.Parameters.AddWithValue("opened_at", activeOpenedAt.Value);
            var unresolved = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);
            if (unresolved != 0)
            {
                throw new FiscalBusinessDateAdmissionException(
                    FiscalBusinessDateAdmissionCodes.UnsafeToClose,
                    "The fiscal business date has unresolved payment operations and cannot be closed.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(hashtextextended(@scope, 0));",
                    connection);
                command.Parameters.AddWithValue("scope", $"fiscal-business-date-admission:{siteId:N}");
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
