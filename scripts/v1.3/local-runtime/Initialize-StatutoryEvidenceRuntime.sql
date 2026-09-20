\set ON_ERROR_STOP on

BEGIN;

SELECT pg_advisory_xact_lock(hashtextextended('exitpass:persistent-pitx:statutory-evidence-governance', 0));

DO $governance$
DECLARE
    v_actor_service_identity_id uuid;
    v_payment_orchestrator_service_identity_id uuid;
    v_site_id uuid;
    v_site_group_id uuid;
    v_active_grant_count integer;
BEGIN
    IF current_database() <> 'exitpass_ist' THEN
        RAISE EXCEPTION 'Persistent PITX statutory-evidence governance may only be initialized in exitpass_ist.';
    END IF;

    SELECT service_identity_id
    INTO STRICT v_actor_service_identity_id
    FROM identity.service_identities
    WHERE service_identity_code = 'seed.reference-data'
      AND identity_status = 'ACTIVE';

    SELECT service_identity_id
    INTO STRICT v_payment_orchestrator_service_identity_id
    FROM identity.service_identities
    WHERE service_identity_code = 'payment-orchestrator'
      AND identity_status = 'ACTIVE';

    SELECT site_id, site_group_id
    INTO STRICT v_site_id, v_site_group_id
    FROM sites.sites
    WHERE site_code = 'PITX-LEVEL-3'
      AND site_status = 'ACTIVE';

    INSERT INTO discounts.statutory_evidence_retention_policies (
        retention_class_code,
        retention_policy_version,
        policy_status,
        environment_scope,
        purpose_code,
        effective_from,
        effective_to,
        created_by_service_identity_id,
        updated_by_service_identity_id)
    VALUES (
        'PITX_STATUTORY_EVIDENCE_LOCAL_REVIEW',
        '1',
        'APPROVED_ENABLED',
        'LOCAL_TEST',
        'STATUTORY_DISCOUNT_REVIEW',
        TIMESTAMPTZ '2026-09-01 00:00:00+00',
        NULL,
        v_actor_service_identity_id,
        v_actor_service_identity_id)
    ON CONFLICT (retention_class_code, retention_policy_version) DO UPDATE
    SET policy_status = EXCLUDED.policy_status,
        environment_scope = EXCLUDED.environment_scope,
        purpose_code = EXCLUDED.purpose_code,
        effective_from = EXCLUDED.effective_from,
        effective_to = EXCLUDED.effective_to,
        updated_at = now(),
        updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
        row_version = statutory_evidence_retention_policies.row_version + 1
    WHERE statutory_evidence_retention_policies.policy_status IS DISTINCT FROM EXCLUDED.policy_status
       OR statutory_evidence_retention_policies.environment_scope IS DISTINCT FROM EXCLUDED.environment_scope
       OR statutory_evidence_retention_policies.purpose_code IS DISTINCT FROM EXCLUDED.purpose_code
       OR statutory_evidence_retention_policies.effective_from IS DISTINCT FROM EXCLUDED.effective_from
       OR statutory_evidence_retention_policies.effective_to IS DISTINCT FROM EXCLUDED.effective_to;

    SELECT count(*)
    INTO v_active_grant_count
    FROM discounts.statutory_evidence_principal_scope_grants
    WHERE actor_service_identity_id = v_payment_orchestrator_service_identity_id
      AND source_channel = 'WEBPAY'
      AND site_id = v_site_id
      AND site_group_id = v_site_group_id
      AND grant_status = 'ACTIVE'
      AND effective_from <= now()
      AND (effective_to IS NULL OR effective_to > now());

    IF v_active_grant_count = 0 THEN
        INSERT INTO discounts.statutory_evidence_principal_scope_grants (
            statutory_evidence_principal_scope_grant_id,
            actor_service_identity_id,
            source_channel,
            site_id,
            site_group_id,
            capture_allowed,
            view_allowed,
            review_lock_allowed,
            hold_allowed,
            deletion_request_allowed,
            grant_status,
            reason_code,
            effective_from,
            created_by_service_identity_id,
            updated_by_service_identity_id)
        VALUES (
            gen_random_uuid(),
            v_payment_orchestrator_service_identity_id,
            'WEBPAY',
            v_site_id,
            v_site_group_id,
            true,
            false,
            false,
            false,
            false,
            'ACTIVE',
            'PERSISTENT_PITX_STATUTORY_EVIDENCE_CAPTURE',
            TIMESTAMPTZ '2026-09-01 00:00:00+00',
            v_actor_service_identity_id,
            v_actor_service_identity_id);
    ELSIF v_active_grant_count <> 1 OR NOT EXISTS (
        SELECT 1
        FROM discounts.statutory_evidence_principal_scope_grants
        WHERE actor_service_identity_id = v_payment_orchestrator_service_identity_id
          AND source_channel = 'WEBPAY'
          AND site_id = v_site_id
          AND site_group_id = v_site_group_id
          AND grant_status = 'ACTIVE'
          AND effective_from <= now()
          AND (effective_to IS NULL OR effective_to > now())
          AND capture_allowed
    ) THEN
        RAISE EXCEPTION 'The persistent PITX WebPay evidence scope grant is ambiguous or does not permit capture.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM discounts.statutory_evidence_retention_policies
        WHERE retention_class_code = 'PITX_STATUTORY_EVIDENCE_LOCAL_REVIEW'
          AND retention_policy_version = '1'
          AND policy_status = 'APPROVED_ENABLED'
          AND environment_scope = 'LOCAL_TEST'
          AND purpose_code = 'STATUTORY_DISCOUNT_REVIEW'
          AND effective_from <= now()
          AND effective_to IS NULL
    ) THEN
        RAISE EXCEPTION 'The persistent PITX LOCAL_TEST statutory-evidence retention policy is not usable.';
    END IF;
END
$governance$;

COMMIT;

SELECT retention_class_code,
       retention_policy_version,
       policy_status,
       environment_scope,
       purpose_code
FROM discounts.statutory_evidence_retention_policies
WHERE retention_class_code = 'PITX_STATUTORY_EVIDENCE_LOCAL_REVIEW'
  AND retention_policy_version = '1';
