# Operator Console Statutory Discount Draft Manual Smoke Tests

Purpose: repeatable manual/API smoke testing for the Central PMS Operator Console statutory discount validation draft, review decision, apply-payable-basis, and policy resolution endpoints.

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/draft
POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision
POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis
POST /v1/ops/operator-console/statutory-discounts/resolve-policy
```

This collection covers access-gated draft creation, duplicate replay behavior, metadata-only evidence reference creation, review decisions, #193 read-only statutory discount policy resolution, #197 persisted-policy apply behavior, #200 final `APPLIED` statutory discount tariff snapshot lifecycle behavior, #202 WebPay/vendor effective payable-basis reads, #205 payment-attempt creation against the effective APPLIED tariff snapshot, and #207 manual payment-finality coverage for #206. It does not call real payment providers, open gates, create coupons, wire UI, upload evidence, store raw evidence, or create reconciliation state.

## Preconditions

- #180 statutory discount draft endpoint is present.
- #182 duplicate-safe draft behavior is present.
- #184 statutory discount decision endpoint is present.
- #188 statutory discount apply-payable-basis endpoint is present.
- #192 policy registry database support is applied.
- #193 statutory discount policy resolution endpoint is present.
- #195 statutory discount draft policy snapshot persistence is present.
- #197 apply-payable-basis uses persisted statutory discount policy snapshots.
- #199 final APPLIED statutory discount tariff snapshot lifecycle DB routine is applied.
- #200 apply-payable-basis calls `discounts.apply_statutory_discount_payable_basis`.
- #204 payment-attempt creation validates the effective APPLIED tariff snapshot.
- #206 payment confirmation/finality validates against the payment attempt stored tariff snapshot and amount/currency.
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

For policy resolution, draft policy snapshot, and apply persisted-policy-snapshot manual tests, it adds synthetic `MANUAL_TEST_*` jurisdiction, site, device, shift, local policy, parking-session, tariff-snapshot, and approved-validation rows. These rows are smoke-test fixtures only; they are not production LGU ordinance seeds and do not mark real local ordinances as verified. The script resets known synthetic draft policy snapshot validation rows and known synthetic apply validations for repeatable replay tests. It also resets payment-attempt rows for the synthetic #205/#207 payment sessions `306`, `307`, `308`, `314`, `315`, and `316` so payment-attempt and payment-finality smoke tests can be rerun after reseeding. It does not delete access evaluation evidence and does not create gate, coupon, provider outcome, reconciliation, or fingerprint records for policy resolution, draft policy snapshot, apply persisted-policy-snapshot, vendor resolve, payment-attempt, or payment-finality effective-tariff testing.

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

This endpoint evaluates and persists Operator Console access first, then applies an approved statutory discount payable basis. Current #200 behavior stores computed audit components in `discounts.statutory_discount_payable_basis_applications`, calls `discounts.apply_statutory_discount_payable_basis`, transitions the application to `APPLIED`, supersedes the original tariff snapshot, and creates one statutory-discount-adjusted active tariff snapshot. It still does not create payment attempts, confirm payments, call providers, call gates, create coupons, wire UI, or create reconciliation state.

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
- `applicationStatus = APPLIED`
- `alreadyApplied = false`
- `appliedTariffSnapshotId` is populated
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
- `applicationStatus = APPLIED`
- `alreadyApplied = true`
- same `appliedTariffSnapshotId`
- no duplicate application row
- no duplicate applied tariff snapshot

Apply failure behavior:

- Access denied: HTTP `200`, `accessAllowed = false`, `applicationAccepted = false`, `applicationPersisted = false`, no application row.
- Validation not found: HTTP `404`, `errorCode = STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND`, no application row.
- Validation not approved: HTTP `200`, `errorCode = STATUTORY_DISCOUNT_NOT_APPROVED`, no application row.
- Evidence required but not captured: approval is blocked by the decision endpoint; apply therefore cannot proceed to payable-basis application for that draft.

## Apply Payable-Basis Persisted Policy Snapshot

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis
```

Starting with #197, apply-payable-basis uses the policy context persisted on `discounts.statutory_discount_validations` at draft creation time. It does not re-resolve the current policy registry as authoritative during apply. The persisted snapshot must contain a valid `statutoryDiscountPolicyId` that matches `statutory_discount_validations.statutory_discount_policy_id`.

Preconditions:

- #197 is merged.
- Central PMS is running.
- Local PostgreSQL is available.
- The fixture seed has been applied.
- Approved validation fixtures have persisted policy context.

Manual workflow:

1. Reset fixtures.
2. Run `53 Create draft for RA9994 policy snapshot apply` to verify draft-time RA 9994 policy snapshot shape.
3. Run `54 Approve RA9994 policy snapshot draft`. This request targets the seeded approved RA 9994 apply fixture; it is deterministic approve replay when already approved.
4. Run `55 Apply payable basis RA9994 policy snapshot`.
5. Run `59 Apply payable basis policy snapshot replay`.
6. Reset fixtures.
7. Run `56 Create draft for RA10754 policy snapshot apply` to verify draft-time RA 10754 policy snapshot shape.
8. Run `57 Approve RA10754 policy snapshot draft`. This request targets the seeded approved RA 10754 apply fixture; it is deterministic approve replay when already approved.
9. Run `58 Apply payable basis RA10754 policy snapshot`.
10. Run blocked cases `60` through `64`.

The national fallback policy rows currently require evidence. Because no evidence upload/capture endpoint exists yet, requests `54` and `57` use synthetic approved validations with `evidence_captured = true` so apply-payable-basis can be smoke-tested without changing runtime behavior.

Apply persisted-policy fixture IDs:

- `policySnapshotApplyValidationId_ra9994 = 77000000-0000-0000-0000-000000000306`
- `policySnapshotApplyValidationId_ra10754 = 77000000-0000-0000-0000-000000000307`
- `validationId_missingPolicyContext = 77000000-0000-0000-0000-000000000308`
- `validationId_invalidPolicySnapshot = 77000000-0000-0000-0000-000000000309`
- `validationId_mismatchedPolicySnapshot = 77000000-0000-0000-0000-000000000310`
- `validationId_freeDurationPolicy = 77000000-0000-0000-0000-000000000311`

