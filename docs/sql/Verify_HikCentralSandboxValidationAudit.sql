-- Read-only verification for a controlled HikCentral sandbox validation attempt.
--
-- Replace these placeholders before running:
--   <audit-id-from-report>
--   <correlation-id-from-request-or-report>
--   <validation-attempt-id-from-report>
--
-- Do not paste AppSecret, raw X-Ca-Signature, or raw signed headers into this file.

-- 1. Verify the audit row by audit ID.
SELECT
    audit_id,
    gate_command_id,
    source_processing_id AS validation_attempt_id,
    request_correlation_id,
    vendor_code,
    operation,
    door_index_code,
    request_method,
    request_path,
    request_body_sha256,
    signed_headers_list,
    http_status_code,
    vendor_response_code,
    vendor_response_message,
    outcome_category,
    retryable,
    terminal_failure,
    duration_ms,
    timeout_occurred,
    vendor_unavailable,
    transport_error_code,
    requested_at,
    responded_at,
    created_at
FROM gates.hikcentral_gate_action_audits
WHERE audit_id = '<audit-id-from-report>'::uuid;

-- 2. Verify correlation and validation-attempt linkage.
SELECT
    audit_id,
    source_processing_id AS validation_attempt_id,
    request_correlation_id,
    outcome_category,
    retryable,
    terminal_failure,
    created_at
FROM gates.hikcentral_gate_action_audits
WHERE request_correlation_id = '<correlation-id-from-request-or-report>'::uuid
   OR source_processing_id = '<validation-attempt-id-from-report>'::uuid
ORDER BY created_at DESC;

-- 3. Verify the validation-only command row linked to the audit row.
SELECT
    c.command_id,
    c.command_type,
    c.source_processing_id AS validation_attempt_id,
    c.command_status,
    c.gate_device_identifier AS door_index_code,
    c.correlation_id,
    c.failure_code,
    c.failure_reason,
    c.requested_at,
    c.completed_at,
    a.audit_id
FROM gates.gate_commands c
JOIN gates.hikcentral_gate_action_audits a
    ON a.gate_command_id = c.command_id
WHERE a.audit_id = '<audit-id-from-report>'::uuid;

-- 4. Verify safe metadata expectations.
SELECT
    audit_id,
    request_body_sha256 ~ '^[0-9a-f]{64}$' AS request_hash_is_sha256_hex,
    request_path = '/artemis/api/acs/v1/door/doControl' AS request_path_expected,
    signed_headers_list = 'x-ca-key,x-ca-nonce,x-ca-timestamp' AS signed_header_names_only,
    request_method = 'POST' AS request_method_expected,
    vendor_code = 'HikCentral' AS vendor_expected
FROM gates.hikcentral_gate_action_audits
WHERE audit_id = '<audit-id-from-report>'::uuid;

-- 5. Verify the audit table does not contain forbidden raw/secret columns.
SELECT
    column_name
FROM information_schema.columns
WHERE table_schema = 'gates'
  AND table_name = 'hikcentral_gate_action_audits'
  AND column_name IN (
      'app_secret',
      'secret',
      'raw_secret',
      'x_ca_signature',
      'request_body',
      'raw_request_body',
      'response_body',
      'raw_response_body',
      'authorization_header'
  )
ORDER BY column_name;

-- Expected for query 5: zero rows.
