/*
 * ExitPass v1.2 durable SQL patch.
 *
 * Operator Console schema migration draft.
 *
 * References:
 * - docs/operator-console/operator-console-schema-extension-design.md
 * - docs/operator-console/proposals/operator-console-ddl-proposal.sql
 * - docs/operator-console/proposals/operator-console-ddl-proposal-notes.md
 *
 * This patch creates Operator Console access, HR/Timekeeping shift import,
 * browser/device binding, takeover, access evaluation evidence, and statutory
 * entitlement fingerprint storage.
 *
 * Non-payment boundary:
 * The objects below must not create, mutate, route, configure, or invoke payment
 * attempts, payment confirmations, payment provider outcomes, exit
 * authorizations, gate authorization consumptions, coupon applications,
 * settlement truth records, provider routing, or payment finality.
 */

CREATE SCHEMA IF NOT EXISTS operator_console;

COMMENT ON SCHEMA operator_console IS
    'Operator Console access, device binding, HR shift import, takeover, and access evaluation support.';

DO $$ BEGIN
    CREATE TYPE operator_console.hr_identity_mapping_status_enum AS ENUM (
        'ACTIVE',
        'SUSPENDED',
        'REVOKED',
        'EXPIRED',
        'SUPERSEDED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE operator_console.operator_shift_operational_status_enum AS ENUM (
        'SCHEDULED',
        'ACTIVE',
        'ENDED',
        'SUSPENDED',
        'REVOKED',
        'TAKEN_OVER',
        'CANCELLED',
        'IMPORT_CONFLICT'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE operator_console.shift_revocation_status_enum AS ENUM (
        'REQUESTED',
        'APPROVED',
        'REJECTED',
        'CANCELLED',
        'EFFECTIVE',
        'EXPIRED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE operator_console.shift_takeover_status_enum AS ENUM (
        'REQUESTED',
        'PENDING_APPROVAL',
        'APPROVED',
        'REJECTED',
        'ACTIVE',
        'ENDED',
        'CANCELLED',
        'EXPIRED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE operator_console.operator_device_binding_status_enum AS ENUM (
        'PENDING',
        'ACTIVE',
        'SUSPENDED',
        'REVOKED',
        'LOST',
        'EXPIRED',
        'RETIRED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE operator_console.operator_device_trust_level_enum AS ENUM (
        'BROWSER_KEY_ONLY',
        'MTLS_ONLY',
        'BROWSER_KEY_AND_MTLS',
        'UNVERIFIED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE operator_console.access_evaluation_status_enum AS ENUM (
        'ALLOWED',
        'DENIED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.entitlement_fingerprint_status_enum AS ENUM (
        'ACTIVE',
        'SUPERSEDED',
        'REDACTED',
        'PURGED',
        'HASH_ONLY'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE TABLE IF NOT EXISTS operator_console.hr_identity_mappings (
    hr_identity_mapping_id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    hr_provider_code varchar(64) NOT NULL,
    external_person_id_hash char(64) NOT NULL,
    external_person_id_masked varchar(64),
    external_employee_number_hash char(64),
    external_employee_number_masked varchar(64),
    mapping_status operator_console.hr_identity_mapping_status_enum NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revoked_by_service_identity_id uuid,
    revocation_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_hr_identity_mappings PRIMARY KEY (hr_identity_mapping_id),
    CONSTRAINT fk_hr_identity_mappings__user_id FOREIGN KEY (user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_hr_identity_mappings__revoked_by_user_id FOREIGN KEY (revoked_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_hr_identity_mappings__revoked_by_service_identity_id FOREIGN KEY (revoked_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_hr_identity_mappings__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_hr_identity_mappings__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_hr_identity_mappings__updated_by_user_id FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_hr_identity_mappings__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_hr_identity_mappings__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT ck_hr_identity_mappings__row_version_positive CHECK (row_version > 0)
);

COMMENT ON TABLE operator_console.hr_identity_mappings IS
    'Maps ExitPass users to imported HR/Timekeeping identities using hashed/masked external identifiers.';

CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_identity_mappings__active_external_person
    ON operator_console.hr_identity_mappings (hr_provider_code, external_person_id_hash)
    WHERE mapping_status = 'ACTIVE';

CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_identity_mappings__active_user_provider
    ON operator_console.hr_identity_mappings (user_id, hr_provider_code)
    WHERE mapping_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_hr_identity_mappings__user_status
    ON operator_console.hr_identity_mappings (user_id, mapping_status);

CREATE INDEX IF NOT EXISTS ix_hr_identity_mappings__correlation_id
    ON operator_console.hr_identity_mappings (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS operator_console.operator_shifts (
    operator_shift_id uuid DEFAULT gen_random_uuid() NOT NULL,
    hr_provider_code varchar(64) NOT NULL,
    external_shift_id_hash char(64) NOT NULL,
    external_shift_id_masked varchar(64),
    hr_identity_mapping_id uuid NOT NULL,
    operator_user_id uuid NOT NULL,
    site_group_id uuid NOT NULL,
    site_id uuid NOT NULL,
    scheduled_start_at timestamptz NOT NULL,
    scheduled_end_at timestamptz NOT NULL,
    source_imported_at timestamptz NOT NULL,
    import_status_code varchar(64) NOT NULL,
    source_system_code varchar(64) NOT NULL,
    source_status_code varchar(96),
    source_status_description text,
    operational_status operator_console.operator_shift_operational_status_enum NOT NULL,
    active_from timestamptz,
    active_to timestamptz,
    revoked_at timestamptz,
    revoked_by_user_id uuid,
    revocation_reason_code varchar(64),
    current_takeover_id uuid,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_operator_shifts PRIMARY KEY (operator_shift_id),
    CONSTRAINT fk_operator_shifts__hr_identity_mapping_id FOREIGN KEY (hr_identity_mapping_id)
        REFERENCES operator_console.hr_identity_mappings(hr_identity_mapping_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__operator_user_id FOREIGN KEY (operator_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__site_group_id FOREIGN KEY (site_group_id)
        REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__site_id FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__revoked_by_user_id FOREIGN KEY (revoked_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__updated_by_user_id FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shifts__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_operator_shifts__scheduled_window CHECK (scheduled_end_at > scheduled_start_at),
    CONSTRAINT ck_operator_shifts__active_window CHECK (active_to IS NULL OR active_from IS NULL OR active_to > active_from),
    CONSTRAINT ck_operator_shifts__row_version_positive CHECK (row_version > 0)
);

COMMENT ON TABLE operator_console.operator_shifts IS
    'Current operational state for imported HR/Timekeeping operator shifts.';

CREATE UNIQUE INDEX IF NOT EXISTS ux_operator_shifts__source_shift
    ON operator_console.operator_shifts (hr_provider_code, external_shift_id_hash);

CREATE INDEX IF NOT EXISTS ix_operator_shifts__active_operator_site_time
    ON operator_console.operator_shifts (operator_user_id, site_id, active_from, active_to)
    WHERE operational_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_operator_shifts__site_time_status
    ON operator_console.operator_shifts (site_id, scheduled_start_at, scheduled_end_at, operational_status);

CREATE INDEX IF NOT EXISTS ix_operator_shifts__mapping_status
    ON operator_console.operator_shifts (hr_identity_mapping_id, operational_status);

CREATE INDEX IF NOT EXISTS ix_operator_shifts__operator_site_status
    ON operator_console.operator_shifts (operator_user_id, site_id, operational_status);

CREATE INDEX IF NOT EXISTS ix_operator_shifts__correlation_id
    ON operator_console.operator_shifts (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS operator_console.operator_shift_versions (
    operator_shift_version_id uuid DEFAULT gen_random_uuid() NOT NULL,
    operator_shift_id uuid NOT NULL,
    hr_provider_code varchar(64) NOT NULL,
    external_shift_id_hash char(64) NOT NULL,
    source_payload_hash char(64),
    source_payload_ref varchar(256),
    import_status_code varchar(64) NOT NULL,
    source_system_code varchar(64) NOT NULL,
    source_status_code varchar(96),
    source_status_description text,
    scheduled_start_at timestamptz NOT NULL,
    scheduled_end_at timestamptz NOT NULL,
    site_id uuid,
    operator_user_id uuid,
    imported_at timestamptz NOT NULL,
    imported_by_service_identity_id uuid NOT NULL,
    correlation_id uuid,
    CONSTRAINT pk_operator_shift_versions PRIMARY KEY (operator_shift_version_id),
    CONSTRAINT fk_operator_shift_versions__operator_shift_id FOREIGN KEY (operator_shift_id)
        REFERENCES operator_console.operator_shifts(operator_shift_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shift_versions__site_id FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shift_versions__operator_user_id FOREIGN KEY (operator_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_shift_versions__imported_by_service_identity_id FOREIGN KEY (imported_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_operator_shift_versions__scheduled_window CHECK (scheduled_end_at > scheduled_start_at)
);

COMMENT ON TABLE operator_console.operator_shift_versions IS
    'Immutable import/version history for HR/Timekeeping operator shifts.';

CREATE INDEX IF NOT EXISTS ix_operator_shift_versions__shift_imported_at
    ON operator_console.operator_shift_versions (operator_shift_id, imported_at DESC);

CREATE INDEX IF NOT EXISTS ix_operator_shift_versions__source_shift
    ON operator_console.operator_shift_versions (hr_provider_code, external_shift_id_hash);

CREATE INDEX IF NOT EXISTS ix_operator_shift_versions__correlation_id
    ON operator_console.operator_shift_versions (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS operator_console.shift_revocations (
    shift_revocation_id uuid DEFAULT gen_random_uuid() NOT NULL,
    operator_shift_id uuid NOT NULL,
    revocation_status operator_console.shift_revocation_status_enum NOT NULL,
    reason_code varchar(64) NOT NULL,
    reason_note text,
    requested_by_user_id uuid NOT NULL,
    approved_by_user_id uuid,
    revoked_operator_user_id uuid NOT NULL,
    site_id uuid NOT NULL,
    requested_at timestamptz NOT NULL,
    approved_at timestamptz,
    effective_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_shift_revocations PRIMARY KEY (shift_revocation_id),
    CONSTRAINT fk_shift_revocations__operator_shift_id FOREIGN KEY (operator_shift_id)
        REFERENCES operator_console.operator_shifts(operator_shift_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__requested_by_user_id FOREIGN KEY (requested_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__approved_by_user_id FOREIGN KEY (approved_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__revoked_operator_user_id FOREIGN KEY (revoked_operator_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__site_id FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__updated_by_user_id FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_revocations__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_shift_revocations__row_version_positive CHECK (row_version > 0)
);

CREATE INDEX IF NOT EXISTS ix_shift_revocations__shift_status
    ON operator_console.shift_revocations (operator_shift_id, revocation_status, requested_at DESC);

CREATE INDEX IF NOT EXISTS ix_shift_revocations__site_effective
    ON operator_console.shift_revocations (site_id, effective_at DESC);

CREATE INDEX IF NOT EXISTS ix_shift_revocations__correlation_id
    ON operator_console.shift_revocations (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS operator_console.shift_takeovers (
    shift_takeover_id uuid DEFAULT gen_random_uuid() NOT NULL,
    operator_shift_id uuid NOT NULL,
    original_operator_user_id uuid NOT NULL,
    takeover_operator_user_id uuid NOT NULL,
    takeover_status operator_console.shift_takeover_status_enum NOT NULL,
    reason_code varchar(64) NOT NULL,
    reason_note text,
    requested_by_user_id uuid NOT NULL,
    approved_by_user_id uuid,
    site_id uuid NOT NULL,
    requested_at timestamptz NOT NULL,
    approved_at timestamptz,
    active_from timestamptz,
    active_to timestamptz,
    ended_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_shift_takeovers PRIMARY KEY (shift_takeover_id),
    CONSTRAINT fk_shift_takeovers__operator_shift_id FOREIGN KEY (operator_shift_id)
        REFERENCES operator_console.operator_shifts(operator_shift_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__original_operator_user_id FOREIGN KEY (original_operator_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__takeover_operator_user_id FOREIGN KEY (takeover_operator_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__requested_by_user_id FOREIGN KEY (requested_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__approved_by_user_id FOREIGN KEY (approved_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__site_id FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__updated_by_user_id FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_shift_takeovers__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_shift_takeovers__different_users CHECK (original_operator_user_id <> takeover_operator_user_id),
    CONSTRAINT ck_shift_takeovers__active_window CHECK (active_to IS NULL OR active_from IS NULL OR active_to > active_from),
    CONSTRAINT ck_shift_takeovers__row_version_positive CHECK (row_version > 0)
);

DO $$ BEGIN
    ALTER TABLE operator_console.operator_shifts
        ADD CONSTRAINT fk_operator_shifts__current_takeover_id
        FOREIGN KEY (current_takeover_id)
        REFERENCES operator_console.shift_takeovers(shift_takeover_id)
        DEFERRABLE INITIALLY IMMEDIATE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE INDEX IF NOT EXISTS ix_shift_takeovers__shift_status
    ON operator_console.shift_takeovers (operator_shift_id, takeover_status, requested_at DESC);

CREATE INDEX IF NOT EXISTS ix_shift_takeovers__active_takeover_operator
    ON operator_console.shift_takeovers (takeover_operator_user_id, site_id, active_from, active_to)
    WHERE takeover_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_shift_takeovers__correlation_id
    ON operator_console.shift_takeovers (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS operator_console.operator_device_bindings (
    operator_device_binding_id uuid DEFAULT gen_random_uuid() NOT NULL,
    device_binding_code varchar(64) NOT NULL,
    device_name varchar(128) NOT NULL,
    site_group_id uuid NOT NULL,
    site_id uuid NOT NULL,
    service_identity_id uuid,
    browser_key_thumbprint char(64),
    browser_public_key_ref varchar(256),
    mtls_certificate_thumbprint char(64),
    mtls_certificate_subject varchar(256),
    mtls_certificate_expires_at timestamptz,
    device_status operator_console.operator_device_binding_status_enum NOT NULL,
    trust_level operator_console.operator_device_trust_level_enum NOT NULL,
    binding_source varchar(64) NOT NULL,
    last_seen_at timestamptz,
    revoked_at timestamptz,
    revocation_reason_code varchar(64),
    lost_reported_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_operator_device_bindings PRIMARY KEY (operator_device_binding_id),
    CONSTRAINT fk_operator_device_bindings__site_group_id FOREIGN KEY (site_group_id)
        REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_bindings__site_id FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_bindings__service_identity_id FOREIGN KEY (service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_bindings__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_bindings__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_bindings__updated_by_user_id FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_bindings__updated_by_service_identity_id FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_operator_device_bindings__has_trust_material CHECK (
        browser_key_thumbprint IS NOT NULL
        OR mtls_certificate_thumbprint IS NOT NULL
        OR service_identity_id IS NOT NULL
    ),
    CONSTRAINT ck_operator_device_bindings__row_version_positive CHECK (row_version > 0)
);

COMMENT ON TABLE operator_console.operator_device_bindings IS
    'Operator Console browser/device trust binding, separate from gate devices.';

CREATE UNIQUE INDEX IF NOT EXISTS ux_operator_device_bindings__device_binding_code
    ON operator_console.operator_device_bindings (device_binding_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_operator_device_bindings__active_browser_key
    ON operator_console.operator_device_bindings (browser_key_thumbprint)
    WHERE device_status = 'ACTIVE' AND browser_key_thumbprint IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_operator_device_bindings__active_mtls_thumbprint
    ON operator_console.operator_device_bindings (mtls_certificate_thumbprint)
    WHERE device_status = 'ACTIVE' AND mtls_certificate_thumbprint IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_operator_device_bindings__active_service_identity
    ON operator_console.operator_device_bindings (service_identity_id)
    WHERE device_status = 'ACTIVE' AND service_identity_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_operator_device_bindings__site_status
    ON operator_console.operator_device_bindings (site_id, device_status, trust_level);

CREATE INDEX IF NOT EXISTS ix_operator_device_bindings__last_seen
    ON operator_console.operator_device_bindings (last_seen_at DESC);

CREATE INDEX IF NOT EXISTS ix_operator_device_bindings__correlation_id
    ON operator_console.operator_device_bindings (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS operator_console.operator_device_assignment_history (
    operator_device_assignment_history_id uuid DEFAULT gen_random_uuid() NOT NULL,
    operator_device_binding_id uuid NOT NULL,
    site_group_id uuid NOT NULL,
    site_id uuid NOT NULL,
    assignment_status_code varchar(64) NOT NULL,
    assignment_source_code varchar(64) NOT NULL,
    assignment_reason_code varchar(64),
    assigned_at timestamptz NOT NULL,
    assigned_by_user_id uuid,
    assigned_by_service_identity_id uuid,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    ended_at timestamptz,
    ended_by_user_id uuid,
    ended_by_service_identity_id uuid,
    end_reason_code varchar(64),
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_operator_device_assignment_history PRIMARY KEY (operator_device_assignment_history_id),
    CONSTRAINT fk_operator_device_assignment_history__binding_id FOREIGN KEY (operator_device_binding_id)
        REFERENCES operator_console.operator_device_bindings(operator_device_binding_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_assignment_history__site_group_id FOREIGN KEY (site_group_id)
        REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_assignment_history__site_id FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_assignment_history__assigned_by_user_id FOREIGN KEY (assigned_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_op_dev_assign_hist__assigned_svc_identity FOREIGN KEY (assigned_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_assignment_history__ended_by_user_id FOREIGN KEY (ended_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_op_dev_assign_hist__ended_svc_identity FOREIGN KEY (ended_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_device_assignment_history__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_op_dev_assign_hist__created_svc_identity FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_operator_device_assignment_history__effective_window CHECK (effective_to IS NULL OR effective_to > effective_from)
);

COMMENT ON TABLE operator_console.operator_device_assignment_history IS
    'Reconstructable Operator Console device/site assignment history for authorization audit.';

CREATE INDEX IF NOT EXISTS ix_operator_device_assignment_history__binding_time
    ON operator_console.operator_device_assignment_history (operator_device_binding_id, effective_from DESC);

CREATE INDEX IF NOT EXISTS ix_operator_device_assignment_history__site_time
    ON operator_console.operator_device_assignment_history (site_id, effective_from DESC, effective_to);

CREATE INDEX IF NOT EXISTS ix_operator_device_assignment_history__active_binding_site
    ON operator_console.operator_device_assignment_history (operator_device_binding_id, site_id, effective_from, effective_to)
    WHERE effective_to IS NULL;

CREATE INDEX IF NOT EXISTS ix_operator_device_assignment_history__correlation_id
    ON operator_console.operator_device_assignment_history (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS operator_console.operator_access_evaluations (
    operator_access_evaluation_id uuid DEFAULT gen_random_uuid() NOT NULL,
    correlation_id uuid,
    requested_action varchar(96) NOT NULL,
    evaluation_status operator_console.access_evaluation_status_enum NOT NULL,
    operator_user_id uuid NOT NULL,
    hr_identity_mapping_id uuid,
    operator_device_binding_id uuid,
    operator_shift_id uuid,
    shift_takeover_id uuid,
    site_group_id uuid,
    site_id uuid,
    target_entity_type varchar(64),
    target_entity_id uuid,
    evaluated_at timestamptz NOT NULL,
    decision_snapshot_json jsonb,
    audit_event_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_operator_access_evaluations PRIMARY KEY (operator_access_evaluation_id),
    CONSTRAINT fk_operator_access_evaluations__operator_user_id FOREIGN KEY (operator_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__hr_identity_mapping_id FOREIGN KEY (hr_identity_mapping_id)
        REFERENCES operator_console.hr_identity_mappings(hr_identity_mapping_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__operator_device_binding_id FOREIGN KEY (operator_device_binding_id)
        REFERENCES operator_console.operator_device_bindings(operator_device_binding_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__operator_shift_id FOREIGN KEY (operator_shift_id)
        REFERENCES operator_console.operator_shifts(operator_shift_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__shift_takeover_id FOREIGN KEY (shift_takeover_id)
        REFERENCES operator_console.shift_takeovers(shift_takeover_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__site_group_id FOREIGN KEY (site_group_id)
        REFERENCES sites.site_groups(site_group_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__site_id FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__audit_event_id FOREIGN KEY (audit_event_id)
        REFERENCES audit.audit_events(audit_event_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluations__created_by_service_identity_id FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE
);

COMMENT ON TABLE operator_console.operator_access_evaluations IS
    'Persisted access evaluation evidence for denied access and controlled Operator Console actions only.';

COMMENT ON COLUMN operator_console.operator_access_evaluations.requested_action IS
    'Controlled action code. Access evaluation runs at workflow start and before every controlled action, but MVP persistence is limited to denied access and controlled-action evaluations; harmless reads/navigation are not persisted.';

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluations__operator_time
    ON operator_console.operator_access_evaluations (operator_user_id, evaluated_at DESC);

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluations__site_action_time
    ON operator_console.operator_access_evaluations (site_id, requested_action, evaluated_at DESC);

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluations__device_time
    ON operator_console.operator_access_evaluations (operator_device_binding_id, evaluated_at DESC);

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluations__shift_time
    ON operator_console.operator_access_evaluations (operator_shift_id, evaluated_at DESC);

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluations__target_time
    ON operator_console.operator_access_evaluations (target_entity_type, target_entity_id, evaluated_at DESC)
    WHERE target_entity_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluations__correlation_id
    ON operator_console.operator_access_evaluations (correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluations__denied
    ON operator_console.operator_access_evaluations (evaluated_at DESC, requested_action)
    WHERE evaluation_status = 'DENIED';

CREATE TABLE IF NOT EXISTS operator_console.operator_access_evaluation_reasons (
    operator_access_evaluation_reason_id uuid DEFAULT gen_random_uuid() NOT NULL,
    operator_access_evaluation_id uuid NOT NULL,
    reason_code varchar(96) NOT NULL,
    reason_message text,
    reason_source varchar(96),
    source_entity_type varchar(64),
    source_entity_id uuid,
    evaluated_fact_path varchar(256),
    display_order integer DEFAULT 0 NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    CONSTRAINT pk_operator_access_evaluation_reasons PRIMARY KEY (operator_access_evaluation_reason_id),
    CONSTRAINT fk_operator_access_evaluation_reasons__evaluation_id FOREIGN KEY (operator_access_evaluation_id)
        REFERENCES operator_console.operator_access_evaluations(operator_access_evaluation_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_operator_access_evaluation_reasons__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_op_access_eval_reasons__created_svc_identity FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_op_access_eval_reasons__display_order_nonneg CHECK (display_order >= 0)
);

COMMENT ON TABLE operator_console.operator_access_evaluation_reasons IS
    'Normalized controlled reason rows for denied or controlled-action access evaluations, with source context for audit and reporting.';

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluation_reasons__evaluation_order
    ON operator_console.operator_access_evaluation_reasons (operator_access_evaluation_id, display_order);

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluation_reasons__reason_time
    ON operator_console.operator_access_evaluation_reasons (reason_code, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_operator_access_evaluation_reasons__source
    ON operator_console.operator_access_evaluation_reasons (reason_source, source_entity_type, source_entity_id)
    WHERE reason_source IS NOT NULL;

CREATE TABLE IF NOT EXISTS discounts.statutory_entitlement_fingerprints (
    statutory_entitlement_fingerprint_id uuid DEFAULT gen_random_uuid() NOT NULL,
    statutory_discount_validation_id uuid NOT NULL,
    entitlement_type discounts.statutory_entitlement_type_enum NOT NULL,
    fingerprint_hash char(64) NOT NULL,
    fingerprint_algorithm varchar(64) NOT NULL,
    fingerprint_algorithm_version varchar(32) NOT NULL,
    salt_reference varchar(256) NOT NULL,
    source_metadata_level varchar(64) NOT NULL,
    duplicate_detection_scope varchar(64) NOT NULL,
    matched_existing_fingerprint_id uuid,
    fingerprint_status discounts.entitlement_fingerprint_status_enum NOT NULL,
    generated_at timestamptz NOT NULL,
    generated_by_service_identity_id uuid NOT NULL,
    retention_policy_code varchar(64) NOT NULL,
    purged_at timestamptz,
    correlation_id uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_statutory_entitlement_fingerprints PRIMARY KEY (statutory_entitlement_fingerprint_id),
    CONSTRAINT fk_statutory_entitlement_fingerprints__validation_id FOREIGN KEY (statutory_discount_validation_id)
        REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_statutory_entitlement_fingerprints__matched_existing_id FOREIGN KEY (matched_existing_fingerprint_id)
        REFERENCES discounts.statutory_entitlement_fingerprints(statutory_entitlement_fingerprint_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_stat_ent_fps__generated_svc_identity FOREIGN KEY (generated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_statutory_entitlement_fingerprints__created_by_user_id FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_stat_ent_fps__created_svc_identity FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_statutory_entitlement_fingerprints__updated_by_user_id FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_stat_ent_fps__updated_svc_identity FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id) DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_statutory_entitlement_fingerprints__row_version_positive CHECK (row_version > 0)
);

COMMENT ON TABLE discounts.statutory_entitlement_fingerprints IS
    'Duplicate-detection fingerprints for statutory entitlement validation without raw personal data.';

COMMENT ON COLUMN discounts.statutory_entitlement_fingerprints.fingerprint_hash IS
    'Stable hash used for duplicate detection. Do not store raw statutory ID numbers, birth dates, or unmasked identity values.';

COMMENT ON COLUMN discounts.statutory_entitlement_fingerprints.salt_reference IS
    'Reference to salt/pepper material only; never store secret values in this table.';

COMMENT ON COLUMN discounts.statutory_entitlement_fingerprints.duplicate_detection_scope IS
    'Controlled-code value, not a PostgreSQL enum. Initial family: OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE.';

CREATE INDEX IF NOT EXISTS ix_statutory_entitlement_fingerprints__validation
    ON discounts.statutory_entitlement_fingerprints (statutory_discount_validation_id);

CREATE INDEX IF NOT EXISTS ix_statutory_entitlement_fingerprints__duplicate_detection
    ON discounts.statutory_entitlement_fingerprints (
        entitlement_type,
        duplicate_detection_scope,
        fingerprint_hash
    )
    WHERE fingerprint_status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_statutory_entitlement_fingerprints__matched_existing
    ON discounts.statutory_entitlement_fingerprints (matched_existing_fingerprint_id)
    WHERE matched_existing_fingerprint_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_statutory_entitlement_fingerprints__correlation_id
    ON discounts.statutory_entitlement_fingerprints (correlation_id)
    WHERE correlation_id IS NOT NULL;

INSERT INTO config.controlled_code_sets (
    code_set_name,
    code_value,
    code_label,
    code_description,
    code_domain,
    code_status,
    sort_order,
    requires_comment,
    requires_approval,
    is_sensitive,
    effective_from
)
VALUES
    ('OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE', 'SAME_SESSION_ONLY', 'Same Session Only', 'Duplicate detection is scoped to the same parking session.', 'OPERATOR_CONSOLE', 'ACTIVE', 10, false, false, false, '2026-01-01T00:00:00Z'),
    ('OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE', 'SAME_SITE_ACTIVE_DAY', 'Same Site Active Day', 'Duplicate detection is scoped to the same site and active business day.', 'OPERATOR_CONSOLE', 'ACTIVE', 20, false, false, false, '2026-01-01T00:00:00Z'),
    ('OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE', 'SAME_SITE_GROUP_ACTIVE_DAY', 'Same Site Group Active Day', 'Duplicate detection is scoped to the same site group and active business day.', 'OPERATOR_CONSOLE', 'ACTIVE', 30, false, false, false, '2026-01-01T00:00:00Z'),
    ('OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE', 'GLOBAL_ACTIVE_DAY', 'Global Active Day', 'Duplicate detection is scoped globally for the active business day.', 'OPERATOR_CONSOLE', 'ACTIVE', 40, false, false, false, '2026-01-01T00:00:00Z'),
    ('OPERATOR_CONSOLE_DUPLICATE_DETECTION_SCOPE', 'CONFIGURED_POLICY_WINDOW', 'Configured Policy Window', 'Duplicate detection is scoped by configured statutory discount policy window.', 'OPERATOR_CONSOLE', 'ACTIVE', 50, false, false, false, '2026-01-01T00:00:00Z')
ON CONFLICT ON CONSTRAINT uq_controlled_code_sets__set_value_domain
DO UPDATE SET
    code_label = EXCLUDED.code_label,
    code_description = EXCLUDED.code_description,
    code_status = EXCLUDED.code_status,
    sort_order = EXCLUDED.sort_order,
    requires_comment = EXCLUDED.requires_comment,
    requires_approval = EXCLUDED.requires_approval,
    is_sensitive = EXCLUDED.is_sensitive,
    updated_at = now(),
    row_version = config.controlled_code_sets.row_version + 1;
