using Npgsql;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

/// <summary>
/// Shared DB seed and cleanup helper for payment integration tests.
///
/// BRD:
/// - 11 Data and Record Model Requirements
///
/// SDD:
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - Test setup and teardown must preserve FK order
/// - Each test manipulates only its own data set
/// </summary>
public static class PaymentTestDataHelper
{
    /// <summary>
    /// Resets and seeds the canonical test data required for a payment integration scenario.
    /// </summary>
    public static async Task ResetAndSeedAsync(
        string connectionString,
        PaymentTestContext context,
        string description)
    {
        const string sql = """
            INSERT INTO sites.site_groups (
                site_group_id,
                site_group_code,
                site_group_name,
                site_group_status,
                timezone_name,
                country_code,
                region_code,
                description,
                created_by,
                updated_by,
                row_version
            )
            VALUES (
                @site_group_id,
                @site_group_id,
                @site_group_id,
                'ACTIVE',
                'Asia/Manila',
                'PH',
                'NCR',
                @description,
                'race-seed',
                'race-seed',
                1
            )
            ON CONFLICT (site_group_id) DO NOTHING;

            INSERT INTO sites.sites (
                site_id,
                site_group_id,
                site_code,
                site_name,
                timezone_name,
                site_status,
                address_line_1,
                city,
                province_or_state,
                postal_code,
                created_by,
                updated_by,
                row_version
            )
            VALUES (
                @site_id,
                @site_group_id,
                @site_id,
                @site_id,
                'Asia/Manila',
                'ACTIVE',
                'Test Address',
                'Quezon City',
                'Metro Manila',
                '1101',
                'race-seed',
                'race-seed',
                1
            )
            ON CONFLICT (site_id) DO NOTHING;

            INSERT INTO integration.vendor_systems (
                vendor_system_code,
                vendor_system_name,
                vendor_type,
                system_status,
                created_by,
                updated_by,
                row_version
            )
            VALUES (
                @vendor_system_code,
                @vendor_system_code,
                'PMS',
                'ACTIVE',
                'race-seed',
                'race-seed',
                1
            )
            ON CONFLICT (vendor_system_code) DO NOTHING;

            INSERT INTO identity.service_identities (
                service_identity_id,
                service_identity_code,
                identity_type,
                identity_name,
                identity_status,
                certificate_thumbprint,
                device_id,
                created_at,
                created_by,
                updated_at,
                updated_by,
                row_version
            )
            VALUES (
                @requested_by_user_id,
                @service_identity_code,
                'SERVICE',
                @service_identity_name,
                'ACTIVE',
                NULL,
                NULL,
                NOW(),
                'race-seed',
                NOW(),
                'race-seed',
                1
            )
            ON CONFLICT (service_identity_id) DO NOTHING;

            UPDATE core.tariff_snapshots
            SET
                consumed_by_payment_attempt_id = NULL,
                updated_at = NOW(),
                updated_by = 'race-seed',
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id
              AND consumed_by_payment_attempt_id IS NOT NULL;

            DELETE FROM core.exit_authorizations
            WHERE parking_session_id = @parking_session_id
               OR payment_attempt_id IN (
                    SELECT payment_attempt_id
                    FROM core.payment_attempts
                    WHERE parking_session_id = @parking_session_id
               );

            DELETE FROM core.payment_confirmations
            WHERE payment_attempt_id IN (
                SELECT payment_attempt_id
                FROM core.payment_attempts
                WHERE parking_session_id = @parking_session_id
            );

            DELETE FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id;

            INSERT INTO core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_code,
                vendor_session_ref,
                identifier_type,
                plate_number,
                ticket_number,
                entry_timestamp,
                session_status,
                created_by,
                updated_by,
                row_version
            )
            VALUES (
                @parking_session_id,
                @site_group_id,
                @site_id,
                @vendor_system_code,
                @vendor_session_ref,
                'PLATE',
                'ABC1234',
                NULL,
                NOW() - INTERVAL '2 hours',
                'PAYMENT_REQUIRED',
                'race-seed',
                'race-seed',
                1
            );

            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                source_type,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_payable,
                currency_code,
                base_fee_amount,
                tariff_version_reference,
                policy_version_reference,
                calculated_at,
                expires_at,
                snapshot_status,
                supersedes_tariff_snapshot_id,
                consumed_by_payment_attempt_id,
                created_by,
                updated_by,
                row_version
            )
            VALUES (
                @tariff_snapshot_id,
                @parking_session_id,
                'BASE',
                100.00,
                0.00,
                0.00,
                100.00,
                'PHP',
                100.00,
                @tariff_version_reference,
                NULL,
                NOW(),
                NOW() + INTERVAL '1 hour',
                'ACTIVE',
                NULL,
                NULL,
                'race-seed',
                'race-seed',
                1
            );
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("site_group_id", context.SiteGroupId);
        command.Parameters.AddWithValue("site_id", context.SiteId);
        command.Parameters.AddWithValue("vendor_system_code", context.VendorSystemCode);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("vendor_session_ref", $"RACE-VSESSION-{context.ParkingSessionId:N}");
        command.Parameters.AddWithValue("tariff_version_reference", $"TVR-{context.TariffSnapshotId:N}");
        command.Parameters.AddWithValue("requested_by_user_id", context.RequestedByUserId);
        command.Parameters.AddWithValue("service_identity_code", $"SVC-{context.RequestedByUserId:N}");
        command.Parameters.AddWithValue("service_identity_name", $"TEST-SVC-{context.RequestedByUserId:N}");

        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Removes seeded data for the supplied payment integration test context.
    /// </summary>
    public static async Task CleanupAsync(string connectionString, PaymentTestContext context)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET
                consumed_by_payment_attempt_id = NULL,
                updated_at = NOW(),
                updated_by = 'race-seed',
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id
              AND consumed_by_payment_attempt_id IS NOT NULL;

            DELETE FROM core.exit_authorizations
            WHERE parking_session_id = @parking_session_id
               OR payment_attempt_id IN (
                    SELECT payment_attempt_id
                    FROM core.payment_attempts
                    WHERE parking_session_id = @parking_session_id
               );

            DELETE FROM core.payment_confirmations
            WHERE payment_attempt_id IN (
                SELECT payment_attempt_id
                FROM core.payment_attempts
                WHERE parking_session_id = @parking_session_id
            );

            DELETE FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM identity.service_identities
            WHERE service_identity_id = @requested_by_user_id;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("requested_by_user_id", context.RequestedByUserId);

        await command.ExecuteNonQueryAsync();
    }
}
