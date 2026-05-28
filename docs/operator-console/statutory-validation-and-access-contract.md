# ExitPass Operator Console Statutory Validation and Access Contract

Status: decision-locked design proposal for ExitPass v1.2

References:
- ExitPass Operator Console BRD v1.0
- ExitPass v1.2 baseline constraints
- Operator Console frontend module shell in `src/Services/OperatorConsoleUi`
- Operator Console schema extension design in `docs/operator-console/operator-console-schema-extension-design.md`

This document freezes the proposed backend-facing contract before implementation. It is a design slice only and does not introduce runtime behavior, database changes, API contract files, or service code.

## Settled Product Decisions

- HR/Timekeeping is the source of imported shift schedule and roster data.
- Operators must still be registered as ExitPass users, and ExitPass user IDs must map to HR/Timekeeping identities.
- Imported shifts provide the time source, but ExitPass remains the operational access authority for controlled Operator Console actions.
- Shift revocation and controlled shift takeover are Operator Console workflows and must be audited.
- The device registry model supports both mTLS and browser key binding.
- MVP device enforcement may use browser key binding first for Operator Console browser/device binding. mTLS should be enforced for managed site devices once certificate issuance, renewal, and revocation are operationalized at the API gateway or reverse proxy layer.
- Statutory Discount Validation MVP uses one-step operator approval.
- Supervisor review and override are later scope and should be added by policy.
- Image capture is site-configurable. Structured ID metadata plus backend-generated entitlement fingerprinting is the default minimum evidence path. Cropped ID image evidence is required only when enabled by site policy or regulation.
- Evidence ownership is split: Audit/Event Service owns evidence metadata governance, retrieval authorization, access audit, and lifecycle audit; encrypted evidence objects live in an external evidence vault or object store.
- The non-payment boundary remains unchanged.

## Scope

The Operator Console is a non-payment, operator-facing platform for ExitPass site workflows. Statutory Discount Validation is the first operational module. Operators must pass role, registered device, site assignment, and active shift checks before performing controlled actions.

Payment collection is out of scope. The Operator Console must not accept, process, collect, confirm, reverse, or refund payments. It may display payment status only as read-only context.

## Access Evaluation

Access evaluation is the backend gate used before controlled Operator Console actions, including statutory discount validation decisions.

ExitPass evaluates operational access even though shift schedule data is imported from HR/Timekeeping. The access evaluator must map the ExitPass operator user to an HR/Timekeeping identity before accepting imported shift context.

### Timing Decision

Access evaluation timing is locked for Operator Console controlled workflows:

- Run access evaluation at workflow start, before entering a controlled workflow such as statutory discount validation, shift takeover, evidence capture, supervisor override, or reporting/export.
- Run access evaluation again before every controlled action that mutates state, captures or views sensitive evidence, submits a decision, requests or approves takeover, revokes shift access, performs supervisor/compliance action, or exports reports.

Controlled actions include:

- start statutory validation workflow
- submit statutory approval or rejection
- capture evidence
- view stored evidence
- request shift takeover
- approve or reject shift takeover
- revoke shift
- supervisor override
- report export

The access evaluation response is authoritative only at the time of the evaluated action. It must not be cached or reused as a long-lived permission grant because user status, role assignment, device status, site assignment, shift status, takeover state, or revocation state can change while an operator remains inside a workflow.

Evaluation frequency is separate from persistence. The backend must persist denied evaluations and controlled-action evaluations, but must not persist harmless navigation, page loads, tab switches, or read-only module browsing.

### Proposed Endpoint

`POST /v1/operator-console/access/evaluate`

This endpoint evaluates the caller, device, shift, and site context. It does not mutate operator workflow state except for audit logging of the evaluation attempt.

### Request DTO

```json
{
  "correlationId": "corr_01HZ...",
  "requestedAction": "statutory_discount.approve",
  "operatorUserId": "usr_123",
  "hrTimekeepingIdentityId": "hr_456",
  "operatorRole": "cashier",
  "deviceId": "dev_123",
  "siteId": "site_001",
  "siteGroupId": "sitegrp_001",
  "shiftId": "shift_123",
  "sessionId": "sess_123",
  "requestTimestamp": "2026-05-28T02:00:00Z"
}
```

