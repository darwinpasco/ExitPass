/* ExitPass v1.3 operational Shift Management MVP.
 *
 * Evolves the existing operator_console.operator_shifts authority so an
 * operational accountability shift can be opened without an HR schedule.
 * Existing imported shift rows and their immutable import history are retained.
 */

ALTER TABLE operator_console.operator_shifts
    ALTER COLUMN hr_provider_code DROP NOT NULL,
    ALTER COLUMN external_shift_id_hash DROP NOT NULL,
    ALTER COLUMN hr_identity_mapping_id DROP NOT NULL,
    ALTER COLUMN scheduled_start_at DROP NOT NULL,
    ALTER COLUMN scheduled_end_at DROP NOT NULL,
    ALTER COLUMN source_imported_at DROP NOT NULL,
    ALTER COLUMN import_status_code DROP NOT NULL,
    ALTER COLUMN source_system_code DROP NOT NULL;

ALTER TABLE operator_console.operator_shifts
    ADD COLUMN IF NOT EXISTS shift_reference varchar(64),
    ADD COLUMN IF NOT EXISTS shift_origin varchar(32) NOT NULL DEFAULT 'HR_IMPORT',
    ADD COLUMN IF NOT EXISTS operator_device_binding_id uuid,
    ADD COLUMN IF NOT EXISTS terminal_reference varchar(128),
    ADD COLUMN IF NOT EXISTS opened_at timestamptz,
    ADD COLUMN IF NOT EXISTS opened_by_user_id uuid,
    ADD COLUMN IF NOT EXISTS closed_at timestamptz,
    ADD COLUMN IF NOT EXISTS closed_by_user_id uuid,
    ADD COLUMN IF NOT EXISTS close_type varchar(32),
    ADD COLUMN IF NOT EXISTS close_reason text,
    ADD COLUMN IF NOT EXISTS cash_custody_status varchar(32) NOT NULL DEFAULT 'NONE',
    ADD COLUMN IF NOT EXISTS cash_custody_session_id uuid,
    ADD COLUMN IF NOT EXISTS opening_cash_minor_units bigint;

UPDATE operator_console.operator_shifts
SET shift_reference = COALESCE(shift_reference, 'SHIFT-' || upper(replace(operator_shift_id::text, '-', ''))),
    opened_at = COALESCE(opened_at, active_from, scheduled_start_at, created_at),
    closed_at = CASE
        WHEN operational_status IN ('ENDED', 'REVOKED', 'TAKEN_OVER', 'CANCELLED')
            THEN COALESCE(closed_at, active_to, revoked_at, updated_at)
        ELSE closed_at
    END
WHERE shift_reference IS NULL OR opened_at IS NULL;

ALTER TABLE operator_console.operator_shifts
    ALTER COLUMN shift_reference SET NOT NULL,
    ALTER COLUMN opened_at SET NOT NULL;

