/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.13 Timeout, Retry, and Duplicate Handling
 * - 10.7.8 Single-Use Consume Invariant
 *
 * SDD:
 * - Gate Integration Service vendor-neutral gate command lifecycle
 *
 * System Invariants:
 * - A processed GateAuthorizationConsumed handoff creates at most one internal gate command.
 * - The internal command lifecycle is durable and vendor-neutral.
 * - The paid tariff snapshot identity carried by Central PMS is preserved on the command record.
 */

CREATE TABLE IF NOT EXISTS gates.gate_commands (
    command_id uuid PRIMARY KEY,
    command_type varchar(128) NOT NULL,
    source_processing_id uuid NOT NULL,
    source_event_id uuid NULL,
    exit_authorization_id uuid NOT NULL,
    gate_authorization_consumption_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    payment_attempt_id uuid NOT NULL,
    tariff_snapshot_id uuid NOT NULL,
    gate_device_id uuid NULL,
    gate_device_identifier varchar(128) NULL,
    lane_id uuid NULL,
    site_id uuid NULL,
    vendor_system_id uuid NULL,
    command_status varchar(32) NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    requested_at timestamptz NOT NULL,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    failure_code varchar(128) NULL,
    failure_reason text NULL,
    correlation_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT ck_gate_commands__status
        CHECK (command_status IN ('REQUESTED', 'IN_PROGRESS', 'SUCCEEDED', 'FAILED', 'RETRYABLE', 'TERMINAL_FAILURE')),
    CONSTRAINT ck_gate_commands__attempt_count
        CHECK (attempt_count >= 0),
    CONSTRAINT ck_gate_commands__gate_device_identity
        CHECK (gate_device_id IS NOT NULL OR gate_device_identifier IS NOT NULL),
    CONSTRAINT ck_gate_commands__started_at
        CHECK (
            command_status = 'REQUESTED'
            OR started_at IS NOT NULL
        ),
    CONSTRAINT ck_gate_commands__completed_at
        CHECK (
            (command_status IN ('SUCCEEDED', 'FAILED', 'RETRYABLE', 'TERMINAL_FAILURE') AND completed_at IS NOT NULL)
            OR (command_status IN ('REQUESTED', 'IN_PROGRESS') AND completed_at IS NULL)
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_gate_commands__source_processing_command_type
    ON gates.gate_commands (source_processing_id, command_type);

CREATE INDEX IF NOT EXISTS ix_gate_commands__source_event_id
    ON gates.gate_commands (source_event_id)
    WHERE source_event_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_gate_commands__consumption
    ON gates.gate_commands (gate_authorization_consumption_id);

CREATE INDEX IF NOT EXISTS ix_gate_commands__status
    ON gates.gate_commands (command_status);

CREATE INDEX IF NOT EXISTS ix_gate_commands__correlation_id
    ON gates.gate_commands (correlation_id);
