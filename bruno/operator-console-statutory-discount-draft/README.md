# Operator Console Statutory Discount Draft Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console statutory discount validation draft and review decision endpoints.

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/draft
POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision
```

This collection covers access-gated draft creation, duplicate replay behavior, and metadata-only evidence reference creation. It does not approve statutory discounts, apply a discount, mutate payable basis, upload evidence, store raw evidence, create entitlement fingerprints, create payments, or mutate gate/session/payment state.

## Preconditions

- #180 statutory discount draft endpoint is present.
- #182 duplicate-safe draft behavior is present.
- #184 statutory discount decision endpoint is present.
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

The script is idempotent. For repeatable statutory discount draft and decision manual tests, it resets Operator Assisted statutory discount validation fixture drafts for the fixture session and `SENIOR_CITIZEN` or `PWD` entitlement types, including terminal review statuses. It does not delete access evaluation evidence and does not create payment, gate, coupon, provider, reconciliation, fingerprint, or applied statutory discount records.

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

## Review Decision Endpoint

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision
```

This endpoint approves or rejects an existing Operator Console statutory discount validation draft after access evaluation. It only transitions the validation record status; it does not apply the discount, mutate payable basis, calculate a discount amount, remove VAT, create payment attempts, issue exit authorizations, call providers, call gates, create coupons, or create reconciliation records.

Suggested manual flow:

1. Run `01 Valid Senior Citizen draft`.
2. Copy `draftId` from the response.
3. POST the decision endpoint with `decision = APPROVE`, `reviewerAttestation = true`, and the same allowed operator access context.
4. Rerun the same approval request and expect deterministic replay with `alreadyDecided = true` and `decisionChanged = false`.
5. Reset fixtures, run `01 Valid Senior Citizen draft` again, then POST `decision = REJECT` with `decisionReasonCode`.

Approval is fail-closed when `evidence_required = true` and `evidence_captured = false`; use `REJECT` for metadata-only evidence drafts until a future evidence capture/upload slice exists.

## Decision Test Workflow

The decision requests use Bruno environment variables for draft IDs:

- `draftId_approve`
- `draftId_reject`
- `draftId_evidenceRequired`

Requests `14`, `16`, and `12` set those variables with `bru.setEnvVar(...)` when Bruno scripting is available. If your Bruno client does not persist variables from scripts, copy the `draftId` from the create-draft response into the matching environment variable manually.

Run the local fixture seed before each independent decision workflow. The seed resets only known Operator Console statutory discount validation fixture rows for the manual parking session and does not create payment, gate, coupon, provider, reconciliation, or payable-basis records.

Recommended decision run order:

1. Reset fixtures.
2. Run `14 Create draft for approve decision`.
3. Run `15 Approve draft decision`.
4. Run `24 Approve replay`.
5. Run `26 Opposite terminal decision conflict`.
6. Reset fixtures.
7. Run `16 Create draft for reject decision`.
8. Run `17 Reject draft decision`.
9. Run `25 Reject replay`.
10. Reset fixtures.
11. Run `12 Valid Senior Citizen draft with evidence metadata`.
12. Run `27 Evidence required approve blocked`.
13. Run `28 Evidence required reject allowed`.

Validation and denied-access cases can be run any time after `draftId_approve` or `draftId_reject` has been set:

- `18 Access denied prevents decision`
- `19 Missing decision`
- `20 Unsupported decision`
- `21 Reviewer attestation false`
- `22 Reject without reason`
- `23 Decision draft not found`

## Decision Expected Outcomes

Approve draft with evidence not required:

- HTTP `200`
- `accessAllowed = true`
- `accessDecision = ALLOWED`
- `accessPersisted = true`
- `decisionAccepted = true`
- `decisionPersisted = true`
- `previousValidationStatus = REQUESTED` or `PENDING_OPERATOR_REVIEW`
- `currentValidationStatus = APPROVED`
- `decision = APPROVE`
- `decisionChanged = true`
- `alreadyDecided = false`