Expected #197/#200 behavior:

- Apply uses `resolved_policy_snapshot_json` stored on the validation.
- `policySnapshotUsed = true` for accepted apply results.
- `STATUTORY_DISCOUNT_VAT_EXEMPT` is supported.
- `FREE_DURATION` and `INITIAL_RATE_EXEMPTION` are blocked for now.
- `computation_basis_json.policyContext` is persisted on accepted application rows.
- Replay preserves the existing `computation_basis_json.policyContext`.
- Starting with #200, successful apply calls `discounts.apply_statutory_discount_payable_basis`.
- `application_status = APPLIED`.
- `applied_tariff_snapshot_id IS NOT NULL`.
- The original tariff snapshot transitions from `ACTIVE` to `SUPERSEDED`.
- The original tariff amount fields remain unchanged.
- A new statutory-discount-adjusted tariff snapshot is `ACTIVE`.
- No payment attempt, provider call, gate record, coupon application, reconciliation item, AUB behavior, or UI wiring is created.

Expected RA 9994 apply:

- HTTP `200`
- `accessAllowed = true`
- `applicationAccepted = true`
- `applicationPersisted = true`
- `policySnapshotUsed = true`
- `policyResolutionBasis = NATIONAL_LAW_FALLBACK`
- `policyCode = PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- `nationalLawReference = RA 9994`
- `applicationStatus = APPLIED`
- `appliedTariffSnapshotId` is populated

Expected RA 10754 apply:

- HTTP `200`
- `accessAllowed = true`
- `applicationAccepted = true`
- `applicationPersisted = true`
- `policySnapshotUsed = true`
- `policyResolutionBasis = NATIONAL_LAW_FALLBACK`
- `policyCode = PH_RA10754_PWD_NATIONAL_FALLBACK`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- `nationalLawReference = RA 10754`
- `applicationStatus = APPLIED`
- `appliedTariffSnapshotId` is populated

Expected replay behavior:

- HTTP `200`
- same `payableBasisApplicationId`
- same policy context fields
- no duplicate application row
- no policy re-resolution
- same applied tariff snapshot
- no duplicate applied tariff snapshot

Expected blocked behavior:

- Missing policy context: HTTP `200`, `errorCode = STATUTORY_DISCOUNT_POLICY_CONTEXT_MISSING`, no application row.
- Invalid policy snapshot identity: HTTP `200`, `errorCode = STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID`, no application row.
- Mismatched policy snapshot identity: HTTP `200`, `errorCode = STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID`, no application row.
- Unsupported `FREE_DURATION` benefit: HTTP `200`, `errorCode = POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION`, no generic VAT-exempt computation, no application row.
- Access denied: HTTP `200`, `accessAllowed = false`, `applicationAccepted = false`, `applicationPersisted = false`, no application row.

## Final APPLIED Apply Payable-Basis Lifecycle

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis
```

Starting with #200, the apply endpoint calls the #199 database routine:

```sql
discounts.apply_statutory_discount_payable_basis(uuid, uuid, uuid)
```

The routine finalizes a persisted payable-basis application by changing the original active tariff snapshot to `SUPERSEDED`, creating one statutory-discount-adjusted `ACTIVE` tariff snapshot, linking the application to that new snapshot, and setting `application_status = APPLIED`.

Preconditions:

- #199 DB routine is applied.
- #200 runtime integration is present.
- Central PMS is running.
- Local PostgreSQL is available.
- The fixture seed has been applied.
- Approved validations have persisted VAT-exempt policy context.

Manual workflow:

1. Reset fixtures.
2. Run `65 Create draft for RA9994 applied lifecycle`.
3. Run `66 Approve RA9994 applied lifecycle draft`.
4. Run `67 Apply payable basis RA9994 applied lifecycle`.
5. Run `71 Apply payable basis applied lifecycle replay`.
6. Reset fixtures.
7. Run `68 Create draft for RA10754 applied lifecycle`.
8. Run `69 Approve RA10754 applied lifecycle draft`.
9. Run `70 Apply payable basis RA10754 applied lifecycle`.
10. Reset fixtures.
11. Run `72 Apply payable basis requested application completes to applied`.
12. Reset fixtures.
13. Run `73 Apply payable basis payment attempt blocks applied lifecycle`.
14. Reset fixtures.
15. Run `74 Apply payable basis free duration still blocked`.
16. Run `75 Apply payable basis access denied applied lifecycle`.

Final APPLIED lifecycle fixture IDs:

- `appliedLifecycleValidationId_ra9994 = 77000000-0000-0000-0000-000000000306`
- `appliedLifecycleValidationId_ra10754 = 77000000-0000-0000-0000-000000000307`
- `validationId_requestedApplication = 77000000-0000-0000-0000-000000000312`
- `validationId_paymentAttemptBlocked = 77000000-0000-0000-0000-000000000313`
- `validationId_freeDurationPolicy = 77000000-0000-0000-0000-000000000311`

Expected successful APPLIED behavior:

- HTTP `200`
- `accessAllowed = true`
- `applicationAccepted = true`
- `applicationPersisted = true`
- `applicationStatus = APPLIED`
- `appliedTariffSnapshotId` is populated
- `policySnapshotUsed = true`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- `alreadyApplied = false` on first successful apply
- original tariff snapshot is `SUPERSEDED`
- applied tariff snapshot is `ACTIVE`
- no payment attempt is created by successful apply

Expected replay behavior:

- HTTP `200`
- `applicationStatus = APPLIED`
- same `payableBasisApplicationId`
- same `appliedTariffSnapshotId`
- `alreadyApplied = true`
- no duplicate application row
- no duplicate applied tariff snapshot
- policy context is preserved

Expected REQUESTED-completion behavior:

- The seeded `validationId_requestedApplication` has an existing `REQUESTED` application with `applied_tariff_snapshot_id IS NULL`.
- Apply completes it to `APPLIED`.
- `appliedTariffSnapshotId` is populated.
- The original snapshot becomes `SUPERSEDED`.
- The applied snapshot is `ACTIVE`.

Expected payment-attempt guardrail behavior:

- HTTP `200`
- `applicationAccepted = false`
- `applicationPersisted = false`
- `errorCode = PAYMENT_ATTEMPT_ALREADY_EXISTS`
- original tariff snapshot remains `ACTIVE`
- application remains not `APPLIED`
- no applied tariff snapshot is created

Expected unsupported/free-duration behavior:

- HTTP `200`
- `applicationAccepted = false`
- `errorCode = POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION`
- no generic VAT-exempt computation is applied
- no applied tariff snapshot is created

Expected access-denied behavior:

- HTTP `200`
- `accessAllowed = false`
- `accessPersisted = true`
- `applicationAccepted = false`
- `applicationPersisted = false`
- no tariff snapshot transition is attempted

## WebPay Vendor Parking Effective Payable Basis

Endpoint:

```http
POST /v1/vendor-parking/resolve
```

Starting with #202, the WebPay-facing vendor parking resolution/session summary read path returns the effective payable basis. If an `APPLIED` statutory discount payable-basis application exists and points to a valid active applied tariff snapshot, `netPayableMinorUnits` and `tariffSnapshotId` come from the applied snapshot. If no `APPLIED` statutory discount exists, the endpoint preserves existing behavior and returns the current active base tariff snapshot. Vendor parking resolution remains read-only and does not create payment attempts.

Preconditions:

- #202 is merged.
- Central PMS is running.
- Local PostgreSQL is available.
- The fixture seed has been applied.
- #199 and #200 APPLIED lifecycle support is available.

Manual workflow:

1. Reset fixtures.
2. Run `76 Vendor resolve before statutory discount apply`.
3. Run `77 Create standalone draft for vendor resolve coverage`.
4. Run `78 Verify vendor resolve applied basis fixture`.
5. Run `79 Apply pre-approved payable basis for vendor resolve`.
6. Run `80 Vendor resolve after applied payable basis`.
7. Run `81 Vendor resolve RA9994 applied payable basis`.
8. Reset fixtures.
9. Run `70 Apply payable basis RA10754 applied lifecycle`.
10. Run `82 Vendor resolve RA10754 applied payable basis`.
13. For replay stability, run `71 Apply payable basis applied lifecycle replay`, then run `83 Vendor resolve after applied lifecycle replay`.
14. Run `84 Vendor resolve no payment attempt created`.

Request `77` is a standalone draft smoke test that proves draft creation still returns a VAT-exempt RA 9994 policy snapshot for the national-fallback fixture. The WebPay effective-payable-basis path uses the seeded `MANUAL-APPLY-POLICY-RA9994` validation because it includes a stable original tariff snapshot for final APPLIED lifecycle verification. That fixture is already `APPROVED` after the local seed is applied; request `78` verifies the seeded session is still on the original active tariff snapshot before request `79` applies it. Do not run an approval request against `77000000-0000-0000-0000-000000000306`; after fixture reset it is already approved, and after an apply replay it may already be APPLIED.

For RA 10754, the seeded `77000000-0000-0000-0000-000000000307` fixture is also already approved after reset. Run request `70` directly to apply it before request `82` when validating the APPLIED PWD path. Request `82` remains deterministic if it is run before request `70`; in that case it confirms the original active payable basis is still returned and no payment attempt is created.

Vendor resolve fixture values:

- `vendorSystemId_manual = 77000000-0000-0000-0000-000000000004`
- `vendorResolveTicketReference_noDiscount = MANUAL-APPLY-MISSING-POLICY-CONTEXT`
- `vendorResolveTicketReference_ra9994 = MANUAL-APPLY-POLICY-RA9994`
- `vendorResolveTicketReference_ra10754 = MANUAL-APPLY-POLICY-RA10754`
- no-APPLIED-discount base snapshot: `vendorResolveOriginalTariffSnapshotId_noDiscount = 77000000-0000-0000-0000-000000000398`
- RA 9994 original snapshot: `77000000-0000-0000-0000-000000000396`
- RA 10754 original snapshot: `77000000-0000-0000-0000-000000000397`

Expected no-discount vendor resolve behavior:

- HTTP `200`
- `lookupOutcome = resolved`
- `statutoryDiscountApplied = false` or the optional field is absent
- `tariffSnapshotId = 77000000-0000-0000-0000-000000000398`
- `effectiveTariffSnapshotId` is absent or equals `tariffSnapshotId`
- `netPayableMinorUnits = 12500`
- `paymentStatus = Not Started`
- no payment attempt is created

Expected vendor resolve after APPLIED behavior:

- HTTP `200`
- `lookupOutcome = resolved`
- `statutoryDiscountApplied = true`
- `statutoryDiscountValidationId` is populated
- `statutoryDiscountApplicationId` is populated
- `originalTariffSnapshotId` is populated
- `appliedTariffSnapshotId` is populated
- `effectiveTariffSnapshotId = appliedTariffSnapshotId`
- `tariffSnapshotId = appliedTariffSnapshotId`
- `netPayableMinorUnits = 8929`
- `policyResolutionBasis = NATIONAL_LAW_FALLBACK`
- `benefitType = STATUTORY_DISCOUNT_VAT_EXEMPT`
- original `SUPERSEDED` amount is not returned as amount due
- no payment attempt is created

Expected replay behavior:

- vendor resolve returns the same `appliedTariffSnapshotId`
- `effectiveTariffSnapshotId` remains the same applied snapshot
- `netPayableMinorUnits` remains `8929`
- no duplicate applied tariff snapshot exists
- no payment attempt is created

Invalid APPLIED application guardrail:

- #202 is expected to fail closed if an `APPLIED` application exists without a valid active applied tariff snapshot.
- The current database constraints make that broken state intentionally difficult to construct through ordinary fixture data, so this manual pack documents the guardrail and relies on code/integration coverage for direct broken-state setup.
- The expected behavior is deterministic failure, not stale original amount display.

## Payment Attempt Effective Applied Tariff Snapshot

Endpoints:

```http
POST /v1/vendor-parking/resolve
POST /v1/public/payment-attempts
```

Starting with #204, payment-attempt creation validates the same effective payable basis returned by WebPay/vendor parking resolution. If an `APPLIED` statutory discount payable-basis application exists, the applied statutory-discount tariff snapshot is the only valid payable basis for a new payment attempt. The original `SUPERSEDED` tariff snapshot is rejected. If no `APPLIED` statutory discount exists, the current active base tariff behavior is preserved.

