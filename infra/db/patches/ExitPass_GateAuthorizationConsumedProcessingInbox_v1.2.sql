/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.13 Timeout, Retry, and Duplicate Handling
 * - 10.7.8 Single-Use Consume Invariant
 *
 * SDD:
 * - Gate Integration Service consumed authorization handoff boundary
 * - Integration event processing idempotency
 *
 * System Invariants:
 * - GateIntegrationService processes each GateAuthorizationConsumed handoff at most once as a gate action.
 * - Processing state is durable across process restarts.
 * - The paid tariff snapshot identity carried by Central PMS is preserved for audit and downstream processing.
 */

CREATE TABLE IF NOT EXISTS gates.gate_authorization_consumed_processing (
    processing_id uuid PRIMARY KEY,
    processing_key uuid NOT NULL,
    event_id uuid NULL,
    event_type varchar(128) NOT NULL,
    source_event_ref varchar(512) NULL,
    gate_authorization_consumption_id uuid NOT NULL,
    exit_authorization_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    payment_attempt_id uuid NOT NULL,
    tariff_snapshot_id uuid NOT NULL,
    gate_device_id uuid NULL,
    gate_device_identifier varchar(128) NULL,
    lane_id uuid NULL,
    site_id uuid NULL,
    vendor_system_id uuid NULL,
    consumed_at_utc timestamptz NOT NULL,
    correlation_id uuid NOT NULL,
    processing_status varchar(32) NOT NULL,
    result_code varchar(128) NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    first_seen_at timestamptz NOT NULL,
    last_attempted_at timestamptz NULL,
    processed_at timestamptz NULL,
    last_failure_code varchar(128) NULL,
    last_failure_reason text NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT ck_gate_auth_consumed_processing__status
        CHECK (processing_status IN ('PROCESSING', 'PROCESSED', 'FAILED')),
    CONSTRAINT ck_gate_auth_consumed_processing__attempt_count
        CHECK (attempt_count >= 0),
    CONSTRAINT ck_gate_auth_consumed_processing__processed_at
        CHECK (
            (processing_status = 'PROCESSED' AND processed_at IS NOT NULL)
            OR (processing_status <> 'PROCESSED' AND processed_at IS NULL)
        ),
    CONSTRAINT ck_gate_auth_consumed_processing__gate_device_identity
        CHECK (gate_device_id IS NOT NULL OR gate_device_identifier IS NOT NULL)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_gate_auth_consumed_processing__key_event_type
    ON gates.gate_authorization_consumed_processing (processing_key, event_type);

CREATE INDEX IF NOT EXISTS ix_gate_auth_consumed_processing__event_id
    ON gates.gate_authorization_consumed_processing (event_id)
    WHERE event_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_gate_auth_consumed_processing__consumption
    ON gates.gate_authorization_consumed_processing (gate_authorization_consumption_id);

CREATE INDEX IF NOT EXISTS ix_gate_auth_consumed_processing__status
    ON gates.gate_authorization_consumed_processing (processing_status);

CREATE INDEX IF NOT EXISTS ix_gate_auth_consumed_processing__correlation_id
    ON gates.gate_authorization_consumed_processing (correlation_id);
