-- ExitPass Reference Data v1.2
-- Purpose: clean, idempotent local-development and integration-test baseline data.
-- Run only after ExitPass_Full_Database_Creation_DDL_v1.2.sql on a fresh v1.2 database.
-- This script uses deterministic UUIDs and v1.2 physical names only.
-- It intentionally stores no production secrets, fake secrets, private keys, or credentials.

BEGIN;

-- ---------------------------------------------------------------------------
-- Stable identifiers used by the v1.2 reference-data baseline.
-- ---------------------------------------------------------------------------
-- 12000000-0000-0000-0000-000000000001 identity.service_identities: seed.reference-data
-- 12000000-0000-0000-0000-00000000000a identity.service_identities: dev-exit-gate-01
-- 12000000-0000-0000-0000-000000000301 sites.site_groups: DEV_PROPERTY
-- 12000000-0000-0000-0000-000000000302 sites.sites: DEV_PARKING
-- 12000000-0000-0000-0000-000000000303 sites.lanes: DEV_ENTRY_01
-- 12000000-0000-0000-0000-000000000304 sites.lanes: DEV_EXIT_01
-- 12000000-0000-0000-0000-000000000401 gates.gate_devices: DEV_EXIT_GATE_01
-- 12000000-0000-0000-0000-000000000801 merchants.merchants: DEV_TEST_MERCHANT
-- 12000000-0000-0000-0000-000000000802 merchants.merchant_wallets: DEV_COUPON_WALLET
-- 12000000-0000-0000-0000-000000000901 coupons.coupons: DEV_FIXED_50

