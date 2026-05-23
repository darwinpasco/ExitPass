-- scripts/dev-data/webpay-20260523-seed.sql
-- ExitPass v1.2 WebPay runtime test data
-- Batch: WEBPAY-20260523
-- Total records: 50 parking sessions
--
-- Validity:
-- Philippine time: 2026-05-23 00:00:00 +08:00 to 2026-05-23 23:59:59 +08:00
-- UTC equivalent:  2026-05-22 16:00:00Z to 2026-05-23 15:59:59Z
--
-- Scenario distribution:
-- 25 fresh happy-path tickets
-- 10 resumable active-payment tickets
-- 10 orphan active-payment tickets with no provider session
-- 5 orphan active-payment tickets with provider session but missing checkout_url
--
-- Cleanup scope:
-- Only WEBPAY-20260523 records are removed/replaced.

BEGIN;

DO $$
DECLARE
    v_valid_from timestamptz := TIMESTAMPTZ '2026-05-22 16:00:00+00';
    v_valid_to   timestamptz := TIMESTAMPTZ '2026-05-23 15:59:59+00';

    v_service_identity_id uuid;
    v_site_group_id uuid;
    v_site_id uuid;
    v_vendor_system_id uuid;
    v_payment_rail_id uuid;

    v_ticket text;
    v_scenario text;
    v_seq int;
    v_session_id uuid;
    v_tariff_snapshot_id uuid;
    v_payment_attempt_id uuid;
    v_amount numeric(18,2);
