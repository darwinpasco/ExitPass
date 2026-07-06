-- ExitPass v1.3 Central PMS fiscal reference state persistence scaffolding.
--
-- Scope:
-- - Persistence/state only for Central PMS fiscal issuance reference evidence.
-- - No POS Server network behavior, retry worker, readback worker, ExitAuthorization gating,
--   Operator Console queue, Dashboard projection, Digital SI, X/Z, BIR, EJ, POSLog, or gate behavior.

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_references (
    fiscal_issuance_reference_id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_confirmation_id uuid NOT NULL,
    payment_attempt_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    tariff_snapshot_id uuid,
    site_id uuid,
    site_pos_server_id uuid,
    site_pos_server_ref varchar(128),
    fiscal_document_type_code_id uuid,
    fiscal_document_type_code_key varchar(80),
    payable_basis_ref varchar(160),
    upstream_finality_reference varchar(200) NOT NULL,
    pos_server_fiscal_document_id uuid,
    fiscal_identity_id uuid,
    fiscal_sequence_policy_id uuid,
    fiscal_sequence_value bigint,
    fiscal_document_number varchar(120),
    fiscal_series varchar(80),
    fiscal_number_prefix_text varchar(80),
    fiscal_number_suffix_text varchar(80),
    fiscal_number_assigned_at timestamptz,
    fiscal_number_assigned_by_ref varchar(160),
    fiscal_document_status_code_id uuid,
    result_classification varchar(40),
    fiscal_issuance_evidence_status varchar(80),
    fiscal_number_assignment_state varchar(40) DEFAULT 'NOT_ASSIGNED' NOT NULL,
    fiscal_issuance_state varchar(80) NOT NULL,
    latest_exception_reason varchar(120),
    latest_error_code varchar(120),
    latest_error_posture varchar(80),
    correlation_id uuid,
    pos_server_response_timestamp timestamptz,
    semantic_request_hash_status varchar(40),
    semantic_request_hash_value varchar(64),
    semantic_request_hash_algorithm varchar(32),
    semantic_request_hash_source_version varchar(80),
    semantic_request_hash_source_fact_count integer,
    semantic_request_hash_safe_summary varchar(240),
    semantic_request_hash_recorded_at timestamptz,
    first_recorded_at timestamptz DEFAULT now() NOT NULL,
    last_updated_at timestamptz DEFAULT now() NOT NULL,
    recorded_by_service_identity_id uuid,
    updated_by_service_identity_id uuid,
    is_active boolean DEFAULT true NOT NULL,
    is_superseded boolean DEFAULT false NOT NULL,
    is_reconciled boolean DEFAULT false NOT NULL,
    CONSTRAINT pk_fiscal_issuance_references PRIMARY KEY (fiscal_issuance_reference_id),
    CONSTRAINT ck_fiscal_issuance_references__fiscal_sequence_value_positive CHECK (fiscal_sequence_value IS NULL OR fiscal_sequence_value > 0),
    CONSTRAINT ck_fiscal_issuance_references__result_classification CHECK (
        result_classification IS NULL
        OR result_classification IN ('NEWLY_CREATED', 'IDEMPOTENT_REPLAY')
    ),
    CONSTRAINT ck_fiscal_issuance_references__evidence_status CHECK (
        fiscal_issuance_evidence_status IS NULL
        OR fiscal_issuance_evidence_status IN ('FISCAL_DOCUMENT_NUMBER_ASSIGNED')
    ),
    CONSTRAINT ck_fiscal_issuance_references__assignment_state CHECK (
        fiscal_number_assignment_state IN ('ASSIGNED', 'NOT_ASSIGNED')
    ),
    CONSTRAINT ck_fiscal_issuance_references__integration_state CHECK (
        fiscal_issuance_state IN (
            'NOT_REQUIRED',
            'PENDING_FISCAL_ISSUANCE',
            'FISCAL_ISSUANCE_REQUESTED',
            'FISCAL_ISSUANCE_RECORDED',
            'FISCAL_ISSUANCE_REPLAYED',
            'FISCAL_ISSUANCE_CONFLICT',
            'FISCAL_ISSUANCE_FAILED_REQUEST',
            'FISCAL_ISSUANCE_FAILED_CONFIGURATION',
            'FISCAL_ISSUANCE_FAILED_SERVICE',
            'FISCAL_ISSUANCE_UNKNOWN',
            'FISCAL_ISSUANCE_MANUAL_REVIEW',
            'FISCAL_ISSUANCE_EXCEPTION_RELEASED',
            'FISCAL_ISSUANCE_RECONCILED'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_references__exception_reason CHECK (
        latest_exception_reason IS NULL
        OR latest_exception_reason IN (
            'MISSING_PAYABLE_BASIS',
            'MISSING_UPSTREAM_FINALITY_REFERENCE',
            'UNAPPROVED_DISCOUNT_REFERENCE',
            'UNSUPPORTED_FISCAL_DOCUMENT_REQUEST',
            'INVALID_FISCAL_TENDER',
            'MISSING_FISCAL_TENDER',
            'INVALID_FISCAL_TAX_DETAIL',
            'INVALID_FISCAL_DISCOUNT_PRIVILEGE_DETAIL',
            'INVALID_FISCAL_TOTAL',
            'SENSITIVE_PAYLOAD_REJECTED',
            'REQUEST_CONSTRUCTION_ERROR',
            'FISCAL_IDENTITY_NOT_FOUND',
            'FISCAL_IDENTITY_AMBIGUOUS',
            'FISCAL_IDENTITY_NOT_EFFECTIVE',
            'FISCAL_SEQUENCE_POLICY_NOT_FOUND',
            'FISCAL_SEQUENCE_POLICY_AMBIGUOUS',
            'FISCAL_SEQUENCE_POLICY_NOT_EFFECTIVE',
            'FISCAL_SEQUENCE_STATE_NOT_FOUND',
            'FISCAL_SEQUENCE_STATE_NOT_EFFECTIVE',
            'FISCAL_NUMBER_ALLOCATION_FAILED',
            'FISCAL_DOCUMENT_NUMBER_FORMAT_FAILED',
            'FISCAL_DOCUMENT_IDEMPOTENCY_CONFLICT',
            'REPLAY_MISMATCH',
            'DUPLICATE_REFERENCE_DETECTED',
            'PERSISTENCE_NOT_CONFIGURED',
            'INVALID_PERSISTENCE_CONFIGURATION',
            'PERSISTENCE_WRITE_FAILED',
            'FISCAL_NUMBER_ASSIGNMENT_INCOMPLETE',
            'POST_TIMEOUT',
            'NETWORK_DISCONNECT_AFTER_POSSIBLE_COMMIT',
            'GET_READBACK_NOT_FOUND',
            'GET_READBACK_SERVICE_FAILED',
            'GET_READBACK_INCONCLUSIVE',
            'CENTRAL_PMS_REFERENCE_PERSISTENCE_FAILED',
            'MANUAL_REVIEW_REQUIRED',
            'MANUAL_RELEASE_REQUESTED_AFTER_FISCAL_FAILURE',
            'FISCAL_REFERENCE_MISMATCH',
            'RECONCILIATION_REQUIRED',
            'RECONCILIATION_CLOSED'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_references__error_posture CHECK (
        latest_error_posture IS NULL
        OR latest_error_posture IN (
            'DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE',
            'RETRY_AFTER_CONFIGURATION_CORRECTION',
            'RETRY_AFTER_SERVICE_RECOVERY'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_references__semantic_request_hash_status CHECK (
        semantic_request_hash_status IS NULL
        OR semantic_request_hash_status IN ('UNAVAILABLE', 'INCOMPLETE', 'AVAILABLE')
    ),
    CONSTRAINT ck_fiscal_issuance_references__semantic_request_hash_available_complete CHECK (
        semantic_request_hash_status IS DISTINCT FROM 'AVAILABLE'
        OR (
            semantic_request_hash_value IS NOT NULL
            AND semantic_request_hash_algorithm IS NOT NULL
            AND semantic_request_hash_source_version IS NOT NULL
            AND semantic_request_hash_source_fact_count IS NOT NULL
            AND semantic_request_hash_source_fact_count > 0
        )
    ),
    CONSTRAINT ck_fiscal_issuance_references__complete_recorded_evidence CHECK (
        fiscal_issuance_state NOT IN (
            'FISCAL_ISSUANCE_RECORDED',
            'FISCAL_ISSUANCE_REPLAYED',
            'FISCAL_ISSUANCE_RECONCILED'
        )
        OR (
            pos_server_fiscal_document_id IS NOT NULL
            AND fiscal_identity_id IS NOT NULL
            AND fiscal_sequence_policy_id IS NOT NULL
            AND fiscal_sequence_value IS NOT NULL
            AND fiscal_document_number IS NOT NULL
            AND fiscal_number_assigned_at IS NOT NULL
            AND fiscal_issuance_evidence_status = 'FISCAL_DOCUMENT_NUMBER_ASSIGNED'
            AND fiscal_number_assignment_state = 'ASSIGNED'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_references__exception_states_have_reason CHECK (
        fiscal_issuance_state NOT IN (
            'FISCAL_ISSUANCE_CONFLICT',
            'FISCAL_ISSUANCE_FAILED_REQUEST',
            'FISCAL_ISSUANCE_FAILED_CONFIGURATION',
            'FISCAL_ISSUANCE_FAILED_SERVICE',
            'FISCAL_ISSUANCE_UNKNOWN',
            'FISCAL_ISSUANCE_MANUAL_REVIEW',
            'FISCAL_ISSUANCE_EXCEPTION_RELEASED'
        )
        OR latest_exception_reason IS NOT NULL
    )
);

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS semantic_request_hash_status varchar(40);

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS semantic_request_hash_value varchar(64);

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS semantic_request_hash_algorithm varchar(32);

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS semantic_request_hash_source_version varchar(80);

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS semantic_request_hash_source_fact_count integer;

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS semantic_request_hash_safe_summary varchar(240);

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS semantic_request_hash_recorded_at timestamptz;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT fk_fiscal_issuance_references__payment_confirmation_id
    FOREIGN KEY (payment_confirmation_id)
    REFERENCES core.payment_confirmations(payment_confirmation_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT fk_fiscal_issuance_references__payment_attempt_id
    FOREIGN KEY (payment_attempt_id)
    REFERENCES core.payment_attempts(payment_attempt_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT fk_fiscal_issuance_references__parking_session_id
    FOREIGN KEY (parking_session_id)
    REFERENCES core.parking_sessions(parking_session_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT fk_fiscal_issuance_references__tariff_snapshot_id
    FOREIGN KEY (tariff_snapshot_id)
    REFERENCES core.tariff_snapshots(tariff_snapshot_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT fk_fiscal_issuance_references__site_id
    FOREIGN KEY (site_id)
    REFERENCES sites.sites(site_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT fk_fiscal_issuance_references__recorded_by_service_identity_id
    FOREIGN KEY (recorded_by_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT fk_fiscal_issuance_references__updated_by_service_identity_id
    FOREIGN KEY (updated_by_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_fiscal_issuance_references__active_payment_confirmation
    ON core.fiscal_issuance_references (payment_confirmation_id)
    WHERE is_active = true;

CREATE UNIQUE INDEX IF NOT EXISTS ux_fiscal_issuance_references__active_idempotency_scope
    ON core.fiscal_issuance_references (site_pos_server_id, fiscal_document_type_code_id, upstream_finality_reference)
    WHERE is_active = true
      AND site_pos_server_id IS NOT NULL
      AND fiscal_document_type_code_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_fiscal_issuance_references__active_pos_document
    ON core.fiscal_issuance_references (pos_server_fiscal_document_id)
    WHERE is_active = true
      AND pos_server_fiscal_document_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_fiscal_issuance_references__active_fiscal_number_scope
    ON core.fiscal_issuance_references (
        site_pos_server_id,
        fiscal_identity_id,
        fiscal_sequence_policy_id,
        fiscal_document_number
    )
    WHERE is_active = true
      AND site_pos_server_id IS NOT NULL
      AND fiscal_identity_id IS NOT NULL
      AND fiscal_sequence_policy_id IS NOT NULL
      AND fiscal_document_number IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_references__state
    ON core.fiscal_issuance_references (fiscal_issuance_state);

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_references__upstream_finality_reference
    ON core.fiscal_issuance_references (upstream_finality_reference);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_attempt_history (
    fiscal_issuance_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid,
    payment_confirmation_id uuid NOT NULL,
    attempt_sequence_number integer NOT NULL,
    trigger_source varchar(60) NOT NULL,
    action_type varchar(80) NOT NULL,
    request_correlation_id uuid,
    upstream_finality_reference varchar(200) NOT NULL,
    request_semantic_hash_ref varchar(200),
    pos_server_http_status integer,
    pos_server_response_code varchar(120),
    result_classification varchar(40),
    fiscal_issuance_evidence_status varchar(80),
    fiscal_number_assignment_state varchar(40),
    pos_server_fiscal_document_id uuid,
    error_code varchar(120),
    error_posture varchar(80),
    attempted_at timestamptz DEFAULT now() NOT NULL,
    completed_at timestamptz,
    actor_service_identity_id uuid,
    outcome_classification varchar(80) NOT NULL,
    operator_note_ref varchar(160),
    CONSTRAINT pk_fiscal_issuance_attempt_history PRIMARY KEY (fiscal_issuance_attempt_id),
    CONSTRAINT ck_fiscal_issuance_attempt_history__attempt_sequence_positive CHECK (attempt_sequence_number > 0),
    CONSTRAINT ck_fiscal_issuance_attempt_history__trigger_source CHECK (
        trigger_source IN ('AUTOMATIC', 'OPERATOR_TRIGGERED', 'RECONCILIATION_TRIGGERED')
    ),
    CONSTRAINT ck_fiscal_issuance_attempt_history__action_type CHECK (
        action_type IN ('CREATE', 'RETRY', 'REPLAY', 'READBACK', 'RECONCILIATION_CLOSE', 'MANUAL_REVIEW_ESCALATION')
    ),
    CONSTRAINT ck_fiscal_issuance_attempt_history__result_classification CHECK (
        result_classification IS NULL
        OR result_classification IN ('NEWLY_CREATED', 'IDEMPOTENT_REPLAY')
    ),
    CONSTRAINT ck_fiscal_issuance_attempt_history__evidence_status CHECK (
        fiscal_issuance_evidence_status IS NULL
        OR fiscal_issuance_evidence_status IN ('FISCAL_DOCUMENT_NUMBER_ASSIGNED')
    ),
    CONSTRAINT ck_fiscal_issuance_attempt_history__assignment_state CHECK (
        fiscal_number_assignment_state IS NULL
        OR fiscal_number_assignment_state IN ('ASSIGNED', 'NOT_ASSIGNED')
    ),
    CONSTRAINT ck_fiscal_issuance_attempt_history__error_posture CHECK (
        error_posture IS NULL
        OR error_posture IN (
            'DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE',
            'RETRY_AFTER_CONFIGURATION_CORRECTION',
            'RETRY_AFTER_SERVICE_RECOVERY'
        )
    )
);

ALTER TABLE core.fiscal_issuance_attempt_history
    ADD CONSTRAINT fk_fiscal_issuance_attempt_history__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_attempt_history
    ADD CONSTRAINT fk_fiscal_issuance_attempt_history__payment_confirmation_id
    FOREIGN KEY (payment_confirmation_id)
    REFERENCES core.payment_confirmations(payment_confirmation_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_attempt_history
    ADD CONSTRAINT fk_fiscal_issuance_attempt_history__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_fiscal_issuance_attempt_history__reference_sequence
    ON core.fiscal_issuance_attempt_history (fiscal_issuance_reference_id, attempt_sequence_number)
    WHERE fiscal_issuance_reference_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_attempt_history__payment_confirmation
    ON core.fiscal_issuance_attempt_history (payment_confirmation_id, attempted_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_exception_reviews (
    fiscal_issuance_exception_review_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid,
    payment_confirmation_id uuid NOT NULL,
    current_exception_state varchar(80) NOT NULL,
    exception_reason_code varchar(120) NOT NULL,
    exception_category varchar(80) NOT NULL,
    review_status varchar(60) NOT NULL,
    assigned_reviewer_ref varchar(160),
    supervisor_escalation_required boolean DEFAULT false NOT NULL,
    manual_release_requested boolean DEFAULT false NOT NULL,
    manual_release_reference_id uuid,
    incident_reference varchar(160),
    reconciliation_status varchar(60),
    reconciliation_closed_at timestamptz,
    reconciliation_closed_by_ref varchar(160),
    latest_readback_status varchar(80),
    latest_mismatch_reason varchar(160),
    customer_impacting boolean DEFAULT false NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_fiscal_issuance_exception_reviews PRIMARY KEY (fiscal_issuance_exception_review_id)
);

ALTER TABLE core.fiscal_issuance_exception_reviews
    ADD CONSTRAINT fk_fiscal_issuance_exception_reviews__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_exception_reviews
    ADD CONSTRAINT fk_fiscal_issuance_exception_reviews__payment_confirmation_id
    FOREIGN KEY (payment_confirmation_id)
    REFERENCES core.payment_confirmations(payment_confirmation_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_exception_reviews__queue
    ON core.fiscal_issuance_exception_reviews (review_status, exception_category, updated_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_readback_reconciliations (
    fiscal_issuance_readback_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid,
    payment_confirmation_id uuid NOT NULL,
    pos_server_fiscal_document_id uuid,
    readback_requested_at timestamptz DEFAULT now() NOT NULL,
    readback_completed_at timestamptz,
    readback_http_status integer,
    readback_result_code varchar(120),
    readback_fiscal_document_number varchar(120),
    readback_evidence_status varchar(80),
    readback_assignment_state varchar(40),
    comparison_result varchar(40) NOT NULL,
    mismatch_reason varchar(160),
    reconciliation_action varchar(120),
    reconciliation_closure_reference varchar(160),
    actor_service_identity_id uuid,
    CONSTRAINT pk_fiscal_issuance_readback_reconciliations PRIMARY KEY (fiscal_issuance_readback_id),
    CONSTRAINT ck_fiscal_issuance_readback_reconciliations__comparison_result CHECK (
        comparison_result IN ('MATCHED', 'MISMATCHED', 'INCONCLUSIVE', 'NOT_FOUND', 'SERVICE_FAILED')
    )
);

ALTER TABLE core.fiscal_issuance_readback_reconciliations
    ADD CONSTRAINT fk_fiscal_issuance_readback_reconciliations__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_readback_reconciliations
    ADD CONSTRAINT fk_fiscal_issuance_readback_reconciliations__payment_confirmation_id
    FOREIGN KEY (payment_confirmation_id)
    REFERENCES core.payment_confirmations(payment_confirmation_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_readback_reconciliations
    ADD CONSTRAINT fk_fiscal_issuance_readback_reconciliations__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_readback_reconciliations__payment_confirmation
    ON core.fiscal_issuance_readback_reconciliations (payment_confirmation_id, readback_requested_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_retry_command_preparations (
    retry_command_preparation_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid NOT NULL,
    payment_confirmation_id uuid,
    payment_attempt_id uuid,
    parking_session_id uuid,
    site_id uuid,
    site_pos_server_id uuid,
    site_pos_server_ref varchar(128),
    latest_readback_classification varchar(40),
    retry_eligibility_decision varchar(40) NOT NULL,
    command_preparation_status varchar(40) NOT NULL,
    command_block_reason_code varchar(160),
    semantic_request_hash_availability varchar(80) NOT NULL,
    idempotency_context_availability varchar(80) NOT NULL,
    attempted_at timestamptz DEFAULT now() NOT NULL,
    safe_summary varchar(240) NOT NULL,
    correlation_id uuid,
    actor_service_identity_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_fiscal_issuance_retry_command_preparations PRIMARY KEY (retry_command_preparation_attempt_id),
    CONSTRAINT ck_fiscal_issuance_retry_command_preparations__readback_classification CHECK (
        latest_readback_classification IS NULL
        OR latest_readback_classification IN (
            'MATCHED',
            'NOT_FOUND',
            'MISMATCH',
            'FAILED',
            'UNAVAILABLE',
            'UNKNOWN',
            'IDENTIFIER_MISSING',
            'NOT_SUPPORTED_YET'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_command_preparations__eligibility_decision CHECK (
        retry_eligibility_decision IN ('NOT_EVALUATED', 'ELIGIBLE', 'BLOCKED', 'UNAVAILABLE', 'NOT_REQUIRED')
    ),
    CONSTRAINT ck_fiscal_issuance_retry_command_preparations__preparation_status CHECK (
        command_preparation_status IN ('NOT_PREPARED', 'PREPARED_NON_EXECUTABLE', 'BLOCKED', 'UNAVAILABLE')
    ),
    CONSTRAINT ck_fiscal_issuance_retry_command_preparations__semantic_hash_status CHECK (
        semantic_request_hash_availability IN (
            'NOT_AVAILABLE_IN_CURRENT_MODEL',
            'AVAILABLE_AND_CONFIRMED',
            'REQUIRED_BUT_MISSING',
            'REQUIRED_BUT_UNCONFIRMED'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_command_preparations__idempotency_status CHECK (
        idempotency_context_availability IN (
            'NOT_EVALUATED',
            'AVAILABLE',
            'MISSING_UPSTREAM_FINALITY_REFERENCE',
            'NEW_UPSTREAM_FINALITY_REFERENCE_REJECTED'
        )
    )
);

ALTER TABLE core.fiscal_issuance_retry_command_preparations
    ADD CONSTRAINT fk_fiscal_issuance_retry_command_preparations__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_retry_command_preparations
    ADD CONSTRAINT fk_fiscal_issuance_retry_command_preparations__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_retry_command_preparations__reference_attempted
    ON core.fiscal_issuance_retry_command_preparations (fiscal_issuance_reference_id, attempted_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_retry_schedule_preparations (
    retry_schedule_preparation_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid NOT NULL,
    retry_command_preparation_attempt_id uuid,
    payment_confirmation_id uuid,
    payment_attempt_id uuid,
    parking_session_id uuid,
    site_id uuid,
    site_pos_server_id uuid,
    site_pos_server_ref varchar(128),
    latest_readback_classification varchar(40),
    retry_eligibility_decision varchar(40) NOT NULL,
    semantic_request_hash_availability varchar(80) NOT NULL,
    idempotency_context_availability varchar(80) NOT NULL,
    scheduling_preparation_status varchar(40) NOT NULL,
    scheduling_block_reason_code varchar(160),
    requested_at timestamptz DEFAULT now() NOT NULL,
    earliest_eligible_at timestamptz,
    safe_summary varchar(240) NOT NULL,
    correlation_id uuid,
    actor_service_identity_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_fiscal_issuance_retry_schedule_preparations PRIMARY KEY (retry_schedule_preparation_attempt_id),
    CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__readback_classification CHECK (
        latest_readback_classification IS NULL
        OR latest_readback_classification IN (
            'MATCHED',
            'NOT_FOUND',
            'MISMATCH',
            'FAILED',
            'UNAVAILABLE',
            'UNKNOWN',
            'IDENTIFIER_MISSING',
            'NOT_SUPPORTED_YET'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__eligibility_decision CHECK (
        retry_eligibility_decision IN ('NOT_EVALUATED', 'ELIGIBLE', 'BLOCKED', 'UNAVAILABLE', 'NOT_REQUIRED')
    ),
    CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__semantic_hash_status CHECK (
        semantic_request_hash_availability IN (
            'NOT_AVAILABLE_IN_CURRENT_MODEL',
            'AVAILABLE_AND_CONFIRMED',
            'REQUIRED_BUT_MISSING',
            'REQUIRED_BUT_UNCONFIRMED'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__idempotency_status CHECK (
        idempotency_context_availability IN (
            'NOT_EVALUATED',
            'AVAILABLE',
            'MISSING_UPSTREAM_FINALITY_REFERENCE',
            'NEW_UPSTREAM_FINALITY_REFERENCE_REJECTED'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__status CHECK (
        scheduling_preparation_status IN (
            'NOT_PREPARED',
            'DISABLED',
            'SCHEDULED_PREPARED',
            'BLOCKED',
            'UNAVAILABLE'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__prepared_has_command_audit CHECK (
        scheduling_preparation_status <> 'SCHEDULED_PREPARED'
        OR retry_command_preparation_attempt_id IS NOT NULL
    )
);

ALTER TABLE core.fiscal_issuance_retry_schedule_preparations
    ADD CONSTRAINT fk_fiscal_issuance_retry_schedule_preparations__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_retry_schedule_preparations
    ADD CONSTRAINT fk_fiscal_issuance_retry_schedule_preparations__command_preparation_id
    FOREIGN KEY (retry_command_preparation_attempt_id)
    REFERENCES core.fiscal_issuance_retry_command_preparations(retry_command_preparation_attempt_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_retry_schedule_preparations
    ADD CONSTRAINT fk_fiscal_issuance_retry_schedule_preparations__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_retry_schedule_preparations__reference_requested
    ON core.fiscal_issuance_retry_schedule_preparations (fiscal_issuance_reference_id, requested_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_retry_execution_attempts (
    retry_execution_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid NOT NULL,
    retry_command_preparation_attempt_id uuid,
    retry_schedule_preparation_attempt_id uuid,
    readback_classification_basis varchar(40),
    semantic_request_hash_value varchar(64),
    semantic_request_hash_algorithm varchar(32),
    semantic_request_hash_source_version varchar(80),
    upstream_finality_reference varchar(200),
    execution_status varchar(40) NOT NULL,
    block_reason_code varchar(160),
    pos_server_outcome varchar(40),
    pos_server_result_classification varchar(40),
    pos_server_fiscal_document_id uuid,
    fiscal_document_number varchar(80),
    fiscal_identity_id uuid,
    fiscal_sequence_policy_id uuid,
    fiscal_sequence_value bigint,
    fiscal_series varchar(40),
    fiscal_number_prefix_text varchar(40),
    fiscal_number_suffix_text varchar(40),
    fiscal_number_assigned_at timestamptz,
    fiscal_number_assigned_by_ref varchar(160),
    attempted_at timestamptz DEFAULT now() NOT NULL,
    completed_at timestamptz,
    actor_service_identity_id uuid,
    correlation_id uuid,
    safe_summary varchar(240) NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_fiscal_issuance_retry_execution_attempts PRIMARY KEY (retry_execution_attempt_id),
    CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__readback_classification CHECK (
        readback_classification_basis IS NULL
        OR readback_classification_basis IN (
            'MATCHED',
            'NOT_FOUND',
            'MISMATCH',
            'FAILED',
            'UNAVAILABLE',
            'UNKNOWN',
            'IDENTIFIER_MISSING',
            'NOT_SUPPORTED_YET'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__status CHECK (
        execution_status IN (
            'NOT_ATTEMPTED',
            'DISABLED',
            'DRY_RUN_READY',
            'EXECUTED',
            'REPLAY_MATCHED',
            'CONFLICT',
            'BLOCKED',
            'UNAVAILABLE',
            'UNKNOWN',
            'FAILED'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__pos_outcome CHECK (
        pos_server_outcome IS NULL
        OR pos_server_outcome IN (
            'ACCEPTED',
            'CONFLICT',
            'FAILED_REQUEST',
            'FAILED_CONFIGURATION',
            'FAILED_SERVICE',
            'INVALID_RESPONSE'
        )
    ),
    CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__result_classification CHECK (
        pos_server_result_classification IS NULL
        OR pos_server_result_classification IN ('NEWLY_CREATED', 'IDEMPOTENT_REPLAY')
    )
);

ALTER TABLE core.fiscal_issuance_retry_execution_attempts
    ADD CONSTRAINT fk_fiscal_issuance_retry_execution_attempts__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_retry_execution_attempts
    ADD CONSTRAINT fk_fiscal_issuance_retry_execution_attempts__command_preparation_id
    FOREIGN KEY (retry_command_preparation_attempt_id)
    REFERENCES core.fiscal_issuance_retry_command_preparations(retry_command_preparation_attempt_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_retry_execution_attempts
    ADD CONSTRAINT fk_fiscal_issuance_retry_execution_attempts__schedule_preparation_id
    FOREIGN KEY (retry_schedule_preparation_attempt_id)
    REFERENCES core.fiscal_issuance_retry_schedule_preparations(retry_schedule_preparation_attempt_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_retry_execution_attempts
    ADD CONSTRAINT fk_fiscal_issuance_retry_execution_attempts__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_retry_execution_attempts__reference_attempted
    ON core.fiscal_issuance_retry_execution_attempts (fiscal_issuance_reference_id, attempted_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_semantic_hash_recalculation_previews (
    semantic_hash_recalculation_preview_audit_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid NOT NULL,
    stored_semantic_hash_source_version varchar(80),
    required_semantic_hash_source_version varchar(80) NOT NULL,
    stored_semantic_hash_value varchar(64),
    recalculation_preview_status varchar(40) NOT NULL,
    recalculation_block_reason_code varchar(160),
    complete_original_request_facts_available boolean DEFAULT false NOT NULL,
    recalculated_hash_value varchar(64),
    recalculated_hash_algorithm varchar(32),
    recalculated_hash_source_version varchar(80),
    recalculated_source_fact_count integer,
    safe_source_summary varchar(240),
    recalculated_hash_matches_stored boolean,
    mutation_status varchar(40) NOT NULL,
    attempted_at timestamptz DEFAULT now() NOT NULL,
    safe_summary varchar(240) NOT NULL,
    correlation_id uuid,
    actor_service_identity_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_fiscal_issuance_semantic_hash_recalculation_previews
        PRIMARY KEY (semantic_hash_recalculation_preview_audit_id),
    CONSTRAINT ck_fiscal_issuance_semantic_hash_recalculation_previews__status CHECK (
        recalculation_preview_status IN ('NOT_REQUIRED', 'PREVIEW_CALCULATED', 'BLOCKED', 'UNAVAILABLE')
    ),
    CONSTRAINT ck_fiscal_issuance_semantic_hash_recalculation_previews__mutation CHECK (
        mutation_status IN ('NOT_MUTATED')
    ),
    CONSTRAINT ck_fiscal_issuance_semantic_hash_recalculation_previews__calculated_has_hash CHECK (
        recalculation_preview_status <> 'PREVIEW_CALCULATED'
        OR (
            complete_original_request_facts_available = true
            AND recalculated_hash_value IS NOT NULL
            AND recalculated_hash_algorithm IS NOT NULL
            AND recalculated_hash_source_version IS NOT NULL
            AND recalculated_source_fact_count IS NOT NULL
            AND recalculated_source_fact_count > 0
        )
    )
);

ALTER TABLE core.fiscal_issuance_semantic_hash_recalculation_previews
    ADD CONSTRAINT fk_fiscal_issuance_semantic_hash_recalculation_previews__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_recalculation_previews
    ADD CONSTRAINT fk_fiscal_issuance_semantic_hash_recalculation_previews__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_sem_hash_recalc_previews__reference_attempted
    ON core.fiscal_issuance_semantic_hash_recalculation_previews (fiscal_issuance_reference_id, attempted_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_semantic_hash_backfill_mutation_preparations (
    semantic_hash_backfill_mutation_audit_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid NOT NULL,
    semantic_hash_recalculation_preview_audit_id uuid,
    mutation_preparation_audit_id uuid,
    controlled_backfill_approval_status varchar(60) NOT NULL,
    old_semantic_hash_source_version varchar(80),
    required_semantic_hash_source_version varchar(80) NOT NULL,
    old_semantic_hash_value varchar(64),
    new_semantic_hash_value varchar(64),
    new_semantic_hash_algorithm varchar(32),
    new_semantic_hash_source_version varchar(80),
    new_semantic_hash_source_fact_count integer,
    safe_source_summary varchar(240),
    mutation_preparation_status varchar(60) NOT NULL,
    mutation_block_reason_code varchar(160),
    mutation_mode varchar(40) NOT NULL,
    mutation_enabled boolean DEFAULT false NOT NULL,
    fiscal_issuance_reference_mutated boolean DEFAULT false NOT NULL,
    attempted_at timestamptz DEFAULT now() NOT NULL,
    safe_summary varchar(240) NOT NULL,
    correlation_id uuid,
    actor_service_identity_id uuid,
    approval_reference varchar(160),
    dual_control_reference varchar(160),
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_fiscal_issuance_semantic_hash_backfill_mutation_preparations
        PRIMARY KEY (semantic_hash_backfill_mutation_audit_id),
    CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__approval_status CHECK (
        controlled_backfill_approval_status IN (
            'NOT_REQUIRED_CURRENT',
            'READY_FOR_CONTROLLED_BACKFILL',
            'BLOCKED',
            'PENDING_DUAL_CONTROL',
            'UNAVAILABLE'
        )
    ),
    CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__status CHECK (
        mutation_preparation_status IN (
            'NOT_PREPARED',
            'PREPARED_BUT_MUTATION_DISABLED',
            'PREPARED_FOR_CONTROLLED_MUTATION',
            'MUTATED',
            'FAILED',
            'STALE',
            'DISABLED',
            'BLOCKED',
            'UNAVAILABLE'
        )
    ),
    CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__mode CHECK (
        mutation_mode IN ('SINGLE_RECORD_ONLY')
    ),
    CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__mutation_guard CHECK (
        fiscal_issuance_reference_mutated = false
        OR (
            fiscal_issuance_reference_mutated = true
            AND mutation_preparation_status = 'MUTATED'
            AND mutation_enabled = true
            AND mutation_preparation_audit_id IS NOT NULL
        )
    ),
    CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__prepared_has_hash CHECK (
        mutation_preparation_status NOT IN (
            'PREPARED_BUT_MUTATION_DISABLED',
            'PREPARED_FOR_CONTROLLED_MUTATION',
            'MUTATED'
        )
        OR (
            semantic_hash_recalculation_preview_audit_id IS NOT NULL
            AND new_semantic_hash_value IS NOT NULL
            AND new_semantic_hash_algorithm IS NOT NULL
            AND new_semantic_hash_source_version IS NOT NULL
            AND new_semantic_hash_source_fact_count IS NOT NULL
            AND new_semantic_hash_source_fact_count > 0
        )
    )
);

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_mutation__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_mutation__preview_audit_id
    FOREIGN KEY (semantic_hash_recalculation_preview_audit_id)
    REFERENCES core.fiscal_issuance_semantic_hash_recalculation_previews(semantic_hash_recalculation_preview_audit_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_mutation__prep_audit_id
    FOREIGN KEY (mutation_preparation_audit_id)
    REFERENCES core.fiscal_issuance_semantic_hash_backfill_mutation_preparations(semantic_hash_backfill_mutation_audit_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_mutation__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_sem_hash_backfill_mutation__reference_attempted
    ON core.fiscal_issuance_semantic_hash_backfill_mutation_preparations (fiscal_issuance_reference_id, attempted_at DESC);

CREATE TABLE IF NOT EXISTS core.fiscal_issuance_semantic_hash_backfill_workflow_requests (
    semantic_hash_backfill_workflow_request_id uuid DEFAULT gen_random_uuid() NOT NULL,
    fiscal_issuance_reference_id uuid NOT NULL,
    semantic_hash_recalculation_preview_audit_id uuid,
    mutation_preparation_audit_id uuid,
    approval_reference varchar(160),
    dual_control_reference varchar(160),
    actor_service_identity_id uuid,
    reason_code varchar(80),
    safe_justification varchar(240),
    request_mode varchar(40) NOT NULL,
    workflow_status varchar(80) NOT NULL,
    workflow_block_reason_code varchar(160),
    mutation_invocation_posture varchar(40) NOT NULL,
    guarded_mutation_audit_id uuid,
    guarded_mutation_status varchar(60),
    execute_controlled_mutation_requested boolean DEFAULT false NOT NULL,
    mutation_invocation_enabled boolean DEFAULT false NOT NULL,
    dry_run_only boolean DEFAULT true NOT NULL,
    requested_at timestamptz DEFAULT now() NOT NULL,
    correlation_id uuid,
    safe_summary varchar(240) NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_fiscal_sem_hash_backfill_workflow_requests
        PRIMARY KEY (semantic_hash_backfill_workflow_request_id),
    CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__request_mode CHECK (
        request_mode IN ('SINGLE_RECORD_ONLY', 'BATCH')
    ),
    CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__status CHECK (
        workflow_status IN (
            'NOT_REQUESTED',
            'READY_FOR_OPERATOR_APPROVAL',
            'PREPARED_BUT_MUTATION_INVOCATION_DISABLED',
            'MUTATION_INVOKED',
            'BLOCKED',
            'UNAVAILABLE'
        )
    ),
    CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__invocation_posture CHECK (
        mutation_invocation_posture IN (
            'NOT_REQUESTED',
            'DRY_RUN_ONLY',
            'DISABLED',
            'INVOKED',
            'BLOCKED'
        )
    ),
    CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__guarded_status CHECK (
        guarded_mutation_status IS NULL
        OR guarded_mutation_status IN (
            'NOT_PREPARED',
            'PREPARED_BUT_MUTATION_DISABLED',
            'PREPARED_FOR_CONTROLLED_MUTATION',
            'MUTATED',
            'FAILED',
            'STALE',
            'DISABLED',
            'BLOCKED',
            'UNAVAILABLE'
        )
    )
);

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_workflow_requests
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_workflow__reference_id
    FOREIGN KEY (fiscal_issuance_reference_id)
    REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_workflow_requests
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_workflow__preview_audit_id
    FOREIGN KEY (semantic_hash_recalculation_preview_audit_id)
    REFERENCES core.fiscal_issuance_semantic_hash_recalculation_previews(semantic_hash_recalculation_preview_audit_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_workflow_requests
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_workflow__mutation_prep_audit_id
    FOREIGN KEY (mutation_preparation_audit_id)
    REFERENCES core.fiscal_issuance_semantic_hash_backfill_mutation_preparations(semantic_hash_backfill_mutation_audit_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_workflow_requests
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_workflow__guarded_mutation_audit_id
    FOREIGN KEY (guarded_mutation_audit_id)
    REFERENCES core.fiscal_issuance_semantic_hash_backfill_mutation_preparations(semantic_hash_backfill_mutation_audit_id)
    DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_workflow_requests
    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_workflow__actor_service_identity_id
    FOREIGN KEY (actor_service_identity_id)
    REFERENCES identity.service_identities(service_identity_id)
    DEFERRABLE INITIALLY IMMEDIATE;

CREATE INDEX IF NOT EXISTS ix_fiscal_sem_hash_backfill_workflow__reference_requested
    ON core.fiscal_issuance_semantic_hash_backfill_workflow_requests (fiscal_issuance_reference_id, requested_at DESC);

COMMENT ON TABLE core.fiscal_issuance_references IS
    'Central PMS v1.3 persistence scaffold for POS Server fiscal issuance reference evidence. Persistence/state only; no POS Server network or ExitAuthorization gating behavior.';
COMMENT ON TABLE core.fiscal_issuance_attempt_history IS
    'Central PMS v1.3 fiscal issuance attempt/history scaffold for future retry, replay, conflict, and reconciliation slices.';
COMMENT ON TABLE core.fiscal_issuance_exception_reviews IS
    'Central PMS v1.3 fiscal issuance exception/review scaffold for future Operator Console governance queues.';
COMMENT ON TABLE core.fiscal_issuance_readback_reconciliations IS
    'Central PMS v1.3 fiscal readback/reconciliation scaffold for future GET readback and reconciliation slices.';
COMMENT ON TABLE core.fiscal_issuance_retry_command_preparations IS
    'Central PMS v1.3 FEQ retry command preparation audit records only. No retry execution, scheduler, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
COMMENT ON TABLE core.fiscal_issuance_retry_schedule_preparations IS
    'Central PMS v1.3 FEQ retry scheduling preparation audit records only. No executable retry job, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
COMMENT ON TABLE core.fiscal_issuance_retry_execution_attempts IS
    'Central PMS v1.3 FEQ controlled retry execution attempt audit records. Single-record feature-flagged POST path only; no public endpoint, batch retry, scheduler job, ExitAuthorization, or gate behavior.';
COMMENT ON TABLE core.fiscal_issuance_semantic_hash_recalculation_previews IS
    'Central PMS v1.3 FEQ semantic hash recalculation preview audit records only. No hash backfill mutation, retry execution, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
COMMENT ON TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations IS
    'Central PMS v1.3 FEQ semantic hash controlled single-record backfill mutation audit records. No automatic batch backfill, retry execution, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
COMMENT ON TABLE core.fiscal_issuance_semantic_hash_backfill_workflow_requests IS
    'Central PMS v1.3 FEQ semantic hash internal operator workflow request audit records. Single-record governed request posture only; no public UI, batch backfill, retry execution, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
