# Operator Console Access Evaluation Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console access evaluation endpoint.

Endpoint:

```http
POST /v1/ops/operator-console/access/evaluate
```

This collection covers the #174 evaluator and #175 persistence behavior. It is a manual smoke pack only; the companion SQL script seeds local fixture context and neither asset changes service runtime behavior.

## Preconditions

- #174 evaluator and #175 persistence/Swagger changes are present.
- Central PMS is running locally or in a reachable environment.
- The local PostgreSQL database is available.
- Operator Console access evaluation fixture data exists for the allow and denial contexts.

## Fixture Seed

Seed script:

```text
infra/db/fixtures/operator-console-access-evaluation/Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

Run against the local development database:

```powershell
docker exec -i exitpass-postgres psql -U exitpass -d exitpass_v12_dev -f /dev/stdin < infra\db\fixtures\operator-console-access-evaluation\Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

The script is idempotent. It upserts only records with the `MANUAL_TEST_OPERATOR_ACCESS_*` names/codes and stable `77000000-...` UUIDs. It does not create payment attempts, payment confirmations, exit authorizations, provider outcomes, gate consume records, coupon applications, statutory discount validations, reconciliation records, settlement records, or AUB records.

Local database environment used by backend tests:

```powershell
$env:EXITPASS_INTEGRATION_DB="Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me"
$env:ConnectionStrings__MainDatabase=$env:EXITPASS_INTEGRATION_DB
```

## Bruno Environment

Use `environments/local.bru`. The local environment is prefilled with the stable fixture IDs from the seed script.

Required variables:

- `centralPmsBaseUrl`
- `userId_allowed`
- `operatorDeviceBindingId_allowed`
- `siteId_allowed`
- `siteGroupId_allowed`
- `operatorShiftId_allowed`
- `parkingSessionId_allowed`
- `userId_missingHrMapping`
- `userId_inactiveHrMapping`
- `operatorDeviceBindingId_missing`
- `operatorDeviceBindingId_inactive`
- `operatorDeviceBindingId_untrusted`
- `operatorDeviceBindingId_invalidAssignment`
- `siteId_invalidAssignment`
- `siteGroupId_invalidAssignment`
- `operatorShiftId_inactiveHrMapping`
- `operatorShiftId_inactive`

Stable fixture IDs:

| Variable | UUID | Purpose |
| --- | --- | --- |
| `siteGroupId_allowed` | `77000000-0000-0000-0000-000000000001` | Manual Operator Console site group |
| `siteId_allowed` | `77000000-0000-0000-0000-000000000002` | Manual Operator Console site |
| `userId_allowed` | `77000000-0000-0000-0000-000000000010` | Active operator with active HR mapping |
| `userId_inactiveHrMapping` | `77000000-0000-0000-0000-000000000011` | Active operator with suspended HR mapping |
| `userId_missingHrMapping` | `77000000-0000-0000-0000-000000000012` | Reserved non-seeded ID for missing HR mapping experiments |
| `operatorDeviceBindingId_allowed` | `77000000-0000-0000-0000-000000000030` | Active trusted device |
| `operatorDeviceBindingId_inactive` | `77000000-0000-0000-0000-000000000031` | Suspended trusted device |
| `operatorDeviceBindingId_untrusted` | `77000000-0000-0000-0000-000000000032` | Active unverified device |
| `operatorDeviceBindingId_invalidAssignment` | `77000000-0000-0000-0000-000000000033` | Active trusted device with suspended assignment |
| `operatorDeviceBindingId_missing` | `77000000-0000-0000-0000-000000000034` | Reserved non-seeded ID for missing device binding experiments |
| `operatorShiftId_allowed` | `77000000-0000-0000-0000-000000000050` | Active shift for the allowed operator |
| `operatorShiftId_inactiveHrMapping` | `77000000-0000-0000-0000-000000000051` | Active shift tied to suspended HR mapping user |
| `operatorShiftId_inactive` | `77000000-0000-0000-0000-000000000052` | Ended shift for no-active-shift case |
| `parkingSessionId_allowed` | `77000000-0000-0000-0000-000000000090` | Active parking session fixture for session lookup; used as a target entity ID by access evaluation |

Correlation and idempotency guidance:

- Bruno requests use `{{$guid}}` for unique `correlationId` values.
- `idempotencyKey` values include the test case name plus `{{$guid}}`.
- Reusing the same `correlationId` is useful for database verification, but avoid reusing IDs when smoke testing repeated persistence.

## Request Shape

Every request includes:

