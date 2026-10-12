-- ExitPass v1.3 governed Device Administration.
-- Extends the canonical service-identity and Site-assignment model; it does not
-- create an alternate runtime principal or Site authority.
BEGIN;

DO $$ BEGIN
    CREATE TYPE identity.device_type_enum AS ENUM (
        'OPERATOR_CONSOLE',
        'ASSISTED_PAYMENT_TERMINAL'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE identity.device_lifecycle_status_enum AS ENUM (
        'REGISTERED',
        'PROVISIONING_PENDING',
        'ACTIVE',
        'SUSPENDED',
        'CREDENTIAL_ROTATION',
        'REPLACED',
        'REVOKED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE identity.device_provisioning_status_enum AS ENUM (
        'ISSUED',
        'CONSUMED',
        'CANCELLED',
        'EXPIRED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE identity.device_provisioning_purpose_enum AS ENUM (
        'ENROLLMENT',
        'ROTATION',
        'REPLACEMENT'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE identity.device_credential_status_enum AS ENUM (
        'ACTIVE',
        'REVOKED',
        'EXPIRED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE TABLE IF NOT EXISTS identity.devices (
    device_id uuid DEFAULT gen_random_uuid() NOT NULL,
    device_reference varchar(64) NOT NULL,
    service_identity_id uuid NOT NULL,
    device_name varchar(128) NOT NULL,
    device_type identity.device_type_enum NOT NULL,
    device_description varchar(512),
    terminal_identifier varchar(128),
    lifecycle_status identity.device_lifecycle_status_enum NOT NULL,
    credential_version bigint DEFAULT 0 NOT NULL,
    last_provisioning_status identity.device_provisioning_status_enum,
    enrolled_at timestamptz,
    suspended_at timestamptz,
    revoked_at timestamptz,
    replaced_at timestamptz,
    replacement_device_id uuid,
    predecessor_device_id uuid,
    effective_from timestamptz DEFAULT now() NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_devices PRIMARY KEY (device_id),
    CONSTRAINT uq_devices__device_reference UNIQUE (device_reference),
    CONSTRAINT uq_devices__service_identity UNIQUE (service_identity_id),
    CONSTRAINT fk_devices__service_identity FOREIGN KEY (service_identity_id)
        REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT fk_devices__replacement FOREIGN KEY (replacement_device_id)
        REFERENCES identity.devices(device_id) DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT fk_devices__predecessor FOREIGN KEY (predecessor_device_id)
        REFERENCES identity.devices(device_id) DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT fk_devices__created_by FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id),
    CONSTRAINT fk_devices__updated_by FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id),
    CONSTRAINT ck_devices__credential_version CHECK (credential_version >= 0),
    CONSTRAINT ck_devices__row_version CHECK (row_version > 0),
    CONSTRAINT ck_devices__terminal_identifier CHECK (
        device_type <> 'ASSISTED_PAYMENT_TERMINAL' OR terminal_identifier IS NOT NULL
    ),
    CONSTRAINT ck_devices__terminal_state CHECK (
        (lifecycle_status = 'REVOKED' AND revoked_at IS NOT NULL)
        OR (lifecycle_status = 'REPLACED' AND replaced_at IS NOT NULL AND replacement_device_id IS NOT NULL)
        OR lifecycle_status NOT IN ('REVOKED', 'REPLACED')
    )
);

COMMENT ON TABLE identity.devices IS
    'Governed v1.3 device lifecycle extension over canonical identity.service_identities.';

