\set ON_ERROR_STOP on

BEGIN;

ALTER TABLE ist_configuration.site_operational_capabilities
    ADD COLUMN IF NOT EXISTS operator_entity_code text NULL,
    ADD COLUMN IF NOT EXISTS hikcentral_instance_code text NULL,
    ADD COLUMN IF NOT EXISTS hikcentral_parking_lot_index_code text NULL,
    ADD COLUMN IF NOT EXISTS hikcentral_parking_lot_name text NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_base_url text NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_environment_code text NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_secret_reference text NULL,
    ADD COLUMN IF NOT EXISTS central_pms_service_identity_id uuid NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_service_identity_id uuid NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_vendor_system_id uuid NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_credential_reference_id uuid NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_endpoint_id uuid NULL,
    ADD COLUMN IF NOT EXISTS site_adapter_mapping_id uuid NULL,
    ADD COLUMN IF NOT EXISTS pos_site_server_id uuid NULL,
    ADD COLUMN IF NOT EXISTS fiscal_identity_id uuid NULL,
    ADD COLUMN IF NOT EXISTS sales_invoice_profile_id uuid NULL;

CREATE OR REPLACE FUNCTION pg_temp.exitpass_operational_uuid(input text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
STRICT
AS $$
    SELECT (
        substr(md5(input), 1, 8) || '-' ||
        substr(md5(input), 9, 4) || '-' ||
        substr(md5(input), 13, 4) || '-' ||
        substr(md5(input), 17, 4) || '-' ||
        substr(md5(input), 21, 12)
    )::uuid
$$;

CREATE TEMP TABLE ep_ist_site_adapter_routes ON COMMIT DROP AS
SELECT input.site_id,
       input.site_code,
       site.site_group_id,
       input.site_adapter_base_url,
       upper(input.site_adapter_environment_code) AS environment_code,
       input.site_adapter_secret_reference,
       input.central_pms_service_identity_id,
       pg_temp.exitpass_operational_uuid('persistent-ist:site-adapter:identity:' || input.site_code) AS adapter_service_identity_id,
       pg_temp.exitpass_operational_uuid('persistent-ist:site-adapter:vendor-system:' || input.site_code) AS vendor_system_id,
       pg_temp.exitpass_operational_uuid('persistent-ist:site-adapter:credential:' || input.site_code) AS credential_reference_id,
       pg_temp.exitpass_operational_uuid('persistent-ist:site-adapter:endpoint:' || input.site_code) AS endpoint_id,
       pg_temp.exitpass_operational_uuid('persistent-ist:site-adapter:mapping:' || input.site_code) AS mapping_id,
       lower(regexp_replace(input.site_code, '[^a-zA-Z0-9]+', '-', 'g')) AS code_suffix,
       upper(regexp_replace(input.site_code, '[^a-zA-Z0-9]+', '_', 'g')) AS upper_code_suffix,
       coalesce(input.last_verified_at, now()) AS effective_from
FROM ep_ist_operational input
JOIN ist_configuration.real_site_catalog_members member USING (site_id, site_code)
JOIN sites.sites site USING (site_id)
WHERE input.hikcentral_target_configured;

DO $$
BEGIN
    IF (SELECT count(*) FROM ep_ist_operational) <> 46 THEN
        RAISE EXCEPTION 'Operational configuration requires exactly 46 real-Site rows.';
    END IF;
    IF EXISTS (SELECT 1 FROM ep_ist_operational GROUP BY site_id HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM ep_ist_operational GROUP BY site_code HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Operational configuration contains duplicate Site identity.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_operational input
        LEFT JOIN ist_configuration.real_site_catalog_members member
          ON member.site_id = input.site_id AND member.site_code = input.site_code
        WHERE member.site_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Operational configuration contains an unknown or conflicting Site identity.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM ep_ist_operational
        WHERE hikcentral_connectivity_verified AND NOT hikcentral_target_configured
           OR fiscal_profile_approved AND NOT (fiscal_merchant_configured AND fiscal_supplier_configured)
           OR fiscal_profile_approved AND (sales_invoice_profile_id IS NULL OR pos_site_server_id IS NULL OR fiscal_identity_id IS NULL)
           OR hikcentral_target_configured AND (
                hikcentral_instance_code IS NULL
                OR hikcentral_parking_lot_index_code IS NULL
                OR site_adapter_base_url IS NULL
                OR site_adapter_environment_code IS NULL
                OR site_adapter_secret_reference IS NULL
                OR central_pms_service_identity_id IS NULL)
           OR NOT hikcentral_target_configured AND (
                site_adapter_base_url IS NOT NULL
                OR site_adapter_environment_code IS NOT NULL
                OR site_adapter_secret_reference IS NOT NULL
                OR central_pms_service_identity_id IS NOT NULL)
    ) THEN
        RAISE EXCEPTION 'Operational configuration contains an invalid capability relationship.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM ep_ist_site_adapter_routes
        WHERE site_adapter_base_url !~* '^https?://[^[:space:]]+/?$'
           OR site_adapter_secret_reference !~* '^file:[^[:space:]]+$'
           OR environment_code = ''
           OR (site_adapter_base_url ~* '^http://' AND environment_code <> 'IST')
    ) THEN
        RAISE EXCEPTION 'Site Adapter route contains an invalid URL, environment, or mounted file credential reference.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_site_adapter_routes route
        LEFT JOIN identity.service_identities central
          ON central.service_identity_id = route.central_pms_service_identity_id
        WHERE central.service_identity_id IS NULL
           OR central.identity_status::text <> 'ACTIVE'
           OR central.identity_type::text <> 'INTERNAL_SERVICE'
    ) THEN
        RAISE EXCEPTION 'Site Adapter route must reference an existing active Central PMS service identity.';
    END IF;
    IF EXISTS (
        SELECT route.site_id
        FROM ep_ist_site_adapter_routes route
        JOIN sessions.vendor_session_projection_sync_targets target
          ON target.site_id = route.site_id
         AND target.enabled_flag
        GROUP BY route.site_id
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Configured HikCentral Site has multiple enabled projection targets.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_site_adapter_routes route
        JOIN sessions.vendor_session_projection_sync_targets target
          ON target.site_id = route.site_id
         AND target.enabled_flag
        WHERE target.site_group_id <> route.site_group_id
           OR target.parking_lot_index_code <> (
                SELECT input.hikcentral_parking_lot_index_code
                FROM ep_ist_operational input
                WHERE input.site_id = route.site_id)
    ) THEN
        RAISE EXCEPTION 'Enabled projection target conflicts with the canonical Site Group or parking lot.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_site_adapter_routes route
        JOIN sessions.vendor_session_projection_sync_targets target
          ON target.site_id = route.site_id
         AND target.enabled_flag
        JOIN sessions.vendor_session_projection_sync_targets conflict
          ON conflict.site_id = target.site_id
         AND conflict.vendor_system_id = route.vendor_system_id
         AND conflict.parking_lot_index_code = target.parking_lot_index_code
         AND conflict.projection_sync_target_id <> target.projection_sync_target_id
    ) THEN
        RAISE EXCEPTION 'Projection target route reconciliation conflicts with an existing target scope.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_site_adapter_routes route
        JOIN identity.service_identities existing
          ON existing.service_identity_code = 'ist-site-adapter-' || route.code_suffix
        WHERE existing.service_identity_id <> route.adapter_service_identity_id
    ) OR EXISTS (
        SELECT 1
        FROM ep_ist_site_adapter_routes route
        JOIN integration.vendor_systems existing
          ON existing.vendor_code = 'IST_SITE_ADAPTER_' || route.upper_code_suffix
         AND existing.environment_code = route.environment_code
        WHERE existing.vendor_system_id <> route.vendor_system_id
    ) THEN
        RAISE EXCEPTION 'Site Adapter route conflicts with an existing stable identity.';
    END IF;
