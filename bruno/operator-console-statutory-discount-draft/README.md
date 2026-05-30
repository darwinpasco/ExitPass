# Operator Console Statutory Discount Draft Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console statutory discount validation draft, review decision, apply-payable-basis, and policy resolution endpoints.

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/draft
POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision
POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis
POST /v1/ops/operator-console/statutory-discounts/resolve-policy
```

This collection covers access-gated draft creation, duplicate replay behavior, metadata-only evidence reference creation, review decisions, #188 payable-basis application records, and #193 read-only statutory discount policy resolution. It does not implement final `APPLIED` superseding tariff snapshot lifecycle, create payment attempts, confirm payments, call providers, open gates, create coupons, wire UI, upload evidence, store raw evidence, or create reconciliation state.

## Preconditions

- #180 statutory discount draft endpoint is present.
- #182 duplicate-safe draft behavior is present.
- #184 statutory discount decision endpoint is present.
- #188 statutory discount apply-payable-basis endpoint is present.
- #192 policy registry database support is applied.
- #193 statutory discount policy resolution endpoint is present.
- #195 statutory discount draft policy snapshot persistence is present.
- Central PMS is running locally or in a reachable environment.
- The local PostgreSQL database is available.
- Operator Console access evaluation fixtures are seeded.
- The active parking session fixture exists:
  - `parkingSessionId_allowed = 77000000-0000-0000-0000-000000000090`
  - `ticketReference_allowed = MANUAL-SESSION-LOOKUP-001`
- The active original tariff snapshot fixture exists:
  - `originalTariffSnapshotId_allowed = 77000000-0000-0000-0000-000000000091`

## Fixture Seed

Seed script:

```text
infra/db/fixtures/operator-console-access-evaluation/Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

Run against the local development database:

```powershell
docker exec -i exitpass-postgres psql -U exitpass -d exitpass_v12_dev -f /dev/stdin < infra\db\fixtures\operator-console-access-evaluation\Seed-OperatorConsoleAccessEvaluationManualFixtures.sql
```

The script is idempotent. For repeatable statutory discount draft, decision, and apply-payable-basis manual tests, it resets Operator Assisted statutory discount validation fixture drafts for the fixture session and `SENIOR_CITIZEN` or `PWD` entitlement types, including terminal review statuses. It also deletes known manual payable-basis application rows for the fixture session and upserts the active original tariff snapshot fixture `77000000-0000-0000-0000-000000000091` with gross/net amount `125.00 PHP`.

For policy resolution and draft policy snapshot manual tests, it adds synthetic `MANUAL_TEST_*` jurisdiction, site, device, shift, local policy, and parking-session rows. These rows are smoke-test fixtures only; they are not production LGU ordinance seeds and do not mark real local ordinances as verified. The script resets known synthetic draft policy snapshot validation rows for repeatable replay tests. It does not delete access evaluation evidence and does not create payment, gate, coupon, provider, reconciliation, fingerprint, payable-basis application, or final `APPLIED` tariff snapshot records for policy resolution or draft policy snapshot testing.

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

