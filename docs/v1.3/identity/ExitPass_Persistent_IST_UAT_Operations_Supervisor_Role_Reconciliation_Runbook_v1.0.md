# ExitPass Persistent IST UAT Operations Supervisor Role Reconciliation Runbook v1.0

## Purpose

This runbook repairs one known identity drift in the local persistent IST database. It is not a migration, application compatibility feature, or general-purpose identity rewrite.

Target database and identity:

- Database: `exitpass_ist`
- User ID: `77000000-0000-0000-0000-000000000012`
- Username: `uat-operations-supervisor`
- Canonical user type: `OPERATIONS_USER`
- Canonical role: `OPERATIONS_SUPERVISOR`

## Cause and boundary

The historical persistent fixture retained `SITE_OPERATOR` user type and an active `UAT_OPERATIONS_SUPERVISOR` assignment after the v1.3 source-controlled UAT identity definition moved this principal to `OPERATIONS_USER` and `OPERATIONS_SUPERVISOR`. Human authentication correctly rejected the unknown role with `APPLICATION_AUDIENCE_POLICY_UNKNOWN`.

The resolution is a narrow environment reconciliation. No alias is added, application fail-closed behavior remains unchanged, and `ApprovedIdentityRoleCatalog` remains limited to its eight canonical assignable human roles. The historical stale assignment is retained as revoked audit history.

The broad Management Platform UAT seed remains prohibited from `exitpass_ist` and is not used by this procedure.

## Scope preservation

Persistent history proves that this exact principal was assigned both PITX Level 3 Site and PITX Site Group grants. The canonical `OPERATIONS_SUPERVISOR` policy permits `SITE` scope and does not permit `SITE_GROUP` scope. The reconciliation therefore establishes only:

- Scope type: `SITE`
- Site: `2d1dcdf8-f563-537c-8542-0bde7cc9da97` (`PITX-LEVEL-3`)
- Parent Site Group validation: `a6dbadf6-68b5-5bed-a7e0-a75faee70841` (`PITX`)

The historical Site Group grant remains revoked and is not copied. Any missing, duplicate, unrelated, or structurally invalid scope evidence causes the procedure to fail closed.

## Safety controls

The tool:

- accepts only database `exitpass_ist`;
- targets only the exact UUID and username above;
- validates the canonical role, provenance, assignability, direct-add eligibility, and `OPERATIONS_USER` compatibility;
- rejects unexpected active roles and ambiguous canonical assignments;
- validates the authoritative PITX Site-to-Site-Group relationship;
- creates a custom-format PostgreSQL backup before `-Apply`;
- applies all database changes in one transaction under an advisory lock;
- increments `authorization_epoch` exactly once when effective authority changes;
- revokes active human sessions only when authority changes;
- leaves `credential_version`, password material, MFA, status, lockout, username, and email unchanged;
- performs no mutation during an idempotent replay.

## Execution

Read-only preflight:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Repair-PersistentIstUatOperationsSupervisorRole.ps1 -PreflightOnly
```

Apply after reviewing preflight output:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Repair-PersistentIstUatOperationsSupervisorRole.ps1 -Apply
```

Repeat the preflight after application. Repeating `-Apply` is safe: a reconciled identity produces no authorization-epoch increment, duplicate assignment, duplicate scope grant, or session revocation.

## Expected result

- User type: `OPERATIONS_USER`
- Effective role: exactly one `OPERATIONS_SUPERVISOR`
- Effective scope: PITX Level 3 `SITE` only
- `UAT_OPERATIONS_SUPERVISOR`: retained but revoked for this user
- Historical stale scope grants: retained as revoked records
- `authorization_epoch`: incremented once for the effective authority change
- `credential_version`: unchanged
- Pre-repair sessions: revoked or rejected by epoch mismatch
- Fresh login: required

If credentials are no longer usable, credential remediation is a separate task. This reconciliation must not reset a password or MFA authenticator.