END $$;

-- The APT payable-basis facade has a distinct read-only permission. Keep it
-- separate from terminal-cash.receive and provision it through SITE_OPERATOR.
DO $$
DECLARE
    expected_permission_id constant uuid := pg_temp.exitpass_operational_uuid(
        'persistent-ist:rbac:permission:terminal-cash.payable-basis.read');
    expected_binding_id constant uuid := pg_temp.exitpass_operational_uuid(
        'persistent-ist:rbac:role-permission:SITE_OPERATOR:terminal-cash.payable-basis.read');
BEGIN
    IF (SELECT count(*) FROM identity.roles
        WHERE role_code = 'SITE_OPERATOR' AND role_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Persistent APT RBAC requires exactly one active SITE_OPERATOR role.';
    END IF;
    IF (SELECT count(*) FROM identity.service_identities
        WHERE service_identity_code = 'seed.reference-data' AND identity_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Persistent APT RBAC requires exactly one active reference-data service identity.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM identity.permissions
        WHERE identity.permissions.permission_id = expected_permission_id
          AND permission_code <> 'terminal-cash.payable-basis.read'
    ) THEN
        RAISE EXCEPTION 'Persistent APT payable-basis permission ID conflicts with existing reference data.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions binding
        JOIN identity.roles role ON role.role_id = binding.role_id
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_permission_id = expected_binding_id
          AND (role.role_code <> 'SITE_OPERATOR'
               OR permission.permission_code <> 'terminal-cash.payable-basis.read')
    ) THEN
        RAISE EXCEPTION 'Persistent APT payable-basis binding ID conflicts with existing reference data.';
    END IF;
END $$;

INSERT INTO identity.permissions (
    permission_id, permission_code, permission_name, permission_description,
    permission_domain, permission_action, permission_status, is_sensitive,
    requires_audit, created_by_service_identity_id,
    updated_by_service_identity_id)
