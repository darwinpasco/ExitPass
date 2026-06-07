# Operator Console Statutory Discount Validation Runbook v1.0

Aligned to ExitPass v1.2 and Operator Console slices #229, #230, #231, and #232.

## 1. Purpose

This runbook validates the Operator Console statutory discount workflow from parking session lookup through policy resolution, statutory discount draft creation, metadata-only evidence capture, approval decision, payable-basis application, and final read model verification.

Use it for controlled sandbox validation and production pilot readiness. It is an operational guide, not a feature specification.

## 2. Scope

In scope:

- Operator Console session lookup.
- Statutory discount policy resolution.
- Statutory discount draft creation.
- Evidence-required gating.
- Metadata-only evidence capture.
- Approval or rejection decision.
- Approved statutory discount payable-basis application.
- Final statutory discount read model verification.
- Operator access, identity, RBAC, and access evaluation checks.
- Audit/access evaluation verification where available.

Out of scope:

- WebPay payment collection.
- Payment provider routing or payment finalization.
- AUB selection, configuration, routing, or invocation.
- Coupon validation.
- Reconciliation.
- HikCentral or gate behavior.
- OCR, automated ID validation, raw file storage, or document verification.
- Dashboards and reports.

## 3. Roles And Responsibilities

| Role | Responsibility |
| --- | --- |
| Site Operator | Performs session lookup, captures metadata-only evidence, submits the validation decision, and records the correlation ID. |
| Site Supervisor | Confirms operator readiness, handles escalations, and signs off on manual exception handling. |
| QA/Test Operator | Executes sandbox test cases and records pass/fail evidence. |
| Product/Business Owner | Confirms the statutory discount workflow matches business policy and pilot expectations. |
| Technical Observer / Backend Engineer | Observes API behavior, verifies logs/database read models, and investigates backend issues. |
| Compliance Observer | Optional reviewer for privacy, evidence minimization, and audit expectations. |

## 4. Preconditions

Do not start unless all preconditions are true:

- Central PMS API is running and reachable.
- PostgreSQL integration or sandbox database is available.
- Operator Console backend endpoints are deployed and reachable.
- #229 evidence intake, #230 RBAC/operator identity hardening, #231 E2E validation, and #232 production-readiness cleanup are merged into `dev`.
- Test site group and site exist.
- Operator identity, device binding, site assignment, and active shift context exist.
- Parking session exists, or a test fixture is seeded.
- A statutory discount policy requiring evidence exists for the target site jurisdiction or fallback path.
- No production payment provider invocation is needed for this validation.
- The validation window, operator, supervisor, and observer are recorded outside the system.

## 5. Required Sandbox/Test Values

Use environment-specific sandbox values. Do not paste production credentials, full ID numbers, or real customer-sensitive identifiers into tickets, screenshots, logs, docs, or chat.

| Value | Placeholder | Example or note |
| --- | --- | --- |
| Base API URL | `<base-api-url>` | `https://central-pms-sandbox.example` |
| Operator user ID | `<operator-user-id>` | Operator Console user GUID. |
| Operator device binding ID | `<operator-device-binding-id>` | Registered operator device binding GUID. |
| Operator shift ID | `<operator-shift-id>` | Active shift GUID for the operator. |
| Site ID | `<site-id>` | Site GUID under validation. |
| Site group ID | `<site-group-id>` | Site group GUID under validation. |
| Correlation ID | `<correlation-id>` | Generate a new GUID per validation run. |
| Ticket reference | `<ticket-reference>` | Parking session ticket/reference, masked where required. |
| Parking session ID | `<parking-session-id>` | Use when lookup mode is `PARKING_SESSION_ID`. |
| Original tariff snapshot ID | `<original-tariff-snapshot-id>` | Current active tariff snapshot before apply. |
| Entitlement type | `SENIOR_CITIZEN` | Also supports `PWD` where policy exists. |
| Required evidence type | `SENIOR_CITIZEN_ID` | Use `PWD_ID` for PWD flow. |
| Capture method | `OPERATOR_CONFIRMED` | `MANUAL_REFERENCE` is also supported when policy allows. |