## Draft Policy Snapshot Persistence

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/draft
```

Starting with #195, draft creation resolves statutory discount policy server-side and persists the resolved policy context on the statutory discount validation row. The frontend is not authoritative for policy selection.

Persisted columns:

- `discounts.statutory_discount_validations.statutory_discount_policy_id`
- `discounts.statutory_discount_validations.resolved_jurisdiction_id`
- `discounts.statutory_discount_validations.resolved_policy_snapshot_json`
- `discounts.statutory_discount_validations.policy_resolution_basis`

Manual workflow:

1. Reset fixtures.
2. Run `45 Create draft Senior national fallback policy snapshot`.
3. Run `46 Create draft PWD national fallback policy snapshot`.
4. Run `47 Create draft verified local policy snapshot`.
5. Run `48 Create draft policy snapshot replay`.
6. Run `49 Create draft unverified local policy blocked`.
7. Run `50 Create draft missing site jurisdiction blocked`.
8. Run `51 Create draft access denied policy resolution`.
9. Run `52 Create draft evidence required policy`.

Draft policy snapshot fixture IDs:

- `draftPolicySnapshotSeniorId = 77000000-0000-0000-0000-000000000301`
- `draftPolicySnapshotPwdId = 77000000-0000-0000-0000-000000000302`
- `draftPolicySnapshotVerifiedLocalId = 77000000-0000-0000-0000-000000000303`
- `draftPolicySnapshotUnverifiedLocalId = 77000000-0000-0000-0000-000000000304`
- `draftPolicySnapshotMissingJurisdictionId = 77000000-0000-0000-0000-000000000305`

Expected Senior Citizen national fallback draft:

- HTTP `200`
- `draftAccepted = true`
- `draftPersisted = true`
- `statutoryDiscountPolicyId` is present
- `resolvedJurisdictionId` is present
- `policyResolutionBasis = NATIONAL_LAW_FALLBACK`
- `policyCode = PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK`
- `nationalLawReference = RA 9994`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- `freeDurationMinutes = null`
- `policySnapshot.nationalLawReference = RA 9994`
- `policySnapshot.initialRateExempt = false`
- `policySnapshot.fullFeeExempt = false`

Expected PWD national fallback draft:

- HTTP `200`
- `draftAccepted = true`
- `draftPersisted = true`
- `policyResolutionBasis = NATIONAL_LAW_FALLBACK`
- `policyCode = PH_RA10754_PWD_NATIONAL_FALLBACK`
- `nationalLawReference = RA 10754`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- `freeDurationMinutes = null`
- `policySnapshot.nationalLawReference = RA 10754`
- no automatic free parking or free duration

Expected verified local policy draft:

- HTTP `200`
- `draftAccepted = true`
- `draftPersisted = true`
- `policyResolutionBasis = LOCAL_ORDINANCE_APPLIED`
- `policyCode = MANUAL_TEST_QC_VERIFIED_LOCAL_POLICY`
- `ordinanceReference = MANUAL-QC-ORD-193`
- `nationalLawReference = null`
- `benefitType = FREE_DURATION`
- `freeDurationMinutes = 120`
- `succeedingHoursDiscountRule = REGULAR_RATE`
- `discountBaseScope = CHARGEABLE_PORTION_ONLY`
- `stackingPolicy = NO_STACKING_ON_FREE_PERIOD`

Expected duplicate replay behavior:

- HTTP `200`
- same `draftId` as request `47`
- `reusedExistingDraft = true`
- same key policy context fields
- stored policy snapshot is not overwritten
- no duplicate active draft row

Expected blocked behavior:

- Unverified local policy: HTTP `200`, `draftAccepted = false`, `errorCode = STATUTORY_DISCOUNT_POLICY_UNVERIFIED`, no validation row.
- Missing jurisdiction: HTTP `200`, `draftAccepted = false`, `errorCode = SITE_JURISDICTION_NOT_CONFIGURED`, no validation row.
- Access denied: HTTP `200`, `accessAllowed = false`, `draftAccepted = false`, no policy details returned, no validation row.

Expected evidence-required policy behavior:

- HTTP `200`
- `evidenceRequired = true`
- `policySnapshot.requiresEvidence = true`
- no evidence image upload occurs in this slice

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

## Apply Payable-Basis Endpoint

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis
```

This endpoint evaluates and persists Operator Console access first, then attempts to create a statutory discount payable-basis application record for an approved validation. Current #188 behavior stores the computed audit components in `discounts.statutory_discount_payable_basis_applications` with `application_status = REQUESTED`. It does not create a final `APPLIED` superseding tariff snapshot and does not mutate the original tariff snapshot.

Apply workflow:

1. Reset fixtures.
2. Run `29 Create draft for apply payable basis`.
3. Copy `draftId` into `applyDraftId` and `applyValidationId` if Bruno scripting is unavailable.
4. Run `30 Approve draft for apply payable basis`.
5. Run `31 Apply payable basis approved validation`.
6. Run `32 Apply payable basis replay`.