Preconditions:

- #204 is merged.
- Central PMS is running.
- Local PostgreSQL is available.
- The fixture seed has been applied.
- #199 and #200 APPLIED lifecycle support is available.
- The #204 replay patch is present so idempotent replay can reuse an attempt after the applied tariff snapshot has been consumed.

Manual workflow:

1. Reset fixtures.
2. Run `85 Vendor resolve no-discount for payment attempt`.
3. Run `86 Create payment attempt no-discount active tariff`.
4. Reset fixtures before continuing, because request `86` consumes the no-discount tariff snapshot.
5. Run `87 Vendor resolve APPLIED basis for payment attempt`.
6. Run `88 Create payment attempt effective applied tariff`.
7. Run `90 Verify vendor resolve and payment attempt effective basis match`.
8. Run `91 Create payment attempt effective applied tariff replay`.
9. Run `89 Create payment attempt stale original tariff rejected`.
10. Run `92 Create payment attempt superseded tariff rejected`.
11. Run `93 Create payment attempt session mismatch rejected`.

Request `87` captures `effectiveTariffSnapshotId` / `appliedTariffSnapshotId` into `paymentTariffSnapshotId_appliedEffective`, which is the exact variable submitted by request `88`. The local environment also carries the same fixture value as a non-zero fallback for single-request inspection, but the documented workflow should still run `87` before `88`.

Payment-attempt fixture values:

- no-discount parking session: `paymentParkingSessionId_noDiscount = 77000000-0000-0000-0000-000000000308`
- no-discount active tariff snapshot: `paymentTariffSnapshotId_noDiscountActive = 77000000-0000-0000-0000-000000000398`
- APPLIED payment session: `paymentParkingSessionId_applied = 77000000-0000-0000-0000-000000000316`
- APPLIED original/superseded snapshot: `paymentTariffSnapshotId_originalSuperseded = 77000000-0000-0000-0000-000000000406`
- APPLIED effective snapshot fallback: `paymentTariffSnapshotId_appliedEffective = 77000000-0000-0000-0000-000000000506`
- stale-rejection APPLIED session: `paymentParkingSessionId_staleRejected = 77000000-0000-0000-0000-000000000314`
- stale-rejection original/superseded snapshot: `paymentTariffSnapshotId_originalSuperseded_stale = 77000000-0000-0000-0000-000000000404`
- superseded-only rejection session: `paymentParkingSessionId_supersededRejected = 77000000-0000-0000-0000-000000000315`
- superseded-only tariff snapshot: `paymentTariffSnapshotId_supersededRejected = 77000000-0000-0000-0000-000000000405`
- session-mismatch tariff snapshot: `paymentTariffSnapshotId_otherSession = 77000000-0000-0000-0000-000000000504`
- no-discount amount: `paymentAmountMinorUnits_noDiscount = 12500`
- applied statutory discount amount: `paymentAmountMinorUnits_appliedEffective = 8929`
- payment method used by these smoke requests: `paymentMethodGcash = GCASH`

Expected no-discount payment behavior:

- request `85` returns HTTP `200`
- `statutoryDiscountApplied = false`
- `effectiveTariffSnapshotId = 77000000-0000-0000-0000-000000000398`
- `netPayableMinorUnits = 12500`
- request `86` returns HTTP `201` on first run or HTTP `200` on idempotent replay
- `paymentAttemptId` is populated
- `paymentProvider = GCASH`
- the payment attempt is persisted against tariff snapshot `77000000-0000-0000-0000-000000000398`
- provider routing behavior remains unchanged; AUB is not selected or invoked

Expected APPLIED statutory discount payment behavior:

- request `87` returns HTTP `200`
- `statutoryDiscountApplied = true`
- `effectiveTariffSnapshotId = appliedTariffSnapshotId`
- `originalTariffSnapshotId = 77000000-0000-0000-0000-000000000406`
- `netPayableMinorUnits = 8929`
- request `88` returns HTTP `201` on first run or HTTP `200` on replay
- the payment attempt is persisted against the applied tariff snapshot, not the original `SUPERSEDED` snapshot
- persisted amount is `89.29 PHP`
- original `SUPERSEDED` tariff snapshot is not used as the payable basis
- PayMongo-only routing behavior remains unchanged for WebPay rails; AUB remains out of scope

Expected payment replay behavior:

- requests `90` and `91` return HTTP `200`
- `wasReused = true`
- same `paymentAttemptId` as request `88`
- no duplicate payment-attempt row is created
- replay succeeds even after the applied tariff snapshot was consumed by the first payment attempt

Expected stale/superseded rejection behavior:

- request `89` returns HTTP `409`
- `errorCode = STALE_TARIFF_SNAPSHOT`
- `details.submitted_tariff_snapshot_id = 77000000-0000-0000-0000-000000000404`
- `details.effective_tariff_snapshot_id` is populated
- no payment-attempt row is created for the stale request
- request `92` returns HTTP `409` with `STALE_TARIFF_SNAPSHOT` or `TARIFF_SNAPSHOT_INVALID`
- request `93` returns HTTP `409`
- `errorCode = TARIFF_SNAPSHOT_INVALID`
- no session-mismatched payment attempt is created
Request `93` uses the clean superseded-only session `315` with the APPLIED fixture tariff snapshot `504`; it does not reuse the no-discount session after request `86`, because request `86` intentionally creates an active payment attempt for that no-discount session.

Provider boundary:

- These smoke requests use `GCASH` as the public payment method.
- They do not confirm payment, call PayMongo, call AUB, create provider outcomes, issue exit authorization, call gates, create coupons, or create reconciliation records.
- QRPH/GCash/Maya/card provider routing remains governed by existing payment rails; this manual slice only verifies tariff snapshot selection and stale snapshot rejection.

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
    computation_basis_json,
    rounding_mode,
    applied_at,
    idempotency_key,
    correlation_id
