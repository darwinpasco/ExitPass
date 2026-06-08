-- ExitPass #235A sandbox-only Operator Console statutory discount pilot fixture.
-- Not production seed data. Do not include in baseline DDL or migrations.
-- Does not configure or invoke payment providers, AUB, WebPay, coupons,
-- reconciliation, HikCentral, exit authorization, or gate behavior.

BEGIN;
SET CONSTRAINTS ALL DEFERRED;

DO $$
DECLARE
    v_boundary_count integer;
BEGIN
    SELECT
        (SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*)
           FROM core.payment_confirmations pc
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
          WHERE pa.parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*)
           FROM payments.provider_sessions ps
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = ps.payment_attempt_id
          WHERE pa.parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*)
           FROM payments.provider_outcomes po
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
          WHERE pa.parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*) FROM core.exit_authorizations WHERE parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*)
           FROM gates.gate_authorization_consumptions gac
           JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
          WHERE ea.parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*)
           FROM gates.gate_events ge
           LEFT JOIN core.exit_authorizations ea ON ea.exit_authorization_id = ge.exit_authorization_id
           LEFT JOIN gates.gate_authorization_consumptions gac ON gac.gate_authorization_consumption_id = ge.gate_authorization_consumption_id
           LEFT JOIN core.exit_authorizations gcea ON gcea.exit_authorization_id = gac.exit_authorization_id
          WHERE ea.parking_session_id = '23100000-0000-0000-0000-000000000003'
             OR gcea.parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*) FROM coupons.coupon_applications WHERE parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*) FROM reconciliation.mops_transaction_records WHERE parking_session_id = '23100000-0000-0000-0000-000000000003')
      + (SELECT COUNT(*)
           FROM reconciliation.reconciliation_items ri
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = ri.payment_attempt_id
          WHERE pa.parking_session_id = '23100000-0000-0000-0000-000000000003')
    INTO v_boundary_count;

    IF v_boundary_count > 0 THEN
        RAISE EXCEPTION
            'Refusing to reset #235A fixture because payment/provider/gate/coupon/reconciliation rows already exist for parking_session_id=%',
            '23100000-0000-0000-0000-000000000003';
    END IF;
END $$;

UPDATE core.tariff_snapshots
   SET statutory_discount_validation_id = NULL,
       superseded_by_tariff_snapshot_id = NULL,
       updated_at = now()
 WHERE parking_session_id = '23100000-0000-0000-0000-000000000003'
    OR tariff_snapshot_id = '23100000-0000-0000-0000-000000000004';

DELETE FROM discounts.discount_evidence_references
 WHERE statutory_discount_validation_id IN (
    SELECT statutory_discount_validation_id
    FROM discounts.statutory_discount_validations
    WHERE parking_session_id = '23100000-0000-0000-0000-000000000003'
       OR requested_by_user_id = '77000000-0000-0000-0000-000000000010'
 );

DELETE FROM discounts.statutory_discount_validations
 WHERE parking_session_id = '23100000-0000-0000-0000-000000000003'
    OR requested_by_user_id = '77000000-0000-0000-0000-000000000010';

DELETE FROM core.tariff_snapshots
 WHERE parking_session_id = '23100000-0000-0000-0000-000000000003'
    OR tariff_snapshot_id = '23100000-0000-0000-0000-000000000004';

