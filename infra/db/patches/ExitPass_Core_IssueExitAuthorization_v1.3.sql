/*
 * ExitPass v1.3 canonical ExitAuthorization issuance.
 *
 * Extends the v1.2 paid-finality routine with the already-established
 * ZERO_PAYABLE_STATUTORY_FINALITY completion basis. The routine re-derives statutory
 * and fiscal ancestry from durable rows and never creates payment artifacts.
 */

DROP FUNCTION IF EXISTS core.issue_exit_authorization(uuid, uuid, uuid, uuid, timestamptz);
DROP FUNCTION IF EXISTS core.issue_exit_authorization(
    uuid, uuid, uuid, uuid, timestamptz, varchar, uuid, uuid, uuid, uuid, uuid, uuid);

CREATE FUNCTION core.issue_exit_authorization(
    p_parking_session_id uuid,
    p_payment_attempt_id uuid,
    p_requested_by uuid,
    p_correlation_id uuid,
    p_now timestamptz,
    p_completion_basis varchar DEFAULT 'PAYMENT_FINALITY',
    p_tariff_snapshot_id uuid DEFAULT NULL,
    p_statutory_discount_decision_command_id uuid DEFAULT NULL,
    p_statutory_discount_payable_basis_application_command_id uuid DEFAULT NULL,
    p_statutory_discount_validation_id uuid DEFAULT NULL,
    p_applied_policy_reference_id uuid DEFAULT NULL,
    p_fiscal_issuance_reference_id uuid DEFAULT NULL
)
RETURNS TABLE (
    exit_authorization_id uuid,
    parking_session_id uuid,
    payment_attempt_id uuid,
    authorization_token text,
    authorization_status text,
    issued_at timestamptz,
    expiration_timestamp timestamptz,
    completion_basis text,
    tariff_snapshot_id uuid,
    fiscal_issuance_reference_id uuid
)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_attempt core.payment_attempts%ROWTYPE;
    v_confirmation core.payment_confirmations%ROWTYPE;
    v_authorization core.exit_authorizations%ROWTYPE;
    v_application_command discounts.statutory_discount_payable_basis_application_commands%ROWTYPE;
    v_statutory_application discounts.statutory_discount_payable_basis_applications%ROWTYPE;
    v_requested_by_service_identity_id uuid;
    v_authorization_token text;
    v_fiscal_reference_id uuid;
    v_statutory_ancestry_valid boolean;
    v_fiscal_ancestry_valid boolean;
    v_payment_conflict boolean;