-- ---------------------------------------------------------------------------
-- identity.service_identities
-- ---------------------------------------------------------------------------
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
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('12000000-0000-0000-0000-000000000001', 'seed.reference-data', 'ExitPass v1.2 Reference Data Seeder', 'SCHEDULED_JOB', 'ACTIVE', 'database-reference-data', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000002', 'central-pms', 'Central PMS Service', 'INTERNAL_SERVICE', 'ACTIVE', 'central-pms', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000003', 'payment-orchestrator', 'Payment Orchestrator Service', 'INTERNAL_SERVICE', 'ACTIVE', 'payment-orchestrator', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000004', 'session-service', 'Session Service', 'INTERNAL_SERVICE', 'ACTIVE', 'session-service', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000005', 'site-integration-adapter', 'Site Integration Adapter', 'ADAPTER', 'ACTIVE', 'site-integration-adapter', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000006', 'gate-integration-service', 'Gate Integration Service', 'INTERNAL_SERVICE', 'ACTIVE', 'gate-integration-service', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000007', 'audit-event-service', 'Audit Event Service', 'BACKGROUND_WORKER', 'ACTIVE', 'audit-event-service', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000008', 'db-migrator', 'Database Migrator', 'SCHEDULED_JOB', 'ACTIVE', 'db-migrator', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-000000000009', 'mock-vendor-pms', 'Mock Vendor PMS', 'EXTERNAL_CLIENT', 'ACTIVE', 'mock-vendor-pms', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-00000000000a', 'dev-exit-gate-01', 'Development Exit Gate Device 01', 'DEVICE', 'ACTIVE', 'gate-integration-service', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL),
    ('12000000-0000-0000-0000-00000000000b', 'mock-payment-provider', 'Mock Payment Provider', 'EXTERNAL_CLIENT', 'ACTIVE', 'mock-payment-provider', NULL, 'NONE', '2026-01-01T00:00:00Z', NULL, NULL)
ON CONFLICT ON CONSTRAINT uq_service_identities__service_identity_code
DO UPDATE SET
    service_identity_name = EXCLUDED.service_identity_name,
    identity_type = EXCLUDED.identity_type,
    identity_status = EXCLUDED.identity_status,
    owning_service_name = EXCLUDED.owning_service_name,
    credential_reference = NULL,
    credential_type = 'NONE',
    effective_from = EXCLUDED.effective_from,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- sites.site_groups, sites.sites, sites.lanes
-- ---------------------------------------------------------------------------
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
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000301',
    'DEV_PROPERTY',
    'ExitPass Development Property',
    'PROPERTY',
    'Reference site group for local ExitPass v1.2 development and integration testing.',
    'ExitPass Development Operator',
    'Asia/Manila',
    'PHP',
    'ACTIVE',
    true,
    true,
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_site_groups__site_group_code
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
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

INSERT INTO sites.sites (
    site_id,
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
    lgu_code,
    site_status,
    public_lookup_enabled,
    payment_enabled,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000302',
    '12000000-0000-0000-0000-000000000301',
    'DEV_PARKING',
    'ExitPass Development Parking Site',
    'Reference parking site for local ExitPass v1.2 development and integration testing.',
    'MIXED_USE_PROPERTY',
    'Asia/Manila',
    'Development Site',
    'Taguig',
    'Metro Manila',
    'PH',
    'PH-00-DEV',
    'ACTIVE',
    true,
    true,
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_sites__site_group_site_code
DO UPDATE SET
    site_name = EXCLUDED.site_name,
    site_description = EXCLUDED.site_description,
    site_type = EXCLUDED.site_type,
    timezone_name = EXCLUDED.timezone_name,
    address_line1 = EXCLUDED.address_line1,
    city = EXCLUDED.city,
    province = EXCLUDED.province,
    country_code = EXCLUDED.country_code,
    lgu_code = EXCLUDED.lgu_code,
    site_status = EXCLUDED.site_status,
    public_lookup_enabled = EXCLUDED.public_lookup_enabled,
    payment_enabled = EXCLUDED.payment_enabled,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

INSERT INTO sites.lanes (
    lane_id,
    site_id,
    lane_code,
    lane_name,
    lane_description,
    lane_type,
    lane_direction,
    lane_status,
    display_order,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('12000000-0000-0000-0000-000000000303', '12000000-0000-0000-0000-000000000302', 'DEV_ENTRY_01', 'Development Entry Lane 01', 'Reference inbound lane for development testing.', 'ENTRY', 'INBOUND', 'ACTIVE', 1, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000304', '12000000-0000-0000-0000-000000000302', 'DEV_EXIT_01', 'Development Exit Lane 01', 'Reference outbound lane for development testing.', 'EXIT', 'OUTBOUND', 'ACTIVE', 2, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001')
ON CONFLICT ON CONSTRAINT uq_lanes__site_lane_code
DO UPDATE SET
    lane_name = EXCLUDED.lane_name,
    lane_description = EXCLUDED.lane_description,
    lane_type = EXCLUDED.lane_type,
    lane_direction = EXCLUDED.lane_direction,
    lane_status = EXCLUDED.lane_status,
    display_order = EXCLUDED.display_order,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- integration.vendor_systems
-- ---------------------------------------------------------------------------
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
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('12000000-0000-0000-0000-000000000101', 'MOCK_VENDOR_PMS', 'Mock Vendor PMS', 'VENDOR_PMS', 'ACTIVE', 'DEV', NULL, 'v1.2', 'ExitPass Engineering', 'local-development', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000102', 'MOCK_PAYMENT_PROVIDER', 'Mock Payment Provider', 'PAYMENT_PROVIDER', 'ACTIVE', 'DEV', NULL, 'v1.2', 'ExitPass Engineering', 'local-development', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000103', 'HIKCENTRAL_PLACEHOLDER', 'HikCentral Placeholder', 'GATE_CONTROLLER', 'DRAFT', 'DEV', NULL, NULL, 'ExitPass Engineering', 'placeholder-only', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001')
ON CONFLICT ON CONSTRAINT uq_vendor_systems__vendor_code_environment
DO UPDATE SET
    vendor_name = EXCLUDED.vendor_name,
    vendor_system_type = EXCLUDED.vendor_system_type,
    vendor_system_status = EXCLUDED.vendor_system_status,
    base_url_ref = NULL,
    api_version = EXCLUDED.api_version,
    owner_team = EXCLUDED.owner_team,
    support_contact_ref = EXCLUDED.support_contact_ref,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- payments.payment_rails
-- ---------------------------------------------------------------------------
INSERT INTO payments.payment_rails (
    payment_rail_id,
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
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('12000000-0000-0000-0000-000000000201', 'QRPH_TEST', 'QRPH Test Rail', 'MOCK_PAYMENT_PROVIDER', 'QRPH', 'PHP', 'ACTIVE', true, false, 'mock-payment-provider-dev', NULL, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000202', 'HOSTED_CHECKOUT_TEST', 'Hosted Checkout Test Rail', 'MOCK_PAYMENT_PROVIDER', 'HOSTED_CHECKOUT', 'PHP', 'ACTIVE', false, true, 'mock-payment-provider-dev', NULL, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000203', 'MOCK_PROVIDER_RAIL', 'Mock Provider Rail', 'MOCK_PAYMENT_PROVIDER', 'OTHER', 'PHP', 'ACTIVE', false, true, 'mock-payment-provider-dev', NULL, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001')
ON CONFLICT ON CONSTRAINT uq_payment_rails__rail_code
DO UPDATE SET
    rail_name = EXCLUDED.rail_name,
    provider_code = EXCLUDED.provider_code,
    rail_type = EXCLUDED.rail_type,
    supported_currency_code = EXCLUDED.supported_currency_code,
    rail_status = EXCLUDED.rail_status,
    is_primary = EXCLUDED.is_primary,
    is_fallback = EXCLUDED.is_fallback,
    provider_profile_ref = EXCLUDED.provider_profile_ref,
    configuration_ref = NULL,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- gates.gate_devices and sites.device_assignments
-- ---------------------------------------------------------------------------
INSERT INTO gates.gate_devices (
    gate_device_id,
    site_id,
    lane_id,
    service_identity_id,
    device_code,
    device_name,
    device_type,
    vendor_device_ref,
    serial_number,
    device_status,
    installed_at,
    activated_at,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000401',
    '12000000-0000-0000-0000-000000000302',
    '12000000-0000-0000-0000-000000000304',
    '12000000-0000-0000-0000-00000000000a',
    'DEV_EXIT_GATE_01',
    'Development Exit Gate 01',
    'BARRIER_CONTROLLER',
    'DEV-EXIT-GATE-01',
    NULL,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_gate_devices__site_device_code
DO UPDATE SET
    lane_id = EXCLUDED.lane_id,
    service_identity_id = EXCLUDED.service_identity_id,
    device_name = EXCLUDED.device_name,
    device_type = EXCLUDED.device_type,
    vendor_device_ref = EXCLUDED.vendor_device_ref,
    serial_number = NULL,
    device_status = EXCLUDED.device_status,
    installed_at = EXCLUDED.installed_at,
    activated_at = EXCLUDED.activated_at,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

INSERT INTO sites.device_assignments (
    device_assignment_id,
    site_id,
    lane_id,
    gate_device_id,
    service_identity_id,
    assignment_type,
    assignment_status,
    assignment_reason_code,
    assigned_at,
    assigned_by_service_identity_id,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000501',
    '12000000-0000-0000-0000-000000000302',
    '12000000-0000-0000-0000-000000000304',
    '12000000-0000-0000-0000-000000000401',
    '12000000-0000-0000-0000-00000000000a',
    'GATE_DEVICE',
    'ACTIVE',
    'DEV_SEED',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT pk_device_assignments
DO UPDATE SET
    site_id = EXCLUDED.site_id,
    lane_id = EXCLUDED.lane_id,
    gate_device_id = EXCLUDED.gate_device_id,
    service_identity_id = EXCLUDED.service_identity_id,
    assignment_type = EXCLUDED.assignment_type,
    assignment_status = EXCLUDED.assignment_status,
    assignment_reason_code = EXCLUDED.assignment_reason_code,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- config.ttl_policies
-- ---------------------------------------------------------------------------
INSERT INTO config.ttl_policies (
    ttl_policy_id,
    policy_code,
    policy_name,
    policy_description,
    policy_domain,
    ttl_scope_type,
    ttl_seconds,
    grace_period_seconds,
    expiry_action,
    policy_status,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('12000000-0000-0000-0000-000000000601', 'TTL_TARIFF_SNAPSHOT_DEV', 'Tariff Snapshot TTL', 'Development TTL for tariff snapshots.', 'PAYMENT_CHAIN', 'TARIFF_SNAPSHOT', 900, 0, 'EXPIRE_RECORD', 'ACTIVE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000602', 'TTL_PAYMENT_ATTEMPT_DEV', 'Payment Attempt TTL', 'Development TTL for active payment attempts.', 'PAYMENT_CHAIN', 'PAYMENT_ATTEMPT', 1800, 0, 'EXPIRE_RECORD', 'ACTIVE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000603', 'TTL_EXIT_AUTHORIZATION_DEV', 'Exit Authorization TTL', 'Development TTL for issued exit authorizations.', 'PAYMENT_CHAIN', 'EXIT_AUTHORIZATION', 600, 0, 'INVALIDATE_RECORD', 'ACTIVE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000604', 'TTL_PROVIDER_CALLBACK_REPLAY_DEV', 'Provider Callback Replay Window', 'Development replay window for provider callback idempotency.', 'PAYMENT_CHAIN', 'PROVIDER_CALLBACK_REPLAY_WINDOW', 86400, 0, 'BLOCK_USE', 'ACTIVE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001')
ON CONFLICT ON CONSTRAINT uq_ttl_policies__policy_code
DO UPDATE SET
    policy_name = EXCLUDED.policy_name,
    policy_description = EXCLUDED.policy_description,
    policy_domain = EXCLUDED.policy_domain,
    ttl_scope_type = EXCLUDED.ttl_scope_type,
    ttl_seconds = EXCLUDED.ttl_seconds,
    grace_period_seconds = EXCLUDED.grace_period_seconds,
    expiry_action = EXCLUDED.expiry_action,
    policy_status = EXCLUDED.policy_status,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- config.rate_limit_policies
-- ---------------------------------------------------------------------------
INSERT INTO config.rate_limit_policies (
    rate_limit_policy_id,
    policy_code,
    policy_name,
    policy_description,
    policy_domain,
    scope_type,
    window_seconds,
    max_requests,
    burst_limit,
    penalty_seconds,
    policy_status,
    enforcement_mode,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('12000000-0000-0000-0000-000000000611', 'RL_PUBLIC_SESSION_LOOKUP_DEV', 'Public Session Lookup', 'Development rate limit for public session lookup.', 'PUBLIC_API', 'PUBLIC_LOOKUP', 60, 120, 20, 60, 'ACTIVE', 'ENFORCE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000612', 'RL_PAYMENT_CREATE_DEV', 'Payment Create', 'Development rate limit for payment attempt creation.', 'PUBLIC_API', 'PAYMENT_CREATE', 60, 60, 10, 60, 'ACTIVE', 'ENFORCE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000613', 'RL_GATE_CONSUME_DEV', 'Gate Consume', 'Development rate limit for gate authorization consumption.', 'GATE_API', 'GATE_CONSUME', 60, 300, 50, 30, 'ACTIVE', 'ENFORCE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000614', 'RL_PROVIDER_CALLBACK_DEV', 'Provider Callback', 'Development rate limit for payment provider callbacks.', 'PROVIDER_API', 'PROVIDER_CALLBACK', 60, 600, 100, 30, 'ACTIVE', 'ENFORCE', '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001')
ON CONFLICT ON CONSTRAINT uq_rate_limit_policies__policy_code
DO UPDATE SET
    policy_name = EXCLUDED.policy_name,
    policy_description = EXCLUDED.policy_description,
    policy_domain = EXCLUDED.policy_domain,
    scope_type = EXCLUDED.scope_type,
    window_seconds = EXCLUDED.window_seconds,
    max_requests = EXCLUDED.max_requests,
    burst_limit = EXCLUDED.burst_limit,
    penalty_seconds = EXCLUDED.penalty_seconds,
    policy_status = EXCLUDED.policy_status,
    enforcement_mode = EXCLUDED.enforcement_mode,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- config.controlled_code_sets
-- ---------------------------------------------------------------------------
INSERT INTO config.controlled_code_sets (
    controlled_code_set_id,
    code_set_name,
    code_value,
    code_label,
    code_description,
    code_domain,
    code_status,
    sort_order,
    requires_comment,
    requires_approval,
    is_sensitive,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('12000000-0000-0000-0000-000000000621', 'ASSIGNMENT_REASON', 'DEV_SEED', 'Development Seed Assignment', 'Reason code used by the v1.2 reference data seed for local device assignments.', 'SITES', 'ACTIVE', 10, false, false, false, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000622', 'PAYMENT_ERROR', 'PROVIDER_REJECTED', 'Provider Rejected', 'Payment provider rejected or failed a payment outcome.', 'PAYMENTS', 'ACTIVE', 20, false, false, false, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000623', 'EXIT_AUTHORIZATION_REASON', 'TOKEN_EXPIRED', 'Token Expired', 'Exit authorization token expired before gate consumption.', 'GATES', 'ACTIVE', 30, false, false, false, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000624', 'GATE_DENIAL_REASON', 'AUTHORIZATION_INVALID', 'Authorization Invalid', 'Gate consume request was denied because the authorization was invalid.', 'GATES', 'ACTIVE', 40, false, false, false, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001'),
    ('12000000-0000-0000-0000-000000000625', 'OPERATOR_ACTION', 'MANUAL_REVIEW', 'Manual Review', 'Operational action code for local support review workflows.', 'OPERATIONS', 'ACTIVE', 50, true, false, false, '2026-01-01T00:00:00Z', '12000000-0000-0000-0000-000000000001', '12000000-0000-0000-0000-000000000001')
ON CONFLICT ON CONSTRAINT uq_controlled_code_sets__set_value_domain
DO UPDATE SET
    code_label = EXCLUDED.code_label,
    code_description = EXCLUDED.code_description,
    code_status = EXCLUDED.code_status,
    sort_order = EXCLUDED.sort_order,
    requires_comment = EXCLUDED.requires_comment,
    requires_approval = EXCLUDED.requires_approval,
    is_sensitive = EXCLUDED.is_sensitive,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- merchants.merchants and merchants.merchant_wallets
-- ---------------------------------------------------------------------------
INSERT INTO merchants.merchants (
    merchant_id,
    merchant_code,
    merchant_name,
    merchant_display_name,
    merchant_type,
    merchant_status,
    tax_identification_number_hash,
    contact_email,
    contact_mobile_masked,
    default_currency_code,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000801',
    'DEV_TEST_MERCHANT',
    'ExitPass Development Test Merchant',
    'Dev Test Merchant',
    'PROMOTIONAL_PARTNER',
    'ACTIVE',
    NULL,
    NULL,
    NULL,
    'PHP',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_merchants__merchant_code
DO UPDATE SET
    merchant_name = EXCLUDED.merchant_name,
    merchant_display_name = EXCLUDED.merchant_display_name,
    merchant_type = EXCLUDED.merchant_type,
    merchant_status = EXCLUDED.merchant_status,
    tax_identification_number_hash = NULL,
    contact_email = NULL,
    contact_mobile_masked = NULL,
    default_currency_code = EXCLUDED.default_currency_code,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

INSERT INTO merchants.merchant_wallets (
    merchant_wallet_id,
    merchant_id,
    wallet_code,
    wallet_name,
    wallet_type,
    wallet_status,
    currency_code,
    available_balance,
    reserved_balance,
    committed_balance,
    external_ledger_ref,
    allows_coupon_funding,
    allows_statutory_discount_funding,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000802',
    '12000000-0000-0000-0000-000000000801',
    'DEV_COUPON_WALLET',
    'Development Coupon Wallet',
    'PROMOTIONAL_BUDGET',
    'ACTIVE',
    'PHP',
    100000.00,
    0.00,
    0.00,
    NULL,
    true,
    false,
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_merchant_wallets__merchant_wallet_code
DO UPDATE SET
    wallet_name = EXCLUDED.wallet_name,
    wallet_type = EXCLUDED.wallet_type,
    wallet_status = EXCLUDED.wallet_status,
    currency_code = EXCLUDED.currency_code,
    available_balance = EXCLUDED.available_balance,
    reserved_balance = EXCLUDED.reserved_balance,
    committed_balance = EXCLUDED.committed_balance,
    external_ledger_ref = NULL,
    allows_coupon_funding = EXCLUDED.allows_coupon_funding,
    allows_statutory_discount_funding = false,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- coupons.coupons, coupons.coupon_rule_groups, coupons.coupon_rules
-- ---------------------------------------------------------------------------
INSERT INTO coupons.coupons (
    coupon_id,
    merchant_id,
    coupon_code,
    coupon_name,
    coupon_description,
    coupon_type,
    denomination_type,
    denomination_value,
    currency_code,
    maximum_discount_amount,
    minimum_gross_amount,
    stacking_policy,
    allows_full_waiver,
    requires_elevated_approval,
    coupon_status,
    valid_from,
    valid_to,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000901',
    '12000000-0000-0000-0000-000000000801',
    'DEV_FIXED_50',
    'Development Fixed PHP 50 Coupon',
    'Simple fixed-amount test coupon for local v1.2 coupon workflows.',
    'STANDARD',
    'FIXED_AMOUNT',
    50.00,
    'PHP',
    50.00,
    0.00,
    'NO_STACKING',
    false,
    false,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    NULL,
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_coupons__merchant_coupon_code
DO UPDATE SET
    coupon_name = EXCLUDED.coupon_name,
    coupon_description = EXCLUDED.coupon_description,
    coupon_type = EXCLUDED.coupon_type,
    denomination_type = EXCLUDED.denomination_type,
    denomination_value = EXCLUDED.denomination_value,
    currency_code = EXCLUDED.currency_code,
    maximum_discount_amount = EXCLUDED.maximum_discount_amount,
    minimum_gross_amount = EXCLUDED.minimum_gross_amount,
    stacking_policy = EXCLUDED.stacking_policy,
    allows_full_waiver = false,
    requires_elevated_approval = false,
    coupon_status = EXCLUDED.coupon_status,
    valid_from = EXCLUDED.valid_from,
    valid_to = NULL,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

INSERT INTO coupons.coupon_rule_groups (
    coupon_rule_group_id,
    coupon_id,
    rule_group_code,
    rule_group_name,
    rule_group_description,
    evaluation_strategy,
    evaluation_priority,
    is_required,
    rule_group_status,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000902',
    '12000000-0000-0000-0000-000000000901',
    'DEV_FIXED_50_ELIGIBILITY',
    'Development Fixed Coupon Eligibility',
    'Eligibility rules for the development fixed-amount coupon.',
    'ALL_RULES_MUST_PASS',
    0,
    true,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_coupon_rule_groups__coupon_rule_group_code
DO UPDATE SET
    rule_group_name = EXCLUDED.rule_group_name,
    rule_group_description = EXCLUDED.rule_group_description,
    evaluation_strategy = EXCLUDED.evaluation_strategy,
    evaluation_priority = EXCLUDED.evaluation_priority,
    is_required = EXCLUDED.is_required,
    rule_group_status = EXCLUDED.rule_group_status,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

INSERT INTO coupons.coupon_rules (
    coupon_rule_id,
    coupon_rule_group_id,
    rule_code,
    rule_name,
    rule_type,
    rule_operator,
    rule_value_text,
    rule_value_numeric,
    rule_value_boolean,
    site_group_id,
    site_id,
    merchant_id,
    rule_status,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000903',
    '12000000-0000-0000-0000-000000000902',
    'DEV_PARKING_ONLY',
    'Development Parking Site Scope',
    'SITE_SCOPE',
    'EQUALS',
    'DEV_PARKING',
    NULL,
    NULL,
    NULL,
    '12000000-0000-0000-0000-000000000302',
    NULL,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_coupon_rules__group_rule_code
DO UPDATE SET
    rule_name = EXCLUDED.rule_name,
    rule_type = EXCLUDED.rule_type,
    rule_operator = EXCLUDED.rule_operator,
    rule_value_text = EXCLUDED.rule_value_text,
    rule_value_numeric = NULL,
    rule_value_boolean = NULL,
    site_group_id = NULL,
    site_id = EXCLUDED.site_id,
    merchant_id = NULL,
    rule_status = EXCLUDED.rule_status,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

-- ---------------------------------------------------------------------------
-- discounts.discount_policy_references
-- ---------------------------------------------------------------------------
INSERT INTO discounts.discount_policy_references (
    discount_policy_reference_id,
    policy_code,
    policy_name,
    policy_description,
    policy_type,
    policy_level,
    entitlement_type,
    national_law_reference,
    local_ordinance_reference,
    lgu_code,
    jurisdiction_name,
    site_group_id,
    site_id,
    parent_policy_reference_id,
    fallback_policy_reference_id,
    precedence_rank,
    policy_version,
    requires_operator_validation,
    requires_evidence_capture,
    evidence_retention_policy_code,
    policy_status,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000a01',
    'PH_NATIONAL_STATUTORY_FALLBACK',
    'Philippines National Statutory Discount Fallback',
    'Development reference for national-law fallback policy resolution.',
    'LEGAL_REFERENCE',
    'NATIONAL_LAW',
    'SENIOR_CITIZEN',
    'PH-STATUTORY-FALLBACK',
    NULL,
    NULL,
    'Philippines',
    NULL,
    NULL,
    NULL,
    NULL,
    100,
    'v1.2-dev',
    true,
    true,
    NULL,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_discount_policy_references__policy_code_version
DO UPDATE SET
    policy_name = EXCLUDED.policy_name,
    policy_description = EXCLUDED.policy_description,
    policy_type = EXCLUDED.policy_type,
    policy_level = EXCLUDED.policy_level,
    entitlement_type = EXCLUDED.entitlement_type,
    national_law_reference = EXCLUDED.national_law_reference,
    local_ordinance_reference = NULL,
    lgu_code = NULL,
    jurisdiction_name = EXCLUDED.jurisdiction_name,
    site_group_id = NULL,
    site_id = NULL,
    parent_policy_reference_id = NULL,
    fallback_policy_reference_id = NULL,
    precedence_rank = EXCLUDED.precedence_rank,
    requires_operator_validation = EXCLUDED.requires_operator_validation,
    requires_evidence_capture = EXCLUDED.requires_evidence_capture,
    evidence_retention_policy_code = NULL,
    policy_status = EXCLUDED.policy_status,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

INSERT INTO discounts.discount_policy_references (
    discount_policy_reference_id,
    policy_code,
    policy_name,
    policy_description,
    policy_type,
    policy_level,
    entitlement_type,
    national_law_reference,
    local_ordinance_reference,
    lgu_code,
    jurisdiction_name,
    site_group_id,
    site_id,
    parent_policy_reference_id,
    fallback_policy_reference_id,
    precedence_rank,
    policy_version,
    requires_operator_validation,
    requires_evidence_capture,
    evidence_retention_policy_code,
    policy_status,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '12000000-0000-0000-0000-000000000a02',
    'DEV_LOCAL_ORDINANCE',
    'Development Local Ordinance Policy',
    'Development reference for local-ordinance policy resolution scoped to the reference site.',
    'LOCAL_ORDINANCE',
    'LOCAL_ORDINANCE',
    'SENIOR_CITIZEN',
    NULL,
    'DEV-ORDINANCE-001',
    'PH-00-DEV',
    'Development LGU',
    '12000000-0000-0000-0000-000000000301',
    '12000000-0000-0000-0000-000000000302',
    NULL,
    '12000000-0000-0000-0000-000000000a01',
    10,
    'v1.2-dev',
    true,
    true,
    NULL,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
)
ON CONFLICT ON CONSTRAINT uq_discount_policy_references__policy_code_version
DO UPDATE SET
    policy_name = EXCLUDED.policy_name,
    policy_description = EXCLUDED.policy_description,
    policy_type = EXCLUDED.policy_type,
    policy_level = EXCLUDED.policy_level,
    entitlement_type = EXCLUDED.entitlement_type,
    national_law_reference = NULL,
    local_ordinance_reference = EXCLUDED.local_ordinance_reference,
    lgu_code = EXCLUDED.lgu_code,
    jurisdiction_name = EXCLUDED.jurisdiction_name,
    site_group_id = EXCLUDED.site_group_id,
    site_id = EXCLUDED.site_id,
    parent_policy_reference_id = NULL,
    fallback_policy_reference_id = EXCLUDED.fallback_policy_reference_id,
    precedence_rank = EXCLUDED.precedence_rank,
    requires_operator_validation = EXCLUDED.requires_operator_validation,
    requires_evidence_capture = EXCLUDED.requires_evidence_capture,
    evidence_retention_policy_code = NULL,
    policy_status = EXCLUDED.policy_status,
    updated_at = now(),
    updated_by_service_identity_id = '12000000-0000-0000-0000-000000000001';

COMMIT;
