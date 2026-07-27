/*
 * ExitPass v1.3 app-local durable SQL patch.
 *
 * Scope:
 * - Shared Central PMS statutory-discount command/readback facade evidence.
 * - Durable idempotency and semantic-conflict detection for channel-neutral requests.
 *
 * Non-goals:
 * - No statutory-discount engine rewrite.
 * - No local ordinance activation or seed data.
 * - No payment finality, POS fiscal issuance, ExitAuthorization, or gate behavior.
 */

CREATE TABLE IF NOT EXISTS discounts.statutory_discount_decision_commands (
    statutory_discount_decision_command_id uuid NOT NULL DEFAULT gen_random_uuid(),
    request_reference uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    source_channel varchar(64) NOT NULL,
    entitlement_type varchar(64) NOT NULL,
    idempotency_scope varchar(256) NOT NULL,
    idempotency_key varchar(128) NOT NULL,
    semantic_request_hash varchar(80) NOT NULL,
    semantic_hash_source_version varchar(64) NOT NULL,
    statutory_discount_validation_id uuid NULL,
    payable_basis_application_id uuid NULL,
    original_tariff_snapshot_id uuid NULL,
    applied_tariff_snapshot_id uuid NULL,
    decision_status varchar(64) NOT NULL,
    result_classification varchar(64) NOT NULL,
    policy_resolution_basis varchar(80) NULL,
    applied_policy_reference_id uuid NULL,
    fallback_policy_reference_id uuid NULL,
    local_ordinance_applied boolean NOT NULL DEFAULT false,
    gross_amount_minor_units bigint NULL,
    statutory_discount_amount_minor_units bigint NULL,
    net_payable_amount_minor_units bigint NULL,
    currency_code char(3) NULL,
    evidence_required boolean NOT NULL DEFAULT false,
    evidence_recorded boolean NOT NULL DEFAULT false,
    reason_code varchar(128) NULL,
    error_code varchar(128) NULL,
    original_correlation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    decided_at timestamptz NULL,
    applied_at timestamptz NULL,
    completed_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_statutory_discount_decision_commands
        PRIMARY KEY (statutory_discount_decision_command_id),
    CONSTRAINT fk_statutory_discount_decision_commands__parking_session
        FOREIGN KEY (parking_session_id)
        REFERENCES core.parking_sessions(parking_session_id),
    CONSTRAINT fk_statutory_discount_decision_commands__validation
        FOREIGN KEY (statutory_discount_validation_id)
        REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id),
    CONSTRAINT fk_statutory_discount_decision_commands__payable_basis_application
        FOREIGN KEY (payable_basis_application_id)
        REFERENCES discounts.statutory_discount_payable_basis_applications(statutory_discount_payable_basis_application_id),
    CONSTRAINT fk_statutory_discount_decision_commands__original_tariff_snapshot
        FOREIGN KEY (original_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id),
    CONSTRAINT fk_statutory_discount_decision_commands__applied_tariff_snapshot
        FOREIGN KEY (applied_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id),
    CONSTRAINT ck_statutory_discount_decision_commands__source_channel
        CHECK (source_channel IN ('OPERATOR_CONSOLE', 'WEBPAY', 'ASSISTED_PAYMENT_TERMINAL')),
    CONSTRAINT ck_statutory_discount_decision_commands__entitlement_type
        CHECK (entitlement_type IN ('SENIOR_CITIZEN', 'PWD')),
    CONSTRAINT ck_statutory_discount_decision_commands__hash
        CHECK (semantic_request_hash ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_statutory_discount_decision_commands__semantic_version
        CHECK (semantic_hash_source_version = 'statutory-discount-decision:sha256:v1'),
    CONSTRAINT ck_statutory_discount_decision_commands__decision_status
        CHECK (decision_status IN (
            'PROCESSING',
            'REQUESTED',
            'PENDING_OPERATOR_REVIEW',
            'APPROVED',
            'REJECTED',
            'FAILED',
            'EXPIRED',
            'CANCELLED',
            'APPLIED_PAYABLE_BASIS'
        )),
    CONSTRAINT ck_statutory_discount_decision_commands__result_classification
        CHECK (result_classification IN ('ACCEPTED', 'IDEMPOTENT_REPLAY'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_statutory_discount_decision_commands__idempotency
    ON discounts.statutory_discount_decision_commands (idempotency_scope, idempotency_key);

CREATE UNIQUE INDEX IF NOT EXISTS ux_statutory_discount_decision_commands__business_identity
    ON discounts.statutory_discount_decision_commands (parking_session_id, entitlement_type);

CREATE UNIQUE INDEX IF NOT EXISTS ux_statutory_discount_decision_commands__request_reference
    ON discounts.statutory_discount_decision_commands (request_reference);

CREATE INDEX IF NOT EXISTS ix_statutory_discount_decision_commands__parking_session
    ON discounts.statutory_discount_decision_commands (parking_session_id);

CREATE INDEX IF NOT EXISTS ix_statutory_discount_decision_commands__validation
    ON discounts.statutory_discount_decision_commands (statutory_discount_validation_id);

CREATE INDEX IF NOT EXISTS ix_statutory_discount_decision_commands__correlation
    ON discounts.statutory_discount_decision_commands (original_correlation_id);

COMMENT ON TABLE discounts.statutory_discount_decision_commands IS
    'Durable shared Central PMS statutory-discount command/readback facade evidence. This table records channel-neutral request attribution, idempotency, semantic hash, and links to existing validation/payable-basis rows; it does not calculate rules, create payment finality, issue fiscal documents, issue ExitAuthorization, or command gates.';
