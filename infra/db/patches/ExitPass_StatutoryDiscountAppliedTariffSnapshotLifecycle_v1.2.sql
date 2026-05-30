/*
 * ExitPass v1.2 durable SQL patch.
 *
 * Statutory discount final APPLIED tariff snapshot lifecycle support.
 *
 * References:
 * - docs/operator-console/statutory-discount-applied-tariff-snapshot-lifecycle-design.md
 * - docs/operator-console/statutory-discount-payable-basis-application-design.md
 *
 * System invariants:
 * - The original tariff snapshot amount fields are not mutated by this routine.
 * - The original tariff snapshot may transition from ACTIVE to SUPERSEDED as lifecycle metadata only.
 * - The applied statutory discount payable basis is represented by one new ACTIVE tariff snapshot.
 * - The payable-basis application row is the durable idempotency/audit anchor.
 * - This patch does not create payment attempts, payment confirmations, provider outcomes, exit authorizations,
 *   gate consumptions, coupon applications, settlement truth, reconciliation records, or AUB objects.
 */

CREATE UNIQUE INDEX IF NOT EXISTS ux_tariff_snapshots__statutory_discount_validation_applied
    ON core.tariff_snapshots (statutory_discount_validation_id)
    WHERE statutory_discount_validation_id IS NOT NULL;

COMMENT ON INDEX core.ux_tariff_snapshots__statutory_discount_validation_applied IS
    'Ensures one statutory-discount-adjusted tariff snapshot per statutory discount validation.';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'core'
          AND cls.relname = 'tariff_snapshots'
          AND con.conname = 'ck_tariff_snapshots__statutory_discount_link_has_discount'
    ) THEN
        ALTER TABLE core.tariff_snapshots
            ADD CONSTRAINT ck_tariff_snapshots__statutory_discount_link_has_discount
            CHECK (
                statutory_discount_validation_id IS NULL
                OR statutory_discount_amount > 0
            );
    END IF;
END $$;

COMMENT ON CONSTRAINT ck_tariff_snapshots__statutory_discount_link_has_discount
    ON core.tariff_snapshots IS
    'A tariff snapshot linked to a statutory discount validation must carry a positive statutory discount amount.';

