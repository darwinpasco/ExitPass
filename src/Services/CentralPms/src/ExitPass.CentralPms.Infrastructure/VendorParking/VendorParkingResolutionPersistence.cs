using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.OperatorConsole;
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
    private const string AppliedStatutoryTariffVersionSuffix = "|STATUTORY_DISCOUNT_APPLIED";
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

        if (!request.ReferencesAlreadyExist)
        {
            await EnsureReferenceRowsAsync(connection, transaction, request, siteGroupId, siteId, cancellationToken);
        }

        var vendorSystemId = await ResolveVendorSystemIdAsync(
            connection,
            transaction,
            request.RequestedVendorSystemId,
            request.ParkingSession.VendorSystemCode,
            cancellationToken);

        var canonicalVendorSessionReference = await ResolveProjectionVendorSessionReferenceAsync(
                connection,
                transaction,
                siteGroupId,
                siteId,
                vendorSystemId,
                request.SourceAdapterIdentityId,
                request.ParkingSession,
                cancellationToken)
            .ConfigureAwait(false);
        var resolvedIncomingSession = RebindVendorSessionReference(
            request.ParkingSession,
            canonicalVendorSessionReference);

        await AcquireParkingSessionIdentityLockAsync(
            connection,
            transaction,
            siteGroupId,
            siteId,
            vendorSystemId,
            canonicalVendorSessionReference,
            cancellationToken);

        var existingSession = await FindExistingSessionAsync(
            connection,
            transaction,
            siteGroupId,
            siteId,
            vendorSystemId,
            resolvedIncomingSession.TicketNumber,
            canonicalVendorSessionReference,
            cancellationToken);

        var parkingSessionWasReused = existingSession is not null;
        var parkingSession = existingSession ?? resolvedIncomingSession;
        var resolvedSiteGroupId = Guid.Parse(parkingSession.SiteGroupId);
        var resolvedSiteId = Guid.Parse(parkingSession.SiteId);

        if (!parkingSessionWasReused)
        {
            await InsertParkingSessionAsync(
                connection,
                transaction,
                request,
                parkingSession,
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
            parkingSessionWasReused && request.TariffOrigin == VendorParkingTariffOrigin.LiveVendor
                ? null
                : vendorTariffRef,
            cancellationToken);

        if (request.TariffOrigin == VendorParkingTariffOrigin.LiveVendor &&
            existingTariff is not null &&
            (IsContinuityTariff(existingTariff) ||
             !HasSameAuthoritativeQuoteForRefresh(existingTariff, request.TariffSnapshot)))
        {
            existingTariff = null;
        }

        var statutoryPayableBasisWasRecalculated = false;
        if (existingTariff is null && parkingSessionWasReused)
        {
            var latestExistingTariff = await FindLatestExistingTariffAsync(
                connection,
                transaction,
                parkingSession.ParkingSessionId,
                cancellationToken);

            if (await HasAppliedPayableBasisApplicationAsync(
                connection,
                transaction,
                parkingSession.ParkingSessionId,
                cancellationToken))
            {
                if (latestExistingTariff is null)
                {
                    throw new VendorParkingResolutionPersistenceException(
                        "EFFECTIVE_PAYABLE_BASIS_INVALID",
                        $"Parking session '{parkingSession.ParkingSessionId}' has an APPLIED statutory discount payable-basis application without a valid active applied tariff snapshot.");
                }

                if (await HasCurrentCompletedZeroPayableStatutoryFinalityAsync(
                    connection,
                    transaction,
                    latestExistingTariff.TariffSnapshotId,
                    cancellationToken))
                {
                    existingTariff = latestExistingTariff;
                }
                else if (request.TariffOrigin == VendorParkingTariffOrigin.LiveVendor)
                {
                    existingTariff = await RecalculateAppliedStatutoryPayableBasisAsync(
                        connection,
                        transaction,
                        parkingSession,
                        latestExistingTariff,
                        request.TariffSnapshot,
                        vendorSystemId,
                        vendorTariffRef,
                        request.SourceAdapterIdentityId,
                        request.CorrelationId,
                        cancellationToken);
                    statutoryPayableBasisWasRecalculated = true;
                }
                else
                {
                    throw new VendorParkingResolutionPersistenceException(
                        "EFFECTIVE_PAYABLE_BASIS_INVALID",
                        $"Parking session '{parkingSession.ParkingSessionId}' has an APPLIED statutory discount payable-basis application that cannot be repriced without an authoritative LIVE vendor quote.");
                }
            }
            else
            {
                if (latestExistingTariff is not null &&
                    await HasActiveOrConfirmedPaymentAttemptAsync(
                        connection,
                        transaction,
                        latestExistingTariff.TariffSnapshotId,
                        cancellationToken))
                {
                    existingTariff = latestExistingTariff;
                }

                var latestCanBeReused = latestExistingTariff is not null &&
                    existingTariff is null &&
                    latestExistingTariff.ExpiresAt > DateTimeOffset.UtcNow &&
                    (request.TariffOrigin != VendorParkingTariffOrigin.LiveVendor ||
                     HasSameAuthoritativeQuote(latestExistingTariff, request.TariffSnapshot)) &&
                    !await WasConsumedOnlyByFailedPaymentAttemptAsync(
                        connection,
                        transaction,
                        latestExistingTariff.TariffSnapshotId,
                        cancellationToken);

                if (latestCanBeReused && !IsContinuityTariff(latestExistingTariff!))
                {
                    existingTariff = latestExistingTariff;
                }
            }
        }

        var tariffSnapshotWasReused = existingTariff is not null && !statutoryPayableBasisWasRecalculated;
        var tariffSnapshot = existingTariff ?? RebindTariffSnapshot(request.TariffSnapshot, parkingSession.ParkingSessionId);

        if (!tariffSnapshotWasReused && !statutoryPayableBasisWasRecalculated)
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

    private static async Task<string> ResolveProjectionVendorSessionReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteGroupId,
        Guid siteId,
        Guid vendorSystemId,
        Guid? sourceAdapterIdentityId,
        ParkingSession session,
        CancellationToken cancellationToken)
    {
        var ticketReference = NormalizeIdentifier(session.TicketNumber);
        var plateNumber = NormalizeIdentifier(session.PlateNumber);
        if (ticketReference is null && plateNumber is null)
        {
            return session.VendorSessionRef;
        }

        const string sql = """
            SELECT DISTINCT
                COALESCE(
                    NULLIF(BTRIM(projection.vendor_record_guid), ''),
                    NULLIF(BTRIM(projection.stable_identity_key), '')) AS canonical_vendor_session_ref
            FROM sessions.vendor_session_projections AS projection
            WHERE projection.site_group_id = @site_group_id
              AND projection.site_id = @site_id
              AND projection.vendor_system_id = @vendor_system_id
              AND (@source_adapter_identity_id IS NULL
                   OR projection.source_adapter_identity_id = @source_adapter_identity_id)
              AND projection.projection_status = 'ACTIVE'
              AND projection.enter_time = @entry_time
              AND (
                    (@ticket_reference IS NOT NULL
                     AND UPPER(BTRIM(projection.card_num)) = @ticket_reference)
                 OR (@ticket_reference IS NULL
                     AND @plate_number IS NOT NULL
                     AND UPPER(BTRIM(projection.plate_license)) = @plate_number)
              )
              AND COALESCE(
                    NULLIF(BTRIM(projection.vendor_record_guid), ''),
                    NULLIF(BTRIM(projection.stable_identity_key), '')) IS NOT NULL
            LIMIT 2;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.Add("source_adapter_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)sourceAdapterIdentityId ?? DBNull.Value;
        command.Parameters.Add("ticket_reference", NpgsqlDbType.Text).Value = DbValue(ticketReference);
        command.Parameters.Add("plate_number", NpgsqlDbType.Text).Value = DbValue(plateNumber);
        command.Parameters.Add("entry_time", NpgsqlDbType.TimestampTz).Value = ToUtc(session.EntryTimestamp);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return session.VendorSessionRef;
        }

        var canonicalReference = reader.GetString(0);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new VendorParkingResolutionPersistenceException(
                "VENDOR_PARKING_PROJECTION_IDENTITY_AMBIGUOUS",
                "The vendor parking result matches more than one authoritative passageway projection.");
        }

        return canonicalReference;
    }

    private static ParkingSession RebindVendorSessionReference(
        ParkingSession session,
        string vendorSessionReference) =>
        string.Equals(session.VendorSessionRef, vendorSessionReference, StringComparison.Ordinal)
            ? session
            : ParkingSession.Rehydrate(
                session.ParkingSessionId,
                session.SiteGroupId,
                session.SiteId,
                session.VendorSystemCode,
                vendorSessionReference,
                session.IdentifierType,
                session.PlateNumber,
                session.TicketNumber,
                session.EntryTimestamp,
                session.SessionStatus);

    private static async Task AcquireParkingSessionIdentityLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteGroupId,
        Guid siteId,
        Guid vendorSystemId,
        string vendorSessionReference,
        CancellationToken cancellationToken)
    {
        var identityKey = $"{siteGroupId:D}:{siteId:D}:{vendorSystemId:D}:{vendorSessionReference.Trim()}";
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@identity_key, 0));",
            connection,
            transaction);
        command.Parameters.AddWithValue("identity_key", identityKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        ParkingSession session,
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
                row_version
            )
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
                1
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParkingSessionInsertParameters(command, session, siteGroupId, siteId, vendorSystemId,
            request.SourceAdapterIdentityId, request.CanonicalVehicleTypeCode, request.CorrelationId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParkingSessionInsertParameters(
        NpgsqlCommand command,
        ParkingSession session,
        Guid siteGroupId,
        Guid siteId,
        Guid vendorSystemId,
        Guid? sourceAdapterIdentityId,
        string? canonicalVehicleTypeCode,
        Guid correlationId)
    {
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = session.ParkingSessionId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = siteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.Add("source_adapter_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)sourceAdapterIdentityId ?? DBNull.Value;
        command.Parameters.Add("canonical_vehicle_type_code", NpgsqlDbType.Varchar).Value =
            DbValue(canonicalVehicleTypeCode);
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

    private static async Task<TariffSnapshot> RecalculateAppliedStatutoryPayableBasisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ParkingSession parkingSession,
        TariffSnapshot currentAppliedTariff,
        TariffSnapshot incomingTariff,
        Guid vendorSystemId,
        string vendorTariffRef,
        Guid? sourceAdapterIdentityId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var incomingBaseTariff = RebindTariffSnapshot(incomingTariff, parkingSession.ParkingSessionId);
        if (incomingBaseTariff.SourceType != TariffSnapshotSourceType.Base ||
            incomingBaseTariff.GrossAmount <= 0m ||
            incomingBaseTariff.StatutoryDiscountAmount != 0m ||
            incomingBaseTariff.CouponDiscountAmount != 0m ||
            incomingBaseTariff.NetPayable != incomingBaseTariff.GrossAmount ||
            incomingBaseTariff.BaseFeeAmount != incomingBaseTariff.GrossAmount)
        {
            throw InvalidEffectivePayableBasis(
                parkingSession.ParkingSessionId,
                "The authoritative LIVE vendor quote is incomplete or already adjusted.");
        }

        var context = await LoadAppliedStatutoryRecalculationContextAsync(
            connection,
            transaction,
            parkingSession.ParkingSessionId,
            currentAppliedTariff.TariffSnapshotId,
            cancellationToken);
        if (context is null)
        {
            throw InvalidEffectivePayableBasis(
                parkingSession.ParkingSessionId,
                "The approved statutory application cannot be resolved unambiguously.");
        }

        if (!string.Equals(context.SessionStatus, "ACTIVE", StringComparison.Ordinal) ||
            !string.Equals(context.DecisionStatus, "APPROVED", StringComparison.Ordinal) ||
            !string.Equals(context.ApplicationCommandStatus, "APPLIED", StringComparison.Ordinal) ||
            !string.Equals(context.ApplicationStatus, "APPLIED", StringComparison.Ordinal) ||
            !string.Equals(context.ValidationStatus, "APPROVED", StringComparison.Ordinal) ||
            !context.EvidenceSatisfied ||
            !string.Equals(context.DecisionEntitlementType, context.ValidationEntitlementType, StringComparison.Ordinal) ||
            context.ApplicationGrossAmountMinorUnits <= 0 ||
            context.ApplicationFinalPayableAmountMinorUnits != 0 ||
            context.ActiveGrossAmount != ToMajorUnits(context.ApplicationGrossAmountMinorUnits) ||
            context.ActiveStatutoryDiscountAmount != ToMajorUnits(context.ApplicationStatutoryDiscountAmountMinorUnits) ||
            context.ActiveNetAmount != 0m ||
            context.ApplicationStatutoryDiscountAmountMinorUnits + context.ApplicationVatAmountMinorUnits !=
                context.ApplicationGrossAmountMinorUnits ||
            !string.Equals(context.CurrencyCode, incomingBaseTariff.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidEffectivePayableBasis(
                parkingSession.ParkingSessionId,
                "The existing statutory application is not a valid approved full-fee payable basis.");
        }

        if (await HasAnyFinancialOrExitFinalityAsync(
            connection,
            transaction,
            parkingSession.ParkingSessionId,
            cancellationToken))
        {
            throw InvalidEffectivePayableBasis(
                parkingSession.ParkingSessionId,
                "The statutory payable basis is already bound to payment, fiscal, or exit finality.");
        }

        var grossAmountMinorUnits = ToMinorUnits(incomingBaseTariff.GrossAmount);
        var computed = OperatorConsoleStatutoryDiscountComputationContract.Compute(
            new OperatorConsoleStatutoryDiscountComputationRequest(
                grossAmountMinorUnits,
                context.DecisionEntitlementType,
                OperatorConsoleStatutoryDiscountComputationContract.FullFeeExemptionBenefitType,
                OperatorConsoleStatutoryDiscountComputationContract.FullFeeExemptionDiscountBaseScope));
        if (!computed.Accepted ||
            !computed.VatAmountMinorUnits.HasValue ||
            !computed.VatExclusiveAmountMinorUnits.HasValue ||
            !computed.StatutoryDiscountAmountMinorUnits.HasValue ||
            computed.FinalPayableAmountMinorUnits is not 0)
        {
            throw InvalidEffectivePayableBasis(
                parkingSession.ParkingSessionId,
                computed.ErrorCode ?? "The approved full-fee statutory basis could not be recalculated.");
        }

        var newAppliedTariffSnapshotId = Guid.NewGuid();
        await PersistRecalculatedStatutoryPayableBasisAsync(
            connection,
            transaction,
            context,
            currentAppliedTariff,
            incomingBaseTariff,
            newAppliedTariffSnapshotId,
            computed,
            vendorSystemId,
            vendorTariffRef,
            sourceAdapterIdentityId,
            correlationId,
            cancellationToken);

        return TariffSnapshot.Rehydrate(
            newAppliedTariffSnapshotId,
            parkingSession.ParkingSessionId,
            TariffSnapshotSourceType.StatutoryAdjusted,
            incomingBaseTariff.GrossAmount,
            ToMajorUnits(computed.StatutoryDiscountAmountMinorUnits.Value),
            0m,
            0m,
            incomingBaseTariff.CurrencyCode,
            incomingBaseTariff.BaseFeeAmount,
            AppendAppliedStatutoryTariffVersionSuffix(incomingBaseTariff.TariffVersionReference),
            incomingBaseTariff.PolicyVersionReference,
            incomingBaseTariff.CalculatedAt,
            incomingBaseTariff.ExpiresAt,
            TariffSnapshotStatus.Active,
            null,
            null);
    }

    private static async Task<AppliedStatutoryRecalculationContext?> LoadAppliedStatutoryRecalculationContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        Guid activeAppliedTariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                application.statutory_discount_payable_basis_application_id,
                application.statutory_discount_validation_id,
                application.application_status::text,
                COALESCE(recalculation.gross_amount_minor_units, application.gross_amount_minor_units),
                COALESCE(recalculation.vat_amount_minor_units, application.vat_amount_minor_units),
                COALESCE(
                    recalculation.statutory_discount_amount_minor_units,
                    application.statutory_discount_amount_minor_units),
                COALESCE(
                    recalculation.final_payable_amount_minor_units,
                    application.final_payable_amount_minor_units),
                COALESCE(recalculation.currency_code, application.currency_code)::text,
                application_command.command_status,
                decision.decision_result_status,
                decision.entitlement_type,
                validation.validation_status::text,
                validation.entitlement_type::text,
                (NOT validation.evidence_required OR validation.evidence_captured) AS evidence_satisfied,
                parking.session_status::text,
                active_tariff.gross_amount,
                active_tariff.statutory_discount_amount,
                active_tariff.net_amount
            FROM discounts.statutory_discount_payable_basis_applications AS application
            JOIN discounts.statutory_discount_payable_basis_application_commands AS application_command
              ON application_command.statutory_discount_payable_basis_application_id =
                 application.statutory_discount_payable_basis_application_id
             AND application_command.statutory_discount_validation_id = application.statutory_discount_validation_id
             AND application_command.parking_session_id = application.parking_session_id
            JOIN discounts.statutory_discount_decision_commands AS decision
              ON decision.statutory_discount_decision_command_id =
                 application_command.statutory_discount_decision_command_id
             AND decision.statutory_discount_validation_id = application.statutory_discount_validation_id
             AND decision.parking_session_id = application.parking_session_id
            JOIN discounts.statutory_discount_validations AS validation
              ON validation.statutory_discount_validation_id = application.statutory_discount_validation_id
             AND validation.parking_session_id = application.parking_session_id
            JOIN core.parking_sessions AS parking
              ON parking.parking_session_id = application.parking_session_id
            JOIN core.tariff_snapshots AS active_tariff
              ON active_tariff.parking_session_id = application.parking_session_id
             AND active_tariff.statutory_discount_validation_id = application.statutory_discount_validation_id
            LEFT JOIN discounts.statutory_discount_payable_basis_recalculations AS recalculation
              ON recalculation.statutory_discount_payable_basis_application_id =
                 application.statutory_discount_payable_basis_application_id
             AND recalculation.applied_tariff_snapshot_id = active_tariff.tariff_snapshot_id
            WHERE application.parking_session_id = @parking_session_id
              AND active_tariff.tariff_snapshot_id = @active_applied_tariff_snapshot_id
              AND active_tariff.snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum
              AND active_tariff.superseded_by_tariff_snapshot_id IS NULL
            FOR UPDATE OF application, validation, parking, active_tariff;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("active_applied_tariff_snapshot_id", NpgsqlDbType.Uuid).Value =
            activeAppliedTariffSnapshotId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var context = new AppliedStatutoryRecalculationContext(
            reader.GetGuid(0),
            reader.GetGuid(1),
            parkingSessionId,
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetString(7).Trim(),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetBoolean(13),
            reader.GetString(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            reader.GetDecimal(17));

        if (await reader.ReadAsync(cancellationToken))
        {
            throw InvalidEffectivePayableBasis(
                parkingSessionId,
                "More than one approved statutory application matched the canonical parking session.");
        }

        return context;
    }

    private static async Task<bool> HasAnyFinancialOrExitFinalityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM core.payment_attempts
                    WHERE parking_session_id = @parking_session_id)
                OR EXISTS (
                    SELECT 1
                    FROM core.fiscal_issuance_references
                    WHERE parking_session_id = @parking_session_id)
                OR EXISTS (
                    SELECT 1
                    FROM core.exit_authorizations
                    WHERE parking_session_id = @parking_session_id);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task PersistRecalculatedStatutoryPayableBasisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AppliedStatutoryRecalculationContext context,
        TariffSnapshot currentAppliedTariff,
        TariffSnapshot newBaseTariff,
        Guid newAppliedTariffSnapshotId,
        OperatorConsoleStatutoryDiscountComputationResult computed,
        Guid vendorSystemId,
        string vendorTariffRef,
        Guid? sourceAdapterIdentityId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await RetireRecalculatedTariffAsync(
            connection,
            transaction,
            currentAppliedTariff.TariffSnapshotId,
            cancellationToken);
        await InsertRecalculatedBaseTariffAsync(
            connection,
            transaction,
            newBaseTariff,
            vendorSystemId,
            vendorTariffRef,
            sourceAdapterIdentityId,
            correlationId,
            cancellationToken);
        await InsertRecalculatedAppliedTariffAsync(
            connection,
            transaction,
            context.StatutoryDiscountValidationId,
            newBaseTariff,
            newAppliedTariffSnapshotId,
            computed,
            vendorSystemId,
            vendorTariffRef,
            sourceAdapterIdentityId,
            correlationId,
            cancellationToken);
        await LinkRecalculatedTariffLineageAsync(
            connection,
            transaction,
            currentAppliedTariff.TariffSnapshotId,
            newBaseTariff.TariffSnapshotId,
            newAppliedTariffSnapshotId,
            cancellationToken);
        await InsertRecalculationEvidenceAsync(
            connection,
            transaction,
            context,
            currentAppliedTariff.TariffSnapshotId,
            newBaseTariff.TariffSnapshotId,
            newAppliedTariffSnapshotId,
            computed,
            correlationId,
            cancellationToken);
    }

    private static async Task RetireRecalculatedTariffAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET snapshot_status = 'SUPERSEDED'::core.tariff_snapshot_status_enum,
                superseded_by_tariff_snapshot_id = NULL,
                updated_at = NOW(),
                updated_by_service_identity_id = @service_identity_id,
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id
              AND snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum
              AND consumed_at IS NULL
              AND superseded_by_tariff_snapshot_id IS NULL;
            """;
        await ExecuteExpectedSingleRowAsync(
            connection,
            transaction,
            sql,
            command => command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshotId,
            cancellationToken);
    }

    private static async Task InsertRecalculatedBaseTariffAsync(
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
                tariff_snapshot_id, parking_session_id, superseded_by_tariff_snapshot_id,
                vendor_system_id, source_adapter_identity_id, vendor_tariff_ref,
                tariff_version_reference, currency_code, gross_amount,
                statutory_discount_amount, coupon_discount_amount, net_amount,
                statutory_discount_validation_id, coupon_application_id, snapshot_status,
                calculated_at, expires_at, consumed_at, correlation_id, created_at,
                created_by_service_identity_id, updated_at, updated_by_service_identity_id, row_version)
            VALUES (
                @tariff_snapshot_id, @parking_session_id, NULL,
                @vendor_system_id, @source_adapter_identity_id, @vendor_tariff_ref,
                @tariff_version_reference, @currency_code, @gross_amount,
                0, 0, @gross_amount,
                NULL, NULL, 'SUPERSEDED'::core.tariff_snapshot_status_enum,
                @calculated_at, @expires_at, NULL, @correlation_id, NOW(),
                @service_identity_id, NOW(), @service_identity_id, 1);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddTariffSnapshotInsertParameters(
            command,
            tariffSnapshot,
            vendorSystemId,
            vendorTariffRef,
            sourceAdapterIdentityId,
            correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRecalculatedAppliedTariffAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid validationId,
        TariffSnapshot baseTariff,
        Guid appliedTariffSnapshotId,
        OperatorConsoleStatutoryDiscountComputationResult computed,
        Guid vendorSystemId,
        string vendorTariffRef,
        Guid? sourceAdapterIdentityId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id, parking_session_id, superseded_by_tariff_snapshot_id,
                vendor_system_id, source_adapter_identity_id, vendor_tariff_ref,
                tariff_version_reference, currency_code, gross_amount,
                statutory_discount_amount, coupon_discount_amount, net_amount,
                statutory_discount_validation_id, coupon_application_id, snapshot_status,
                calculated_at, expires_at, consumed_at, correlation_id, created_at,
                created_by_service_identity_id, updated_at, updated_by_service_identity_id, row_version)
            VALUES (
                @tariff_snapshot_id, @parking_session_id, NULL,
                @vendor_system_id, @source_adapter_identity_id, @vendor_tariff_ref,
                @tariff_version_reference, @currency_code, @gross_amount,
                @statutory_discount_amount, 0, 0,
                @validation_id, NULL, 'ACTIVE'::core.tariff_snapshot_status_enum,
                @calculated_at, @expires_at, NULL, @correlation_id, NOW(),
                @service_identity_id, NOW(), @service_identity_id, 1);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = appliedTariffSnapshotId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = baseTariff.ParkingSessionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = vendorSystemId;
        command.Parameters.Add("source_adapter_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)sourceAdapterIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("vendor_tariff_ref", vendorTariffRef);
        command.Parameters.Add("tariff_version_reference", NpgsqlDbType.Text).Value =
            DbValue(AppendAppliedStatutoryTariffVersionSuffix(baseTariff.TariffVersionReference));
        command.Parameters.AddWithValue("currency_code", baseTariff.CurrencyCode);
        command.Parameters.AddWithValue("gross_amount", baseTariff.GrossAmount);
        command.Parameters.AddWithValue(
            "statutory_discount_amount",
            ToMajorUnits(computed.StatutoryDiscountAmountMinorUnits!.Value));
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = validationId;
        command.Parameters.Add("calculated_at", NpgsqlDbType.TimestampTz).Value = ToUtc(baseTariff.CalculatedAt);
        command.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value = ToUtc(baseTariff.ExpiresAt);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LinkRecalculatedTariffLineageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid priorAppliedTariffSnapshotId,
        Guid newBaseTariffSnapshotId,
        Guid newAppliedTariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET superseded_by_tariff_snapshot_id = CASE
                    WHEN tariff_snapshot_id = @prior_applied_tariff_snapshot_id
                        THEN @new_base_tariff_snapshot_id
                    ELSE @new_applied_tariff_snapshot_id
                END,
                updated_at = NOW(),
                updated_by_service_identity_id = @service_identity_id,
                row_version = row_version + 1
            WHERE tariff_snapshot_id IN (
                @prior_applied_tariff_snapshot_id,
                @new_base_tariff_snapshot_id)
              AND snapshot_status = 'SUPERSEDED'::core.tariff_snapshot_status_enum
              AND superseded_by_tariff_snapshot_id IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("prior_applied_tariff_snapshot_id", NpgsqlDbType.Uuid).Value =
            priorAppliedTariffSnapshotId;
        command.Parameters.Add("new_base_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = newBaseTariffSnapshotId;
        command.Parameters.Add("new_applied_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = newAppliedTariffSnapshotId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 2)
        {
            throw new InvalidOperationException("The statutory tariff lineage could not be linked atomically.");
        }
    }

    private static async Task InsertRecalculationEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AppliedStatutoryRecalculationContext context,
        Guid priorAppliedTariffSnapshotId,
        Guid newBaseTariffSnapshotId,
        Guid newAppliedTariffSnapshotId,
        OperatorConsoleStatutoryDiscountComputationResult computed,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_payable_basis_recalculations (
                statutory_discount_payable_basis_application_id,
                statutory_discount_validation_id,
                parking_session_id,
                previous_applied_tariff_snapshot_id,
                original_tariff_snapshot_id,
                applied_tariff_snapshot_id,
                gross_amount_minor_units,
                vat_amount_minor_units,
                vat_exclusive_amount_minor_units,
                statutory_discount_amount_minor_units,
                final_payable_amount_minor_units,
                currency_code,
                computation_basis_json,
                correlation_id,
                created_by_service_identity_id)
            VALUES (
                @application_id,
                @validation_id,
                @parking_session_id,
                @prior_applied_tariff_snapshot_id,
                @new_base_tariff_snapshot_id,
                @new_applied_tariff_snapshot_id,
                @gross_amount_minor_units,
                @vat_amount_minor_units,
                @vat_exclusive_amount_minor_units,
                @statutory_discount_amount_minor_units,
                @final_payable_amount_minor_units,
                @currency_code,
                jsonb_build_object(
                    'sourceTariffSnapshotId', @new_base_tariff_snapshot_id,
                    'originalAmountMinorUnits', @gross_amount_minor_units,
                    'vatExclusiveBasisAmountMinorUnits', @vat_exclusive_amount_minor_units,
                    'originalEmbeddedVatAmountMinorUnits', @vat_amount_minor_units,
                    'statutoryDiscountAmountMinorUnits', @statutory_discount_amount_minor_units,
                    'finalPayableAmountMinorUnits', @final_payable_amount_minor_units,
                    'roundingMode', @rounding_mode,
                    'recalculatedBy', 'LIVE_VENDOR_FEE_REFRESH'),
                @correlation_id,
                @service_identity_id);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("application_id", NpgsqlDbType.Uuid).Value = context.ApplicationId;
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = context.StatutoryDiscountValidationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = context.ParkingSessionId;
        command.Parameters.Add("prior_applied_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = priorAppliedTariffSnapshotId;
        command.Parameters.Add("new_base_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = newBaseTariffSnapshotId;
        command.Parameters.Add("new_applied_tariff_snapshot_id", NpgsqlDbType.Uuid).Value = newAppliedTariffSnapshotId;
        command.Parameters.Add("gross_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.GrossAmountMinorUnits;
        command.Parameters.Add("vat_amount_minor_units", NpgsqlDbType.Bigint).Value = computed.VatAmountMinorUnits!.Value;
        command.Parameters.Add("vat_exclusive_amount_minor_units", NpgsqlDbType.Bigint).Value =
            computed.VatExclusiveAmountMinorUnits!.Value;
        command.Parameters.Add("statutory_discount_amount_minor_units", NpgsqlDbType.Bigint).Value =
            computed.StatutoryDiscountAmountMinorUnits!.Value;
        command.Parameters.Add("final_payable_amount_minor_units", NpgsqlDbType.Bigint).Value =
            computed.FinalPayableAmountMinorUnits!.Value;
        command.Parameters.AddWithValue("currency_code", context.CurrencyCode);
        command.Parameters.AddWithValue("rounding_mode", computed.RoundingMode);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The statutory payable-basis recalculation evidence was not persisted.");
        }
    }

    private static async Task ExecuteExpectedSingleRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        configure(command);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The current tariff snapshot changed during statutory recalculation.");
        }
    }

    private static VendorParkingResolutionPersistenceException InvalidEffectivePayableBasis(
        Guid parkingSessionId,
        string detail) =>
        new(
            "EFFECTIVE_PAYABLE_BASIS_INVALID",
            $"Parking session '{parkingSessionId}' cannot replace its applied statutory payable basis. {detail}");

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

    private static async Task<bool> HasCurrentCompletedZeroPayableStatutoryFinalityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM core.tariff_snapshots AS tariff
                JOIN discounts.statutory_discount_payable_basis_application_commands AS application
                  ON application.applied_tariff_snapshot_id = tariff.tariff_snapshot_id
                 AND application.parking_session_id = tariff.parking_session_id
                 AND application.command_status = 'APPLIED'
                JOIN core.fiscal_issuance_references AS fiscal
                  ON fiscal.statutory_discount_payable_basis_application_command_id = application.statutory_discount_payable_basis_application_command_id
                 AND fiscal.parking_session_id = tariff.parking_session_id
                 AND fiscal.tariff_snapshot_id = tariff.tariff_snapshot_id
                 AND fiscal.completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
                 AND fiscal.pos_server_fiscal_document_id IS NOT NULL
                 AND fiscal.electronic_journal_event_reference IS NOT NULL
                 AND fiscal.payment_attempt_id IS NULL
                 AND fiscal.payment_confirmation_id IS NULL
                LEFT JOIN core.exit_authorizations AS exit_auth
                  ON exit_auth.statutory_discount_payable_basis_application_command_id = application.statutory_discount_payable_basis_application_command_id
                 AND exit_auth.parking_session_id = tariff.parking_session_id
                 AND exit_auth.tariff_snapshot_id = tariff.tariff_snapshot_id
                 AND exit_auth.completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
                 AND exit_auth.authorization_status = 'ISSUED'
                 AND exit_auth.payment_attempt_id IS NULL
                 AND exit_auth.payment_confirmation_id IS NULL
                WHERE tariff.tariff_snapshot_id = @tariff_snapshot_id
                  AND tariff.snapshot_status = 'ACTIVE'
                  AND tariff.superseded_by_tariff_snapshot_id IS NULL
                  AND tariff.net_amount = 0
                  AND tariff.gross_amount > 0
                  AND tariff.statutory_discount_validation_id IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM core.payment_attempts AS attempt
                      WHERE attempt.tariff_snapshot_id = tariff.tariff_snapshot_id)
                  AND exit_auth.exit_authorization_id IS NOT NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshotId;
        return await command.ExecuteScalarAsync(cancellationToken) is true;
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
                ROUND(applied_ts.statutory_discount_amount * 100)::bigint AS statutory_discount_amount_minor_units,
                ROUND(applied_ts.net_amount * 100)::bigint AS net_payable_amount_minor_units,
                COALESCE(cmd.decided_at, cmd.applied_at, cmd.created_at) AS statutory_discount_decision_timestamp,
                'STATUTORY_DISCOUNT_VAT_EXEMPT' AS benefit_type
            FROM core.tariff_snapshots AS applied_ts
            INNER JOIN discounts.statutory_discount_validations AS sdv
                ON sdv.statutory_discount_validation_id = applied_ts.statutory_discount_validation_id
               AND sdv.parking_session_id = applied_ts.parking_session_id
            LEFT JOIN core.tariff_snapshots AS original
                ON original.superseded_by_tariff_snapshot_id = applied_ts.tariff_snapshot_id
               AND original.parking_session_id = applied_ts.parking_session_id
            INNER JOIN discounts.statutory_discount_payable_basis_applications AS pba
                ON pba.parking_session_id = applied_ts.parking_session_id
               AND pba.statutory_discount_validation_id = applied_ts.statutory_discount_validation_id
               AND pba.application_status = 'APPLIED'::discounts.statutory_discount_payable_application_status_enum
            INNER JOIN discounts.statutory_discount_decision_commands AS cmd
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

    private static async Task<bool> HasActiveOrConfirmedPaymentAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM core.payment_attempts AS pa
                WHERE pa.tariff_snapshot_id = @tariff_snapshot_id
                  AND pa.attempt_status IN (
                      'REQUESTED'::core.payment_attempt_status_enum,
                      'PENDING_PROVIDER'::core.payment_attempt_status_enum,
                      'PENDING_FINALIZATION'::core.payment_attempt_status_enum,
                      'CONFIRMED'::core.payment_attempt_status_enum
                  )
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshotId;
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static bool IsContinuityTariff(TariffSnapshot tariffSnapshot) =>
        tariffSnapshot.TariffVersionReference?.StartsWith(
            ContinuityTariffCalculator.TariffVersionPrefix,
            StringComparison.Ordinal) == true;

    private static bool HasSameAuthoritativeQuote(TariffSnapshot existing, TariffSnapshot incoming) =>
        existing.SourceType == incoming.SourceType &&
        existing.GrossAmount == incoming.GrossAmount &&
        existing.StatutoryDiscountAmount == incoming.StatutoryDiscountAmount &&
        existing.CouponDiscountAmount == incoming.CouponDiscountAmount &&
        existing.NetPayable == incoming.NetPayable &&
        string.Equals(existing.CurrencyCode, incoming.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
        existing.BaseFeeAmount == incoming.BaseFeeAmount &&
        string.Equals(existing.TariffVersionReference, incoming.TariffVersionReference, StringComparison.Ordinal) &&
        string.Equals(existing.PolicyVersionReference, incoming.PolicyVersionReference, StringComparison.Ordinal);

    private static bool HasAppliedStatutoryPayableBasis(TariffSnapshot tariffSnapshot) =>
        tariffSnapshot.StatutoryDiscountAmount > 0m;

    private static bool HasSameAuthoritativeQuoteForRefresh(TariffSnapshot existing, TariffSnapshot incoming) =>
        HasAppliedStatutoryPayableBasis(existing)
            ? HasSameAuthoritativeBaseQuote(existing, incoming)
            : HasSameAuthoritativeQuote(existing, incoming);

    private static bool HasSameAuthoritativeBaseQuote(TariffSnapshot applied, TariffSnapshot incoming) =>
        applied.GrossAmount == incoming.GrossAmount &&
        string.Equals(applied.CurrencyCode, incoming.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
        applied.BaseFeeAmount == incoming.BaseFeeAmount &&
        string.Equals(
            RemoveAppliedStatutoryTariffVersionSuffix(applied.TariffVersionReference),
            incoming.TariffVersionReference,
            StringComparison.Ordinal) &&
        string.Equals(applied.PolicyVersionReference, incoming.PolicyVersionReference, StringComparison.Ordinal);

    private static string? RemoveAppliedStatutoryTariffVersionSuffix(string? value) =>
        value?.EndsWith(AppliedStatutoryTariffVersionSuffix, StringComparison.Ordinal) == true
            ? value[..^AppliedStatutoryTariffVersionSuffix.Length]
            : value;

    private static string AppendAppliedStatutoryTariffVersionSuffix(string? value) =>
        $"{value ?? "LIVE"}{AppliedStatutoryTariffVersionSuffix}";

    private static long ToMinorUnits(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private static decimal ToMajorUnits(long amountMinorUnits) => amountMinorUnits / 100m;

    private static string? HashIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? NormalizeIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

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

    private sealed record AppliedStatutoryRecalculationContext(
        Guid ApplicationId,
        Guid StatutoryDiscountValidationId,
        Guid ParkingSessionId,
        string ApplicationStatus,
        long ApplicationGrossAmountMinorUnits,
        long ApplicationVatAmountMinorUnits,
        long ApplicationStatutoryDiscountAmountMinorUnits,
        long ApplicationFinalPayableAmountMinorUnits,
        string CurrencyCode,
        string ApplicationCommandStatus,
        string DecisionStatus,
        string DecisionEntitlementType,
        string ValidationStatus,
        string ValidationEntitlementType,
        bool EvidenceSatisfied,
        string SessionStatus,
        decimal ActiveGrossAmount,
        decimal ActiveStatutoryDiscountAmount,
        decimal ActiveNetAmount);
}