FROM discounts.statutory_discount_payable_basis_applications
WHERE statutory_discount_payable_basis_application_id = '<payableBasisApplicationId-from-response>'::uuid;
```

Expected #200 successful APPLIED values:

- `application_status = APPLIED`
- `application_channel = OPERATOR_CONSOLE`
- `applied_tariff_snapshot_id IS NOT NULL`
- `applied_at IS NOT NULL`
- `gross_amount_minor_units = 12500`
- `vat_amount_minor_units = 1339`
- `vat_exclusive_amount_minor_units = 11161`
- `statutory_discount_amount_minor_units = 2232`
- `final_payable_amount_minor_units = 8929`
- `currency_code = PHP`

Verify final APPLIED tariff snapshot lifecycle state:

```sql
SELECT
    app.statutory_discount_payable_basis_application_id,
    app.statutory_discount_validation_id,
    app.application_status,
    app.original_tariff_snapshot_id,
    original.snapshot_status AS original_snapshot_status,
    original.gross_amount AS original_gross_amount,
    original.statutory_discount_amount AS original_discount_amount,
    original.coupon_discount_amount AS original_coupon_amount,
    original.net_amount AS original_net_amount,
    app.applied_tariff_snapshot_id,
    applied.snapshot_status AS applied_snapshot_status,
    applied.gross_amount AS applied_gross_amount,
    applied.statutory_discount_amount AS applied_discount_amount,
    applied.coupon_discount_amount AS applied_coupon_amount,
    applied.net_amount AS applied_net_amount,
    applied.statutory_discount_validation_id AS applied_snapshot_validation_id
FROM discounts.statutory_discount_payable_basis_applications app
JOIN core.tariff_snapshots original
  ON original.tariff_snapshot_id = app.original_tariff_snapshot_id
LEFT JOIN core.tariff_snapshots applied
  ON applied.tariff_snapshot_id = app.applied_tariff_snapshot_id
WHERE app.statutory_discount_validation_id = '<validationId>'::uuid;
```

Expected after successful #200 apply:

- `application_status = APPLIED`
- `applied_tariff_snapshot_id IS NOT NULL`
- `original_snapshot_status = SUPERSEDED`
- original amount fields remain `gross_amount = 125.00`, `statutory_discount_amount = 0`, `coupon_discount_amount = 0`, `net_amount = 125.00`
- `applied_snapshot_status = ACTIVE`
- `applied_discount_amount = 22.32`
- `applied_net_amount = 89.29`
- `applied_snapshot_validation_id = <validationId>`

Verify no duplicate applied tariff snapshots exist after replay:

```sql
SELECT COUNT(*) AS applied_tariff_snapshot_count
FROM core.tariff_snapshots
WHERE statutory_discount_validation_id = '<validationId>'::uuid;
```

Expected result is `1`.

Verify the WebPay/vendor parking effective payable basis for an APPLIED statutory discount:

```sql
SELECT
    ps.parking_session_id,
    ps.ticket_number_masked,
    app.statutory_discount_payable_basis_application_id,
    app.statutory_discount_validation_id,
    app.application_status,
    app.original_tariff_snapshot_id,
    original.snapshot_status AS original_snapshot_status,
    original.net_amount AS original_net_amount,
    app.applied_tariff_snapshot_id,
    applied.snapshot_status AS applied_snapshot_status,
    applied.net_amount AS applied_net_amount,
    app.final_payable_amount_minor_units,
    applied.statutory_discount_validation_id AS applied_snapshot_validation_id
FROM core.parking_sessions ps
JOIN discounts.statutory_discount_payable_basis_applications app
  ON app.parking_session_id = ps.parking_session_id
JOIN core.tariff_snapshots original
  ON original.tariff_snapshot_id = app.original_tariff_snapshot_id
JOIN core.tariff_snapshots applied
  ON applied.tariff_snapshot_id = app.applied_tariff_snapshot_id
WHERE ps.ticket_number_masked IN (
    'MANUAL-APPLY-POLICY-RA9994',
    'MANUAL-APPLY-POLICY-RA10754'
)
  AND app.application_status = 'APPLIED';
```

Expected after requests `79` or `70`: `original_snapshot_status = SUPERSEDED`, `applied_snapshot_status = ACTIVE`, `applied_net_amount = 89.29`, and `final_payable_amount_minor_units = 8929`.

Verify no-discount vendor resolve fixture still has only the active base tariff snapshot:

```sql
SELECT
    ps.parking_session_id,
    ps.ticket_number_masked,
    ts.tariff_snapshot_id,
    ts.snapshot_status,
    ts.net_amount,
    ts.statutory_discount_validation_id
FROM core.parking_sessions ps
JOIN core.tariff_snapshots ts
  ON ts.parking_session_id = ps.parking_session_id
WHERE ps.parking_session_id = '77000000-0000-0000-0000-000000000308'::uuid
ORDER BY ts.created_at DESC;
```

Expected after fixture reset and before apply: active snapshot `77000000-0000-0000-0000-000000000398`, `snapshot_status = ACTIVE`, `net_amount = 125.00`, and no `APPLIED` payable-basis application for the session.

Verify vendor parking resolution did not create payment attempts:

```sql
SELECT
    ps.parking_session_id,
    ps.ticket_number_masked,
    COUNT(pa.payment_attempt_id) AS payment_attempt_count
FROM core.parking_sessions ps
LEFT JOIN core.payment_attempts pa
  ON pa.parking_session_id = ps.parking_session_id
WHERE ps.parking_session_id IN (
    '77000000-0000-0000-0000-000000000308'::uuid,
    '77000000-0000-0000-0000-000000000306'::uuid,
    '77000000-0000-0000-0000-000000000307'::uuid
)
GROUP BY ps.parking_session_id, ps.ticket_number_masked
ORDER BY ps.parking_session_id;
```

Expected result is `payment_attempt_count = 0` for all three sessions when only vendor resolve and apply-payable-basis smoke requests have been run.

Verify no duplicate APPLIED tariff snapshots exist for WebPay vendor resolve fixtures:

```sql
SELECT
    statutory_discount_validation_id,
    COUNT(*) AS applied_snapshot_count
