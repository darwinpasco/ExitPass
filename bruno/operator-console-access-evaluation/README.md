# Operator Console Access Evaluation Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console access evaluation endpoint.

Endpoint:

```http
POST /v1/ops/operator-console/access/evaluate
```

This collection covers the #174 evaluator and #175 persistence behavior. It is a manual smoke pack only; it does not seed data and does not change service runtime behavior.

## Preconditions

- #174 evaluator and #175 persistence/Swagger changes are present.
- Central PMS is running locally or in a reachable environment.
- The local PostgreSQL database is available.
- Operator Console access evaluation fixture data exists for the allow and denial contexts.

Local database environment used by backend tests:

```powershell
$env:EXITPASS_INTEGRATION_DB="Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me"
$env:ConnectionStrings__MainDatabase=$env:EXITPASS_INTEGRATION_DB
```

## Bruno Environment

Use `environments/local.bru` and replace every `REPLACE_WITH_*` placeholder before running the collection.

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
- `siteId_invalidAssignment`
- `siteGroupId_invalidAssignment`
- `operatorShiftId_inactive`

Stable Operator Console fixture IDs are not currently defined in repo-level seed data. The current integration tests create throwaway records at test runtime, so do not copy random local rows and treat them as permanent fixtures. Until a fixture seed slice exists, obtain IDs from a controlled local setup or create a small, explicit manual fixture data set outside this collection.

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

## Fixture Discovery Helpers

These are read-only helper queries. They do not create stable fixture data.

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

Recommended follow-up: add a dedicated Operator Console access evaluation fixture seed slice with stable IDs for the allow, inactive HR mapping, inactive device, untrusted device, invalid assignment, and inactive shift cases.

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
