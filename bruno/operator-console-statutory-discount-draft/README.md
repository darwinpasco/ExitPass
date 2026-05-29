# Operator Console Statutory Discount Draft Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console statutory discount validation draft endpoint.

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/draft
```

This collection covers access-gated draft creation and duplicate replay behavior. It does not approve statutory discounts, apply a discount, mutate payable basis, upload evidence, create entitlement fingerprints, create payments, or mutate gate/session/payment state.

## Preconditions

- #180 statutory discount draft endpoint is present.
- #182 duplicate-safe draft behavior is present.
- Central PMS is running locally or in a reachable environment.
- The local PostgreSQL database is available.
- Operator Console access evaluation fixtures are seeded.
- The active parking session fixture exists:
  - `parkingSessionId_allowed = 77000000-0000-0000-0000-000000000090`
  - `ticketReference_allowed = MANUAL-SESSION-LOOKUP-001`

## Fixture Seed

Seed script:

```text
infra/db/fixtures/operator-console-access-evaluation/Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

Run against the local development database:

```powershell
docker exec -i exitpass-postgres psql -U exitpass -d exitpass_v12_dev -f /dev/stdin < infra\db\fixtures\operator-console-access-evaluation\Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

The script is idempotent. For repeatable statutory discount draft manual tests, it resets only `REQUESTED` and `PENDING_OPERATOR_REVIEW` Operator Assisted statutory discount validation drafts for the fixture session and `SENIOR_CITIZEN` or `PWD` entitlement types. It does not delete access evaluation evidence and does not create payment, gate, coupon, provider, reconciliation, evidence, fingerprint, or approved statutory discount records.

## Replay Behavior

Run `01 Valid Senior Citizen draft`, then run `11 Duplicate Senior Citizen draft replay` without resetting fixtures.

Expected replay behavior:

- HTTP `200`
- `accessAllowed = true`
- `accessDecision = ALLOWED`
- `accessPersisted = true`
- `draftAccepted = true`
- `draftPersisted = true`
- `validationStatus = REQUESTED`
- `entitlementType = SENIOR_CITIZEN`
- `reusedExistingDraft = true`
- no second active draft row is created

## Read-Only Database Verification

Verify the statutory discount validation draft returned as `draftId`:

```sql
SELECT
    statutory_discount_validation_id,
    parking_session_id,
    entitlement_type,
    validation_status,
    validation_channel,
    evidence_required,
    evidence_captured,
    correlation_id
FROM discounts.statutory_discount_validations
WHERE statutory_discount_validation_id = '<draftId-from-response>'::uuid;
```

Verify only one active Operator Assisted Senior Citizen draft exists for the fixture session after replay:

```sql
SELECT COUNT(*) AS active_senior_draft_count
FROM discounts.statutory_discount_validations
WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid
  AND entitlement_type = 'SENIOR_CITIZEN'
  AND validation_channel = 'OPERATOR_ASSISTED'
  AND validation_status IN ('REQUESTED', 'PENDING_OPERATOR_REVIEW');
```

Expected result is `1`.

Verify no evidence references were created for the draft:

```sql
SELECT COUNT(*) AS evidence_reference_count
FROM discounts.discount_evidence_references
WHERE statutory_discount_validation_id = '<draftId-from-response>'::uuid;
```

Expected result is `0`.

## Scope Boundary

This manual pack does not test:

- Operator Console UI
- statutory discount approval
- payable-basis mutation
- tariff mutation
- VAT removal
- payment creation or payment finality
- gate consume
- coupons
- evidence upload
- entitlement fingerprinting
- reconciliation
- AUB
- vendor PMS integration
