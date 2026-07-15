/*
 * ExitPass v1.3 app-local durable SQL patch.
 *
 * Scope:
 * - Terminal cash-payment command evidence and durable idempotency.
 * - CASH payment rail seed needed by Central PMS cash confirmation.
 *
 * Non-goals:
 * - No Payment Orchestrator provider outcome.
 * - No POS Server fiscal issuance.
 * - No ExitAuthorization or gate behavior.
 */

INSERT INTO identity.service_identities (
    service_identity_id,
    service_identity_code,
    service_identity_name,
    identity_type,
    identity_status,
    owning_service_name,
    credential_reference,
    credential_type,
    effective_from
)
VALUES (
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    'seed.reference-data',
    'ExitPass Reference Data Seeder',
    'INTERNAL_SERVICE',
    'ACTIVE',
    'ExitPass.DbMigrator',
    NULL,
    NULL,
    now()
)
ON CONFLICT ON CONSTRAINT uq_service_identities__service_identity_code DO UPDATE
SET
    service_identity_name = EXCLUDED.service_identity_name,
    identity_type = EXCLUDED.identity_type,
    identity_status = EXCLUDED.identity_status,
    owning_service_name = EXCLUDED.owning_service_name,
    updated_at = now();

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
VALUES (
    '42c4f2e7-35a7-5f71-9f80-1c3fcf6d3c01',
    'CASH',
    'Cash',
    'CASH',
    'OTHER',
    'PHP',
    'ACTIVE',
    false,
    false,
    'cash-terminal',
    'payment-rail/cash/terminal',
    now(),
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
)
ON CONFLICT ON CONSTRAINT uq_payment_rails__rail_code DO UPDATE
SET
    rail_name = EXCLUDED.rail_name,
    provider_code = EXCLUDED.provider_code,
    rail_type = EXCLUDED.rail_type,
    supported_currency_code = EXCLUDED.supported_currency_code,
    rail_status = EXCLUDED.rail_status,
    is_primary = EXCLUDED.is_primary,
    is_fallback = EXCLUDED.is_fallback,
    provider_profile_ref = EXCLUDED.provider_profile_ref,
    configuration_ref = EXCLUDED.configuration_ref,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = payments.payment_rails.row_version + 1;