CREATE INDEX IF NOT EXISTS ix_devices__type_status
    ON identity.devices (device_type, lifecycle_status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_devices__predecessor
    ON identity.devices (predecessor_device_id) WHERE predecessor_device_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS identity.device_provisioning_credentials (
    device_provisioning_credential_id uuid DEFAULT gen_random_uuid() NOT NULL,
    device_id uuid NOT NULL,
    token_hash char(64) NOT NULL,
    provisioning_purpose identity.device_provisioning_purpose_enum NOT NULL,
    provisioning_status identity.device_provisioning_status_enum NOT NULL,
    intended_device_type identity.device_type_enum NOT NULL,
    intended_site_id uuid NOT NULL,
    intended_site_group_id uuid,
    intended_credential_version bigint NOT NULL,
    issued_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    cancelled_at timestamptz,
    issued_by_user_id uuid NOT NULL,
    cancellation_reason_code varchar(64),
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_device_provisioning_credentials PRIMARY KEY (device_provisioning_credential_id),
    CONSTRAINT uq_device_provisioning_credentials__token_hash UNIQUE (token_hash),
    CONSTRAINT fk_device_provisioning_credentials__device FOREIGN KEY (device_id)
        REFERENCES identity.devices(device_id),
    CONSTRAINT fk_device_provisioning_credentials__site FOREIGN KEY (intended_site_id)
        REFERENCES sites.sites(site_id),
    CONSTRAINT fk_device_provisioning_credentials__site_group FOREIGN KEY (intended_site_group_id)
        REFERENCES sites.site_groups(site_group_id),
    CONSTRAINT fk_device_provisioning_credentials__issuer FOREIGN KEY (issued_by_user_id)
        REFERENCES identity.users(user_id),
    CONSTRAINT ck_device_provisioning_credentials__window CHECK (expires_at > issued_at),
    CONSTRAINT ck_device_provisioning_credentials__version CHECK (intended_credential_version > 0),
    CONSTRAINT ck_device_provisioning_credentials__state CHECK (
        (provisioning_status = 'CONSUMED' AND consumed_at IS NOT NULL AND cancelled_at IS NULL)
        OR (provisioning_status = 'CANCELLED' AND cancelled_at IS NOT NULL AND consumed_at IS NULL)
        OR (provisioning_status IN ('ISSUED', 'EXPIRED') AND consumed_at IS NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_device_provisioning_credentials__one_issued
    ON identity.device_provisioning_credentials (device_id)
    WHERE provisioning_status = 'ISSUED';
CREATE INDEX IF NOT EXISTS ix_device_provisioning_credentials__device_history
    ON identity.device_provisioning_credentials (device_id, issued_at DESC);

CREATE TABLE IF NOT EXISTS identity.device_credential_history (
    device_credential_history_id uuid DEFAULT gen_random_uuid() NOT NULL,
    device_id uuid NOT NULL,
    credential_version bigint NOT NULL,
    credential_type identity.service_credential_type_enum NOT NULL,
    credential_status identity.device_credential_status_enum NOT NULL,
    credential_fingerprint char(64) NOT NULL,
    activated_at timestamptz NOT NULL,
    expires_at timestamptz,
    revoked_at timestamptz,
    revocation_reason_code varchar(64),
    provisioning_credential_id uuid,
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_device_credential_history PRIMARY KEY (device_credential_history_id),
    CONSTRAINT uq_device_credential_history__version UNIQUE (device_id, credential_version),
    CONSTRAINT fk_device_credential_history__device FOREIGN KEY (device_id)
        REFERENCES identity.devices(device_id),
    CONSTRAINT fk_device_credential_history__provisioning FOREIGN KEY (provisioning_credential_id)
        REFERENCES identity.device_provisioning_credentials(device_provisioning_credential_id),
    CONSTRAINT ck_device_credential_history__version CHECK (credential_version > 0),
    CONSTRAINT ck_device_credential_history__state CHECK (
        (credential_status = 'REVOKED' AND revoked_at IS NOT NULL)
        OR credential_status <> 'REVOKED'
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_device_credential_history__one_active
    ON identity.device_credential_history (device_id)
    WHERE credential_status = 'ACTIVE';
CREATE UNIQUE INDEX IF NOT EXISTS ux_device_credential_history__active_fingerprint
    ON identity.device_credential_history (credential_fingerprint)
    WHERE credential_status = 'ACTIVE';

CREATE TABLE IF NOT EXISTS identity.device_lifecycle_history (
    device_lifecycle_history_id uuid DEFAULT gen_random_uuid() NOT NULL,
    device_id uuid NOT NULL,
    action_code varchar(64) NOT NULL,
    previous_status identity.device_lifecycle_status_enum,
    resulting_status identity.device_lifecycle_status_enum NOT NULL,
    site_id uuid NOT NULL,
    site_group_id uuid,
    previous_credential_version bigint,
    resulting_credential_version bigint NOT NULL,
    actor_user_id uuid,
    actor_service_identity_id uuid,
    reason_code varchar(64),
    correlation_id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    CONSTRAINT pk_device_lifecycle_history PRIMARY KEY (device_lifecycle_history_id),
    CONSTRAINT fk_device_lifecycle_history__device FOREIGN KEY (device_id)
        REFERENCES identity.devices(device_id),
    CONSTRAINT fk_device_lifecycle_history__site FOREIGN KEY (site_id)
        REFERENCES sites.sites(site_id),
    CONSTRAINT fk_device_lifecycle_history__site_group FOREIGN KEY (site_group_id)
        REFERENCES sites.site_groups(site_group_id),
    CONSTRAINT fk_device_lifecycle_history__actor_user FOREIGN KEY (actor_user_id)
        REFERENCES identity.users(user_id),
    CONSTRAINT fk_device_lifecycle_history__actor_service FOREIGN KEY (actor_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id),
    CONSTRAINT ck_device_lifecycle_history__actor CHECK (
        (actor_user_id IS NOT NULL)::integer + (actor_service_identity_id IS NOT NULL)::integer = 1
    )
);

CREATE INDEX IF NOT EXISTS ix_device_lifecycle_history__device_time
    ON identity.device_lifecycle_history (device_id, occurred_at DESC);

-- Backfill existing Operator Console bindings. The existing binding remains the
-- runtime trust record; the new row supplies generic administration and lineage.
INSERT INTO identity.service_identities (
    service_identity_id, service_identity_code, service_identity_name,
    identity_type, identity_status, owning_service_name, credential_reference,
    credential_type, credential_expires_at, last_rotated_at, effective_from,
    created_at, created_by_user_id, created_by_service_identity_id,
    updated_at, updated_by_user_id, updated_by_service_identity_id)
SELECT COALESCE(binding.service_identity_id, gen_random_uuid()),
       left('device-' || lower(regexp_replace(binding.device_binding_code, '[^a-zA-Z0-9]+', '-', 'g')), 64),
       binding.device_name,
       'DEVICE',
       CASE WHEN binding.device_status = 'ACTIVE' THEN 'ACTIVE'::identity.service_identity_status_enum
            WHEN binding.device_status = 'SUSPENDED' THEN 'SUSPENDED'::identity.service_identity_status_enum
            ELSE 'REVOKED'::identity.service_identity_status_enum END,
       'SERVICE_PRINCIPAL',
       CASE WHEN binding.browser_key_thumbprint IS NULL THEN NULL
            ELSE 'device-browser-sha256:' || binding.browser_key_thumbprint END,
       CASE WHEN binding.browser_key_thumbprint IS NULL THEN 'NONE'::identity.service_credential_type_enum
            ELSE 'API_KEY_REFERENCE'::identity.service_credential_type_enum END,
       binding.mtls_certificate_expires_at,
       binding.updated_at,
       binding.created_at,
       binding.created_at,
       binding.created_by_user_id,
       binding.created_by_service_identity_id,
       binding.updated_at,
       binding.updated_by_user_id,
       binding.updated_by_service_identity_id
FROM operator_console.operator_device_bindings binding
WHERE binding.service_identity_id IS NULL
   OR NOT EXISTS (
       SELECT 1 FROM identity.service_identities service
       WHERE service.service_identity_id = binding.service_identity_id
   )
ON CONFLICT ON CONSTRAINT uq_service_identities__service_identity_code DO NOTHING;

UPDATE operator_console.operator_device_bindings binding
SET service_identity_id = service.service_identity_id,
    updated_at = now(),
    row_version = binding.row_version + 1
FROM identity.service_identities service
WHERE binding.service_identity_id IS NULL
  AND service.service_identity_code = left(
      'device-' || lower(regexp_replace(binding.device_binding_code, '[^a-zA-Z0-9]+', '-', 'g')), 64);

INSERT INTO identity.devices (
    device_id, device_reference, service_identity_id, device_name, device_type,
    terminal_identifier, lifecycle_status, credential_version,
    last_provisioning_status, enrolled_at, revoked_at, created_at, created_by_user_id,
    updated_at, updated_by_user_id)
SELECT binding.operator_device_binding_id,
       binding.device_binding_code,
       binding.service_identity_id,
       binding.device_name,
       'OPERATOR_CONSOLE',
       binding.device_binding_code,
       CASE WHEN binding.device_status = 'ACTIVE' THEN 'ACTIVE'::identity.device_lifecycle_status_enum
            WHEN binding.device_status = 'SUSPENDED' THEN 'SUSPENDED'::identity.device_lifecycle_status_enum
            ELSE 'REVOKED'::identity.device_lifecycle_status_enum END,
       CASE WHEN binding.browser_key_thumbprint IS NULL THEN 0 ELSE 1 END,
       CASE WHEN binding.browser_key_thumbprint IS NULL THEN NULL
            ELSE 'CONSUMED'::identity.device_provisioning_status_enum END,
       CASE WHEN binding.browser_key_thumbprint IS NULL THEN NULL ELSE binding.updated_at END,
       CASE WHEN binding.device_status IN ('ACTIVE', 'SUSPENDED') THEN NULL
            ELSE COALESCE(binding.revoked_at, binding.updated_at) END,
       binding.created_at,
       binding.created_by_user_id,
       binding.updated_at,
       binding.updated_by_user_id
FROM operator_console.operator_device_bindings binding
WHERE binding.service_identity_id IS NOT NULL
ON CONFLICT (device_id) DO NOTHING;

INSERT INTO sites.device_assignments (
    device_assignment_id, site_id, service_identity_id, assignment_type,
    assignment_status, assignment_reason_code, assigned_at,
    assigned_by_user_id, assigned_by_service_identity_id, created_at,
    created_by_user_id, created_by_service_identity_id, updated_at,
    updated_by_user_id, updated_by_service_identity_id)
SELECT gen_random_uuid(), binding.site_id, binding.service_identity_id,
       'SERVICE_PRINCIPAL',
       CASE WHEN binding.device_status = 'ACTIVE' THEN 'ACTIVE'::sites.device_assignment_status_enum
            ELSE 'SUSPENDED'::sites.device_assignment_status_enum END,
       'V13_DEVICE_ADMINISTRATION_BACKFILL', binding.created_at,
       binding.created_by_user_id, binding.created_by_service_identity_id,
       binding.created_at, binding.created_by_user_id,
       binding.created_by_service_identity_id, binding.updated_at,
       binding.updated_by_user_id, binding.updated_by_service_identity_id
FROM operator_console.operator_device_bindings binding
WHERE binding.service_identity_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM sites.device_assignments assignment
      WHERE assignment.service_identity_id = binding.service_identity_id
        AND assignment.assignment_type = 'SERVICE_PRINCIPAL'
        AND assignment.unassigned_at IS NULL
  );

INSERT INTO identity.device_credential_history (
    device_id, credential_version, credential_type, credential_status,
    credential_fingerprint, activated_at, expires_at, revoked_at, correlation_id)
SELECT binding.operator_device_binding_id, 1, 'API_KEY_REFERENCE',
       CASE WHEN binding.device_status = 'ACTIVE' THEN 'ACTIVE'::identity.device_credential_status_enum
            ELSE 'REVOKED'::identity.device_credential_status_enum END,
       binding.browser_key_thumbprint, binding.updated_at,
       binding.mtls_certificate_expires_at,
       CASE WHEN binding.device_status = 'ACTIVE' THEN NULL
            ELSE COALESCE(binding.revoked_at, binding.updated_at) END,
       COALESCE(binding.correlation_id, gen_random_uuid())
FROM operator_console.operator_device_bindings binding
WHERE binding.browser_key_thumbprint IS NOT NULL
ON CONFLICT (device_id, credential_version) DO NOTHING;

-- Backfill APT device principals that already own a PAYMENT_DEVICE assignment.
INSERT INTO identity.devices (
    device_reference, service_identity_id, device_name, device_type,
    terminal_identifier, lifecycle_status, credential_version,
    last_provisioning_status, enrolled_at, revoked_at, created_at, created_by_user_id,
    updated_at, updated_by_user_id)
SELECT service.service_identity_code,
       service.service_identity_id,
       service.service_identity_name,
       'ASSISTED_PAYMENT_TERMINAL',
       service.service_identity_code,
       CASE WHEN service.identity_status = 'ACTIVE' THEN 'ACTIVE'::identity.device_lifecycle_status_enum
            WHEN service.identity_status = 'SUSPENDED' THEN 'SUSPENDED'::identity.device_lifecycle_status_enum
            ELSE 'REVOKED'::identity.device_lifecycle_status_enum END,
       CASE WHEN service.credential_reference IS NULL THEN 0 ELSE 1 END,
       CASE WHEN service.credential_reference IS NULL THEN NULL
            ELSE 'CONSUMED'::identity.device_provisioning_status_enum END,
       CASE WHEN service.credential_reference IS NULL THEN NULL ELSE service.updated_at END,
       CASE WHEN service.identity_status IN ('ACTIVE', 'SUSPENDED') THEN NULL
            ELSE COALESCE(service.revoked_at, service.updated_at) END,
       service.created_at,
       service.created_by_user_id,
       service.updated_at,
       service.updated_by_user_id
FROM identity.service_identities service
WHERE service.identity_type = 'DEVICE'
  AND EXISTS (
      SELECT 1 FROM sites.device_assignments assignment
      WHERE assignment.service_identity_id = service.service_identity_id
        AND assignment.assignment_type = 'PAYMENT_DEVICE'
  )
ON CONFLICT (service_identity_id) DO NOTHING;

INSERT INTO identity.device_credential_history (
    device_id, credential_version, credential_type, credential_status,
    credential_fingerprint, activated_at, expires_at, revoked_at, correlation_id)
SELECT device.device_id, 1,
       COALESCE(service.credential_type, 'MTLS_CERTIFICATE_REFERENCE'),
       CASE WHEN device.lifecycle_status = 'ACTIVE' THEN 'ACTIVE'::identity.device_credential_status_enum
            ELSE 'REVOKED'::identity.device_credential_status_enum END,
       encode(digest(service.credential_reference, 'sha256'), 'hex'),
       COALESCE(service.last_rotated_at, service.updated_at),
       service.credential_expires_at,
       CASE WHEN device.lifecycle_status = 'ACTIVE' THEN NULL
            ELSE COALESCE(service.revoked_at, service.updated_at) END,
       gen_random_uuid()
FROM identity.devices device
JOIN identity.service_identities service
  ON service.service_identity_id = device.service_identity_id
WHERE device.device_type = 'ASSISTED_PAYMENT_TERMINAL'
  AND device.credential_version = 1
  AND service.credential_reference IS NOT NULL
ON CONFLICT (device_id, credential_version) DO NOTHING;

COMMIT;
