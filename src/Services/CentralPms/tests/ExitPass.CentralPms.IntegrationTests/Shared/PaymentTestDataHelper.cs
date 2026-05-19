using Npgsql;
using NpgsqlTypes;

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
/// - Test setup and teardown must preserve FK order.
/// - Each test manipulates only its own data set.
/// - Seed data must follow the ExitPass v1.2 physical schema.
/// - Service-origin records must use service identity audit attribution.
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
            /*
             * BRD Requirement:
             * - BRD 11 Data and Record Model Requirements
             *
             * SDD Section:
             * - SDD 9.6 Integrity Constraints and Concurrency Rules
             *
             * System Invariant:
             * - Integration tests must seed only v1.2-compliant canonical control data.
             * - Test rows must preserve FK order and must not reintroduce v1.0/v1.1 columns.
             */

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
                @requested_by_service_identity_id,
                @service_identity_code,
                @service_identity_name,
                'DEVICE',
                'ACTIVE',
                'ExitPass.CentralPms.IntegrationTests',
                NULL,
                'NONE',
                NOW() - INTERVAL '1 minute',
                NOW(),
                @requested_by_service_identity_id,
                NOW(),
                @requested_by_service_identity_id,
                1
            )
            ON CONFLICT (service_identity_id) DO UPDATE
            SET
                service_identity_code = EXCLUDED.service_identity_code,
                service_identity_name = EXCLUDED.service_identity_name,
                identity_type = EXCLUDED.identity_type,
                identity_status = EXCLUDED.identity_status,
                owning_service_name = EXCLUDED.owning_service_name,
                credential_type = EXCLUDED.credential_type,
                updated_at = NOW(),
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
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
                @site_group_code,
                @site_group_name,
                'PROPERTY',
                @description,
                'ExitPass Test Operator',
                'Asia/Manila',
                'PHP',
                'ACTIVE',
                TRUE,
                TRUE,
                NOW() - INTERVAL '1 minute',
                NOW(),
                @requested_by_service_identity_id,
                NOW(),
                @requested_by_service_identity_id,
                1
            )
            ON CONFLICT (site_group_id) DO UPDATE
            SET
                site_group_code = EXCLUDED.site_group_code,
                site_group_name = EXCLUDED.site_group_name,
                business_label = EXCLUDED.business_label,
                description = EXCLUDED.description,
                operator_entity_name = EXCLUDED.operator_entity_name,
                timezone_name = EXCLUDED.timezone_name,
                default_currency_code = EXCLUDED.default_currency_code,
                site_group_status = EXCLUDED.site_group_status,
                public_lookup_enabled = EXCLUDED.public_lookup_enabled,
                default_payment_enabled = EXCLUDED.default_payment_enabled,
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
                @description,
                'MALL_PARKING',
                'Asia/Manila',
                'Test Address Line 1',
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
                @requested_by_service_identity_id,
                NOW(),
                @requested_by_service_identity_id,
                1
            )
            ON CONFLICT (site_id) DO UPDATE
            SET
                site_group_id = EXCLUDED.site_group_id,
                site_code = EXCLUDED.site_code,
                site_name = EXCLUDED.site_name,
                site_description = EXCLUDED.site_description,
                site_type = EXCLUDED.site_type,
                timezone_name = EXCLUDED.timezone_name,
                address_line1 = EXCLUDED.address_line1,
                address_line2 = EXCLUDED.address_line2,
                city = EXCLUDED.city,
                province = EXCLUDED.province,
                country_code = EXCLUDED.country_code,
                lgu_code = EXCLUDED.lgu_code,
                site_status = EXCLUDED.site_status,
                public_lookup_enabled = EXCLUDED.public_lookup_enabled,
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
                gen_random_uuid(),
                @vendor_system_code,
                @vendor_system_name,
                'VENDOR_PMS',
                'ACTIVE',
                'TEST',
                'mock://vendor-pms',
                'v1',
                'ExitPass Integration Tests',
                'test-support',
                NOW() - INTERVAL '1 minute',
                NOW(),
                @requested_by_service_identity_id,
                NOW(),
                @requested_by_service_identity_id,
                1
            )
            ON CONFLICT (vendor_code, environment_code) DO UPDATE
            SET
                vendor_name = EXCLUDED.vendor_name,
                vendor_system_type = EXCLUDED.vendor_system_type,
                vendor_system_status = EXCLUDED.vendor_system_status,
                base_url_ref = EXCLUDED.base_url_ref,
                api_version = EXCLUDED.api_version,
                owner_team = EXCLUDED.owner_team,
                support_contact_ref = EXCLUDED.support_contact_ref,
                updated_at = NOW(),
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                row_version = integration.vendor_systems.row_version + 1;

            UPDATE core.tariff_snapshots
            SET
                consumed_at = NULL,
                snapshot_status = 'ACTIVE',
                updated_at = NOW(),
                updated_by_service_identity_id = @requested_by_service_identity_id,
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id
              AND consumed_at IS NOT NULL;

            DELETE FROM gates.gate_authorization_consumptions
            WHERE exit_authorization_id IN (
                SELECT exit_authorization_id
                FROM core.exit_authorizations
                WHERE parking_session_id = @parking_session_id
                   OR payment_attempt_id IN (
                        SELECT payment_attempt_id
                        FROM core.payment_attempts
                        WHERE parking_session_id = @parking_session_id
                   )
            );

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
                (
                    SELECT vendor_system_id
                    FROM integration.vendor_systems
                    WHERE vendor_code = @vendor_system_code
                      AND environment_code = 'TEST'
                ),
                @vendor_session_ref,
                @plate_number_hash,
                'ABC1234',
                NULL,
                NULL,
                NOW() - INTERVAL '2 hours',
                'PAYMENT_REQUIRED',
                'ACTIVE',
                @correlation_id,
                NOW(),
                @requested_by_service_identity_id,
                NOW(),
                @requested_by_service_identity_id,
                1
            );

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
                (
                    SELECT vendor_system_id
                    FROM integration.vendor_systems
                    WHERE vendor_code = @vendor_system_code
                      AND environment_code = 'TEST'
                ),
                @vendor_tariff_ref,
                @tariff_version_reference,
                'PHP',
                100.00,
                0.00,
                0.00,
                100.00,
                NULL,
                NULL,
                'ACTIVE',
                NOW(),
                NOW() + INTERVAL '1 hour',
                NULL,
                @correlation_id,
                NOW(),
                @requested_by_service_identity_id,
                NOW(),
                @requested_by_service_identity_id,
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

        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.SiteGroupId, nameof(context.SiteGroupId));
        command.Parameters.AddWithValue("site_group_code", $"SG-{context.SiteGroupId:N}");
        command.Parameters.AddWithValue("site_group_name", $"TEST-SG-{context.SiteGroupId:N}");

        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.SiteId, nameof(context.SiteId));
        command.Parameters.AddWithValue("site_code", $"SITE-{context.SiteId:N}");
        command.Parameters.AddWithValue("site_name", $"TEST-SITE-{context.SiteId:N}");

        command.Parameters.AddWithValue("vendor_system_code", context.VendorSystemCode);
        command.Parameters.AddWithValue("vendor_system_name", context.VendorSystemCode);

        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.ParkingSessionId, nameof(context.ParkingSessionId));
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.TariffSnapshotId, nameof(context.TariffSnapshotId));

        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("vendor_session_ref", $"RACE-VSESSION-{context.ParkingSessionId:N}");
        command.Parameters.AddWithValue("vendor_tariff_ref", $"VTAR-{context.TariffSnapshotId:N}");
        command.Parameters.AddWithValue("tariff_version_reference", $"TVR-{context.TariffSnapshotId:N}");

        command.Parameters.Add("requested_by_service_identity_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.RequestedByUserId, nameof(context.RequestedByUserId));
        command.Parameters.AddWithValue("service_identity_code", $"SVC-{context.RequestedByUserId:N}");
        command.Parameters.AddWithValue("service_identity_name", $"TEST-SVC-{context.RequestedByUserId:N}");

        command.Parameters.AddWithValue("plate_number_hash", "0000000000000000000000000000000000000000000000000000000000000000");
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();

        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Removes seeded data for the supplied payment integration test context.
    /// </summary>
    public static async Task CleanupAsync(string connectionString, PaymentTestContext context)
    {
        const string sql = """
            /*
             * BRD Requirement:
             * - BRD 11 Data and Record Model Requirements
             *
             * SDD Section:
             * - SDD 9.6 Integrity Constraints and Concurrency Rules
             *
             * System Invariant:
             * - Cleanup must reverse the FK dependency order used during test seeding.
             */

            UPDATE core.tariff_snapshots
            SET
                consumed_at = NULL,
                snapshot_status = 'ACTIVE',
                updated_at = NOW(),
                updated_by_service_identity_id = @requested_by_service_identity_id,
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id
              AND consumed_at IS NOT NULL;

            DELETE FROM gates.gate_authorization_consumptions
            WHERE exit_authorization_id IN (
                SELECT exit_authorization_id
                FROM core.exit_authorizations
                WHERE parking_session_id = @parking_session_id
                   OR payment_attempt_id IN (
                        SELECT payment_attempt_id
                        FROM core.payment_attempts
                        WHERE parking_session_id = @parking_session_id
                   )
            );

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

            DELETE FROM sites.sites
            WHERE site_id = @site_id;

            DELETE FROM sites.site_groups
            WHERE site_group_id = @site_group_id;

            DELETE FROM integration.vendor_systems
            WHERE vendor_code = @vendor_system_code
              AND environment_code = 'TEST';

            DELETE FROM identity.service_identities
            WHERE service_identity_id = @requested_by_service_identity_id;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.ParkingSessionId, nameof(context.ParkingSessionId));

        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.SiteId, nameof(context.SiteId));

        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.SiteGroupId, nameof(context.SiteGroupId));

        command.Parameters.AddWithValue("vendor_system_code", context.VendorSystemCode);
        
        command.Parameters.Add("requested_by_service_identity_id", NpgsqlDbType.Uuid).Value =
            ToGuid(context.RequestedByUserId, nameof(context.RequestedByUserId));

        await command.ExecuteNonQueryAsync();
    }
    private static Guid ToGuid(object value, string parameterName)
    {
        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => throw new InvalidOperationException(
                $"Payment test context value '{parameterName}' must be a Guid or a valid Guid string.")
        };
    }
}
