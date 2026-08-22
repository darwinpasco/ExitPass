using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.VendorParking;
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

        var vendorSystemId = await ResolveVendorSystemIdAsync(
            connection,
            transaction,
            request.RequestedVendorSystemId,
            request.ParkingSession.VendorSystemCode,
            cancellationToken);

        var existingSession = await FindExistingSessionAsync(
            connection,
            transaction,
            siteGroupId,
            siteId,
            vendorSystemId,
            request.ParkingSession.TicketNumber,
            request.ParkingSession.VendorSessionRef,
            cancellationToken);

        var parkingSessionWasReused = existingSession is not null;
        var parkingSession = existingSession ?? request.ParkingSession;
        var resolvedSiteGroupId = Guid.Parse(parkingSession.SiteGroupId);
        var resolvedSiteId = Guid.Parse(parkingSession.SiteId);

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
            parkingSessionWasReused ? null : vendorTariffRef,
            cancellationToken);

        if (existingTariff is null && parkingSessionWasReused)
        {
            if (await HasAppliedPayableBasisApplicationAsync(
                connection,
                transaction,
                parkingSession.ParkingSessionId,
                cancellationToken))
            {
                throw new VendorParkingResolutionPersistenceException(
                    "EFFECTIVE_PAYABLE_BASIS_INVALID",
                    $"Parking session '{parkingSession.ParkingSessionId}' has an APPLIED statutory discount payable-basis application without a valid active applied tariff snapshot.");
            }

            var latestExistingTariff = await FindLatestExistingTariffAsync(
                connection,
                transaction,
                parkingSession.ParkingSessionId,
                cancellationToken);

            existingTariff = latestExistingTariff is not null &&
                latestExistingTariff.ExpiresAt > DateTimeOffset.UtcNow &&
                !await WasConsumedOnlyByFailedPaymentAttemptAsync(
                    connection,
                    transaction,
                    latestExistingTariff.TariffSnapshotId,
                    cancellationToken)
                    ? latestExistingTariff
                    : null;
        }

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
                request.SourceAdapterIdentityId,
                request.CorrelationId,
                cancellationToken);
        }

        var operationalSummary = await LoadOperationalSummaryAsync(
            connection,
            transaction,
            parkingSession.ParkingSessionId,
            resolvedSiteGroupId,
            resolvedSiteId,
            cancellationToken);
        var effectivePayableBasis = await LoadEffectivePayableBasisSummaryAsync(
            connection,
            transaction,
            parkingSession.ParkingSessionId,
            tariffSnapshot.TariffSnapshotId,
            cancellationToken);
        var resolvedVendorSystemId = await LoadParkingSessionVendorSystemIdAsync(
            connection,
            transaction,
            parkingSession.ParkingSessionId,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PersistVendorParkingResolutionResult
        {
            ParkingSession = parkingSession,
            TariffSnapshot = tariffSnapshot,
            ParkingSessionWasReused = parkingSessionWasReused,
            TariffSnapshotWasReused = tariffSnapshotWasReused,
            VendorSystemId = resolvedVendorSystemId.ToString(),
            SiteGroupName = operationalSummary.SiteGroupName,
            SiteName = operationalSummary.SiteName,
            PaymentStatus = operationalSummary.PaymentStatus,
            EffectivePayableBasis = effectivePayableBasis
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
        command.Parameters.AddWithValue("site_group_name", ParkingDisplayNameSanitizer.GenericSiteGroupName);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.AddWithValue("site_code", $"SITE-{siteId:N}");
        command.Parameters.AddWithValue("site_name", ParkingDisplayNameSanitizer.GenericSiteName);
        command.Parameters.AddWithValue("vendor_system_code", request.ParkingSession.VendorSystemCode);
        command.Parameters.AddWithValue("vendor_system_name", request.ParkingSession.VendorSystemCode);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid> ResolveVendorSystemIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? requestedVendorSystemId,
        string vendorSystemCode,
        CancellationToken cancellationToken)
    {
        if (requestedVendorSystemId.HasValue &&
            await VendorSystemExistsAsync(connection, transaction, requestedVendorSystemId.Value, cancellationToken))
        {
            return requestedVendorSystemId.Value;
        }

        return await GetVendorSystemIdAsync(connection, transaction, vendorSystemCode, cancellationToken);
    }

    private static async Task<bool> VendorSystemExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid vendorSystemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM integration.vendor_systems
                WHERE vendor_system_id = @vendor_system_id
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
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
        string? ticketReference,
        string vendorSessionRef,
        CancellationToken cancellationToken)
    {
        const string sql = """
            /*
             * ExitPass v1.2 SDD:
             * - Section 13.1.2, core.parking_sessions
             * - Section 13.1.3 core.tariff_snapshots
             * Invariant: WebPay resolution must reuse the authoritative Central PMS session/tariff for a
             * seeded ticket instead of replacing it with transient fake-adapter data.
             */
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
            WHERE (
                  ps.site_group_id = @site_group_id
                  AND ps.site_id = @site_id
                  AND ps.vendor_system_id = @vendor_system_id
                  AND (
                      ps.vendor_session_ref = @vendor_session_ref
                      OR (
                          @ticket_reference IS NOT NULL
                          AND (
                              ps.ticket_number_masked = @ticket_reference
                              OR ps.ticket_number_hash = @ticket_reference_hash
                          )
                      )
                  )
                )
              OR (
                  @is_seeded_webpay_reference = TRUE
                  AND @ticket_reference IS NOT NULL
                  AND (
                      ps.vendor_session_ref = @ticket_reference
                      OR ps.ticket_number_masked = @ticket_reference
                      OR ps.ticket_number_hash = @ticket_reference_hash
                  )
              )
            ORDER BY
                CASE
                    WHEN @is_seeded_webpay_reference = TRUE
                     AND vs.vendor_code LIKE 'WEBPAY\_%' ESCAPE '\' THEN 0
                    ELSE 1
                END,
                ps.created_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.Add("ticket_reference", NpgsqlDbType.Text).Value = DbValue(ticketReference);
        command.Parameters.Add("ticket_reference_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(ticketReference));
        command.Parameters.AddWithValue("vendor_session_ref", vendorSessionRef);
        command.Parameters.Add("is_seeded_webpay_reference", NpgsqlDbType.Boolean).Value = IsSeededWebPayReference(ticketReference);

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
                source_adapter_identity_id,
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
                @source_adapter_identity_id,
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
        AddParkingSessionInsertParameters(command, session, siteGroupId, siteId, vendorSystemId,
            request.SourceAdapterIdentityId, request.CorrelationId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParkingSessionInsertParameters(
        NpgsqlCommand command,
        ParkingSession session,
        Guid siteGroupId,
        Guid siteId,
        Guid vendorSystemId,
        Guid? sourceAdapterIdentityId,
        Guid correlationId)
    {
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = session.ParkingSessionId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.Add("source_adapter_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)sourceAdapterIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("vendor_session_ref", session.VendorSessionRef);
        command.Parameters.Add("plate_number_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(session.PlateNumber));
        command.Parameters.Add("plate_number_masked", NpgsqlDbType.Text).Value = DbValue(session.PlateNumber);
        command.Parameters.Add("ticket_number_hash", NpgsqlDbType.Text).Value = DbValue(HashIdentifier(session.TicketNumber));
        command.Parameters.Add("ticket_number_masked", NpgsqlDbType.Text).Value = DbValue(session.TicketNumber);
        command.Parameters.Add("entry_at", NpgsqlDbType.TimestampTz).Value = ToUtc(session.EntryTimestamp);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
    }

    private static async Task<TariffSnapshot?> FindExistingActiveTariffAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        string? vendorTariffRef,
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
              AND (@vendor_tariff_ref IS NULL OR vendor_tariff_ref = @vendor_tariff_ref)
              AND snapshot_status = 'ACTIVE'
              AND expires_at > NOW()
              AND consumed_at IS NULL
              AND superseded_by_tariff_snapshot_id IS NULL
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("vendor_tariff_ref", NpgsqlDbType.Text).Value = DbValue(vendorTariffRef);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var statutoryDiscountAmount = reader.GetDecimal(reader.GetOrdinal("statutory_discount_amount"));
        var couponDiscountAmount = reader.GetDecimal(reader.GetOrdinal("coupon_discount_amount"));

        return TariffSnapshot.Rehydrate(
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            ResolveTariffSnapshotSourceType(statutoryDiscountAmount, couponDiscountAmount),
            reader.GetDecimal(reader.GetOrdinal("gross_amount")),
            statutoryDiscountAmount,
            couponDiscountAmount,
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

    private static async Task<TariffSnapshot?> FindLatestExistingTariffAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
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
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var statutoryDiscountAmount = reader.GetDecimal(reader.GetOrdinal("statutory_discount_amount"));
        var couponDiscountAmount = reader.GetDecimal(reader.GetOrdinal("coupon_discount_amount"));

        return TariffSnapshot.Rehydrate(
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            ResolveTariffSnapshotSourceType(statutoryDiscountAmount, couponDiscountAmount),
            reader.GetDecimal(reader.GetOrdinal("gross_amount")),
            statutoryDiscountAmount,
            couponDiscountAmount,
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

    private static async Task<bool> WasConsumedOnlyByFailedPaymentAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM core.payment_attempts AS pa
                    WHERE pa.tariff_snapshot_id = @tariff_snapshot_id
                      AND pa.attempt_status = 'FAILED'::core.payment_attempt_status_enum
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM core.payment_attempts AS pa
                    WHERE pa.tariff_snapshot_id = @tariff_snapshot_id
                      AND pa.attempt_status <> 'FAILED'::core.payment_attempt_status_enum
                )
                AS consumed_by_failed_attempt_only;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshotId;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool consumedByFailedAttemptOnly && consumedByFailedAttemptOnly;
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

    private static async Task<OperationalSummary> LoadOperationalSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        Guid siteGroupId,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                sg.site_group_name,
                s.site_name,
                (
                    SELECT pa.attempt_status::text
                    FROM core.payment_attempts pa
                    WHERE pa.parking_session_id = @parking_session_id
                    ORDER BY pa.created_at DESC
                    LIMIT 1
                ) AS latest_attempt_status
            FROM sites.site_groups sg
            INNER JOIN sites.sites s
                ON s.site_id = @site_id
               AND s.site_group_id = sg.site_group_id
            WHERE sg.site_group_id = @site_group_id
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new OperationalSummary(null, null, "Not Started");
        }

        var attemptStatus = reader.IsDBNull(reader.GetOrdinal("latest_attempt_status"))
            ? null
            : reader.GetString(reader.GetOrdinal("latest_attempt_status"));

        return new OperationalSummary(
            reader.IsDBNull(reader.GetOrdinal("site_group_name")) ? null : reader.GetString(reader.GetOrdinal("site_group_name")),
            reader.IsDBNull(reader.GetOrdinal("site_name")) ? null : reader.GetString(reader.GetOrdinal("site_name")),
            MapPaymentStatus(attemptStatus));
    }

    private static async Task<bool> HasAppliedPayableBasisApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM core.tariff_snapshots AS ts
                WHERE ts.parking_session_id = @parking_session_id
                  AND ts.snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum
                  AND ts.statutory_discount_validation_id IS NOT NULL
                  AND ts.statutory_discount_amount > 0
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    private static async Task<EffectivePayableBasisSummary?> LoadEffectivePayableBasisSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        Guid effectiveTariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                pba.statutory_discount_payable_basis_application_id,
                applied_ts.statutory_discount_validation_id,
                cmd.statutory_discount_decision_command_id,
                original.tariff_snapshot_id AS original_tariff_snapshot_id,
                applied_ts.tariff_snapshot_id AS applied_tariff_snapshot_id,
                sdv.policy_resolution_basis::text AS policy_resolution_basis,
                cmd.applied_policy_reference_id,
                cmd.entitlement_type::text AS entitlement_type,
                cmd.statutory_discount_amount_minor_units,
                cmd.net_payable_amount_minor_units,
                COALESCE(cmd.decided_at, cmd.applied_at, cmd.created_at) AS statutory_discount_decision_timestamp,
                'STATUTORY_DISCOUNT_VAT_EXEMPT' AS benefit_type
            FROM core.tariff_snapshots AS applied_ts
            INNER JOIN discounts.statutory_discount_validations AS sdv
                ON sdv.statutory_discount_validation_id = applied_ts.statutory_discount_validation_id
               AND sdv.parking_session_id = applied_ts.parking_session_id
            LEFT JOIN core.tariff_snapshots AS original
                ON original.superseded_by_tariff_snapshot_id = applied_ts.tariff_snapshot_id
               AND original.parking_session_id = applied_ts.parking_session_id
            LEFT JOIN discounts.statutory_discount_payable_basis_applications AS pba
                ON pba.applied_tariff_snapshot_id = applied_ts.tariff_snapshot_id
               AND pba.statutory_discount_validation_id = applied_ts.statutory_discount_validation_id
               AND pba.application_status = 'APPLIED'::discounts.statutory_discount_payable_application_status_enum
            LEFT JOIN discounts.statutory_discount_decision_commands AS cmd
                ON cmd.statutory_discount_validation_id = applied_ts.statutory_discount_validation_id
               AND cmd.parking_session_id = applied_ts.parking_session_id
               AND cmd.decision_status <> 'PROCESSING'
            WHERE applied_ts.parking_session_id = @parking_session_id
              AND applied_ts.tariff_snapshot_id = @effective_tariff_snapshot_id
              AND applied_ts.snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum
              AND applied_ts.statutory_discount_validation_id IS NOT NULL
              AND applied_ts.statutory_discount_amount > 0
            ORDER BY applied_ts.calculated_at DESC, applied_ts.updated_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("effective_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = effectiveTariffSnapshotId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EffectivePayableBasisSummary
        {
            StatutoryDiscountApplied = true,
            StatutoryDiscountApplicationId = reader.IsDBNull(reader.GetOrdinal("statutory_discount_payable_basis_application_id"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("statutory_discount_payable_basis_application_id")),
            StatutoryDiscountDecisionCommandId = reader.IsDBNull(reader.GetOrdinal("statutory_discount_decision_command_id"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            StatutoryDiscountValidationId = reader.GetGuid(reader.GetOrdinal("statutory_discount_validation_id")),
            OriginalTariffSnapshotId = reader.IsDBNull(reader.GetOrdinal("original_tariff_snapshot_id"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("original_tariff_snapshot_id")),
            EffectiveTariffSnapshotId = effectiveTariffSnapshotId,
            AppliedTariffSnapshotId = reader.GetGuid(reader.GetOrdinal("applied_tariff_snapshot_id")),
            PolicyResolutionBasis = reader.IsDBNull(reader.GetOrdinal("policy_resolution_basis"))
                ? null
                : reader.GetString(reader.GetOrdinal("policy_resolution_basis")),
            AppliedPolicyReferenceId = reader.IsDBNull(reader.GetOrdinal("applied_policy_reference_id"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("applied_policy_reference_id")),
            BenefitType = reader.IsDBNull(reader.GetOrdinal("benefit_type"))
                ? null
                : reader.GetString(reader.GetOrdinal("benefit_type")),
            EntitlementType = reader.IsDBNull(reader.GetOrdinal("entitlement_type"))
                ? null
                : reader.GetString(reader.GetOrdinal("entitlement_type")),
            StatutoryDiscountAmountMinorUnits = reader.IsDBNull(reader.GetOrdinal("statutory_discount_amount_minor_units"))
                ? null
                : reader.GetInt64(reader.GetOrdinal("statutory_discount_amount_minor_units")),
            FinalPayableAmountMinorUnits = reader.IsDBNull(reader.GetOrdinal("net_payable_amount_minor_units"))
                ? null
                : reader.GetInt64(reader.GetOrdinal("net_payable_amount_minor_units")),
            StatutoryDiscountDecisionTimestamp = reader.IsDBNull(reader.GetOrdinal("statutory_discount_decision_timestamp"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("statutory_discount_decision_timestamp"))
        };
    }

    private static async Task<Guid> LoadParkingSessionVendorSystemIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT vendor_system_id
            FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid vendorSystemId
            ? vendorSystemId
            : throw new InvalidOperationException($"Parking session '{parkingSessionId}' does not have a vendor system.");
    }

    private static async Task InsertTariffSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TariffSnapshot tariffSnapshot,
        Guid vendorSystemId,
        string vendorTariffRef,
        Guid? sourceAdapterIdentityId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                superseded_by_tariff_snapshot_id,
                vendor_system_id,
                source_adapter_identity_id,
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
                @source_adapter_identity_id,
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
        AddTariffSnapshotInsertParameters(command, tariffSnapshot, vendorSystemId, vendorTariffRef,
            sourceAdapterIdentityId, correlationId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddTariffSnapshotInsertParameters(
        NpgsqlCommand command,
        TariffSnapshot tariffSnapshot,
        Guid vendorSystemId,
        string vendorTariffRef,
        Guid? sourceAdapterIdentityId,
        Guid correlationId)
    {
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshot.TariffSnapshotId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = tariffSnapshot.ParkingSessionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.Add("source_adapter_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)sourceAdapterIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("vendor_tariff_ref", vendorTariffRef);
        command.Parameters.Add("tariff_version_reference", NpgsqlDbType.Text).Value = DbValue(tariffSnapshot.TariffVersionReference);
        command.Parameters.AddWithValue("currency_code", tariffSnapshot.CurrencyCode);
        command.Parameters.AddWithValue("gross_amount", tariffSnapshot.GrossAmount);
        command.Parameters.AddWithValue("statutory_discount_amount", tariffSnapshot.StatutoryDiscountAmount);
        command.Parameters.AddWithValue("coupon_discount_amount", tariffSnapshot.CouponDiscountAmount);
        command.Parameters.AddWithValue("net_amount", tariffSnapshot.NetPayable);
        command.Parameters.Add("calculated_at", NpgsqlDbType.TimestampTz).Value = ToUtc(tariffSnapshot.CalculatedAt);
        command.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value = ToUtc(tariffSnapshot.ExpiresAt);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
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

    private static object ToUtcOrDbNull(DateTimeOffset? value) =>
        value.HasValue ? ToUtc(value.Value) : DBNull.Value;

    private static DateTimeOffset ToUtc(DateTimeOffset value) => value.ToUniversalTime();

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

    private static bool IsSeededWebPayReference(string? value)
    {
        return value?.Trim().StartsWith("WEBPAY-", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static TariffSnapshotSourceType ResolveTariffSnapshotSourceType(
        decimal statutoryDiscountAmount,
        decimal couponDiscountAmount)
    {
        if (couponDiscountAmount > 0m)
        {
            return TariffSnapshotSourceType.CouponAdjusted;
        }

        return statutoryDiscountAmount > 0m
            ? TariffSnapshotSourceType.StatutoryAdjusted
            : TariffSnapshotSourceType.Base;
    }

    private static string MapPaymentStatus(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            null or "" => "Not Started",
            "REQUESTED" or "PENDING_PROVIDER" => "Pending Payment",
            "CONFIRMED" or "PAID" or "FINALIZED" => "Paid",
            "FAILED" or "CANCELLED" => "Failed",
            "EXPIRED" => "Expired",
            _ => value!.Trim()
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

    private sealed record OperationalSummary(
        string? SiteGroupName,
        string? SiteName,
        string PaymentStatus);
}