Validation and denied-access cases:

- Run `29`, then run `35 Apply payable basis validation not approved` before running `30`.
- Run `33 Access denied prevents apply payable basis` after `applyValidationId` has been set. Access denial happens before validation lookup/application mutation.
- Run `34 Apply payable basis validation not found` at any time.
- Run `12 Valid Senior Citizen draft with evidence metadata`, then `27 Evidence required approve blocked`, then `36 Evidence required apply blocked`. Since approval is blocked while `evidence_required = true` and `evidence_captured = false`, apply normally returns `STATUTORY_DISCOUNT_NOT_APPROVED` for that draft. If a future fixture creates an approved evidence-required validation, apply should fail with `EVIDENCE_REQUIRED_NOT_CAPTURED` until evidence capture exists.

Expected first apply behavior:

- HTTP `200`
- `accessAllowed = true`
- `accessDecision = ALLOWED`
- `accessPersisted = true`
- `applicationAccepted = true`
- `applicationPersisted = true`
- `payableBasisApplicationId` is not empty
- `statutoryDiscountValidationId = applyValidationId`
- `parkingSessionId = 77000000-0000-0000-0000-000000000090`
- `originalTariffSnapshotId = 77000000-0000-0000-0000-000000000091`
- `applicationStatus = REQUESTED`
- `alreadyApplied = false`
- `appliedTariffSnapshotId = null`
- `grossAmountMinorUnits = 12500`
- `vatAmountMinorUnits = 1339`
- `vatExclusiveAmountMinorUnits = 11161`
- `statutoryDiscountAmountMinorUnits = 2232`
- `finalPayableAmountMinorUnits = 8929`
- `currencyCode = PHP`

Expected replay behavior:

- HTTP `200`
- `applicationAccepted = true`
- `applicationPersisted = true`
- same `payableBasisApplicationId`
- `applicationStatus = REQUESTED`
- `alreadyApplied = false` while #188 has not implemented final `APPLIED` lifecycle
- no duplicate application row

Apply failure behavior:

- Access denied: HTTP `200`, `accessAllowed = false`, `applicationAccepted = false`, `applicationPersisted = false`, no application row.
- Validation not found: HTTP `404`, `errorCode = STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND`, no application row.
- Validation not approved: HTTP `200`, `errorCode = STATUTORY_DISCOUNT_NOT_APPROVED`, no application row.
- Evidence required but not captured: approval is blocked by the decision endpoint; apply therefore cannot proceed to payable-basis application for that draft.