FROM core.tariff_snapshots
WHERE statutory_discount_validation_id IN (
    '77000000-0000-0000-0000-000000000306'::uuid,
    '77000000-0000-0000-0000-000000000307'::uuid
)
GROUP BY statutory_discount_validation_id;
```

Expected result is one applied tariff snapshot per validation after replay.

Verify payment attempts created by #205 effective-tariff requests:

```sql
SELECT
    pa.payment_attempt_id,
    pa.parking_session_id,
    ps.ticket_number_masked,
    pa.tariff_snapshot_id,
    ts.snapshot_status,
    pa.idempotency_key,
    pa.amount,
    pa.currency_code,
    pa.attempt_status,
    pr.provider_code,
    pr.rail_code
FROM core.payment_attempts pa
JOIN core.parking_sessions ps
  ON ps.parking_session_id = pa.parking_session_id
JOIN core.tariff_snapshots ts
  ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
LEFT JOIN payments.payment_rails pr
  ON pr.payment_rail_id = pa.payment_rail_id
WHERE pa.parking_session_id IN (
    '77000000-0000-0000-0000-000000000316'::uuid,
    '77000000-0000-0000-0000-000000000308'::uuid
)
ORDER BY pa.created_at DESC;
```

Expected after requests `86` and `88`: one payment-attempt row for the no-discount fixture and one row for the APPLIED RA 9994 fixture. The no-discount row uses tariff snapshot `77000000-0000-0000-0000-000000000398` and amount `125.00`. The APPLIED row uses the applied tariff snapshot captured by request `87` and amount `89.29`.

Verify no payment-attempt row was created for stale original tariff submissions:

```sql
SELECT COUNT(*) AS stale_payment_attempt_count
FROM core.payment_attempts
WHERE parking_session_id IN (
    '77000000-0000-0000-0000-000000000314'::uuid,
    '77000000-0000-0000-0000-000000000315'::uuid,
    '77000000-0000-0000-0000-000000000308'::uuid
)
  AND idempotency_key IN (
      'manual-payment-stale-original-rejected',
      'manual-payment-superseded-rejected',
      'manual-payment-session-mismatch-rejected'
  );
```

Expected result after requests `89`, `92`, and `93` is `0`.

Verify payment attempt replay did not create a duplicate row:

```sql
SELECT
    idempotency_key,
    COUNT(*) AS payment_attempt_count,
    MIN(payment_attempt_id) AS payment_attempt_id,
    MIN(tariff_snapshot_id) AS tariff_snapshot_id,
    MIN(amount) AS amount
FROM core.payment_attempts
WHERE idempotency_key = 'manual-payment-applied-effective'
GROUP BY idempotency_key;
```

Expected result after requests `88`, `90`, and `91`: `payment_attempt_count = 1`, the tariff snapshot is the APPLIED tariff snapshot, and `amount = 89.29`.

Verify the original/applied tariff lifecycle after #205 payment creation:

```sql
SELECT
    original.tariff_snapshot_id AS original_tariff_snapshot_id,
    original.snapshot_status AS original_status,
    original.net_amount AS original_net_amount,
    applied.tariff_snapshot_id AS applied_tariff_snapshot_id,
    applied.snapshot_status AS applied_status,
    applied.net_amount AS applied_net_amount,
    pa.payment_attempt_id,
    pa.amount AS payment_amount
FROM discounts.statutory_discount_payable_basis_applications app
JOIN core.tariff_snapshots original
  ON original.tariff_snapshot_id = app.original_tariff_snapshot_id
JOIN core.tariff_snapshots applied
  ON applied.tariff_snapshot_id = app.applied_tariff_snapshot_id
LEFT JOIN core.payment_attempts pa
  ON pa.tariff_snapshot_id = app.applied_tariff_snapshot_id
WHERE app.statutory_discount_validation_id = '77000000-0000-0000-0000-000000000316'::uuid;
```

Expected after request `88`: original snapshot is `SUPERSEDED`, applied snapshot is `CONSUMED`, the original net amount remains `125.00`, the applied net amount remains `89.29`, and the payment amount is `89.29`.

## Payment Finality Effective APPLIED Tariff Snapshot

Issue #207 extends the manual smoke suite for #206 payment finality. These requests prove confirmation and finalization keep using the tariff snapshot, amount, and currency stored on `core.payment_attempts`; they do not re-resolve the payable basis and they do not drift back to the original superseded tariff snapshot.

Endpoints:

```http
POST /v1/vendor-parking/resolve
POST /v1/public/payment-attempts
POST /v1/internal/payments/confirmation
POST /v1/internal/payment-attempts/{paymentAttemptId}/finalize
POST /v1/internal/payments/outcome
```

The direct confirmation and direct finalization endpoints are the primary #207 manual path. The internal payment outcome endpoint is listed because it exists in the current finality surface, but these smoke requests avoid it so the workflow does not issue exit authorization as a side effect.

Preconditions:

- #206 is merged.
- Central PMS is running.
- The local PostgreSQL database is available.
- The fixture seed above has been applied.
- APPLIED statutory discount fixture `MANUAL-PAYMENT-APPLIED-EFFECTIVE` exists.
- Payment attempt creation still accepts the effective APPLIED tariff snapshot and consumes only that stored snapshot.

Manual workflow:

1. Run `94 Vendor resolve APPLIED basis for payment finality`.
2. Run `95 Create payment attempt effective APPLIED tariff for finality`.
3. Run `96 Record payment confirmation effective APPLIED tariff`.
4. Run `97 Finalize payment attempt effective APPLIED tariff`.
5. Run `100 Record payment confirmation replay`.
6. Run `101 Finalize payment attempt replay`.
7. Run the read-only DB verification queries below.

Expected happy-path behavior:

- Request `94` returns `statutoryDiscountApplied = true`.
- Request `94` returns `effectiveTariffSnapshotId = appliedTariffSnapshotId = 77000000-0000-0000-0000-000000000506`.
- Request `95` creates or reuses a payment attempt with the effective APPLIED tariff snapshot.
- Request `96` records confirmation for the payment attempt amount `89.29 PHP`.
- Request `97` returns `attemptStatus = CONFIRMED`.
- Requests `100` and `101` return the existing confirmation/final state without duplicate finality rows.
- The original tariff snapshot `77000000-0000-0000-0000-000000000406` remains `SUPERSEDED`.
- The applied tariff snapshot may be `CONSUMED` after request `95`; confirmation must still succeed because it is the snapshot stored on the payment attempt.

Negative mismatch workflows:

Run each mismatch case from a clean fixture reset, then run requests `94` and `95` before the mismatch request. Do not run request `96` first for these negative cases, because a valid confirmation intentionally makes the payment attempt terminal and replay-safe.

- Run `98 Record payment confirmation amount mismatch rejected` after `94` and `95`.
  - Expected: HTTP `409`, `errorCode = PAYMENT_AMOUNT_MISMATCH`, and no confirmation row is created for `manual-paymongo-finality-amount-mismatch`.
- Run `99 Record payment confirmation currency mismatch rejected` after a fresh fixture reset plus `94` and `95`.
  - Expected: HTTP `409`, `errorCode = PAYMENT_CURRENCY_MISMATCH`, and no confirmation row is created for `manual-paymongo-finality-currency-mismatch`.

No-discount baseline:

- After a fixture reset, run `103 No discount payment finality baseline`.
- Run `104 Record no-discount payment confirmation baseline`.
- Run `105 Finalize no-discount payment attempt baseline`.
- Expected: the active no-discount tariff snapshot `77000000-0000-0000-0000-000000000398` remains the payment attempt payable basis and amount/currency are `125.00 PHP`.

### Payment Finality DB Verification

Verify the APPLIED finality attempt remains tied to the applied tariff snapshot and amount:

```sql
SELECT
    pa.payment_attempt_id,
    pa.parking_session_id,
    ps.ticket_number_masked,
    pa.tariff_snapshot_id AS attempt_tariff_snapshot_id,
    pa.amount AS attempt_amount,
    pa.currency_code AS attempt_currency,
    pa.attempt_status,
    pa.finalized_at,
    ts.snapshot_status AS attempt_snapshot_status,
    ts.net_amount AS attempt_snapshot_net_amount,
    app.original_tariff_snapshot_id,
    original.snapshot_status AS original_snapshot_status,
    app.applied_tariff_snapshot_id
