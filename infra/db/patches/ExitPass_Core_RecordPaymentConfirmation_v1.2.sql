/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.10 Payment Processing and Confirmation
 * - 10.7.9 Provider Outcome Traceability Invariant
 * - 10.7.10 Idempotent Payment Confirmation Invariant
 *
 * SDD:
 * - 6.4 Finalize Payment
 * - 7.3 Provider Callback / Confirmation Handling
 * - 9.6 Integrity Constraints and Concurrency Rules
 *
 * System Invariants:
 * - PaymentConfirmation must be anchored to an existing PaymentAttempt.
 * - Provider transaction references must not create ambiguous duplicate confirmation evidence.
 * - Confirmed provider evidence moves the canonical PaymentAttempt to terminal CONFIRMED state.
 * - All writes use ExitPass v1.2 table names and service-identity audit attribution.
 */

CREATE OR REPLACE FUNCTION core.record_payment_confirmation(
    p_payment_attempt_id uuid,
    p_provider_reference text,
    p_provider_status text,
    p_requested_by text,
    p_correlation_id uuid,
    p_now timestamptz
)
RETURNS TABLE (
    payment_confirmation_id uuid,
    payment_attempt_id uuid,
    provider_reference text,
    provider_status text,
    verified_timestamp timestamptz
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_attempt core.payment_attempts%ROWTYPE;
    v_confirmation core.payment_confirmations%ROWTYPE;
    v_requested_by_service_identity_id uuid;
    v_normalized_provider_reference text;
    v_normalized_provider_status text;
BEGIN
    v_normalized_provider_reference := NULLIF(btrim(p_provider_reference), '');
    v_normalized_provider_status := NULLIF(upper(btrim(p_provider_status)), '');

    IF v_normalized_provider_reference IS NULL THEN
        RAISE EXCEPTION 'provider reference is required'
            USING ERRCODE = '22023';
    END IF;

    IF v_normalized_provider_status IS NULL THEN
        RAISE EXCEPTION 'provider status is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_requested_by IS NOT NULL AND btrim(p_requested_by) <> '' THEN
        BEGIN
            v_requested_by_service_identity_id := p_requested_by::uuid;
        EXCEPTION
            WHEN invalid_text_representation THEN
                SELECT si.service_identity_id
                INTO v_requested_by_service_identity_id
                FROM identity.service_identities AS si
                WHERE si.service_identity_code = p_requested_by
                LIMIT 1;
        END;
    END IF;

    SELECT pa.*
    INTO v_attempt
    FROM core.payment_attempts AS pa
    WHERE pa.payment_attempt_id = p_payment_attempt_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'payment attempt % was not found', p_payment_attempt_id
            USING ERRCODE = 'P0002';
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_attempt.updated_by_service_identity_id;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_attempt.created_by_service_identity_id;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        SELECT si.service_identity_id
        INTO v_requested_by_service_identity_id
        FROM identity.service_identities AS si
        WHERE si.identity_status = 'ACTIVE'
        ORDER BY si.created_at DESC
        LIMIT 1;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        RAISE EXCEPTION 'requested_by service identity could not be resolved'
            USING ERRCODE = 'P0002';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM core.payment_confirmations AS pc
        WHERE pc.payment_attempt_id = p_payment_attempt_id
    ) THEN
        RAISE EXCEPTION 'payment confirmation already exists for payment attempt %', p_payment_attempt_id
            USING
                ERRCODE = '23505',
                CONSTRAINT = 'uq_payment_confirmations__payment_attempt';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM core.payment_confirmations AS pc
        WHERE pc.payment_rail_id IS NOT DISTINCT FROM v_attempt.payment_rail_id
          AND pc.provider_transaction_ref = v_normalized_provider_reference
    ) THEN
        RAISE EXCEPTION 'provider reference % has already been recorded', v_normalized_provider_reference
            USING
                ERRCODE = '23505',
                CONSTRAINT = 'ux_payment_confirmations__provider_transaction_ref';
    END IF;

    INSERT INTO core.payment_confirmations (
        payment_confirmation_id,
        payment_attempt_id,
        provider_outcome_id,
        payment_rail_id,
        provider_transaction_ref,
        currency_code,
        confirmed_amount,
        confirmation_status,
        verified_at,
        confirmed_at,
        correlation_id,
        created_at,
        created_by_service_identity_id
    )
    VALUES (
        gen_random_uuid(),
        p_payment_attempt_id,
        NULL,
        v_attempt.payment_rail_id,
        v_normalized_provider_reference,
        v_attempt.currency_code,
        v_attempt.amount,
        'RECORDED',
        p_now,
        p_now,
        p_correlation_id,
        p_now,
        v_requested_by_service_identity_id
    )
    RETURNING *
    INTO v_confirmation;

    IF v_normalized_provider_status IN ('SUCCESS', 'SUCCEEDED', 'PAID', 'CONFIRMED') THEN
        UPDATE core.payment_attempts AS pa
        SET
            attempt_status = 'CONFIRMED',
            finalized_at = COALESCE(pa.finalized_at, p_now),
            failure_reason_code = NULL,
            updated_at = p_now,
            updated_by_service_identity_id = v_requested_by_service_identity_id,
            row_version = pa.row_version + 1
        WHERE pa.payment_attempt_id = p_payment_attempt_id;
    ELSIF v_normalized_provider_status IN ('FAILED', 'DECLINED', 'CANCELLED', 'EXPIRED') THEN
        UPDATE core.payment_attempts AS pa
        SET
            attempt_status = 'FAILED',
            finalized_at = COALESCE(pa.finalized_at, p_now),
            failure_reason_code = v_normalized_provider_status,
            updated_at = p_now,
            updated_by_service_identity_id = v_requested_by_service_identity_id,
            row_version = pa.row_version + 1
        WHERE pa.payment_attempt_id = p_payment_attempt_id;
    END IF;

    RETURN QUERY
    SELECT
        v_confirmation.payment_confirmation_id::uuid,
        v_confirmation.payment_attempt_id::uuid,
        v_confirmation.provider_transaction_ref::text,
        v_normalized_provider_status::text,
        v_confirmation.verified_at::timestamptz;
END;
$function$;
