/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.12 Exit Authorization
 * - 9.13 Timeout, Retry, and Duplicate Handling
 * - 10.7.7 Exit Token Integrity Invariant
 * - 10.7.8 Single-Use Consume Invariant
 *
 * SDD:
 * - 6.6 Consume Exit Authorization
 * - 8.5 ExitAuthorization State Machine
 * - 9.6 Integrity Constraints and Concurrency Rules
 *
 * System Invariants:
 * - AuthorizationConsumption must be anchored to an existing issued ExitAuthorization.
 * - Consumption must be single-use, auditable, and deterministic.
 * - Expired, invalidated, and already-consumed authorizations fail closed.
 * - All writes use ExitPass v1.2 core and gates table names, v1.2 enums, and service-identity audit attribution.
 */

CREATE OR REPLACE FUNCTION core.consume_exit_authorization(
    p_exit_authorization_id uuid,
    p_requested_by uuid,
    p_correlation_id uuid,
    p_now timestamptz
)
RETURNS TABLE (
    exit_authorization_id uuid,
    authorization_status text,
    consumed_at timestamptz
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_authorization core.exit_authorizations%ROWTYPE;
    v_session core.parking_sessions%ROWTYPE;
    v_existing_consumed_at timestamptz;
    v_requested_by_service_identity_id uuid;
    v_consumption gates.gate_authorization_consumptions%ROWTYPE;
BEGIN
    SELECT ea.*
    INTO v_authorization
    FROM core.exit_authorizations AS ea
    WHERE ea.exit_authorization_id = p_exit_authorization_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'exit authorization % was not found', p_exit_authorization_id
            USING ERRCODE = 'P0002';
    END IF;

    SELECT ps.*
    INTO v_session
    FROM core.parking_sessions AS ps
    WHERE ps.parking_session_id = v_authorization.parking_session_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'parking session % was not found for exit authorization %',
            v_authorization.parking_session_id,
            p_exit_authorization_id
            USING ERRCODE = 'P0002';
    END IF;

    SELECT gac.consumed_at
    INTO v_existing_consumed_at
    FROM gates.gate_authorization_consumptions AS gac
    WHERE gac.exit_authorization_id = p_exit_authorization_id
      AND gac.consume_status = 'CONSUMED'
    ORDER BY gac.consumed_at DESC
    LIMIT 1;

    IF FOUND THEN
        RAISE EXCEPTION 'exit authorization % has already been consumed', p_exit_authorization_id
            USING ERRCODE = 'P0001';
    END IF;

    IF v_authorization.authorization_status <> 'ISSUED' THEN
        RAISE EXCEPTION 'exit authorization % is not issued', p_exit_authorization_id
            USING ERRCODE = 'P0001';
    END IF;

    IF v_authorization.expires_at <= p_now THEN
        INSERT INTO gates.gate_authorization_consumptions (
            gate_authorization_consumption_id,
            exit_authorization_id,
            authorization_token_hash,
            site_id,
            consume_status,
            consume_reason_code,
            requested_at,
            validated_at,
            command_requested,
            command_result_status,
            failure_detail,
            correlation_id,
            created_at,
            created_by_service_identity_id,
            updated_at,
            updated_by_service_identity_id
        )
        VALUES (
            gen_random_uuid(),
            v_authorization.exit_authorization_id,
            v_authorization.authorization_token_hash,
            v_session.site_id,
            'EXPIRED',
            'EXIT_AUTHORIZATION_EXPIRED',
            p_now,
            p_now,
            false,
            'NOT_REQUESTED',
            'Exit authorization expired before consume.',
            p_correlation_id,
            p_now,
            COALESCE(v_authorization.updated_by_service_identity_id, v_authorization.created_by_service_identity_id),
            p_now,
            COALESCE(v_authorization.updated_by_service_identity_id, v_authorization.created_by_service_identity_id)
        );

        RAISE EXCEPTION 'exit authorization % is expired', p_exit_authorization_id
            USING ERRCODE = 'P0001';
    END IF;

    SELECT si.service_identity_id
    INTO v_requested_by_service_identity_id
    FROM identity.service_identities AS si
    WHERE si.service_identity_id = p_requested_by
    LIMIT 1;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_authorization.updated_by_service_identity_id;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        v_requested_by_service_identity_id := v_authorization.created_by_service_identity_id;
    END IF;

    IF v_requested_by_service_identity_id IS NULL THEN
        RAISE EXCEPTION 'requested_by service identity could not be resolved'
            USING ERRCODE = 'P0002';
    END IF;

    INSERT INTO gates.gate_authorization_consumptions (
        gate_authorization_consumption_id,
        exit_authorization_id,
        authorization_token_hash,
        site_id,
        consume_status,
        consume_reason_code,
        requested_at,
        validated_at,
        consumed_at,
        command_requested,
        command_result_status,
        command_result_at,
        correlation_id,
        created_at,
        created_by_service_identity_id,
        updated_at,
        updated_by_service_identity_id
    )
    VALUES (
        gen_random_uuid(),
        v_authorization.exit_authorization_id,
        v_authorization.authorization_token_hash,
        v_session.site_id,
        'CONSUMED',
        'EXIT_AUTHORIZATION_CONSUMED',
        p_now,
        p_now,
        p_now,
        true,
        'REQUESTED',
        p_now,
        p_correlation_id,
        p_now,
        v_requested_by_service_identity_id,
        p_now,
        v_requested_by_service_identity_id
    )
    RETURNING *
    INTO v_consumption;

    UPDATE core.exit_authorizations AS ea
    SET
        updated_at = p_now,
        updated_by_service_identity_id = v_requested_by_service_identity_id,
        row_version = ea.row_version + 1
    WHERE ea.exit_authorization_id = p_exit_authorization_id;

    RETURN QUERY
    SELECT
        v_authorization.exit_authorization_id::uuid,
        'CONSUMED'::text,
        v_consumption.consumed_at::timestamptz;
END;
$function$;
