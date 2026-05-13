/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.9 Payment Initiation
 * - 9.13 Timeout, Retry, and Duplicate Handling
 * - 10.7.4 One Active Payment Attempt Per Session
 * - 18.11 Payment Attempt Integrity
 *
 * SDD:
 * - 6.3 Initiate Payment Attempt
 * - 8.3 PaymentAttempt State Machine
 * - 9.6 Integrity Constraints and Concurrency Rules
 *
 * System Invariants:
 * - PaymentAttempt creation must be anchored to an existing ParkingSession and TariffSnapshot.
 * - Idempotency-key replay must return the existing PaymentAttempt deterministically.
 * - Concurrent callers must not create duplicate active PaymentAttempts for one ParkingSession.
 * - TariffSnapshot consumption and PaymentAttempt creation are serialized by database row locks.
 * - All writes use ExitPass v1.2 table names, v1.2 enums, and service-identity audit attribution.
 */

CREATE OR REPLACE FUNCTION core.create_or_reuse_payment_attempt(
    p_parking_session_id uuid,
    p_tariff_snapshot_id uuid,
    p_payment_provider_code text,
    p_idempotency_key text,
    p_requested_by text,
    p_correlation_id uuid,
    p_now timestamptz
)
RETURNS TABLE (
    payment_attempt_id uuid,
    parking_session_id uuid,
    tariff_snapshot_id uuid,
    attempt_status text,
    payment_provider_code text,
    was_reused boolean,
    outcome_code text,
    failure_code text,
    gross_amount_snapshot numeric,
    statutory_discount_snapshot numeric,
    coupon_discount_snapshot numeric,
    net_amount_due_snapshot numeric,
    currency_code text,
    tariff_version_reference text
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_session core.parking_sessions%ROWTYPE;
    v_tariff core.tariff_snapshots%ROWTYPE;
    v_existing core.payment_attempts%ROWTYPE;
    v_created core.payment_attempts%ROWTYPE;

    v_requested_by_service_identity_id uuid;
    v_payment_rail_id uuid;
    v_resolved_payment_provider_code text;
BEGIN
    /*
     * ExitPass v1.2 BRD 9.9 and SDD 6.3 require PaymentAttempt creation to carry service audit attribution.
     * The routine accepts UUID text, service_identity_code, seeded row attribution, or latest active service identity.
     */
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

    SELECT ps.*
    INTO v_session
    FROM core.parking_sessions AS ps
    WHERE ps.parking_session_id = p_parking_session_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN QUERY
        SELECT
            NULL::uuid,
            p_parking_session_id::uuid,
            p_tariff_snapshot_id::uuid,
            NULL::text,
            COALESCE(p_payment_provider_code, '')::text,
            FALSE::boolean,
            'PARKING_SESSION_NOT_FOUND'::text,
            'PARKING_SESSION_NOT_FOUND'::text,
            0::numeric,
            0::numeric,
            0::numeric,
            0::numeric,
            ''::text,
            NULL::text;
        RETURN;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_session.created_by_service_identity_id;
    END IF;

    SELECT ts.*
    INTO v_tariff
    FROM core.tariff_snapshots AS ts
    WHERE ts.tariff_snapshot_id = p_tariff_snapshot_id
      AND ts.parking_session_id = p_parking_session_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN QUERY
        SELECT
            NULL::uuid,
            p_parking_session_id::uuid,
            p_tariff_snapshot_id::uuid,
            NULL::text,
            COALESCE(p_payment_provider_code, '')::text,
            FALSE::boolean,
            'TARIFF_SNAPSHOT_NOT_FOUND'::text,
            'TARIFF_SNAPSHOT_NOT_FOUND'::text,
            0::numeric,
            0::numeric,
            0::numeric,
            0::numeric,
            ''::text,
            NULL::text;
        RETURN;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_tariff.created_by_service_identity_id;
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
        RETURN QUERY
        SELECT
            NULL::uuid,
            p_parking_session_id::uuid,
            p_tariff_snapshot_id::uuid,
            NULL::text,
            COALESCE(p_payment_provider_code, '')::text,
            FALSE::boolean,
            'REQUESTED_BY_SERVICE_IDENTITY_NOT_FOUND'::text,
            'REQUESTED_BY_SERVICE_IDENTITY_NOT_FOUND'::text,
            COALESCE(v_tariff.gross_amount, 0)::numeric,
            COALESCE(v_tariff.statutory_discount_amount, 0)::numeric,
            COALESCE(v_tariff.coupon_discount_amount, 0)::numeric,
            COALESCE(v_tariff.net_amount, 0)::numeric,
            COALESCE(v_tariff.currency_code::text, '')::text,
            v_tariff.tariff_version_reference::text;
        RETURN;
    END IF;

    /*
     * ExitPass v1.2 BRD 9.13 and SDD 9.6 require retry-safe idempotency-key replay.
     */
    SELECT pa.*
    INTO v_existing
    FROM core.payment_attempts AS pa
    WHERE pa.idempotency_key = p_idempotency_key
    LIMIT 1;

    IF FOUND THEN
        RETURN QUERY
        SELECT
            v_existing.payment_attempt_id::uuid,
            v_existing.parking_session_id::uuid,
            v_existing.tariff_snapshot_id::uuid,
            v_existing.attempt_status::text,
            COALESCE(p_payment_provider_code, v_resolved_payment_provider_code, '')::text,
            TRUE::boolean,
            'REUSED_BY_IDEMPOTENCY_KEY'::text,
            NULL::text,
            COALESCE(v_tariff.gross_amount, v_existing.amount)::numeric,
            COALESCE(v_tariff.statutory_discount_amount, 0)::numeric,
            COALESCE(v_tariff.coupon_discount_amount, 0)::numeric,
            v_existing.amount::numeric,
            v_existing.currency_code::text,
            v_tariff.tariff_version_reference::text;
        RETURN;
    END IF;

    /*
     * ExitPass v1.2 BRD 10.7.4 and SDD 9.6 require one active PaymentAttempt per ParkingSession.
     */
    SELECT pa.*
    INTO v_existing
    FROM core.payment_attempts AS pa
    WHERE pa.parking_session_id = p_parking_session_id
      AND pa.attempt_status IN (
          'REQUESTED',
          'PENDING_PROVIDER',
          'PENDING_FINALIZATION'
      )
    ORDER BY pa.created_at DESC
    LIMIT 1;

    IF FOUND THEN
        RETURN QUERY
        SELECT
            v_existing.payment_attempt_id::uuid,
            v_existing.parking_session_id::uuid,
            v_existing.tariff_snapshot_id::uuid,
            v_existing.attempt_status::text,
            COALESCE(p_payment_provider_code, v_resolved_payment_provider_code, '')::text,
            TRUE::boolean,
            'ACTIVE_ATTEMPT_EXISTS'::text,
            NULL::text,
            COALESCE(v_tariff.gross_amount, v_existing.amount)::numeric,
            COALESCE(v_tariff.statutory_discount_amount, 0)::numeric,
            COALESCE(v_tariff.coupon_discount_amount, 0)::numeric,
            v_existing.amount::numeric,
            v_existing.currency_code::text,
            v_tariff.tariff_version_reference::text;
        RETURN;
    END IF;

    /*
     * ExitPass v1.2 SDD 6.3 requires the immutable TariffSnapshot to be eligible before it can be consumed.
     */
    IF v_tariff.snapshot_status <> 'ACTIVE'
       OR v_tariff.consumed_at IS NOT NULL
       OR v_tariff.expires_at <= p_now
       OR v_tariff.superseded_by_tariff_snapshot_id IS NOT NULL THEN
        RETURN QUERY
        SELECT
            NULL::uuid,
            p_parking_session_id::uuid,
            p_tariff_snapshot_id::uuid,
            NULL::text,
            COALESCE(p_payment_provider_code, '')::text,
            FALSE::boolean,
            'TARIFF_SNAPSHOT_NOT_ELIGIBLE'::text,
            'TARIFF_SNAPSHOT_NOT_ELIGIBLE'::text,
            v_tariff.gross_amount::numeric,
            v_tariff.statutory_discount_amount::numeric,
            v_tariff.coupon_discount_amount::numeric,
            v_tariff.net_amount::numeric,
            v_tariff.currency_code::text,
            v_tariff.tariff_version_reference::text;
        RETURN;
    END IF;

    /*
     * payment_rail_id is stored for internal settlement/routing.
     * The returned payment_provider_code preserves p_payment_provider_code because the public API/test
     * expects the requested method/rail code, for example GCASH, not the backend provider, for example PAYMONGO.
     */
    SELECT pr.payment_rail_id, pr.provider_code
    INTO v_payment_rail_id, v_resolved_payment_provider_code
    FROM payments.payment_rails AS pr
    WHERE pr.rail_status = 'ACTIVE'
      AND pr.supported_currency_code = v_tariff.currency_code
      AND (
             pr.provider_code = p_payment_provider_code
          OR pr.rail_code = p_payment_provider_code
      )
      AND pr.effective_from <= p_now
      AND (pr.effective_to IS NULL OR pr.effective_to > p_now)
    ORDER BY pr.is_primary DESC, pr.created_at ASC
    LIMIT 1;

    IF v_payment_rail_id IS NULL THEN
        SELECT pr.payment_rail_id, pr.provider_code
        INTO v_payment_rail_id, v_resolved_payment_provider_code
        FROM payments.payment_rails AS pr
        WHERE pr.rail_status = 'ACTIVE'
          AND pr.supported_currency_code = v_tariff.currency_code
          AND pr.effective_from <= p_now
          AND (pr.effective_to IS NULL OR pr.effective_to > p_now)
        ORDER BY pr.is_primary DESC, pr.created_at ASC
        LIMIT 1;
    END IF;

    IF v_payment_rail_id IS NULL THEN
        RETURN QUERY
        SELECT
            NULL::uuid,
            p_parking_session_id::uuid,
            p_tariff_snapshot_id::uuid,
            NULL::text,
            COALESCE(p_payment_provider_code, '')::text,
            FALSE::boolean,
            'PAYMENT_RAIL_NOT_FOUND'::text,
            'PAYMENT_RAIL_NOT_FOUND'::text,
            v_tariff.gross_amount::numeric,
            v_tariff.statutory_discount_amount::numeric,
            v_tariff.coupon_discount_amount::numeric,
            v_tariff.net_amount::numeric,
            v_tariff.currency_code::text,
            v_tariff.tariff_version_reference::text;
        RETURN;
    END IF;

    INSERT INTO core.payment_attempts (
        payment_attempt_id,
        parking_session_id,
        tariff_snapshot_id,
        idempotency_key,
        payment_rail_id,
        currency_code,
        amount,
        attempt_status,
        requested_at,
        expires_at,
        finalized_at,
        failure_reason_code,
        correlation_id,
        created_at,
        created_by_service_identity_id,
        updated_at,
        updated_by_service_identity_id,
        row_version
    )
    VALUES (
        gen_random_uuid(),
        p_parking_session_id,
        p_tariff_snapshot_id,
        p_idempotency_key,
        v_payment_rail_id,
        v_tariff.currency_code,
        v_tariff.net_amount,
        'REQUESTED',
        p_now,
        p_now + INTERVAL '15 minutes',
        NULL,
        NULL,
        p_correlation_id,
        p_now,
        v_requested_by_service_identity_id,
        p_now,
        v_requested_by_service_identity_id,
        1
    )
    RETURNING *
    INTO v_created;

    UPDATE core.tariff_snapshots AS ts
    SET
        consumed_at = p_now,
        snapshot_status = 'CONSUMED',
        updated_at = p_now,
        updated_by_service_identity_id = v_requested_by_service_identity_id,
        row_version = ts.row_version + 1
    WHERE ts.tariff_snapshot_id = p_tariff_snapshot_id;

    RETURN QUERY
    SELECT
        v_created.payment_attempt_id::uuid,
        v_created.parking_session_id::uuid,
        v_created.tariff_snapshot_id::uuid,
        v_created.attempt_status::text,
        COALESCE(p_payment_provider_code, v_resolved_payment_provider_code, '')::text,
        FALSE::boolean,
        'CREATED'::text,
        NULL::text,
        v_tariff.gross_amount::numeric,
        v_tariff.statutory_discount_amount::numeric,
        v_tariff.coupon_discount_amount::numeric,
        v_tariff.net_amount::numeric,
        v_tariff.currency_code::text,
        v_tariff.tariff_version_reference::text;
END;
$function$;
