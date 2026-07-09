using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresControlledUatFiscalIssuanceFixtureStore : IControlledUatFiscalIssuanceFixtureStore
{
    private static readonly Guid ApprovedPaymentConfirmationId =
        Guid.Parse("00000000-0000-4000-8000-000000000301");
    private static readonly Guid ApprovedPaymentAttemptId =
        Guid.Parse("00000000-0000-4000-8000-000000000302");
    private static readonly Guid ApprovedParkingSessionId =
        Guid.Parse("00000000-0000-4000-8000-000000000303");
    private static readonly Guid ApprovedTariffSnapshotId =
        Guid.Parse("00000000-0000-4000-8000-000000000601");
    private static readonly Guid ApprovedServiceIdentityId =
        Guid.Parse("00000000-0000-4000-8000-000000000901");
    private static readonly Guid ApprovedSiteGroupId =
        Guid.Parse("00000000-0000-4000-8000-000000000401");
    private static readonly Guid ApprovedSiteId =
        Guid.Parse("00000000-0000-4000-8000-000000000402");
    private static readonly Guid ApprovedVendorSystemId =
        Guid.Parse("00000000-0000-4000-8000-000000000501");

    private const string ApprovedRunId = "CPS-POS-UAT-20260709-DEV-ATC-001";
    private const string ApprovedCorrelationId = "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df";
    private const string ApprovedSiteRef = "DEV-SITE-ATC-001";
    private const string ApprovedParkingSessionRef = "DEV-PARKING-SESSION-ATC-001";
    private const string ApprovedPaymentAttemptRef = "DEV-PAYMENT-ATTEMPT-ATC-001";
    private const string ApprovedPaymentConfirmationRef = "DEV-PAYMENT-FINALITY-ATC-001";
    private const string ApprovedUpstreamFinalityRef = "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001";
    private const string ApprovedCurrency = "PHP";

    private static readonly DateOnly ApprovedBusinessDayDate = new(2026, 7, 9);

    private readonly string _connectionString;

    public PostgresControlledUatFiscalIssuanceFixtureStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task EnsureApprovedFirstRunFixtureAsync(
        ControlledUatFiscalIssuanceFixture fixture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateApprovedFirstRunFixture(fixture);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(Sql, connection, transaction);

        AddParameters(command, fixture);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void ValidateApprovedFirstRunFixture(ControlledUatFiscalIssuanceFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var errors = new List<string>();

        Require(fixture.PaymentConfirmationId == ApprovedPaymentConfirmationId, "payment_confirmation_id_not_approved", errors);
        Require(fixture.PaymentAttemptId == ApprovedPaymentAttemptId, "payment_attempt_id_not_approved", errors);
        Require(fixture.ParkingSessionId == ApprovedParkingSessionId, "parking_session_id_not_approved", errors);
        Require(fixture.TariffSnapshotId == ApprovedTariffSnapshotId, "tariff_snapshot_id_not_approved", errors);
        Require(fixture.ServiceIdentityId == ApprovedServiceIdentityId, "service_identity_id_not_approved", errors);
        Require(fixture.SiteGroupId == ApprovedSiteGroupId, "site_group_id_not_approved", errors);
        Require(fixture.SiteId == ApprovedSiteId, "site_id_not_approved", errors);
        Require(fixture.VendorSystemId == ApprovedVendorSystemId, "vendor_system_id_not_approved", errors);
        Require(fixture.RunId == ApprovedRunId, "run_id_not_approved", errors);
        Require(fixture.CorrelationId.ToString("D") == ApprovedCorrelationId, "correlation_id_not_approved", errors);
        Require(fixture.SiteRef == ApprovedSiteRef, "site_ref_not_approved", errors);
        Require(fixture.ParkingSessionRef == ApprovedParkingSessionRef, "parking_session_ref_not_approved", errors);
        Require(fixture.PaymentAttemptRef == ApprovedPaymentAttemptRef, "payment_attempt_ref_not_approved", errors);
        Require(fixture.PaymentConfirmationRef == ApprovedPaymentConfirmationRef, "payment_confirmation_ref_not_approved", errors);
        Require(fixture.UpstreamFinalityRef == ApprovedUpstreamFinalityRef, "upstream_finality_ref_not_approved", errors);
        Require(fixture.Currency == ApprovedCurrency, "currency_not_approved", errors);
        Require(fixture.AmountMinorUnits == 10000, "amount_not_approved", errors);
        Require(fixture.BusinessDayDate == ApprovedBusinessDayDate, "business_day_date_not_approved", errors);

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Controlled UAT fixture does not match the approved first-run fixture: {string.Join(", ", errors)}",
                nameof(fixture));
        }
    }

    private static void Require(bool condition, string error, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(error);
        }
    }

    private static void AddParameters(NpgsqlCommand command, ControlledUatFiscalIssuanceFixture fixture)
    {
        var amount = fixture.AmountMinorUnits / 100m;
        var businessStart = new DateTimeOffset(
            fixture.BusinessDayDate.ToDateTime(new TimeOnly(8, 0)),
            TimeSpan.FromHours(8));
        var businessEnd = businessStart.AddDays(1);

        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = fixture.ServiceIdentityId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = fixture.SiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = fixture.SiteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = fixture.VendorSystemId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = fixture.ParkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = fixture.TariffSnapshotId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = fixture.PaymentAttemptId;
        command.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = fixture.PaymentConfirmationId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = fixture.CorrelationId;
        command.Parameters.Add("site_ref", NpgsqlDbType.Text).Value = fixture.SiteRef;
        command.Parameters.Add("parking_session_ref", NpgsqlDbType.Text).Value = fixture.ParkingSessionRef;
        command.Parameters.Add("payment_attempt_ref", NpgsqlDbType.Text).Value = fixture.PaymentAttemptRef;
        command.Parameters.Add("payment_confirmation_ref", NpgsqlDbType.Text).Value = fixture.PaymentConfirmationRef;
        command.Parameters.Add("upstream_finality_ref", NpgsqlDbType.Text).Value = fixture.UpstreamFinalityRef;
        command.Parameters.Add("currency", NpgsqlDbType.Text).Value = fixture.Currency;
        command.Parameters.Add("amount", NpgsqlDbType.Numeric).Value = amount;
        command.Parameters.Add("business_start_at", NpgsqlDbType.TimestampTz).Value = businessStart;
        command.Parameters.Add("business_end_at", NpgsqlDbType.TimestampTz).Value = businessEnd;
    }

    private const string Sql = """
        DO $$
        DECLARE
            db_name text := current_database();
        BEGIN
            IF db_name ~* '(prod|production|shared|live)' THEN
                RAISE EXCEPTION 'Refusing controlled UAT fixture preparation for unsafe database name: %', db_name;
            END IF;
        END $$;

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
            updated_at,
            row_version
        )
        VALUES (
            @service_identity_id,
            'CENTRAL-PMS-UAT-FISCAL-ISSUANCE',
            'Central PMS UAT Fiscal Issuance Service',
            'INTERNAL_SERVICE',
            'ACTIVE',
            'ExitPass.CentralPms',
            NULL,
            NULL,
            @business_start_at,
            NOW(),
            NOW(),
            1
        )
        ON CONFLICT ON CONSTRAINT uq_service_identities__service_identity_code DO UPDATE SET
            service_identity_id = EXCLUDED.service_identity_id,
            service_identity_code = EXCLUDED.service_identity_code,
            service_identity_name = EXCLUDED.service_identity_name,
            identity_type = EXCLUDED.identity_type,
            identity_status = EXCLUDED.identity_status,
            owning_service_name = EXCLUDED.owning_service_name,
            updated_at = NOW(),
            row_version = identity.service_identities.row_version + 1;

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
            'DEV-UAT-SITE-GROUP-ATC',
            'Disposable UAT Site Group ATC',
            'Disposable UAT',
            'Disposable local-only Central PMS to POS Server UAT seed data.',
            'ExitPass Local UAT',
            'Asia/Manila',
            @currency,
            'ACTIVE',
            FALSE,
            TRUE,
            @business_start_at,
            NOW(),
            @service_identity_id,
            NOW(),
            @service_identity_id,
            1
        )
        ON CONFLICT ON CONSTRAINT uq_site_groups__site_group_code DO UPDATE SET
            site_group_id = EXCLUDED.site_group_id,
            site_group_code = EXCLUDED.site_group_code,
            site_group_name = EXCLUDED.site_group_name,
            site_group_status = EXCLUDED.site_group_status,
            updated_at = NOW(),
            updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
            row_version = sites.site_groups.row_version + 1;

        INSERT INTO sites.sites (
            site_id,
            site_group_id,
            site_code,
            site_name,
            site_description,
            site_type,
            timezone_name,
            city,
            province,
            country_code,
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
            @site_ref,
            'Disposable UAT Site ATC 001',
            'Disposable local-only Central PMS to POS Server UAT site.',
            'MALL_PARKING',
            'Asia/Manila',
            'Makati',
            'Metro Manila',
            'PH',
            'ACTIVE',
            FALSE,
            TRUE,
            @business_start_at,
            NOW(),
            @service_identity_id,
            NOW(),
            @service_identity_id,
            1
        )
        ON CONFLICT ON CONSTRAINT uq_sites__site_group_site_code DO UPDATE SET
            site_id = EXCLUDED.site_id,
            site_group_id = EXCLUDED.site_group_id,
            site_code = EXCLUDED.site_code,
            site_name = EXCLUDED.site_name,
            site_status = EXCLUDED.site_status,
            payment_enabled = EXCLUDED.payment_enabled,
            updated_at = NOW(),
            updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
            row_version = sites.sites.row_version + 1;

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
            @vendor_system_id,
            'DEV-UAT-VENDOR-PMS',
            'Disposable UAT Vendor PMS',
            'VENDOR_PMS',
            'ACTIVE',
            'LOCAL-UAT',
            'local-only',
            'v1',
            'Central PMS UAT',
            'local-only',
            @business_start_at,
            NOW(),
            @service_identity_id,
            NOW(),
            @service_identity_id,
            1
        )
        ON CONFLICT ON CONSTRAINT uq_vendor_systems__vendor_code_environment DO UPDATE SET
            vendor_system_id = EXCLUDED.vendor_system_id,
            vendor_code = EXCLUDED.vendor_code,
            vendor_name = EXCLUDED.vendor_name,
            vendor_system_status = EXCLUDED.vendor_system_status,
            environment_code = EXCLUDED.environment_code,
            updated_at = NOW(),
            updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
            row_version = integration.vendor_systems.row_version + 1;

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
            @parking_session_ref,
            NULL,
            'UAT-001',
            NULL,
            'UAT-TICKET-001',
            @business_start_at,
            'PAYMENT_REQUIRED',
            'ACTIVE',
            @correlation_id,
            NOW(),
            @service_identity_id,
            NOW(),
            @service_identity_id,
            1
        )
        ON CONFLICT (parking_session_id) DO UPDATE SET
            site_group_id = EXCLUDED.site_group_id,
            site_id = EXCLUDED.site_id,
            vendor_system_id = EXCLUDED.vendor_system_id,
            vendor_session_ref = EXCLUDED.vendor_session_ref,
            session_status = EXCLUDED.session_status,
            correlation_id = EXCLUDED.correlation_id,
            updated_at = NOW(),
            updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
            row_version = core.parking_sessions.row_version + 1;

        UPDATE core.tariff_snapshots
        SET
            snapshot_status = 'EXPIRED',
            updated_at = NOW(),
            updated_by_service_identity_id = @service_identity_id,
            row_version = core.tariff_snapshots.row_version + 1
        WHERE parking_session_id = @parking_session_id
          AND snapshot_status = 'ACTIVE'
          AND tariff_snapshot_id <> @tariff_snapshot_id;

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
            'DEV-PAYABLE-BASIS-ATC-001',
            'DEV-PAYABLE-BASIS-ATC-V1',
            @currency,
            @amount,
            0.00,
            0.00,
            @amount,
            NULL,
            NULL,
            'ACTIVE',
            @business_start_at,
            @business_end_at,
            NULL,
            @correlation_id,
            NOW(),
            @service_identity_id,
            NOW(),
            @service_identity_id,
            1
        )
        ON CONFLICT (tariff_snapshot_id) DO UPDATE SET
            parking_session_id = EXCLUDED.parking_session_id,
            vendor_system_id = EXCLUDED.vendor_system_id,
            vendor_tariff_ref = EXCLUDED.vendor_tariff_ref,
            tariff_version_reference = EXCLUDED.tariff_version_reference,
            snapshot_status = EXCLUDED.snapshot_status,
            updated_at = NOW(),
            updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
            row_version = core.tariff_snapshots.row_version + 1;

        UPDATE core.payment_attempts
        SET
            parking_session_id = @parking_session_id,
            tariff_snapshot_id = @tariff_snapshot_id,
            idempotency_key = @payment_attempt_ref,
            currency_code = @currency,
            amount = @amount,
            attempt_status = 'CONFIRMED',
            finalized_at = @business_start_at,
            failure_reason_code = NULL,
            correlation_id = @correlation_id,
            updated_at = NOW(),
            updated_by_service_identity_id = @service_identity_id,
            row_version = core.payment_attempts.row_version + 1
        WHERE payment_attempt_id = @payment_attempt_id;

        INSERT INTO core.payment_attempts (
            payment_attempt_id,
            parking_session_id,
            tariff_snapshot_id,
            idempotency_key,
            payment_rail_id,
            currency_code,
            amount,
            attempt_status,
            requested_at,
            expires_at,
            finalized_at,
            failure_reason_code,
            correlation_id,
            created_at,
            created_by_service_identity_id,
            updated_at,
            updated_by_service_identity_id,
            row_version
        )
        VALUES (
            @payment_attempt_id,
            @parking_session_id,
            @tariff_snapshot_id,
            @payment_attempt_ref,
            NULL,
            @currency,
            @amount,
            'CONFIRMED',
            @business_start_at,
            @business_end_at,
            @business_start_at,
            NULL,
            @correlation_id,
            NOW(),
            @service_identity_id,
            NOW(),
            @service_identity_id,
            1
        )
        ON CONFLICT (payment_attempt_id) DO UPDATE SET
            parking_session_id = EXCLUDED.parking_session_id,
            tariff_snapshot_id = EXCLUDED.tariff_snapshot_id,
            idempotency_key = EXCLUDED.idempotency_key,
            currency_code = EXCLUDED.currency_code,
            amount = EXCLUDED.amount,
            attempt_status = EXCLUDED.attempt_status,
            finalized_at = EXCLUDED.finalized_at,
            correlation_id = EXCLUDED.correlation_id,
            updated_at = NOW(),
            updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
            row_version = core.payment_attempts.row_version + 1;

        UPDATE core.payment_confirmations
        SET
            payment_attempt_id = @payment_attempt_id,
            provider_transaction_ref = @upstream_finality_ref,
            currency_code = @currency,
            confirmed_amount = @amount,
            confirmation_status = 'RECORDED',
            verified_at = @business_start_at,
            confirmed_at = @business_start_at,
            correlation_id = @correlation_id,
            created_by_service_identity_id = @service_identity_id
        WHERE payment_confirmation_id = @payment_confirmation_id;

        INSERT INTO core.payment_confirmations (
            payment_confirmation_id,
            payment_attempt_id,
            provider_outcome_id,
            payment_rail_id,
            provider_transaction_ref,
            currency_code,
            confirmed_amount,
            confirmation_status,
            verified_at,
            confirmed_at,
            correlation_id,
            created_at,
            created_by_service_identity_id
        )
        VALUES (
            @payment_confirmation_id,
            @payment_attempt_id,
            NULL,
            NULL,
            @upstream_finality_ref,
            @currency,
            @amount,
            'RECORDED',
            @business_start_at,
            @business_start_at,
            @correlation_id,
            NOW(),
            @service_identity_id
        )
        ON CONFLICT (payment_confirmation_id) DO UPDATE SET
            payment_attempt_id = EXCLUDED.payment_attempt_id,
            provider_transaction_ref = EXCLUDED.provider_transaction_ref,
            currency_code = EXCLUDED.currency_code,
            confirmed_amount = EXCLUDED.confirmed_amount,
            confirmation_status = EXCLUDED.confirmation_status,
            verified_at = EXCLUDED.verified_at,
            confirmed_at = EXCLUDED.confirmed_at,
            correlation_id = EXCLUDED.correlation_id,
            created_by_service_identity_id = EXCLUDED.created_by_service_identity_id;
        """;
}
