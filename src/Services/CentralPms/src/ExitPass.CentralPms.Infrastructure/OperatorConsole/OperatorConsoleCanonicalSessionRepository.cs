using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

public sealed class OperatorConsoleCanonicalSessionRepository(string connectionString)
    : IOperatorConsoleCanonicalSessionRepository
{
    private static readonly Guid CentralPmsServiceIdentityId =
        Guid.Parse("12000000-0000-0000-0000-000000000001");

    public async Task<OperatorConsoleCanonicalSessionPersistenceResult> EnsureAsync(
        OperatorConsoleCanonicalSessionPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var projection = command.Projection;
        var vendorSessionReference = Trim(projection.VendorRecordGuid) ?? Trim(projection.StableIdentityKey)
            ?? throw new OperatorConsoleCanonicalSessionException("OPERATOR_SESSION_PROJECTION_INVALID");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var identityKey = $"{projection.SiteGroupId:D}:{projection.SiteId:D}:{projection.VendorSystemId:D}:{vendorSessionReference}";
        await AcquireIdentityLockAsync(connection, transaction, identityKey, cancellationToken);

        var existing = await FindExistingAsync(
            connection,
            transaction,
            projection.SiteGroupId!.Value,
            projection.SiteId!.Value,
            projection.VendorSystemId!.Value,
            vendorSessionReference,
            projection.CardNum,
            cancellationToken);
        if (existing.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return new OperatorConsoleCanonicalSessionPersistenceResult(existing.Value, ReusedExistingSession: true);
        }

        var parkingSessionId = Guid.NewGuid();
        const string insertSql = """
            INSERT INTO core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
                source_adapter_identity_id,
                canonical_vehicle_type_code,
                vendor_session_ref,
                plate_number_hash,
                plate_number_masked,
                ticket_number_hash,
                ticket_number_masked,
                entry_at,
                vendor_session_status,
                session_status,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version)
            VALUES (
                @parking_session_id,
                @site_group_id,
                @site_id,
                @vendor_system_id,
                @source_adapter_identity_id,
                @canonical_vehicle_type_code,
                @vendor_session_ref,
                @plate_number_hash,
                @plate_number_masked,
                @ticket_number_hash,
                @ticket_number_masked,
                @entry_at,
                'PAYMENT_REQUIRED',
                'ACTIVE',
                @correlation_id,
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1);
            """;

        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
            insert.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = projection.SiteGroupId.Value;
            insert.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = projection.SiteId.Value;
            insert.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = projection.VendorSystemId.Value;
            insert.Parameters.Add("source_adapter_identity_id", NpgsqlDbType.Uuid).Value = projection.SourceAdapterIdentityId!.Value;
            insert.Parameters.Add("canonical_vehicle_type_code", NpgsqlDbType.Varchar).Value = DbValue(NormalizeIdentifier(projection.CanonicalVehicleTypeCode));
            insert.Parameters.Add("vendor_session_ref", NpgsqlDbType.Varchar).Value = vendorSessionReference;
            insert.Parameters.Add("plate_number_hash", NpgsqlDbType.Char).Value = DbValue(HashIdentifier(projection.PlateLicense));
            insert.Parameters.Add("plate_number_masked", NpgsqlDbType.Varchar).Value = DbValue(NormalizeIdentifier(projection.PlateLicense));
            insert.Parameters.Add("ticket_number_hash", NpgsqlDbType.Char).Value = DbValue(HashIdentifier(projection.CardNum));
            insert.Parameters.Add("ticket_number_masked", NpgsqlDbType.Varchar).Value = DbValue(Trim(projection.CardNum));
            insert.Parameters.Add("entry_at", NpgsqlDbType.TimestampTz).Value = projection.EnterTime!.Value.ToUniversalTime();
            insert.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
            insert.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new OperatorConsoleCanonicalSessionPersistenceResult(parkingSessionId, ReusedExistingSession: false);
    }

    private static async Task AcquireIdentityLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string identityKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@identity_key, 0));",
            connection,
            transaction);
        command.Parameters.AddWithValue("identity_key", identityKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> FindExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteGroupId,
        Guid siteId,
        Guid vendorSystemId,
        string vendorSessionReference,
        string? ticketReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT parking_session_id
            FROM core.parking_sessions
            WHERE site_group_id = @site_group_id
              AND site_id = @site_id
              AND vendor_system_id = @vendor_system_id
              AND (
                    vendor_session_ref = @vendor_session_ref
                 OR (@ticket_hash IS NOT NULL AND ticket_number_hash = @ticket_hash)
              )
            ORDER BY created_at
            LIMIT 2;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.AddWithValue("vendor_session_ref", vendorSessionReference);
        command.Parameters.Add("ticket_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(ticketReference));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = reader.GetGuid(0);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new OperatorConsoleCanonicalSessionException("OPERATOR_CANONICAL_SESSION_IDENTITY_CONFLICT");
        }

        return result;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? HashIdentifier(string? value)
    {
        var normalized = NormalizeIdentifier(value);
        return normalized is null
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