- `userId`
- `operatorDeviceBindingId`
- `siteId`
- `siteGroupId`
- `operatorShiftId`
- `workflowCode`
- `controlledActionCode`
- `parkingSessionId`
- `evidenceAccessIntent`
- `idempotencyKey`
- `correlationId`

Supported MVP workflow/action values:

- `STATUTORY_DISCOUNT_VALIDATION`
- `START_WORKFLOW`
- `SUBMIT_DECISION`
- `CAPTURE_EVIDENCE`
- `VIEW_EVIDENCE`

## Test Cases

| Case | Expected status | Expected decision | Persistence | Expected denial reason |
| --- | --- | --- | --- | --- |
| `01 Allowed access evaluation` | `200` | `ALLOWED`, `allowed = true` | `persisted = true`, non-empty `evaluationId` | none |
| `02 Missing or inactive HR mapping` | `200` | `DENIED`, `allowed = false` | `persisted = true`, non-empty `evaluationId` | `HR_IDENTITY_MAPPING_NOT_FOUND` or `HR_IDENTITY_MAPPING_INACTIVE` |
| `03 Missing or inactive device binding` | `200` | `DENIED`, `allowed = false` | `persisted = true`, non-empty `evaluationId` | `DEVICE_BINDING_NOT_FOUND` or `DEVICE_BINDING_INACTIVE` |
| `04 Untrusted device` | `200` | `DENIED`, `allowed = false` | `persisted = true`, non-empty `evaluationId` | `DEVICE_NOT_TRUSTED` |
| `05 Invalid device site assignment` | `200` | `DENIED`, `allowed = false` | `persisted = true`, non-empty `evaluationId` | `DEVICE_SITE_ASSIGNMENT_NOT_FOUND` or `DEVICE_SITE_ASSIGNMENT_INVALID` |
| `06 No active shift` | `200` | `DENIED`, `allowed = false` | `persisted = true`, non-empty `evaluationId` | `NO_ACTIVE_SHIFT` |
| `07 Unsupported workflow` | `200` | `DENIED`, `allowed = false` | `persisted = true`, non-empty `evaluationId` | `WORKFLOW_NOT_SUPPORTED` |
| `08 Unsupported controlled action` | `200` | `DENIED`, `allowed = false` | `persisted = true`, non-empty `evaluationId` | `ACTION_NOT_SUPPORTED` |
| `09 Evidence view action` | `200` | deterministic `ALLOWED` or `DENIED` based on fixture context | `persisted = true`, non-empty `evaluationId` | depends on fixture context |

With the bundled local fixture seed, case 09 uses the same valid context as the allowed case and is expected to return `ALLOWED` with no denial reasons.

## Fixture-to-Request Map

| Request | Fixture variables | Expected result |
| --- | --- | --- |
| `01 Allowed access evaluation` | `userId_allowed`, `operatorDeviceBindingId_allowed`, `operatorShiftId_allowed` | `ALLOWED`, no denial reasons |
| `02 Missing or inactive HR mapping` | `userId_inactiveHrMapping`, `operatorShiftId_inactiveHrMapping`, allowed device/site | `DENIED`, `HR_IDENTITY_MAPPING_INACTIVE` |
| `03 Missing or inactive device binding` | allowed user/shift/site, `operatorDeviceBindingId_inactive` | `DENIED`, `DEVICE_BINDING_INACTIVE` |
| `04 Untrusted device` | allowed user/shift/site, `operatorDeviceBindingId_untrusted` | `DENIED`, `DEVICE_NOT_TRUSTED` |
| `05 Invalid device site assignment` | allowed user/shift/site, `operatorDeviceBindingId_invalidAssignment` | `DENIED`, `DEVICE_SITE_ASSIGNMENT_INVALID` |
| `06 No active shift` | allowed user/device/site, `operatorShiftId_inactive` | `DENIED`, `NO_ACTIVE_SHIFT` |
| `07 Unsupported workflow` | allowed context with `UNSUPPORTED_WORKFLOW` | `DENIED`, `WORKFLOW_NOT_SUPPORTED` |
| `08 Unsupported controlled action` | allowed context with `UNSUPPORTED_ACTION` | `DENIED`, `ACTION_NOT_SUPPORTED` |
| `09 Evidence view action` | allowed context with `VIEW_EVIDENCE` and `SUPERVISOR_REVIEW` | `ALLOWED`, no denial reasons |

The evidence view case uses:

```json
{
  "workflowCode": "STATUTORY_DISCOUNT_VALIDATION",
  "controlledActionCode": "VIEW_EVIDENCE",
  "evidenceAccessIntent": "SUPERVISOR_REVIEW"
}
```