### Response DTO

```json
{
  "correlationId": "corr_01HZ...",
  "accessEvaluationId": "aeval_123",
  "status": "allowed",
  "denialReasons": [],
  "user": {
    "operatorUserId": "usr_123",
    "hrTimekeepingIdentityId": "hr_456",
    "role": "cashier",
    "roleAllowed": true
  },
  "device": {
    "deviceId": "dev_123",
    "deviceName": "North Exit Kiosk 02",
    "status": "active",
    "trustMechanisms": ["browser_key_binding", "mtls_certificate"],
    "enforcementPhase": "browser_key_binding",
    "trusted": true
  },
  "siteAssignment": {
    "siteId": "site_001",
    "siteGroupId": "sitegrp_001",
    "assigned": true,
    "matchedRequestedSite": true
  },
  "shift": {
    "shiftId": "shift_123",
    "source": "hr_timekeeping_import",
    "hrTimekeepingShiftId": "hrshift_789",
    "status": "active",
    "siteId": "site_001",
    "activeAt": "2026-05-28T02:00:00Z",
    "revoked": false,
    "takeover": {
      "active": false,
      "takeoverId": null,
      "originalOperatorUserId": null
    }
  },
  "audit": {
    "logged": true,
    "auditEventId": "audit_123"
  }
}
```

### Status Values

- `allowed`
- `denied`

### Denial Reason Codes

- `USER_ROLE_NOT_ALLOWED`
- `DEVICE_NOT_REGISTERED`
- `DEVICE_PENDING`
- `DEVICE_SUSPENDED`
- `DEVICE_REVOKED`
- `DEVICE_LOST`
- `SITE_NOT_ASSIGNED`
- `SITE_MISMATCH`
- `SHIFT_NOT_FOUND`
- `SHIFT_NOT_ACTIVE`
- `SHIFT_SUSPENDED`
- `SHIFT_REVOKED`
- `SHIFT_TAKEOVER_NOT_APPROVED`
- `SESSION_NOT_PROVIDED`
- `SESSION_SITE_MISMATCH`
- `ACTION_NOT_SUPPORTED`

Denied responses must include one or more denial reason codes and must still write an audit event.

### Shift Source and Takeover Rules

HR/Timekeeping is the source of imported shift schedule data. ExitPass owns the operational access decision. Imported shift records must be associated with an ExitPass user through an HR/Timekeeping identity mapping before they can authorize actions.

Shifts may be revoked by policy or imported state. Revoked shifts must deny controlled actions even if their scheduled time window would otherwise be active.

A user may take over another user's shift only through a controlled Operator Console takeover workflow. Takeover approval must create an auditable takeover record linking the original operator, takeover operator, shift, site, approver where applicable, reason code, and timestamps. Access evaluation must deny takeover use unless that controlled workflow has approved the takeover.

### Device Trust Model

The device registry model supports both browser key binding and mTLS certificate trust.

For managed operator devices, the target architecture should use both when operationally feasible. MVP enforcement may phase this:

- browser key binding first for Operator Console browser/device binding
- mTLS later at the API gateway or reverse proxy layer once certificate issuance, renewal, and revocation operations are ready

This contract does not implement certificate issuance, browser key generation, or gateway enforcement.

## Session Lookup

The Operator Console should prefer reusing existing session resolution capabilities only if they already provide the required operator-safe read model and access checks. If existing public/WebPay-oriented endpoints expose payment or checkout behavior, the console should use a new ops-scoped read-only endpoint.

### Proposed Endpoint

`POST /v1/operator-console/sessions/resolve`

This endpoint is read-only. It resolves a session by ticket number, plate number, or backend session reference and returns only the operator context needed by the console.

### Request DTO

```json
{
  "correlationId": "corr_01HZ...",
  "ticketNumber": "TCK-123",
  "plateNumber": "ABC 1234",
  "siteId": "site_001",
  "operatorUserId": "usr_123",
  "deviceId": "dev_123"
}
```

### Response DTO