FROM core.payment_attempts pa
JOIN core.parking_sessions ps
  ON ps.parking_session_id = pa.parking_session_id
JOIN core.tariff_snapshots ts
  ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
JOIN discounts.statutory_discount_payable_basis_applications app
  ON app.applied_tariff_snapshot_id = pa.tariff_snapshot_id
JOIN core.tariff_snapshots original
  ON original.tariff_snapshot_id = app.original_tariff_snapshot_id
WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid
ORDER BY pa.created_at DESC;
```

Expected after requests `94` through `97`: `attempt_tariff_snapshot_id = 77000000-0000-0000-0000-000000000506`, `attempt_amount = 89.29`, `attempt_currency = PHP`, `attempt_status = CONFIRMED`, `finalized_at IS NOT NULL`, `original_snapshot_status = SUPERSEDED`, and `original_tariff_snapshot_id = 77000000-0000-0000-0000-000000000406`.

Verify confirmation amount/currency/provider reference:

```sql
SELECT
    pc.payment_confirmation_id,
    pc.payment_attempt_id,
    pc.provider_transaction_ref,
    pc.confirmed_amount,
    pc.currency_code,
    pc.confirmation_status,
    pc.verified_at,
    pc.confirmed_at
FROM core.payment_confirmations pc
JOIN core.payment_attempts pa
  ON pa.payment_attempt_id = pc.payment_attempt_id
WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid
ORDER BY pc.created_at DESC;
```

Expected after request `96`: one row with provider reference `manual-paymongo-finality-applied-effective`, `confirmed_amount = 89.29`, `currency_code = PHP`, and `confirmation_status = RECORDED`.

Verify confirmation replay did not create a duplicate row:

```sql
SELECT
    pc.payment_attempt_id,
    pc.provider_transaction_ref,
    COUNT(*) AS confirmation_count
FROM core.payment_confirmations pc
JOIN core.payment_attempts pa
  ON pa.payment_attempt_id = pc.payment_attempt_id
WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid
GROUP BY pc.payment_attempt_id, pc.provider_transaction_ref;
```

Expected after requests `96` and `100`: one grouped row for `manual-paymongo-finality-applied-effective` with `confirmation_count = 1`.

Verify amount/currency mismatch requests left no invalid confirmation row:

```sql
SELECT
    pc.provider_transaction_ref,
    COUNT(*) AS invalid_confirmation_count
FROM core.payment_confirmations pc
WHERE pc.provider_transaction_ref IN (
    'manual-paymongo-finality-amount-mismatch',
    'manual-paymongo-finality-currency-mismatch'
)
GROUP BY pc.provider_transaction_ref;
```

Expected after rejected requests `98` or `99`: no rows.

Verify no provider routing/AUB drift is visible on the APPLIED finality payment attempt:

```sql
SELECT
    pa.payment_attempt_id,
    pr.provider_code,
    pr.rail_code
FROM core.payment_attempts pa
LEFT JOIN payments.payment_rails pr
  ON pr.payment_rail_id = pa.payment_rail_id
WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid
ORDER BY pa.created_at DESC;
```

Expected: `provider_code` remains the existing PayMongo provider for the selected `GCASH` path. No AUB rail should appear.

Verify this direct confirmation/finalization workflow did not create gate, coupon, reconciliation, or exit-authorization rows:

```sql
SELECT
    (SELECT COUNT(*)
       FROM core.exit_authorizations ea
      WHERE ea.payment_attempt_id IN (
          SELECT payment_attempt_id
          FROM core.payment_attempts
          WHERE parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid
      )) AS exit_authorization_count,
    (SELECT COUNT(*)
       FROM coupons.coupon_applications ca
      WHERE ca.parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid) AS coupon_application_count,
    (SELECT COUNT(*)
       FROM reconciliation.reconciliation_items ri
      WHERE ri.payment_attempt_id IN (
          SELECT payment_attempt_id
          FROM core.payment_attempts
          WHERE parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid
      )) AS reconciliation_item_count,
    (SELECT COUNT(*)
       FROM gates.gate_authorization_consumptions gac
      WHERE gac.exit_authorization_id IN (
          SELECT ea.exit_authorization_id
          FROM core.exit_authorizations ea
          JOIN core.payment_attempts pa
            ON pa.payment_attempt_id = ea.payment_attempt_id
          WHERE pa.parking_session_id = '77000000-0000-0000-0000-000000000316'::uuid
      )) AS gate_consumption_count;