## Read-Only Database Verification

After any request returns `evaluationId`, verify the persisted evaluation row:

```sql
SELECT
    operator_access_evaluation_id,
    correlation_id,
    evaluation_status
FROM operator_console.operator_access_evaluations
WHERE operator_access_evaluation_id = '<evaluationId-from-response>'::uuid;
```

Allowed cases should have zero denial reasons:

```sql
SELECT
    operator_access_evaluation_id,
    reason_code
FROM operator_console.operator_access_evaluation_reasons
WHERE operator_access_evaluation_id = '<evaluationId-from-response>'::uuid;
```

Denied cases should include the expected reason codes:

```sql
SELECT
    operator_access_evaluation_id,
    reason_code
FROM operator_console.operator_access_evaluation_reasons
WHERE operator_access_evaluation_id = '<evaluationId-from-response>'::uuid
ORDER BY display_order, operator_access_evaluation_reason_id;
```

Correlation-based lookup:

```sql
SELECT
    operator_access_evaluation_id,
    correlation_id,
    evaluation_status
FROM operator_console.operator_access_evaluations
WHERE correlation_id = '<correlationId-from-request>'::uuid
ORDER BY evaluated_at DESC;
```

## Fixture Verification Helpers

These are read-only helper queries.

Verify seeded fixture context:

```sql
SELECT user_id, username, user_status
FROM identity.users
WHERE user_id IN (
    '77000000-0000-0000-0000-000000000010'::uuid,
    '77000000-0000-0000-0000-000000000011'::uuid
)
ORDER BY user_id;

SELECT operator_device_binding_id, device_binding_code, device_status, trust_level
FROM operator_console.operator_device_bindings
WHERE operator_device_binding_id IN (
    '77000000-0000-0000-0000-000000000030'::uuid,
    '77000000-0000-0000-0000-000000000031'::uuid,
    '77000000-0000-0000-0000-000000000032'::uuid,
    '77000000-0000-0000-0000-000000000033'::uuid
)
ORDER BY operator_device_binding_id;

SELECT operator_shift_id, operator_user_id, operational_status, active_from, active_to
FROM operator_console.operator_shifts
WHERE operator_shift_id IN (
    '77000000-0000-0000-0000-000000000050'::uuid,
    '77000000-0000-0000-0000-000000000051'::uuid,
    '77000000-0000-0000-0000-000000000052'::uuid
)
ORDER BY operator_shift_id;
```

General discovery helpers:

Find active site rows:

```sql
SELECT
    sg.site_group_id,
    s.site_id,
    sg.site_group_code,
    s.site_code
FROM sites.sites s
JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
WHERE s.site_status = 'ACTIVE'
  AND sg.site_group_status = 'ACTIVE'
ORDER BY s.site_code;
```

Find current active Operator Console contexts:

```sql
SELECT
    u.user_id,
    him.hr_identity_mapping_id,
    odb.operator_device_binding_id,
    oda.site_group_id,
    oda.site_id,
    os.operator_shift_id
FROM identity.users u
JOIN operator_console.hr_identity_mappings him
    ON him.user_id = u.user_id
JOIN operator_console.operator_shifts os
    ON os.operator_user_id = u.user_id
JOIN operator_console.operator_device_bindings odb
    ON odb.site_id = os.site_id
JOIN operator_console.operator_device_assignment_history oda
    ON oda.operator_device_binding_id = odb.operator_device_binding_id
   AND oda.site_id = os.site_id
WHERE u.user_status = 'ACTIVE'
  AND him.mapping_status = 'ACTIVE'
  AND odb.device_status = 'ACTIVE'
  AND odb.trust_level <> 'UNVERIFIED'
  AND oda.assignment_status_code = 'ACTIVE'
  AND os.operational_status = 'ACTIVE'
  AND (him.effective_to IS NULL OR him.effective_to > now())
  AND (oda.effective_to IS NULL OR oda.effective_to > now())
  AND (os.active_to IS NULL OR os.active_to > now())
ORDER BY u.user_id;
```

## Cleanup and Reset

The fixture script is safe to rerun. For a full reset, rebuild or reset the local development database using the repo's normal database reset workflow, then rerun the fixture seed script. The fixture does not delete access evaluation evidence rows because those rows are produced by manual API calls and may be useful for verification.

## Scope Boundary

This manual pack does not test:

- Operator Console UI
- statutory discount application
- payments
- gate consume
- coupons
- reconciliation
- AUB
- vendor PMS integration

It only targets Central PMS access evaluation API behavior and persisted Operator Console evaluation evidence.