SELECT pg_temp.exitpass_operational_uuid(
           'persistent-ist:rbac:permission:terminal-cash.payable-basis.read'),
       'terminal-cash.payable-basis.read',
       'Read terminal cash payable basis',
       'Resolve and revalidate payable-basis readiness through the read-only APT facade. Does not authorize cash receipt or any payment, fiscal, exit, statutory, or gate mutation.',
       'terminal-cash', 'read', 'ACTIVE', false, true,
       service.service_identity_id, service.service_identity_id
FROM identity.service_identities service
WHERE service.service_identity_code = 'seed.reference-data'
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    permission_description = EXCLUDED.permission_description,
    permission_domain = EXCLUDED.permission_domain,
    permission_action = EXCLUDED.permission_action,
    permission_status = 'ACTIVE',
    is_sensitive = EXCLUDED.is_sensitive,
    requires_audit = EXCLUDED.requires_audit,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = identity.permissions.row_version + 1
WHERE (identity.permissions.permission_name,
       identity.permissions.permission_description,
       identity.permissions.permission_domain,
       identity.permissions.permission_action,
       identity.permissions.permission_status::text,
       identity.permissions.is_sensitive,
       identity.permissions.requires_audit)
  IS DISTINCT FROM
      (EXCLUDED.permission_name, EXCLUDED.permission_description,
       EXCLUDED.permission_domain, EXCLUDED.permission_action,
       'ACTIVE', EXCLUDED.is_sensitive, EXCLUDED.requires_audit);

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT pg_temp.exitpass_operational_uuid(
           'persistent-ist:rbac:role-permission:SITE_OPERATOR:terminal-cash.payable-basis.read'),
       role.role_id, permission.permission_id, 'ACTIVE',
       'PERSISTENT_IST_REFERENCE_DATA', service.service_identity_id, now(),
       service.service_identity_id, service.service_identity_id
FROM identity.roles role
JOIN identity.permissions permission
  ON permission.permission_code = 'terminal-cash.payable-basis.read'
JOIN identity.service_identities service
  ON service.service_identity_code = 'seed.reference-data'
WHERE role.role_code = 'SITE_OPERATOR'
  AND NOT EXISTS (
      SELECT 1
      FROM identity.role_permissions existing
      WHERE existing.role_id = role.role_id
        AND existing.permission_id = permission.permission_id
        AND existing.binding_status::text = 'ACTIVE'
  )
ON CONFLICT (role_permission_id) DO UPDATE
SET role_id = EXCLUDED.role_id,
    permission_id = EXCLUDED.permission_id,
    binding_status = 'ACTIVE',
    binding_reason_code = EXCLUDED.binding_reason_code,
    assigned_by_service_identity_id = EXCLUDED.assigned_by_service_identity_id,
    effective_from = EXCLUDED.effective_from,
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = identity.role_permissions.row_version + 1;