Reject draft:

- HTTP `200`
- `accessAllowed = true`
- `decisionAccepted = true`
- `decisionPersisted = true`
- `currentValidationStatus = REJECTED`
- `decision = REJECT`
- `decisionChanged = true`
- `alreadyDecided = false`

Access denied prevents decision:

- HTTP `200`
- `accessAllowed = false`
- `accessDecision = DENIED`
- `accessPersisted = true`
- `decisionAccepted = false`
- `decisionPersisted = false`
- draft status remains unchanged

Validation failures:

- Missing decision: HTTP `400`, `errorCode = INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_REQUEST`
- Unsupported decision: HTTP `400`, `errorCode = INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_REQUEST`
- `reviewerAttestation = false`: HTTP `400`, `errorCode = INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_REQUEST`
- Reject without reason: HTTP `400`, `errorCode = INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_REQUEST`

Draft not found:

- HTTP `404`
- `accessAllowed = true`
- `decisionAccepted = false`
- `errorCode = DRAFT_NOT_FOUND`

Replay and conflict behavior:

- Approve replay: HTTP `200`, `alreadyDecided = true`, `decisionChanged = false`
- Reject replay: HTTP `200`, `alreadyDecided = true`, `decisionChanged = false`
- Opposite terminal decision: HTTP `409`, `errorCode = STATUTORY_DISCOUNT_DRAFT_ALREADY_DECIDED`

Evidence-required behavior:

- Approve when `evidence_required = true` and `evidence_captured = false`: HTTP `200`, `decisionAccepted = false`, `errorCode = EVIDENCE_REQUIRED_NOT_CAPTURED`, status remains `REQUESTED`
- Reject when `evidence_required = true`: HTTP `200`, `currentValidationStatus = REJECTED`, `decisionPersisted = true`

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

Verify the decision endpoint status transition:

```sql
SELECT
    statutory_discount_validation_id,
    validation_status,
    decision_reason_code,
    validated_at,
    validated_by_user_id,
    correlation_id
FROM discounts.statutory_discount_validations
WHERE statutory_discount_validation_id = '<draftId-from-response>'::uuid;
```

For approved decisions, expect `validation_status = APPROVED`. For rejected decisions, expect `validation_status = REJECTED` and `decision_reason_code` populated.

Verify the access evaluation row returned as `accessEvaluationId`:

```sql
SELECT
    operator_access_evaluation_id,
    correlation_id,
    evaluation_status,
    requested_action,
    workflow_code,
    target_entity_type,
    target_entity_id
FROM operator_console.operator_access_evaluations
WHERE operator_access_evaluation_id = '<accessEvaluationId-from-response>'::uuid;
```

Verify no payment, gate, coupon, provider, or reconciliation records were created for the fixture parking session:

```sql
SELECT
    (SELECT COUNT(*) FROM core.payment_attempts
      WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid) AS payment_attempt_count,
    (SELECT COUNT(*)
       FROM core.payment_confirmations pc
       JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
      WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid) AS payment_confirmation_count,
    (SELECT COUNT(*) FROM core.exit_authorizations
      WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid) AS exit_authorization_count,
    (SELECT COUNT(*)
       FROM gates.gate_authorization_consumptions gac
       JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
      WHERE ea.parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid) AS gate_consumption_count,
    (SELECT COUNT(*) FROM coupons.coupon_applications
      WHERE parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid) AS coupon_application_count,
    (SELECT COUNT(*)
       FROM payments.provider_outcomes po
       JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
      WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid) AS provider_outcome_count,
    (SELECT COUNT(*)
       FROM reconciliation.reconciliation_items ri
       JOIN core.payment_attempts pa ON pa.payment_attempt_id = ri.payment_attempt_id
      WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000090'::uuid) AS reconciliation_item_count;
```

Expected result for all counts is `0` when the local fixture seed has not been combined with payment-flow fixtures for the same parking session.

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