The #231 integration test uses deterministic test-only fixture values. These are not production IDs:

| Fixture value | Test-only value |
| --- | --- |
| Operator user ID | `77000000-0000-0000-0000-000000000010` |
| Operator device binding ID | `77000000-0000-0000-0000-000000000030` |
| Operator shift ID | `77000000-0000-0000-0000-000000000050` |
| Site group ID | `77000000-0000-0000-0000-000000000001` |
| Site ID | `77000000-0000-0000-0000-000000000002` |
| Policy ID | `23100000-0000-0000-0000-000000000002` |
| Parking session ID | `23100000-0000-0000-0000-000000000003` |
| Original tariff snapshot ID | `23100000-0000-0000-0000-000000000004` |
| Ticket reference | `E2E-231-SESSION-001` |
| Entitlement/evidence | `SENIOR_CITIZEN` / `SENIOR_CITIZEN_ID` |

Operator Console identity may be supplied by authenticated claims and, in local/sandbox validation, by these headers:

```http
X-Operator-User-Id: <operator-user-id>
X-Operator-Device-Binding-Id: <operator-device-binding-id>
X-Operator-Shift-Id: <operator-shift-id>
X-Site-Id: <site-id>
X-Site-Group-Id: <site-group-id>
X-Correlation-Id: <correlation-id>
```

## 6. Manual Validation Sequence

Use one new `<correlation-id>` for the run and keep it in every request.

### A. Start Validation Session

1. Confirm API health using the environment's standard Central PMS health endpoint.
2. Confirm the operator context has a mapped user, active device binding, site assignment, and active shift.
3. Generate a new correlation ID.
4. Confirm no WebPay payment, payment provider, AUB, coupon, reconciliation, HikCentral, or gate step is part of the run.

Optional access pre-check:

```http
POST /v1/ops/operator-console/access/evaluate
```

Expected result: `allowed = true`, `decision = ALLOWED`, and `persisted = true` for the requested action.

### B. Session Lookup

Endpoint:

```http
POST /v1/ops/operator-console/sessions/lookup
```

Request shape:

```json
{
  "userId": "<operator-user-id>",
  "operatorDeviceBindingId": "<operator-device-binding-id>",
  "operatorShiftId": "<operator-shift-id>",
  "siteId": "<site-id>",
  "siteGroupId": "<site-group-id>",
  "parkingSessionId": "<parking-session-id>",
  "ticketReference": "<ticket-reference>",
  "lookupMode": "PARKING_SESSION_ID",
  "idempotencyKey": "operator-console-validation-<correlation-id>-session-lookup",
  "correlationId": "<correlation-id>"
}
```

Expected result:

- `accessAllowed = true`.
- `sessionFound = true`.
- `sessionEligible = true`.
- The resolved session has the expected site, site group, ticket reference, payable amount, and currency.

Failure handling:

- `SESSION_NOT_FOUND`: confirm the ticket/session reference and fixture state.
- `SESSION_NOT_ACTIVE`: do not proceed; select an active session.
- Access denied or denial reasons: stop and escalate to the supervisor/backend observer.

### C. Policy Resolution

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/resolve-policy
```

Request shape:

```json
{
  "userId": "<operator-user-id>",
  "operatorDeviceBindingId": "<operator-device-binding-id>",
  "operatorShiftId": "<operator-shift-id>",
  "siteId": "<site-id>",
  "siteGroupId": "<site-group-id>",
  "parkingSessionId": "<parking-session-id>",
  "entitlementType": "SENIOR_CITIZEN",
  "idempotencyKey": "operator-console-validation-<correlation-id>-policy",
  "correlationId": "<correlation-id>"
}
```

Expected result:

- `accessAllowed = true`.
- `policyResolved = true`.
- `entitlementType = SENIOR_CITIZEN`.
- `requiresOperatorValidation = true`.
- `requiresEvidence = true`.
- Policy resolution is a verified local policy or allowed national fallback.

### D. Create Statutory Discount Draft

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/draft
```

Request shape:

```json
{
  "userId": "<operator-user-id>",
  "operatorDeviceBindingId": "<operator-device-binding-id>",
  "operatorShiftId": "<operator-shift-id>",
  "siteId": "<site-id>",
  "siteGroupId": "<site-group-id>",
  "parkingSessionId": "<parking-session-id>",
  "ticketReference": "<ticket-reference>",
  "plateNumber": null,
  "entitlementType": "SENIOR_CITIZEN",
  "idDocumentType": "SENIOR_CITIZEN_ID",
  "issuingAuthority": "SANDBOX",
  "expiryDate": null,
  "maskedIdReference": null,
  "entitlementFingerprint": null,
  "evidenceCaptureRequested": true,
  "evidenceAccessIntent": "OPERATOR_VALIDATION",
  "operatorAttestation": true,
  "attestationNotes": "Controlled operator validation run.",
  "reasonCode": "MANUAL_VALIDATION_RUN",
  "idempotencyKey": "operator-console-validation-<correlation-id>-draft",
  "correlationId": "<correlation-id>"
}
```

Expected result:

- `accessAllowed = true`.
- `draftAccepted = true`.
- `draftPersisted = true`.
- `validationStatus = REQUESTED` or equivalent draft state.
- `evidenceRequired = true`.
- `evidenceCaptureRequired = true`.
- `draftId` is populated. Use this as `<draft-id>` for later steps.

Confirm the draft detail:

```http
GET /v1/ops/operator-console/statutory-discounts/drafts/<draft-id>?correlationId=<correlation-id>
```

Expected read model:

- `evidenceRequired = true`.
- `evidenceRequiredSatisfied = false`.
- `requiredEvidenceTypes` contains `SENIOR_CITIZEN_ID`.

### E. Confirm Approval Is Blocked Before Evidence

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/<draft-id>/decision
```

Request shape:

```json
{
  "userId": "<operator-user-id>",
  "operatorDeviceBindingId": "<operator-device-binding-id>",
  "operatorShiftId": "<operator-shift-id>",
  "siteId": "<site-id>",
  "siteGroupId": "<site-group-id>",
  "decision": "APPROVE",
  "decisionReasonCode": "MANUAL_VALIDATION_APPROVE",
  "decisionNotes": "Attempt before evidence to verify gating.",
  "idempotencyKey": "operator-console-validation-<correlation-id>-approve-before-evidence",
  "correlationId": "<correlation-id>"
}
```

Expected result:

- Response is deterministic and operator-safe.
- `errorCode = EVIDENCE_REQUIRED_NOT_CAPTURED`.
- The draft is not approved.
- Draft detail still shows evidence unsatisfied.

### F. Capture Metadata-Only Evidence

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/<draft-id>/evidence
```

Preferred request for this run:

```json
{
  "userId": "<operator-user-id>",
  "operatorDeviceBindingId": "<operator-device-binding-id>",
  "operatorShiftId": "<operator-shift-id>",
  "siteId": "<site-id>",
  "siteGroupId": "<site-group-id>",
  "evidenceType": "SENIOR_CITIZEN_ID",
  "captureMethod": "OPERATOR_CONFIRMED",
  "fileName": null,
  "contentType": null,
  "sizeBytes": null,
  "storageReference": null,
  "referenceNumber": null,
  "notes": "Metadata-only operator confirmation.",
  "operatorConfirmation": true,
  "idempotencyKey": "operator-console-validation-<correlation-id>-evidence",
  "correlationId": "<correlation-id>"
}
```

Expected result:

- Evidence is captured.
- `evidenceRequiredSatisfied = true`.
- `storageReference = operator-confirmed` for operator-confirmed capture.
- No raw evidence bytes are uploaded.
- No OCR or automated ID verification is performed.
- No full ID number is returned.

If `MANUAL_REFERENCE` is used, enter only the minimum authorized reference. Expected behavior is that returned reference data is masked. If masking is not visible in the response, stop and ask the backend observer to verify storage and response behavior before continuing.

### G. Confirm Evidence List And Draft Read Model

