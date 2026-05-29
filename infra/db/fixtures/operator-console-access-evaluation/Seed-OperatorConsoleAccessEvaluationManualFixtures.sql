BEGIN;

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
    effective_to
)
VALUES (
    '77000000-0000-0000-0000-000000000001',
    'MANUAL_TEST_OPERATOR_ACCESS_GROUP',
    'Manual Test Operator Access Site Group',
    'Manual Test Operator Access',
    'Local fixture site group for Operator Console access evaluation API smoke tests.',
    'Manual Test Operator Access',
    'Asia/Manila',
    'PHP',
    'ACTIVE',
    false,
    false,
    '2020-01-01T00:00:00Z',
    '2035-01-01T00:00:00Z'
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
    effective_to
)
VALUES (
    '77000000-0000-0000-0000-000000000002',
    '77000000-0000-0000-0000-000000000001',
    'MANUAL_TEST_OPERATOR_ACCESS_SITE',
    'Manual Test Operator Access Site',
    'Local fixture site for Operator Console access evaluation API smoke tests.',
    'OTHER',
    'Asia/Manila',
    'Pasig',
    'Metro Manila',
    'PH',
    'ACTIVE',
    false,
    false,
    '2020-01-01T00:00:00Z',
    '2035-01-01T00:00:00Z'
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
    updated_at = now();

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
    effective_to
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
        '2035-01-01T00:00:00Z'
    ),
    (
        '77000000-0000-0000-0000-000000000011',
        'manual-test-operator-access-inactive-hr',
        'manual-test-operator-access-inactive-hr@example.test',
        'MANUAL-TEST-OPERATOR-ACCESS-INACTIVE-HR@EXAMPLE.TEST',
        'Manual Test Operator Access Inactive HR',
        'SITE_OPERATOR',
        'ACTIVE',
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z'
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
    updated_at = now();

INSERT INTO operator_console.hr_identity_mappings (
    hr_identity_mapping_id,
    user_id,
    hr_provider_code,
    external_person_id_hash,
    external_person_id_masked,
    mapping_status,
    effective_from,
    effective_to,
    revoked_at,
    revocation_reason_code,
    correlation_id
)
VALUES
    (
        '77000000-0000-0000-0000-000000000020',
        '77000000-0000-0000-0000-000000000010',
        'MANUAL_TEST_OPERATOR_ACCESS',
        '7700000000000000000000000000002077000000000000000000000000000020',
        'MTOA-ALLOWED',
        'ACTIVE',
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        null,
        '77000000-0000-0000-0000-0000000000c1'
    ),
    (
        '77000000-0000-0000-0000-000000000021',
        '77000000-0000-0000-0000-000000000011',
        'MANUAL_TEST_OPERATOR_ACCESS',
        '7700000000000000000000000000002177000000000000000000000000000021',
        'MTOA-INACTIVE-HR',
        'SUSPENDED',
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        'MANUAL_TEST_OPERATOR_ACCESS_INACTIVE_HR',
        '77000000-0000-0000-0000-0000000000c2'
    )
ON CONFLICT (hr_identity_mapping_id) DO UPDATE
SET user_id = EXCLUDED.user_id,
    hr_provider_code = EXCLUDED.hr_provider_code,
    external_person_id_hash = EXCLUDED.external_person_id_hash,
    external_person_id_masked = EXCLUDED.external_person_id_masked,
    mapping_status = EXCLUDED.mapping_status,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    revoked_at = EXCLUDED.revoked_at,
    revocation_reason_code = EXCLUDED.revocation_reason_code,
    correlation_id = EXCLUDED.correlation_id,
    updated_at = now();

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
    revoked_at,
    revocation_reason_code,
    correlation_id
)
VALUES
    (
        '77000000-0000-0000-0000-000000000030',
        'MANUAL_TEST_OPERATOR_ACCESS_ALLOWED_DEVICE',
        'Manual Test Operator Access Allowed Device',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        '7700000000000000000000000000003077000000000000000000000000000030',
        'ACTIVE',
        'BROWSER_KEY_AND_MTLS',
        'MANUAL_TEST_OPERATOR_ACCESS',
        now(),
        null,
        null,
        '77000000-0000-0000-0000-0000000000c3'
    ),
    (
        '77000000-0000-0000-0000-000000000031',
        'MANUAL_TEST_OPERATOR_ACCESS_INACTIVE_DEVICE',
        'Manual Test Operator Access Inactive Device',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        '7700000000000000000000000000003177000000000000000000000000000031',
        'SUSPENDED',
        'BROWSER_KEY_AND_MTLS',
        'MANUAL_TEST_OPERATOR_ACCESS',
        now(),
        null,
        'MANUAL_TEST_OPERATOR_ACCESS_INACTIVE_DEVICE',
        '77000000-0000-0000-0000-0000000000c4'
    ),
    (
        '77000000-0000-0000-0000-000000000032',
        'MANUAL_TEST_OPERATOR_ACCESS_UNTRUSTED_DEVICE',
        'Manual Test Operator Access Untrusted Device',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        '7700000000000000000000000000003277000000000000000000000000000032',
        'ACTIVE',
        'UNVERIFIED',
        'MANUAL_TEST_OPERATOR_ACCESS',
        now(),
        null,
        null,
        '77000000-0000-0000-0000-0000000000c5'
    ),
    (
        '77000000-0000-0000-0000-000000000033',
        'MANUAL_TEST_OPERATOR_ACCESS_INVALID_ASSIGNMENT_DEVICE',
        'Manual Test Operator Access Invalid Assignment Device',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        '7700000000000000000000000000003377000000000000000000000000000033',
        'ACTIVE',
        'BROWSER_KEY_AND_MTLS',
        'MANUAL_TEST_OPERATOR_ACCESS',
        now(),
        null,
        null,
        '77000000-0000-0000-0000-0000000000c6'
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
    revoked_at = EXCLUDED.revoked_at,
    revocation_reason_code = EXCLUDED.revocation_reason_code,
    correlation_id = EXCLUDED.correlation_id,
    updated_at = now();

INSERT INTO operator_console.operator_device_assignment_history (
    operator_device_assignment_history_id,
    operator_device_binding_id,
    site_group_id,
    site_id,
    assignment_status_code,
    assignment_source_code,
    assignment_reason_code,
    assigned_at,
    effective_from,
    effective_to,
    ended_at,
    end_reason_code,
    correlation_id
)
VALUES
    (
        '77000000-0000-0000-0000-000000000040',
        '77000000-0000-0000-0000-000000000030',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        'ACTIVE',
        'MANUAL_TEST_OPERATOR_ACCESS',
        'MANUAL_TEST_OPERATOR_ACCESS_ALLOWED',
        now(),
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        null,
        '77000000-0000-0000-0000-0000000000d0'
    ),
    (
        '77000000-0000-0000-0000-000000000041',
        '77000000-0000-0000-0000-000000000031',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        'ACTIVE',
        'MANUAL_TEST_OPERATOR_ACCESS',
        'MANUAL_TEST_OPERATOR_ACCESS_INACTIVE_DEVICE',
        now(),
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        null,
        '77000000-0000-0000-0000-0000000000d1'
    ),
    (
        '77000000-0000-0000-0000-000000000042',
        '77000000-0000-0000-0000-000000000032',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        'ACTIVE',
        'MANUAL_TEST_OPERATOR_ACCESS',
        'MANUAL_TEST_OPERATOR_ACCESS_UNTRUSTED_DEVICE',
        now(),
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        null,
        '77000000-0000-0000-0000-0000000000d2'
    ),
    (
        '77000000-0000-0000-0000-000000000043',
        '77000000-0000-0000-0000-000000000033',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        'SUSPENDED',
        'MANUAL_TEST_OPERATOR_ACCESS',
        'MANUAL_TEST_OPERATOR_ACCESS_INVALID_ASSIGNMENT',
        now(),
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        'MANUAL_TEST_OPERATOR_ACCESS_INVALID_ASSIGNMENT',
        '77000000-0000-0000-0000-0000000000d3'
    )
ON CONFLICT (operator_device_assignment_history_id) DO UPDATE
SET operator_device_binding_id = EXCLUDED.operator_device_binding_id,
    site_group_id = EXCLUDED.site_group_id,
    site_id = EXCLUDED.site_id,
    assignment_status_code = EXCLUDED.assignment_status_code,
    assignment_source_code = EXCLUDED.assignment_source_code,
    assignment_reason_code = EXCLUDED.assignment_reason_code,
    assigned_at = EXCLUDED.assigned_at,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    ended_at = EXCLUDED.ended_at,
    end_reason_code = EXCLUDED.end_reason_code,
    correlation_id = EXCLUDED.correlation_id;

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
    revoked_at,
    revocation_reason_code,
    current_takeover_id,
    correlation_id
)
VALUES
    (
        '77000000-0000-0000-0000-000000000050',
        'MANUAL_TEST_OPERATOR_ACCESS',
        '7700000000000000000000000000005077000000000000000000000000000050',
        'MTOA-SHIFT-ALLOWED',
        '77000000-0000-0000-0000-000000000020',
        '77000000-0000-0000-0000-000000000010',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        now(),
        'IMPORTED',
        'MANUAL_TEST_OPERATOR_ACCESS',
        'ACTIVE',
        'ACTIVE',
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        null,
        null,
        '77000000-0000-0000-0000-0000000000e0'
    ),
    (
        '77000000-0000-0000-0000-000000000051',
        'MANUAL_TEST_OPERATOR_ACCESS',
        '7700000000000000000000000000005177000000000000000000000000000051',
        'MTOA-SHIFT-INACTIVE-HR',
        '77000000-0000-0000-0000-000000000021',
        '77000000-0000-0000-0000-000000000011',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        now(),
        'IMPORTED',
        'MANUAL_TEST_OPERATOR_ACCESS',
        'ACTIVE',
        'ACTIVE',
        '2020-01-01T00:00:00Z',
        '2035-01-01T00:00:00Z',
        null,
        null,
        null,
        '77000000-0000-0000-0000-0000000000e1'
    ),
    (
        '77000000-0000-0000-0000-000000000052',
        'MANUAL_TEST_OPERATOR_ACCESS',
        '7700000000000000000000000000005277000000000000000000000000000052',
        'MTOA-SHIFT-NO-ACTIVE',
        '77000000-0000-0000-0000-000000000020',
        '77000000-0000-0000-0000-000000000010',
        '77000000-0000-0000-0000-000000000001',
        '77000000-0000-0000-0000-000000000002',
        '2020-01-01T00:00:00Z',
        '2020-01-02T00:00:00Z',
        now(),
        'IMPORTED',
        'MANUAL_TEST_OPERATOR_ACCESS',
        'ENDED',
        'ENDED',
        '2020-01-01T00:00:00Z',
        '2020-01-02T00:00:00Z',
        null,
        null,
        null,
        '77000000-0000-0000-0000-0000000000e2'
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
    revoked_at = EXCLUDED.revoked_at,
    revocation_reason_code = EXCLUDED.revocation_reason_code,
    current_takeover_id = EXCLUDED.current_takeover_id,
    correlation_id = EXCLUDED.correlation_id,
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
    'db0f3cc6d7f064c0472ee745c6afdce3c097959263e75784f9f8df5fe2e07ecf',
    'ABC-1234',
    'af56738409b6803992ba87c54bf00fc2b2c29de870fbae5fadaac3c6f242db08',
    'MANUAL-SESSION-LOOKUP-001',
    '2026-05-29T00:00:00Z',
    'ACTIVE',
    'ACTIVE',
    '77000000-0000-0000-0000-0000000000f0',
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
    entry_at = EXCLUDED.entry_at,
    vendor_session_status = EXCLUDED.vendor_session_status,
    session_status = EXCLUDED.session_status,
    correlation_id = EXCLUDED.correlation_id,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now();

DELETE FROM discounts.discount_evidence_references
WHERE statutory_discount_validation_id IN (
    SELECT statutory_discount_validation_id
    FROM discounts.statutory_discount_validations
    WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'
      AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD')
      AND validation_channel = 'OPERATOR_ASSISTED'
      AND validation_status IN ('REQUESTED', 'PENDING_OPERATOR_REVIEW')
);

DELETE FROM discounts.statutory_discount_validations
WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'
  AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD')
  AND validation_channel = 'OPERATOR_ASSISTED'
  AND validation_status IN ('REQUESTED', 'PENDING_OPERATOR_REVIEW');

COMMIT;
