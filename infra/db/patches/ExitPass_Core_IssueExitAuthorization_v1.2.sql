/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.12 Exit Authorization
 * - 9.13 Timeout, Retry, and Duplicate Handling
 * - 10.7.2 Payment Finality Invariant
 * - 10.7.7 Exit Token Integrity Invariant
 *
 * SDD:
 * - 6.5 Issue Exit Authorization
 * - 8.5 ExitAuthorization State Machine
 * - 9.6 Integrity Constraints and Concurrency Rules
 *
 * System Invariants:
 * - ExitAuthorization must be anchored to an existing finalized/confirmed PaymentAttempt.
 * - ExitAuthorization must be tied back to the canonical ParkingSession and PaymentConfirmation chain.
 * - Replayed issuance for the same confirmed PaymentAttempt returns the existing authorization deterministically.
 * - All writes use ExitPass v1.2 table names, v1.2 enums, hashed token storage, and service-identity audit attribution.
 */

CREATE OR REPLACE FUNCTION core.issue_exit_authorization(
    p_parking_session_id uuid,
    p_payment_attempt_id uuid,
    p_requested_by uuid,
    p_correlation_id uuid,
    p_now timestamptz
)
RETURNS TABLE (
    exit_authorization_id uuid,
    parking_session_id uuid,
    payment_attempt_id uuid,
    authorization_token text,
    authorization_status text,
    issued_at timestamptz,
    expiration_timestamp timestamptz
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_attempt core.payment_attempts%ROWTYPE;
    v_confirmation core.payment_confirmations%ROWTYPE;
    v_authorization core.exit_authorizations%ROWTYPE;
    v_requested_by_service_identity_id uuid;
    v_authorization_token text;
BEGIN
    SELECT pa.*
    INTO v_attempt
    FROM core.payment_attempts AS pa
    WHERE pa.payment_attempt_id = p_payment_attempt_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'payment attempt % was not found', p_payment_attempt_id
            USING ERRCODE = 'P0002';
    END IF;

    IF v_attempt.parking_session_id <> p_parking_session_id THEN
        RAISE EXCEPTION 'payment attempt % does not belong to parking session %', p_payment_attempt_id, p_parking_session_id
            USING ERRCODE = 'P0001';
    END IF;

    SELECT ea.*
    INTO v_authorization
    FROM core.exit_authorizations AS ea
    WHERE ea.payment_attempt_id = p_payment_attempt_id
    FOR UPDATE;

    IF FOUND THEN
        RETURN QUERY
        SELECT
            v_authorization.exit_authorization_id::uuid,
            v_authorization.parking_session_id::uuid,
            v_authorization.payment_attempt_id::uuid,
            v_authorization.exit_authorization_id::text,
            v_authorization.authorization_status::text,
            v_authorization.issued_at::timestamptz,
            v_authorization.expires_at::timestamptz;
        RETURN;
    END IF;

    IF v_attempt.attempt_status <> 'CONFIRMED' THEN
        RAISE EXCEPTION 'payment attempt % is not confirmed', p_payment_attempt_id
            USING ERRCODE = 'P0001';
    END IF;

    SELECT pc.*
    INTO v_confirmation
    FROM core.payment_confirmations AS pc
    WHERE pc.payment_attempt_id = p_payment_attempt_id
      AND pc.confirmation_status = 'RECORDED'
    ORDER BY pc.confirmed_at DESC, pc.created_at DESC
    LIMIT 1;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'payment attempt % has no recorded payment confirmation', p_payment_attempt_id
            USING ERRCODE = 'P0001';
    END IF;

    IF v_confirmation.payment_attempt_id <> v_attempt.payment_attempt_id THEN
        RAISE EXCEPTION 'payment confirmation % is not anchored to payment attempt %', v_confirmation.payment_confirmation_id, p_payment_attempt_id
            USING ERRCODE = 'P0001';
    END IF;

    SELECT si.service_identity_id
    INTO v_requested_by_service_identity_id
    FROM identity.service_identities AS si
    WHERE si.service_identity_id = p_requested_by
    LIMIT 1;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_attempt.updated_by_service_identity_id;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_confirmation.created_by_service_identity_id;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_attempt.created_by_service_identity_id;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        RAISE EXCEPTION 'requested_by service identity could not be resolved'
            USING ERRCODE = 'P0002';
    END IF;

    v_authorization_token := 'EXIT-' || replace(gen_random_uuid()::text, '-', '');

    INSERT INTO core.exit_authorizations (
        exit_authorization_id,
        parking_session_id,
        payment_attempt_id,
        payment_confirmation_id,
        authorization_token_hash,
        authorization_status,
        issued_at,
        expires_at,
        correlation_id,
        created_at,
        created_by_service_identity_id,
        updated_at,
        updated_by_service_identity_id
    )
    VALUES (
        gen_random_uuid(),
        v_attempt.parking_session_id,
        v_attempt.payment_attempt_id,
        v_confirmation.payment_confirmation_id,
        encode(digest(v_authorization_token, 'sha256'), 'hex'),
        'ISSUED',
        p_now,
        p_now + interval '15 minutes',
        p_correlation_id,
        p_now,
        v_requested_by_service_identity_id,
        p_now,
        v_requested_by_service_identity_id
    )
    RETURNING *
    INTO v_authorization;

    RETURN QUERY
    SELECT
        v_authorization.exit_authorization_id::uuid,
        v_authorization.parking_session_id::uuid,
        v_authorization.payment_attempt_id::uuid,
        v_authorization_token::text,
        v_authorization.authorization_status::text,
        v_authorization.issued_at::timestamptz,
        v_authorization.expires_at::timestamptz;
END;
$function$;
