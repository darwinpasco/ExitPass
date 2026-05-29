# Statutory Discount Payable-Basis Application Design

Status: implementation-readiness design for ExitPass v1.2  
Scope: documentation only; no runtime, schema, endpoint, UI, payment, gate, coupon, provider, reconciliation, or AUB behavior is implemented here.

## Purpose

This document defines the implementation-ready contract, invariants, schema mapping, state transitions, and tests for applying an already-approved Operator Console statutory discount validation to the ExitPass payable basis.

The design is intentionally conservative. Applying a statutory discount changes the amount that a future payment attempt will collect, so the backend must treat this as a controlled payable-basis transition, not as a UI edit or a payment-side adjustment.

## Current Implemented Baseline

The implemented Operator Console backend chain currently supports:

- access evaluation through `POST /v1/ops/operator-console/access/evaluate`, with persisted access evaluation evidence;
- access-gated session lookup through `POST /v1/ops/operator-console/sessions/lookup`;
- access-gated statutory discount draft creation through `POST /v1/ops/operator-console/statutory-discounts/draft`;
- metadata-only evidence references in `discounts.discount_evidence_references` when evidence capture is requested;
- duplicate-safe draft replay for active draft rows;
- access-gated review decisions through `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`;
- validation status transitions from `REQUESTED` or `PENDING_OPERATOR_REVIEW` to `APPROVED` or `REJECTED`.

The current decision endpoint explicitly does not apply a statutory discount, does not calculate a discount amount, does not mutate `core.tariff_snapshots`, and does not create payment, provider, gate, coupon, settlement, or reconciliation state.

## Explicit Non-Goals

This design does not implement:

- Operator Console UI wiring;
- WebPay UI wiring;
- image upload or raw evidence storage;
- entitlement fingerprinting;
- payment attempt creation;
- payment confirmation or payment finality;
- exit authorization or gate opening;
- AUB routing, configuration, selection, or invocation;
- vendor PMS tariff recalculation;
- coupon application or coupon stacking execution;
- reconciliation or settlement records;
- database patches or production migrations.

## Business Rule Boundary

Only an approved Operator Console statutory discount validation may be applied to payable basis.

Required rules:

- `discounts.statutory_discount_validations.validation_status` must be `APPROVED`.
- `REQUESTED`, `PENDING_OPERATOR_REVIEW`, `REJECTED`, `FAILED`, `EXPIRED`, and `CANCELLED` validations must not be applied.
- A parking session may have at most one applied statutory discount.
- WebPay must not validate or apply statutory discounts.
- Operator Console validation produces a backend-approved input to payable-basis application; it does not directly edit amounts.
- Payable-basis application must be backend-controlled, transactional, auditable, and idempotent.
- The application must happen before payment attempt creation. If any active or terminal payment attempt already exists for the session, the next implementation should fail closed with `PAYMENT_ATTEMPT_ALREADY_EXISTS` unless a separate requote/invalidation design is approved.
- No exit authorization mutation may occur until payment finality is confirmed by the existing payment finalization flow.

## Schema Inspection Findings

The live local PostgreSQL schema was inspected before this design.

### discounts.statutory_discount_validations

Relevant columns:

- `statutory_discount_validation_id uuid primary key`
- `parking_session_id uuid not null`
- `tariff_snapshot_id uuid null`
- `entitlement_type discounts.statutory_entitlement_type_enum not null`
- `evaluated_policy_reference_id uuid null`
- `applied_policy_reference_id uuid null`
- `fallback_policy_reference_id uuid null`
- `policy_resolution_basis discounts.policy_resolution_basis_enum not null`
- `validation_channel discounts.statutory_discount_validations_channel_enum not null`
- `validation_status discounts.statutory_discount_validations_status_enum not null`
- `currency_code char(3) null`
- `gross_amount_at_validation numeric null`
- `statutory_discount_amount numeric null`
- `net_amount_after_discount numeric null`
- `evidence_required boolean not null default false`
- `evidence_captured boolean not null default false`
- `decision_reason_code varchar null`
- `failure_reason_code varchar null`
- `requested_at timestamptz not null`
- `validated_at timestamptz null`
- `validated_by_user_id uuid null`
- `requested_by_user_id uuid null`
- `correlation_id uuid null`
- `updated_at timestamptz not null default now()`
- `updated_by_user_id uuid null`
- `row_version bigint not null default 1`

Relevant enum values:

- `statutory_entitlement_type_enum`: `SENIOR_CITIZEN`, `PWD`, `OTHER_STATUTORY`
- `statutory_discount_validations_status_enum`: `REQUESTED`, `PENDING_OPERATOR_REVIEW`, `APPROVED`, `REJECTED`, `FAILED`, `EXPIRED`, `CANCELLED`
- `statutory_discount_validations_channel_enum`: `WEB_PAY`, `OPERATOR_ASSISTED`, `SYSTEM_VALIDATED`, `SUPPORT_REVIEW`, `RECONCILIATION_REVIEW`
- `policy_resolution_basis_enum`: `LOCAL_ORDINANCE_APPLIED`, `NATIONAL_LAW_FALLBACK`, `SITE_POLICY_OPERATIONAL_ONLY`, `MANUAL_POLICY_SELECTION`, `SYSTEM_DEFAULT`

Relevant index:

- `ux_statutory_discount_validations__active_session_entitlement` on `(parking_session_id, entitlement_type)` where `validation_status IN ('REQUESTED', 'PENDING_OPERATOR_REVIEW', 'APPROVED')`

Finding: this prevents more than one active or approved validation for the same session and entitlement type, but it does not enforce the product rule of one statutory discount per session across all entitlement types.

### discounts.discount_evidence_references

Relevant columns:

- `discount_evidence_reference_id uuid primary key`
- `statutory_discount_validation_id uuid not null`
- `evidence_type discounts.discount_evidence_type_enum not null`
- `evidence_storage_type discounts.evidence_storage_type_enum not null`
- `evidence_storage_ref varchar null`
- `evidence_hash char(64) null`
- `evidence_capture_status discounts.evidence_capture_status_enum not null`
- `access_classification discounts.evidence_access_classification_enum not null`
- `redaction_status discounts.evidence_redaction_status_enum not null`
- `retention_policy_code varchar not null`
- `retention_expires_at timestamptz null`
- `captured_at timestamptz not null`
- `captured_by_user_id uuid null`
- `purged_at timestamptz null`
- `correlation_id uuid null`
- `row_version bigint not null default 1`

Relevant enum values:

- `discount_evidence_type_enum`: `SENIOR_CITIZEN_ID`, `PWD_ID`, `AUTHORIZATION_LETTER`, `SUPPORTING_DOCUMENT`, `VALIDATION_SCREENSHOT`, `HASH_ONLY_REFERENCE`, `OTHER`
- `evidence_capture_status_enum`: `CAPTURED`, `REFERENCED`, `REDACTED`, `PURGED`, `HASH_ONLY`, `REJECTED`
- `evidence_storage_type_enum`: `OBJECT_STORAGE`, `EVIDENCE_VAULT`, `HASH_ONLY`, `EXTERNAL_REFERENCE`, `REDACTED_REFERENCE`
- `evidence_access_classification_enum`: `INTERNAL`, `RESTRICTED`, `HIGHLY_RESTRICTED`
- `evidence_redaction_status_enum`: `NOT_REDACTED`, `PARTIALLY_REDACTED`, `FULLY_REDACTED`, `HASH_ONLY`

Finding: evidence metadata exists and should remain separate from payable-basis application.

### core.tariff_snapshots

Relevant columns:

- `tariff_snapshot_id uuid primary key`
- `parking_session_id uuid not null`
- `superseded_by_tariff_snapshot_id uuid null`
- `vendor_system_id uuid not null`
- `vendor_tariff_ref varchar null`
- `tariff_version_reference varchar null`
- `currency_code char(3) not null`
- `gross_amount numeric not null`
- `statutory_discount_amount numeric not null`
- `coupon_discount_amount numeric not null`
- `net_amount numeric not null`
- `statutory_discount_validation_id uuid null`
- `coupon_application_id uuid null`
- `snapshot_status core.tariff_snapshot_status_enum not null`
- `calculated_at timestamptz not null`
- `expires_at timestamptz not null`
- `consumed_at timestamptz null`
- `correlation_id uuid null`
- `created_by_service_identity_id uuid not null`
- `updated_by_service_identity_id uuid null`
- `row_version bigint not null default 1`

Relevant enum values:

- `tariff_snapshot_status_enum`: `ACTIVE`, `CONSUMED`, `EXPIRED`, `SUPERSEDED`, `INVALIDATED`

Relevant indexes:

- `ux_tariff_snapshots__active_by_session` unique on `(parking_session_id)` where `snapshot_status = 'ACTIVE'`
- `ux_tariff_snapshots__superseded_by` unique on `(superseded_by_tariff_snapshot_id)` where `superseded_by_tariff_snapshot_id IS NOT NULL`
- `ix_tariff_snapshots__statutory_discount_validation_id`

Finding: tariff snapshots already have statutory discount amount and statutory validation linkage fields. The model supports superseding an active snapshot, but it lacks VAT component columns and does not enforce one applied statutory discount per session or one tariff snapshot per statutory validation.

### core.payment_attempts

Relevant columns:

- `payment_attempt_id uuid primary key`
- `parking_session_id uuid not null`
- `tariff_snapshot_id uuid not null`
- `idempotency_key varchar not null`
- `payment_rail_id uuid null`
- `currency_code char(3) not null`
- `amount numeric not null`
- `attempt_status core.payment_attempt_status_enum not null`
- `requested_at timestamptz not null`
- `expires_at timestamptz not null`
- `finalized_at timestamptz null`
- `failure_reason_code varchar null`
- `correlation_id uuid null`
- `created_by_service_identity_id uuid not null`
- `row_version bigint not null default 1`

Relevant enum values:

- `payment_attempt_status_enum`: `REQUESTED`, `PENDING_PROVIDER`, `PENDING_FINALIZATION`, `CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED`

Relevant indexes and constraints:

- `uq_payment_attempts__idempotency_key`
- `uq_payment_attempts__tariff_snapshot`
- `ux_payment_attempts__active_by_session` unique on `(parking_session_id)` where status is `REQUESTED`, `PENDING_PROVIDER`, or `PENDING_FINALIZATION`

Finding: payment attempts consume a single tariff snapshot. The authoritative `core.create_or_reuse_payment_attempt` routine copies the current snapshot amount into `core.payment_attempts.amount` and marks the tariff snapshot `CONSUMED`.

### core.parking_sessions

Relevant columns:

- `parking_session_id uuid primary key`
- `site_group_id uuid not null`
- `site_id uuid not null`
- `vendor_system_id uuid not null`
- `vendor_session_ref varchar not null`
- `ticket_number_hash char(64) null`
- `ticket_number_masked varchar null`
- `entry_at timestamptz null`
- `session_status core.parking_session_status_enum not null`
- `correlation_id uuid null`
- `row_version bigint not null default 1`

Relevant enum values:

- `parking_session_status_enum`: `ACTIVE`, `CLOSED`, `EXPIRED`, `INVALIDATED`

Finding: only `ACTIVE` sessions should be eligible for payable-basis application.

### coupons.coupon_applications

Relevant columns:

- `coupon_application_id uuid primary key`
- `parking_session_id uuid not null`
- `tariff_snapshot_id uuid null`
- `payment_attempt_id uuid null`
- `application_status coupons.coupon_application_status_enum not null`
- `gross_amount_at_application numeric not null`
- `coupon_discount_amount numeric not null`
- `net_amount_after_coupon numeric not null`

Relevant enum values:

- `coupon_application_status_enum`: `REQUESTED`, `RESERVED`, `APPLIED`, `COMMITTED`, `RELEASED`, `EXPIRED`, `REJECTED`, `CANCELLED`, `REVERSED`
- `coupon_stacking_policy_enum`: `NO_STACKING`, `STACK_WITH_STATUTORY_DISCOUNT`, `STACK_WITH_COUPON`, `STACK_WITH_BOTH`, `HIGHEST_BENEFIT_ONLY`

Finding: coupon composition is a separate domain. This slice should not apply coupons or create coupon applications. If an active coupon application exists, statutory application must fail closed or use a separately designed composition routine.

### Database routines

Inspected routines:

- `core.create_or_reuse_payment_attempt(...)`: implemented; creates or reuses payment attempts from a tariff snapshot and marks the snapshot `CONSUMED`.
- `core.finalize_payment_attempt(...)`: implemented for payment finality.
- `discounts.record_statutory_discount_validation()`: placeholder only; raises an exception and is not production usable.

Finding: there is no implemented routine for statutory discount payable-basis application.

## Recommended Application Model

Chosen readiness option: **Option D, schema gap. No payable-basis application implementation should proceed until a database patch/design update exists.**

Recommended target model after the schema gap is closed: **Option A, apply by creating a new superseding tariff snapshot**.

Rationale:

- The current domain and payment creation path treat `core.tariff_snapshots` as the immutable payable basis.
- `core.payment_attempts` copies `core.tariff_snapshots.net_amount` at creation time and consumes the snapshot.
- Updating an existing active snapshot would weaken immutability and make payment audit harder.
- A separate modifier table is not present, and payment attempt creation does not consume one.
- The existing snapshot fields can represent `statutory_discount_amount`, `net_amount`, and `statutory_discount_validation_id`, but the schema does not yet provide all implementation-safe constraints or VAT components.

Implementation must wait for a database patch or routine that can:

- lock the parking session, approved validation, active tariff snapshot, and payment-attempt boundary;
- supersede the old active tariff snapshot;
- insert a new active tariff snapshot with the statutory discount fields;
- link the approved validation to the new tariff snapshot;
- enforce one applied statutory discount per parking session;
- reject sessions with existing payment attempts unless a requote design is approved;
- prevent duplicate/concurrent applications.

## Endpoint Contract Recommendation

Recommended next endpoint:

`POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`

This keeps the action anchored to the approved validation record and avoids implying that any arbitrary session discount can be applied without validation.

Request:

```json
{
  "userId": "77000000-0000-0000-0000-000000000010",
  "operatorDeviceBindingId": "77000000-0000-0000-0000-000000000030",
  "siteId": "77000000-0000-0000-0000-000000000002",
  "siteGroupId": "77000000-0000-0000-0000-000000000001",
  "operatorShiftId": "77000000-0000-0000-0000-000000000050",
  "validationId": "00000000-0000-0000-0000-000000000000",
  "idempotencyKey": "operator-console-statutory-discount-apply-001",
  "correlationId": "00000000-0000-0000-0000-000000000000"
}
```

Response:

```json
{
  "accessEvaluationId": "00000000-0000-0000-0000-000000000000",
  "accessAllowed": true,
  "accessPersisted": true,
  "applicationAccepted": true,
  "applicationPersisted": true,
  "parkingSessionId": "00000000-0000-0000-0000-000000000000",
  "statutoryDiscountValidationId": "00000000-0000-0000-0000-000000000000",
  "previousTariffSnapshotId": "00000000-0000-0000-0000-000000000000",
  "newTariffSnapshotId": "00000000-0000-0000-0000-000000000000",
  "previousPayableAmountMinorUnits": 15000,
  "newPayableAmountMinorUnits": 10714,
  "vatRemovedAmountMinorUnits": 1607,
  "discountAmountMinorUnits": 2679,
  "applicationStatus": "APPLIED",
  "alreadyApplied": false,
  "ineligibilityReason": null,
  "errorCode": null,
  "correlationId": "00000000-0000-0000-0000-000000000000"
}
```

Field readiness notes:

- `previousTariffSnapshotId` and `newTariffSnapshotId` are backed by `core.tariff_snapshots`.
- `previousPayableAmountMinorUnits` and `newPayableAmountMinorUnits` are backed by `core.tariff_snapshots.net_amount`.
- `discountAmountMinorUnits` is backed by `core.tariff_snapshots.statutory_discount_amount`.
- `vatRemovedAmountMinorUnits` is not currently backed by a dedicated inspected column. This field should be omitted or returned only after schema support is added.
- `applicationStatus` is response-level unless a future patch adds a persisted application status.

## Access Gating

The apply endpoint must use the existing Operator Console access evaluator and persist the access evaluation before any payable-basis lookup or mutation.

Recommended current evaluator values:

- `workflowCode = STATUTORY_DISCOUNT_VALIDATION`
- `controlledActionCode = SUBMIT_DECISION`

`SUBMIT_DECISION` is not semantically perfect for payable-basis application, but it is an existing supported controlled action code. If product wants a distinct action such as `APPLY_PAYABLE_BASIS`, a prerequisite slice must add that supported action to the evaluator and tests before the endpoint uses it.

If access is denied:

- persist the access evaluation;
- do not read or mutate the validation or tariff snapshot except where strictly required by framework plumbing;
- return `accessAllowed = false`, `applicationAccepted = false`, and `applicationPersisted = false`.

## Payable-Basis Computation Rule

Design-level rule only; no computation is implemented in this slice.

The statutory discount calculation must use currency minor units and deterministic rounding:

1. Read the active base tariff snapshot gross amount.
2. Determine VAT-inclusive gross amount and VAT rate from the authoritative tariff/policy source.
3. Compute VAT amount and VAT-exclusive amount deterministically.
4. Apply the statutory discount as 20% of the VAT-exclusive amount.
5. Final payable amount = VAT-exclusive amount - statutory discount amount - allowed coupon amount, subject to approved stacking rules.
6. Round each persisted monetary component in a deterministic way, preferably by computing in decimal major units then converting to minor units with a single documented rounding mode.

Schema readiness gap:

- `core.tariff_snapshots` has `gross_amount`, `statutory_discount_amount`, `coupon_discount_amount`, and `net_amount`.
- It does not have dedicated VAT amount, VAT rate, VAT-exclusive amount, or VAT removal amount columns.
- The proposed response field `vatRemovedAmountMinorUnits` is therefore not directly persistable with the current inspected schema.

Until VAT component storage/source of truth is designed, implementation must either omit VAT component fields from API responses or add schema support in a prerequisite patch.

## State Transition Rules

### statutory_discount_validations

- Approved but not applied: `validation_status = APPROVED`, `tariff_snapshot_id IS NULL`, monetary fields may be null.
- Applied: `validation_status = APPROVED`, `tariff_snapshot_id` points to the new statutory-adjusted tariff snapshot, monetary fields reflect applied basis.
- Already applied: same validation has a non-null `tariff_snapshot_id`; repeated apply returns the existing result.
- Rejected/requested/pending/failed/expired/cancelled apply attempt: reject with `STATUTORY_DISCOUNT_NOT_APPROVED`.
- Evidence-required approval with `evidence_captured = false`: decision endpoint already blocks approval; if legacy data exists, apply should fail closed with `EVIDENCE_REQUIRED_NOT_CAPTURED`.

Do not introduce a new validation status for applied unless a future schema patch adds it. `APPROVED` remains the review decision status; application is represented by the tariff snapshot link and monetary fields.

### tariff_snapshots

Target model after schema patch:

- Existing active snapshot starts as `ACTIVE`.
- Payable-basis application updates old active snapshot to `SUPERSEDED` and sets `superseded_by_tariff_snapshot_id` to the new snapshot.
- New statutory-adjusted snapshot is inserted with:
  - `snapshot_status = ACTIVE`
  - same `parking_session_id`
  - same `vendor_system_id`
  - same `currency_code`
  - `gross_amount` copied from previous snapshot or authoritative recalculation
  - `statutory_discount_amount > 0`
  - `coupon_discount_amount` preserved only if a separately approved coupon composition rule allows it
  - `net_amount` set to the final payable amount
  - `statutory_discount_validation_id` set to the approved validation
  - `correlation_id` set from the apply request

Do not update the old snapshot amounts in place.

### payment_attempts

- If no payment attempt exists, apply may proceed after all other checks pass.
- If an active attempt exists (`REQUESTED`, `PENDING_PROVIDER`, `PENDING_FINALIZATION`), reject with `PAYMENT_ATTEMPT_ALREADY_EXISTS`.
- If a terminal attempt exists (`CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED`), reject until a separate requote/refund/retry policy exists. A confirmed payment must never be repriced by this endpoint.
- The apply endpoint must not create or reuse a payment attempt.

### parking_sessions

- `ACTIVE`: eligible if all other checks pass.
- `CLOSED`, `EXPIRED`, `INVALIDATED`: reject with `SESSION_NOT_ELIGIBLE`.

## Idempotency and Duplicate Behavior

Repeated apply of the same approved validation must be deterministic:

- If the validation already links to an applied tariff snapshot, return `alreadyApplied = true` and the existing snapshot/amounts.
- If the request races with another apply, the database routine must return the existing applied result or fail with a deterministic conflict, never create a duplicate.
- The implementation should use transactional row locks and database constraints, not application-only checks.

Required future constraint/routine behavior:

- enforce one applied statutory discount per `parking_session_id`;
- enforce at most one tariff snapshot created from a given `statutory_discount_validation_id`;
- preserve `ux_tariff_snapshots__active_by_session`;
- lock the active tariff snapshot and payment-attempt boundary in the same transaction.

## Failure Behavior

Recommended deterministic error codes:

- `ACCESS_DENIED`
- `STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND`
- `STATUTORY_DISCOUNT_NOT_APPROVED`
- `STATUTORY_DISCOUNT_ALREADY_APPLIED`
- `PAYMENT_ATTEMPT_ALREADY_EXISTS`
- `SESSION_NOT_ELIGIBLE`
- `EVIDENCE_REQUIRED_NOT_CAPTURED`
- `PAYABLE_BASIS_SCHEMA_NOT_READY`
- `PAYABLE_BASIS_APPLICATION_FAILED`

