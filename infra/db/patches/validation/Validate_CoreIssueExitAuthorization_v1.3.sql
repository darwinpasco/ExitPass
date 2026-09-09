DO $validation$
BEGIN
    IF to_regprocedure(
        'core.issue_exit_authorization(uuid,uuid,uuid,uuid,timestamp with time zone,character varying,uuid,uuid,uuid,uuid,uuid,uuid)')
        IS NULL THEN
        RAISE EXCEPTION 'canonical v1.3 ExitAuthorization issuance routine is missing';
    END IF;

    IF to_regprocedure(
        'core.issue_exit_authorization(uuid,uuid,uuid,uuid,timestamp with time zone)')
        IS NOT NULL THEN
        RAISE EXCEPTION 'paid-only v1.2 ExitAuthorization issuance overload remains installed';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM core.exit_authorizations AS exit_auth
        WHERE NOT (
            (exit_auth.completion_basis = 'PAYMENT_FINALITY'
             AND exit_auth.payment_attempt_id IS NOT NULL
             AND exit_auth.payment_confirmation_id IS NOT NULL)
            OR
            (exit_auth.completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
             AND exit_auth.payment_attempt_id IS NULL
             AND exit_auth.payment_confirmation_id IS NULL
             AND exit_auth.statutory_discount_decision_command_id IS NOT NULL
             AND exit_auth.statutory_discount_payable_basis_application_command_id IS NOT NULL
             AND exit_auth.statutory_discount_validation_id IS NOT NULL
             AND exit_auth.applied_policy_reference_id IS NOT NULL)
        )
    ) THEN
        RAISE EXCEPTION 'ExitAuthorization completion ancestry contains invalid rows';
    END IF;
END;
$validation$;

SELECT 'CORE_ISSUE_EXIT_AUTHORIZATION_V1_3_VALID' AS validation_result;
