using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorParking;

/// <summary>
/// Persists provider-neutral vendor parking resolution data into Central PMS PostgreSQL storage.
/// </summary>
public sealed class VendorParkingResolutionPersistence : IVendorParkingResolutionPersistence
{
    private static readonly Guid CentralPmsServiceIdentityId =
        Guid.Parse("12000000-0000-0000-0000-000000000001");

    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="VendorParkingResolutionPersistence"/> class.
    /// </summary>
    /// <param name="connectionString">Central PMS database connection string.</param>
    public VendorParkingResolutionPersistence(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<PersistVendorParkingResolutionResult> PersistAsync(
        PersistVendorParkingResolutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ParkingSession);
        ArgumentNullException.ThrowIfNull(request.TariffSnapshot);

        var siteGroupId = Guid.Parse(request.ParkingSession.SiteGroupId);
        var siteId = Guid.Parse(request.ParkingSession.SiteId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await EnsureReferenceRowsAsync(connection, transaction, request, siteGroupId, siteId, cancellationToken);

        var vendorSystemId = await GetVendorSystemIdAsync(
            connection,
            transaction,
            request.ParkingSession.VendorSystemCode,
            cancellationToken);

        var existingSession = await FindExistingSessionAsync(
            connection,
            transaction,
            siteGroupId,
            siteId,
            vendorSystemId,
            request.ParkingSession.VendorSessionRef,
            cancellationToken);

        var parkingSessionWasReused = existingSession is not null;
        var parkingSession = existingSession ?? request.ParkingSession;

        if (!parkingSessionWasReused)
        {
            await InsertParkingSessionAsync(
                connection,
                transaction,
                request,
                siteGroupId,
                siteId,
                vendorSystemId,
                cancellationToken);
        }

        var vendorTariffRef = ResolveVendorTariffReference(request.TariffSnapshot);
        var existingTariff = await FindExistingActiveTariffAsync(
            connection,
            transaction,
            parkingSession.ParkingSessionId,
            vendorTariffRef,
            cancellationToken);

        var tariffSnapshotWasReused = existingTariff is not null;
        var tariffSnapshot = existingTariff ?? RebindTariffSnapshot(request.TariffSnapshot, parkingSession.ParkingSessionId);

        if (!tariffSnapshotWasReused)
        {
            await RetireExistingActiveTariffsAsync(
                connection,
                transaction,
                tariffSnapshot.ParkingSessionId,
                cancellationToken);

            await InsertTariffSnapshotAsync(
                connection,
                transaction,
                tariffSnapshot,
                vendorSystemId,
                vendorTariffRef,
                request.CorrelationId,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new PersistVendorParkingResolutionResult
        {
            ParkingSession = parkingSession,
            TariffSnapshot = tariffSnapshot,
            ParkingSessionWasReused = parkingSessionWasReused,
            TariffSnapshotWasReused = tariffSnapshotWasReused
        };
    }

    private static async Task EnsureReferenceRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersistVendorParkingResolutionRequest request,
        Guid siteGroupId,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO identity.service_identities (
                service_identity_id,
                service_identity_code,
                service_identity_name,
                identity_type,
                identity_status,
                owning_service_name,
                credential_reference,
                credential_type,
                effective_from,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @service_identity_id,
                'CENTRAL_PMS_API',
                'Central PMS API',
                'DEVICE',
                'ACTIVE',
                'ExitPass.CentralPms.Api',
                NULL,
                'NONE',
                NOW() - INTERVAL '1 minute',
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            )
            ON CONFLICT (service_identity_id) DO NOTHING;

            INSERT INTO sites.site_groups (
                site_group_id,
                site_group_code,
                site_group_name,
                business_label,
                description,
                operator_entity_name,
                timezone_name,
                default_currency_code,
                site_group_status,
                public_lookup_enabled,
                default_payment_enabled,
                effective_from,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @site_group_id,
                @site_group_code,
                @site_group_name,
                'PROPERTY',
                'Vendor parking resolution',
                'ExitPass',
                'Asia/Manila',
                'PHP',
                'ACTIVE',
                TRUE,
                TRUE,
                NOW() - INTERVAL '1 minute',
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            )
            ON CONFLICT (site_group_id) DO NOTHING;

            INSERT INTO sites.sites (
                site_id,
                site_group_id,
                site_code,
                site_name,
                site_description,
                site_type,
                timezone_name,
                address_line1,
                address_line2,
                city,
                province,
                country_code,
                lgu_code,
                site_status,
                public_lookup_enabled,
                payment_enabled,
                effective_from,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @site_id,
                @site_group_id,
                @site_code,
                @site_name,
                'Vendor parking resolution',
                'MALL_PARKING',
                'Asia/Manila',
                'Vendor resolved site',
                NULL,
                'Quezon City',
                'Metro Manila',
                'PH',
                'QC',
                'ACTIVE',
                TRUE,
                TRUE,
                NOW() - INTERVAL '1 minute',
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            )
            ON CONFLICT (site_id) DO NOTHING;

            INSERT INTO integration.vendor_systems (
                vendor_system_id,
                vendor_code,
                vendor_name,
                vendor_system_type,
                vendor_system_status,
                environment_code,
                base_url_ref,
                api_version,
                owner_team,
                support_contact_ref,
                effective_from,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                gen_random_uuid(),
                @vendor_system_code,
                @vendor_system_name,
                'VENDOR_PMS',
                'ACTIVE',
                'TEST',
                'fake://vendor-pms',
                'v1',
                'ExitPass Engineering',
                'test-support',
                NOW() - INTERVAL '1 minute',
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            )
            ON CONFLICT (vendor_code, environment_code) DO UPDATE
            SET
                vendor_name = EXCLUDED.vendor_name,
                vendor_system_type = EXCLUDED.vendor_system_type,
                vendor_system_status = EXCLUDED.vendor_system_status,
                updated_at = NOW(),
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                row_version = integration.vendor_systems.row_version + 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.AddWithValue("site_group_code", $"SG-{siteGroupId:N}");
        command.Parameters.AddWithValue("site_group_name", $"Site Group {siteGroupId:N}");
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.AddWithValue("site_code", $"SITE-{siteId:N}");
        command.Parameters.AddWithValue("site_name", $"Site {siteId:N}");
        command.Parameters.AddWithValue("vendor_system_code", request.ParkingSession.VendorSystemCode);
        command.Parameters.AddWithValue("vendor_system_name", request.ParkingSession.VendorSystemCode);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid> GetVendorSystemIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string vendorSystemCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT vendor_system_id
            FROM integration.vendor_systems
            WHERE vendor_code = @vendor_system_code
              AND environment_code = 'TEST'
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("vendor_system_code", vendorSystemCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id
            ? id
            : throw new InvalidOperationException($"Vendor system '{vendorSystemCode}' was not persisted.");
    }

    private static async Task<ParkingSession?> FindExistingSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteGroupId,
        Guid siteId,
        Guid vendorSystemId,
        string vendorSessionRef,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ps.parking_session_id,
                ps.site_group_id::text AS site_group_id,
                ps.site_id::text AS site_id,
                vs.vendor_code AS vendor_system_code,
                ps.vendor_session_ref,
                CASE
                    WHEN ps.plate_number_masked IS NOT NULL OR ps.plate_number_hash IS NOT NULL THEN 'PLATE'
                    WHEN ps.ticket_number_masked IS NOT NULL OR ps.ticket_number_hash IS NOT NULL THEN 'TICKET'
                    ELSE 'VENDOR_SESSION_REF'
                END AS identifier_type,
                ps.plate_number_masked,
                ps.ticket_number_masked,
                COALESCE(ps.entry_at, ps.created_at) AS entry_timestamp,
                ps.session_status::text AS session_status
            FROM core.parking_sessions AS ps
            INNER JOIN integration.vendor_systems AS vs
                ON vs.vendor_system_id = ps.vendor_system_id
            WHERE ps.site_group_id = @site_group_id
              AND ps.site_id = @site_id
              AND ps.vendor_system_id = @vendor_system_id
              AND ps.vendor_session_ref = @vendor_session_ref
            ORDER BY ps.created_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.AddWithValue("vendor_session_ref", vendorSessionRef);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ParkingSession.Rehydrate(
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("site_group_id")),
            reader.GetString(reader.GetOrdinal("site_id")),
            reader.GetString(reader.GetOrdinal("vendor_system_code")),
            reader.GetString(reader.GetOrdinal("vendor_session_ref")),
            reader.GetString(reader.GetOrdinal("identifier_type")),
            reader.IsDBNull(reader.GetOrdinal("plate_number_masked")) ? null : reader.GetString(reader.GetOrdinal("plate_number_masked")),
            reader.IsDBNull(reader.GetOrdinal("ticket_number_masked")) ? null : reader.GetString(reader.GetOrdinal("ticket_number_masked")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("entry_timestamp")),
            MapParkingSessionStatus(reader.GetString(reader.GetOrdinal("session_status"))));
    }

    private static async Task InsertParkingSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersistVendorParkingResolutionRequest request,
        Guid siteGroupId,
        Guid siteId,
        Guid vendorSystemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
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
                row_version
            )
            VALUES (
                @parking_session_id,
                @site_group_id,
                @site_id,
                @vendor_system_id,
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
                1
            );
            """;

        var session = request.ParkingSession;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = session.ParkingSessionId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.AddWithValue("vendor_session_ref", session.VendorSessionRef);
        command.Parameters.Add("plate_number_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(session.PlateNumber));
        command.Parameters.Add("plate_number_masked", NpgsqlDbType.Text).Value = DbValue(session.PlateNumber);
        command.Parameters.Add("ticket_number_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(session.TicketNumber));
        command.Parameters.Add("ticket_number_masked", NpgsqlDbType.Text).Value = DbValue(session.TicketNumber);
        command.Parameters.Add("entry_at", NpgsqlDbType.TimestampTz).Value = session.EntryTimestamp;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = request.CorrelationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<TariffSnapshot?> FindExistingActiveTariffAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        string vendorTariffRef,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                tariff_snapshot_id,
                parking_session_id,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                currency_code,
                tariff_version_reference,
                calculated_at,
                expires_at,
                snapshot_status::text AS snapshot_status,
                superseded_by_tariff_snapshot_id
            FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id
              AND vendor_tariff_ref = @vendor_tariff_ref
              AND snapshot_status = 'ACTIVE'
              AND consumed_at IS NULL
              AND expires_at > NOW()
              AND superseded_by_tariff_snapshot_id IS NULL
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.AddWithValue("vendor_tariff_ref", vendorTariffRef);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return TariffSnapshot.Rehydrate(
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotSourceType.Base,
            reader.GetDecimal(reader.GetOrdinal("gross_amount")),
            reader.GetDecimal(reader.GetOrdinal("statutory_discount_amount")),
            reader.GetDecimal(reader.GetOrdinal("coupon_discount_amount")),
            reader.GetDecimal(reader.GetOrdinal("net_amount")),
            reader.GetString(reader.GetOrdinal("currency_code")),
            reader.GetDecimal(reader.GetOrdinal("gross_amount")),
            reader.IsDBNull(reader.GetOrdinal("tariff_version_reference")) ? null : reader.GetString(reader.GetOrdinal("tariff_version_reference")),
            null,
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("calculated_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expires_at")),
            MapTariffSnapshotStatus(reader.GetString(reader.GetOrdinal("snapshot_status"))),
            reader.IsDBNull(reader.GetOrdinal("superseded_by_tariff_snapshot_id")) ? null : reader.GetGuid(reader.GetOrdinal("superseded_by_tariff_snapshot_id")),
            null);
    }

    private static async Task RetireExistingActiveTariffsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET
                snapshot_status = CASE
                    WHEN expires_at <= NOW() THEN 'EXPIRED'::core.tariff_snapshot_status_enum
                    WHEN consumed_at IS NOT NULL THEN 'CONSUMED'::core.tariff_snapshot_status_enum
                    ELSE 'SUPERSEDED'::core.tariff_snapshot_status_enum
                END,
                updated_at = NOW(),
                updated_by_service_identity_id = @service_identity_id,
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id
              AND snapshot_status = 'ACTIVE';
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTariffSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TariffSnapshot tariffSnapshot,
        Guid vendorSystemId,
        string vendorTariffRef,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                superseded_by_tariff_snapshot_id,
                vendor_system_id,
                vendor_tariff_ref,
                tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                statutory_discount_validation_id,
                coupon_application_id,
                snapshot_status,
                calculated_at,
                expires_at,
                consumed_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @tariff_snapshot_id,
                @parking_session_id,
                NULL,
                @vendor_system_id,
                @vendor_tariff_ref,
                @tariff_version_reference,
                @currency_code,
                @gross_amount,
                @statutory_discount_amount,
                @coupon_discount_amount,
                @net_amount,
                NULL,
                NULL,
                'ACTIVE',
                @calculated_at,
                @expires_at,
                NULL,
                @correlation_id,
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshot.TariffSnapshotId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = tariffSnapshot.ParkingSessionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.AddWithValue("vendor_tariff_ref", vendorTariffRef);
        command.Parameters.Add("tariff_version_reference", NpgsqlDbType.Text).Value = DbValue(tariffSnapshot.TariffVersionReference);
        command.Parameters.AddWithValue("currency_code", tariffSnapshot.CurrencyCode);
        command.Parameters.AddWithValue("gross_amount", tariffSnapshot.GrossAmount);
        command.Parameters.AddWithValue("statutory_discount_amount", tariffSnapshot.StatutoryDiscountAmount);
        command.Parameters.AddWithValue("coupon_discount_amount", tariffSnapshot.CouponDiscountAmount);
        command.Parameters.AddWithValue("net_amount", tariffSnapshot.NetPayable);
        command.Parameters.Add("calculated_at", NpgsqlDbType.TimestampTz).Value = tariffSnapshot.CalculatedAt;
        command.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value = tariffSnapshot.ExpiresAt;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static TariffSnapshot RebindTariffSnapshot(TariffSnapshot tariffSnapshot, Guid parkingSessionId)
    {
        if (tariffSnapshot.ParkingSessionId == parkingSessionId)
        {
            return tariffSnapshot;
        }

        return TariffSnapshot.Rehydrate(
            Guid.NewGuid(),
            parkingSessionId,
            tariffSnapshot.SourceType,
            tariffSnapshot.GrossAmount,
            tariffSnapshot.StatutoryDiscountAmount,
            tariffSnapshot.CouponDiscountAmount,
            tariffSnapshot.NetPayable,
            tariffSnapshot.CurrencyCode,
            tariffSnapshot.BaseFeeAmount,
            tariffSnapshot.TariffVersionReference,
            tariffSnapshot.PolicyVersionReference,
            tariffSnapshot.CalculatedAt,
            tariffSnapshot.ExpiresAt,
            tariffSnapshot.SnapshotStatus,
            null,
            null);
    }

    private static string ResolveVendorTariffReference(TariffSnapshot tariffSnapshot)
    {
        return string.IsNullOrWhiteSpace(tariffSnapshot.TariffVersionReference)
            ? $"VTAR-{tariffSnapshot.TariffSnapshotId:N}"
            : tariffSnapshot.TariffVersionReference;
    }

    private static string? HashIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static object DbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static ParkingSessionStatus MapParkingSessionStatus(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "ACTIVE" => ParkingSessionStatus.PaymentRequired,
            "CLOSED" => ParkingSessionStatus.Closed,
            "EXPIRED" => ParkingSessionStatus.Closed,
            "INVALIDATED" => ParkingSessionStatus.Closed,
            _ => ParkingSessionStatus.PaymentRequired
        };
    }

    private static TariffSnapshotStatus MapTariffSnapshotStatus(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "ACTIVE" => TariffSnapshotStatus.Active,
            "SUPERSEDED" => TariffSnapshotStatus.Superseded,
            "EXPIRED" => TariffSnapshotStatus.Expired,
            "CONSUMED" => TariffSnapshotStatus.Consumed,
            "INVALIDATED" => TariffSnapshotStatus.Invalidated,
            _ => TariffSnapshotStatus.Invalidated
        };
    }
}
