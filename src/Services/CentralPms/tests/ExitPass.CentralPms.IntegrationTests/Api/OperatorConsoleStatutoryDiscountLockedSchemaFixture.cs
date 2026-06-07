using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.IntegrationTests.Api;

internal static class OperatorConsoleStatutoryDiscountLockedSchemaFixture
{
    public static async Task SeedAsync(Func<Task<NpgsqlConnection>> openConnectionAsync)
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            UPDATE core.tariff_snapshots
               SET statutory_discount_validation_id = NULL,
                   superseded_by_tariff_snapshot_id = NULL
             WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE requested_by_user_id IN (
                    '77000000-0000-0000-0000-000000000010',
                    '77000000-0000-0000-0000-000000000011'
                )
             );

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE requested_by_user_id IN (
                    '77000000-0000-0000-0000-000000000010',
                    '77000000-0000-0000-0000-000000000011'
                )
            );

            DELETE FROM discounts.statutory_discount_validations
            WHERE requested_by_user_id IN (
                '77000000-0000-0000-0000-000000000010',
                '77000000-0000-0000-0000-000000000011'
            );

            DELETE FROM discounts.discount_policy_references
            WHERE site_id = '77000000-0000-0000-0000-000000000002'
               OR site_group_id = '77000000-0000-0000-0000-000000000001'
               OR lgu_code LIKE 'PH-INT-%'
               OR policy_code LIKE 'INTEGRATION_%'
               OR policy_code IN (
                    'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                    'PH_RA10754_PWD_NATIONAL_FALLBACK'
               );

            INSERT INTO identity.service_identities (
                service_identity_id,
                service_identity_code,
                service_identity_name,
                identity_type,
                identity_status,
                owning_service_name,
                credential_type,
                effective_from,
                effective_to
            )
            VALUES (
                '77000000-0000-0000-0000-000000000003',
                'MANUAL_TEST_OPERATOR_ACCESS_FIXTURE_SERVICE',
                'Manual Test Operator Access Fixture Service',
                'INTERNAL_SERVICE',
                'ACTIVE',
                'Central PMS Manual Fixtures',
                'NONE',
                '2020-01-01T00:00:00Z',
                '2035-01-01T00:00:00Z'
            )
            ON CONFLICT (service_identity_id) DO UPDATE
            SET service_identity_code = EXCLUDED.service_identity_code,
                service_identity_name = EXCLUDED.service_identity_name,
                identity_type = EXCLUDED.identity_type,
                identity_status = EXCLUDED.identity_status,
                owning_service_name = EXCLUDED.owning_service_name,
                credential_type = EXCLUDED.credential_type,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_at = now();

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
                effective_to,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000001',
                'MANUAL_TEST_OPERATOR_ACCESS_GROUP',
                'Manual Test Operator Access Site Group',
                'Manual Test Operator Access',
                'Local fixture site group for Operator Console tests.',
                'Manual Test Operator Access',
                'Asia/Manila',
                'PHP',
                'ACTIVE',
                false,
                false,
                '2020-01-01T00:00:00Z',
                '2035-01-01T00:00:00Z',
                '77000000-0000-0000-0000-000000000003',
                '77000000-0000-0000-0000-000000000003'
            )
            ON CONFLICT (site_group_id) DO UPDATE
            SET site_group_code = EXCLUDED.site_group_code,
                site_group_name = EXCLUDED.site_group_name,
                business_label = EXCLUDED.business_label,
                description = EXCLUDED.description,
                operator_entity_name = EXCLUDED.operator_entity_name,
                timezone_name = EXCLUDED.timezone_name,
                default_currency_code = EXCLUDED.default_currency_code,
                site_group_status = EXCLUDED.site_group_status,
                public_lookup_enabled = EXCLUDED.public_lookup_enabled,
                default_payment_enabled = EXCLUDED.default_payment_enabled,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                updated_at = now();

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
                effective_to,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000002',
                '77000000-0000-0000-0000-000000000001',
                'MANUAL_TEST_OPERATOR_ACCESS_SITE',
                'Manual Test Operator Access Site',
                'Local fixture site for Operator Console tests.',
                'OTHER',
                'Asia/Manila',
                'Pasig',
                'Metro Manila',
                'PH',
                'ACTIVE',
                false,
                false,
                '2020-01-01T00:00:00Z',
                '2035-01-01T00:00:00Z',
                '77000000-0000-0000-0000-000000000003',
                '77000000-0000-0000-0000-000000000003'
            )
            ON CONFLICT (site_id) DO UPDATE
            SET site_group_id = EXCLUDED.site_group_id,
                site_code = EXCLUDED.site_code,
                site_name = EXCLUDED.site_name,
                site_description = EXCLUDED.site_description,
                site_type = EXCLUDED.site_type,
                timezone_name = EXCLUDED.timezone_name,
                city = EXCLUDED.city,
                province = EXCLUDED.province,
                country_code = EXCLUDED.country_code,
                site_status = EXCLUDED.site_status,
                public_lookup_enabled = EXCLUDED.public_lookup_enabled,
                payment_enabled = EXCLUDED.payment_enabled,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                updated_at = now();

            INSERT INTO integration.vendor_systems (
                vendor_system_id,
                vendor_code,
                vendor_name,
                vendor_system_type,
                vendor_system_status,
                environment_code,
                owner_team,
                effective_from,
                effective_to,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000004',
                'MANUAL_TEST_OPERATOR_ACCESS_VENDOR_PMS',
                'Manual Test Operator Access Vendor PMS',
                'VENDOR_PMS',
                'ACTIVE',
                'LOCAL',
                'Central PMS Manual Fixtures',
                '2020-01-01T00:00:00Z',
                '2035-01-01T00:00:00Z',
                '77000000-0000-0000-0000-000000000003',
                '77000000-0000-0000-0000-000000000003'
            )
            ON CONFLICT (vendor_system_id) DO UPDATE
            SET vendor_code = EXCLUDED.vendor_code,
                vendor_name = EXCLUDED.vendor_name,
                vendor_system_type = EXCLUDED.vendor_system_type,
                vendor_system_status = EXCLUDED.vendor_system_status,
                environment_code = EXCLUDED.environment_code,
                owner_team = EXCLUDED.owner_team,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                updated_at = now();

            INSERT INTO identity.users (
                user_id,
                username,
                email,
                email_normalized,
                display_name,
                user_type,
                user_status,
                effective_from,
                effective_to,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES
                (
                    '77000000-0000-0000-0000-000000000010',
                    'manual-test-operator-access-allowed',
                    'manual-test-operator-access-allowed@example.test',
                    'MANUAL-TEST-OPERATOR-ACCESS-ALLOWED@EXAMPLE.TEST',
                    'Manual Test Operator Access Allowed',
                    'SITE_OPERATOR',
                    'ACTIVE',
                    '2020-01-01T00:00:00Z',
                    '2035-01-01T00:00:00Z',
                    '77000000-0000-0000-0000-000000000003',
                    '77000000-0000-0000-0000-000000000003'
                ),
                (
                    '77000000-0000-0000-0000-000000000011',
                    'manual-test-operator-access-inactive',
                    'manual-test-operator-access-inactive@example.test',
                    'MANUAL-TEST-OPERATOR-ACCESS-INACTIVE@EXAMPLE.TEST',
                    'Manual Test Operator Access Inactive',
                    'SITE_OPERATOR',
                    'SUSPENDED',
                    '2020-01-01T00:00:00Z',
                    '2035-01-01T00:00:00Z',
                    '77000000-0000-0000-0000-000000000003',
                    '77000000-0000-0000-0000-000000000003'
                )
            ON CONFLICT (user_id) DO UPDATE
            SET username = EXCLUDED.username,
                email = EXCLUDED.email,
                email_normalized = EXCLUDED.email_normalized,
                display_name = EXCLUDED.display_name,
                user_type = EXCLUDED.user_type,
                user_status = EXCLUDED.user_status,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                updated_at = now();

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
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000090',
                '77000000-0000-0000-0000-000000000001',
                '77000000-0000-0000-0000-000000000002',
                '77000000-0000-0000-0000-000000000004',
                'MANUAL-SESSION-LOOKUP-001',
                '7700000000000000000000000000009077000000000000000000000000000090',
                'MANUAL-090',
                '7700000000000000000000000000009177000000000000000000000000000091',
                'MANUAL-SESSION-LOOKUP-001',
                '2026-05-29T00:00:00Z',
                'ACTIVE',
                'ACTIVE',
                gen_random_uuid(),
                '77000000-0000-0000-0000-000000000003',
                '77000000-0000-0000-0000-000000000003'
            )
            ON CONFLICT (parking_session_id) DO UPDATE
            SET site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                vendor_system_id = EXCLUDED.vendor_system_id,
                vendor_session_ref = EXCLUDED.vendor_session_ref,
                plate_number_hash = EXCLUDED.plate_number_hash,
                plate_number_masked = EXCLUDED.plate_number_masked,
                ticket_number_hash = EXCLUDED.ticket_number_hash,
                ticket_number_masked = EXCLUDED.ticket_number_masked,
                vendor_session_status = EXCLUDED.vendor_session_status,
                session_status = EXCLUDED.session_status,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                updated_at = now();

            COMMIT;
            """;

        await using var connection = await openConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };
        await command.ExecuteNonQueryAsync();
    }
}
