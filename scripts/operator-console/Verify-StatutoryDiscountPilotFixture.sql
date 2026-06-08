-- Read-only verification for ExitPass #235A sandbox-only fixture.
-- This script does not create, update, or delete data.

WITH fixture AS (
    SELECT
        '23100000-0000-0000-0000-000000000003'::uuid AS parking_session_id,
        '23100000-0000-0000-0000-000000000004'::uuid AS original_tariff_snapshot_id,
        '23100000-0000-0000-0000-000000000002'::uuid AS policy_id,
        '77000000-0000-0000-0000-000000000001'::uuid AS site_group_id,
        '77000000-0000-0000-0000-000000000002'::uuid AS site_id,
        '77000000-0000-0000-0000-000000000010'::uuid AS operator_user_id,
        '77000000-0000-0000-0000-000000000020'::uuid AS hr_identity_mapping_id,
        '77000000-0000-0000-0000-000000000030'::uuid AS operator_device_binding_id,
        '77000000-0000-0000-0000-000000000040'::uuid AS operator_device_assignment_history_id,
        '77000000-0000-0000-0000-000000000050'::uuid AS operator_shift_id,
        'E2E-231-SESSION-001'::text AS ticket_reference
),
operator_console_counts AS (
    SELECT
        CASE
            WHEN to_regclass('operator_console.hr_identity_mappings') IS NULL THEN NULL
            ELSE COALESCE(((xpath('/row/c/text()', query_to_xml(
                'SELECT COUNT(*) AS c FROM operator_console.hr_identity_mappings WHERE hr_identity_mapping_id = ''77000000-0000-0000-0000-000000000020'' AND user_id = ''77000000-0000-0000-0000-000000000010'' AND mapping_status::text = ''ACTIVE''',
                false,
                true,
                ''
            )))[1])::text::integer, 0)
        END AS active_hr_mapping_count,
        CASE
            WHEN to_regclass('operator_console.operator_device_bindings') IS NULL THEN NULL
            ELSE COALESCE(((xpath('/row/c/text()', query_to_xml(
                'SELECT COUNT(*) AS c FROM operator_console.operator_device_bindings WHERE operator_device_binding_id = ''77000000-0000-0000-0000-000000000030'' AND site_group_id = ''77000000-0000-0000-0000-000000000001'' AND site_id = ''77000000-0000-0000-0000-000000000002'' AND device_status::text = ''ACTIVE''',
                false,
                true,
                ''
            )))[1])::text::integer, 0)
        END AS active_device_binding_count,
        CASE
            WHEN to_regclass('operator_console.operator_device_assignment_history') IS NULL THEN NULL
            ELSE COALESCE(((xpath('/row/c/text()', query_to_xml(
                'SELECT COUNT(*) AS c FROM operator_console.operator_device_assignment_history WHERE operator_device_assignment_history_id = ''77000000-0000-0000-0000-000000000040'' AND operator_device_binding_id = ''77000000-0000-0000-0000-000000000030'' AND site_group_id = ''77000000-0000-0000-0000-000000000001'' AND site_id = ''77000000-0000-0000-0000-000000000002'' AND assignment_status_code = ''ACTIVE'' AND now() >= effective_from AND (effective_to IS NULL OR now() < effective_to)',
                false,
                true,
                ''
            )))[1])::text::integer, 0)
        END AS active_device_assignment_count,
        CASE
            WHEN to_regclass('operator_console.operator_shifts') IS NULL THEN NULL
            ELSE COALESCE(((xpath('/row/c/text()', query_to_xml(
                'SELECT COUNT(*) AS c FROM operator_console.operator_shifts WHERE operator_shift_id = ''77000000-0000-0000-0000-000000000050'' AND operator_user_id = ''77000000-0000-0000-0000-000000000010'' AND site_group_id = ''77000000-0000-0000-0000-000000000001'' AND site_id = ''77000000-0000-0000-0000-000000000002'' AND operational_status::text = ''ACTIVE'' AND active_from <= now() AND (active_to IS NULL OR now() < active_to)',
                false,
                true,
                ''
            )))[1])::text::integer, 0)
        END AS active_shift_count
),
boundary_counts AS (
    SELECT
        (SELECT COUNT(*) FROM core.payment_attempts pa, fixture f WHERE pa.parking_session_id = f.parking_session_id) AS payment_attempt_count,
        (SELECT COUNT(*)
           FROM core.payment_confirmations pc
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
           CROSS JOIN fixture f
          WHERE pa.parking_session_id = f.parking_session_id) AS payment_confirmation_count,
        (SELECT COUNT(*)
           FROM payments.provider_sessions ps
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = ps.payment_attempt_id
           CROSS JOIN fixture f
          WHERE pa.parking_session_id = f.parking_session_id) AS provider_session_count,
        (SELECT COUNT(*)
           FROM payments.provider_outcomes po
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
           CROSS JOIN fixture f
          WHERE pa.parking_session_id = f.parking_session_id) AS provider_outcome_count,
        (SELECT COUNT(*) FROM core.exit_authorizations ea, fixture f WHERE ea.parking_session_id = f.parking_session_id) AS exit_authorization_count,
        (SELECT COUNT(*)
           FROM gates.gate_authorization_consumptions gac
           JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
           CROSS JOIN fixture f
          WHERE ea.parking_session_id = f.parking_session_id) AS gate_consumption_count,
        (SELECT COUNT(*)
           FROM gates.gate_events ge
           LEFT JOIN core.exit_authorizations ea ON ea.exit_authorization_id = ge.exit_authorization_id
           LEFT JOIN gates.gate_authorization_consumptions gac ON gac.gate_authorization_consumption_id = ge.gate_authorization_consumption_id
           LEFT JOIN core.exit_authorizations gcea ON gcea.exit_authorization_id = gac.exit_authorization_id
           CROSS JOIN fixture f
          WHERE ea.parking_session_id = f.parking_session_id
             OR gcea.parking_session_id = f.parking_session_id) AS gate_event_count,
        (SELECT COUNT(*) FROM coupons.coupon_applications ca, fixture f WHERE ca.parking_session_id = f.parking_session_id) AS coupon_application_count,
        (SELECT COUNT(*) FROM reconciliation.mops_transaction_records mr, fixture f WHERE mr.parking_session_id = f.parking_session_id) AS mops_transaction_count,
        (SELECT COUNT(*)
           FROM reconciliation.reconciliation_items ri
           JOIN core.payment_attempts pa ON pa.payment_attempt_id = ri.payment_attempt_id
           CROSS JOIN fixture f
          WHERE pa.parking_session_id = f.parking_session_id) AS reconciliation_item_count
)
SELECT
    timezone('Asia/Manila', now()) AS verified_at_ph,
    ps.parking_session_id IS NOT NULL
        AND ps.session_status::text = 'ACTIVE'
        AND ps.ticket_number_masked = f.ticket_reference AS active_parking_session_exists,
    ts.tariff_snapshot_id IS NOT NULL
        AND ts.snapshot_status::text = 'ACTIVE'
        AND ts.gross_amount = 125.00
        AND ts.net_amount = 125.00
        AND ts.currency_code = 'PHP' AS active_original_tariff_snapshot_exists,
    sg.site_group_id IS NOT NULL
        AND sg.site_group_status::text = 'ACTIVE'
        AND s.site_id IS NOT NULL
        AND s.site_status::text = 'ACTIVE'
        AND s.lgu_code = 'PH-INT-E2E-231' AS site_and_site_group_exist,
    u.user_id IS NOT NULL
        AND u.user_status::text = 'ACTIVE'
        AND u.user_type::text = 'SITE_OPERATOR' AS operator_user_exists,
    CASE
        WHEN to_regnamespace('operator_console') IS NULL THEN 'NOT_INSTALLED'
        WHEN occ.active_hr_mapping_count IS NULL
          OR occ.active_device_binding_count IS NULL
          OR occ.active_device_assignment_count IS NULL
          OR occ.active_shift_count IS NULL THEN 'PARTIAL_SCHEMA'
        WHEN occ.active_hr_mapping_count = 1
          AND occ.active_device_binding_count = 1
          AND occ.active_device_assignment_count = 1
          AND occ.active_shift_count = 1 THEN 'EXISTS'
        ELSE 'MISSING'
    END AS operator_access_context_status,
    p.discount_policy_reference_id IS NOT NULL
        AND p.policy_status::text = 'ACTIVE'
        AND p.entitlement_type::text = 'SENIOR_CITIZEN'
        AND p.requires_operator_validation = true
        AND p.requires_evidence_capture = true
        AND p.site_group_id = f.site_group_id
        AND p.site_id = f.site_id
        AND p.lgu_code = s.lgu_code AS active_required_evidence_policy_exists,
    bc.payment_attempt_count,
    bc.payment_confirmation_count,
    bc.provider_session_count,
    bc.provider_outcome_count,
    bc.exit_authorization_count,
    bc.gate_consumption_count,
    bc.gate_event_count,
    bc.coupon_application_count,
    bc.mops_transaction_count,
    bc.reconciliation_item_count,
    (
        bc.payment_attempt_count
      + bc.payment_confirmation_count
      + bc.provider_session_count
      + bc.provider_outcome_count
      + bc.exit_authorization_count
      + bc.gate_consumption_count
      + bc.gate_event_count
      + bc.coupon_application_count
      + bc.mops_transaction_count
      + bc.reconciliation_item_count
    ) = 0 AS no_payment_provider_gate_coupon_reconciliation_rows,
    f.ticket_reference,
    f.parking_session_id,
    f.original_tariff_snapshot_id,
    f.policy_id,
    f.site_id,
    f.site_group_id,
    f.operator_user_id,
    f.operator_device_binding_id,
    f.operator_shift_id,
    'SENIOR_CITIZEN' AS entitlement_type,
    'SENIOR_CITIZEN_ID' AS required_evidence_type,
    'OPERATOR_CONFIRMED' AS capture_method
FROM fixture f
LEFT JOIN core.parking_sessions ps ON ps.parking_session_id = f.parking_session_id
LEFT JOIN core.tariff_snapshots ts ON ts.tariff_snapshot_id = f.original_tariff_snapshot_id
LEFT JOIN sites.site_groups sg ON sg.site_group_id = f.site_group_id
LEFT JOIN sites.sites s ON s.site_id = f.site_id
LEFT JOIN identity.users u ON u.user_id = f.operator_user_id
LEFT JOIN discounts.discount_policy_references p ON p.discount_policy_reference_id = f.policy_id
CROSS JOIN operator_console_counts occ
CROSS JOIN boundary_counts bc;
