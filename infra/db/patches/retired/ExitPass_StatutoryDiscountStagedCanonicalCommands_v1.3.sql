/*
 * ExitPass v1.3 app-local durable SQL patch.
 *
 * Scope:
 * - Internal staged Central PMS statutory-discount decision-v2 command support.
 * - Internal canonical payable-basis-application-v1 command persistence.
 *
 * Non-goals:
 * - No public API route changes.
 * - No Operator Console route convergence.
 * - No WebPay or APT integration.
 * - No statutory calculation, VAT treatment, payment finality, fiscal issuance, ExitAuthorization, or gate behavior.
 */

ALTER TABLE discounts.statutory_discount_decision_commands
    ADD COLUMN IF NOT EXISTS business_identity varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS command_status varchar(64) NOT NULL DEFAULT 'PROCESSING',
    ADD COLUMN IF NOT EXISTS decision_result_status varchar(64) NOT NULL DEFAULT 'NOT_DECIDED',
    ADD COLUMN IF NOT EXISTS retryable boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS recovery_classification varchar(80) NOT NULL DEFAULT 'NONE',
    ADD COLUMN IF NOT EXISTS vat_exclusive_amount_minor_units bigint NULL,
    ADD COLUMN IF NOT EXISTS vat_amount_minor_units bigint NULL,
    ADD COLUMN IF NOT EXISTS processing_started_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS failed_at timestamptz NULL;

UPDATE discounts.statutory_discount_decision_commands
SET business_identity = idempotency_scope
WHERE business_identity IS NULL
  AND idempotency_scope LIKE 'statutory-discount-decision:%';

DO $$
BEGIN
    ALTER TABLE discounts.statutory_discount_decision_commands
        DROP CONSTRAINT IF EXISTS ck_statutory_discount_decision_commands__semantic_version;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_statutory_discount_decision_commands__semantic_version'
    ) THEN
        ALTER TABLE discounts.statutory_discount_decision_commands
            ADD CONSTRAINT ck_statutory_discount_decision_commands__semantic_version
            CHECK (semantic_hash_source_version IN (
                'statutory-discount-decision:sha256:v1',
                'statutory-discount-decision:sha256:v2'
            ));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_statutory_discount_decision_commands__command_status'
    ) THEN
        ALTER TABLE discounts.statutory_discount_decision_commands
            ADD CONSTRAINT ck_statutory_discount_decision_commands__command_status
            CHECK (command_status IN (
                'RECEIVED',
                'PROCESSING',
                'COMPLETED',
                'FAILED_RETRYABLE',
                'FAILED_NON_RETRYABLE'
            ));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_statutory_discount_decision_commands__decision_result_status'
    ) THEN
        ALTER TABLE discounts.statutory_discount_decision_commands
            ADD CONSTRAINT ck_statutory_discount_decision_commands__decision_result_status
            CHECK (decision_result_status IN (
                'APPROVED',
                'REJECTED',
                'NOT_DECIDED'
            ));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_stat_disc_decision_cmds__recovery'
    ) THEN
        ALTER TABLE discounts.statutory_discount_decision_commands
            DROP CONSTRAINT IF EXISTS ck_statutory_discount_decision_commands__recovery_classification;
        ALTER TABLE discounts.statutory_discount_decision_commands
            DROP CONSTRAINT IF EXISTS ck_statutory_discount_decision_commands__recovery_classificatio;
        ALTER TABLE discounts.statutory_discount_decision_commands
            ADD CONSTRAINT ck_stat_disc_decision_cmds__recovery
            CHECK (recovery_classification IN (
                'NONE',
                'READ_CANONICAL_RESULT',
                'RETRY_ORIGINAL_IDEMPOTENCY_KEY',
                'WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY',
                'CORRECT_REQUEST_REQUIRED',
                'NOT_RECOVERABLE'
            ));
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_statutory_discount_decision_commands__business_identity_text
    ON discounts.statutory_discount_decision_commands (business_identity)
    WHERE business_identity IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_statutory_discount_decision_commands__command_status
    ON discounts.statutory_discount_decision_commands (command_status);

