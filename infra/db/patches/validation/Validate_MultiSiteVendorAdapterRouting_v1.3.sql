BEGIN TRANSACTION READ ONLY;

DO $validation$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='sessions'
        AND table_name='vendor_session_projections' AND column_name='source_adapter_identity_id') OR
       NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='core'
        AND table_name='parking_sessions' AND column_name='source_adapter_identity_id') OR
       NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='core'
        AND table_name='tariff_snapshots' AND column_name='source_adapter_identity_id') THEN
        RAISE EXCEPTION 'MULTI_SITE_ADAPTER_IDENTITY_COLUMNS_MISSING';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM integration.adapter_mappings am
         WHERE am.vendor_object_type = 'SITE_ADAPTER'
           AND am.mapping_status::text = 'ACTIVE'
           AND am.site_id IS NOT NULL
           AND am.effective_from <= now()
           AND (am.effective_to IS NULL OR am.effective_to > now())
         GROUP BY am.site_id, am.vendor_system_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'MULTIPLE_ACTIVE_SITE_ADAPTER_MAPPINGS';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM integration.adapter_mappings am
         WHERE am.vendor_object_type = 'SITE_ADAPTER'
           AND (am.site_id IS NULL OR am.site_group_id IS NULL OR
                am.vendor_object_ref !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' OR
                NOT EXISTS (
                    SELECT 1 FROM identity.service_identities si
                     WHERE si.service_identity_id = CASE
                         WHEN am.vendor_object_ref ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                         THEN am.vendor_object_ref::uuid
                         ELSE NULL
                     END
                       AND si.identity_status::text = 'ACTIVE'))
    ) THEN
        RAISE EXCEPTION 'SITE_ADAPTER_MAPPING_IDENTITY_INVALID';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM integration.vendor_systems vs
          JOIN integration.adapter_mappings am ON am.vendor_system_id = vs.vendor_system_id
         WHERE am.vendor_object_type = 'SITE_ADAPTER'
           AND am.mapping_status::text = 'ACTIVE'
           AND (vs.base_url_ref IS NULL OR vs.base_url_ref !~* '^https://')
           AND vs.environment_code NOT IN ('DEV', 'TEST', 'LOCAL_UAT')
    ) THEN
        RAISE EXCEPTION 'SITE_ADAPTER_PRIVATE_TLS_REQUIRED';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM integration.vendor_systems vs
          JOIN integration.adapter_mappings am ON am.vendor_system_id = vs.vendor_system_id
         WHERE am.vendor_object_type = 'SITE_ADAPTER'
           AND am.mapping_status::text = 'ACTIVE'
           AND NOT EXISTS (
               SELECT 1
                 FROM integration.vendor_endpoints ve
                 JOIN integration.integration_credential_references cr
                   ON cr.integration_credential_reference_id = ve.credential_reference_id
                WHERE ve.vendor_system_id = vs.vendor_system_id
                  AND ve.endpoint_code = 'SITE_ADAPTER_API'
                  AND ve.endpoint_status::text = 'ACTIVE'
                  AND cr.credential_status::text = 'ACTIVE'
                  AND cr.secret_reference LIKE 'file:%')
    ) THEN
        RAISE EXCEPTION 'SITE_ADAPTER_ENDPOINT_OR_CREDENTIAL_INVALID';
    END IF;
END
$validation$;

SELECT 'MULTI_SITE_VENDOR_ADAPTER_ROUTING_VALID' AS validation_result;
ROLLBACK;