BEGIN
    -------------------------------------------------------------------------
    -- Reference records
    -------------------------------------------------------------------------

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
        'WEBPAY_20260523_TEST_SEEDER',
        'WebPay 2026-05-23 Test Seeder',
        'INTERNAL_SERVICE',
        'ACTIVE',
        'ExitPass Dev Data',
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
        'WEBPAY_20260523_GROUP',
        'WebPay Test Site Group 2026-05-23',
        'Property',
        'Temporary WebPay runtime test site group for 2026-05-23 only.',
        'Pro Parking Group',
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
        'WEBPAY_20260523_SITE',
        'WebPay Test Site 2026-05-23',
        'Temporary WebPay runtime test site for 2026-05-23 only.',
        'MALL_PARKING',
        'Asia/Manila',
        'WebPay Test Address',
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
        'WEBPAY_20260523_MOCK_PMS',
        'WebPay 2026-05-23 Mock Vendor PMS',
        'VENDOR_PMS',
        'ACTIVE',
        'DEV',
        'mock://webpay-20260523',
        'v1',
        'ExitPass Dev',
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
        'PAYMONGO_QRPH_WEBPAY_20260523',
        'PayMongo QRPh WebPay Test Rail 2026-05-23',
        'PAYMONGO',
        'QRPH',
        'PHP',
        'ACTIVE',
        false,
        true,
        'PAYMONGO_TEST',
        'WEBPAY_20260523',
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

    -------------------------------------------------------------------------
    -- Local/dev WebPay QRPh routing
    -------------------------------------------------------------------------

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
      AND max_amount_minor_units IS NULL
      AND (
          primary_provider_code <> 'PAYMONGO'
          OR fallback_provider_code IS NOT NULL
          OR is_enabled IS DISTINCT FROM true
          OR primary_provider_enabled IS DISTINCT FROM true
          OR fallback_provider_enabled IS DISTINCT FROM false
      );

    UPDATE payments.payment_rails
    SET
        rail_status = 'ACTIVE',
        updated_at = now(),
        row_version = row_version + 1
    WHERE provider_code = 'PAYMONGO'
      AND rail_type = 'QRPH'
      AND rail_status IS DISTINCT FROM 'ACTIVE';

    -------------------------------------------------------------------------
    -- Cleanup existing batch data, child tables first.
    -------------------------------------------------------------------------

    DELETE FROM core.exit_authorizations ea
    USING core.parking_sessions ps
    WHERE ea.parking_session_id = ps.parking_session_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM core.payment_confirmations pc
    USING core.payment_attempts pa
    JOIN core.parking_sessions ps
      ON ps.parking_session_id = pa.parking_session_id
    WHERE pc.payment_attempt_id = pa.payment_attempt_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM payments.provider_outcomes po
    USING core.payment_attempts pa
    JOIN core.parking_sessions ps
      ON ps.parking_session_id = pa.parking_session_id
    WHERE po.payment_attempt_id = pa.payment_attempt_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM payments.provider_status_queries psq
    USING core.payment_attempts pa
    JOIN core.parking_sessions ps
      ON ps.parking_session_id = pa.parking_session_id
    WHERE psq.payment_attempt_id = pa.payment_attempt_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM payments.provider_callbacks pc
    USING core.payment_attempts pa
    JOIN core.parking_sessions ps
      ON ps.parking_session_id = pa.parking_session_id
    WHERE pc.payment_attempt_id = pa.payment_attempt_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM payments.provider_sessions prv
    USING core.payment_attempts pa
    JOIN core.parking_sessions ps
      ON ps.parking_session_id = pa.parking_session_id
    WHERE prv.payment_attempt_id = pa.payment_attempt_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM core.payment_attempts pa
    USING core.parking_sessions ps
    WHERE pa.parking_session_id = ps.parking_session_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM core.tariff_snapshots ts
    USING core.parking_sessions ps
    WHERE ts.parking_session_id = ps.parking_session_id
      AND ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    DELETE FROM sessions.session_identifier_indexes sii
    WHERE sii.identifier_masked LIKE 'WEBPAY-20260523%';

    DELETE FROM core.parking_sessions ps
    WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260523%';

    -------------------------------------------------------------------------
    -- Insert 50 parking sessions, tariff snapshots, payment attempts, and
    -- provider session states according to scenario.
    -------------------------------------------------------------------------

    FOR v_scenario, v_ticket, v_seq IN
        SELECT scenario, ticket_number, scenario_sequence
        FROM (
            SELECT
                'FRESH'::text AS scenario,
                'WEBPAY-20260523-FRESH-' || LPAD(n::text, 3, '0') AS ticket_number,
                n AS scenario_sequence
            FROM generate_series(1, 25) AS n

            UNION ALL

            SELECT
                'RESUME'::text AS scenario,
                'WEBPAY-20260523-RESUME-' || LPAD(n::text, 3, '0') AS ticket_number,
                n AS scenario_sequence
            FROM generate_series(1, 10) AS n

            UNION ALL

            SELECT
                'ORPHAN_NOSESSION'::text AS scenario,
                'WEBPAY-20260523-ORPHAN-NOSESSION-' || LPAD(n::text, 3, '0') AS ticket_number,
                n AS scenario_sequence
            FROM generate_series(1, 10) AS n

            UNION ALL

            SELECT
                'ORPHAN_NOURL'::text AS scenario,
                'WEBPAY-20260523-ORPHAN-NOURL-' || LPAD(n::text, 3, '0') AS ticket_number,
                n AS scenario_sequence
            FROM generate_series(1, 5) AS n
        ) s
        ORDER BY scenario, scenario_sequence
    LOOP
        v_amount := 100.00;

        INSERT INTO core.parking_sessions (
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
            v_site_group_id,
            v_site_id,
            v_vendor_system_id,
            v_ticket,
            encode(digest('PLATE-' || v_ticket, 'sha256'), 'hex'),
            'WEBPAY' || LPAD(v_seq::text, 3, '0'),
            encode(digest(v_ticket, 'sha256'), 'hex'),
            v_ticket,
            TIMESTAMPTZ '2026-05-23 02:00:00+08' + (v_seq || ' minutes')::interval,
            'ACTIVE',
            'ACTIVE',
            gen_random_uuid(),
            v_service_identity_id,
            v_service_identity_id
        )
        RETURNING parking_session_id
        INTO v_session_id;

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
        VALUES (
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
        );

        UPDATE core.tariff_snapshots
        SET snapshot_status = 'SUPERSEDED',
            updated_at = now()
        WHERE parking_session_id = v_session_id
          AND snapshot_status = 'ACTIVE';

        INSERT INTO core.tariff_snapshots (
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
            v_session_id,
            v_vendor_system_id,
            'TARIFF-' || v_ticket,
            'WEBPAY-20260523-TARIFF-v1',
            'PHP',
            v_amount,
            0.00,
            0.00,
            v_amount,
            'ACTIVE',
            TIMESTAMPTZ '2026-05-23 08:00:00+08',
            v_valid_to,
            gen_random_uuid(),
            v_service_identity_id,
            v_service_identity_id
        )
        RETURNING tariff_snapshot_id
        INTO v_tariff_snapshot_id;

        IF v_scenario IN ('RESUME', 'ORPHAN_NOSESSION', 'ORPHAN_NOURL') THEN
            INSERT INTO core.payment_attempts (
                parking_session_id,
                tariff_snapshot_id,
                idempotency_key,
                payment_rail_id,
                currency_code,
                amount,
                attempt_status,
                requested_at,
                expires_at,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                v_session_id,
                v_tariff_snapshot_id,
                'WEBPAY-20260523-' || v_scenario || '-' || LPAD(v_seq::text, 3, '0'),
                v_payment_rail_id,
                'PHP',
                v_amount,
                'REQUESTED',
                TIMESTAMPTZ '2026-05-23 08:10:00+08',
                v_valid_to,
                gen_random_uuid(),
                v_service_identity_id,
                v_service_identity_id
            )
            RETURNING payment_attempt_id
            INTO v_payment_attempt_id;

            IF v_scenario = 'RESUME' THEN
                INSERT INTO payments.provider_sessions (
                    payment_attempt_id,
                    payment_rail_id,
                    provider_session_ref,
                    provider_transaction_ref,
                    idempotency_key,
                    session_status,
                    currency_code,
                    amount,
                    checkout_url,
                    qr_payload,
                    expires_at,
                    provider_created_at,
                    provider_expires_at,
                    raw_provider_metadata_ref,
                    correlation_id,
                    created_by_service_identity_id,
                    updated_by_service_identity_id
                )
                VALUES (
                    v_payment_attempt_id,
                    v_payment_rail_id,
                    'pm_test_resume_' || LPAD(v_seq::text, 3, '0'),
                    'pm_txn_resume_' || LPAD(v_seq::text, 3, '0'),
                    'WEBPAY-20260523-PROVIDER-RESUME-' || LPAD(v_seq::text, 3, '0'),
                    'ACTIVE',
                    'PHP',
                    v_amount,
                    'https://checkout.test.paymongo.local/webpay-20260523/resume/' || LPAD(v_seq::text, 3, '0'),
                    'QRPH-TEST-PAYLOAD-WEBPAY-20260523-RESUME-' || LPAD(v_seq::text, 3, '0'),
                    v_valid_to,
                    TIMESTAMPTZ '2026-05-23 08:11:00+08',
                    v_valid_to,
                    'WEBPAY-20260523-SEEDED-RESUME',
                    gen_random_uuid(),
                    v_service_identity_id,
                    v_service_identity_id
                );
            ELSIF v_scenario = 'ORPHAN_NOURL' THEN
                INSERT INTO payments.provider_sessions (
                    payment_attempt_id,
                    payment_rail_id,
                    provider_session_ref,
                    provider_transaction_ref,
                    idempotency_key,
                    session_status,
                    currency_code,
                    amount,
                    checkout_url,
                    qr_payload,
                    expires_at,
                    provider_created_at,
                    provider_expires_at,
                    raw_provider_metadata_ref,
                    correlation_id,
                    created_by_service_identity_id,
                    updated_by_service_identity_id
                )
                VALUES (
                    v_payment_attempt_id,
                    v_payment_rail_id,
                    'pm_test_orphan_nourl_' || LPAD(v_seq::text, 3, '0'),
                    'pm_txn_orphan_nourl_' || LPAD(v_seq::text, 3, '0'),
                    'WEBPAY-20260523-PROVIDER-ORPHAN-NOURL-' || LPAD(v_seq::text, 3, '0'),
                    'ACTIVE',
                    'PHP',
                    v_amount,
                    NULL,
                    NULL,
                    v_valid_to,
                    TIMESTAMPTZ '2026-05-23 08:11:00+08',
                    v_valid_to,
                    'WEBPAY-20260523-SEEDED-ORPHAN-NOURL',
                    gen_random_uuid(),
                    v_service_identity_id,
                    v_service_identity_id
                );
            END IF;
        END IF;
    END LOOP;

    RAISE NOTICE 'WEBPAY-20260523 seed completed.';
    RAISE NOTICE 'site_group_id=%', v_site_group_id;
    RAISE NOTICE 'site_id=%', v_site_id;
    RAISE NOTICE 'vendor_system_id=%', v_vendor_system_id;
    RAISE NOTICE 'payment_rail_id=%', v_payment_rail_id;
    RAISE NOTICE 'service_identity_id=%', v_service_identity_id;