Evidence list endpoint:

```http
GET /v1/ops/operator-console/statutory-discounts/<draft-id>/evidence?correlationId=<correlation-id>
```

Expected evidence list:

- `evidenceRequired = true`.
- `evidenceRequiredSatisfied = true`.
- `evidenceCount >= 1`.
- `latestEvidenceStatus = CAPTURED`.
- `requiredEvidenceTypes` contains `SENIOR_CITIZEN_ID`.
- Evidence items do not return raw ID numbers or raw evidence bytes.

Draft detail endpoint:

```http
GET /v1/ops/operator-console/statutory-discounts/drafts/<draft-id>?correlationId=<correlation-id>
```

Expected draft detail:

- Evidence is satisfied.
- Latest evidence status is `CAPTURED` or the current equivalent status.
- Validation is not yet approved until the decision step completes.

### H. Approve Statutory Discount

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/<draft-id>/decision
```

Request shape:

```json
{
  "userId": "<operator-user-id>",
  "operatorDeviceBindingId": "<operator-device-binding-id>",
  "operatorShiftId": "<operator-shift-id>",
  "siteId": "<site-id>",
  "siteGroupId": "<site-group-id>",
  "decision": "APPROVE",
  "decisionReasonCode": "MANUAL_VALIDATION_APPROVE",
  "decisionNotes": "Controlled validation approval after evidence capture.",
  "idempotencyKey": "operator-console-validation-<correlation-id>-approve",
  "correlationId": "<correlation-id>"
}
```

Expected result:

- `accessAllowed = true`.
- `decisionAccepted = true`.
- `decisionPersisted = true`.
- `currentValidationStatus = APPROVED`.
- Access evaluation for `DECIDE_STATUTORY_DISCOUNT` is persisted.

### I. Apply Payable Basis

Endpoint:

```http
POST /v1/ops/operator-console/statutory-discounts/<draft-id>/apply-payable-basis
```

Request shape:

```json
{
  "userId": "<operator-user-id>",
  "operatorDeviceBindingId": "<operator-device-binding-id>",
  "operatorShiftId": "<operator-shift-id>",
  "siteId": "<site-id>",
  "siteGroupId": "<site-group-id>",
  "originalTariffSnapshotId": "<original-tariff-snapshot-id>",
  "idempotencyKey": "operator-console-validation-<correlation-id>-apply",
  "correlationId": "<correlation-id>"
}
```

Expected result:

- `accessAllowed = true`.
- `applicationAccepted = true`.
- `applicationPersisted = true`.
- `applicationStatus = APPLIED`.
- `appliedTariffSnapshotId` is populated.
- Final amount fields are populated where policy supports payable-basis application.
- No payment attempt, payment confirmation, provider call, AUB routing, exit authorization, gate action, coupon application, or reconciliation record is created by this step.

### J. Final Verification

Call draft detail:

```http
GET /v1/ops/operator-console/statutory-discounts/drafts/<draft-id>?correlationId=<correlation-id>
```

Expected final read model:

- `validationStatus = APPROVED`.
- `evidenceRequired = true`.
- `evidenceRequiredSatisfied = true`.
- `evidenceCount >= 1`.
- `latestEvidenceStatus = CAPTURED`.
- `payableBasisApplicationStatus = APPLIED`.
- Applied tariff/payable-basis fields are reflected where available.

## 7. Negative Validation Scenarios

Run these in sandbox only:

| Scenario | Action | Expected result |
| --- | --- | --- |
| Missing operator user ID | Omit `X-Operator-User-Id` and `userId` fallback. | Bad request with operator-safe message that operator user identity is required. |
| Access denied operator context | Use inactive/unassigned device or no active shift. | Access denied response or `accessAllowed = false`; no controlled mutation. |
| Approval before evidence | Submit `APPROVE` before evidence capture. | `EVIDENCE_REQUIRED_NOT_CAPTURED`; status remains unapproved. |
| Wrong evidence type | Submit `PWD_ID` for `SENIOR_CITIZEN` policy. | `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST`; evidence requirement remains unsatisfied. |
| Apply before approval | Apply payable basis before approved decision. | `STATUTORY_DISCOUNT_NOT_APPROVED`; no application persisted. |
| Invalid/missing draft | Use an unknown draft GUID. | `STATUTORY_DISCOUNT_DRAFT_NOT_FOUND`, `DRAFT_NOT_FOUND`, or `STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND` based on endpoint. |
| Policy unavailable or unverified | Use a site with no configured/verified policy. | Policy resolution returns an error such as `SITE_JURISDICTION_NOT_CONFIGURED`, `STATUTORY_DISCOUNT_POLICY_UNVERIFIED`, or fallback unavailable. |
| Manual reference masking | Capture with `MANUAL_REFERENCE` in sandbox. | Raw reference number is not returned; returned reference is masked. |

## 8. Expected API Outcomes And Error Codes

Observed current Operator Console outcomes include:

| Code or outcome | Meaning |
| --- | --- |
| `ALLOWED` | Access evaluation permitted the controlled action. |
| `DENIED` | Access evaluation denied the controlled action. |
| `OPERATOR_CONSOLE_ACCESS_DENIED` | Endpoint-level forbidden result for a read/list action after denied access. |
| `ACCESS_DENIED` | Service-level ineligibility or error code for denied access. |
| `INVALID_OPERATOR_ACCESS_EVALUATION_REQUEST` | Access evaluation request or identity context is invalid. |
| `INVALID_OPERATOR_CONSOLE_SESSION_LOOKUP_REQUEST` | Session lookup request is invalid. |
| `SESSION_NOT_FOUND` | No matching parking session was resolved. |
| `SESSION_NOT_ACTIVE` | Session exists but is not eligible for the operator workflow. |
| `INVALID_OPERATOR_CONSOLE_POLICY_RESOLUTION_REQUEST` | Policy resolution request is invalid. |
| `SITE_NOT_FOUND` | Policy/session context references an unknown site. |
| `SITE_JURISDICTION_NOT_CONFIGURED` | Site has no jurisdiction configured for policy resolution. |
| `NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED` | Required fallback policy is unavailable. |
| `STATUTORY_DISCOUNT_POLICY_UNVERIFIED` | Policy exists but is not verified for use. |
| `STATUTORY_DISCOUNT_POLICY_NOT_RESOLVED` | Draft could not continue because policy was unresolved. |
| `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DRAFT_REQUEST` | Draft request is invalid. |
| `STATUTORY_DISCOUNT_DRAFT_ALREADY_EXISTS` | Duplicate draft conflict for the same request context. |
| `STATUTORY_DISCOUNT_DRAFT_NOT_FOUND` | Draft detail/evidence endpoint did not find the draft. |
| `DRAFT_NOT_FOUND` | Decision service did not find the draft. |
| `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST` | Evidence capture/list request is invalid, including wrong evidence type. |
| `EVIDENCE_REQUIRED_NOT_CAPTURED` | Approval is blocked because required evidence has not been captured. |
| `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_REQUEST` | Decision request is invalid. |
| `STATUTORY_DISCOUNT_DRAFT_ALREADY_DECIDED` | Decision conflicts with an already-decided draft. |
| `INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_APPLY_PAYABLE_BASIS_REQUEST` | Apply request is invalid. |
| `STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND` | Apply endpoint did not find the validation. |
| `STATUTORY_DISCOUNT_NOT_APPROVED` | Apply is blocked until the validation is approved. |
| `STATUTORY_DISCOUNT_POLICY_CONTEXT_MISSING` | Apply could not find required policy context. |
| `STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID` | Apply could not use the persisted policy snapshot. |
| `POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION` | Policy benefit type is unsupported by the apply endpoint. |
| `PAYMENT_ATTEMPT_ALREADY_EXISTS` | Apply is blocked because payment has already started for the session. |

Common access denial reasons include `HR_IDENTITY_MAPPING_NOT_FOUND`, `HR_IDENTITY_MAPPING_INACTIVE`, `DEVICE_BINDING_NOT_FOUND`, `DEVICE_BINDING_INACTIVE`, `DEVICE_NOT_TRUSTED`, `DEVICE_SITE_ASSIGNMENT_NOT_FOUND`, `DEVICE_SITE_ASSIGNMENT_INVALID`, `NO_ACTIVE_SHIFT`, `SHIFT_REVOKED`, `SHIFT_TAKEOVER_ACTIVE`, `WORKFLOW_NOT_SUPPORTED`, and `ACTION_NOT_SUPPORTED`.

## 9. Evidence Handling And Privacy Rules

- Evidence intake for this workflow is metadata-only.
- Do not upload raw ID images, raw documents, or raw evidence bytes.
- Do not perform OCR or automated ID validation.
- Do not store full ID numbers unless a separately approved policy explicitly requires it.
- Prefer `OPERATOR_CONFIRMED` for this validation run.
- If `MANUAL_REFERENCE` is used, returned reference values must be masked.
- Evidence list responses must not expose raw reference numbers.
- Evidence access should be auditable through Operator Console access evaluation and evidence metadata records.
- Operators should record only the minimum information needed to prove the manual validation outcome.

## 10. Pass/Fail Criteria

Pass criteria:

- Session lookup resolves the intended active session.
- Policy resolution returns a verified policy requiring evidence.
- Draft creation succeeds and shows evidence required.
- Approval is blocked before evidence.
- Wrong evidence type does not satisfy the requirement.
- Correct metadata-only evidence satisfies the requirement.
- Approval succeeds after evidence capture.
- Apply payable basis succeeds after approval.
- Final read model reflects approved status, evidence satisfied, latest evidence captured, and payable-basis application applied.
- No WebPay, payment provider, AUB, gate, coupon, reconciliation, or unrelated mutation occurs.

Fail criteria:

- Approval succeeds before required evidence.
- Wrong evidence type satisfies required evidence.
- Raw reference number or raw evidence is returned.
- Payment, provider, AUB, gate, coupon, or reconciliation state mutates.
- Endpoint returns non-deterministic or internal-error details for common validation failures.
- Access evaluation or audit evidence is missing where expected.

## 11. Reset/Rollback Guidance

Sandbox reset should follow existing integration-test cleanup conventions. Coordinate with a backend engineer before deleting rows.

Safe guidance:

- Identify validation records by `correlation_id`, deterministic test prefix, or known test-only GUID.
- Prefer creating a fresh parking session fixture for each run.
- Do not run destructive SQL in production.
- Use read-only verification SQL for troubleshooting.
- If cleanup is required, perform it only in sandbox with an approved cleanup script or explicit backend engineer supervision.

Read-only verification SQL examples:

```sql
SELECT
    parking_session_id,
    session_status::text,
    ticket_number_masked,
    site_id,
    site_group_id,
    correlation_id
