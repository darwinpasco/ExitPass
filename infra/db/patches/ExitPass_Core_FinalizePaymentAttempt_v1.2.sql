/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.10 Payment Processing and Confirmation
 * - 9.13 Timeout, Retry, and Duplicate Handling
 * - 10.7.2 Payment Finality Invariant
 * - 10.7.9 Provider Outcome Traceability Invariant
 *
 * SDD:
 * - 6.4 Finalize Payment
 * - 10.5.3 Report Verified Payment Outcome
 * - 9.6 Integrity Constraints and Concurrency Rules
 *
 * System Invariants:
 * - PaymentAttempt finalization must be anchored to an existing v1.2 PaymentAttempt.
 * - Terminal PaymentAttempt state transitions must be deterministic and replay-safe.
 * - A conflicting terminal replay must not overwrite prior finality.
 * - All writes use ExitPass v1.2 table names, v1.2 enums, and service-identity audit attribution.
 */

CREATE OR REPLACE FUNCTION core.finalize_payment_attempt(
    p_payment_attempt_id uuid,
    p_final_attempt_status text,
    p_requested_by text,
    p_correlation_id uuid,
    p_now timestamptz
)
RETURNS TABLE (
    payment_attempt_id uuid,
    attempt_status text
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_attempt core.payment_attempts%ROWTYPE;
    v_requested_by_service_identity_id uuid;
    v_final_attempt_status text;
BEGIN
    v_final_attempt_status := NULLIF(upper(btrim(p_final_attempt_status)), '');

    IF v_final_attempt_status IS NULL THEN
        RAISE EXCEPTION 'final attempt status is required'
            USING ERRCODE = '22023';
    END IF;

    IF v_final_attempt_status NOT IN ('CONFIRMED', 'FAILED', 'EXPIRED', 'CANCELLED') THEN
        RAISE EXCEPTION 'final attempt status % is not supported for finalization', v_final_attempt_status
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

    IF v_attempt.attempt_status IN ('CONFIRMED', 'FAILED', 'EXPIRED', 'CANCELLED') THEN
        IF v_attempt.attempt_status::text = v_final_attempt_status THEN
            RETURN QUERY
            SELECT
                v_attempt.payment_attempt_id::uuid,
                v_attempt.attempt_status::text;
            RETURN;
        END IF;

        RAISE EXCEPTION 'payment attempt % is already final with status %', p_payment_attempt_id, v_attempt.attempt_status
            USING ERRCODE = 'P0001';
    END IF;

    UPDATE core.payment_attempts AS pa
    SET
        attempt_status = v_final_attempt_status::core.payment_attempt_status_enum,
        finalized_at = p_now,
        failure_reason_code = CASE
            WHEN v_final_attempt_status = 'FAILED' THEN 'PROVIDER_REPORTED_FAILURE'
            WHEN v_final_attempt_status IN ('EXPIRED', 'CANCELLED') THEN v_final_attempt_status
            ELSE NULL
        END,
        correlation_id = COALESCE(p_correlation_id, pa.correlation_id),
        updated_at = p_now,
        updated_by_service_identity_id = v_requested_by_service_identity_id,
        row_version = pa.row_version + 1
    WHERE pa.payment_attempt_id = p_payment_attempt_id
    RETURNING pa.*
    INTO v_attempt;

    RETURN QUERY
    SELECT
        v_attempt.payment_attempt_id::uuid,
        v_attempt.attempt_status::text;
END;
$function$;