END $$;

COMMIT;

SELECT
    CASE
        WHEN ps.vendor_session_ref LIKE 'WEBPAY-20260523-FRESH-%' THEN 'FRESH'
        WHEN ps.vendor_session_ref LIKE 'WEBPAY-20260523-RESUME-%' THEN 'RESUME'
        WHEN ps.vendor_session_ref LIKE 'WEBPAY-20260523-ORPHAN-NOSESSION-%' THEN 'ORPHAN_NOSESSION'
        WHEN ps.vendor_session_ref LIKE 'WEBPAY-20260523-ORPHAN-NOURL-%' THEN 'ORPHAN_NOURL'
        ELSE 'UNKNOWN'
    END AS scenario,
    COUNT(*) AS parking_session_count
FROM core.parking_sessions ps
WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260523%'
GROUP BY 1
ORDER BY 1;

SELECT parking_session_id, COUNT(*)
FROM core.tariff_snapshots
WHERE snapshot_status = 'ACTIVE'
  AND parking_session_id IN (
      SELECT parking_session_id
      FROM core.parking_sessions
      WHERE vendor_session_ref LIKE 'WEBPAY-20260523%'
  )
GROUP BY parking_session_id
HAVING COUNT(*) > 1;

SELECT
    COUNT(*) AS active_tariff_snapshots_outside_may_23_utc