DELETE FROM core.parking_sessions
 WHERE parking_session_id = '23100000-0000-0000-0000-000000000003';

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
    'SANDBOX_OPERATOR_CONSOLE_FIXTURE_SERVICE',
    'Sandbox Operator Console Fixture Service',
    'INTERNAL_SERVICE',
    'ACTIVE',
    'Central PMS Sandbox Fixtures',
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
    'SANDBOX_OC_SD_PILOT_GROUP',
    'Sandbox Operator Console Statutory Discount Pilot Group',
    'Sandbox OC Pilot',
    'Sandbox-only fixture for Operator Console statutory discount validation.',
    'Sandbox Operator',
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
    lgu_code,
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
    'SANDBOX_OC_SD_PILOT_SITE',
    'Sandbox Operator Console Statutory Discount Pilot Site',
    'Sandbox-only fixture site for Operator Console statutory discount validation.',
    'OTHER',
    'Asia/Manila',
    'Pasig',
    'Metro Manila',
    'PH',
    'PH-INT-E2E-231',
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
    lgu_code = EXCLUDED.lgu_code,
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
    'SANDBOX_OC_SD_PILOT_VENDOR_PMS',
    'Sandbox Operator Console Statutory Discount Pilot Vendor PMS',
    'VENDOR_PMS',
    'ACTIVE',
    'LOCAL',
    'Central PMS Sandbox Fixtures',
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
VALUES (
    '77000000-0000-0000-0000-000000000010',
    'sandbox-oc-sd-pilot-operator',
    'sandbox-oc-sd-pilot-operator@example.test',
    'SANDBOX-OC-SD-PILOT-OPERATOR@EXAMPLE.TEST',
    'Sandbox OC Statutory Discount Operator',
    'SITE_OPERATOR',
    'ACTIVE',
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

INSERT INTO discounts.discount_policy_references (
    discount_policy_reference_id,
    policy_code,
    policy_name,
    policy_description,
    policy_type,
    policy_level,
    entitlement_type,
    local_ordinance_reference,
    lgu_code,
    jurisdiction_name,
    site_group_id,
    site_id,
    precedence_rank,
    policy_version,
    requires_operator_validation,
    requires_evidence_capture,
    evidence_retention_policy_code,
    effective_from,
    effective_to,
    policy_status,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '23100000-0000-0000-0000-000000000002',
    'SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A',
    'Sandbox Operator Console Senior Citizen Required Evidence Policy',
    'Sandbox-only policy requiring metadata-only operator evidence for senior citizen statutory discount validation.',
    'SITE_POLICY',
    'SITE_POLICY',
    'SENIOR_CITIZEN',
    'SANDBOX-OC-SD-ORD-235A',
    'PH-INT-E2E-231',
    'Sandbox LGU',
    '77000000-0000-0000-0000-000000000001',
    '77000000-0000-0000-0000-000000000002',
    0,
    'sandbox-235a-v1',
    true,
    true,
    'SANDBOX_METADATA_ONLY',
    now() - interval '1 day',
    now() + interval '365 days',
    'ACTIVE',
    '77000000-0000-0000-0000-000000000003',
    '77000000-0000-0000-0000-000000000003'
)
ON CONFLICT (discount_policy_reference_id) DO UPDATE
SET policy_code = EXCLUDED.policy_code,
    policy_name = EXCLUDED.policy_name,
    policy_description = EXCLUDED.policy_description,
    policy_type = EXCLUDED.policy_type,
    policy_level = EXCLUDED.policy_level,
    entitlement_type = EXCLUDED.entitlement_type,
    local_ordinance_reference = EXCLUDED.local_ordinance_reference,
    lgu_code = EXCLUDED.lgu_code,
    jurisdiction_name = EXCLUDED.jurisdiction_name,
    site_group_id = EXCLUDED.site_group_id,
    site_id = EXCLUDED.site_id,
    precedence_rank = EXCLUDED.precedence_rank,
    policy_version = EXCLUDED.policy_version,
    requires_operator_validation = EXCLUDED.requires_operator_validation,
    requires_evidence_capture = EXCLUDED.requires_evidence_capture,
    evidence_retention_policy_code = EXCLUDED.evidence_retention_policy_code,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    policy_status = EXCLUDED.policy_status,
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
    '23100000-0000-0000-0000-000000000003',
    '77000000-0000-0000-0000-000000000001',
    '77000000-0000-0000-0000-000000000002',
    '77000000-0000-0000-0000-000000000004',
    'E2E-231-SESSION-001',
    '2312312312312312312312312312312312312312312312312312312312312312',
    'SANDBOX-231',
    'd6f5f9ecab9492c63d3dd2795db3f74d14fd2f071b7fc27a9c9d8fa6d341f199',
    'E2E-231-SESSION-001',
    now() - interval '2 hours',
    'ACTIVE',
    'ACTIVE',
    gen_random_uuid(),
    '77000000-0000-0000-0000-000000000003',
    '77000000-0000-0000-0000-000000000003'
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
    '23100000-0000-0000-0000-000000000004',
    '23100000-0000-0000-0000-000000000003',
    '77000000-0000-0000-0000-000000000004',
    'SANDBOX-OC-SD-235A-ORIGINAL',
    'SANDBOX-235A-V1',
    'PHP',
    125.00,
    0,
    0,
    125.00,
    'ACTIVE',
    now(),
    now() + interval '4 hours',
    gen_random_uuid(),
    '77000000-0000-0000-0000-000000000003',
    '77000000-0000-0000-0000-000000000003'
);