```json
{
  "correlationId": "corr_01HZ...",
  "status": "session_found",
  "session": {
    "sessionId": "sess_123",
    "parkingSessionReference": "PARK-123",
    "vehiclePlate": "ABC 1234",
    "entryTime": "2026-05-28T01:30:00Z",
    "siteId": "site_001",
    "siteDisplayName": "North Site / Terminal Parking",
    "sessionStatus": "active",
    "currentFee": {
      "amount": 15000,
      "currency": "PHP"
    },
    "paymentStatus": "unpaid",
    "payableBasisStatus": "standard"
  },
  "matches": []
}
```

### Failure States

- `not_found`
- `inactive`
- `ambiguous`
- `site_mismatch`

For `ambiguous`, the response should include a bounded `matches` array with non-sensitive disambiguation fields such as session reference, partial plate, entry time, and site display name.

## Statutory Discount Validation Lifecycle

The MVP approval model is one-step operator approval. The recommended MVP contract is a start endpoint plus an idempotent decision endpoint. This keeps the audit trail explicit while avoiding premature workflow complexity.

### Start Validation

`POST /v1/operator-console/statutory-discounts/validations`

Creates or reuses a pending validation workflow for a resolved session after a workflow-start access evaluation passes.

Request DTO:

```json
{
  "correlationId": "corr_01HZ...",
  "idempotencyKey": "opcon-stat-start-123",
  "accessEvaluationId": "aeval_123",
  "sessionId": "sess_123",
  "operatorUserId": "usr_123",
  "deviceId": "dev_123",
  "siteId": "site_001",
  "discountType": "senior_citizen"
}
```

Response DTO:

```json
{
  "correlationId": "corr_01HZ...",
  "validationId": "sdv_123",
  "status": "pending_operator_review",
  "discountType": "senior_citizen",
  "sessionId": "sess_123",
  "createdAt": "2026-05-28T02:05:00Z"
}
```

### Submit Decision

`POST /v1/operator-console/statutory-discounts/validations/{validationId}/decision`

Submits a mock-reviewed decision for approval or rejection after a fresh controlled-action access evaluation passes. The endpoint must be idempotent by `idempotencyKey`.

Request DTO:

```json
{
  "correlationId": "corr_01HZ...",
  "idempotencyKey": "opcon-stat-decision-123",
  "accessEvaluationId": "aeval_124",
  "operatorUserId": "usr_123",
  "deviceId": "dev_123",
  "siteId": "site_001",
  "decision": "approve",
  "reasonCode": "VALID_ID_PRESENTED",
  "structuredIdDetails": {
    "idNumberLast4": "1234",
    "cardholderName": "Juan Dela Cruz",
    "issuingAuthority": "OSCA",
    "birthDateProvided": true
  },
  "evidenceReferences": [
    {
      "evidenceReferenceId": "evref_123",
      "evidenceType": "cropped_id_image",
      "captureLevel": "cropped_id"
    }
  ],
  "operatorAttestation": {
    "attested": true,
    "attestedAt": "2026-05-28T02:07:00Z"
  }
}
```

Response DTO:

```json
{
  "correlationId": "corr_01HZ...",
  "validationId": "sdv_123",
  "status": "approved",
  "decision": "approve",
  "entitlementFingerprint": "fp_abc123",
  "payableBasisUpdate": {
    "status": "queued",
    "boundary": "backend_owned"
  },
  "auditEventId": "audit_456",
  "processedAt": "2026-05-28T02:07:02Z"
}
```

### Optional Review Endpoints

Supervisor-reviewable approval and override are later scope. If policy requires supervisor review in a later slice, add these endpoints:

- `POST /v1/operator-console/statutory-discounts/validations/{validationId}/submit-review`
- `POST /v1/operator-console/statutory-discounts/validations/{validationId}/supervisor-decision`

These should reuse the same access evaluation and idempotency rules.

### Status Values

Use v1.2 DDL-aligned status values from `discounts.statutory_discount_validations.validation_status`, whose type is `discounts.statutory_discount_validations_status_enum`.

Verified DDL values:

- `REQUESTED`
- `PENDING_OPERATOR_REVIEW`
- `APPROVED`
- `REJECTED`
- `FAILED`
- `EXPIRED`
- `CANCELLED`

