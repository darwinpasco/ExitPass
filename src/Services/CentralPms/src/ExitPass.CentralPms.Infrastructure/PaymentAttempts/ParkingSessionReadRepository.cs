using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Sessions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

public sealed class ParkingSessionReadRepository : IParkingSessionReadRepository
{
    private readonly string _connectionString;

    public ParkingSessionReadRepository(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <summary>
    /// BRD:
    /// - 9.9 Payment Initiation
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    ///
    /// Invariants Enforced:
    /// - payment initiation must read canonical ParkingSession state from Central PMS persistence
    /// - session eligibility must be evaluated from the stored control-layer record, not from external providers directly
    /// </summary>
    public async Task<ParkingSession?> GetByIdAsync(Guid parkingSessionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_code,
                vendor_session_ref,
                identifier_type,
                plate_number,
                ticket_number,
                entry_timestamp,
                session_status
            FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

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

    private static ParkingSessionStatus MapParkingSessionStatus(string dbValue)
    {
        return dbValue.ToUpperInvariant() switch
        {
            "OPEN" => ParkingSessionStatus.Open,
            "PAYMENT_REQUIRED" => ParkingSessionStatus.PaymentRequired,
            "PAYMENT_IN_PROGRESS" => ParkingSessionStatus.PaymentInProgress,
            "PAID" => ParkingSessionStatus.Paid,
            "EXIT_AUTHORIZED" => ParkingSessionStatus.ExitAuthorized,
            "EXITED" => ParkingSessionStatus.Exited,
            "CLOSED" => ParkingSessionStatus.Closed,
            _ => throw new InvalidOperationException($"Unsupported parking session status '{dbValue}'.")
        };
    }
}
