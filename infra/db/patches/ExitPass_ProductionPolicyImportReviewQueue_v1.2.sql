/*
 * ExitPass v1.2 durable SQL patch.
 *
 * Operator Console production policy import review queue persistence.
 *
 * System invariants:
 * - This patch creates only review queue persistence objects.
 * - Approval means APPROVED_FOR_DB_REPO_ALIGNMENT only.
 * - This patch does not import, seed, activate, or approve production policy registry rows.
 * - This patch does not create production policy import execution jobs.
 */

CREATE SCHEMA IF NOT EXISTS operator_console;

CREATE TABLE IF NOT EXISTS operator_console.production_policy_import_review_submissions (
    review_id uuid DEFAULT gen_random_uuid() NOT NULL,
    maker_operator_id uuid NOT NULL,
    file_name varchar(512),
    submission_fingerprint varchar(64) NOT NULL,
    review_status varchar(64) NOT NULL,
    dry_run_result_json jsonb NOT NULL,
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz DEFAULT now() NOT NULL,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_policy_import_review_submissions PRIMARY KEY (review_id),
    CONSTRAINT ck_policy_import_review_submissions__status
        CHECK (review_status IN (
            'DRAFT_DRY_RUN',
            'SUBMITTED_FOR_REVIEW',
            'LEGAL_REVIEW_PENDING',
            'OPS_REVIEW_PENDING',
            'QA_REVIEW_PENDING',
            'DB_REVIEW_PENDING',
            'APPROVED_FOR_DB_REPO_ALIGNMENT',
            'REJECTED',
            'CANCELLED',
            'SUPERSEDED'
        )),
    CONSTRAINT ck_policy_import_review_submissions__fingerprint
        CHECK (submission_fingerprint ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_policy_import_review_submissions__dry_run_only
        CHECK (
            COALESCE((dry_run_result_json ->> 'isDryRun')::boolean, false) = true
            AND COALESCE((dry_run_result_json ->> 'policiesImported')::boolean, true) = false
        ),
    CONSTRAINT ck_policy_import_review_submissions__row_version_positive
        CHECK (row_version > 0)
);

COMMENT ON TABLE operator_console.production_policy_import_review_submissions IS
    'DB-backed Operator Console production policy import review submissions. Approval does not import or activate policy rows.';

CREATE UNIQUE INDEX IF NOT EXISTS ux_policy_import_review_submissions__active_fingerprint
    ON operator_console.production_policy_import_review_submissions (maker_operator_id, submission_fingerprint)
    WHERE review_status NOT IN ('REJECTED', 'CANCELLED', 'SUPERSEDED');

CREATE INDEX IF NOT EXISTS ix_policy_import_review_submissions__status
    ON operator_console.production_policy_import_review_submissions (review_status, updated_at DESC);

CREATE TABLE IF NOT EXISTS operator_console.production_policy_import_review_decisions (
    review_decision_id uuid DEFAULT gen_random_uuid() NOT NULL,
    review_id uuid NOT NULL,
    reviewer_role varchar(32) NOT NULL,
    decision_action varchar(64) NOT NULL,
    reviewer_operator_id uuid NOT NULL,
    reason text,
    decided_at timestamptz DEFAULT now() NOT NULL,
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_policy_import_review_decisions PRIMARY KEY (review_decision_id),
    CONSTRAINT fk_policy_import_review_decisions__review
        FOREIGN KEY (review_id)
        REFERENCES operator_console.production_policy_import_review_submissions(review_id)
        ON DELETE CASCADE,
    CONSTRAINT uq_policy_import_review_decisions__review_role
        UNIQUE (review_id, reviewer_role),
    CONSTRAINT ck_policy_import_review_decisions__role
        CHECK (reviewer_role IN ('LEGAL', 'OPS', 'QA', 'DB')),
    CONSTRAINT ck_policy_import_review_decisions__action
        CHECK (decision_action IN ('APPROVE_LEGAL', 'APPROVE_OPS', 'APPROVE_QA', 'APPROVE_DB')),
    CONSTRAINT ck_policy_import_review_decisions__reason
        CHECK (reason IS NULL OR btrim(reason) <> '')
);

COMMENT ON TABLE operator_console.production_policy_import_review_decisions IS
    'Per-role checker approvals for production policy import review alignment. Contains no import or activation action.';

CREATE INDEX IF NOT EXISTS ix_policy_import_review_decisions__review
    ON operator_console.production_policy_import_review_decisions (review_id, decided_at);

CREATE TABLE IF NOT EXISTS operator_console.production_policy_import_review_history (
    review_history_id uuid DEFAULT gen_random_uuid() NOT NULL,
    review_id uuid NOT NULL,
    history_fingerprint varchar(64) NOT NULL,
    decision_action varchar(64) NOT NULL,
    review_status varchar(64) NOT NULL,
    actor_operator_id uuid NOT NULL,
    reviewer_role varchar(32),
    reason text,
    occurred_at timestamptz DEFAULT now() NOT NULL,
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_policy_import_review_history PRIMARY KEY (review_history_id),
    CONSTRAINT fk_policy_import_review_history__review
        FOREIGN KEY (review_id)
        REFERENCES operator_console.production_policy_import_review_submissions(review_id)
        ON DELETE CASCADE,
    CONSTRAINT uq_policy_import_review_history__fingerprint
        UNIQUE (review_id, history_fingerprint),
    CONSTRAINT ck_policy_import_review_history__fingerprint
        CHECK (history_fingerprint ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_policy_import_review_history__status
        CHECK (review_status IN (
            'DRAFT_DRY_RUN',
            'SUBMITTED_FOR_REVIEW',
            'LEGAL_REVIEW_PENDING',
            'OPS_REVIEW_PENDING',
            'QA_REVIEW_PENDING',
            'DB_REVIEW_PENDING',
            'APPROVED_FOR_DB_REPO_ALIGNMENT',
            'REJECTED',
            'CANCELLED',
            'SUPERSEDED'
        )),
    CONSTRAINT ck_policy_import_review_history__action
        CHECK (decision_action IN (
            'SUBMIT_FOR_REVIEW',
            'REQUEST_CHANGES',
            'APPROVE_LEGAL',
            'APPROVE_OPS',
            'APPROVE_QA',
            'APPROVE_DB',
            'REJECT',
            'ESCALATE',
            'CANCEL',
            'MARK_SUPERSEDED'
        )),
    CONSTRAINT ck_policy_import_review_history__role
        CHECK (reviewer_role IS NULL OR reviewer_role IN ('LEGAL', 'OPS', 'QA', 'DB')),
    CONSTRAINT ck_policy_import_review_history__reason
        CHECK (reason IS NULL OR btrim(reason) <> '')
);

COMMENT ON TABLE operator_console.production_policy_import_review_history IS
    'Decision history for Operator Console production policy import review. Final approval is DB repo alignment only.';

CREATE INDEX IF NOT EXISTS ix_policy_import_review_history__review
    ON operator_console.production_policy_import_review_history (review_id, occurred_at);

CREATE TABLE IF NOT EXISTS operator_console.production_policy_import_review_findings (
    review_finding_id uuid DEFAULT gen_random_uuid() NOT NULL,
    review_id uuid NOT NULL,
    finding_fingerprint varchar(64) NOT NULL,
    severity varchar(16) NOT NULL,
    message text NOT NULL,
    field_name varchar(128),
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_policy_import_review_findings PRIMARY KEY (review_finding_id),
    CONSTRAINT fk_policy_import_review_findings__review
        FOREIGN KEY (review_id)
        REFERENCES operator_console.production_policy_import_review_submissions(review_id)
        ON DELETE CASCADE,
    CONSTRAINT uq_policy_import_review_findings__fingerprint
        UNIQUE (review_id, finding_fingerprint),
    CONSTRAINT ck_policy_import_review_findings__fingerprint
        CHECK (finding_fingerprint ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_policy_import_review_findings__severity
        CHECK (severity IN ('PASS', 'WARN', 'FAIL')),
    CONSTRAINT ck_policy_import_review_findings__message
        CHECK (btrim(message) <> '')
);

COMMENT ON TABLE operator_console.production_policy_import_review_findings IS
    'Review-level findings generated by the production policy import review queue workflow.';

CREATE INDEX IF NOT EXISTS ix_policy_import_review_findings__review
    ON operator_console.production_policy_import_review_findings (review_id, created_at);
