BEGIN;

INSERT INTO identity.service_identities (
    service_identity_id, service_identity_code, service_identity_name, identity_type,
    identity_status, owning_service_name, credential_type, effective_from)
VALUES (
    '12000000-0000-0000-0000-000000000002', 'exitpass-central-pms-developer',
    'Developer Central PMS', 'INTERNAL_SERVICE', 'ACTIVE',
    'central-pms', 'NONE', '2026-01-01T00:00:00Z')
ON CONFLICT (service_identity_id) DO UPDATE SET
    service_identity_name = EXCLUDED.service_identity_name,
    identity_status = EXCLUDED.identity_status,
    updated_at = now(),
    row_version = identity.service_identities.row_version + 1;

-- Existing v1.3 patches retain this historical reference-data actor UUID.
-- Developer bootstrap supplies the FK target without importing legacy data.
INSERT INTO identity.service_identities (
    service_identity_id, service_identity_code, service_identity_name, identity_type,
    identity_status, owning_service_name, credential_type, effective_from)
VALUES (
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', 'developer-v1.3-patch-actor',
    'Developer v1.3 Patch Actor', 'INTERNAL_SERVICE', 'ACTIVE',
    'developer-runtime', 'NONE', '2026-01-01T00:00:00Z')
ON CONFLICT (service_identity_id) DO UPDATE SET
    identity_status = EXCLUDED.identity_status,
    updated_at = now(),
    row_version = identity.service_identities.row_version + 1;

UPDATE sites.site_groups
SET site_group_code = 'DEV',
    site_group_name = 'DEV',
    business_label = 'Developer',
    description = 'Isolated synthetic Developer runtime Site Group.',
    public_lookup_enabled = true,
    default_payment_enabled = true,
    updated_at = now(),
    row_version = row_version + 1
WHERE site_group_id = '12000000-0000-0000-0000-000000000301';

UPDATE sites.sites
SET site_code = 'DEV-PARKING',
    site_name = 'ExitPass Developer Parking',
    site_description = 'Isolated synthetic parking Site backed by WireMock HikCentral.',
    public_lookup_enabled = true,
    payment_enabled = true,
    updated_at = now(),
    row_version = row_version + 1
WHERE site_id = '12000000-0000-0000-0000-000000000302';

INSERT INTO identity.service_identities (
    service_identity_id, service_identity_code, service_identity_name, identity_type,
    identity_status, owning_service_name, credential_type, effective_from)
VALUES (
    'de100000-0000-0000-0000-000000000004', 'exitpass-dev-site-adapter',
    'Developer HikCentral Site Adapter', 'ADAPTER', 'ACTIVE',
    'site-integration-adapter', 'NONE', '2026-01-01T00:00:00Z')
ON CONFLICT (service_identity_id) DO UPDATE SET
    service_identity_name = EXCLUDED.service_identity_name,
    identity_status = EXCLUDED.identity_status,
    updated_at = now(),
    row_version = identity.service_identities.row_version + 1;

INSERT INTO integration.vendor_systems (
    vendor_system_id, vendor_code, vendor_name, vendor_system_type,
    vendor_system_status, environment_code, base_url_ref, api_version,
    owner_team, support_contact_ref, effective_from)
VALUES (
    'de100000-0000-0000-0000-000000000003', 'HIKCENTRAL_DEV',
    'Developer WireMock HikCentral', 'VENDOR_PMS', 'ACTIVE', 'DEVELOPER',
    'http://exitpass-dev-site-adapter:8080', 'V3.1.0',
    'ExitPass Engineering', 'developer-runtime', '2026-01-01T00:00:00Z')
ON CONFLICT (vendor_system_id) DO UPDATE SET
    vendor_code = EXCLUDED.vendor_code,
    vendor_name = EXCLUDED.vendor_name,
    vendor_system_status = EXCLUDED.vendor_system_status,
    environment_code = EXCLUDED.environment_code,
    base_url_ref = EXCLUDED.base_url_ref,
    api_version = EXCLUDED.api_version,
    updated_at = now(),
    row_version = integration.vendor_systems.row_version + 1;

INSERT INTO integration.integration_credential_references (
    integration_credential_reference_id, vendor_system_id, service_identity_id,
    credential_code, credential_name, credential_type, secret_store_type,
    secret_reference, credential_status, created_at)