DO $$ BEGIN
    ALTER TABLE operator_console.operator_shifts
        ADD CONSTRAINT fk_operator_shifts__operator_device_binding_id
        FOREIGN KEY (operator_device_binding_id)
        REFERENCES operator_console.operator_device_bindings(operator_device_binding_id)
        DEFERRABLE INITIALLY IMMEDIATE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE operator_console.operator_shifts
        ADD CONSTRAINT fk_operator_shifts__opened_by_user_id
        FOREIGN KEY (opened_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE operator_console.operator_shifts
        ADD CONSTRAINT fk_operator_shifts__closed_by_user_id
        FOREIGN KEY (closed_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE operator_console.operator_shifts
        ADD CONSTRAINT ck_operator_shifts__shift_origin
        CHECK (shift_origin IN ('OPERATOR_STARTED', 'HR_IMPORT'));
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE operator_console.operator_shifts
        ADD CONSTRAINT ck_operator_shifts__cash_custody_status
        CHECK (cash_custody_status IN ('NONE', 'OPEN', 'CLOSED', 'UNAVAILABLE'));
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE operator_console.operator_shifts
        ADD CONSTRAINT ck_operator_shifts__close_fields
        CHECK (
            (operational_status = 'ACTIVE' AND closed_at IS NULL AND close_type IS NULL)
            OR operational_status <> 'ACTIVE'
        );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_operator_shifts__shift_reference
    ON operator_console.operator_shifts (shift_reference);

CREATE UNIQUE INDEX IF NOT EXISTS ux_operator_shifts__one_active_per_user
    ON operator_console.operator_shifts (operator_user_id)
    WHERE operational_status = 'ACTIVE' AND revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_operator_shifts__site_operational_history
    ON operator_console.operator_shifts (site_id, operational_status, opened_at DESC);

COMMENT ON TABLE operator_console.operator_shifts IS
    'Authoritative operational accountability shifts. HR-import metadata is optional and does not gate operator-started shifts.';

COMMENT ON COLUMN operator_console.operator_shifts.cash_custody_status IS
    'Observed custody state kept distinct from shift lifecycle; an OPEN value blocks all shift closure.';

ALTER TYPE operations.operator_action_type_enum ADD VALUE IF NOT EXISTS 'SHIFT_START';
ALTER TYPE operations.operator_action_type_enum ADD VALUE IF NOT EXISTS 'SHIFT_RESUME';
ALTER TYPE operations.operator_action_type_enum ADD VALUE IF NOT EXISTS 'SHIFT_CLOSE';
ALTER TYPE operations.operator_action_type_enum ADD VALUE IF NOT EXISTS 'SHIFT_EXCEPTION_CLOSE';
ALTER TYPE operations.operator_action_type_enum ADD VALUE IF NOT EXISTS 'SHIFT_ACTION_DENIED';

INSERT INTO identity.permissions (
    permission_id, permission_code, permission_name, permission_description,
    permission_domain, permission_action, permission_status, is_sensitive,
    requires_audit, created_by_service_identity_id, updated_by_service_identity_id)
SELECT permission.permission_id, permission.permission_code, permission.permission_name,
       permission.permission_description, 'shift-management', permission.permission_action,
       'ACTIVE', permission.is_sensitive, true,
       service.service_identity_id, service.service_identity_id
FROM (VALUES
    ('3fa581ab-3836-b399-a96a-a89664ecc0dc'::uuid, 'shift-management.view',
     'View Site operational shifts',
     'View current and recently closed operational shifts within current Site or Site Group scope.',
     'view', false),
    ('79804fea-9504-7d21-77d2-b71670735785'::uuid, 'shift-management.manage',
     'Manage Site operational shifts',
     'Perform audited supervisor exception close within current Site or Site Group scope.',
     'manage', true)
) AS permission(permission_id, permission_code, permission_name, permission_description, permission_action, is_sensitive)
CROSS JOIN identity.service_identities service
WHERE service.service_identity_code = 'seed.reference-data'
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    permission_description = EXCLUDED.permission_description,
    permission_domain = EXCLUDED.permission_domain,
    permission_action = EXCLUDED.permission_action,
    permission_status = 'ACTIVE',
    is_sensitive = EXCLUDED.is_sensitive,
    requires_audit = true,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = identity.permissions.row_version + 1
WHERE (identity.permissions.permission_name, identity.permissions.permission_description,
       identity.permissions.permission_domain, identity.permissions.permission_action,
       identity.permissions.permission_status::text, identity.permissions.is_sensitive,
       identity.permissions.requires_audit)
  IS DISTINCT FROM
      (EXCLUDED.permission_name, EXCLUDED.permission_description,
       EXCLUDED.permission_domain, EXCLUDED.permission_action,
       'ACTIVE', EXCLUDED.is_sensitive, true);

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT binding.role_permission_id, role.role_id, permission.permission_id, 'ACTIVE',
       'SHIFT_MANAGEMENT_MVP', service.service_identity_id, now(),
       service.service_identity_id, service.service_identity_id
FROM (VALUES
    ('7fa6477a-0c34-f925-8a50-68730ebb0833'::uuid, 'shift-management.view'),
    ('b3595927-469d-b3c1-d52f-28daf6a613a6'::uuid, 'shift-management.manage')
) AS binding(role_permission_id, permission_code)
JOIN identity.permissions permission ON permission.permission_code = binding.permission_code
CROSS JOIN identity.roles role
CROSS JOIN identity.service_identities service
WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
  AND role.role_status = 'ACTIVE'
  AND service.service_identity_code = 'seed.reference-data'
  AND NOT EXISTS (
      SELECT 1 FROM identity.role_permissions existing
      WHERE existing.role_id = role.role_id
        AND existing.permission_id = permission.permission_id
        AND existing.binding_status = 'ACTIVE')
ON CONFLICT (role_permission_id) DO UPDATE
SET role_id = EXCLUDED.role_id,
    permission_id = EXCLUDED.permission_id,
    binding_status = 'ACTIVE',
    binding_reason_code = EXCLUDED.binding_reason_code,
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = identity.role_permissions.row_version + 1;