CREATE TABLE IF NOT EXISTS core.terminal_cash_payment_commands (
    terminal_cash_payment_command_id uuid NOT NULL DEFAULT gen_random_uuid(),
    terminal_cash_tender_id uuid NOT NULL,
    cash_custody_session_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    tariff_snapshot_id uuid NOT NULL,
    cashier_id varchar(128) NOT NULL,
    cashier_session_reference varchar(256) NOT NULL,
    cashier_shift_id varchar(128) NOT NULL,
    terminal_id varchar(128) NOT NULL,
    site_id uuid NOT NULL,
    site_group_id uuid NOT NULL,
    pos_server_id varchar(128) NOT NULL,
    currency_code char(3) NOT NULL,
    amount_due_minor_units bigint NOT NULL,
    amount_tendered_minor_units bigint NOT NULL,
    change_due_minor_units bigint NOT NULL,
    cash_received_at timestamptz NOT NULL,
    denomination_entries jsonb NOT NULL DEFAULT '[]'::jsonb,
    local_event_reference varchar(256) NOT NULL,
    idempotency_key varchar(128) NOT NULL,
    idempotency_scope varchar(256) NOT NULL,
    semantic_request_hash varchar(80) NOT NULL,
    semantic_hash_source_version varchar(32) NOT NULL,
    original_correlation_id uuid NOT NULL,
    payment_attempt_id uuid NOT NULL,
    payment_confirmation_id uuid NOT NULL,
    canonical_payment_status varchar(64) NOT NULL,
    result_classification varchar(64) NOT NULL,
    fiscal_status varchar(64) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    confirmed_at timestamptz NOT NULL,
    last_updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_terminal_cash_payment_commands PRIMARY KEY (terminal_cash_payment_command_id),
    CONSTRAINT fk_terminal_cash_payment_commands__parking_session_id
        FOREIGN KEY (parking_session_id)
        REFERENCES core.parking_sessions(parking_session_id),
    CONSTRAINT fk_terminal_cash_payment_commands__tariff_snapshot_id
        FOREIGN KEY (tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id),
    CONSTRAINT fk_terminal_cash_payment_commands__payment_attempt_id
        FOREIGN KEY (payment_attempt_id)
        REFERENCES core.payment_attempts(payment_attempt_id),
    CONSTRAINT fk_terminal_cash_payment_commands__payment_confirmation_id
        FOREIGN KEY (payment_confirmation_id)
        REFERENCES core.payment_confirmations(payment_confirmation_id),
    CONSTRAINT ck_terminal_cash_payment_commands__amounts
        CHECK (
            amount_due_minor_units > 0
            AND amount_tendered_minor_units >= amount_due_minor_units
            AND change_due_minor_units = amount_tendered_minor_units - amount_due_minor_units
        ),
    CONSTRAINT ck_terminal_cash_payment_commands__currency
        CHECK (currency_code = upper(currency_code) AND length(currency_code) = 3),
    CONSTRAINT ck_terminal_cash_payment_commands__hash
        CHECK (semantic_request_hash ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_terminal_cash_payment_commands__semantic_version
        CHECK (semantic_hash_source_version = 'terminal-cash-payment:sha256:v1'),
    CONSTRAINT ck_terminal_cash_payment_commands__classification
        CHECK (result_classification IN ('CREATED', 'IDEMPOTENT_REPLAY')),
    CONSTRAINT ck_terminal_cash_payment_commands__fiscal_status
        CHECK (fiscal_status = 'NOT_STARTED_IN_THIS_SLICE')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_terminal_cash_payment_commands__idempotency
    ON core.terminal_cash_payment_commands (idempotency_scope, idempotency_key);

CREATE UNIQUE INDEX IF NOT EXISTS ux_terminal_cash_payment_commands__terminal_tender
    ON core.terminal_cash_payment_commands (terminal_id, site_id, terminal_cash_tender_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_terminal_cash_payment_commands__payment_attempt
    ON core.terminal_cash_payment_commands (payment_attempt_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_terminal_cash_payment_commands__payment_confirmation
    ON core.terminal_cash_payment_commands (payment_confirmation_id);

CREATE INDEX IF NOT EXISTS ix_terminal_cash_payment_commands__parking_session
    ON core.terminal_cash_payment_commands (parking_session_id);

CREATE INDEX IF NOT EXISTS ix_terminal_cash_payment_commands__tariff_snapshot
    ON core.terminal_cash_payment_commands (tariff_snapshot_id);

CREATE INDEX IF NOT EXISTS ix_terminal_cash_payment_commands__correlation
    ON core.terminal_cash_payment_commands (original_correlation_id);

COMMENT ON TABLE core.terminal_cash_payment_commands IS
    'Durable Central PMS evidence for terminal cash-payment commands and idempotent readback. This table records backend acceptance of terminal-local cash evidence; it does not perform fiscal issuance, issue exit authorization, or command gates.';

CREATE TABLE IF NOT EXISTS core.terminal_cash_payment_command_audits (
    terminal_cash_payment_command_audit_id uuid NOT NULL DEFAULT gen_random_uuid(),
    terminal_cash_payment_command_id uuid NULL,
    terminal_cash_tender_id uuid NOT NULL,
    audit_event_type varchar(64) NOT NULL,
    error_code varchar(128) NULL,
    correlation_id uuid NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_terminal_cash_payment_command_audits PRIMARY KEY (terminal_cash_payment_command_audit_id),
    CONSTRAINT fk_terminal_cash_payment_command_audits__command
        FOREIGN KEY (terminal_cash_payment_command_id)
        REFERENCES core.terminal_cash_payment_commands(terminal_cash_payment_command_id),
    CONSTRAINT ck_terminal_cash_payment_command_audits__event
        CHECK (audit_event_type IN ('ACCEPTED', 'IDEMPOTENT_REPLAY', 'SEMANTIC_CONFLICT', 'REJECTED'))
);

CREATE INDEX IF NOT EXISTS ix_terminal_cash_payment_command_audits__tender
    ON core.terminal_cash_payment_command_audits (terminal_cash_tender_id);

CREATE INDEX IF NOT EXISTS ix_terminal_cash_payment_command_audits__correlation
    ON core.terminal_cash_payment_command_audits (correlation_id);
