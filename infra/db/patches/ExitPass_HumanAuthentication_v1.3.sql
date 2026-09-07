-- ExitPass v1.3 canonical human-authentication persistence.
-- This patch closes the gap between the tracked v1.2 baseline and the current
-- Central PMS human-authentication, identity-administration, and scope runtime.

DO $$ BEGIN
    CREATE TYPE identity.authentication_provider_enum AS ENUM ('LOCAL', 'OIDC');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.external_identity_provider_status_enum AS ENUM ('DISABLED', 'ACTIVE', 'SUSPENDED', 'RETIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.external_identity_binding_status_enum AS ENUM ('PENDING', 'ACTIVE', 'SUSPENDED', 'REVOKED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.local_credential_status_enum AS ENUM ('PENDING_ACTIVATION', 'ACTIVE', 'CHANGE_REQUIRED', 'LOCKED', 'REVOKED', 'EXPIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.mfa_authenticator_type_enum AS ENUM ('TOTP');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.mfa_authenticator_status_enum AS ENUM ('PENDING_ENROLLMENT', 'ACTIVE', 'SUSPENDED', 'RESET_REQUIRED', 'REVOKED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.human_session_audience_enum AS ENUM ('MANAGEMENT_PLATFORM', 'OPERATOR_CONSOLE', 'APT');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.human_session_status_enum AS ENUM ('ACTIVE', 'REVOKED', 'EXPIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.authentication_attempt_type_enum AS ENUM ('PASSWORD', 'TOTP', 'ACTIVATION_CHALLENGE', 'PASSWORD_RESET_CHALLENGE', 'CREDENTIAL_RECOVERY_CHALLENGE');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.authentication_attempt_result_enum AS ENUM ('SUCCESS', 'INVALID', 'THROTTLED', 'LOCKED', 'EXPIRED', 'REVOKED', 'UNAVAILABLE', 'UNKNOWN');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.credential_challenge_purpose_enum AS ENUM ('ACCOUNT_ACTIVATION', 'PASSWORD_RESET', 'CREDENTIAL_RECOVERY');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.credential_challenge_status_enum AS ENUM ('ISSUED', 'CONSUMED', 'REVOKED', 'EXPIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.authorization_scope_type_enum AS ENUM ('SITE', 'SITE_GROUP', 'GLOBAL');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    CREATE TYPE identity.user_role_scope_grant_status_enum AS ENUM ('PENDING', 'ACTIVE', 'SUSPENDED', 'REVOKED', 'EXPIRED');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS username_normalized varchar(128)
        GENERATED ALWAYS AS (lower(btrim(username))) STORED,
    ADD COLUMN IF NOT EXISTS lockout_expires_at timestamptz,
    ADD COLUMN IF NOT EXISTS lockout_reason_code varchar(64),
    ADD COLUMN IF NOT EXISTS credential_version bigint NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS authorization_epoch bigint NOT NULL DEFAULT 1;

-- The generated normalized username is the canonical login key. The former
-- case-sensitive username and nullable-email uniqueness rules are superseded.
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS uq_users__username;
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS uq_users__email_normalized;
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS ck_users__row_version_positive;
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS fk_users__created_by_user_id;
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS fk_users__created_by_service_identity_id;
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS fk_users__updated_by_user_id;
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS fk_users__updated_by_service_identity_id;

DO $$ BEGIN
    ALTER TABLE identity.users ADD CONSTRAINT ck_users__username_not_blank
        CHECK (btrim(username) <> '');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE identity.users ADD CONSTRAINT ck_users__username_normalized_not_blank
        CHECK (btrim(username_normalized) <> '');
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE identity.users ADD CONSTRAINT ck_users__lockout_window
        CHECK (lockout_expires_at IS NULL OR locked_at IS NOT NULL);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE identity.users ADD CONSTRAINT ck_users__credential_version
        CHECK (credential_version > 0);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE identity.users ADD CONSTRAINT ck_users__authorization_epoch
        CHECK (authorization_epoch > 0);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE identity.users ADD CONSTRAINT ck_users__row_version
        CHECK (row_version > 0);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_users__username_normalized
    ON identity.users (username_normalized);
CREATE INDEX IF NOT EXISTS ix_users__user_status ON identity.users (user_status);
CREATE INDEX IF NOT EXISTS ix_users__last_login_at ON identity.users (last_login_at);
CREATE INDEX IF NOT EXISTS ix_users__locked_at ON identity.users (locked_at);

CREATE TABLE IF NOT EXISTS identity.external_identity_providers (
    external_identity_provider_id uuid NOT NULL DEFAULT gen_random_uuid(),
    provider_code varchar(64) NOT NULL,
    provider_name varchar(128) NOT NULL,
    issuer_identifier_hash char(64) NOT NULL,
    provider_status identity.external_identity_provider_status_enum NOT NULL DEFAULT 'DISABLED',
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_external_identity_providers PRIMARY KEY (external_identity_provider_id),
    CONSTRAINT uq_external_identity_providers__code UNIQUE (provider_code),
    CONSTRAINT uq_external_identity_providers__issuer_hash UNIQUE (issuer_identifier_hash),
    CONSTRAINT ck_external_identity_providers__code CHECK (btrim(provider_code) <> ''),
    CONSTRAINT ck_external_identity_providers__name CHECK (btrim(provider_name) <> ''),
    CONSTRAINT ck_external_identity_providers__issuer_hash CHECK (issuer_identifier_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_external_identity_providers__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT ck_external_identity_providers__row_version CHECK (row_version > 0)
);

CREATE INDEX IF NOT EXISTS ix_external_identity_providers__status
    ON identity.external_identity_providers (provider_status, effective_from, effective_to);

CREATE TABLE IF NOT EXISTS identity.local_credentials (
    local_credential_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    credential_status identity.local_credential_status_enum NOT NULL DEFAULT 'PENDING_ACTIVATION',
    password_verifier bytea NOT NULL,
    verifier_salt bytea NOT NULL,
    verifier_algorithm_code varchar(32) NOT NULL,
    verifier_algorithm_version smallint NOT NULL,
    verifier_work_factor integer NOT NULL,
    verifier_memory_kib integer,
    verifier_parallelism smallint,
    credential_version bigint NOT NULL DEFAULT 1,
    activated_at timestamptz,
    last_changed_at timestamptz,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    status_reason_code varchar(64),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_local_credentials PRIMARY KEY (local_credential_id),
    CONSTRAINT fk_local_credentials__user FOREIGN KEY (user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_local_credentials__revoked_by_user FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_local_credentials__revoked_by_service FOREIGN KEY (revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_local_credentials__activation CHECK (credential_status = 'PENDING_ACTIVATION' OR activated_at IS NOT NULL),
    CONSTRAINT ck_local_credentials__algorithm CHECK (btrim(verifier_algorithm_code) <> '' AND verifier_algorithm_version > 0),
    CONSTRAINT ck_local_credentials__changed_at CHECK (last_changed_at IS NULL OR last_changed_at >= created_at),
    CONSTRAINT ck_local_credentials__credential_version CHECK (credential_version > 0),
    CONSTRAINT ck_local_credentials__revocation CHECK ((credential_status = 'REVOKED') = (revoked_at IS NOT NULL)),
    CONSTRAINT ck_local_credentials__revocation_actor CHECK (
        (revoked_at IS NULL AND revoked_by_user_id IS NULL AND revoked_by_service_identity_id IS NULL)
        OR (revoked_at IS NOT NULL AND num_nonnulls(revoked_by_user_id, revoked_by_service_identity_id) = 1)),
    CONSTRAINT ck_local_credentials__row_version CHECK (row_version > 0),
    CONSTRAINT ck_local_credentials__verifier CHECK (octet_length(password_verifier) >= 32 AND octet_length(verifier_salt) >= 16),
    CONSTRAINT ck_local_credentials__work_parameters CHECK (
        verifier_work_factor > 0
        AND (verifier_memory_kib IS NULL OR verifier_memory_kib > 0)
        AND (verifier_parallelism IS NULL OR verifier_parallelism > 0))
);

CREATE INDEX IF NOT EXISTS ix_local_credentials__user_status
    ON identity.local_credentials (user_id, credential_status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_local_credentials__current_user
    ON identity.local_credentials (user_id)
    WHERE credential_status IN ('PENDING_ACTIVATION', 'ACTIVE', 'CHANGE_REQUIRED', 'LOCKED');

CREATE TABLE IF NOT EXISTS identity.external_identity_bindings (
    external_identity_binding_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    external_identity_provider_id uuid NOT NULL,
    external_subject_hash char(64) NOT NULL,
    binding_status identity.external_identity_binding_status_enum NOT NULL DEFAULT 'PENDING',
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    revocation_reason_code varchar(64),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_external_identity_bindings PRIMARY KEY (external_identity_binding_id),
    CONSTRAINT fk_external_identity_bindings__user FOREIGN KEY (user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_external_identity_bindings__provider FOREIGN KEY (external_identity_provider_id) REFERENCES identity.external_identity_providers(external_identity_provider_id),
    CONSTRAINT fk_external_identity_bindings__revoked_by_user FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_external_identity_bindings__revoked_by_service FOREIGN KEY (revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_external_identity_bindings__subject_hash CHECK (external_subject_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_external_identity_bindings__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT ck_external_identity_bindings__revocation CHECK ((binding_status = 'REVOKED') = (revoked_at IS NOT NULL)),
    CONSTRAINT ck_external_identity_bindings__revocation_actor CHECK (
        (revoked_at IS NULL AND revoked_by_user_id IS NULL AND revoked_by_service_identity_id IS NULL)
        OR (revoked_at IS NOT NULL AND num_nonnulls(revoked_by_user_id, revoked_by_service_identity_id) = 1)),
    CONSTRAINT ck_external_identity_bindings__row_version CHECK (row_version > 0)
);

CREATE INDEX IF NOT EXISTS ix_external_identity_bindings__user_status
    ON identity.external_identity_bindings (user_id, binding_status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_external_identity_bindings__current_subject
    ON identity.external_identity_bindings (external_identity_provider_id, external_subject_hash)
    WHERE binding_status IN ('PENDING', 'ACTIVE', 'SUSPENDED');
CREATE UNIQUE INDEX IF NOT EXISTS ux_external_identity_bindings__current_user_provider
    ON identity.external_identity_bindings (user_id, external_identity_provider_id)
    WHERE binding_status IN ('PENDING', 'ACTIVE', 'SUSPENDED');

CREATE TABLE IF NOT EXISTS identity.user_mfa_authenticators (
    user_mfa_authenticator_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    authenticator_type identity.mfa_authenticator_type_enum NOT NULL DEFAULT 'TOTP',
    authenticator_status identity.mfa_authenticator_status_enum NOT NULL DEFAULT 'PENDING_ENROLLMENT',
    protected_secret_envelope bytea NOT NULL,
    protection_key_reference varchar(256) NOT NULL,
    protection_key_version varchar(64) NOT NULL,
    envelope_format_version smallint NOT NULL,
    enrollment_started_at timestamptz NOT NULL DEFAULT now(),
    activated_at timestamptz,
    last_successfully_used_at timestamptz,
    last_successfully_used_time_step bigint,
    reset_at timestamptz,
    revoked_at timestamptz,
    reset_or_revoked_by_user_id uuid,
    reset_or_revoked_by_service_identity_id uuid,
    status_reason_code varchar(64),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_user_mfa_authenticators PRIMARY KEY (user_mfa_authenticator_id),
    CONSTRAINT fk_user_mfa_authenticators__user FOREIGN KEY (user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_user_mfa_authenticators__reset_user FOREIGN KEY (reset_or_revoked_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_user_mfa_authenticators__reset_service FOREIGN KEY (reset_or_revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_user_mfa_authenticators__activation CHECK (
        (authenticator_status = 'PENDING_ENROLLMENT' AND activated_at IS NULL)
        OR (authenticator_status <> 'PENDING_ENROLLMENT' AND activated_at IS NOT NULL)),
    CONSTRAINT ck_user_mfa_authenticators__key_metadata CHECK (
        btrim(protection_key_reference) <> '' AND btrim(protection_key_version) <> '' AND envelope_format_version > 0),
    CONSTRAINT ck_user_mfa_authenticators__last_use CHECK (
        (last_successfully_used_at IS NULL) = (last_successfully_used_time_step IS NULL)
        AND (last_successfully_used_time_step IS NULL OR last_successfully_used_time_step >= 0)),
    CONSTRAINT ck_user_mfa_authenticators__protected_envelope CHECK (octet_length(protected_secret_envelope) >= 32),
    CONSTRAINT ck_user_mfa_authenticators__reset CHECK (
        (authenticator_status = 'RESET_REQUIRED' AND reset_at IS NOT NULL AND revoked_at IS NULL)
        OR (authenticator_status <> 'RESET_REQUIRED' AND reset_at IS NULL)),
    CONSTRAINT ck_user_mfa_authenticators__reset_actor CHECK (
        (reset_at IS NULL AND revoked_at IS NULL AND reset_or_revoked_by_user_id IS NULL AND reset_or_revoked_by_service_identity_id IS NULL)
        OR ((reset_at IS NOT NULL OR revoked_at IS NOT NULL)
            AND num_nonnulls(reset_or_revoked_by_user_id, reset_or_revoked_by_service_identity_id) = 1)),
    CONSTRAINT ck_user_mfa_authenticators__row_version CHECK (row_version > 0),
    CONSTRAINT ck_user_mfa_authenticators__termination CHECK ((authenticator_status = 'REVOKED') = (revoked_at IS NOT NULL)),
    CONSTRAINT ck_user_mfa_authenticators__termination_reason CHECK (
        authenticator_status NOT IN ('RESET_REQUIRED', 'REVOKED')
        OR (status_reason_code IS NOT NULL AND btrim(status_reason_code) <> ''))
);

CREATE INDEX IF NOT EXISTS ix_user_mfa_authenticators__user_status
    ON identity.user_mfa_authenticators (user_id, authenticator_status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_user_mfa_authenticators__current_type
    ON identity.user_mfa_authenticators (user_id, authenticator_type)
    WHERE authenticator_status IN ('PENDING_ENROLLMENT', 'ACTIVE', 'SUSPENDED', 'RESET_REQUIRED');

CREATE TABLE IF NOT EXISTS identity.human_sessions (
    human_session_id uuid NOT NULL DEFAULT gen_random_uuid(),
    session_reference uuid NOT NULL DEFAULT gen_random_uuid(),
    session_secret_hash char(64) NOT NULL,
    user_id uuid NOT NULL,
    authentication_provider identity.authentication_provider_enum NOT NULL,
    local_credential_id uuid,
    external_identity_binding_id uuid,
    session_audience identity.human_session_audience_enum NOT NULL,
    device_service_identity_id uuid,
    session_status identity.human_session_status_enum NOT NULL DEFAULT 'ACTIVE',
    assurance_context_code varchar(64) NOT NULL,
    mfa_requirement_satisfied boolean NOT NULL DEFAULT false,
    mfa_authenticator_id uuid,
    mfa_verified_at timestamptz,
    authenticated_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    idle_expires_at timestamptz NOT NULL,
    absolute_expires_at timestamptz NOT NULL,
    credential_version_snapshot bigint NOT NULL,
    authorization_epoch_snapshot bigint NOT NULL,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    revocation_reason_code varchar(64),
    correlation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_human_sessions PRIMARY KEY (human_session_id),
    CONSTRAINT uq_human_sessions__reference UNIQUE (session_reference),
    CONSTRAINT uq_human_sessions__secret_hash UNIQUE (session_secret_hash),
    CONSTRAINT fk_human_sessions__user FOREIGN KEY (user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_human_sessions__local_credential FOREIGN KEY (local_credential_id) REFERENCES identity.local_credentials(local_credential_id),
    CONSTRAINT fk_human_sessions__external_binding FOREIGN KEY (external_identity_binding_id) REFERENCES identity.external_identity_bindings(external_identity_binding_id),
    CONSTRAINT fk_human_sessions__device_service FOREIGN KEY (device_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT fk_human_sessions__mfa_authenticator FOREIGN KEY (mfa_authenticator_id) REFERENCES identity.user_mfa_authenticators(user_mfa_authenticator_id),
    CONSTRAINT fk_human_sessions__revoked_by_user FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_human_sessions__revoked_by_service FOREIGN KEY (revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_human_sessions__assurance CHECK (
        btrim(assurance_context_code) <> ''
        AND ((mfa_requirement_satisfied AND mfa_verified_at IS NOT NULL)
             OR (NOT mfa_requirement_satisfied AND mfa_verified_at IS NULL AND mfa_authenticator_id IS NULL))
        AND (mfa_authenticator_id IS NULL OR mfa_requirement_satisfied)),
    CONSTRAINT ck_human_sessions__expiry CHECK (
        last_seen_at >= authenticated_at
        AND idle_expires_at > last_seen_at
        AND absolute_expires_at > authenticated_at
        AND idle_expires_at <= absolute_expires_at),
    CONSTRAINT ck_human_sessions__provider_binding CHECK (
        (authentication_provider = 'LOCAL' AND local_credential_id IS NOT NULL AND external_identity_binding_id IS NULL)
        OR (authentication_provider = 'OIDC' AND local_credential_id IS NULL AND external_identity_binding_id IS NOT NULL)),
    CONSTRAINT ck_human_sessions__revocation CHECK ((session_status = 'REVOKED') = (revoked_at IS NOT NULL)),
    CONSTRAINT ck_human_sessions__revocation_actor CHECK (
        (revoked_at IS NULL AND revoked_by_user_id IS NULL AND revoked_by_service_identity_id IS NULL)
        OR (revoked_at IS NOT NULL AND num_nonnulls(revoked_by_user_id, revoked_by_service_identity_id) = 1)),
    CONSTRAINT ck_human_sessions__row_version CHECK (row_version > 0),
    CONSTRAINT ck_human_sessions__secret_hash CHECK (session_secret_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_human_sessions__version_snapshots CHECK (credential_version_snapshot > 0 AND authorization_epoch_snapshot > 0)
);

CREATE INDEX IF NOT EXISTS ix_human_sessions__audience_status_expiry
    ON identity.human_sessions (session_audience, session_status, idle_expires_at);
CREATE INDEX IF NOT EXISTS ix_human_sessions__correlation
    ON identity.human_sessions (correlation_id);
CREATE INDEX IF NOT EXISTS ix_human_sessions__device_status
    ON identity.human_sessions (device_service_identity_id, session_status)
    WHERE device_service_identity_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_human_sessions__user_status_expiry
    ON identity.human_sessions (user_id, session_status, absolute_expires_at);

CREATE TABLE IF NOT EXISTS identity.authentication_attempts (
    authentication_attempt_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid,
    login_identifier_hash char(64),
    attempt_type identity.authentication_attempt_type_enum NOT NULL,
    attempt_result identity.authentication_attempt_result_enum NOT NULL,
    session_audience identity.human_session_audience_enum NOT NULL,
    source_ip_hash char(64),
    user_agent_hash char(64),
    request_fingerprint_hash char(64),
    reason_code varchar(64),
    observed_at timestamptz NOT NULL,
    correlation_id uuid NOT NULL,
    recorded_by_service_identity_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_authentication_attempts PRIMARY KEY (authentication_attempt_id),
    CONSTRAINT fk_authentication_attempts__user FOREIGN KEY (user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_authentication_attempts__recorded_by_service FOREIGN KEY (recorded_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_authentication_attempts__principal CHECK (user_id IS NOT NULL OR login_identifier_hash IS NOT NULL),
    CONSTRAINT ck_authentication_attempts__login_hash CHECK (login_identifier_hash IS NULL OR login_identifier_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_authentication_attempts__source_hash CHECK (source_ip_hash IS NULL OR source_ip_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_authentication_attempts__agent_hash CHECK (user_agent_hash IS NULL OR user_agent_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_authentication_attempts__fingerprint_hash CHECK (request_fingerprint_hash IS NULL OR request_fingerprint_hash ~ '^[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS ix_authentication_attempts__correlation
    ON identity.authentication_attempts (correlation_id);
CREATE INDEX IF NOT EXISTS ix_authentication_attempts__login_time
    ON identity.authentication_attempts (login_identifier_hash, attempt_type, observed_at DESC)
    WHERE login_identifier_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_authentication_attempts__source_time
    ON identity.authentication_attempts (source_ip_hash, observed_at DESC)
    WHERE source_ip_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_authentication_attempts__user_time
    ON identity.authentication_attempts (user_id, attempt_type, observed_at DESC)
    WHERE user_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS identity.credential_challenges (
    credential_challenge_id uuid NOT NULL DEFAULT gen_random_uuid(),
    challenge_reference uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    challenge_purpose identity.credential_challenge_purpose_enum NOT NULL,
    challenge_status identity.credential_challenge_status_enum NOT NULL DEFAULT 'ISSUED',
    challenge_secret_hash char(64) NOT NULL,
    issued_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    revoked_at timestamptz,
    requested_by_user_id uuid,
    requested_by_service_identity_id uuid,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    reason_code varchar(64),
    correlation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    row_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_credential_challenges PRIMARY KEY (credential_challenge_id),
    CONSTRAINT uq_credential_challenges__reference UNIQUE (challenge_reference),
    CONSTRAINT uq_credential_challenges__secret_hash UNIQUE (challenge_secret_hash),
    CONSTRAINT fk_credential_challenges__user FOREIGN KEY (user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_credential_challenges__requested_user FOREIGN KEY (requested_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_credential_challenges__requested_service FOREIGN KEY (requested_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT fk_credential_challenges__revoked_user FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_credential_challenges__revoked_service FOREIGN KEY (revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_credential_challenges__expiry CHECK (expires_at > issued_at),
    CONSTRAINT ck_credential_challenges__consumed_at CHECK (consumed_at IS NULL OR (consumed_at >= issued_at AND consumed_at <= expires_at)),
    CONSTRAINT ck_credential_challenges__revoked_at CHECK (revoked_at IS NULL OR revoked_at >= issued_at),
    CONSTRAINT ck_credential_challenges__request_actor CHECK (num_nonnulls(requested_by_user_id, requested_by_service_identity_id) = 1),
    CONSTRAINT ck_credential_challenges__revocation_actor CHECK (
        (revoked_at IS NULL AND revoked_by_user_id IS NULL AND revoked_by_service_identity_id IS NULL)
        OR (revoked_at IS NOT NULL AND num_nonnulls(revoked_by_user_id, revoked_by_service_identity_id) = 1)),
    CONSTRAINT ck_credential_challenges__lifecycle CHECK (
        (challenge_status = 'ISSUED' AND consumed_at IS NULL AND revoked_at IS NULL)
        OR (challenge_status = 'CONSUMED' AND consumed_at IS NOT NULL AND revoked_at IS NULL)
        OR (challenge_status = 'REVOKED' AND consumed_at IS NULL AND revoked_at IS NOT NULL)
        OR (challenge_status = 'EXPIRED' AND consumed_at IS NULL AND revoked_at IS NULL)),
    CONSTRAINT ck_credential_challenges__secret_hash CHECK (challenge_secret_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_credential_challenges__row_version CHECK (row_version > 0)
);

CREATE INDEX IF NOT EXISTS ix_credential_challenges__correlation
    ON identity.credential_challenges (correlation_id);
CREATE INDEX IF NOT EXISTS ix_credential_challenges__user_status_expiry
    ON identity.credential_challenges (user_id, challenge_status, expires_at);
CREATE UNIQUE INDEX IF NOT EXISTS ux_credential_challenges__issued_user_purpose
    ON identity.credential_challenges (user_id, challenge_purpose)
    WHERE challenge_status = 'ISSUED';

CREATE TABLE IF NOT EXISTS identity.user_role_scope_grants (
    user_role_scope_grant_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_role_id uuid NOT NULL,
    scope_type identity.authorization_scope_type_enum NOT NULL,
    site_id uuid,
    site_group_id uuid,
    grant_status identity.user_role_scope_grant_status_enum NOT NULL DEFAULT 'PENDING',
    grant_reason_code varchar(64) NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    granted_at timestamptz NOT NULL DEFAULT now(),
    granted_by_user_id uuid,
    granted_by_service_identity_id uuid,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    revocation_reason_code varchar(64),
    last_reviewed_at timestamptz,
    last_reviewed_by_user_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint NOT NULL DEFAULT 1,
    CONSTRAINT pk_user_role_scope_grants PRIMARY KEY (user_role_scope_grant_id),
    CONSTRAINT fk_user_role_scope_grants__user_role FOREIGN KEY (user_role_id) REFERENCES identity.user_roles(user_role_id),
    CONSTRAINT fk_user_role_scope_grants__site FOREIGN KEY (site_id) REFERENCES sites.sites(site_id),
    CONSTRAINT fk_user_role_scope_grants__site_group FOREIGN KEY (site_group_id) REFERENCES sites.site_groups(site_group_id),
    CONSTRAINT fk_user_role_scope_grants__granted_user FOREIGN KEY (granted_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_user_role_scope_grants__granted_service FOREIGN KEY (granted_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT fk_user_role_scope_grants__revoked_user FOREIGN KEY (revoked_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT fk_user_role_scope_grants__revoked_service FOREIGN KEY (revoked_by_service_identity_id) REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT fk_user_role_scope_grants__reviewed_user FOREIGN KEY (last_reviewed_by_user_id) REFERENCES identity.users(user_id),
    CONSTRAINT ck_user_role_scope_grants__scope_shape CHECK (
        (scope_type = 'SITE' AND site_id IS NOT NULL AND site_group_id IS NULL)
        OR (scope_type = 'SITE_GROUP' AND site_id IS NULL AND site_group_id IS NOT NULL)
        OR (scope_type = 'GLOBAL' AND site_id IS NULL AND site_group_id IS NULL)),
    CONSTRAINT ck_user_role_scope_grants__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT ck_user_role_scope_grants__grant_actor CHECK (num_nonnulls(granted_by_user_id, granted_by_service_identity_id) = 1),
    CONSTRAINT ck_user_role_scope_grants__reason CHECK (btrim(grant_reason_code) <> ''),
    CONSTRAINT ck_user_role_scope_grants__review CHECK ((last_reviewed_at IS NULL) = (last_reviewed_by_user_id IS NULL)),
    CONSTRAINT ck_user_role_scope_grants__revocation CHECK ((grant_status = 'REVOKED') = (revoked_at IS NOT NULL)),
    CONSTRAINT ck_user_role_scope_grants__revocation_actor CHECK (
        (revoked_at IS NULL AND revoked_by_user_id IS NULL AND revoked_by_service_identity_id IS NULL)
        OR (revoked_at IS NOT NULL AND num_nonnulls(revoked_by_user_id, revoked_by_service_identity_id) = 1)),
    CONSTRAINT ck_user_role_scope_grants__row_version CHECK (row_version > 0)
);

CREATE INDEX IF NOT EXISTS ix_user_role_scope_grants__global
    ON identity.user_role_scope_grants (grant_status, effective_from, effective_to)
    WHERE scope_type = 'GLOBAL';
CREATE INDEX IF NOT EXISTS ix_user_role_scope_grants__role_effective
    ON identity.user_role_scope_grants (user_role_id, grant_status, effective_from, effective_to);
CREATE INDEX IF NOT EXISTS ix_user_role_scope_grants__site
    ON identity.user_role_scope_grants (site_id, grant_status, effective_from, effective_to)
    WHERE site_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_user_role_scope_grants__site_group
    ON identity.user_role_scope_grants (site_group_id, grant_status, effective_from, effective_to)
    WHERE site_group_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_user_role_scope_grants__current_exact
    ON identity.user_role_scope_grants (
        user_role_id,
        scope_type,
        COALESCE(site_id, '00000000-0000-0000-0000-000000000000'::uuid),
        COALESCE(site_group_id, '00000000-0000-0000-0000-000000000000'::uuid))
    WHERE grant_status IN ('PENDING', 'ACTIVE', 'SUSPENDED');
