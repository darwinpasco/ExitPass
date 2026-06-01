-- ExitPass v1.2 - HikCentral gate action request/response audit.
-- Stores safe vendor exchange metadata only. Raw request/response bodies, credentials,
-- signatures, and secret-bearing header values are intentionally excluded.

CREATE TABLE IF NOT EXISTS gates.hikcentral_gate_action_audits (
    audit_id uuid PRIMARY KEY,
    gate_command_id uuid NOT NULL REFERENCES gates.gate_commands(command_id),
    source_processing_id uuid NOT NULL,
    source_event_id uuid NULL,
    exit_authorization_id uuid NOT NULL,
    gate_authorization_consumption_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    payment_attempt_id uuid NOT NULL,
    tariff_snapshot_id uuid NOT NULL,
    gate_device_id uuid NULL,
    gate_device_identifier varchar(128) NULL,
    door_index_code varchar(128) NOT NULL,
    lane_id uuid NULL,
    site_id uuid NULL,
    vendor_system_id uuid NULL,
    vendor_code varchar(64) NOT NULL,
    vendor_name varchar(128) NOT NULL,
    operation varchar(128) NOT NULL,
    request_method varchar(16) NOT NULL,
    request_path varchar(512) NOT NULL,
    request_body_sha256 char(64) NOT NULL,
    signed_headers_list text NOT NULL,
    request_correlation_id uuid NOT NULL,
    vendor_request_id varchar(128) NULL,
    vendor_correlation_id varchar(128) NULL,
    http_status_code integer NULL,
    vendor_response_code varchar(64) NULL,
    vendor_response_message varchar(256) NULL,
    outcome_category varchar(64) NOT NULL,
    retryable boolean NOT NULL,
    terminal_failure boolean NOT NULL,
    duration_ms integer NOT NULL,
    timeout_occurred boolean NOT NULL,
    vendor_unavailable boolean NOT NULL,
    transport_error_code varchar(128) NULL,
    transport_error_message text NULL,
    requested_at timestamptz NOT NULL,
    responded_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_hikcentral_gate_action_audits_vendor
        CHECK (vendor_code = 'HikCentral'),
    CONSTRAINT ck_hikcentral_gate_action_audits_method
        CHECK (request_method = 'POST'),
    CONSTRAINT ck_hikcentral_gate_action_audits_http_status
        CHECK (http_status_code IS NULL OR http_status_code BETWEEN 100 AND 599),
    CONSTRAINT ck_hikcentral_gate_action_audits_duration
        CHECK (duration_ms >= 0),
    CONSTRAINT ck_hikcentral_gate_action_audits_hash
        CHECK (request_body_sha256 ~ '^[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS ix_hikcentral_gate_action_audits_gate_command
    ON gates.hikcentral_gate_action_audits (gate_command_id);

CREATE INDEX IF NOT EXISTS ix_hikcentral_gate_action_audits_source_processing
    ON gates.hikcentral_gate_action_audits (source_processing_id);

CREATE INDEX IF NOT EXISTS ix_hikcentral_gate_action_audits_consumption
    ON gates.hikcentral_gate_action_audits (gate_authorization_consumption_id);

CREATE INDEX IF NOT EXISTS ix_hikcentral_gate_action_audits_exit_authorization
    ON gates.hikcentral_gate_action_audits (exit_authorization_id);

CREATE INDEX IF NOT EXISTS ix_hikcentral_gate_action_audits_vendor_response_code
    ON gates.hikcentral_gate_action_audits (vendor_response_code);

CREATE INDEX IF NOT EXISTS ix_hikcentral_gate_action_audits_outcome
    ON gates.hikcentral_gate_action_audits (outcome_category);

CREATE INDEX IF NOT EXISTS ix_hikcentral_gate_action_audits_created_requested
    ON gates.hikcentral_gate_action_audits (created_at, requested_at);