CREATE OR REPLACE FUNCTION discounts.apply_statutory_discount_payable_basis(
    p_statutory_discount_payable_basis_application_id uuid,
    p_actor_user_id uuid,
    p_correlation_id uuid
)
RETURNS TABLE (
    statutory_discount_payable_basis_application_id uuid,
    statutory_discount_validation_id uuid,
    parking_session_id uuid,
    original_tariff_snapshot_id uuid,
    applied_tariff_snapshot_id uuid,
    application_status text,
    previous_tariff_snapshot_status text,
    applied_tariff_snapshot_status text,
    final_payable_amount_minor_units bigint,
    currency_code text,
    already_applied boolean,
    outcome_code text,
    failure_code text
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_now timestamptz := now();
    v_application discounts.statutory_discount_payable_basis_applications%ROWTYPE;
    v_validation discounts.statutory_discount_validations%ROWTYPE;
    v_session core.parking_sessions%ROWTYPE;
    v_original_tariff core.tariff_snapshots%ROWTYPE;
    v_applied_tariff core.tariff_snapshots%ROWTYPE;
    v_applied_tariff_snapshot_id uuid;
    v_payment_attempt_exists boolean;
BEGIN
    SET CONSTRAINTS ALL DEFERRED;

    SELECT app.*
    INTO v_application
    FROM discounts.statutory_discount_payable_basis_applications AS app
    WHERE app.statutory_discount_payable_basis_application_id = p_statutory_discount_payable_basis_application_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN QUERY
        SELECT
            p_statutory_discount_payable_basis_application_id,
            NULL::uuid,
            NULL::uuid,
            NULL::uuid,
            NULL::uuid,
            NULL::text,
            NULL::text,
            NULL::text,
            NULL::bigint,
            NULL::text,
            FALSE,
            'PAYABLE_BASIS_APPLICATION_NOT_FOUND'::text,
            'PAYABLE_BASIS_APPLICATION_NOT_FOUND'::text;
        RETURN;
    END IF;

    IF v_application.application_status = 'APPLIED'
       AND v_application.applied_tariff_snapshot_id IS NOT NULL THEN
        SELECT ts.*
        INTO v_original_tariff
        FROM core.tariff_snapshots AS ts
        WHERE ts.tariff_snapshot_id = v_application.original_tariff_snapshot_id;

        SELECT ts.*
        INTO v_applied_tariff
        FROM core.tariff_snapshots AS ts
        WHERE ts.tariff_snapshot_id = v_application.applied_tariff_snapshot_id;

        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            v_application.applied_tariff_snapshot_id,
            v_application.application_status::text,
            CASE WHEN v_original_tariff.tariff_snapshot_id IS NULL THEN NULL ELSE v_original_tariff.snapshot_status::text END,
            CASE WHEN v_applied_tariff.tariff_snapshot_id IS NULL THEN NULL ELSE v_applied_tariff.snapshot_status::text END,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            TRUE,
            'ALREADY_APPLIED'::text,
            NULL::text;
        RETURN;
    END IF;

    IF v_application.application_status <> 'REQUESTED' THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            v_application.applied_tariff_snapshot_id,
            v_application.application_status::text,
            NULL::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'PAYABLE_BASIS_APPLICATION_NOT_REQUESTED'::text,
            'PAYABLE_BASIS_APPLICATION_NOT_REQUESTED'::text;
        RETURN;
    END IF;

    SELECT sdv.*
    INTO v_validation
    FROM discounts.statutory_discount_validations AS sdv
    WHERE sdv.statutory_discount_validation_id = v_application.statutory_discount_validation_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            NULL::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND'::text,
            'STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND'::text;
        RETURN;
    END IF;

    IF v_validation.validation_status <> 'APPROVED' THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            NULL::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'STATUTORY_DISCOUNT_NOT_APPROVED'::text,
            'STATUTORY_DISCOUNT_NOT_APPROVED'::text;
        RETURN;
    END IF;

    IF v_validation.evidence_required AND NOT v_validation.evidence_captured THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            NULL::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'EVIDENCE_REQUIRED_NOT_CAPTURED'::text,
            'EVIDENCE_REQUIRED_NOT_CAPTURED'::text;
        RETURN;
    END IF;

    SELECT ps.*
    INTO v_session
    FROM core.parking_sessions AS ps
    WHERE ps.parking_session_id = v_application.parking_session_id
    FOR UPDATE;

    IF NOT FOUND OR v_session.session_status <> 'ACTIVE' THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            NULL::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'SESSION_NOT_ELIGIBLE'::text,
            'SESSION_NOT_ELIGIBLE'::text;
        RETURN;
    END IF;

    IF v_validation.parking_session_id <> v_application.parking_session_id THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            NULL::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'PAYABLE_BASIS_APPLICATION_FAILED'::text,
            'PAYABLE_BASIS_APPLICATION_FAILED'::text;
        RETURN;
    END IF;

    SELECT ts.*
    INTO v_original_tariff
    FROM core.tariff_snapshots AS ts
    WHERE ts.tariff_snapshot_id = v_application.original_tariff_snapshot_id
    FOR UPDATE;

    IF NOT FOUND OR v_original_tariff.parking_session_id <> v_application.parking_session_id THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            NULL::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'TARIFF_SNAPSHOT_NOT_FOUND'::text,
            'TARIFF_SNAPSHOT_NOT_FOUND'::text;
        RETURN;
    END IF;

    IF v_original_tariff.snapshot_status <> 'ACTIVE'
       OR v_original_tariff.consumed_at IS NOT NULL
       OR v_original_tariff.superseded_by_tariff_snapshot_id IS NOT NULL
       OR v_original_tariff.expires_at <= v_now THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            v_original_tariff.snapshot_status::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'TARIFF_SNAPSHOT_NOT_ELIGIBLE'::text,
            'TARIFF_SNAPSHOT_NOT_ELIGIBLE'::text;
        RETURN;
    END IF;

    SELECT EXISTS (
        SELECT 1
        FROM core.payment_attempts AS pa
        WHERE pa.parking_session_id = v_application.parking_session_id
           OR pa.tariff_snapshot_id = v_application.original_tariff_snapshot_id
    )
    INTO v_payment_attempt_exists;

    IF v_payment_attempt_exists THEN
        RETURN QUERY
        SELECT
            v_application.statutory_discount_payable_basis_application_id,
            v_application.statutory_discount_validation_id,
            v_application.parking_session_id,
            v_application.original_tariff_snapshot_id,
            NULL::uuid,
            v_application.application_status::text,
            v_original_tariff.snapshot_status::text,
            NULL::text,
            v_application.final_payable_amount_minor_units,
            v_application.currency_code::text,
            FALSE,
            'PAYMENT_ATTEMPT_ALREADY_EXISTS'::text,
            'PAYMENT_ATTEMPT_ALREADY_EXISTS'::text;
        RETURN;
    END IF;

    v_applied_tariff_snapshot_id := gen_random_uuid();

    UPDATE core.tariff_snapshots AS original
    SET snapshot_status = 'SUPERSEDED',
        superseded_by_tariff_snapshot_id = v_applied_tariff_snapshot_id,
        updated_at = v_now,
        updated_by_service_identity_id = COALESCE(
            v_original_tariff.updated_by_service_identity_id,
            v_original_tariff.created_by_service_identity_id
        ),
        row_version = original.row_version + 1
    WHERE original.tariff_snapshot_id = v_original_tariff.tariff_snapshot_id;

    INSERT INTO core.tariff_snapshots (
        tariff_snapshot_id,
        parking_session_id,
        vendor_system_id,
        vendor_tariff_ref,
        tariff_version_reference,
        currency_code,
        gross_amount,
        statutory_discount_amount,
        coupon_discount_amount,
        net_amount,
        statutory_discount_validation_id,
        coupon_application_id,
        snapshot_status,
        calculated_at,
        expires_at,
        consumed_at,
        correlation_id,
        created_at,
        created_by_service_identity_id,
        updated_at,
        updated_by_service_identity_id,
        row_version
    )
    VALUES (
        v_applied_tariff_snapshot_id,
        v_original_tariff.parking_session_id,
        v_original_tariff.vendor_system_id,
        v_original_tariff.vendor_tariff_ref,
        CASE
            WHEN v_original_tariff.tariff_version_reference IS NULL THEN 'STATUTORY_DISCOUNT_APPLIED'
            ELSE v_original_tariff.tariff_version_reference || '|STATUTORY_DISCOUNT_APPLIED'
        END,
        v_application.currency_code,
        (v_application.gross_amount_minor_units::numeric / 100),
        (v_application.statutory_discount_amount_minor_units::numeric / 100),
        0,
        (v_application.final_payable_amount_minor_units::numeric / 100),
        v_application.statutory_discount_validation_id,
        NULL,
        'ACTIVE',
        v_now,
        v_original_tariff.expires_at,
        NULL,
        COALESCE(p_correlation_id, v_application.correlation_id),
        v_now,
        v_original_tariff.created_by_service_identity_id,
        v_now,
        COALESCE(v_original_tariff.updated_by_service_identity_id, v_original_tariff.created_by_service_identity_id),
        1
    );

    UPDATE discounts.statutory_discount_validations AS sdv
    SET tariff_snapshot_id = v_applied_tariff_snapshot_id,
        currency_code = v_application.currency_code,
        gross_amount_at_validation = (v_application.gross_amount_minor_units::numeric / 100),
        statutory_discount_amount = (v_application.statutory_discount_amount_minor_units::numeric / 100),
        net_amount_after_discount = (v_application.final_payable_amount_minor_units::numeric / 100),
        updated_at = v_now,
        updated_by_user_id = p_actor_user_id,
        row_version = sdv.row_version + 1
    WHERE sdv.statutory_discount_validation_id = v_application.statutory_discount_validation_id;

    UPDATE discounts.statutory_discount_payable_basis_applications AS app
    SET applied_tariff_snapshot_id = v_applied_tariff_snapshot_id,
        application_status = 'APPLIED',
        applied_at = v_now,
        applied_by_user_id = p_actor_user_id,
        correlation_id = COALESCE(p_correlation_id, app.correlation_id),
        updated_at = v_now,
        updated_by_user_id = p_actor_user_id,
        row_version = app.row_version + 1
    WHERE app.statutory_discount_payable_basis_application_id = v_application.statutory_discount_payable_basis_application_id
    RETURNING *
    INTO v_application;

    SELECT ts.*
    INTO v_applied_tariff
    FROM core.tariff_snapshots AS ts
    WHERE ts.tariff_snapshot_id = v_applied_tariff_snapshot_id;

    RETURN QUERY
    SELECT
        v_application.statutory_discount_payable_basis_application_id,
        v_application.statutory_discount_validation_id,
        v_application.parking_session_id,
        v_application.original_tariff_snapshot_id,
        v_application.applied_tariff_snapshot_id,
        v_application.application_status::text,
        'ACTIVE'::text,
        v_applied_tariff.snapshot_status::text,
        v_application.final_payable_amount_minor_units,
        v_application.currency_code::text,
        FALSE,
        'APPLIED'::text,
        NULL::text;
END;
$function$;

COMMENT ON FUNCTION discounts.apply_statutory_discount_payable_basis(uuid, uuid, uuid) IS
    'Finalizes a REQUESTED statutory discount payable-basis application by superseding the original active tariff snapshot, creating one statutory-adjusted ACTIVE tariff snapshot, and marking the application APPLIED. The routine does not create payment, provider, gate, coupon, reconciliation, or AUB records.';
