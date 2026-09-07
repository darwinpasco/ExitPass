DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(required_name, ', ' ORDER BY required_name)
    INTO missing
    FROM unnest(ARRAY[
        'identity.authentication_attempts',
        'identity.credential_challenges',
        'identity.external_identity_bindings',
        'identity.external_identity_providers',
        'identity.human_sessions',
        'identity.local_credentials',
        'identity.user_mfa_authenticators',
        'identity.user_role_scope_grants',
        'identity.users'
    ]) AS required(required_name)
    WHERE to_regclass(required_name) IS NULL;

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing canonical human-authentication tables: %', missing;
    END IF;

    WITH required(table_name, column_name, data_type, not_null) AS (
        VALUES
            ('users', 'username_normalized', 'character varying(128)', false),
            ('users', 'lockout_expires_at', 'timestamp with time zone', false),
            ('users', 'lockout_reason_code', 'character varying(64)', false),
            ('users', 'credential_version', 'bigint', true),
            ('users', 'authorization_epoch', 'bigint', true),
            ('local_credentials', 'password_verifier', 'bytea', true),
            ('local_credentials', 'verifier_salt', 'bytea', true),
            ('local_credentials', 'credential_version', 'bigint', true),
            ('external_identity_providers', 'issuer_identifier_hash', 'character(64)', true),
            ('external_identity_bindings', 'external_subject_hash', 'character(64)', true),
            ('user_mfa_authenticators', 'protected_secret_envelope', 'bytea', true),
            ('user_mfa_authenticators', 'last_successfully_used_time_step', 'bigint', false),
            ('human_sessions', 'session_secret_hash', 'character(64)', true),
            ('human_sessions', 'session_audience', 'identity.human_session_audience_enum', true),
            ('human_sessions', 'device_service_identity_id', 'uuid', false),
            ('human_sessions', 'credential_version_snapshot', 'bigint', true),
            ('human_sessions', 'authorization_epoch_snapshot', 'bigint', true),
            ('authentication_attempts', 'attempt_type', 'identity.authentication_attempt_type_enum', true),
            ('authentication_attempts', 'attempt_result', 'identity.authentication_attempt_result_enum', true),
            ('authentication_attempts', 'request_fingerprint_hash', 'character(64)', false),
            ('credential_challenges', 'challenge_purpose', 'identity.credential_challenge_purpose_enum', true),
            ('credential_challenges', 'challenge_status', 'identity.credential_challenge_status_enum', true),
            ('user_role_scope_grants', 'scope_type', 'identity.authorization_scope_type_enum', true),
            ('user_role_scope_grants', 'site_id', 'uuid', false),
            ('user_role_scope_grants', 'site_group_id', 'uuid', false),
            ('user_role_scope_grants', 'grant_status', 'identity.user_role_scope_grant_status_enum', true)
    ), actual AS (
        SELECT c.relname AS table_name,
               a.attname AS column_name,
               format_type(a.atttypid, a.atttypmod) AS data_type,
               a.attnotnull AS not_null
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'identity' AND a.attnum > 0 AND NOT a.attisdropped
    )
    SELECT string_agg(format('%s.%s', r.table_name, r.column_name), ', ' ORDER BY r.table_name, r.column_name)
    INTO missing
    FROM required r
    LEFT JOIN actual a USING (table_name, column_name, data_type, not_null)
    WHERE a.column_name IS NULL;

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing or incompatible canonical human-authentication columns: %', missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
        WHERE n.nspname = 'identity'
          AND c.relname = 'users'
          AND a.attname = 'username_normalized'
          AND a.attgenerated = 's'
          AND pg_get_expr(d.adbin, d.adrelid) = 'lower(btrim((username)::text))'
    ) THEN
        RAISE EXCEPTION 'identity.users.username_normalized is not the canonical stored generated login key';
    END IF;

    SELECT string_agg(legacy_name, ', ' ORDER BY legacy_name)
    INTO missing
    FROM unnest(ARRAY[
        'uq_users__username',
        'uq_users__email_normalized',
        'ck_users__row_version_positive',
        'fk_users__created_by_user_id',
        'fk_users__created_by_service_identity_id',
        'fk_users__updated_by_user_id',
        'fk_users__updated_by_service_identity_id'
    ]) AS legacy(legacy_name)
    WHERE EXISTS (
        SELECT 1
        FROM pg_constraint c
        JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE n.nspname = 'identity' AND c.conname = legacy_name
    );

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Legacy identity.users constraints remain after canonical human-authentication recovery: %', missing;
    END IF;

    WITH required(type_name, labels) AS (
        VALUES
            ('authentication_provider_enum', ARRAY['LOCAL','OIDC']),
            ('external_identity_provider_status_enum', ARRAY['DISABLED','ACTIVE','SUSPENDED','RETIRED']),
            ('external_identity_binding_status_enum', ARRAY['PENDING','ACTIVE','SUSPENDED','REVOKED']),
            ('local_credential_status_enum', ARRAY['PENDING_ACTIVATION','ACTIVE','CHANGE_REQUIRED','LOCKED','REVOKED','EXPIRED']),
            ('mfa_authenticator_type_enum', ARRAY['TOTP']),
            ('mfa_authenticator_status_enum', ARRAY['PENDING_ENROLLMENT','ACTIVE','SUSPENDED','RESET_REQUIRED','REVOKED']),
            ('human_session_audience_enum', ARRAY['MANAGEMENT_PLATFORM','OPERATOR_CONSOLE','APT']),
            ('human_session_status_enum', ARRAY['ACTIVE','REVOKED','EXPIRED']),
            ('authentication_attempt_type_enum', ARRAY['PASSWORD','TOTP','ACTIVATION_CHALLENGE','PASSWORD_RESET_CHALLENGE','CREDENTIAL_RECOVERY_CHALLENGE']),
            ('authentication_attempt_result_enum', ARRAY['SUCCESS','INVALID','THROTTLED','LOCKED','EXPIRED','REVOKED','UNAVAILABLE','UNKNOWN']),
            ('credential_challenge_purpose_enum', ARRAY['ACCOUNT_ACTIVATION','PASSWORD_RESET','CREDENTIAL_RECOVERY']),
            ('credential_challenge_status_enum', ARRAY['ISSUED','CONSUMED','REVOKED','EXPIRED']),
            ('authorization_scope_type_enum', ARRAY['SITE','SITE_GROUP','GLOBAL']),
            ('user_role_scope_grant_status_enum', ARRAY['PENDING','ACTIVE','SUSPENDED','REVOKED','EXPIRED'])
    ), actual AS (
        SELECT t.typname AS type_name,
               array_agg(e.enumlabel::text ORDER BY e.enumsortorder) AS labels
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        JOIN pg_enum e ON e.enumtypid = t.oid
        WHERE n.nspname = 'identity'
        GROUP BY t.typname
    )
    SELECT string_agg(r.type_name, ', ' ORDER BY r.type_name)
    INTO missing
    FROM required r
    LEFT JOIN actual a USING (type_name, labels)
    WHERE a.type_name IS NULL;

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing or incompatible canonical human-authentication enums: %', missing;
    END IF;

    SELECT string_agg(required_name, ', ' ORDER BY required_name)
    INTO missing
    FROM unnest(ARRAY[
        'ux_users__username_normalized',
        'ux_local_credentials__current_user',
        'ux_external_identity_bindings__current_subject',
        'ux_external_identity_bindings__current_user_provider',
        'ux_user_mfa_authenticators__current_type',
        'ix_human_sessions__audience_status_expiry',
        'ix_human_sessions__device_status',
        'ix_human_sessions__user_status_expiry',
        'ix_authentication_attempts__login_time',
        'ix_authentication_attempts__source_time',
        'ix_authentication_attempts__user_time',
        'ux_credential_challenges__issued_user_purpose',
        'ux_user_role_scope_grants__current_exact'
    ]) AS required(required_name)
    WHERE to_regclass('identity.' || required_name) IS NULL;

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing canonical human-authentication indexes: %', missing;
    END IF;

    SELECT string_agg(required_name, ', ' ORDER BY required_name)
    INTO missing
    FROM unnest(ARRAY[
        'fk_local_credentials__user',
        'fk_external_identity_bindings__provider',
        'fk_user_mfa_authenticators__user',
        'fk_human_sessions__user',
        'fk_human_sessions__local_credential',
        'fk_human_sessions__external_binding',
        'fk_human_sessions__device_service',
        'fk_human_sessions__mfa_authenticator',
        'fk_authentication_attempts__user',
        'fk_authentication_attempts__recorded_by_service',
        'fk_credential_challenges__user',
        'fk_user_role_scope_grants__user_role',
        'fk_user_role_scope_grants__site',
        'fk_user_role_scope_grants__site_group'
    ]) AS required(required_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint c
        JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE n.nspname = 'identity'
          AND c.conname = required_name
          AND c.contype = 'f'
    );

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing canonical human-authentication foreign keys: %', missing;
    END IF;

    SELECT string_agg(required_name, ', ' ORDER BY required_name)
    INTO missing
    FROM unnest(ARRAY[
        'ck_users__username_not_blank',
        'ck_users__username_normalized_not_blank',
        'ck_users__lockout_window',
        'ck_users__credential_version',
        'ck_users__authorization_epoch',
        'ck_local_credentials__verifier',
        'ck_local_credentials__work_parameters',
        'ck_external_identity_bindings__subject_hash',
        'ck_user_mfa_authenticators__protected_envelope',
        'ck_human_sessions__provider_binding',
        'ck_human_sessions__expiry',
        'ck_human_sessions__secret_hash',
        'ck_authentication_attempts__principal',
        'ck_credential_challenges__lifecycle',
        'ck_user_role_scope_grants__scope_shape'
    ]) AS required(required_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint c
        JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE n.nspname = 'identity'
          AND c.conname = required_name
          AND c.contype = 'c'
    );

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing canonical human-authentication checks: %', missing;
    END IF;
END $$;

SELECT 'CANONICAL_HUMAN_AUTH_SCHEMA_VALIDATION_PASSED' AS validation_result;