Suggested status mapping:

- `200`: access denied envelope or idempotent already-applied success, if consistent with Operator Console access-gated endpoints
- `400`: invalid request
- `404`: validation/session not found
- `409`: payment attempt exists, opposite state conflict, duplicate conflict that cannot be reused
- `422`: validation not approved or session not eligible
- `500`: unexpected failure only

Do not return `applicationPersisted = true` unless the transaction committed.

## Non-Payment Boundary

The apply-payable-basis endpoint would intentionally change payable basis, but it still must not:

- create a payment attempt;
- confirm payment;
- call a payment provider;
- issue an exit authorization;
- open a gate;
- call AUB;
- call vendor PMS;
- create a coupon application;
- create provider outcome records;
- create reconciliation or settlement records.

QRPH, GCash, Maya, and card payments remain PayMongo-only. AUB remains out of scope.

## Required Database Constraints And Routines

Existing constraints are not sufficient for implementation.

Required future database patch items:

- an implemented `discounts.apply_statutory_discount_to_payable_basis(...)` routine or equivalent backend-owned routine;
- one applied statutory discount per `parking_session_id`, across entitlement types;
- at most one tariff snapshot per `statutory_discount_validation_id` when the validation is applied;
- deterministic idempotency storage for apply requests, if endpoint-level idempotency cannot be derived from validation linkage alone;
- lock and conflict behavior for active payment attempts before inserting a new tariff snapshot;
- VAT source/component representation, or an explicit decision to omit VAT component response fields;
- audit/outbox requirement for payable-basis application result, if the product requires event publication.

Do not create these items in this documentation slice.

## Required Tests For Next Implementation

Unit/application tests:

- access denied prevents apply and does not call the apply writer;
- validation not found returns deterministic not-found;
- non-approved statuses cannot apply;
- approved validation applies once;
- already-applied validation returns existing result;
- payment attempt already exists blocks apply;
- session not found blocks apply;
- closed/expired/invalidated session blocks apply;
- evidence-required but not captured blocks apply if legacy data reaches this path;
- payable calculation expected values and rounding are deterministic;
- response mapping includes previous/new snapshot IDs and persisted flags.

Integration tests:

- endpoint appears in Swagger under `OperatorConsole`;
- access evaluation is persisted before apply;
- approved validation supersedes the active tariff snapshot and creates exactly one new active snapshot;
- repeated apply returns existing result and does not create duplicate snapshots;
- concurrent apply does not create duplicate snapshots;
- active payment attempt blocks apply;
- confirmed payment attempt blocks apply;
- no payment attempts, confirmations, exit authorizations, provider outcomes, gate consumptions, coupon applications, reconciliation items, or settlement records are created;
- WebPay/payment attempt creation uses the new active statutory-adjusted tariff snapshot only after apply succeeds.

## Recommended Implementation Sequence

1. `#187` Add database support for statutory discount payable-basis application.
2. `#188` Implement backend apply-payable-basis endpoint and writer.
3. `#189` Add Bruno/manual test coverage for payable-basis application.
4. `#190` Add WebPay display of backend-approved payable basis, if needed.
5. `#191` Add Operator Console UI wiring, if needed.

## Open Decisions

- Which database object owns applied statutory discount state: only `core.tariff_snapshots` plus `discounts.statutory_discount_validations.tariff_snapshot_id`, or a new immutable application table.
- Whether to add a unique constraint on `core.tariff_snapshots.statutory_discount_validation_id` where non-null.
- Whether to add a unique applied statutory discount constraint per `parking_session_id` across entitlement types.
- Whether tariff snapshot supersession should be handled by a dedicated database routine only.
- Whether any existing payment attempt, including terminal failed/expired/cancelled attempts, blocks application or can be invalidated through a separate requote design.
- Whether `evidence_required = true` should ever allow payable-basis application without `evidence_captured = true`.
- Source of truth for VAT rate, VAT amount, VAT-exclusive amount, and VAT removal amount.
- Rounding mode for VAT removal and 20% statutory discount computation.
- Whether coupon stacking with statutory discount is allowed in v1.2 MVP or must fail closed.
- Whether payable-basis application publishes an outbox/integration event.
