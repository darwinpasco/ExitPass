-- Read-only lifecycle verification for the WebPay statutory-discount local walkthrough.
-- Run only after completing the manual API/browser sequence in the runbook.
-- The report intentionally omits evidence bytes, storage locators, checksums,
-- provider checkout URLs, credential material, and customer identity values.

\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

\echo '=== Fixture, policy, and reviewer authority ==='
WITH fixture AS (
    SELECT
        'E2E-231-SESSION-001'::text AS ticket_reference,
        (SELECT user_id FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer') AS reviewer_user_id,
        (SELECT site_id FROM sites.sites WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE') AS site_id,
        (SELECT site_group_id FROM sites.site_groups WHERE site_group_code = 'SANDBOX_OC_SD_PILOT_GROUP') AS site_group_id
), required_permissions(permission_code) AS (
    VALUES
        ('statutory-discounts.review.queue.read'),
        ('statutory-discounts.review.detail.read'),
        ('statutory-discounts.decision.review'),
        ('statutory-discounts.decision.approve'),
        ('statutory-discounts.decision.reject'),
        ('statutory-discounts.evidence.review.view')
)
SELECT
    ps.parking_session_id,
    ps.ticket_number_masked AS ticket_reference,
    ps.site_id,
    ps.site_group_id,
    count(DISTINCT rp.permission_id) FILTER (
        WHERE p.permission_code IN (SELECT permission_code FROM required_permissions)
          AND p.permission_status::text = 'ACTIVE'
          AND rp.binding_status::text = 'ACTIVE'
    ) = 6 AS reviewer_permission_bundle_present,
    bool_or(g.scope_type::text = 'SITE' AND g.site_id = f.site_id) AS reviewer_site_scope_present,
    bool_or(g.scope_type::text = 'SITE_GROUP' AND g.site_group_id = f.site_group_id) AS reviewer_site_group_scope_present,
    count(*) FILTER (WHERE g.scope_type::text = 'GLOBAL') = 0 AS reviewer_has_no_global_scope
FROM fixture f
JOIN core.parking_sessions ps ON ps.ticket_number_masked = f.ticket_reference
JOIN identity.users u ON u.user_id = f.reviewer_user_id
JOIN identity.user_roles ur
  ON ur.user_id = u.user_id
 AND ur.assignment_status::text = 'ACTIVE'
 AND ur.effective_from <= now()
 AND (ur.effective_to IS NULL OR ur.effective_to > now())
JOIN identity.roles r ON r.role_id = ur.role_id AND r.role_code = 'OPERATIONS_SUPERVISOR'
JOIN identity.role_permissions rp ON rp.role_id = r.role_id
JOIN identity.permissions p ON p.permission_id = rp.permission_id
JOIN identity.user_role_scope_grants g
  ON g.user_role_id = ur.user_role_id
 AND g.grant_status::text = 'ACTIVE'
 AND g.effective_from <= now()
 AND (g.effective_to IS NULL OR g.effective_to > now())
GROUP BY ps.parking_session_id, ps.ticket_number_masked, ps.site_id, ps.site_group_id;

SELECT
    j.jurisdiction_code,
    p.policy_code,
    p.policy_status,
    p.verification_status,
    v.policy_version,
    v.source_verification_status,
    v.transaction_publication_status,
    v.parking_service_applicability,
    e.evidence_type,
    e.requirement_status
FROM sites.site_jurisdiction_assignments a
JOIN sites.jurisdictions j ON j.jurisdiction_id = a.jurisdiction_id
JOIN discounts.statutory_discount_policy_registry p ON p.jurisdiction_id = j.jurisdiction_id
JOIN discounts.statutory_discount_policy_versions v
  ON v.statutory_discount_policy_registry_id = p.statutory_discount_policy_registry_id
LEFT JOIN discounts.statutory_discount_policy_version_evidence_requirements e
  ON e.statutory_discount_policy_version_id = v.statutory_discount_policy_version_id
WHERE a.site_id = (SELECT site_id FROM sites.sites WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE')
  AND a.assignment_status::text = 'ACTIVE'
  AND p.entitlement_type::text = 'SENIOR_CITIZEN'
ORDER BY v.policy_version, e.evidence_type;

\echo '=== Decision and pending/reviewed lifecycle ==='
SELECT
    c.statutory_discount_decision_command_id,
    c.request_reference,
    c.source_channel,
    c.entitlement_type,
    c.decision_status,
    c.command_status,
    c.decision_result_status,
    c.result_classification,
    c.retryable,
    c.recovery_classification,
    c.policy_resolution_basis,
    c.evidence_required,
    c.evidence_recorded,
    c.reason_code,
    c.error_code,
    c.original_correlation_id,
    c.created_at,
    c.decided_at,
    c.applied_at,
    c.completed_at
FROM discounts.statutory_discount_decision_commands c
JOIN core.parking_sessions ps ON ps.parking_session_id = c.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY c.created_at;

SELECT
    v.statutory_discount_validation_id,
    v.entitlement_type,
    v.validation_channel,
    v.validation_status,
    v.policy_resolution_basis,
    v.local_ordinance_applied,
    v.national_law_fallback_applied,
    v.evidence_required,
    v.evidence_captured,
    v.decision_reason_code,
    v.failure_reason_code,
    v.requested_at,
    v.validated_at,
    v.validated_by_user_id,
    v.validated_by_service_identity_id,
    v.correlation_id
FROM discounts.statutory_discount_validations v
JOIN core.parking_sessions ps ON ps.parking_session_id = v.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY v.requested_at;

\echo '=== Evidence lifecycle and review access ==='
SELECT
    s.evidence_set_reference,
    i.evidence_item_reference,
    s.source_channel,
    s.set_status,
    s.entitlement_type,
    s.required_document_profile_code,
    s.retention_status AS set_retention_status,
    s.deletion_status AS set_deletion_status,
    s.hold_active AS set_hold_active,
    i.document_type,
    i.item_role,
    i.upload_status,
    i.validation_status,
    i.scan_status,
    i.reviewability_status,
    i.binding_status,
    i.retention_status AS item_retention_status,
    i.deletion_status AS item_deletion_status,
    i.hold_active AS item_hold_active,
    i.declared_content_type,
    i.validation_result_classification,
    i.scan_result_classification,
    i.uploaded_at,
    i.reviewable_at,
    i.correlation_id
FROM discounts.statutory_evidence_sets s
JOIN discounts.statutory_evidence_items i
  ON i.statutory_evidence_set_id = s.statutory_evidence_set_id
JOIN core.parking_sessions ps ON ps.parking_session_id = s.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY i.created_at;

SELECT
    a.scan_attempt_reference,
    a.attempt_number,
    a.attempt_status,
    a.validation_status,
    a.validation_result,
    a.malware_scan_status,
    a.malware_scan_result,
    a.safe_failure_classification,
    a.scanner_provider,
    a.retry_count,
    a.retryable,
    a.terminal,
    a.started_at,
    a.completed_at,
    a.correlation_id
FROM discounts.statutory_evidence_scan_attempts a
JOIN discounts.statutory_evidence_sets s
  ON s.statutory_evidence_set_id = a.statutory_evidence_set_id
JOIN core.parking_sessions ps ON ps.parking_session_id = s.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY a.attempt_number;

SELECT
    e.event_type,
    e.event_result,
    e.safe_reason_code,
    e.source_channel,
    e.actor_user_id,
    e.actor_service_identity_id,
    e.correlation_id,
    e.occurred_at
FROM discounts.statutory_evidence_events e
JOIN core.parking_sessions ps ON ps.parking_session_id = e.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY e.occurred_at;

\echo '=== Payable-basis application and payment handoff ==='
SELECT
    a.statutory_discount_payable_basis_application_command_id,
    a.request_reference,
    a.statutory_discount_decision_command_id,
    a.command_status,
    a.result_classification,
    a.retryable,
    a.recovery_classification,
    a.safe_error_code,
    a.policy_resolution_basis,
    a.approved_discount_amount_minor_units,
    a.approved_final_payable_amount_minor_units,
    a.currency_code,
    a.source_channel,
    a.original_correlation_id,
    a.created_at,
    a.applied_at,
    a.completed_at
FROM discounts.statutory_discount_payable_basis_application_commands a
JOIN core.parking_sessions ps ON ps.parking_session_id = a.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY a.created_at;

SELECT
    a.statutory_discount_payable_basis_application_id,
    a.statutory_discount_validation_id,
    a.original_tariff_snapshot_id,
    a.applied_tariff_snapshot_id,
    a.application_status,
    a.application_channel,
    a.gross_amount_minor_units,
    a.statutory_discount_amount_minor_units,
    a.final_payable_amount_minor_units,
    a.currency_code,
    a.applied_at,
    a.applied_by_user_id,
    a.applied_by_service_identity_id,
    a.correlation_id
FROM discounts.statutory_discount_payable_basis_applications a
JOIN core.parking_sessions ps ON ps.parking_session_id = a.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY a.created_at;

SELECT
    t.tariff_snapshot_id,
    t.vendor_tariff_ref,
    t.tariff_version_reference,
    t.currency_code,
    t.gross_amount,
    t.statutory_discount_amount,
    t.net_amount,
    t.snapshot_status,
    t.calculated_at,
    t.correlation_id
FROM core.tariff_snapshots t
JOIN core.parking_sessions ps ON ps.parking_session_id = t.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
ORDER BY t.calculated_at;

SELECT
    pa.payment_attempt_id,
    pa.tariff_snapshot_id,
    pa.payment_rail_id,
    pa.currency_code,
    pa.amount,
    pa.attempt_status,
    pa.requested_at,
    pa.expires_at,
    pa.finalized_at,
    pa.failure_reason_code,
    pa.correlation_id,
    count(ps.provider_session_id) AS provider_session_count,
    array_agg(ps.session_status ORDER BY ps.created_at) FILTER (WHERE ps.provider_session_id IS NOT NULL) AS provider_session_statuses
FROM core.payment_attempts pa
JOIN core.parking_sessions parking ON parking.parking_session_id = pa.parking_session_id
LEFT JOIN payments.provider_sessions ps ON ps.payment_attempt_id = pa.payment_attempt_id
WHERE parking.ticket_number_masked = 'E2E-231-SESSION-001'
GROUP BY pa.payment_attempt_id
ORDER BY pa.requested_at;

\echo '=== Replay, correlation, audit, and privacy checks ==='
WITH decision_stats AS (
    SELECT
        count(*) AS command_count,
        count(DISTINCT idempotency_key) AS idempotency_key_count,
        count(DISTINCT business_identity) AS business_identity_count,
        count(*) FILTER (WHERE result_classification = 'IDEMPOTENT_REPLAY') AS replay_classification_count
    FROM discounts.statutory_discount_decision_commands c
    JOIN core.parking_sessions ps ON ps.parking_session_id = c.parking_session_id
    WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
), application_stats AS (
    SELECT
        count(*) AS command_count,
        count(DISTINCT idempotency_key) AS idempotency_key_count,
        count(DISTINCT business_identity) AS business_identity_count,
        count(*) FILTER (WHERE result_classification = 'IDEMPOTENT_REPLAY') AS replay_classification_count
    FROM discounts.statutory_discount_payable_basis_application_commands a
    JOIN core.parking_sessions ps ON ps.parking_session_id = a.parking_session_id
    WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
), payment_stats AS (
    SELECT
        count(*) AS attempt_count,
        count(DISTINCT pa.idempotency_key) AS idempotency_key_count,
        count(DISTINCT pr.idempotency_key) AS provider_idempotency_key_count
    FROM core.payment_attempts pa
    JOIN core.parking_sessions ps ON ps.parking_session_id = pa.parking_session_id
    LEFT JOIN payments.provider_sessions pr ON pr.payment_attempt_id = pa.payment_attempt_id
    WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
)
SELECT
    d.command_count AS decision_command_count,
    d.idempotency_key_count AS decision_idempotency_key_count,
    d.business_identity_count AS decision_business_identity_count,
    d.replay_classification_count AS decision_replay_classification_count,
    a.command_count AS application_command_count,
    a.idempotency_key_count AS application_idempotency_key_count,
    a.business_identity_count AS application_business_identity_count,
    a.replay_classification_count AS application_replay_classification_count,
    p.attempt_count AS payment_attempt_count,
    p.idempotency_key_count AS payment_idempotency_key_count,
    p.provider_idempotency_key_count
FROM decision_stats d
CROSS JOIN application_stats a
CROSS JOIN payment_stats p;

SELECT
    ae.event_type,
    ae.event_category,
    ae.event_result,
    ae.event_reason_code,
    ae.target_entity_type,
    ae.target_entity_id,
    ae.actor_user_id,
    ae.actor_service_identity_id,
    ae.source_channel,
    ae.correlation_id,
    ae.occurred_at
FROM audit.audit_events ae
WHERE ae.actor_user_id = (SELECT user_id FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer')
   OR ae.target_entity_id IN (
        SELECT c.statutory_discount_decision_command_id
        FROM discounts.statutory_discount_decision_commands c
        JOIN core.parking_sessions ps ON ps.parking_session_id = c.parking_session_id
        WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001'
   )
ORDER BY ae.occurred_at;

SELECT
    count(*) FILTER (WHERE i.internal_storage_locator_ref IS NOT NULL) AS internally_located_item_count,
    count(*) FILTER (WHERE i.internal_checksum_sha256 IS NOT NULL) AS internally_checksummed_item_count,
    count(*) FILTER (WHERE octet_length(i.internal_storage_locator_ref) = 0) AS malformed_empty_locator_count,
    count(*) FILTER (WHERE i.declared_content_type NOT IN ('image/jpeg', 'image/png')) AS unsupported_declared_media_count,
    count(*) FILTER (WHERE i.document_type::text <> 'SENIOR_CITIZEN_ID') AS unexpected_document_type_count
FROM discounts.statutory_evidence_items i
JOIN discounts.statutory_evidence_sets s ON s.statutory_evidence_set_id = i.statutory_evidence_set_id
JOIN core.parking_sessions ps ON ps.parking_session_id = s.parking_session_id
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001';

-- Schema-only privacy assertions: byte/Base64 evidence columns must not exist.
SELECT
    count(*) FILTER (WHERE column_name ~* '(evidence|document|image).*(bytes|base64|payload|content)$') = 0 AS no_evidence_byte_or_base64_columns,
    count(*) FILTER (WHERE table_schema = 'discounts' AND column_name ~* '(password|totp|session_secret|bearer|refresh_token)') = 0 AS no_authentication_secrets_in_discount_tables
FROM information_schema.columns
WHERE table_schema IN ('discounts', 'audit', 'core', 'payments');

ROLLBACK;
