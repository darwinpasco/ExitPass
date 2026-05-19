using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Sessions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

/// <summary>
/// Repository for reading canonical parking-session control state from Central PMS persistence.
///
/// BRD v1.2:
/// - Section 9.9, Payment Initiation
///
/// SDD v1.2:
/// - Section 6.3, Initiate Payment Attempt
///
/// ExitPass v1.2 Database Design:
/// - Section 12.5.2, ParkingSession
/// - Section 12.5.8, Core Relationships
/// - Section 12.5.9, Cross-Entity Invariants
/// - Section 13.1.2, core.parking_sessions
///
/// Invariants enforced:
/// - ParkingSession is the canonical parent of the core control chain.
/// - Payment initiation must read canonical ParkingSession state from Central PMS persistence.
/// - Session eligibility must be evaluated from the stored control-layer record, not directly from Vendor PMS.
/// - Vendor PMS identity must be resolved through core.parking_sessions.vendor_system_id to integration.vendor_systems.vendor_system_id.
/// - The repository must not read obsolete v1.0/v1.1 columns from core.parking_sessions.
/// </summary>
public sealed class ParkingSessionReadRepository : IParkingSessionReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a repository using the configured Central PMS PostgreSQL connection string.
    ///
    /// BRD v1.2:
    /// - Section 9.9, Payment Initiation
    ///
    /// SDD v1.2:
    /// - Section 6.3, Initiate Payment Attempt
    ///
    /// ExitPass v1.2 Database Design:
    /// - Section 13.1.2, core.parking_sessions
    ///
    /// Invariant enforced:
    /// - Payment initiation must fail closed when authoritative persistence is not configured.
    /// </summary>
    public ParkingSessionReadRepository(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <summary>
    /// Reads a canonical ParkingSession by its v1.2 UUID identifier.
    ///
    /// BRD v1.2:
    /// - Section 9.9, Payment Initiation
    ///
    /// SDD v1.2:
    /// - Section 6.3, Initiate Payment Attempt
    ///
    /// ExitPass v1.2 Database Design:
    /// - Section 12.5.2, ParkingSession
    /// - Section 12.5.8, Core Relationships
    /// - Section 12.5.9, Cross-Entity Invariants
    /// - Section 13.1.2, core.parking_sessions
    ///
    /// Invariants enforced:
    /// - ParkingSession is the canonical parent of the control chain:
    ///   ParkingSession → TariffSnapshot → PaymentAttempt → PaymentConfirmation → ExitAuthorization.
    /// - PaymentAttempt creation must not bypass ParkingSession.
    /// - Vendor system code is traceability metadata resolved through integration.vendor_systems, not a core parking_sessions column.
    /// - Site and site-group identifiers remain UUIDs in storage, even if the current domain model still represents them as strings.
    /// </summary>
    public async Task<ParkingSession?> GetByIdAsync(
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            /*
             * BRD v1.2:
             * - Section 9.9, Payment Initiation
             *
             * SDD v1.2:
             * - Section 6.3, Initiate Payment Attempt
             *
             * ExitPass v1.2 Database Design:
             * - Section 12.5.2, ParkingSession
             * - Section 12.5.8, Core Relationships
             * - Section 12.5.9, Cross-Entity Invariants
             * - Section 13.1.2, core.parking_sessions
             *
             * Invariants enforced:
             * - ParkingSession anchors the payment-linked control chain.
             * - Vendor PMS identity is resolved by FK:
             *   core.parking_sessions.vendor_system_id → integration.vendor_systems.vendor_system_id.
             * - Obsolete v1.0/v1.1 fields are not read from core.parking_sessions.
             */
            SELECT
                ps.parking_session_id,
                ps.site_group_id::text AS site_group_id,
                ps.site_id::text AS site_id,
                vs.vendor_code AS vendor_system_code,
                COALESCE(ps.vendor_session_ref, '') AS vendor_session_ref,
                CASE
                    WHEN ps.plate_number_masked IS NOT NULL OR ps.plate_number_hash IS NOT NULL THEN 'PLATE'
                    WHEN ps.ticket_number_masked IS NOT NULL OR ps.ticket_number_hash IS NOT NULL THEN 'TICKET'
                    WHEN ps.vendor_session_ref IS NOT NULL THEN 'VENDOR_SESSION_REF'
                    ELSE 'UNKNOWN'
                END AS identifier_type,
                ps.plate_number_masked AS plate_number,
                ps.ticket_number_masked AS ticket_number,
                COALESCE(ps.entry_at, ps.created_at) AS entry_timestamp,
                ps.session_status::text AS session_status
            FROM core.parking_sessions AS ps
            INNER JOIN integration.vendor_systems AS vs
                ON vs.vendor_system_id = ps.vendor_system_id
            WHERE ps.parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ParkingSession.Rehydrate(
            parkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            siteGroupId: reader.GetString(reader.GetOrdinal("site_group_id")),
            siteId: reader.GetString(reader.GetOrdinal("site_id")),
            vendorSystemCode: reader.GetString(reader.GetOrdinal("vendor_system_code")),
            vendorSessionRef: reader.GetString(reader.GetOrdinal("vendor_session_ref")),
            identifierType: reader.GetString(reader.GetOrdinal("identifier_type")),
            plateNumber: reader.IsDBNull(reader.GetOrdinal("plate_number"))
                ? null
                : reader.GetString(reader.GetOrdinal("plate_number")),
            ticketNumber: reader.IsDBNull(reader.GetOrdinal("ticket_number"))
                ? null
                : reader.GetString(reader.GetOrdinal("ticket_number")),
            entryTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("entry_timestamp")),
            sessionStatus: MapParkingSessionStatus(reader.GetString(reader.GetOrdinal("session_status"))));
    }

    /// <summary>
    /// Maps ExitPass v1.2 persistence session statuses to the current Central PMS domain model.
    ///
    /// BRD v1.2:
    /// - Section 9.9, Payment Initiation
    ///
    /// SDD v1.2:
    /// - Section 6.3, Initiate Payment Attempt
    ///
    /// ExitPass v1.2 Database Design:
    /// - Section 12.5.2, ParkingSession
    /// - Section 13.1.2, core.parking_sessions
    ///
    /// Invariants enforced:
    /// - ParkingSession is the canonical parent of the payment-linked control chain.
    /// - Payment initiation may proceed only from a session that remains eligible for ExitPass control.
    /// - v1.2 persistence status ACTIVE means the parking session is still eligible for ExitPass workflows.
    /// </summary>
    private static ParkingSessionStatus MapParkingSessionStatus(string dbValue)
    {
        return dbValue.ToUpperInvariant() switch
        {
            // v1.2 persistence enum value.
            // The current domain model still treats payment-initiation eligibility as PaymentRequired.
            "ACTIVE" => ParkingSessionStatus.PaymentRequired,

            // v1.2 terminal / non-eligible session states.
            "CLOSED" => ParkingSessionStatus.Closed,
            "EXPIRED" => ParkingSessionStatus.Closed,
            "INVALIDATED" => ParkingSessionStatus.Closed,

            // Legacy compatibility while older domain/test paths still exist.
            "OPEN" => ParkingSessionStatus.Open,
            "PAYMENT_REQUIRED" => ParkingSessionStatus.PaymentRequired,
            "PAYMENT_IN_PROGRESS" => ParkingSessionStatus.PaymentInProgress,
            "PAID" => ParkingSessionStatus.Paid,
            "EXIT_AUTHORIZED" => ParkingSessionStatus.ExitAuthorized,
            "EXITED" => ParkingSessionStatus.Exited,

            _ => throw new InvalidOperationException($"Unsupported parking session status '{dbValue}'.")
        };
    }

}