Operator Console MVP mapping:

| Operator Console state | Persisted DDL value | Notes |
| --- | --- | --- |
| No request | none | UI-only state before a row exists. Do not persist `NO_REQUEST`. |
| Start validation submitted | `REQUESTED` | Initial backend row state. |
| Waiting for operator decision | `PENDING_OPERATOR_REVIEW` | Use for in-progress operator-assisted review. |
| One-step approval complete | `APPROVED` | Final successful MVP decision. |
| One-step rejection complete | `REJECTED` | Final rejected MVP decision. |
| Backend/system validation failed | `FAILED` | Failure state, not an operator rejection. |
| Review window expired | `EXPIRED` | Expiry state. |
| Request cancelled | `CANCELLED` | Cancelled workflow state. |

UI labels such as `no request`, `mock only`, `later slice`, `blocked`, `access allowed`, `device not registered`, and `site mismatch` are UI-only or response-only labels. They must not be inserted into `discounts.statutory_discount_validations.validation_status`.

### Entitlement Type Values

The statutory entitlement type is stored in `discounts.statutory_discount_validations.entitlement_type`, whose type is `discounts.statutory_entitlement_type_enum`.

Verified DDL values:

- `SENIOR_CITIZEN`
- `PWD`
- `OTHER_STATUTORY`

Operator Console MVP allows only:

- `SENIOR_CITIZEN`
- `PWD`

`OTHER_STATUTORY` exists in v1.2 DDL for future statutory categories, but it is later scope for Operator Console MVP. The MVP UI must not offer `OTHER_STATUTORY` unless a later policy/design slice explicitly enables it.

### Channel and Policy Values

Operator Console statutory validation should use `discounts.statutory_discount_validations.validation_channel = OPERATOR_ASSISTED`.

The DDL type is `discounts.statutory_discount_validations_channel_enum` with values:

- `WEB_PAY`
- `OPERATOR_ASSISTED`
- `SYSTEM_VALIDATED`
- `SUPPORT_REVIEW`
- `RECONCILIATION_REVIEW`

Policy resolution is stored in `discounts.statutory_discount_validations.policy_resolution_basis`, type `discounts.policy_resolution_basis_enum`, with values:

- `LOCAL_ORDINANCE_APPLIED`
- `NATIONAL_LAW_FALLBACK`
- `SITE_POLICY_OPERATIONAL_ONLY`
- `MANUAL_POLICY_SELECTION`
- `SYSTEM_DEFAULT`

The applicable value must be selected by backend policy resolution, not invented by the UI.

### Idempotency Behavior

- All mutation endpoints require `idempotencyKey`.
- Repeated requests with the same key and same payload return the original response.
- Repeated requests with the same key and different payload return `409 idempotency_key_conflict`.
- Idempotency scope should include endpoint path, operator user, session, and validation ID where applicable.

### Evidence Reference Behavior

Evidence storage ownership is locked as a split-responsibility model. Audit/Event Service owns evidence metadata governance, evidence retrieval authorization, evidence access audit, and evidence lifecycle audit. Actual encrypted images and documents are stored in an external evidence vault or object store.

Central PMS receives evidence references and validation results. Operator Console captures and submits evidence only through controlled flows allowed by site configuration and policy. Neither Central PMS nor Operator Console owns raw evidence storage.

Image capture is configurable by site. The default minimum evidence path is structured ID metadata plus a backend-generated entitlement fingerprint. Cropped ID image evidence is required only when enabled by site policy or regulation.

Evidence references align to `discounts.discount_evidence_references`.

Relevant DDL columns and enum types:

- `discounts.discount_evidence_references.evidence_type`: `discounts.discount_evidence_type_enum`
- `discounts.discount_evidence_references.evidence_storage_type`: `discounts.evidence_storage_type_enum`
- `discounts.discount_evidence_references.evidence_capture_status`: `discounts.evidence_capture_status_enum`
- `discounts.discount_evidence_references.access_classification`: `discounts.evidence_access_classification_enum`
- `discounts.discount_evidence_references.redaction_status`: `discounts.evidence_redaction_status_enum`