INSERT INTO identity.service_identities (
    service_identity_id, service_identity_code, service_identity_name,
    identity_type, identity_status, owning_service_name, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT route.adapter_service_identity_id,
       'ist-site-adapter-' || route.code_suffix,
       route.site_code || ' Site Adapter',
       'ADAPTER', 'ACTIVE', 'ExitPass.VendorPmsAdapter.Api', route.effective_from,
       route.central_pms_service_identity_id, route.central_pms_service_identity_id
FROM ep_ist_site_adapter_routes route
ON CONFLICT (service_identity_id) DO UPDATE
SET service_identity_name = EXCLUDED.service_identity_name,
    identity_status = 'ACTIVE',
    owning_service_name = EXCLUDED.owning_service_name,
    effective_to = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = identity.service_identities.row_version + 1
WHERE (identity.service_identities.service_identity_name,
       identity.service_identities.identity_status::text,
       identity.service_identities.owning_service_name,
       identity.service_identities.effective_to)
  IS DISTINCT FROM
      (EXCLUDED.service_identity_name, 'ACTIVE', EXCLUDED.owning_service_name, NULL);

INSERT INTO integration.vendor_systems (
    vendor_system_id, vendor_code, vendor_name, vendor_system_type,
    vendor_system_status, environment_code, base_url_ref, api_version,
    owner_team, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT route.vendor_system_id,
       'IST_SITE_ADAPTER_' || route.upper_code_suffix,
       route.site_code || ' Site Adapter Route',
       'VENDOR_PMS', 'ACTIVE', route.environment_code, route.site_adapter_base_url,
       'v1', 'ExitPass Engineering', route.effective_from,
       route.central_pms_service_identity_id, route.central_pms_service_identity_id
FROM ep_ist_site_adapter_routes route
ON CONFLICT (vendor_system_id) DO UPDATE
SET vendor_name = EXCLUDED.vendor_name,
    vendor_system_status = 'ACTIVE',
    environment_code = EXCLUDED.environment_code,
    base_url_ref = EXCLUDED.base_url_ref,
    effective_to = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = integration.vendor_systems.row_version + 1
WHERE (integration.vendor_systems.vendor_name,
       integration.vendor_systems.vendor_system_status::text,
       integration.vendor_systems.environment_code,
       integration.vendor_systems.base_url_ref,
       integration.vendor_systems.effective_to)
  IS DISTINCT FROM
      (EXCLUDED.vendor_name, 'ACTIVE', EXCLUDED.environment_code,
       EXCLUDED.base_url_ref, NULL);

INSERT INTO integration.integration_credential_references (
    integration_credential_reference_id, vendor_system_id, service_identity_id,
    credential_code, credential_name, credential_type, secret_store_type,
    secret_reference, credential_status, created_by_service_identity_id,
    updated_by_service_identity_id)
SELECT route.credential_reference_id, route.vendor_system_id,
       route.central_pms_service_identity_id,
       'IST_' || route.upper_code_suffix || '_CENTRAL_API',
       route.site_code || ' Central PMS to Site Adapter credential',
       'API_KEY_REFERENCE', 'OTHER', route.site_adapter_secret_reference, 'ACTIVE',
       route.central_pms_service_identity_id, route.central_pms_service_identity_id
FROM ep_ist_site_adapter_routes route
ON CONFLICT (integration_credential_reference_id) DO UPDATE
SET vendor_system_id = EXCLUDED.vendor_system_id,
    service_identity_id = EXCLUDED.service_identity_id,
    credential_name = EXCLUDED.credential_name,
    secret_reference = EXCLUDED.secret_reference,
    credential_status = 'ACTIVE',
    revoked_at = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = integration.integration_credential_references.row_version + 1
WHERE (integration.integration_credential_references.vendor_system_id,
       integration.integration_credential_references.service_identity_id,
       integration.integration_credential_references.credential_name,
       integration.integration_credential_references.secret_reference,
       integration.integration_credential_references.credential_status::text,
       integration.integration_credential_references.revoked_at,
       integration.integration_credential_references.revocation_reason_code)
  IS DISTINCT FROM
      (EXCLUDED.vendor_system_id, EXCLUDED.service_identity_id,
       EXCLUDED.credential_name, EXCLUDED.secret_reference, 'ACTIVE', NULL, NULL);

INSERT INTO integration.vendor_endpoints (
    vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name,
    endpoint_description, endpoint_type, path_template, operation_ref,
    credential_reference_id, endpoint_status, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT route.endpoint_id, route.vendor_system_id, 'SITE_ADAPTER_API',
       route.site_code || ' provider-neutral Site Adapter API',
       'Central PMS provider-neutral Site Adapter root; not the upstream HikCentral URL.',
       'OTHER', '/', 'SITE_ADAPTER_API', route.credential_reference_id,
       'ACTIVE', route.effective_from,
       route.central_pms_service_identity_id, route.central_pms_service_identity_id
FROM ep_ist_site_adapter_routes route
ON CONFLICT (vendor_endpoint_id) DO UPDATE
SET vendor_system_id = EXCLUDED.vendor_system_id,
    endpoint_name = EXCLUDED.endpoint_name,
    endpoint_description = EXCLUDED.endpoint_description,
    credential_reference_id = EXCLUDED.credential_reference_id,
    endpoint_status = 'ACTIVE',
    effective_to = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = integration.vendor_endpoints.row_version + 1
WHERE (integration.vendor_endpoints.vendor_system_id,
       integration.vendor_endpoints.endpoint_name,
       integration.vendor_endpoints.endpoint_description,
       integration.vendor_endpoints.credential_reference_id,
       integration.vendor_endpoints.endpoint_status::text,
       integration.vendor_endpoints.effective_to)
  IS DISTINCT FROM
      (EXCLUDED.vendor_system_id, EXCLUDED.endpoint_name,
       EXCLUDED.endpoint_description, EXCLUDED.credential_reference_id,
       'ACTIVE', NULL);

INSERT INTO integration.adapter_mappings (
    adapter_mapping_id, vendor_system_id, mapping_type, site_group_id, site_id,
    vendor_object_type, vendor_object_ref, vendor_object_name, mapping_status,
    mapping_confidence, effective_from, created_by_service_identity_id,
    updated_by_service_identity_id)
SELECT route.mapping_id, route.vendor_system_id, 'SITE', route.site_group_id, route.site_id,
       'SITE_ADAPTER', route.adapter_service_identity_id::text,
       route.site_code || ' Site Adapter', 'ACTIVE', 'IMPORTED_APPROVED',
       route.effective_from, route.central_pms_service_identity_id,
       route.central_pms_service_identity_id
FROM ep_ist_site_adapter_routes route
ON CONFLICT (adapter_mapping_id) DO UPDATE
SET vendor_system_id = EXCLUDED.vendor_system_id,
    site_group_id = EXCLUDED.site_group_id,
    site_id = EXCLUDED.site_id,
    vendor_object_ref = EXCLUDED.vendor_object_ref,
    vendor_object_name = EXCLUDED.vendor_object_name,
    mapping_status = 'ACTIVE',
    mapping_confidence = EXCLUDED.mapping_confidence,
    effective_to = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = integration.adapter_mappings.row_version + 1
WHERE (integration.adapter_mappings.vendor_system_id,
       integration.adapter_mappings.site_group_id,
       integration.adapter_mappings.site_id,
       integration.adapter_mappings.vendor_object_ref,
       integration.adapter_mappings.vendor_object_name,
       integration.adapter_mappings.mapping_status::text,
       integration.adapter_mappings.mapping_confidence::text,
       integration.adapter_mappings.effective_to)
  IS DISTINCT FROM
      (EXCLUDED.vendor_system_id, EXCLUDED.site_group_id, EXCLUDED.site_id,
       EXCLUDED.vendor_object_ref, EXCLUDED.vendor_object_name, 'ACTIVE',
       EXCLUDED.mapping_confidence::text, NULL);

-- An existing enabled projection target declares that projection is required for the Site.
-- Preserve its identity and operational history while aligning its hard route selector.
-- PITX uses a 30-second poll so the 15-second managed scheduler scan remains inside
-- the existing one-minute freshness requirement.
UPDATE sessions.vendor_session_projection_sync_targets target
SET vendor_system_id = route.vendor_system_id,
    poll_interval_seconds = CASE
        WHEN target.projection_sync_target_id = 'e244a8af-0e30-7db1-3621-ad883ae3542c'::uuid
         AND route.site_code = 'PITX-LEVEL-3' THEN 30
        ELSE target.poll_interval_seconds
    END,
    updated_at = now(),
    row_version = target.row_version + 1
FROM ep_ist_site_adapter_routes route
JOIN ep_ist_operational input USING (site_id, site_code)
WHERE target.site_id = route.site_id
  AND target.site_group_id = route.site_group_id
  AND target.parking_lot_index_code = input.hikcentral_parking_lot_index_code
  AND target.enabled_flag
  AND (target.vendor_system_id IS DISTINCT FROM route.vendor_system_id
       OR (target.projection_sync_target_id = 'e244a8af-0e30-7db1-3621-ad883ae3542c'::uuid
           AND route.site_code = 'PITX-LEVEL-3'
           AND target.poll_interval_seconds IS DISTINCT FROM 30))
  AND (SELECT count(*)
       FROM sessions.vendor_session_projection_sync_targets enabled
       WHERE enabled.site_id = route.site_id
         AND enabled.enabled_flag) = 1;

UPDATE sites.sites site
SET public_lookup_enabled = input.webpay_public_lookup_enabled,
    payment_enabled = input.webpay_payment_enabled,
    updated_at = now(),
    row_version = site.row_version + 1
FROM ep_ist_operational input
WHERE site.site_id = input.site_id
  AND (site.public_lookup_enabled IS DISTINCT FROM input.webpay_public_lookup_enabled
       OR site.payment_enabled IS DISTINCT FROM input.webpay_payment_enabled);

UPDATE ist_configuration.site_operational_capabilities capability
SET operator_entity_code = input.operator_entity_code,
    hikcentral_instance_code = input.hikcentral_instance_code,
    hikcentral_parking_lot_index_code = input.hikcentral_parking_lot_index_code,
    hikcentral_parking_lot_name = input.hikcentral_parking_lot_name,
    hikcentral_target_configured = input.hikcentral_target_configured,
    hikcentral_connectivity_verified = input.hikcentral_connectivity_verified,
    site_adapter_base_url = input.site_adapter_base_url,
    site_adapter_environment_code = upper(input.site_adapter_environment_code),
    site_adapter_secret_reference = input.site_adapter_secret_reference,
    central_pms_service_identity_id = input.central_pms_service_identity_id,
    site_adapter_service_identity_id = route.adapter_service_identity_id,
    site_adapter_vendor_system_id = route.vendor_system_id,
    site_adapter_credential_reference_id = route.credential_reference_id,
    site_adapter_endpoint_id = route.endpoint_id,
    site_adapter_mapping_id = route.mapping_id,
    fiscal_merchant_configured = input.fiscal_merchant_configured,
    fiscal_supplier_configured = input.fiscal_supplier_configured,
    fiscal_profile_approved = input.fiscal_profile_approved,
    paymongo_enabled = input.paymongo_enabled,
    pos_site_server_id = input.pos_site_server_id,
    fiscal_identity_id = input.fiscal_identity_id,
    sales_invoice_profile_id = input.sales_invoice_profile_id,
    last_verified_at = input.last_verified_at,
    verification_reference = input.verification_reference
FROM ep_ist_operational input
LEFT JOIN ep_ist_site_adapter_routes route USING (site_id, site_code)
WHERE capability.site_id = input.site_id;

CREATE OR REPLACE VIEW ist_configuration.effective_site_adapter_routes AS
SELECT capability.site_id,
       mapping.site_group_id,
       vendor.vendor_system_id,
       endpoint.vendor_endpoint_id,
       mapping.adapter_mapping_id,
       credential.integration_credential_reference_id,
       adapter.service_identity_id AS adapter_service_identity_id,
       credential.service_identity_id AS central_pms_service_identity_id,
       vendor.base_url_ref,
       vendor.environment_code,
       credential.secret_reference
FROM ist_configuration.site_operational_capabilities capability
JOIN integration.adapter_mappings mapping
  ON mapping.site_id = capability.site_id
 AND mapping.vendor_object_type = 'SITE_ADAPTER'
JOIN integration.vendor_systems vendor
  ON vendor.vendor_system_id = mapping.vendor_system_id
JOIN integration.vendor_endpoints endpoint
  ON endpoint.vendor_system_id = vendor.vendor_system_id
 AND endpoint.endpoint_code = 'SITE_ADAPTER_API'
JOIN integration.integration_credential_references credential
  ON credential.integration_credential_reference_id = endpoint.credential_reference_id
JOIN identity.service_identities adapter
  ON adapter.service_identity_id = CASE
       WHEN mapping.vendor_object_ref ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
       THEN mapping.vendor_object_ref::uuid
       ELSE NULL
     END
WHERE mapping.site_group_id = (SELECT site_group_id FROM sites.sites WHERE site_id = capability.site_id)
  AND mapping.mapping_status::text = 'ACTIVE'
  AND vendor.vendor_system_status::text = 'ACTIVE'
  AND endpoint.endpoint_status::text = 'ACTIVE'
  AND credential.credential_status::text = 'ACTIVE'
  AND credential.service_identity_id = capability.central_pms_service_identity_id
  AND adapter.identity_status::text = 'ACTIVE'
  AND vendor.environment_code = capability.site_adapter_environment_code
  AND vendor.base_url_ref ~* '^https?://[^[:space:]]+/?$'
  AND credential.secret_reference ~* '^file:[^[:space:]]+$'
  AND vendor.effective_from <= now()
  AND (vendor.effective_to IS NULL OR vendor.effective_to > now())
  AND endpoint.effective_from <= now()
  AND (endpoint.effective_to IS NULL OR endpoint.effective_to > now())
  AND mapping.effective_from <= now()
  AND (mapping.effective_to IS NULL OR mapping.effective_to > now())
  AND credential.revoked_at IS NULL
  AND (credential.expires_at IS NULL OR credential.expires_at > now());

CREATE OR REPLACE VIEW ist_configuration.site_adapter_route_readiness AS
SELECT capability.site_id,
       count(route.vendor_system_id) AS effective_route_count,
       count(route.vendor_system_id) = 1
       AND min(route.vendor_system_id::text)::uuid = capability.site_adapter_vendor_system_id
       AND min(route.vendor_endpoint_id::text)::uuid = capability.site_adapter_endpoint_id
       AND min(route.adapter_mapping_id::text)::uuid = capability.site_adapter_mapping_id
       AND min(route.integration_credential_reference_id::text)::uuid = capability.site_adapter_credential_reference_id
       AND min(route.adapter_service_identity_id::text)::uuid = capability.site_adapter_service_identity_id
       AND min(route.central_pms_service_identity_id::text)::uuid = capability.central_pms_service_identity_id
       AND min(route.base_url_ref) = capability.site_adapter_base_url
       AND min(route.environment_code) = capability.site_adapter_environment_code
       AND min(route.secret_reference) = capability.site_adapter_secret_reference
         AS site_adapter_route_ready,
       min(route.vendor_system_id::text)::uuid AS vendor_system_id,
       min(route.vendor_endpoint_id::text)::uuid AS endpoint_id,
       min(route.adapter_mapping_id::text)::uuid AS mapping_id,
       min(route.integration_credential_reference_id::text)::uuid AS credential_reference_id,
       min(route.adapter_service_identity_id::text)::uuid AS adapter_service_identity_id,
       min(route.central_pms_service_identity_id::text)::uuid AS central_pms_service_identity_id,
       min(route.base_url_ref) AS base_url,
       min(route.environment_code) AS environment_code
FROM ist_configuration.site_operational_capabilities capability
LEFT JOIN ist_configuration.effective_site_adapter_routes route USING (site_id)
GROUP BY capability.site_id,
         capability.site_adapter_vendor_system_id,
         capability.site_adapter_endpoint_id,
         capability.site_adapter_mapping_id,
         capability.site_adapter_credential_reference_id,
         capability.site_adapter_service_identity_id,
         capability.central_pms_service_identity_id,
         capability.site_adapter_base_url,
         capability.site_adapter_environment_code,
         capability.site_adapter_secret_reference;

CREATE OR REPLACE VIEW ist_configuration.projection_target_route_readiness AS
SELECT capability.site_id,
       count(target.projection_sync_target_id) FILTER (WHERE target.enabled_flag) > 0
         AS projection_target_required,
       count(target.projection_sync_target_id) FILTER (WHERE target.enabled_flag)
         AS enabled_projection_target_count,
       CASE
         WHEN count(target.projection_sync_target_id) FILTER (WHERE target.enabled_flag) = 0 THEN true
         WHEN count(target.projection_sync_target_id) FILTER (WHERE target.enabled_flag) = 1 THEN
              coalesce(
                   bool_and(target.site_group_id = site.site_group_id) FILTER (WHERE target.enabled_flag)
               AND bool_and(target.vendor_system_id = route.vendor_system_id) FILTER (WHERE target.enabled_flag)
               AND bool_and(target.parking_lot_index_code = capability.hikcentral_parking_lot_index_code)
                     FILTER (WHERE target.enabled_flag)
               AND route.site_adapter_route_ready,
               false)
         ELSE false
       END AS projection_target_route_aligned,
       (min(target.projection_sync_target_id::text) FILTER (WHERE target.enabled_flag))::uuid
         AS projection_sync_target_id,
       (min(target.vendor_system_id::text) FILTER (WHERE target.enabled_flag))::uuid
         AS projection_target_vendor_system_id,
       min(target.parking_lot_index_code) FILTER (WHERE target.enabled_flag)
         AS projection_target_parking_lot_index_code,
       min(target.health_status) FILTER (WHERE target.enabled_flag) AS projection_target_health_status,
       max(target.last_success_at) FILTER (WHERE target.enabled_flag) AS projection_target_last_success_at,
       CASE
         WHEN count(target.projection_sync_target_id) FILTER (WHERE target.enabled_flag) = 0 THEN NULL
         WHEN count(target.projection_sync_target_id) FILTER (WHERE target.enabled_flag) = 1 THEN
              bool_and(target.health_status = 'HEALTHY'
                       AND target.last_success_at IS NOT NULL
                       AND target.last_success_at >= target.updated_at)
                FILTER (WHERE target.enabled_flag)
         ELSE false
       END AS projection_target_runtime_healthy
FROM ist_configuration.site_operational_capabilities capability
JOIN sites.sites site USING (site_id)
JOIN ist_configuration.site_adapter_route_readiness route USING (site_id)
LEFT JOIN sessions.vendor_session_projection_sync_targets target USING (site_id)
GROUP BY capability.site_id,
         capability.hikcentral_parking_lot_index_code,
         site.site_group_id,
         route.vendor_system_id,
         route.site_adapter_route_ready;

CREATE OR REPLACE VIEW ist_configuration.real_site_readiness AS
WITH policy AS (
    SELECT jurisdiction_id,
           max(CASE WHEN entitlement_type = 'SENIOR_CITIZEN' THEN
               CASE WHEN operational_verification_status = 'VERIFIED_ACTIVE_OPERATIONAL' THEN 'ACTIVE_MANUAL_REVIEW'
                    WHEN parking_policy_identified THEN 'RESEARCH_COVERAGE_REVIEW_REQUIRED'
                    ELSE 'NO_LOCAL_POLICY_IDENTIFIED' END END) AS senior_policy_status,
           max(CASE WHEN entitlement_type = 'PWD' THEN
               CASE WHEN operational_verification_status = 'VERIFIED_ACTIVE_OPERATIONAL' THEN 'ACTIVE_MANUAL_REVIEW'
                    WHEN parking_policy_identified THEN 'RESEARCH_COVERAGE_REVIEW_REQUIRED'
                    ELSE 'NO_LOCAL_POLICY_IDENTIFIED' END END) AS pwd_policy_status
    FROM ist_configuration.statutory_coverage_register
    GROUP BY jurisdiction_id
)
SELECT member.site_id,
       member.site_code,
       site.site_name,
       site.site_group_id,
       site_group.site_group_name,
       jurisdiction.jurisdiction_code,
       jurisdiction.display_name AS jurisdiction,
       site.site_status = 'ACTIVE' AS site_exists_active,
       site_group.site_group_status = 'ACTIVE' AS site_group_exists_active,
       assignment.assignment_status = 'ACTIVE' AS jurisdiction_active,
       policy.senior_policy_status,
       policy.pwd_policy_status,
       capability.hikcentral_target_configured,
       capability.hikcentral_connectivity_verified,
       site.public_lookup_enabled AS webpay_public_lookup_enabled,
       site.payment_enabled AS webpay_payment_enabled,
       capability.fiscal_merchant_configured,
       capability.fiscal_supplier_configured,
       capability.fiscal_profile_approved,
       capability.paymongo_enabled,
       CASE
         WHEN site.site_status = 'ACTIVE'
          AND site_group.site_group_status = 'ACTIVE'
          AND assignment.assignment_status = 'ACTIVE'
           AND capability.hikcentral_connectivity_verified
           AND route.site_adapter_route_ready
           AND projection.projection_target_route_aligned
           AND site.public_lookup_enabled
          AND site.payment_enabled
          AND capability.fiscal_profile_approved
          AND capability.paymongo_enabled THEN 'READY'
         WHEN site.site_status = 'ACTIVE'
          AND site_group.site_group_status = 'ACTIVE'
          AND assignment.assignment_status = 'ACTIVE' THEN 'PARTIALLY_CONFIGURED'
         ELSE 'CONFIGURATION_REQUIRED'
       END AS final_test_readiness,
       route.effective_route_count AS site_adapter_route_count,
       route.site_adapter_route_ready,
       projection.projection_target_required,
       projection.enabled_projection_target_count,
       projection.projection_target_route_aligned,
       projection.projection_sync_target_id,
       projection.projection_target_vendor_system_id,
       projection.projection_target_parking_lot_index_code,
       projection.projection_target_health_status,
       projection.projection_target_last_success_at,
       projection.projection_target_runtime_healthy
FROM ist_configuration.real_site_catalog_members member
JOIN sites.sites site ON site.site_id = member.site_id
JOIN sites.site_groups site_group ON site_group.site_group_id = site.site_group_id
JOIN sites.site_jurisdiction_assignments assignment
  ON assignment.site_id = site.site_id
 AND assignment.assignment_status = 'ACTIVE'
 AND assignment.effective_to IS NULL
JOIN sites.jurisdictions jurisdiction ON jurisdiction.jurisdiction_id = assignment.jurisdiction_id
JOIN ist_configuration.site_operational_capabilities capability ON capability.site_id = site.site_id
JOIN ist_configuration.site_adapter_route_readiness route ON route.site_id = site.site_id
JOIN ist_configuration.projection_target_route_readiness projection ON projection.site_id = site.site_id
LEFT JOIN policy ON policy.jurisdiction_id = jurisdiction.jurisdiction_id;

CREATE OR REPLACE VIEW ist_configuration.real_site_operational_readiness AS
SELECT readiness.site_id,
       readiness.site_code,
       readiness.site_name,
       readiness.site_group_id,
       readiness.site_group_name,
       readiness.jurisdiction,
       readiness.senior_policy_status,
       readiness.pwd_policy_status,
       capability.operator_entity_code,
       capability.hikcentral_instance_code,
       capability.hikcentral_target_configured,
       capability.hikcentral_connectivity_verified,
       capability.hikcentral_parking_lot_index_code,
       capability.hikcentral_parking_lot_name,
       readiness.webpay_public_lookup_enabled,
       readiness.webpay_payment_enabled,
       capability.fiscal_merchant_configured,
       capability.fiscal_supplier_configured,
       capability.fiscal_profile_approved,
       capability.pos_site_server_id,
       capability.fiscal_identity_id,
       capability.sales_invoice_profile_id,
       capability.paymongo_enabled,
       readiness.final_test_readiness,
       capability.last_verified_at,
       capability.verification_reference,
       readiness.site_adapter_route_count,
       readiness.site_adapter_route_ready,
       capability.site_adapter_base_url,
       capability.site_adapter_environment_code,
       capability.central_pms_service_identity_id,
       capability.site_adapter_service_identity_id,
       capability.site_adapter_vendor_system_id,
       capability.site_adapter_credential_reference_id,
       capability.site_adapter_endpoint_id,
       capability.site_adapter_mapping_id,
       readiness.projection_target_required,
       readiness.enabled_projection_target_count,
       readiness.projection_target_route_aligned,
       readiness.projection_sync_target_id,
       readiness.projection_target_vendor_system_id,
       readiness.projection_target_parking_lot_index_code,
       readiness.projection_target_health_status,
       readiness.projection_target_last_success_at,
       readiness.projection_target_runtime_healthy
FROM ist_configuration.real_site_readiness readiness
JOIN ist_configuration.site_operational_capabilities capability USING (site_id);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ep_ist_operational input
        JOIN ist_configuration.site_adapter_route_readiness route USING (site_id)
        WHERE input.hikcentral_target_configured AND route.effective_route_count <> 1
    ) THEN
        RAISE EXCEPTION 'Configured HikCentral Site does not resolve exactly one canonical SITE_ADAPTER_API route.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_operational input
        JOIN ist_configuration.projection_target_route_readiness projection USING (site_id)
        WHERE input.hikcentral_target_configured
          AND projection.projection_target_required
          AND NOT projection.projection_target_route_aligned
    ) THEN
        RAISE EXCEPTION 'Enabled projection target is not aligned with the canonical Site Adapter route.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_operational input
        JOIN ist_configuration.real_site_readiness readiness USING (site_id)
        WHERE input.hikcentral_target_configured
          AND (NOT readiness.site_adapter_route_ready OR readiness.final_test_readiness <> 'READY')
          AND input.hikcentral_connectivity_verified
          AND input.webpay_public_lookup_enabled
          AND input.webpay_payment_enabled
          AND input.fiscal_profile_approved
          AND input.paymongo_enabled
    ) THEN
        RAISE EXCEPTION 'Operational readiness failed closed for a fully configured Site Adapter route.';
    END IF;
END $$;

COMMIT;