VALUES (
    'de100000-0000-0000-0000-000000000005',
    'de100000-0000-0000-0000-000000000003',
    '12000000-0000-0000-0000-000000000002',
    'DEV_SITE_ADAPTER_KEY', 'Developer Site Adapter API Key',
    'API_KEY_REFERENCE', 'OTHER', 'file:developer-site-adapter.key', 'ACTIVE', now())
ON CONFLICT (integration_credential_reference_id) DO UPDATE SET
    secret_reference = EXCLUDED.secret_reference,
    credential_status = EXCLUDED.credential_status,
    revoked_at = NULL,
    expires_at = NULL,
    updated_at = now(),
    row_version = integration.integration_credential_references.row_version + 1;

INSERT INTO integration.vendor_endpoints (
    vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name,
    endpoint_type, http_method, path_template, credential_reference_id,
    endpoint_status, effective_from)
VALUES (
    'de100000-0000-0000-0000-000000000006',
    'de100000-0000-0000-0000-000000000003',
    'SITE_ADAPTER_API', 'Developer Site Adapter API', 'OTHER', 'POST',
    '/v1/vendor/*', 'de100000-0000-0000-0000-000000000005',
    'ACTIVE', '2026-01-01T00:00:00Z')
ON CONFLICT (vendor_endpoint_id) DO UPDATE SET
    credential_reference_id = EXCLUDED.credential_reference_id,
    endpoint_status = EXCLUDED.endpoint_status,
    effective_to = NULL,
    updated_at = now(),
    row_version = integration.vendor_endpoints.row_version + 1;

INSERT INTO integration.adapter_mappings (
    adapter_mapping_id, vendor_system_id, mapping_type, site_group_id, site_id,
    vendor_object_type, vendor_object_ref, vendor_object_name, mapping_status,
    mapping_confidence, effective_from)
VALUES (
    'de100000-0000-0000-0000-000000000007',
    'de100000-0000-0000-0000-000000000003', 'SITE',
    '12000000-0000-0000-0000-000000000301',
    '12000000-0000-0000-0000-000000000302',
    'SITE_ADAPTER', 'de100000-0000-0000-0000-000000000004',
    'Developer HikCentral Site Adapter', 'ACTIVE', 'MANUAL_APPROVED',
    '2026-01-01T00:00:00Z')
ON CONFLICT (adapter_mapping_id) DO UPDATE SET
    vendor_system_id = EXCLUDED.vendor_system_id,
    site_group_id = EXCLUDED.site_group_id,
    site_id = EXCLUDED.site_id,
    vendor_object_ref = EXCLUDED.vendor_object_ref,
    mapping_status = EXCLUDED.mapping_status,
    effective_to = NULL,
    updated_at = now(),
    row_version = integration.adapter_mappings.row_version + 1;

INSERT INTO sessions.vendor_session_projection_sync_targets (
    projection_sync_target_id, site_id, site_group_id, vendor_system_id,
    parking_lot_index_code, parking_lot_name, enabled_flag,
    poll_interval_seconds, lookback_window_minutes, page_size,
    health_status, failure_count, created_at, updated_at)
VALUES (
    'de100000-0000-0000-0000-000000000008',
    '12000000-0000-0000-0000-000000000302',
    '12000000-0000-0000-0000-000000000301',
    'de100000-0000-0000-0000-000000000003',
    'DEV-LOT-1', 'ExitPass Developer Parking', true, 30, 180, 100,
    'UNKNOWN', 0, now(), now())
ON CONFLICT (projection_sync_target_id) DO UPDATE SET
    site_id = EXCLUDED.site_id,
    site_group_id = EXCLUDED.site_group_id,
    vendor_system_id = EXCLUDED.vendor_system_id,
    parking_lot_index_code = EXCLUDED.parking_lot_index_code,
    parking_lot_name = EXCLUDED.parking_lot_name,
    enabled_flag = EXCLUDED.enabled_flag,
    poll_interval_seconds = EXCLUDED.poll_interval_seconds,
    lookback_window_minutes = EXCLUDED.lookback_window_minutes,
    page_size = EXCLUDED.page_size,
    updated_at = now(),
    row_version = sessions.vendor_session_projection_sync_targets.row_version + 1;

COMMIT;
