# Operator Console Session Lookup Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console read-only session lookup endpoint.

Endpoint:

```http
POST /v1/ops/operator-console/sessions/lookup
```

This collection covers the #178 access-gated session lookup behavior. It does not change service runtime behavior, start statutory discount validation, create payments, or mutate gate/session/payment state.

## Preconditions

- #178 session lookup endpoint is present.
- Central PMS is running locally or in a reachable environment.
- The local PostgreSQL database is available.
- Operator Console access evaluation fixtures are seeded.
- The bundled fixture script has been rerun after the #179 fixture update so `core.parking_sessions` contains the session lookup fixture.

## Fixture Seed

Seed script:

```text
infra/db/fixtures/operator-console-access-evaluation/Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

Run against the local development database:

```powershell
docker exec -i exitpass-postgres psql -U exitpass -d exitpass_v12_dev -f /dev/stdin < infra\db\fixtures\operator-console-access-evaluation\Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

The script is idempotent. For session lookup it adds only the minimum local fixture records:

- `identity.service_identities`: fixture service identity
- `integration.vendor_systems`: fixture Vendor PMS reference
- `core.parking_sessions`: active parking session for lookup

It does not create payment attempts, payment confirmations, exit authorizations, provider outcomes, gate consume records, coupon applications, statutory discount validations, reconciliation records, settlement records, or AUB records.

## Bruno Environment

Use `environments/local.bru`.

Required variables:

- `centralPmsBaseUrl`
- `userId_allowed`
- `operatorDeviceBindingId_allowed`
- `siteId_allowed`
- `siteGroupId_allowed`
- `operatorShiftId_allowed`
- `parkingSessionId_allowed`
- `ticketReference_allowed`
- `userId_inactiveHrMapping`
- `operatorShiftId_inactiveHrMapping`
- `operatorShiftId_inactive`
- `parkingSessionId_notFound`
- `ticketReference_notFound`

Stable fixture IDs:

| Variable | Value | Purpose |
| --- | --- | --- |
| `siteGroupId_allowed` | `77000000-0000-0000-0000-000000000001` | Manual Operator Console site group |
| `siteId_allowed` | `77000000-0000-0000-0000-000000000002` | Manual Operator Console site |
| fixture service identity | `77000000-0000-0000-0000-000000000003` | Local fixture creator/updater reference |
| fixture vendor system | `77000000-0000-0000-0000-000000000004` | Local Vendor PMS reference |
| `userId_allowed` | `77000000-0000-0000-0000-000000000010` | Active operator with active HR mapping |
| `userId_inactiveHrMapping` | `77000000-0000-0000-0000-000000000011` | Active operator with suspended HR mapping |
| `operatorDeviceBindingId_allowed` | `77000000-0000-0000-0000-000000000030` | Active trusted device |
| `operatorShiftId_allowed` | `77000000-0000-0000-0000-000000000050` | Active shift for allowed operator |
| `operatorShiftId_inactiveHrMapping` | `77000000-0000-0000-0000-000000000051` | Active shift tied to inactive HR mapping user |
| `operatorShiftId_inactive` | `77000000-0000-0000-0000-000000000052` | Ended shift for denial experiments |
| `parkingSessionId_allowed` | `77000000-0000-0000-0000-000000000090` | Active parking session lookup fixture |
| `ticketReference_allowed` | `MANUAL-SESSION-LOOKUP-001` | Ticket reference for the active session fixture |
| `parkingSessionId_notFound` | `77000000-0000-0000-0000-000000000099` | Stable nonexistent session ID |
| `ticketReference_notFound` | `MANUAL-SESSION-LOOKUP-NOT-FOUND` | Stable nonexistent ticket reference |

Correlation and idempotency guidance:

- Bruno requests use `{{$guid}}` for unique `correlationId` values.
- `idempotencyKey` values include the test case name plus `{{$guid}}`.
- The endpoint persists access evaluation evidence on normal access evaluation paths, even when the session is not found.

## Test Cases