BEGIN
    IF p_parking_session_id IS NULL OR p_requested_by IS NULL OR
       p_correlation_id IS NULL OR p_now IS NULL THEN
        RAISE EXCEPTION 'ExitAuthorization issuance requires complete request facts'
            USING ERRCODE = 'P0001';
    END IF;

    IF p_completion_basis = 'PAYMENT_FINALITY' THEN
        IF p_payment_attempt_id IS NULL OR p_tariff_snapshot_id IS NOT NULL OR
           p_statutory_discount_decision_command_id IS NOT NULL OR
           p_statutory_discount_payable_basis_application_command_id IS NOT NULL OR
           p_statutory_discount_validation_id IS NOT NULL OR
           p_applied_policy_reference_id IS NOT NULL OR
           p_fiscal_issuance_reference_id IS NOT NULL THEN
            RAISE EXCEPTION 'PAYMENT_FINALITY issuance has incompatible completion ancestry'
                USING ERRCODE = 'P0001';
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

        IF v_attempt.parking_session_id <> p_parking_session_id THEN
            RAISE EXCEPTION 'payment attempt % does not belong to parking session %',
                p_payment_attempt_id, p_parking_session_id
                USING ERRCODE = 'P0001';
        END IF;

        SELECT ea.*
        INTO v_authorization
        FROM core.exit_authorizations AS ea
        WHERE ea.payment_attempt_id = p_payment_attempt_id
        FOR UPDATE;

        IF FOUND THEN
            IF v_authorization.completion_basis <> 'PAYMENT_FINALITY' OR
               v_authorization.parking_session_id <> p_parking_session_id THEN
                RAISE EXCEPTION 'existing ExitAuthorization has incompatible payment ancestry'
                    USING ERRCODE = 'P0001';
            END IF;

            RETURN QUERY SELECT
                v_authorization.exit_authorization_id,
                v_authorization.parking_session_id,
                v_authorization.payment_attempt_id,
                v_authorization.exit_authorization_id::text,
                v_authorization.authorization_status::text,
                v_authorization.issued_at,
                v_authorization.expires_at,
                v_authorization.completion_basis::text,
                v_authorization.tariff_snapshot_id,
                NULL::uuid;
            RETURN;
        END IF;

        IF v_attempt.attempt_status <> 'CONFIRMED' OR v_attempt.finalized_at IS NULL THEN
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

        IF NOT FOUND OR v_confirmation.payment_attempt_id <> v_attempt.payment_attempt_id THEN
            RAISE EXCEPTION 'payment attempt % has no reconciled recorded payment confirmation',
                p_payment_attempt_id
                USING ERRCODE = 'P0001';
        END IF;

        SELECT si.service_identity_id
        INTO v_requested_by_service_identity_id
        FROM identity.service_identities AS si
        WHERE si.service_identity_id = p_requested_by
        LIMIT 1;

        v_requested_by_service_identity_id := COALESCE(
            v_requested_by_service_identity_id,
            v_attempt.updated_by_service_identity_id,
            v_confirmation.created_by_service_identity_id,
            v_attempt.created_by_service_identity_id);

        IF v_requested_by_service_identity_id IS NULL THEN
            RAISE EXCEPTION 'requested_by service identity could not be resolved'
                USING ERRCODE = 'P0002';
        END IF;

        v_authorization_token := 'EXIT-' || replace(gen_random_uuid()::text, '-', '');

        INSERT INTO core.exit_authorizations (
            exit_authorization_id, parking_session_id, payment_attempt_id,
            payment_confirmation_id, tariff_snapshot_id, completion_basis,
            authorization_token_hash, authorization_status, issued_at, expires_at,
            correlation_id, created_at, created_by_service_identity_id,
            updated_at, updated_by_service_identity_id)
        VALUES (
            gen_random_uuid(), v_attempt.parking_session_id, v_attempt.payment_attempt_id,
            v_confirmation.payment_confirmation_id, v_attempt.tariff_snapshot_id,
            'PAYMENT_FINALITY', encode(digest(v_authorization_token, 'sha256'), 'hex'),
            'ISSUED', p_now, p_now + interval '15 minutes', p_correlation_id, p_now,
            v_requested_by_service_identity_id, p_now, v_requested_by_service_identity_id)
        RETURNING * INTO v_authorization;

        RETURN QUERY SELECT
            v_authorization.exit_authorization_id,
            v_authorization.parking_session_id,
            v_authorization.payment_attempt_id,
            v_authorization_token,
            v_authorization.authorization_status::text,
            v_authorization.issued_at,
            v_authorization.expires_at,
            v_authorization.completion_basis::text,
            v_authorization.tariff_snapshot_id,
            NULL::uuid;
        RETURN;
    ELSIF p_completion_basis <> 'ZERO_PAYABLE_STATUTORY_FINALITY' THEN
        RAISE EXCEPTION 'unsupported ExitAuthorization completion basis: %', p_completion_basis
            USING ERRCODE = 'P0001';
    END IF;

    IF p_payment_attempt_id IS NOT NULL OR p_tariff_snapshot_id IS NULL OR
       p_statutory_discount_decision_command_id IS NULL OR
       p_statutory_discount_payable_basis_application_command_id IS NULL OR
       p_statutory_discount_validation_id IS NULL OR
       p_applied_policy_reference_id IS NULL OR
       p_fiscal_issuance_reference_id IS NULL THEN
        RAISE EXCEPTION 'ZERO_PAYABLE_STATUTORY_FINALITY issuance has incomplete or incompatible ancestry'
            USING ERRCODE = 'P0001';
    END IF;

    PERFORM 1
    FROM core.parking_sessions AS parking
    WHERE parking.parking_session_id = p_parking_session_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'parking session % was not found', p_parking_session_id
            USING ERRCODE = 'P0002';
    END IF;

    SELECT command.*
    INTO v_application_command
    FROM discounts.statutory_discount_payable_basis_application_commands AS command
    WHERE command.statutory_discount_payable_basis_application_command_id =
        p_statutory_discount_payable_basis_application_command_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'zero-payable statutory completion authority was not found'
            USING ERRCODE = 'P0002';
    END IF;

    SELECT application.*
    INTO v_statutory_application
    FROM discounts.statutory_discount_payable_basis_applications AS application
    WHERE application.statutory_discount_payable_basis_application_id =
        v_application_command.statutory_discount_payable_basis_application_id;

    SELECT EXISTS (
        SELECT 1
        FROM discounts.statutory_discount_decision_commands AS decision
        JOIN discounts.statutory_discount_decision_policy_authorities AS authority
          ON authority.statutory_discount_decision_command_id = decision.statutory_discount_decision_command_id
        JOIN discounts.statutory_discount_policy_versions AS policy_version
          ON policy_version.statutory_discount_policy_version_id = authority.statutory_discount_policy_version_id
        JOIN discounts.discount_policy_references AS applied_policy
          ON applied_policy.discount_policy_reference_id = p_applied_policy_reference_id
        JOIN discounts.statutory_discount_validations AS validation
          ON validation.statutory_discount_validation_id = p_statutory_discount_validation_id
        JOIN core.parking_sessions AS parking
          ON parking.parking_session_id = p_parking_session_id
        JOIN core.tariff_snapshots AS tariff
          ON tariff.tariff_snapshot_id = p_tariff_snapshot_id
        WHERE decision.statutory_discount_decision_command_id = p_statutory_discount_decision_command_id
          AND decision.decision_result_status = 'APPROVED'
          AND decision.parking_session_id = p_parking_session_id
          AND decision.statutory_discount_validation_id = p_statutory_discount_validation_id
          AND decision.applied_policy_reference_id = p_applied_policy_reference_id
          AND decision.net_payable_amount_minor_units = 0
          AND decision.decided_at IS NOT NULL
          AND v_application_command.statutory_discount_decision_command_id = decision.statutory_discount_decision_command_id
          AND v_application_command.parking_session_id = p_parking_session_id
          AND v_application_command.site_id = parking.site_id
          AND v_application_command.command_status = 'APPLIED'
          AND v_application_command.statutory_discount_validation_id = p_statutory_discount_validation_id
          AND v_application_command.applied_tariff_snapshot_id = p_tariff_snapshot_id
          AND v_application_command.applied_policy_reference_id = p_applied_policy_reference_id
          AND v_application_command.approved_final_payable_amount_minor_units = 0
          AND v_application_command.applied_at IS NOT NULL
          AND v_statutory_application.statutory_discount_validation_id = p_statutory_discount_validation_id
          AND v_statutory_application.parking_session_id = p_parking_session_id
          AND v_statutory_application.applied_tariff_snapshot_id = p_tariff_snapshot_id
          AND v_statutory_application.application_status = 'APPLIED'
          AND v_statutory_application.final_payable_amount_minor_units = 0
          AND v_statutory_application.applied_at IS NOT NULL
          AND validation.parking_session_id = p_parking_session_id
          AND validation.tariff_snapshot_id = p_tariff_snapshot_id
          AND validation.applied_policy_reference_id = p_applied_policy_reference_id
          AND validation.validation_status = 'APPROVED'
          AND ROUND(validation.net_amount_after_discount * 100)::bigint = 0
          AND tariff.parking_session_id = p_parking_session_id
          AND tariff.statutory_discount_validation_id = p_statutory_discount_validation_id
          AND tariff.snapshot_status = 'ACTIVE'
          AND ROUND(tariff.net_amount * 100)::bigint = 0
          AND (applied_policy.site_id IS NULL OR applied_policy.site_id = parking.site_id)
          AND (applied_policy.site_group_id IS NULL OR applied_policy.site_group_id = parking.site_group_id)
          AND (policy_version.site_id IS NULL OR policy_version.site_id = parking.site_id)
          AND (policy_version.site_group_id IS NULL OR policy_version.site_group_id = parking.site_group_id)
          AND policy_version.full_fee_exempt
          AND policy_version.benefit_type = 'FULL_FEE_EXEMPTION'
          AND policy_version.policy_effect_support_status = 'SUPPORTED_BY_CURRENT_CALCULATION'
    ) INTO v_statutory_ancestry_valid;

    IF NOT v_statutory_ancestry_valid THEN
        RAISE EXCEPTION 'ZERO_PAYABLE_STATUTORY_FINALITY ancestry does not reconcile'
            USING ERRCODE = 'P0001';
    END IF;

    SELECT EXISTS (
        SELECT 1
        FROM core.fiscal_issuance_references AS fiscal
        WHERE fiscal.fiscal_issuance_reference_id = p_fiscal_issuance_reference_id
          AND fiscal.completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
          AND fiscal.completion_authority_reference_id = p_statutory_discount_payable_basis_application_command_id
          AND fiscal.statutory_discount_decision_command_id = p_statutory_discount_decision_command_id
          AND fiscal.statutory_discount_payable_basis_application_command_id = p_statutory_discount_payable_basis_application_command_id
          AND fiscal.statutory_discount_validation_id = p_statutory_discount_validation_id
          AND fiscal.applied_policy_reference_id = p_applied_policy_reference_id
          AND fiscal.parking_session_id = p_parking_session_id
          AND fiscal.tariff_snapshot_id = p_tariff_snapshot_id
          AND fiscal.site_id = v_application_command.site_id
          AND fiscal.payment_attempt_id IS NULL
          AND fiscal.payment_confirmation_id IS NULL
          AND fiscal.is_active AND NOT fiscal.is_superseded
          AND fiscal.fiscal_issuance_state IN ('FISCAL_ISSUANCE_RECORDED', 'FISCAL_ISSUANCE_REPLAYED')
          AND fiscal.fiscal_issuance_evidence_status = 'FISCAL_DOCUMENT_NUMBER_ASSIGNED'
          AND fiscal.fiscal_number_assignment_state = 'ASSIGNED'
          AND fiscal.pos_server_fiscal_document_id IS NOT NULL
          AND fiscal.fiscal_document_number IS NOT NULL
          AND fiscal.fiscal_identity_id IS NOT NULL
          AND fiscal.fiscal_sequence_policy_id IS NOT NULL
          AND fiscal.fiscal_sequence_value > 0
          AND fiscal.fiscal_number_assigned_at IS NOT NULL
          AND fiscal.fiscal_number_assigned_by_ref IS NOT NULL
          AND fiscal.fiscal_document_status_code_id IS NOT NULL
          AND fiscal.electronic_journal_event_reference IS NOT NULL
          AND btrim(fiscal.electronic_journal_event_reference) <> ''
    ) INTO v_fiscal_ancestry_valid;

    IF NOT v_fiscal_ancestry_valid THEN
        RAISE EXCEPTION 'ZERO_PAYABLE_FISCAL_PREREQUISITE_SATISFIED was not established'
            USING ERRCODE = 'P0001';
    END IF;

    SELECT EXISTS (
        SELECT 1 FROM core.payment_attempts AS attempt
        WHERE attempt.tariff_snapshot_id = p_tariff_snapshot_id
    ) INTO v_payment_conflict;
    IF v_payment_conflict THEN
        RAISE EXCEPTION 'zero-payable applied tariff has conflicting payment ancestry'
            USING ERRCODE = 'P0001';
    END IF;

    SELECT ea.*
    INTO v_authorization
    FROM core.exit_authorizations AS ea
    WHERE ea.statutory_discount_payable_basis_application_command_id =
        p_statutory_discount_payable_basis_application_command_id
    FOR UPDATE;

    IF FOUND THEN
        IF v_authorization.completion_basis <> 'ZERO_PAYABLE_STATUTORY_FINALITY' OR
           v_authorization.parking_session_id <> p_parking_session_id OR
           v_authorization.tariff_snapshot_id <> p_tariff_snapshot_id OR
           v_authorization.statutory_discount_decision_command_id <> p_statutory_discount_decision_command_id OR
           v_authorization.statutory_discount_validation_id <> p_statutory_discount_validation_id OR
           v_authorization.applied_policy_reference_id <> p_applied_policy_reference_id OR
           v_authorization.payment_attempt_id IS NOT NULL OR
           v_authorization.payment_confirmation_id IS NOT NULL THEN
            RAISE EXCEPTION 'existing ExitAuthorization has incompatible statutory ancestry'
                USING ERRCODE = 'P0001';
        END IF;

        RETURN QUERY SELECT
            v_authorization.exit_authorization_id,
            v_authorization.parking_session_id,
            v_authorization.payment_attempt_id,
            v_authorization.exit_authorization_id::text,
            v_authorization.authorization_status::text,
            v_authorization.issued_at,
            v_authorization.expires_at,
            v_authorization.completion_basis::text,
            v_authorization.tariff_snapshot_id,
            p_fiscal_issuance_reference_id;
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1 FROM core.exit_authorizations AS ea
        WHERE ea.parking_session_id = p_parking_session_id
    ) THEN
        RAISE EXCEPTION 'parking session already has an incompatible ExitAuthorization'
            USING ERRCODE = 'P0001';
    END IF;

    SELECT si.service_identity_id
    INTO v_requested_by_service_identity_id
    FROM identity.service_identities AS si
    WHERE si.service_identity_id = p_requested_by
    LIMIT 1;

    v_requested_by_service_identity_id := COALESCE(
        v_requested_by_service_identity_id,
        v_statutory_application.applied_by_service_identity_id,
        v_statutory_application.created_by_service_identity_id);
    IF v_requested_by_service_identity_id IS NULL THEN
        RAISE EXCEPTION 'requested_by service identity could not be resolved'
            USING ERRCODE = 'P0002';
    END IF;

    v_authorization_token := 'EXIT-' || replace(gen_random_uuid()::text, '-', '');
    INSERT INTO core.exit_authorizations (
        exit_authorization_id, parking_session_id, payment_attempt_id,
        payment_confirmation_id, tariff_snapshot_id, completion_basis,
        statutory_discount_decision_command_id,
        statutory_discount_payable_basis_application_command_id,
        statutory_discount_validation_id, applied_policy_reference_id,
        authorization_token_hash, authorization_status, issued_at, expires_at,
        correlation_id, created_at, created_by_service_identity_id,
        updated_at, updated_by_service_identity_id)
    VALUES (
        gen_random_uuid(), p_parking_session_id, NULL, NULL, p_tariff_snapshot_id,
        'ZERO_PAYABLE_STATUTORY_FINALITY', p_statutory_discount_decision_command_id,
        p_statutory_discount_payable_basis_application_command_id,
        p_statutory_discount_validation_id, p_applied_policy_reference_id,
        encode(digest(v_authorization_token, 'sha256'), 'hex'), 'ISSUED', p_now,
        p_now + interval '15 minutes', p_correlation_id, p_now,
        v_requested_by_service_identity_id, p_now, v_requested_by_service_identity_id)
    RETURNING * INTO v_authorization;

    RETURN QUERY SELECT
        v_authorization.exit_authorization_id,
        v_authorization.parking_session_id,
        v_authorization.payment_attempt_id,
        v_authorization_token,
        v_authorization.authorization_status::text,
        v_authorization.issued_at,
        v_authorization.expires_at,
        v_authorization.completion_basis::text,
        v_authorization.tariff_snapshot_id,
        p_fiscal_issuance_reference_id;
END;
$function$;

COMMENT ON FUNCTION core.issue_exit_authorization(
    uuid, uuid, uuid, uuid, timestamptz, varchar, uuid, uuid, uuid, uuid, uuid, uuid) IS
    'Canonical replay-safe issuance for PAYMENT_FINALITY or ZERO_PAYABLE_STATUTORY_FINALITY; statutory issuance requires authoritative fiscal document and EJ evidence and creates no payment artifacts.';
