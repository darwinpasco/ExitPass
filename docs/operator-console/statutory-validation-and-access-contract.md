# ExitPass Operator Console Statutory Validation and Access Contract

Status: decision-locked design proposal for ExitPass v1.2

References:
- ExitPass Operator Console BRD v1.0
- ExitPass v1.2 baseline constraints
- Operator Console frontend module shell in `src/Services/OperatorConsoleUi`

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
- The non-payment boundary remains unchanged.

## Scope

The Operator Console is a non-payment, operator-facing platform for ExitPass site workflows. Statutory Discount Validation is the first operational module. Operators must pass role, registered device, site assignment, and active shift checks before performing controlled actions.

Payment collection is out of scope. The Operator Console must not accept, process, collect, confirm, reverse, or refund payments. It may display payment status only as read-only context.

## Access Evaluation

Access evaluation is the backend gate used before controlled Operator Console actions, including statutory discount validation decisions.

ExitPass evaluates operational access even though shift schedule data is imported from HR/Timekeeping. The access evaluator must map the ExitPass operator user to an HR/Timekeeping identity before accepting imported shift context.

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

Creates or reuses a pending validation workflow for a resolved session after access evaluation passes.

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

Submits a mock-reviewed decision for approval or rejection. The endpoint must be idempotent by `idempotencyKey`.

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

Use v1.2 DDL-aligned status values where available. Proposed frontend-visible values are:

- `no_request`
- `pending_operator_review`
- `approved`
- `rejected`
- `expired`

Before implementation, confirm the actual database enum/check constraint values from the live schema and DDL. Do not assume these exact storage values until verified.

### Idempotency Behavior

- All mutation endpoints require `idempotencyKey`.
- Repeated requests with the same key and same payload return the original response.
- Repeated requests with the same key and different payload return `409 idempotency_key_conflict`.
- Idempotency scope should include endpoint path, operator user, session, and validation ID where applicable.

### Evidence Reference Behavior

Evidence storage is backend-owned and must not be invented by the frontend. The validation decision accepts evidence references only after an evidence service or storage contract exists.

Image capture is configurable by site. The default minimum evidence path is structured ID metadata plus a backend-generated entitlement fingerprint. Cropped ID image evidence is required only when enabled by site policy or regulation.

Evidence references should include:

- evidence reference ID
- evidence type
- capture level
- storage classification
- hash or integrity token if available
- capture timestamp
- site policy requirement indicator

No raw image bytes should be included in the decision endpoint.

### Entitlement Fingerprint Behavior

The backend generates the entitlement fingerprint from normalized statutory ID attributes and policy-approved matching fields. The frontend must not generate the fingerprint.

The response should expose only the fingerprint reference or stable opaque fingerprint value needed for audit and duplicate detection. Sensitive source fields must remain protected.

### Payable-Basis Update Boundary

The statutory validation service owns the decision. The payable-basis update is a backend boundary and must be performed by a backend workflow after approval.

The Operator Console must not manually mark payment as paid, change payment state, or directly edit payable amounts. It may show update status such as `not_started`, `queued`, `applied`, or `failed` as read-only context.

### WebPay Notification and Display Boundary

WebPay may display updated payable basis or discount outcome after backend propagation. The Operator Console must not call WebPay payment-intent, payment confirmation, refund, or collection endpoints.

## Audit Requirements

Audit events are required for:

- access evaluation attempts, allowed and denied
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

- Which existing v1.2 DDL status values should be reused exactly for statutory validation storage?
- Which service owns evidence storage and retention policy?
- Should access evaluation be called once per workflow start or before every controlled action?