## Policy Resolution Endpoint

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/resolve-policy
```

This endpoint performs and persists Operator Console access evaluation first, then resolves the statutory discount policy for a site jurisdiction and entitlement type. It is read-only except for access evaluation persistence. It does not create drafts, approve or reject discounts, apply discounts, mutate tariff snapshots, mutate payable basis, create payment attempts, call providers, call gates, create coupons, or create reconciliation records.

Policy resolution workflow:

1. Run the local fixture seed after #192 DB support is applied.
2. Run `37 Resolve policy Senior national fallback`.
3. Run `38 Resolve policy PWD national fallback`.
4. Run `39 Resolve policy verified local policy`.
5. Run `40 Resolve policy unverified local policy blocked`.
6. Run `41 Resolve policy missing site jurisdiction`.
7. Run `42 Resolve policy unsupported entitlement`.
8. Run `43 Resolve policy access denied`.
9. Run `44 Resolve policy site group mismatch`.

Expected policy resolution behavior:

- Access evaluation runs first and is persisted.
- Verified local policy resolves before national fallback.
- Senior Citizen fallback resolves to `RA 9994`.
- PWD fallback resolves to `RA 10754`.
- National fallback does not grant free parking or free duration.
- Unverified/proposed local policy rows do not auto-resolve.
- Missing site jurisdiction fails closed.
- Access-denied requests do not return policy details.
- Site group mismatch is denied by access evaluation before policy resolution, so no policy details are returned.

Policy resolution fixture IDs:

- `policyResolutionSiteId_nationalFallback = 77000000-0000-0000-0000-000000000201`
- `policyResolutionSiteId_verifiedLocal = 77000000-0000-0000-0000-000000000202`
- `policyResolutionSiteId_unverifiedLocal = 77000000-0000-0000-0000-000000000203`
- `policyResolutionSiteId_missingJurisdiction = 77000000-0000-0000-0000-000000000204`
- `policyResolutionVerifiedLocalPolicyId = 77000000-0000-0000-0000-000000000261`
- `policyResolutionUnverifiedLocalPolicyId = 77000000-0000-0000-0000-000000000262`

Expected Senior Citizen national fallback:

- HTTP `200`
- `accessAllowed = true`
- `policyResolved = true`
- `policyCode = PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK`
- `policyResolutionBasis = NATIONAL_LAW_FALLBACK`
- `entitlementType = SENIOR_CITIZEN`
- `nationalLawReference = RA 9994`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- `freeDurationMinutes = null`
- `initialRateExempt = false`
- `fullFeeExempt = false`
- `policySnapshot` exists

Expected PWD national fallback:

- HTTP `200`
- `accessAllowed = true`
- `policyResolved = true`
- `policyCode = PH_RA10754_PWD_NATIONAL_FALLBACK`
- `policyResolutionBasis = NATIONAL_LAW_FALLBACK`
- `entitlementType = PWD`
- `nationalLawReference = RA 10754`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- `freeDurationMinutes = null`
- `initialRateExempt = false`
- `fullFeeExempt = false`
- `policySnapshot` exists

Expected verified local policy:

- HTTP `200`
- `policyResolved = true`
- `policyCode = MANUAL_TEST_QC_VERIFIED_LOCAL_POLICY`
- `policyResolutionBasis = LOCAL_ORDINANCE_APPLIED`
- `ordinanceReference = MANUAL-QC-ORD-193`
- `nationalLawReference = null`
- `verificationStatus = VERIFIED_OFFICIAL`
- `benefitType = FREE_DURATION`
- `freeDurationMinutes = 120`
- `succeedingHoursDiscountRule = REGULAR_RATE`
- `discountBaseScope = CHARGEABLE_PORTION_ONLY`
- `stackingPolicy = NO_STACKING_ON_FREE_PERIOD`

Expected unverified local policy:

- HTTP `200`
- `accessAllowed = true`
- `policyResolved = false`
- `errorCode = STATUTORY_DISCOUNT_POLICY_UNVERIFIED`
- no policy details are returned

Expected missing jurisdiction:

- HTTP `200`
- `accessAllowed = true`
- `policyResolved = false`
- `errorCode = SITE_JURISDICTION_NOT_CONFIGURED`
- no policy details are returned

Expected unsupported entitlement:

- HTTP `400`
- `errorCode = INVALID_OPERATOR_CONSOLE_POLICY_RESOLUTION_REQUEST`
- no access evaluation or policy resolution is attempted

Expected access denied:

- HTTP `200`
- `accessAllowed = false`
- `accessDecision = DENIED`
- `accessPersisted = true`
- `policyResolved = false`
- no policy details are returned

Expected site group mismatch:

- HTTP `200`
- `accessAllowed = false`
- `accessDecision = DENIED`
- `accessPersisted = true`
- `policyResolved = false`
- no policy details are returned

## Read-Only Database Verification

Verify the statutory discount validation draft returned as `draftId`:

```sql
SELECT
    statutory_discount_validation_id,
    parking_session_id,
    entitlement_type,
    validation_status,
    validation_channel,
    statutory_discount_policy_id,
    resolved_jurisdiction_id,
    policy_resolution_basis,
    resolved_policy_snapshot_json,
    evidence_required,
    evidence_captured,
    correlation_id