FROM core.parking_sessions
WHERE parking_session_id = '<parking-session-id>';
```

```sql
SELECT
    tariff_snapshot_id,
    snapshot_status::text,
    gross_amount,
    statutory_discount_amount,
    coupon_discount_amount,
    net_amount,
    statutory_discount_validation_id,
    correlation_id
FROM core.tariff_snapshots
WHERE parking_session_id = '<parking-session-id>'
ORDER BY calculated_at, tariff_snapshot_id;
```

```sql
SELECT
    statutory_discount_validation_id,
    parking_session_id,
    tariff_snapshot_id,
    entitlement_type::text,
    validation_channel::text,
    validation_status::text,
    evidence_required,
    evidence_captured,
    statutory_discount_policy_id,
    resolved_jurisdiction_id,
    correlation_id
FROM discounts.statutory_discount_validations
WHERE statutory_discount_validation_id = '<draft-id>';
```

```sql
SELECT
    discount_evidence_reference_id,
    evidence_type::text,
    evidence_storage_type::text,
    evidence_storage_ref,
    evidence_capture_status::text,
    redaction_status::text,
    captured_at,
    captured_by_user_id,
    correlation_id
FROM discounts.discount_evidence_references
WHERE statutory_discount_validation_id = '<draft-id>'
  AND purged_at IS NULL