CREATE TABLE IF NOT EXISTS discounts.statutory_discount_payable_basis_application_commands (
    statutory_discount_payable_basis_application_command_id uuid NOT NULL DEFAULT gen_random_uuid(),
    request_reference uuid NOT NULL,
    statutory_discount_decision_command_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    site_id uuid NULL,
    entitlement_type varchar(64) NOT NULL,
    business_identity varchar(256) NOT NULL,
    idempotency_scope varchar(256) NOT NULL,
    idempotency_key varchar(128) NOT NULL,
    semantic_request_hash varchar(80) NOT NULL,
    semantic_hash_source_version varchar(80) NOT NULL,
    command_status varchar(64) NOT NULL,
    result_classification varchar(64) NOT NULL,
    retryable boolean NOT NULL DEFAULT false,
    recovery_classification varchar(80) NOT NULL DEFAULT 'NONE',
    safe_error_code varchar(128) NULL,
    statutory_discount_validation_id uuid NULL,
    statutory_discount_payable_basis_application_id uuid NULL,
    original_tariff_snapshot_id uuid NULL,
    target_tariff_snapshot_id uuid NULL,
    applied_tariff_snapshot_id uuid NULL,
    applied_policy_reference_id uuid NULL,
    policy_resolution_basis varchar(80) NULL,
    approved_discount_amount_minor_units bigint NOT NULL,
    approved_vat_exclusive_amount_minor_units bigint NULL,
    approved_vat_amount_minor_units bigint NULL,
    approved_final_payable_amount_minor_units bigint NOT NULL,
    currency_code char(3) NOT NULL,
    source_channel varchar(64) NOT NULL,
    original_correlation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    processing_started_at timestamptz NULL,
    applied_at timestamptz NULL,
    completed_at timestamptz NULL,
    failed_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_statutory_discount_payable_basis_application_commands
        PRIMARY KEY (statutory_discount_payable_basis_application_command_id),
    CONSTRAINT fk_stat_discount_pba_commands__decision_command
        FOREIGN KEY (statutory_discount_decision_command_id)
        REFERENCES discounts.statutory_discount_decision_commands(statutory_discount_decision_command_id),
    CONSTRAINT fk_stat_discount_pba_commands__parking_session
        FOREIGN KEY (parking_session_id)
        REFERENCES core.parking_sessions(parking_session_id),
    CONSTRAINT fk_stat_discount_pba_commands__site
        FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id),
    CONSTRAINT fk_stat_discount_pba_commands__validation
        FOREIGN KEY (statutory_discount_validation_id)
        REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id),
    CONSTRAINT fk_stat_discount_pba_commands__payable_basis_application
        FOREIGN KEY (statutory_discount_payable_basis_application_id)
        REFERENCES discounts.statutory_discount_payable_basis_applications(statutory_discount_payable_basis_application_id),
    CONSTRAINT fk_stat_discount_pba_commands__original_tariff_snapshot
        FOREIGN KEY (original_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id),
    CONSTRAINT fk_stat_discount_pba_commands__target_tariff_snapshot
        FOREIGN KEY (target_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id),
    CONSTRAINT fk_stat_discount_pba_commands__applied_tariff_snapshot
        FOREIGN KEY (applied_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id),
    CONSTRAINT ck_stat_discount_pba_commands__source_channel
        CHECK (source_channel IN ('OPERATOR_CONSOLE', 'WEBPAY', 'ASSISTED_PAYMENT_TERMINAL')),
    CONSTRAINT ck_stat_discount_pba_commands__entitlement_type
        CHECK (entitlement_type IN ('SENIOR_CITIZEN', 'PWD')),
    CONSTRAINT ck_stat_discount_pba_commands__hash
        CHECK (semantic_request_hash ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_stat_discount_pba_commands__semantic_version
        CHECK (semantic_hash_source_version = 'statutory-discount-payable-basis-application:sha256:v1'),
    CONSTRAINT ck_stat_discount_pba_commands__command_status
        CHECK (command_status IN (
            'RECEIVED',
            'PROCESSING',
            'APPLIED',
            'FAILED_RETRYABLE',
            'FAILED_NON_RETRYABLE'
        )),
    CONSTRAINT ck_stat_discount_pba_commands__result_classification
        CHECK (result_classification IN (
            'APPLIED',
            'IDEMPOTENT_REPLAY',
            'SEMANTIC_CONFLICT',
            'DECISION_NOT_APPROVED',
            'DECISION_NOT_FOUND',
            'IN_PROGRESS',
            'RETRYABLE_FAILURE',
            'NON_RETRYABLE_FAILURE'
        )),
    CONSTRAINT ck_stat_discount_pba_commands__recovery_classification
        CHECK (recovery_classification IN (
            'NONE',
            'READ_CANONICAL_RESULT',
            'RETRY_ORIGINAL_IDEMPOTENCY_KEY',
            'WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY',
            'CORRECT_REQUEST_REQUIRED',
            'NOT_RECOVERABLE'
        )),
    CONSTRAINT ck_stat_discount_pba_commands__amounts_non_negative
        CHECK (
            approved_discount_amount_minor_units >= 0
            AND approved_final_payable_amount_minor_units >= 0
            AND (
                approved_vat_exclusive_amount_minor_units IS NULL
                OR approved_vat_exclusive_amount_minor_units >= 0
            )
            AND (
                approved_vat_amount_minor_units IS NULL
                OR approved_vat_amount_minor_units >= 0
            )
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stat_discount_pba_commands__business_identity
    ON discounts.statutory_discount_payable_basis_application_commands (business_identity);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stat_discount_pba_commands__decision_command
    ON discounts.statutory_discount_payable_basis_application_commands (statutory_discount_decision_command_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stat_discount_pba_commands__idempotency
    ON discounts.statutory_discount_payable_basis_application_commands (idempotency_scope, idempotency_key);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stat_discount_pba_commands__request_reference
    ON discounts.statutory_discount_payable_basis_application_commands (request_reference);

CREATE INDEX IF NOT EXISTS ix_stat_discount_pba_commands__parking_session
    ON discounts.statutory_discount_payable_basis_application_commands (parking_session_id);

CREATE INDEX IF NOT EXISTS ix_stat_discount_pba_commands__validation
    ON discounts.statutory_discount_payable_basis_application_commands (statutory_discount_validation_id);

CREATE INDEX IF NOT EXISTS ix_stat_discount_pba_commands__correlation
    ON discounts.statutory_discount_payable_basis_application_commands (original_correlation_id);

COMMENT ON COLUMN discounts.statutory_discount_decision_commands.business_identity IS
    'Canonical staged decision business identity statutory-discount-decision:{parkingSessionId}:{entitlementType}; nullable for historical compatibility but populated by staged command persistence.';

COMMENT ON TABLE discounts.statutory_discount_payable_basis_application_commands IS
    'Internal canonical command table for staged statutory-discount payable-basis application. The table records idempotency, semantic hash, and safe linkage only; it does not itself mark payment final, issue fiscal documents, issue ExitAuthorization, call HikCentral, call payment providers, or command gates.';
