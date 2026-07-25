/*
 * ExitPass v1.3 app-local durable SQL patch.
 *
 * Scope:
 * - Operator Console review linkage/read model for service-channel-originated
 *   statutory-discount decision-v2 commands in AWAITING_REVIEW.
 *
 * Non-goals:
 * - No new decision authority.
 * - No payable-basis application command creation.
 * - No payable-basis mutation, payment finality, fiscal issuance,
 *   ExitAuthorization, gate behavior, statutory calculation, or VAT change.
 */

BEGIN;

CREATE SCHEMA IF NOT EXISTS operator_console;

CREATE TABLE IF NOT EXISTS operator_console.statutory_discount_service_channel_reviews (
    statutory_discount_decision_command_id uuid NOT NULL,
    request_reference uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    source_channel varchar(64) NOT NULL,
    site_id uuid NULL,
    site_group_id uuid NULL,
    ticket_reference varchar(160) NULL,
    plate_number varchar(32) NULL,
    entitlement_type varchar(64) NOT NULL,
    id_document_type varchar(64) NULL,
    issuing_authority varchar(160) NULL,
    expiry_date date NULL,
    masked_id_reference varchar(128) NULL,
    evidence_references jsonb NOT NULL DEFAULT '[]'::jsonb,
    requester_attestation boolean NOT NULL DEFAULT false,
    attestation_notes varchar(512) NULL,
    reason_code varchar(128) NULL,
    original_tariff_snapshot_id uuid NULL,
    review_status varchar(64) NOT NULL,
    reviewer_user_id uuid NULL,
    reviewer_operator_device_binding_id uuid NULL,
    reviewer_operator_shift_id uuid NULL,
    reviewer_access_evaluation_id uuid NULL,
    reviewer_decision varchar(16) NULL,
    reviewer_decision_reason_code varchar(128) NULL,
    intake_correlation_id uuid NOT NULL,
    review_correlation_id uuid NULL,
    submitted_at timestamptz NOT NULL,
    reviewed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_stat_disc_service_channel_reviews
        PRIMARY KEY (statutory_discount_decision_command_id),
    CONSTRAINT fk_stat_disc_svc_reviews__decision_command
        FOREIGN KEY (statutory_discount_decision_command_id)
        REFERENCES discounts.statutory_discount_decision_commands(statutory_discount_decision_command_id),
    CONSTRAINT fk_stat_disc_svc_reviews__parking_session
        FOREIGN KEY (parking_session_id)
        REFERENCES core.parking_sessions(parking_session_id),
    CONSTRAINT fk_stat_disc_svc_reviews__site
        FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id),
    CONSTRAINT fk_stat_disc_svc_reviews__original_tariff_snapshot
        FOREIGN KEY (original_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id),
    CONSTRAINT ck_stat_disc_svc_reviews__source_channel
        CHECK (source_channel IN ('WEBPAY', 'ASSISTED_PAYMENT_TERMINAL')),
    CONSTRAINT ck_stat_disc_svc_reviews__entitlement_type
        CHECK (entitlement_type IN ('SENIOR_CITIZEN', 'PWD')),
    CONSTRAINT ck_stat_disc_svc_reviews__review_status
        CHECK (review_status IN ('PENDING_REVIEW', 'APPROVED', 'REJECTED', 'REVIEW_FACTS_UNAVAILABLE')),
    CONSTRAINT ck_stat_disc_svc_reviews__reviewer_decision
        CHECK (reviewer_decision IS NULL OR reviewer_decision IN ('APPROVE', 'REJECT')),
    CONSTRAINT ck_stat_disc_svc_reviews__review_completion
        CHECK (
            (review_status = 'PENDING_REVIEW'
                AND reviewer_user_id IS NULL
                AND reviewer_decision IS NULL
                AND reviewed_at IS NULL)
            OR
            (review_status IN ('APPROVED', 'REJECTED')
                AND reviewer_user_id IS NOT NULL
                AND reviewer_access_evaluation_id IS NOT NULL
                AND reviewer_decision IS NOT NULL
                AND reviewed_at IS NOT NULL)
            OR review_status = 'REVIEW_FACTS_UNAVAILABLE'
        ),
    CONSTRAINT ck_stat_disc_svc_reviews__evidence_json
        CHECK (jsonb_typeof(evidence_references) = 'array')
);

CREATE INDEX IF NOT EXISTS ix_stat_disc_svc_reviews__pending_queue
    ON operator_console.statutory_discount_service_channel_reviews
    (review_status, site_id, submitted_at, statutory_discount_decision_command_id)
    WHERE review_status = 'PENDING_REVIEW';

CREATE INDEX IF NOT EXISTS ix_stat_disc_svc_reviews__source_status
    ON operator_console.statutory_discount_service_channel_reviews
    (source_channel, review_status, submitted_at);

COMMENT ON TABLE operator_console.statutory_discount_service_channel_reviews IS
    'Safe Operator Console review linkage/read model for service-channel statutory-discount decision-v2 commands. It stores masked/reference-only submitted facts and reviewer attribution, not raw evidence, raw IDs, payable-basis application commands, payment finality, fiscal issuance, ExitAuthorization, or gate state.';

COMMIT;