FROM discounts.statutory_discount_validations
WHERE statutory_discount_validation_id = '<draftId-from-response>'::uuid;
```

Verify the stored draft policy context by `draftId`:

```sql
SELECT
    sdv.statutory_discount_validation_id,
    sdv.parking_session_id,
    sdv.entitlement_type,
    sdv.statutory_discount_policy_id,
    p.policy_code,
    sdv.resolved_jurisdiction_id,
    j.city_municipality_name,
    sdv.policy_resolution_basis,
    sdv.resolved_policy_snapshot_json ->> 'policyCode' AS snapshot_policy_code,
    sdv.resolved_policy_snapshot_json ->> 'nationalLawReference' AS snapshot_national_law_reference,
    sdv.resolved_policy_snapshot_json ->> 'ordinanceReference' AS snapshot_ordinance_reference,
    sdv.resolved_policy_snapshot_json ->> 'benefitType' AS snapshot_benefit_type,
    sdv.resolved_policy_snapshot_json ->> 'freeDurationMinutes' AS snapshot_free_duration_minutes,
    sdv.resolved_policy_snapshot_json ->> 'requiresEvidence' AS snapshot_requires_evidence
FROM discounts.statutory_discount_validations sdv
LEFT JOIN discounts.statutory_discount_policy_registry p
  ON p.statutory_discount_policy_id = sdv.statutory_discount_policy_id
LEFT JOIN sites.jurisdictions j
  ON j.jurisdiction_id = sdv.resolved_jurisdiction_id
WHERE sdv.statutory_discount_validation_id = '<draftId-from-response>'::uuid;
```

Verify no duplicate active draft rows exist for draft policy snapshot replay:

```sql
SELECT
    parking_session_id,
    entitlement_type,
    validation_channel,
    validation_status,
    COUNT(*) AS active_draft_count
FROM discounts.statutory_discount_validations
WHERE parking_session_id = '77000000-0000-0000-0000-000000000303'::uuid
  AND entitlement_type = 'SENIOR_CITIZEN'
  AND validation_channel = 'OPERATOR_ASSISTED'
  AND validation_status IN ('REQUESTED', 'PENDING_OPERATOR_REVIEW')
GROUP BY parking_session_id, entitlement_type, validation_channel, validation_status;
```

Expected result is one active draft for the verified-local replay fixture.

Verify blocked draft policy snapshot requests did not create validation rows:

```sql
SELECT
    parking_session_id,
    COUNT(*) AS validation_count
FROM discounts.statutory_discount_validations
WHERE parking_session_id IN (
    '77000000-0000-0000-0000-000000000304'::uuid,
    '77000000-0000-0000-0000-000000000305'::uuid
)
GROUP BY parking_session_id;
```

Expected result is no rows after requests `49` and `50`.

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

Verify national fallback policy rows and that they do not grant free parking:

```sql
SELECT
    policy_code,
    entitlement_type,
    policy_resolution_basis,
    national_law_reference,
    benefit_type,
    free_duration_minutes,
    initial_rate_exempt_flag,
    full_fee_exempt_flag,
    succeeding_hours_discount_rule,
    discount_base_scope,
    stacking_policy
FROM discounts.statutory_discount_policy_registry
WHERE policy_code IN (
    'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
    'PH_RA10754_PWD_NATIONAL_FALLBACK'
);
```

Expected: both rows have `benefit_type = STATUTORY_DISCOUNT_VAT_EXEMPT`, `free_duration_minutes IS NULL`, `initial_rate_exempt_flag = false`, and `full_fee_exempt_flag = false`.

Verify local policy resolution fixture rows:

```sql
SELECT
    statutory_discount_policy_id,
    jurisdiction_id,
    policy_code,
    entitlement_type,
    policy_resolution_basis,
    ordinance_reference,
    verification_status,
    benefit_type,
    free_duration_minutes,
    succeeding_hours_discount_rule,
    discount_base_scope,
    stacking_policy,
    policy_status