```

Expected for requests `94` through `101`: all counts are `0`. If the broader payment outcome endpoint is used manually instead of direct confirmation/finalization, exit authorization may be created by existing finality behavior and should be evaluated separately.

Scope boundary:

- These requests do not test real PayMongo callback delivery or external provider settlement.
- These requests do not call AUB, change provider routing, call gate integration, consume exits, create coupons, create reconciliation records, or render UI.
- These requests do not test WebPay or Operator Console UI layout.
- The confirmation/finality contracts do not include `tariffSnapshotId`; #207 validates that the stored `payment_attempts.tariff_snapshot_id` is the source of truth.

Verify the payment-attempt guardrail case leaves the application and original snapshot unfinalized:

```sql
SELECT
    app.application_status,
    app.applied_tariff_snapshot_id,
    ts.snapshot_status AS original_snapshot_status,
    ts.gross_amount,
    ts.statutory_discount_amount,
    ts.net_amount,
    (SELECT COUNT(*) FROM core.payment_attempts
      WHERE parking_session_id = app.parking_session_id
         OR tariff_snapshot_id = app.original_tariff_snapshot_id) AS payment_attempt_count
FROM discounts.statutory_discount_payable_basis_applications app
JOIN core.tariff_snapshots ts
  ON ts.tariff_snapshot_id = app.original_tariff_snapshot_id
WHERE app.statutory_discount_validation_id = '77000000-0000-0000-0000-000000000313'::uuid;
```

Expected result: `application_status = REQUESTED`, `applied_tariff_snapshot_id IS NULL`, `original_snapshot_status = ACTIVE`, and `payment_attempt_count > 0`.

Verify the persisted policy context inside `computation_basis_json`:

```sql
SELECT
    statutory_discount_validation_id,
    computation_basis_json -> 'policyContext' AS policy_context,
    computation_basis_json -> 'policyContext' ->> 'statutoryDiscountPolicyId' AS policy_id,
    computation_basis_json -> 'policyContext' ->> 'policyCode' AS policy_code,
    computation_basis_json -> 'policyContext' ->> 'policyResolutionBasis' AS policy_resolution_basis,
    computation_basis_json -> 'policyContext' ->> 'benefitType' AS benefit_type,
    computation_basis_json -> 'policyContext' ->> 'nationalLawReference' AS national_law_reference,
    computation_basis_json -> 'policyContext' ->> 'ordinanceReference' AS ordinance_reference
FROM discounts.statutory_discount_payable_basis_applications
WHERE statutory_discount_validation_id = '<validationId>'::uuid;
```

Expected: policy context is present and matches the validation's persisted policy snapshot. For RA 9994, expect `national_law_reference = RA 9994`; for RA 10754, expect `national_law_reference = RA 10754`.

Verify no duplicate payable-basis application rows exist for a validation after replay:

```sql
SELECT COUNT(*) AS payable_basis_application_count
FROM discounts.statutory_discount_payable_basis_applications
WHERE statutory_discount_validation_id = '<applyValidationId>'::uuid;
```

Expected result is `1`.

Verify blocked persisted-policy apply fixtures did not create application rows:

```sql
SELECT
    statutory_discount_validation_id,
    COUNT(*) AS payable_basis_application_count
FROM discounts.statutory_discount_payable_basis_applications
WHERE statutory_discount_validation_id IN (
    '77000000-0000-0000-0000-000000000308'::uuid,
    '77000000-0000-0000-0000-000000000309'::uuid,
    '77000000-0000-0000-0000-000000000310'::uuid,
    '77000000-0000-0000-0000-000000000311'::uuid
)
GROUP BY statutory_discount_validation_id;
```

Expected result is no rows after requests `60` through `63`.

Verify apply persisted-policy validation fixtures:

```sql
SELECT
    statutory_discount_validation_id,
    parking_session_id,
    tariff_snapshot_id,
    entitlement_type,
    validation_status,
    evidence_required,
    evidence_captured,
    statutory_discount_policy_id,
    resolved_jurisdiction_id,
    policy_resolution_basis,
    resolved_policy_snapshot_json ->> 'statutoryDiscountPolicyId' AS snapshot_policy_id,
    resolved_policy_snapshot_json ->> 'policyCode' AS snapshot_policy_code,
    resolved_policy_snapshot_json ->> 'benefitType' AS snapshot_benefit_type,
    resolved_policy_snapshot_json ->> 'nationalLawReference' AS snapshot_national_law_reference,
    resolved_policy_snapshot_json ->> 'ordinanceReference' AS snapshot_ordinance_reference
FROM discounts.statutory_discount_validations
WHERE statutory_discount_validation_id IN (
    '77000000-0000-0000-0000-000000000306'::uuid,
    '77000000-0000-0000-0000-000000000307'::uuid,
    '77000000-0000-0000-0000-000000000308'::uuid,
    '77000000-0000-0000-0000-000000000309'::uuid,
    '77000000-0000-0000-0000-000000000310'::uuid,
    '77000000-0000-0000-0000-000000000311'::uuid,
    '77000000-0000-0000-0000-000000000312'::uuid,
    '77000000-0000-0000-0000-000000000313'::uuid
);
```

Expected: `306` and `307` are approved national fallback fixtures with captured evidence and valid persisted policy snapshots; `308` lacks policy context; `309` has an invalid snapshot policy ID; `310` has a mismatched snapshot policy ID; `311` is a `FREE_DURATION` local policy fixture; `312` is a seeded `REQUESTED` application completion fixture; `313` is a seeded payment-attempt guardrail fixture; `314`, `315`, and `316` are #205 payment-attempt effective-tariff fixtures.

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

Expected values after fixture reset and before successful #200 apply:

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
- WebPay display
- payment attempt creation by the apply endpoint
- payment confirmation/finality
- provider handoff execution or provider callbacks
- gate consume
- coupons
- evidence upload
- raw evidence storage
- entitlement fingerprinting
- reconciliation
- AUB
- vendor PMS integration