Verified `discounts.discount_evidence_type_enum` values:

- `SENIOR_CITIZEN_ID`
- `PWD_ID`
- `AUTHORIZATION_LETTER`
- `SUPPORTING_DOCUMENT`
- `VALIDATION_SCREENSHOT`
- `HASH_ONLY_REFERENCE`
- `OTHER`

Verified `discounts.evidence_capture_status_enum` values:

- `CAPTURED`
- `REFERENCED`
- `REDACTED`
- `PURGED`
- `HASH_ONLY`
- `REJECTED`

Verified `discounts.evidence_storage_type_enum` values:

- `OBJECT_STORAGE`
- `EVIDENCE_VAULT`
- `HASH_ONLY`
- `EXTERNAL_REFERENCE`
- `REDACTED_REFERENCE`

Site-configurable image evidence maps to `SENIOR_CITIZEN_ID` or `PWD_ID` with `evidence_capture_status = CAPTURED` when image evidence is captured, or `REFERENCED` when an external controlled evidence reference is used. The default minimum evidence path may use `HASH_ONLY_REFERENCE` plus `evidence_storage_type = HASH_ONLY` when only the entitlement fingerprint/hash reference is retained. No new DDL status is needed for "image required"; use `discounts.statutory_discount_validations.evidence_required` and `evidence_captured`, backed by site policy from `discounts.discount_policy_references.requires_evidence_capture`.

Evidence references should include:

- evidence reference ID
- evidence type
- capture level
- storage classification
- storage URI/reference
- object hash
- hash algorithm
- retention expiry
- capture timestamp
- site policy requirement indicator

No raw image bytes, raw document bytes, or raw sensitive evidence payloads should be included in the decision endpoint or stored in PostgreSQL.

Access rules:

- Operators may view structured evidence only during the active validation workflow.
- Operators must not retrieve stored ID images after submission.
- Supervisors and compliance users may access stored evidence only through controlled, audited flows.

Retention and lifecycle rules:

- Evidence references must include retention metadata.
- Retention should be configurable by evidence type and site policy.
- Evidence deletion or purge must leave audit-safe traces where legally allowed.

### Entitlement Fingerprint Behavior

The backend generates the entitlement fingerprint from normalized statutory ID attributes and policy-approved matching fields. The frontend must not generate the fingerprint.

The v1.2 DDL does not expose a dedicated `entitlement_fingerprint` column on `discounts.statutory_discount_validations`. Until a controlled schema design adds one, the contract should treat the entitlement fingerprint as backend-derived evidence/audit metadata, not as an assumed statutory validation table column. Sensitive source fields must remain protected.

### Payable-Basis Update Boundary

The statutory validation service owns the decision. The payable-basis update is a backend boundary and must be performed by a backend workflow after approval.

The Operator Console must not manually mark payment as paid, change payment state, or directly edit payable amounts. It may show update status such as `not_started`, `queued`, `applied`, or `failed` as read-only context.

The DDL materializes approved statutory discount effect through existing backend-owned fields, including `discounts.statutory_discount_validations.tariff_snapshot_id`, `statutory_discount_amount`, `net_amount_after_discount`, and `core.tariff_snapshots.statutory_discount_validation_id`. Operator Console response labels such as `queued`, `applied`, or `failed` for payable-basis update are response-only workflow labels unless a later schema design adds a persisted payable-basis update status.

## Operator Access and Shift Storage Alignment

Access evaluation response statuses and denial reason codes are contract-level values. The v1.2 DDL does not define a persisted access evaluation table or enum for Operator Console access decisions.

Existing relevant DDL:

- `identity.users`: ExitPass users. Operators must be registered here.
- `identity.roles`, `identity.user_roles`, `identity.role_permissions`: role and permission assignment.
- `identity.service_identities`: non-human service/device identities; supports credential references and `MTLS_CERTIFICATE_REFERENCE` through `identity.service_credential_type_enum`.
- `sites.device_assignments`: site/lane/service-identity assignment with `sites.device_assignment_status_enum`.
- `operations.operator_action_logs`: generic operator audit/action log with `operations.operator_action_status_enum`.
- `audit.audit_events`: generic audit events.