FROM discounts.statutory_discount_policy_registry
WHERE policy_code IN (
    'MANUAL_TEST_QC_VERIFIED_LOCAL_POLICY',
    'MANUAL_TEST_UNVERIFIED_LOCAL_POLICY'
);
```

Verify site jurisdiction mappings used by policy resolution:

```sql
SELECT
    site_id,
    site_code,
    jurisdiction_id
FROM sites.sites
WHERE site_id IN (
    '77000000-0000-0000-0000-000000000201'::uuid,
    '77000000-0000-0000-0000-000000000202'::uuid,
    '77000000-0000-0000-0000-000000000203'::uuid,
    '77000000-0000-0000-0000-000000000204'::uuid
);
```

Expected: the first three sites have `jurisdiction_id`; the missing-jurisdiction fixture site has `jurisdiction_id IS NULL`.

Verify resolve-policy did not create statutory discount validations:

```sql
SELECT COUNT(*) AS statutory_discount_validation_count
FROM discounts.statutory_discount_validations
WHERE correlation_id = '<correlationId-from-response>'::uuid;
```

Expected result is `0`.

Verify resolve-policy did not create payable-basis application rows:

```sql
SELECT COUNT(*) AS payable_basis_application_count
FROM discounts.statutory_discount_payable_basis_applications
WHERE correlation_id = '<correlationId-from-response>'::uuid;
```

Expected result is `0`.

Verify the payable-basis application row returned as `payableBasisApplicationId`:

```sql
SELECT
    statutory_discount_payable_basis_application_id,
    statutory_discount_validation_id,
    parking_session_id,
    original_tariff_snapshot_id,
    applied_tariff_snapshot_id,
    application_status,
    application_channel,
    gross_amount_minor_units,
    vat_amount_minor_units,
    vat_exclusive_amount_minor_units,
    statutory_discount_amount_minor_units,
    final_payable_amount_minor_units,
    currency_code,
    rounding_mode,
    applied_at,
    idempotency_key,
    correlation_id
FROM discounts.statutory_discount_payable_basis_applications
WHERE statutory_discount_payable_basis_application_id = '<payableBasisApplicationId-from-response>'::uuid;
```

Expected #188 values:

- `application_status = REQUESTED`
- `application_channel = OPERATOR_CONSOLE`
- `applied_tariff_snapshot_id IS NULL`
- `applied_at IS NULL`
- `gross_amount_minor_units = 12500`
- `vat_amount_minor_units = 1339`
- `vat_exclusive_amount_minor_units = 11161`
- `statutory_discount_amount_minor_units = 2232`
- `final_payable_amount_minor_units = 8929`
- `currency_code = PHP`

Verify no duplicate payable-basis application rows exist for a validation after replay:

```sql
SELECT COUNT(*) AS payable_basis_application_count
FROM discounts.statutory_discount_payable_basis_applications
WHERE statutory_discount_validation_id = '<applyValidationId>'::uuid;
```

Expected result is `1`.

Verify the original tariff snapshot remains unchanged:

```sql
SELECT
    tariff_snapshot_id,
    parking_session_id,
    gross_amount,
    statutory_discount_amount,
    coupon_discount_amount,
    net_amount,
    statutory_discount_validation_id,
    snapshot_status,
    consumed_at
FROM core.tariff_snapshots
WHERE tariff_snapshot_id = '77000000-0000-0000-0000-000000000091'::uuid;
```

Expected #188 values:

- `gross_amount = 125.00`
- `statutory_discount_amount = 0`
- `coupon_discount_amount = 0`
- `net_amount = 125.00`
- `statutory_discount_validation_id IS NULL`
- `snapshot_status = ACTIVE`
- `consumed_at IS NULL`

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
- final `APPLIED` superseding tariff snapshot lifecycle
- WebPay display
- payment attempt creation from the requested payable-basis application
- original tariff snapshot mutation
- payable-basis finality
- payment creation or payment finality
- gate consume
- coupons
- evidence upload
- raw evidence storage
- entitlement fingerprinting
- reconciliation
- AUB
- vendor PMS integration
