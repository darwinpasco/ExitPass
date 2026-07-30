-- ExitPass v1.3 WebPay ordinary-payment local walkthrough fixture.
-- Scope: one non-statutory WebPay parking session and active payable basis.
-- This script is intended only for the disposable database:
-- exitpass_webpay_local_walkthrough

BEGIN;

DO $$
DECLARE
    v_database_name text := current_database();
    v_valid_from timestamptz := now() - interval '1 day';
    v_valid_to timestamptz := now() + interval '2 days';
    v_service_identity_id uuid;
    v_site_group_id uuid;
    v_site_id uuid;
    v_vendor_system_id uuid;
    v_payment_rail_id uuid;
    v_session_id uuid := '24100000-0000-0000-0000-000000000001'::uuid;
    v_tariff_snapshot_id uuid := '24100000-0000-0000-0000-000000000002'::uuid;
    v_ticket text := 'WEBPAY-LOCAL-ORDINARY-001';
    v_plate text := 'LOCALPAY001';
    v_vendor_session_ref text := 'FAKE-SESSION-LOCALPAY001';
    v_amount numeric(18,2) := 137.50;
BEGIN
    IF v_database_name !~ '^exitpass_webpay_local_walkthrough(_[a-z0-9_]+)?$' THEN
        RAISE EXCEPTION 'Refusing to seed WebPay local walkthrough fixture against database %. Use a disposable exitpass_webpay_local_walkthrough database.', v_database_name;
    END IF;

    INSERT INTO identity.service_identities (
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
        'WEBPAY_LOCAL_WALKTHROUGH_SEEDER',
        'WebPay Local Walkthrough Seeder',
        'INTERNAL_SERVICE',
        'ACTIVE',
        'ExitPass Local Walkthrough',
        'NONE',
        v_valid_from,
        v_valid_to
    )
    ON CONFLICT (service_identity_code)
    DO UPDATE SET
        service_identity_name = EXCLUDED.service_identity_name,
        identity_type = EXCLUDED.identity_type,
        identity_status = EXCLUDED.identity_status,
        owning_service_name = EXCLUDED.owning_service_name,
        credential_type = EXCLUDED.credential_type,
        effective_from = EXCLUDED.effective_from,
        effective_to = EXCLUDED.effective_to,
        updated_at = now()
    RETURNING service_identity_id
    INTO v_service_identity_id;

    INSERT INTO sites.site_groups (
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
        'WEBPAY_LOCAL_GROUP',
        'WebPay Local Walkthrough Group',
        'Property',
        'Disposable ordinary WebPay walkthrough site group.',
        'ExitPass Local',
        'Asia/Manila',
        'PHP',
        'ACTIVE',
        true,
        true,
        v_valid_from,
        v_valid_to,
        v_service_identity_id,
        v_service_identity_id
    )
    ON CONFLICT (site_group_code)
    DO UPDATE SET
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
        updated_at = now(),
        updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id
    RETURNING site_group_id
    INTO v_site_group_id;

    INSERT INTO sites.sites (
        site_group_id,
        site_code,
        site_name,
        site_description,
        site_type,
        timezone_name,
        address_line1,
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
        v_site_group_id,
        'WEBPAY_LOCAL_SITE',
        'WebPay Local Walkthrough Site',
        'Disposable ordinary WebPay walkthrough site.',
        'MALL_PARKING',
        'Asia/Manila',
        'Local Walkthrough Avenue',
        'Manila',
        'Metro Manila',
        'PH',
        'ACTIVE',
        true,
        true,
        v_valid_from,
        v_valid_to,
        v_service_identity_id,
        v_service_identity_id
    )
    ON CONFLICT (site_group_id, site_code)
    DO UPDATE SET
        site_name = EXCLUDED.site_name,
        site_description = EXCLUDED.site_description,
        site_type = EXCLUDED.site_type,
        timezone_name = EXCLUDED.timezone_name,
        address_line1 = EXCLUDED.address_line1,
        city = EXCLUDED.city,
        province = EXCLUDED.province,
        country_code = EXCLUDED.country_code,
        site_status = EXCLUDED.site_status,
        public_lookup_enabled = EXCLUDED.public_lookup_enabled,
        payment_enabled = EXCLUDED.payment_enabled,
        effective_from = EXCLUDED.effective_from,
        effective_to = EXCLUDED.effective_to,
        updated_at = now(),
        updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id
    RETURNING site_id
    INTO v_site_id;

    INSERT INTO integration.vendor_systems (
        vendor_code,
        vendor_name,
        vendor_system_type,
        vendor_system_status,
        environment_code,
        base_url_ref,
        api_version,
        owner_team,
        effective_from,
        effective_to,
        created_by_service_identity_id,
        updated_by_service_identity_id
    )
    VALUES (
        'WEBPAY_LOCAL_MOCK_PMS',
        'WebPay Local Mock Vendor PMS',
        'VENDOR_PMS',
        'ACTIVE',
        'LOCAL',
        'mock://webpay-local-walkthrough',
        'v1',
        'ExitPass Local',
        v_valid_from,
        v_valid_to,
        v_service_identity_id,
        v_service_identity_id
    )
    ON CONFLICT (vendor_code, environment_code)
    DO UPDATE SET
        vendor_name = EXCLUDED.vendor_name,
        vendor_system_type = EXCLUDED.vendor_system_type,
        vendor_system_status = EXCLUDED.vendor_system_status,
        base_url_ref = EXCLUDED.base_url_ref,
        api_version = EXCLUDED.api_version,
        owner_team = EXCLUDED.owner_team,
        effective_from = EXCLUDED.effective_from,
        effective_to = EXCLUDED.effective_to,
        updated_at = now(),
        updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id
    RETURNING vendor_system_id
    INTO v_vendor_system_id;

    INSERT INTO payments.payment_rails (
        rail_code,
        rail_name,
        provider_code,
        rail_type,
        supported_currency_code,
        rail_status,
        is_primary,
        is_fallback,
        provider_profile_ref,
        configuration_ref,
        effective_from,
        effective_to,
        created_by_service_identity_id,
        updated_by_service_identity_id
    )
    VALUES (
        'PAYMONGO_QRPH_WEBPAY_LOCAL',
        'PayMongo QRPh WebPay Local Walkthrough Rail',
        'PAYMONGO',
        'QRPH',
        'PHP',
        'ACTIVE',
        true,
        false,
        'PAYMONGO_LOCAL',
        'WEBPAY_LOCAL_WALKTHROUGH',
        v_valid_from,
        v_valid_to,
        v_service_identity_id,
        v_service_identity_id
    )
    ON CONFLICT (rail_code)
    DO UPDATE SET
        rail_name = EXCLUDED.rail_name,
        provider_code = EXCLUDED.provider_code,
        rail_type = EXCLUDED.rail_type,
        supported_currency_code = EXCLUDED.supported_currency_code,
        rail_status = EXCLUDED.rail_status,
        is_primary = EXCLUDED.is_primary,
        is_fallback = EXCLUDED.is_fallback,
        provider_profile_ref = EXCLUDED.provider_profile_ref,
        configuration_ref = EXCLUDED.configuration_ref,
        effective_from = EXCLUDED.effective_from,
        effective_to = EXCLUDED.effective_to,
        updated_at = now(),
        updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id
    RETURNING payment_rail_id
    INTO v_payment_rail_id;

    UPDATE payments.payment_provider_routing_policies
    SET
        primary_provider_code = 'PAYMONGO',
        fallback_provider_code = NULL,
        is_enabled = true,
        primary_provider_enabled = true,
        fallback_provider_enabled = false,
        updated_at = now(),
        row_version = row_version + 1
    WHERE site_id IS NULL
      AND site_group_id IS NULL
      AND payment_method_code = 'QRPH'
      AND currency_code = 'PHP'
      AND min_amount_minor_units IS NULL
      AND max_amount_minor_units IS NULL;

    DELETE FROM payments.provider_sessions prv
    USING core.payment_attempts pa
    WHERE prv.payment_attempt_id = pa.payment_attempt_id
      AND pa.parking_session_id = v_session_id;

    DELETE FROM core.payment_attempts
    WHERE parking_session_id = v_session_id;

    DELETE FROM core.tariff_snapshots
    WHERE parking_session_id = v_session_id;

    DELETE FROM sessions.session_identifier_indexes
    WHERE parking_session_id = v_session_id
       OR identifier_masked IN (v_ticket, v_plate);

    DELETE FROM core.parking_sessions
    WHERE parking_session_id = v_session_id
       OR vendor_session_ref IN ('WEBPAY-LOCAL-ORDINARY-SESSION', v_vendor_session_ref)
       OR ticket_number_masked = v_ticket
       OR plate_number_masked = v_plate;

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
        v_session_id,
        v_site_group_id,
        v_site_id,
        v_vendor_system_id,
        v_vendor_session_ref,
        encode(digest(v_plate, 'sha256'), 'hex'),
        v_plate,
        encode(digest(v_ticket, 'sha256'), 'hex'),
        v_ticket,
        now() - interval '2 hours',
        'ACTIVE',
        'ACTIVE',
        gen_random_uuid(),
        v_service_identity_id,
        v_service_identity_id
    );

    INSERT INTO sessions.session_identifier_indexes (
        parking_session_id,
        site_group_id,
        site_id,
        vendor_system_id,
        identifier_type,
        identifier_hash,
        identifier_masked,
        identifier_status,
        effective_from,
        effective_to,
        created_by_service_identity_id,
        updated_by_service_identity_id,
        correlation_id
    )
    VALUES
    (
        v_session_id,
        v_site_group_id,
        v_site_id,
        v_vendor_system_id,
        'TICKET_NUMBER',
        encode(digest(v_ticket, 'sha256'), 'hex'),
        v_ticket,
        'ACTIVE',
        v_valid_from,
        v_valid_to,
        v_service_identity_id,
        v_service_identity_id,
        gen_random_uuid()
    ),
    (
        v_session_id,
        v_site_group_id,
        v_site_id,
        v_vendor_system_id,
        'PLATE_NUMBER',
        encode(digest(v_plate, 'sha256'), 'hex'),
        v_plate,
        'ACTIVE',
        v_valid_from,
        v_valid_to,
        v_service_identity_id,
        v_service_identity_id,
        gen_random_uuid()
    );

    INSERT INTO core.tariff_snapshots (
        tariff_snapshot_id,
        parking_session_id,
        vendor_system_id,
        vendor_tariff_ref,
        tariff_version_reference,
        currency_code,
        gross_amount,
        statutory_discount_amount,
        coupon_discount_amount,
        net_amount,
        snapshot_status,
        calculated_at,
        expires_at,
        correlation_id,
        created_by_service_identity_id,
        updated_by_service_identity_id
    )
    VALUES (
        v_tariff_snapshot_id,
        v_session_id,
        v_vendor_system_id,
        'WEBPAY-LOCAL-ORDINARY-TARIFF',
        'WEBPAY-LOCAL-ORDINARY-v1',
        'PHP',
        v_amount,
        0.00,
        0.00,
        v_amount,
        'ACTIVE',
        now(),
        now() + interval '4 hours',
        gen_random_uuid(),
        v_service_identity_id,
        v_service_identity_id
    );
END $$;

COMMIT;

SELECT
    'WEBPAY_LOCAL_ORDINARY_FIXTURE_READY' AS result,
    'WEBPAY-LOCAL-ORDINARY-001' AS ticket_reference,
    'LOCALPAY001' AS plate_number,
    '24100000-0000-0000-0000-000000000001'::uuid AS parking_session_id,
    '24100000-0000-0000-0000-000000000002'::uuid AS tariff_snapshot_id,
    13750 AS amount_minor_units,
    'PHP' AS currency_code;