ORDER BY captured_at DESC;
```

```sql
SELECT
    statutory_discount_payable_basis_application_id,
    application_status::text,
    application_channel::text,
    original_tariff_snapshot_id,
    applied_tariff_snapshot_id,
    gross_amount_minor_units,
    statutory_discount_amount_minor_units,
    final_payable_amount_minor_units,
    currency_code,
    applied_at,
    correlation_id
FROM discounts.statutory_discount_payable_basis_applications
WHERE statutory_discount_validation_id = '<draft-id>';
```

```sql
SELECT
    e.operator_access_evaluation_id,
    e.requested_action,
    e.evaluation_status::text,
    e.operator_user_id,
    e.operator_device_binding_id,
    e.operator_shift_id,
    e.site_id,
    e.site_group_id,
    e.target_entity_type,
    e.target_entity_id,
    e.evaluated_at,
    e.audit_event_id,
    r.reason_code
FROM operator_console.operator_access_evaluations e
LEFT JOIN operator_console.operator_access_evaluation_reasons r
    ON r.operator_access_evaluation_id = e.operator_access_evaluation_id
WHERE e.correlation_id = '<correlation-id>'
ORDER BY e.evaluated_at, r.display_order;
```

## 12. Production Pilot Checklist

### A. Business Readiness

- [ ] Pilot site selected.
- [ ] LGU/statutory discount policy verified.
- [ ] Entitlement types for pilot confirmed.
- [ ] Operator roles assigned.
- [ ] Supervisor escalation path defined.
- [ ] Customer privacy notice/process confirmed.
- [ ] Manual exception handling process approved.

### B. Technical Readiness

- [ ] Central PMS API deployed.
- [ ] Required DB migrations applied.
- [ ] Operator identities available.
- [ ] HR identity mappings available.
- [ ] Device binding assumptions confirmed.
- [ ] Active shift assumptions confirmed.
- [ ] Site and site group scope configured.
- [ ] Policy registry contains verified policy/fallback.
- [ ] Logs and correlation IDs available.
- [ ] Monitoring enabled for access denied, evidence capture, approval, and apply outcomes.

### C. Operational Readiness

- [ ] Operators trained.
- [ ] Supervisors trained.
- [ ] Evidence capture rules explained.
- [ ] Operators understand not to enter full ID numbers unless policy explicitly authorizes it.
- [ ] Fallback process documented.
- [ ] Escalation path tested.
- [ ] Pilot support window scheduled.

### D. Compliance/Privacy Readiness

- [ ] Data minimization confirmed.
- [ ] Manual reference masking confirmed.
- [ ] No raw evidence storage unless separately approved.
- [ ] Retention rules understood.
- [ ] Evidence access audit process defined.
- [ ] Compliance sign-off recorded.

### E. Go/No-Go Gates

- [ ] Sandbox run passed.
- [ ] Approval-before-evidence negative check passed.
- [ ] Wrong-evidence-type negative check passed.
- [ ] Apply-before-approval negative check passed.
- [ ] Supervisor sign-off recorded.
- [ ] Compliance sign-off recorded.
- [ ] Product sign-off recorded.
- [ ] Production support contact assigned.
- [ ] Rollback/escalation procedure agreed.

### F. Pilot Success Metrics

- Number of statutory discount validations.
- Approval/rejection rate.
- Evidence capture error rate.
- Average handling time.
- Operator access denial rate.
- Exception count.
- Apply-payable-basis failure count.
- Supervisor escalation count.
- User/operator feedback.

## 13. Troubleshooting

| Symptom | Likely cause | Action |
| --- | --- | --- |
| 401/403 or access denied | Missing/invalid operator identity, inactive device, no shift, site mismatch. | Verify headers/claims, device binding, site assignment, active shift, and access denial reasons. |
| Bad request: operator user identity required | Missing `X-Operator-User-Id` and missing request `userId`. | Supply authenticated user claim or local/sandbox fallback GUID. |
| Session not found | Wrong ticket/session ID, inactive fixture, or wrong site context. | Re-check session lookup values and `core.parking_sessions`. |
| Policy not resolved | Site jurisdiction missing, policy unverified, fallback unavailable. | Verify site jurisdiction and `discounts.statutory_discount_policy_registry`. |
| Evidence still unsatisfied | Wrong evidence type or evidence capture failed. | Confirm required type from read model and recapture correct metadata-only evidence. |
| Approval blocked | Required evidence not captured. | Complete evidence capture and verify evidence list before approval. |
| Apply blocked | Draft is not approved, policy snapshot invalid, payment already started, or unsupported policy. | Verify decision state, policy snapshot, and payment boundary before retrying. |
| Raw reference concern | Manual reference may have been entered or returned unexpectedly. | Stop validation, preserve correlation ID, and ask backend/compliance observers to review response and storage. |

## 14. Pilot Feedback and Defect Logging

Use the pilot feedback artifacts for every failed step, operator confusion, privacy concern, control exception, or runbook mismatch observed during validation:

- Feedback log template: [OperatorConsole_Statutory_Discount_Pilot_Feedback_Log_Template.md](OperatorConsole_Statutory_Discount_Pilot_Feedback_Log_Template.md)
- Triage guide: [OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md](OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md)

Every failed runbook step or operator confusion must be logged with a feedback/defect ID, workflow step, issue type, severity, expected result, actual result, reproducibility, workaround, owner, and status.

Privacy and control issues must be escalated immediately. This includes raw ID number exposure, unexpected raw image/document storage, evidence visibility to an unauthorized operator, approval before required evidence, wrong evidence type satisfying entitlement, or any payment/provider/gate/coupon/reconciliation mutation.

Do not include production credentials, raw ID numbers, raw evidence images, unredacted screenshots, customer names, operator names, vehicle identifiers, or other personal data in feedback artifacts. Use masked values, sandbox values, correlation IDs, and redacted screenshots only.

## 15. Related Tests And Proof Points

Current backend proof points:

- `OperatorConsoleStatutoryDiscountE2EIntegrationTests.cs`
- `OperatorConsoleStatutoryDiscountEvidenceApiIntegrationTests.cs`
- `OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests.cs`
- `OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests.cs`
- `OperatorConsoleStatutoryDiscountReadApiIntegrationTests.cs`
- `OperatorConsoleStatutoryDiscountDraftApiIntegrationTests.cs`
- `OperatorConsoleStatutoryDiscountPolicyResolutionApiIntegrationTests.cs`
- `OperatorConsoleSessionLookupApiIntegrationTests.cs`
- `OperatorConsoleAccessEvaluationApiIntegrationTests.cs`
- `OperatorConsoleAccessEvaluationPersistenceIntegrationTests.cs`
- `OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests.cs`

The controlled E2E proof verifies:

- Session lookup through the real API endpoint.
- Policy resolution requiring evidence.
- Draft creation for `SENIOR_CITIZEN`.
- Approval blocked before evidence with `EVIDENCE_REQUIRED_NOT_CAPTURED`.
- Apply blocked before approval with `STATUTORY_DISCOUNT_NOT_APPROVED`.
- Wrong evidence type `PWD_ID` rejected for a senior citizen policy.
- Correct metadata-only `SENIOR_CITIZEN_ID` evidence satisfies gating.
- Approval succeeds after evidence.
- Payable-basis application succeeds after approval.
- Final read model shows evidence satisfied, latest evidence `CAPTURED`, approved validation, and application `APPLIED`.

## 16. Revision Notes

- v1.0: Initial manual operator validation runbook and production pilot checklist for ExitPass v1.2 Operator Console statutory discount validation.
- Aligned to Operator Console #229 evidence intake, #230 RBAC/operator identity hardening, #231 controlled E2E validation session, and #232 production-readiness cleanup.
- This document introduces no code behavior, production seed data, migrations, provider routing, payment behavior, AUB behavior, WebPay UI behavior, coupon behavior, reconciliation behavior, HikCentral behavior, or gate behavior.