FROM core.tariff_snapshots ts
JOIN core.parking_sessions ps
  ON ps.parking_session_id = ts.parking_session_id
WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260523%'
  AND ts.snapshot_status = 'ACTIVE'
  AND (
      ts.expires_at < TIMESTAMPTZ '2026-05-22 16:00:00+00'
      OR ts.expires_at > TIMESTAMPTZ '2026-05-23 15:59:59+00'
  );

SELECT
    payment_method_code,
    currency_code,
    primary_provider_code,
    fallback_provider_code,
    is_enabled,
    primary_provider_enabled,
    fallback_provider_enabled
FROM payments.payment_provider_routing_policies
WHERE payment_method_code = 'QRPH'
  AND currency_code = 'PHP';

SELECT
    provider_code,
    rail_type,
    rail_status
FROM payments.payment_rails
WHERE provider_code = 'PAYMONGO'
  AND rail_type = 'QRPH';

SELECT
    COUNT(*) AS webpay_20260519_active_tariffs_outside_may_19_utc
FROM core.tariff_snapshots ts
JOIN core.parking_sessions ps
  ON ps.parking_session_id = ts.parking_session_id
WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260519%'
  AND ts.snapshot_status = 'ACTIVE'
  AND (
      ts.expires_at < TIMESTAMPTZ '2026-05-18 16:00:00+00'
      OR ts.expires_at > TIMESTAMPTZ '2026-05-19 15:59:59+00'
  );
