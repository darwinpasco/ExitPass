using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.WebPay;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.WebPay;

/// <summary>
/// Read-only PostgreSQL rediscovery repository for existing WebPay statutory pending lifecycles.
/// </summary>
public sealed class PostgresWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository
    : IWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository
{
    private const string WebPaySourceChannel = "WEBPAY";
    private readonly string _connectionString;

    public PostgresWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<WebPayStatutoryDiscountPendingLifecycleSessionLookupResult> ResolveSessionAsync(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sessions = query.LookupMode.Trim().ToUpperInvariant() switch
            {
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId =>
                    await ReadByParkingSessionIdAsync(connection, query.ParkingSessionId!.Value, cancellationToken).ConfigureAwait(false),
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeTicketReference =>
                    await ReadByTicketReferenceAsync(connection, query.TicketReference!, cancellationToken).ConfigureAwait(false),
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModePlateNumber =>
                    await ReadByPlateNumberAsync(connection, query.PlateNumber!, cancellationToken).ConfigureAwait(false),
                _ => []
            };

            if (sessions.Count == 0)
            {
                return new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NotFound,
                    Session: null);
            }

            var scoped = sessions
                .Where(session => session.SiteId == query.SiteId && session.SiteGroupId == query.SiteGroupId)
                .GroupBy(session => session.ParkingSessionId)
                .Select(group => group.OrderByDescending(session => session.UpdatedAt).First())
                .OrderByDescending(session => session.UpdatedAt)
                .ToArray();

            if (scoped.Length == 0)
            {
                return new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AccessDenied,
                    Session: null);
            }

            if (scoped.Length > 1)
            {
                return new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AmbiguousSession,
                    Session: null);
            }

            return new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found,
                scoped[0]);
        }
        catch (NpgsqlException)
        {
            return new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable,
                Session: null,
                Retryable: true);
        }
        catch (TimeoutException)
        {
            return new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable,
                Session: null,
                Retryable: true);
        }
    }

    public async Task<WebPayStatutoryDiscountPendingLifecycleRecord?> FindLatestLifecycleAsync(
        Guid parkingSessionId,
        Guid siteId,
        Guid siteGroupId,
        string? entitlementType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                d.statutory_discount_decision_command_id,
                d.request_reference,
                d.parking_session_id,
                COALESCE(r.site_id, ps.site_id) AS site_id,
                COALESCE(r.site_group_id, ps.site_group_id) AS site_group_id,
                d.entitlement_type,
                d.command_status,
                d.decision_result_status,
                d.retryable AS decision_retryable,
                d.created_at,
                d.updated_at,
                d.decided_at,
                r.review_status,
                r.submitted_at,
                r.reviewed_at,
                a.command_status AS application_command_status,
                a.result_classification AS application_result_classification,
                a.retryable AS application_retryable,
                a.updated_at AS application_updated_at
            FROM discounts.statutory_discount_decision_commands AS d
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = d.parking_session_id
            LEFT JOIN operator_console.statutory_discount_service_channel_reviews AS r
              ON r.statutory_discount_decision_command_id = d.statutory_discount_decision_command_id
            LEFT JOIN LATERAL (
                SELECT
                    command_status,
                    result_classification,
                    retryable,
                    updated_at
                FROM discounts.statutory_discount_payable_basis_application_commands
                WHERE statutory_discount_decision_command_id = d.statutory_discount_decision_command_id
                ORDER BY updated_at DESC, created_at DESC
                LIMIT 1
            ) AS a ON TRUE
            WHERE d.parking_session_id = @parking_session_id
              AND d.source_channel = @source_channel
              AND COALESCE(r.site_id, ps.site_id) = @site_id
              AND COALESCE(r.site_group_id, ps.site_group_id) = @site_group_id
              AND (@entitlement_type IS NULL OR d.entitlement_type = @entitlement_type)
            ORDER BY
                CASE
                    WHEN d.command_status = 'AWAITING_REVIEW' AND d.decision_result_status = 'NOT_DECIDED' THEN 0
                    WHEN d.decision_result_status = 'APPROVED' THEN 1
                    WHEN d.decision_result_status = 'REJECTED' THEN 2
                    ELSE 3
                END,
                GREATEST(d.updated_at, COALESCE(r.updated_at, d.updated_at), COALESCE(a.updated_at, d.updated_at)) DESC,
                d.created_at DESC
            LIMIT 1;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
            command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
            command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
            command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
            command.Parameters.AddWithValue("source_channel", WebPaySourceChannel);
            command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = (object?)entitlementType ?? DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadLifecycle(reader)
                : null;
        }
        catch (NpgsqlException)
        {
            return new WebPayStatutoryDiscountPendingLifecycleRecord(
                Guid.Empty,
                Guid.Empty,
                Guid.Empty,
                string.Empty,
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable,
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable,
                parkingSessionId,
                siteId,
                siteGroupId,
                string.Empty,
                null,
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable,
                Retryable: true,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null,
                null);
        }
    }

    private static async Task<IReadOnlyList<WebPayStatutoryDiscountPendingLifecycleSession>> ReadByParkingSessionIdAsync(
        NpgsqlConnection connection,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ps.parking_session_id,
                ps.site_id,
                ps.site_group_id,
                COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS ticket_reference,
                ps.plate_number_masked,
                ps.updated_at
            FROM core.parking_sessions AS ps
            WHERE ps.parking_session_id = @parking_session_id
            LIMIT 2;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        return await ReadSessionsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<WebPayStatutoryDiscountPendingLifecycleSession>> ReadByTicketReferenceAsync(
        NpgsqlConnection connection,
        string ticketReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT ON (session_source.parking_session_id)
                session_source.parking_session_id,
                session_source.site_id,
                session_source.site_group_id,
                session_source.ticket_reference,
                session_source.plate_number_masked,
                session_source.updated_at
            FROM (
                SELECT
                    ps.parking_session_id,
                    ps.site_id,
                    ps.site_group_id,
                    COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS ticket_reference,
                    ps.plate_number_masked,
                    ps.updated_at
                FROM core.parking_sessions AS ps
                WHERE ps.ticket_number_masked = @ticket_reference
                   OR ps.vendor_session_ref = @ticket_reference
                   OR ps.ticket_number_hash = @ticket_reference_hash
                UNION ALL
                SELECT
                    ps.parking_session_id,
                    COALESCE(r.site_id, ps.site_id) AS site_id,
                    COALESCE(r.site_group_id, ps.site_group_id) AS site_group_id,
                    r.ticket_reference,
                    COALESCE(r.plate_number, ps.plate_number_masked) AS plate_number_masked,
                    GREATEST(ps.updated_at, r.updated_at) AS updated_at
                FROM operator_console.statutory_discount_service_channel_reviews AS r
                JOIN core.parking_sessions AS ps
                  ON ps.parking_session_id = r.parking_session_id
                WHERE r.source_channel = @source_channel
                  AND r.ticket_reference = @ticket_reference
            ) AS session_source
            ORDER BY session_source.parking_session_id, session_source.updated_at DESC
            LIMIT 3;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("ticket_reference", ticketReference.Trim());
        command.Parameters.AddWithValue("ticket_reference_hash", Sha256Hex(ticketReference));
        command.Parameters.AddWithValue("source_channel", WebPaySourceChannel);
        return await ReadSessionsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<WebPayStatutoryDiscountPendingLifecycleSession>> ReadByPlateNumberAsync(
        NpgsqlConnection connection,
        string plateNumber,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT ON (session_source.parking_session_id)
                session_source.parking_session_id,
                session_source.site_id,
                session_source.site_group_id,
                session_source.ticket_reference,
                session_source.plate_number_masked,
                session_source.updated_at
            FROM (
                SELECT
                    ps.parking_session_id,
                    ps.site_id,
                    ps.site_group_id,
                    COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS ticket_reference,
                    ps.plate_number_masked,
                    ps.updated_at
                FROM core.parking_sessions AS ps
                WHERE ps.plate_number_masked = @plate_number
                   OR ps.plate_number_hash = @plate_number_hash
                UNION ALL
                SELECT
                    ps.parking_session_id,
                    COALESCE(r.site_id, ps.site_id) AS site_id,
                    COALESCE(r.site_group_id, ps.site_group_id) AS site_group_id,
                    r.ticket_reference,
                    r.plate_number AS plate_number_masked,
                    GREATEST(ps.updated_at, r.updated_at) AS updated_at
                FROM operator_console.statutory_discount_service_channel_reviews AS r
                JOIN core.parking_sessions AS ps
                  ON ps.parking_session_id = r.parking_session_id
                WHERE r.source_channel = @source_channel
                  AND r.plate_number = @plate_number
            ) AS session_source
            ORDER BY session_source.parking_session_id, session_source.updated_at DESC
            LIMIT 3;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("plate_number", plateNumber.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("plate_number_hash", Sha256Hex(plateNumber));
        command.Parameters.AddWithValue("source_channel", WebPaySourceChannel);
        return await ReadSessionsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<WebPayStatutoryDiscountPendingLifecycleSession>> ReadSessionsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<WebPayStatutoryDiscountPendingLifecycleSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new WebPayStatutoryDiscountPendingLifecycleSession(
                reader.GetGuid(reader.GetOrdinal("parking_session_id")),
                reader.GetGuid(reader.GetOrdinal("site_id")),
                reader.GetGuid(reader.GetOrdinal("site_group_id")),
                GetNullableString(reader, "ticket_reference"),
                GetNullableString(reader, "plate_number_masked"),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"))));
        }

        return results;
    }

    private static WebPayStatutoryDiscountPendingLifecycleRecord ReadLifecycle(NpgsqlDataReader reader)
    {
        var decisionCommandStatus = reader.GetString(reader.GetOrdinal("command_status"));
        var decisionResultStatus = reader.GetString(reader.GetOrdinal("decision_result_status"));
        var decisionStatus = ResolveDecisionStatus(decisionCommandStatus, decisionResultStatus, GetNullableString(reader, "application_command_status"));
        var payableBasisStatus = ResolvePayableBasisStatus(
            decisionCommandStatus,
            decisionResultStatus,
            GetNullableString(reader, "application_command_status"));
        var lifecycleState = ResolveLifecycleState(decisionStatus, payableBasisStatus);
        var retryable = reader.GetBoolean(reader.GetOrdinal("decision_retryable"))
            || (GetNullableBool(reader, "application_retryable") ?? false)
            || decisionCommandStatus is StatutoryDiscountDecisionV2CommandStates.Received
                or StatutoryDiscountDecisionV2CommandStates.Processing
                or StatutoryDiscountDecisionV2CommandStates.AwaitingReview
            || GetNullableString(reader, "application_command_status") is StatutoryDiscountPayableBasisApplicationV1CommandStates.Received
                or StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing
                or StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedRetryable;

        var requestReference = reader.GetGuid(reader.GetOrdinal("request_reference"));
        var createdAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"));
        var decisionUpdatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"));
        var applicationUpdatedAt = GetNullableDateTimeOffset(reader, "application_updated_at");

        return new WebPayStatutoryDiscountPendingLifecycleRecord(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            requestReference,
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            decisionStatus,
            payableBasisStatus,
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("site_id")),
            reader.GetGuid(reader.GetOrdinal("site_group_id")),
            requestReference.ToString("D"),
            OpaqueContinuationUrl: null,
            lifecycleState,
            retryable,
            createdAt,
            Max(decisionUpdatedAt, applicationUpdatedAt),
            GetNullableDateTimeOffset(reader, "submitted_at"),
            GetNullableDateTimeOffset(reader, "decided_at"),
            GetNullableDateTimeOffset(reader, "reviewed_at"));
    }

    private static string ResolveDecisionStatus(
        string commandStatus,
        string decisionResultStatus,
        string? applicationCommandStatus) =>
        decisionResultStatus switch
        {
            StatutoryDiscountDecisionV2ResultStates.Approved
                when applicationCommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied =>
                "APPLIED_PAYABLE_BASIS",
            StatutoryDiscountDecisionV2ResultStates.Approved => "APPROVED",
            StatutoryDiscountDecisionV2ResultStates.Rejected => "REJECTED",
            _ when commandStatus is StatutoryDiscountDecisionV2CommandStates.AwaitingReview => "AWAITING_REVIEW",
            _ => commandStatus
        };

    private static string ResolvePayableBasisStatus(
        string decisionCommandStatus,
        string decisionResultStatus,
        string? applicationCommandStatus)
    {
        if (decisionCommandStatus is StatutoryDiscountDecisionV2CommandStates.AwaitingReview)
        {
            return "AWAITING_REVIEW";
        }

        if (decisionResultStatus is StatutoryDiscountDecisionV2ResultStates.Rejected)
        {
            return "DECISION_REJECTED";
        }

        if (decisionResultStatus is StatutoryDiscountDecisionV2ResultStates.Approved
            && string.IsNullOrWhiteSpace(applicationCommandStatus))
        {
            return "DECISION_APPROVED_APPLICATION_NOT_REQUESTED";
        }

        return applicationCommandStatus switch
        {
            StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied => "PAYABLE_BASIS_READY",
            StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedRetryable => "RETRYABLE_FAILURE",
            StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable => "TERMINAL_FAILURE",
            StatutoryDiscountPayableBasisApplicationV1CommandStates.Received or
                StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing => "APPLICATION_PROCESSING",
            _ => "NOT_READY"
        };
    }

    private static string ResolveLifecycleState(string decisionStatus, string payableBasisStatus) =>
        decisionStatus switch
        {
            "AWAITING_REVIEW" => "PENDING_REVIEW",
            "APPROVED" => "APPROVED_PENDING_PAYMENT_APPLICATION",
            "REJECTED" => "REJECTED",
            "APPLIED_PAYABLE_BASIS" => "APPLIED",
            _ when payableBasisStatus is "RETRYABLE_FAILURE" => "RECOVERABLE_FAILURE",
            _ when payableBasisStatus is "TERMINAL_FAILURE" => "TERMINAL_FAILURE",
            _ => "PROCESSING"
        };

    private static string Sha256Hex(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset? second) =>
        second.HasValue && second.Value > first ? second.Value : first;

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool? GetNullableBool(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