| Case | Expected status | Expected access | Expected session behavior |
| --- | --- | --- | --- |
| `01 Allowed session found by parking session ID` | `200` | `accessAllowed = true`, `accessDecision = ALLOWED`, `accessPersisted = true` | `sessionFound = true`, `sessionEligible = true`, `parkingSessionId` matches fixture |
| `02 Allowed session found by ticket reference` | `200` | `accessAllowed = true`, `accessDecision = ALLOWED`, `accessPersisted = true` | `sessionFound = true`, `ticketReference` matches fixture |
| `03 Access denied prevents session lookup` | `200` | `accessAllowed = false`, `accessDecision = DENIED`, `accessPersisted = true`, reason includes `HR_IDENTITY_MAPPING_INACTIVE` | `sessionFound = false`, session details are null |
| `04 Missing lookup identifier` | `400` | access evaluation is not attempted | `INVALID_OPERATOR_CONSOLE_SESSION_LOOKUP_REQUEST` |
| `05 Unsupported lookup mode` | `400` | access evaluation is not attempted | `INVALID_OPERATOR_CONSOLE_SESSION_LOOKUP_REQUEST` |
| `06 Allowed session not found` | `404` | `accessAllowed = true`, `accessDecision = ALLOWED`, `accessPersisted = true` | `sessionFound = false`, `ineligibilityReason = SESSION_NOT_FOUND` |

## Read-Only Database Verification

Verify the access evaluation row returned as `accessEvaluationId`:

```sql
SELECT
    operator_access_evaluation_id,
    correlation_id,
    evaluation_status
FROM operator_console.operator_access_evaluations
WHERE operator_access_evaluation_id = '<accessEvaluationId-from-response>'::uuid;
```

Verify denial reasons for denied access:

```sql
SELECT
    operator_access_evaluation_id,
    reason_code
FROM operator_console.operator_access_evaluation_reasons
WHERE operator_access_evaluation_id = '<accessEvaluationId-from-response>'::uuid
ORDER BY display_order, operator_access_evaluation_reason_id;
```

Verify the session fixture by ID:

```sql
SELECT
    parking_session_id,
    site_group_id,
    site_id,
    vendor_session_ref,
    ticket_number_masked,
    plate_number_masked,
    session_status
FROM core.parking_sessions
WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;
```

Verify the session fixture by ticket reference:

```sql
SELECT
    parking_session_id,
    vendor_session_ref,
    ticket_number_masked,
    session_status
FROM core.parking_sessions
WHERE vendor_session_ref = 'MANUAL-SESSION-LOOKUP-001'
   OR ticket_number_masked = 'MANUAL-SESSION-LOOKUP-001';
```

Correlation-based access evaluation lookup:

```sql
SELECT
    operator_access_evaluation_id,
    correlation_id,
    evaluation_status
FROM operator_console.operator_access_evaluations
WHERE correlation_id = '<correlationId-from-request>'::uuid
ORDER BY evaluated_at DESC;
```

Non-payment boundary checks for the fixture session:

```sql
SELECT COUNT(*) AS payment_attempt_count
FROM core.payment_attempts
WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;

SELECT COUNT(*) AS payment_confirmation_count
FROM core.payment_confirmations pc
JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;

SELECT COUNT(*) AS exit_authorization_count
FROM core.exit_authorizations
WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;

SELECT COUNT(*) AS provider_outcome_count
FROM payments.provider_outcomes po
JOIN core.payment_confirmations pc ON pc.provider_outcome_id = po.provider_outcome_id
JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;

SELECT COUNT(*) AS statutory_discount_validation_count
FROM discounts.statutory_discount_validations
WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;

SELECT COUNT(*) AS coupon_application_count
FROM coupons.coupon_applications
WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;

SELECT COUNT(*) AS gate_authorization_consumption_count
FROM gates.gate_authorization_consumptions gac
JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
WHERE ea.parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid;

SELECT COUNT(*) AS reconciliation_item_count
FROM reconciliation.reconciliation_items
WHERE (
        target_entity_type = 'PARKING_SESSION'
    AND target_entity_id = '77000000-0000-0000-0000-000000000090'::uuid
)
   OR payment_attempt_id IN (
        SELECT payment_attempt_id
        FROM core.payment_attempts
        WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid
   );
```

Expected result for each non-payment boundary count is `0`.

## Cleanup and Reset

The fixture script is safe to rerun. For a full reset, rebuild or reset the local development database using the repo's normal database reset workflow, then rerun the fixture seed script. The fixture script does not delete access evaluation evidence rows generated by manual API calls.

## Scope Boundary

This manual pack does not test:

- Operator Console UI
- statutory discount validation
- payment creation or payment finality
- gate consume
- coupons
- reconciliation
- AUB
- vendor PMS integration

It only targets Central PMS Operator Console session lookup API behavior, access gating, and persisted Operator Console access evaluation evidence.
