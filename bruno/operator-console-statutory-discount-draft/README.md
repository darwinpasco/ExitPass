# Operator Console Statutory Discount Draft Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console statutory discount validation draft endpoint.

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/draft
```

This collection covers access-gated draft creation, duplicate replay behavior, and metadata-only evidence reference creation. It does not approve statutory discounts, apply a discount, mutate payable basis, upload evidence, store raw evidence, create entitlement fingerprints, create payments, or mutate gate/session/payment state.

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

The script is idempotent. For repeatable statutory discount draft manual tests, it resets only `REQUESTED` and `PENDING_OPERATOR_REVIEW` Operator Assisted statutory discount validation drafts for the fixture session and `SENIOR_CITIZEN` or `PWD` entitlement types. It does not delete access evaluation evidence and does not create payment, gate, coupon, provider, reconciliation, fingerprint, or approved statutory discount records.

## Evidence Metadata Behavior

Run `12 Valid Senior Citizen draft with evidence metadata`, then run `13 Duplicate evidence metadata replay` without resetting fixtures.

Expected first-call behavior:

- HTTP `200`
- `accessAllowed = true`
- `accessDecision = ALLOWED`
- `accessPersisted = true`
- `draftAccepted = true`
- `draftPersisted = true`
- `validationStatus = REQUESTED`
- `entitlementType = SENIOR_CITIZEN`
- `evidenceRequired = true`
- `evidenceReferenceCreated = true` when no equivalent metadata row already exists
- `evidenceReferenceId` is not empty
- no image upload occurs
- no raw ID, raw evidence payload, storage reference, or evidence hash is stored

Expected replay behavior:

- HTTP `200`
- `reusedExistingDraft = true`
- `evidenceRequired = true`
- `evidenceReferenceCreated = false` when the metadata row already exists
- `evidenceReferenceId` returns the existing metadata row
- no second evidence metadata row is created

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

Verify no evidence references were created for request `01` or `11` when `evidenceCaptureRequested = false`:

```sql
SELECT COUNT(*) AS evidence_reference_count
FROM discounts.discount_evidence_references
WHERE statutory_discount_validation_id = '<draftId-from-response>'::uuid;
```

Expected result is `0`.

Verify metadata-only evidence reference behavior for request `12` or `13`:

```sql
SELECT
    discount_evidence_reference_id,
    statutory_discount_validation_id,
    evidence_type,
    evidence_storage_type,
    evidence_storage_ref,
    evidence_hash,
    evidence_capture_status,
    access_classification,
    redaction_status,
    retention_policy_code,
    correlation_id
FROM discounts.discount_evidence_references
WHERE statutory_discount_validation_id = '<draftId-from-response>'::uuid;
```

Expected metadata values for Senior Citizen evidence:

- `evidence_type = SENIOR_CITIZEN_ID`
- `evidence_storage_type = EXTERNAL_REFERENCE`
- `evidence_storage_ref IS NULL`
- `evidence_hash IS NULL`
- `evidence_capture_status = REFERENCED`
- `access_classification = RESTRICTED`
- `redaction_status = NOT_REDACTED`

Verify replay did not create duplicate metadata rows:

```sql
SELECT COUNT(*) AS senior_evidence_metadata_count
FROM discounts.discount_evidence_references
WHERE statutory_discount_validation_id = '<draftId-from-response>'::uuid
  AND evidence_type = 'SENIOR_CITIZEN_ID'
  AND purged_at IS NULL;
```

Expected result is `1` after running request `12` and then request `13`.

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
- raw evidence storage
- entitlement fingerprinting
- reconciliation
- AUB
- vendor PMS integration