DO $$
BEGIN
    IF to_regclass('operator_console.hr_identity_mappings') IS NOT NULL THEN
        EXECUTE $sql$
            INSERT INTO operator_console.hr_identity_mappings (
                hr_identity_mapping_id,
                user_id,
                hr_provider_code,
                external_person_id_hash,
                external_person_id_masked,
                mapping_status,
                effective_from,
                effective_to,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000020',
                '77000000-0000-0000-0000-000000000010',
                'SANDBOX_HR',
                '7700000000000000000000000000002077000000000000000000000000000020',
                'EMP-SANDBOX',
                'ACTIVE',
                '2020-01-01T00:00:00Z',
                '2035-01-01T00:00:00Z',
                gen_random_uuid(),
                '77000000-0000-0000-0000-000000000010',
                '77000000-0000-0000-0000-000000000010'
            )
            ON CONFLICT (hr_identity_mapping_id) DO UPDATE
            SET user_id = EXCLUDED.user_id,
                hr_provider_code = EXCLUDED.hr_provider_code,
                external_person_id_hash = EXCLUDED.external_person_id_hash,
                external_person_id_masked = EXCLUDED.external_person_id_masked,
                mapping_status = EXCLUDED.mapping_status,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_by_user_id = EXCLUDED.updated_by_user_id,
                updated_at = now();
        $sql$;
    END IF;

    IF to_regclass('operator_console.operator_device_bindings') IS NOT NULL THEN
        EXECUTE $sql$
            INSERT INTO operator_console.operator_device_bindings (
                operator_device_binding_id,
                device_binding_code,
                device_name,
                site_group_id,
                site_id,
                browser_key_thumbprint,
                device_status,
                trust_level,
                binding_source,
                last_seen_at,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000030',
                'SANDBOX-OC-SD-235A-DEVICE',
                'Sandbox OC Statutory Discount Device',
                '77000000-0000-0000-0000-000000000001',
                '77000000-0000-0000-0000-000000000002',
                '7700000000000000000000000000003077000000000000000000000000000030',
                'ACTIVE',
                'BROWSER_KEY_AND_MTLS',
                'SANDBOX_FIXTURE',
                now(),
                gen_random_uuid(),
                '77000000-0000-0000-0000-000000000010',
                '77000000-0000-0000-0000-000000000010'
            )
            ON CONFLICT (operator_device_binding_id) DO UPDATE
            SET device_binding_code = EXCLUDED.device_binding_code,
                device_name = EXCLUDED.device_name,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                browser_key_thumbprint = EXCLUDED.browser_key_thumbprint,
                device_status = EXCLUDED.device_status,
                trust_level = EXCLUDED.trust_level,
                binding_source = EXCLUDED.binding_source,
                last_seen_at = EXCLUDED.last_seen_at,
                updated_by_user_id = EXCLUDED.updated_by_user_id,
                updated_at = now();
        $sql$;
    END IF;

    IF to_regclass('operator_console.operator_device_assignment_history') IS NOT NULL THEN
        EXECUTE $sql$
            INSERT INTO operator_console.operator_device_assignment_history (
                operator_device_assignment_history_id,
                operator_device_binding_id,
                site_group_id,
                site_id,
                assignment_status_code,
                assignment_source_code,
                assignment_reason_code,
                assigned_at,
                assigned_by_user_id,
                effective_from,
                effective_to,
                correlation_id,
                created_by_user_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000040',
                '77000000-0000-0000-0000-000000000030',
                '77000000-0000-0000-0000-000000000001',
                '77000000-0000-0000-0000-000000000002',
                'ACTIVE',
                'SANDBOX_FIXTURE',
                'OPERATOR_CONSOLE_STATUTORY_DISCOUNT_PILOT',
                now(),
                '77000000-0000-0000-0000-000000000010',
                '2020-01-01T00:00:00Z',
                '2035-01-01T00:00:00Z',
                gen_random_uuid(),
                '77000000-0000-0000-0000-000000000010'
            )
            ON CONFLICT (operator_device_assignment_history_id) DO UPDATE
            SET operator_device_binding_id = EXCLUDED.operator_device_binding_id,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                assignment_status_code = EXCLUDED.assignment_status_code,
                assignment_source_code = EXCLUDED.assignment_source_code,
                assignment_reason_code = EXCLUDED.assignment_reason_code,
                assigned_at = EXCLUDED.assigned_at,
                assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to;
        $sql$;
    END IF;

    IF to_regclass('operator_console.operator_shifts') IS NOT NULL THEN
        EXECUTE $sql$
            INSERT INTO operator_console.operator_shifts (
                operator_shift_id,
                hr_provider_code,
                external_shift_id_hash,
                external_shift_id_masked,
                hr_identity_mapping_id,
                operator_user_id,
                site_group_id,
                site_id,
                scheduled_start_at,
                scheduled_end_at,
                source_imported_at,
                import_status_code,
                source_system_code,
                source_status_code,
                operational_status,
                active_from,
                active_to,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                '77000000-0000-0000-0000-000000000050',
                'SANDBOX_HR',
                '7700000000000000000000000000005077000000000000000000000000000050',
                'SHIFT-SANDBOX',
                '77000000-0000-0000-0000-000000000020',
                '77000000-0000-0000-0000-000000000010',
                '77000000-0000-0000-0000-000000000001',
                '77000000-0000-0000-0000-000000000002',
                now() - interval '1 hour',
                now() + interval '8 hours',
                now(),
                'IMPORTED',
                'SANDBOX_FIXTURE',
                'ACTIVE',
                'ACTIVE',
                now() - interval '1 hour',
                now() + interval '8 hours',
                gen_random_uuid(),
                '77000000-0000-0000-0000-000000000010',
                '77000000-0000-0000-0000-000000000010'
            )
            ON CONFLICT (operator_shift_id) DO UPDATE
            SET hr_provider_code = EXCLUDED.hr_provider_code,
                external_shift_id_hash = EXCLUDED.external_shift_id_hash,
                external_shift_id_masked = EXCLUDED.external_shift_id_masked,
                hr_identity_mapping_id = EXCLUDED.hr_identity_mapping_id,
                operator_user_id = EXCLUDED.operator_user_id,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                scheduled_start_at = EXCLUDED.scheduled_start_at,
                scheduled_end_at = EXCLUDED.scheduled_end_at,
                source_imported_at = EXCLUDED.source_imported_at,
                import_status_code = EXCLUDED.import_status_code,
                source_system_code = EXCLUDED.source_system_code,
                source_status_code = EXCLUDED.source_status_code,
                operational_status = EXCLUDED.operational_status,
                active_from = EXCLUDED.active_from,
                active_to = EXCLUDED.active_to,
                updated_by_user_id = EXCLUDED.updated_by_user_id,
                updated_at = now();
        $sql$;
    END IF;
END $$;

COMMIT;

SELECT
    timezone('Asia/Manila', now()) AS validation_date_time_ph,
    'local-sandbox' AS environment,
    'Sandbox Operator Console Statutory Discount Pilot Site' AS pilot_site,
    '77000000-0000-0000-0000-000000000002'::uuid AS site_id,
    '77000000-0000-0000-0000-000000000001'::uuid AS site_group_id,
    '77000000-0000-0000-0000-000000000010'::uuid AS operator_user_id,
    '77000000-0000-0000-0000-000000000030'::uuid AS operator_device_binding_id,
    '77000000-0000-0000-0000-000000000050'::uuid AS operator_shift_id,
    'E2E-231-SESSION-001' AS ticket_reference,
    '23100000-0000-0000-0000-000000000003'::uuid AS parking_session_id,
    '23100000-0000-0000-0000-000000000004'::uuid AS original_tariff_snapshot_id,
    'SENIOR_CITIZEN' AS entitlement_type,
    'SENIOR_CITIZEN_ID' AS required_evidence_type,
    'OPERATOR_CONFIRMED' AS capture_method,
    gen_random_uuid() AS correlation_id;