Verified `sites.device_assignment_status_enum` values:

- `ACTIVE`
- `SUSPENDED`
- `SUPERSEDED`
- `EXPIRED`
- `RETIRED`

Verified `operations.operator_action_status_enum` values:

- `RECORDED`
- `SUCCESS`
- `FAILED`
- `DENIED`
- `CANCELLED`

Verified `identity.user_status_enum` values:

- `INVITED`
- `ACTIVE`
- `LOCKED`
- `SUSPENDED`
- `INACTIVE`
- `RETIRED`

Verified `identity.user_role_assignment_status_enum` values:

- `ACTIVE`
- `SUSPENDED`
- `REVOKED`
- `EXPIRED`
- `RETIRED`

DDL gaps for future implementation:

- No dedicated Operator Console access evaluation table exists.
- No dedicated registered Operator Console browser/device binding table exists.
- No HR/Timekeeping identity mapping table exists.
- No imported operator shift table exists.
- No shift revocation table/status enum exists.
- No controlled shift takeover table/status enum exists.

Future implementation must introduce those concepts through a controlled schema design. Do not overload `gates.gate_devices` or `sites.device_assignments` to represent browser key binding or HR shift state without a dedicated design update.

Operator Console denial reasons such as `DEVICE_NOT_REGISTERED`, `SHIFT_REVOKED`, `SHIFT_TAKEOVER_NOT_APPROVED`, and `SITE_MISMATCH` remain response/audit reason codes, not existing DDL enums.

### WebPay Notification and Display Boundary

WebPay may display updated payable basis or discount outcome after backend propagation. The Operator Console must not call WebPay payment-intent, payment confirmation, refund, or collection endpoints.

The Operator Console must not create or mutate `core.payment_attempts`, `core.payment_confirmations`, `payments.provider_outcomes`, `core.exit_authorizations`, `gates.gate_authorization_consumptions`, `coupons.coupon_applications`, or settlement truth records.

## Audit Requirements

Audit events are required for:

- persisted access evaluation attempts, including denied evaluations and controlled-action evaluations
- session lookup attempts and outcomes
- shift revocation events that affect Operator Console access
- shift takeover request, approval, rejection, and use
- statutory validation start
- evidence reference association
- discount type selection
- approval decision
- rejection decision
- payable-basis update request and result
- backend validation failures
- idempotency conflicts

Required audit fields:

- correlation ID
- audit event ID
- operator user ID
- HR/Timekeeping identity ID
- operator role
- session ID
- parking session reference where available
- device ID
- site ID
- site group ID where available
- shift ID
- HR/Timekeeping shift ID
- shift takeover ID where applicable
- access evaluation ID
- evidence level
- evidence reference IDs
- evidence access attempts and outcomes
- evidence lifecycle actions, including purge or redaction where legally allowed
- entitlement fingerprint
- decision
- reason code
- request timestamp
- decision timestamp
- backend response code
- denial reason codes for denied attempts

Denied access attempts must be logged even when no workflow mutation occurs.

## Error Model

Recommended error response shape:

```json
{
  "correlationId": "corr_01HZ...",
  "errorCode": "ACCESS_DENIED",
  "message": "Operator access evaluation denied the requested action.",
  "details": {
    "denialReasons": ["SHIFT_NOT_ACTIVE"]
  }
}
```

Recommended status mappings:

- `400` invalid request shape
- `401` unauthenticated
- `403` access denied
- `404` session or validation not found
- `409` ambiguous session, idempotency conflict, or invalid workflow transition
- `422` validation failed by business policy
- `500` unexpected backend failure

## Explicit Non-Goals

- No payment collection
- No manual payment confirmation
- No payment reversal or refunds
- No gate control
- No exit authorization issuance
- No coupon validation
- No AUB routing, configuration, selection, or invocation
- No real implementation in this slice
- No database scripts, DDL, migrations, or runtime API contract files in this slice

## Open Questions

- Final executable migration approval for HR/Timekeeping identity mapping, imported shifts, shift revocation, shift takeover, browser key binding, and access evaluation persistence.
