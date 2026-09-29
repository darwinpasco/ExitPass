-- Forward-only reference-data correction for Operator Console fiscal authority.
-- This patch intentionally changes no schema and grants no Z-close authority.
BEGIN;

DO $preflight$
DECLARE
    v_permission_count integer;
BEGIN
    IF (SELECT count(*) FROM identity.roles
        WHERE role_code = 'OPERATIONS_SUPERVISOR' AND role_status = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one active OPERATIONS_SUPERVISOR role.';
    END IF;

    IF (SELECT count(*) FROM identity.roles
        WHERE role_code = 'SITE_OPERATOR' AND role_status = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one active SITE_OPERATOR role.';
    END IF;

    SELECT count(*) INTO v_permission_count
    FROM identity.permissions
    WHERE permission_status = 'ACTIVE'
      AND permission_code = ANY (ARRAY[
          'fiscal-issuance.status.read',
          'fiscal-reporting.ej.read',
          'fiscal-reporting.ej.export',
          'fiscal-reporting.x.read',
          'fiscal-reporting.x.generate',
          'fiscal-reporting.z.read'
      ]);
    IF v_permission_count <> 6 THEN
        RAISE EXCEPTION 'The complete Operator Console fiscal permission bundle is unavailable.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM identity.service_identities
        WHERE service_identity_code = 'seed.reference-data'
          AND identity_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'The canonical reference-data service identity is unavailable.';
    END IF;
END
$preflight$;

CREATE TEMP TABLE operator_console_supervisor_fiscal_permissions (
    permission_code text PRIMARY KEY
) ON COMMIT DROP;

INSERT INTO operator_console_supervisor_fiscal_permissions(permission_code) VALUES
('fiscal-issuance.status.read'),
('fiscal-reporting.ej.read'),
('fiscal-reporting.ej.export'),
('fiscal-reporting.x.read'),
('fiscal-reporting.x.generate'),
('fiscal-reporting.z.read');

-- Preserve one canonical binding row per permission and retire duplicates.
WITH ranked AS (
    SELECT rp.role_permission_id,
           row_number() OVER (
               PARTITION BY rp.role_id, rp.permission_id
               ORDER BY (rp.binding_status = 'ACTIVE') DESC, rp.created_at, rp.role_permission_id
           ) AS binding_rank
    FROM identity.role_permissions rp
    JOIN identity.roles r ON r.role_id = rp.role_id
    JOIN identity.permissions p ON p.permission_id = rp.permission_id
    JOIN operator_console_supervisor_fiscal_permissions target
      ON target.permission_code = p.permission_code
    WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
)
UPDATE identity.role_permissions rp
SET binding_status = 'RETIRED',
    effective_to = COALESCE(rp.effective_to, now()),
    revoked_at = COALESCE(rp.revoked_at, now()),
    revoked_by_service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    revocation_reason_code = 'DUPLICATE_FISCAL_AUTHORITY_BINDING',
    updated_at = now(),
    updated_by_service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    row_version = rp.row_version + 1
FROM ranked
WHERE rp.role_permission_id = ranked.role_permission_id
  AND ranked.binding_rank > 1
  AND rp.binding_status <> 'RETIRED';

WITH ranked AS (
    SELECT rp.role_permission_id,
           row_number() OVER (
               PARTITION BY rp.role_id, rp.permission_id
               ORDER BY (rp.binding_status = 'ACTIVE') DESC, rp.created_at, rp.role_permission_id
           ) AS binding_rank
    FROM identity.role_permissions rp
    JOIN identity.roles r ON r.role_id = rp.role_id
    JOIN identity.permissions p ON p.permission_id = rp.permission_id
    JOIN operator_console_supervisor_fiscal_permissions target
      ON target.permission_code = p.permission_code
    WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
)
UPDATE identity.role_permissions rp
SET binding_status = 'ACTIVE',
    binding_reason_code = 'OPERATOR_CONSOLE_OPERATIONS_SUPERVISOR_FISCAL_AUTHORITY',
    assigned_by_service_identity_id = COALESCE(
        rp.assigned_by_service_identity_id,
        '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
    effective_from = LEAST(rp.effective_from, now()),
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    row_version = CASE
        WHEN rp.binding_status = 'ACTIVE'
         AND rp.binding_reason_code = 'OPERATOR_CONSOLE_OPERATIONS_SUPERVISOR_FISCAL_AUTHORITY'
         AND rp.effective_to IS NULL
         AND rp.revoked_at IS NULL
        THEN rp.row_version
        ELSE rp.row_version + 1
    END
FROM ranked
WHERE rp.role_permission_id = ranked.role_permission_id
  AND ranked.binding_rank = 1
  AND (
      rp.binding_status <> 'ACTIVE'
      OR rp.binding_reason_code IS DISTINCT FROM 'OPERATOR_CONSOLE_OPERATIONS_SUPERVISOR_FISCAL_AUTHORITY'
      OR rp.effective_to IS NOT NULL
      OR rp.revoked_at IS NOT NULL
      OR rp.revoked_by_user_id IS NOT NULL
      OR rp.revoked_by_service_identity_id IS NOT NULL
      OR rp.revocation_reason_code IS NOT NULL
  );

INSERT INTO identity.role_permissions (
    role_permission_id,
    role_id,
    permission_id,
    binding_status,
    binding_reason_code,
    assigned_by_service_identity_id,
    effective_from,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT gen_random_uuid(),
       r.role_id,
       p.permission_id,
       'ACTIVE',
       'OPERATOR_CONSOLE_OPERATIONS_SUPERVISOR_FISCAL_AUTHORITY',
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
       now(),
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p
  ON p.permission_code IN (
      SELECT permission_code FROM operator_console_supervisor_fiscal_permissions
  )
WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
  AND NOT EXISTS (
      SELECT 1
      FROM identity.role_permissions existing
      WHERE existing.role_id = r.role_id
        AND existing.permission_id = p.permission_id
  );

-- Site Operators retain ticket/statutory operation, but no fiscal reporting
-- or fiscal-status authority.
UPDATE identity.role_permissions rp
SET binding_status = 'RETIRED',
    effective_to = COALESCE(rp.effective_to, now()),
    revoked_at = COALESCE(rp.revoked_at, now()),
    revoked_by_service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    revocation_reason_code = 'OPERATIONS_SUPERVISOR_FISCAL_AUTHORITY_ONLY',
    updated_at = now(),
    updated_by_service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    row_version = rp.row_version + 1
FROM identity.roles r
JOIN identity.permissions p ON TRUE
JOIN operator_console_supervisor_fiscal_permissions target
  ON target.permission_code = p.permission_code
WHERE rp.role_id = r.role_id
  AND rp.permission_id = p.permission_id
  AND r.role_code = 'SITE_OPERATOR'
  AND rp.binding_status = 'ACTIVE';

-- Z generation remains reserved for a separately governed closer role.
UPDATE identity.role_permissions rp
SET binding_status = 'RETIRED',
    effective_to = COALESCE(rp.effective_to, now()),
    revoked_at = COALESCE(rp.revoked_at, now()),
    revoked_by_service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    revocation_reason_code = 'Z_CLOSE_SEPARATION_OF_DUTIES',
    updated_at = now(),
    updated_by_service_identity_id = '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    row_version = rp.row_version + 1
FROM identity.roles r,
     identity.permissions p
WHERE rp.role_id = r.role_id
  AND rp.permission_id = p.permission_id
  AND r.role_code IN ('OPERATIONS_SUPERVISOR', 'SITE_OPERATOR')
  AND p.permission_code = 'fiscal-reporting.z.generate'
  AND rp.binding_status = 'ACTIVE';

COMMIT;
