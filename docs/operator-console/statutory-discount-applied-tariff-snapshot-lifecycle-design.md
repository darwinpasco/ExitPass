# Statutory Discount Applied Tariff Snapshot Lifecycle Design

Status: implementation-readiness design for ExitPass v1.2  
Scope: documentation only; no runtime code, endpoint, database patch, baseline DDL, UI, payment provider, gate, coupon, reconciliation, or AUB behavior is changed here.

## Purpose

This document defines how an approved Operator Console statutory discount payable-basis application should move from `REQUESTED` to `APPLIED` by creating an applied statutory-discount tariff snapshot without corrupting the original tariff snapshot, payment attempt state, or WebPay payable-basis display.

The design exists because #188 intentionally stopped at a database-backed `REQUESTED` application row. Final `APPLIED` state requires a tariff snapshot that payment creation and WebPay can consume, and that transition must be transactional, idempotent, and auditable.

## Current Baseline

The implemented chain is:

- Operator Console creates a privacy-minimized statutory discount validation draft.
- Operator Console approves or rejects the validation draft.
- `POST /v1/ops/operator-console/statutory-discounts/{validationId:guid}/apply-payable-basis` access-gates the request and persists the access evaluation.
- The apply writer validates an `APPROVED` statutory discount validation, active parking session, active original tariff snapshot, evidence state, and absence of payment attempts.
- The writer computes VAT, VAT-exclusive amount, 20% statutory discount, and final payable amount in minor units.
- The writer inserts one row in `discounts.statutory_discount_payable_basis_applications` with `application_status = REQUESTED`.
- `applied_tariff_snapshot_id` remains `NULL`.
- No payment attempt, provider outcome, exit authorization, gate consumption, coupon application, reconciliation, settlement, WebPay UI, Operator Console UI, or AUB behavior is created or changed.

## Problem Statement

Final `APPLIED` state requires a tariff snapshot that downstream payment and WebPay flows can treat as the effective payable basis. The original tariff snapshot must remain an immutable record of the pre-discount quote amounts, but the system also has a database invariant that only one `ACTIVE` tariff snapshot may exist per parking session.

The unresolved problem is therefore:

- payment attempt creation consumes `core.tariff_snapshots.net_amount`;
- WebPay/session display must show a backend-approved statutory discount basis, not validate the discount itself;
- `discounts.statutory_discount_payable_basis_applications` can only be `APPLIED` when `applied_tariff_snapshot_id` points to an `ACTIVE` tariff snapshot;
- `core.tariff_snapshots` currently has `ux_tariff_snapshots__active_by_session`;
- creating a new active applied snapshot requires moving the original active snapshot out of `ACTIVE`;
- payment attempt creation must not use the stale base snapshot after statutory discount application;
- if a payment attempt exists, the payable-basis application must fail closed or be handled by a separate requote/refund design.

## Schema Inspection Findings

The live local PostgreSQL schema was inspected on 2026-05-30 before writing this design.

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
- service attribution columns and `row_version`

Enum values for `core.tariff_snapshot_status_enum`:

- `ACTIVE`
- `CONSUMED`
- `EXPIRED`
- `SUPERSEDED`
- `INVALIDATED`

Relevant indexes and constraints:

- `ux_tariff_snapshots__active_by_session`: unique `(parking_session_id)` where `snapshot_status = 'ACTIVE'`
- `ux_tariff_snapshots__superseded_by`: unique `(superseded_by_tariff_snapshot_id)` where not null
- `ix_tariff_snapshots__statutory_discount_validation_id`
- `fk_tariff_snapshots__statutory_discount_validation_id`
- `fk_tariff_snapshots__superseded_by_tariff_snapshot_id`
- non-negative checks for gross, statutory discount, coupon discount, and net amounts

There is no unique constraint on `statutory_discount_validation_id` in `core.tariff_snapshots`.

### discounts.statutory_discount_payable_basis_applications

Relevant columns:

- `statutory_discount_payable_basis_application_id uuid primary key`
- `statutory_discount_validation_id uuid not null`
- `parking_session_id uuid not null`
- `original_tariff_snapshot_id uuid not null`
- `applied_tariff_snapshot_id uuid null`
- `application_status discounts.statutory_discount_payable_application_status_enum not null`
- `application_channel discounts.statutory_discount_payable_application_channel_enum not null`
- `gross_amount_minor_units bigint not null`
- `vat_amount_minor_units bigint not null`
- `vat_exclusive_amount_minor_units bigint not null`
- `statutory_discount_amount_minor_units bigint not null`
- `final_payable_amount_minor_units bigint not null`
- `currency_code char(3) not null`
- `computation_basis_json jsonb not null default '{}'`
- `rounding_mode varchar(64) not null default 'HALF_AWAY_FROM_ZERO'`
- `applied_at timestamptz null`
- `applied_by_user_id uuid null`
- `idempotency_key varchar(128) null`
- `correlation_id uuid not null`
- attribution columns and `row_version`

Enum values:

- `statutory_discount_payable_application_status_enum`: `REQUESTED`, `APPLIED`, `FAILED`, `CANCELLED`
- `statutory_discount_payable_application_channel_enum`: `OPERATOR_CONSOLE`, `OPERATOR_ASSISTED`, `SYSTEM`

Relevant indexes and constraints:

- `ux_sd_pba__validation_active`: unique `statutory_discount_validation_id` where status is `REQUESTED` or `APPLIED`
- `ux_sd_pba__session_active`: unique `parking_session_id` where status is `REQUESTED` or `APPLIED`
- `ux_sd_pba__applied_tariff_snapshot`: unique `applied_tariff_snapshot_id` where not null
- `ux_sd_pba__idempotency_key`
- amount non-negative and arithmetic sanity checks
- `ck_sd_pba__applied_fields`: `APPLIED` requires `applied_tariff_snapshot_id` and `applied_at`
- `ck_sd_pba__distinct_snapshots`: applied snapshot must differ from original snapshot

Trigger:

- `trg_sd_pba__enforce` executes `discounts.enforce_statutory_discount_payable_basis_application()`

For `APPLIED`, the trigger requires:

- the validation belongs to the same session;
- `validation_status = APPROVED`;
- if evidence is required, `evidence_captured = true`;
- original tariff snapshot belongs to the same session;
- applied tariff snapshot exists;
- applied tariff snapshot belongs to the same session;
- applied tariff snapshot references the same statutory discount validation;
- applied tariff snapshot has `snapshot_status = ACTIVE`;
- applied tariff snapshot has positive `statutory_discount_amount`;
- no payment attempt exists for the parking session.

### discounts.statutory_discount_validations

Relevant columns:

- `statutory_discount_validation_id uuid primary key`
- `parking_session_id uuid not null`
- `tariff_snapshot_id uuid null`
- `entitlement_type discounts.statutory_entitlement_type_enum not null`
- `validation_status discounts.statutory_discount_validations_status_enum not null`
- `currency_code char(3) null`
- `gross_amount_at_validation numeric null`
- `statutory_discount_amount numeric null`
- `net_amount_after_discount numeric null`
- `evidence_required boolean not null default false`
- `evidence_captured boolean not null default false`
- decision, timestamp, correlation, and attribution columns

Enum values for `discounts.statutory_discount_validations_status_enum`:

- `REQUESTED`
- `PENDING_OPERATOR_REVIEW`
- `APPROVED`
- `REJECTED`
- `FAILED`
- `EXPIRED`
- `CANCELLED`

Relevant index:

- `ux_statutory_discount_validations__active_session_entitlement`: unique `(parking_session_id, entitlement_type)` where status is `REQUESTED`, `PENDING_OPERATOR_REVIEW`, or `APPROVED`

### core.payment_attempts

Relevant columns:

- `payment_attempt_id uuid primary key`
- `parking_session_id uuid not null`
- `tariff_snapshot_id uuid not null`
- `idempotency_key varchar not null`
- `currency_code char(3) not null`
- `amount numeric not null`
- `attempt_status core.payment_attempt_status_enum not null`
- `requested_at timestamptz not null`
- `expires_at timestamptz not null`
- `finalized_at timestamptz null`
- attribution columns and `row_version`

Enum values:

- `REQUESTED`
- `PENDING_PROVIDER`
- `PENDING_FINALIZATION`
- `CONFIRMED`
- `FAILED`
- `EXPIRED`
- `CANCELLED`

Relevant constraints and indexes:

- `uq_payment_attempts__tariff_snapshot`
- `uq_payment_attempts__idempotency_key`
- `ux_payment_attempts__active_by_session`: unique active attempt by session for `REQUESTED`, `PENDING_PROVIDER`, `PENDING_FINALIZATION`

The DB routine `core.create_or_reuse_payment_attempt(...)` locks the parking session and tariff snapshot, requires the tariff snapshot to be `ACTIVE`, not consumed, not expired, and not superseded, then inserts the payment attempt and updates the snapshot to `CONSUMED`.

### core.parking_sessions

Relevant columns:

- `parking_session_id uuid primary key`
- `site_group_id uuid not null`
- `site_id uuid not null`
- `vendor_system_id uuid not null`
- `vendor_session_ref varchar not null`
- ticket/plate masked and hash columns
- `session_status core.parking_session_status_enum not null`
- correlation, attribution, and `row_version`

Enum values:

- `ACTIVE`
- `CLOSED`
- `EXPIRED`
- `INVALIDATED`

Only `ACTIVE` sessions are eligible for this lifecycle.

## Tariff Snapshot Lifecycle Options

### Option A: Mark Original SUPERSEDED And Create New ACTIVE Snapshot

This option updates the original active snapshot lifecycle fields only, inserts a new statutory-adjusted `ACTIVE` snapshot, links the application row to the new snapshot, and links the validation to the new snapshot.

Immutability:

- Preserves original amount columns.
- Allows a lifecycle status transition from `ACTIVE` to `SUPERSEDED`.
- Requires treating status and supersession pointer as lifecycle metadata, not amount mutation.

Auditability:

- Strong. Original basis, applied basis, validation, and application row are all separately inspectable.
- Matches current payment attempt model where `core.tariff_snapshots` is the payable basis.

Idempotency:

- Strong if enforced by `ux_sd_pba__validation_active`, `ux_sd_pba__session_active`, `ux_sd_pba__applied_tariff_snapshot`, a future unique `core.tariff_snapshots.statutory_discount_validation_id` index, and row locks.

Payment attempt compatibility:

- Strong. Payment creation already accepts one tariff snapshot ID and consumes that snapshot.
- The old base snapshot becomes ineligible because it is `SUPERSEDED` and has a supersession pointer.
- The new applied snapshot is eligible because it is `ACTIVE`.

WebPay compatibility:

- Strong if WebPay/session summary resolves the active tariff snapshot after apply.
- WebPay still does not validate statutory discounts.

Required DB support:

- A transactional routine or application writer that updates old snapshot, inserts new snapshot, updates validation, and updates application row atomically.
- Additional constraints described below.

Operational risk:

- Moderate. The only risky part is ordering around `ux_tariff_snapshots__active_by_session`; the transaction must move the old snapshot out of `ACTIVE` before inserting the new `ACTIVE` row.

### Option B: Keep Original ACTIVE And Create Separate Applied Snapshot Type

This option keeps the original base snapshot `ACTIVE` and creates a second statutory-adjusted snapshot using a different status/type consumed by payment creation.

Immutability:

- Original remains unchanged and active.
- Requires changing the meaning of active/effective payable basis.

Auditability:

- Acceptable if a new effective-basis resolver exists.

Idempotency:

- Requires new uniqueness rules beyond the current active snapshot constraint.

Payment attempt compatibility:

- Weak. `core.create_or_reuse_payment_attempt` currently requires `snapshot_status = ACTIVE`; a non-active applied snapshot would be rejected.
- If the applied snapshot is also `ACTIVE`, it violates `ux_tariff_snapshots__active_by_session`.

WebPay compatibility:

- Requires new read-model logic to choose the applied snapshot over the active base snapshot.

Operational risk:

- High because it weakens the current simple "one active payable snapshot" invariant.

### Option C: Payment Attempt Creation Consumes Application Table Directly

This option avoids a new tariff snapshot and changes payment attempt creation to read `discounts.statutory_discount_payable_basis_applications`.

Immutability:

- Preserves original snapshot.
- Moves effective payable basis outside the current tariff snapshot model.

Auditability:

- Strong for discount computation, but split across two models.

Idempotency:

- Strong using #187 application table constraints.

Payment attempt compatibility:

- Weak. Current payment creation copies `core.tariff_snapshots.net_amount`; changing it to consume an application table is broader than the Operator Console slice and risks WebPay/provider regressions.

WebPay compatibility:

- Requires WebPay/session summary and payment attempt creation to resolve effective payable basis from a new table.

Operational risk:

- High because it changes the core payment attempt contract.

### Option D: Separate Effective Payable-Basis View/Table

This option introduces a read model or table that resolves the effective payable basis across base tariff, statutory applications, and future coupon composition.

Immutability:

- Strong.

Auditability:

- Strong if backed by immutable inputs.

Idempotency:

- Requires a complete new ownership model.

Payment attempt compatibility:

- Requires payment attempt creation to use the view/table instead of direct tariff snapshot input, or to mint a payment-consumable snapshot from the effective basis.

WebPay compatibility:

- Good after read-model changes, but not compatible with current payment attempt routine alone.

Operational risk:

- Medium to high due to larger surface area and unsettled coupon composition rules.

## Recommended Option

Recommended lifecycle: **Option A, mark the original active tariff snapshot `SUPERSEDED` and create one new statutory-adjusted `ACTIVE` tariff snapshot.**

This is the safest implementation path because:

- it preserves the original snapshot amounts;
- it uses the existing `core.tariff_snapshots` payable-basis model;
- it keeps payment attempt creation compatible with the current `tariff_snapshot_id` contract;
- it makes stale base snapshots ineligible through existing `snapshot_status`, `superseded_by_tariff_snapshot_id`, and `core.create_or_reuse_payment_attempt` checks;
- it satisfies the #187 trigger requirement that an `APPLIED` application point to an `ACTIVE` applied tariff snapshot;
- it provides a clear audit chain from original snapshot to application row to applied snapshot to validation.

The original snapshot is not immutable in the strict append-only sense because lifecycle fields change. The invariant should be refined as: original tariff snapshot amount fields are immutable; lifecycle metadata may transition from `ACTIVE` to `SUPERSEDED` inside the approved payable-basis application transaction.

## Required DB Changes For The Chosen Option

Do not implement these in this slice. A future DB patch should add or confirm:

- A database routine such as `discounts.apply_statutory_discount_payable_basis(...)` that owns the transaction.
- A unique partial index on `core.tariff_snapshots(statutory_discount_validation_id)` where `statutory_discount_validation_id IS NOT NULL`.
- A check or trigger that a statutory-adjusted tariff snapshot must have:
  - `statutory_discount_validation_id IS NOT NULL`;
  - `statutory_discount_amount > 0`;
  - `net_amount = gross_amount - statutory_discount_amount - coupon_discount_amount`, or an approved equivalent if VAT-exclusive gross is used as net basis.
- A trigger/routine guard preventing supersession if any `core.payment_attempts` row exists for the parking session.
- A trigger/routine guard preventing supersession unless the current original snapshot is `ACTIVE`, unconsumed, unexpired, and has `superseded_by_tariff_snapshot_id IS NULL`.
- A routine-level lock order:
  1. parking session row;
  2. statutory discount validation row;
  3. payable-basis application row;
  4. original tariff snapshot row;
  5. payment attempt existence boundary.
- Explicit use of deferred constraints if the routine sets `original.superseded_by_tariff_snapshot_id` before the applied snapshot insert is visible.
- A consistent way to populate `discounts.statutory_discount_validations.tariff_snapshot_id`, `gross_amount_at_validation`, `statutory_discount_amount`, and `net_amount_after_discount` after application.
- Optional but recommended comments documenting that amount fields on the original snapshot must not be changed by supersession.

No new tariff snapshot status value is required. Existing `ACTIVE` and `SUPERSEDED` values are sufficient.

## Required API And Service Behavior

Future implementation of the existing apply endpoint should:

- continue to run and persist Operator Console access evaluation first;
- require `validation_status = APPROVED`;
- reject evidence-required validations where `evidence_captured = false`;
- require the parking session to be `ACTIVE`;
- require no payment attempt row for the session, not only no active attempt;
- create or reuse a `REQUESTED` application row;
- when the row is `REQUESTED`, transition it to `APPLIED` by creating the applied snapshot in the same transaction;
- update the original snapshot only by lifecycle metadata:
  - `snapshot_status = SUPERSEDED`;
  - `superseded_by_tariff_snapshot_id = <new applied snapshot id>`;
  - `updated_at`, attribution, and `row_version`;
- insert the new tariff snapshot as:
  - `snapshot_status = ACTIVE`;
  - same parking session and vendor system;
  - `gross_amount` from original basis or approved computation basis;
  - positive `statutory_discount_amount`;
  - `coupon_discount_amount = 0` until coupon composition is designed;
  - `net_amount` from the application final payable amount;
  - `statutory_discount_validation_id = validationId`;
  - `correlation_id` from request;
- update the application:
  - `application_status = APPLIED`;
  - `applied_tariff_snapshot_id = <new snapshot id>`;
  - `applied_at = now`;
  - applied/updated attribution;
- update the validation:
  - `tariff_snapshot_id = <new snapshot id>`;
  - amount fields reflect applied basis;
  - `updated_at`, attribution, and `row_version`;
- return replay of existing `APPLIED` application as success with `alreadyApplied = true`;
- fail closed for any conflicting payment/session/snapshot state;
- not create payment attempts, provider outcomes, payment confirmations, exit authorizations, gate records, coupon applications, reconciliation, settlement, WebPay UI state, Operator Console UI state, or AUB behavior.

## WebPay And Payment Attempt Consumption Rule

After successful `APPLIED` transition:

- WebPay/session summary should display the new active statutory-adjusted tariff snapshot.
- Payment attempt creation should use the applied active tariff snapshot ID.
- WebPay must not validate statutory discount eligibility or compute the statutory discount.
- Any stale client-side base amount or stale base tariff snapshot ID must be rejected or requoted.
- The old base snapshot must fail payment attempt creation because it is `SUPERSEDED` and has a supersession pointer.
- If a payment attempt exists before application, application should be blocked. This design does not support repricing after payment attempt creation.

Current code implication:

- `TariffSnapshotReadRepository` can read statutory-adjusted snapshots because it projects `source_type = STATUTORY_ADJUSTED` when `statutory_discount_amount > 0`.
- `core.create_or_reuse_payment_attempt` already rejects superseded snapshots and consumes active snapshots.
- WebPay/vendor resolution code may reuse an existing active tariff snapshot. After apply, the active snapshot should be the applied snapshot; vendor re-resolution must not retire or overwrite it without an explicit composition/requote design.

## State Transitions

### Tariff Snapshots

Happy path:

1. Base snapshot: `ACTIVE`, `superseded_by_tariff_snapshot_id = NULL`, statutory discount amount `0`.
2. Apply transaction computes or reuses requested application components.
3. New applied snapshot is inserted with `ACTIVE`, positive statutory discount amount, and `statutory_discount_validation_id`.
4. Original snapshot transitions to `SUPERSEDED` and points to the new applied snapshot.
5. Payment attempt later consumes the applied snapshot and changes it to `CONSUMED`.

Rollback:

- If any insert/update fails, the transaction rolls back and the original snapshot remains `ACTIVE`.

### Payable-Basis Applications

- `REQUESTED`: computation evidence exists, no applied snapshot yet.
- `APPLIED`: applied snapshot exists and is active at application time.
- `FAILED`: deterministic failed state may be used only if a future implementation explicitly records failed attempts. Prefer no failed row for validation/business rejection.
- `CANCELLED`: administrative future state only; not part of this implementation path.

### Statutory Discount Validations

- `APPROVED` without `tariff_snapshot_id`: approved but not applied.
- `APPROVED` with `tariff_snapshot_id`: applied to payable basis.
- Non-approved statuses cannot apply.

### Payment Attempts

- No row exists: apply may proceed.
- Any row exists for the session: apply is blocked with `PAYMENT_ATTEMPT_ALREADY_EXISTS`.
- After apply, payment attempt creation must use the new applied snapshot.

### Parking Sessions

- `ACTIVE`: eligible.
- `CLOSED`, `EXPIRED`, `INVALIDATED`: reject with `SESSION_NOT_ELIGIBLE`.

## Idempotency And Concurrency

The future implementation must use one transaction for:

- application row lookup/create;
- original tariff snapshot lock and lifecycle transition;
- applied snapshot insert;
- validation link update;
- application row `APPLIED` update.

Concurrency safeguards:

- lock parking session `FOR UPDATE`;
- lock validation `FOR UPDATE`;
- lock application row `FOR UPDATE`;
- lock original tariff snapshot `FOR UPDATE`;
- check payment attempts in the same transaction;
- rely on `ux_sd_pba__validation_active`, `ux_sd_pba__session_active`, `ux_sd_pba__applied_tariff_snapshot`, and `ux_tariff_snapshots__active_by_session`;
- add a unique tariff snapshot constraint by `statutory_discount_validation_id`;
- catch unique violations and re-read existing applied state before returning a deterministic response.

Replay behavior:

- If application is `REQUESTED`, continue the transition to `APPLIED`.
- If application is already `APPLIED`, return existing application and applied snapshot with `alreadyApplied = true`.
- If validation already points to an applied snapshot and the application row is missing, treat as data inconsistency and return `PAYABLE_BASIS_APPLICATION_FAILED` unless a repair routine is explicitly designed.

## Failure Behavior

Recommended deterministic errors:

- `ACCESS_DENIED`
- `STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND`
- `STATUTORY_DISCOUNT_NOT_APPROVED`
- `PAYABLE_BASIS_APPLICATION_NOT_FOUND`
- `PAYABLE_BASIS_ALREADY_APPLIED`
- `PAYMENT_ATTEMPT_ALREADY_EXISTS`
- `SESSION_NOT_ELIGIBLE`
- `TARIFF_SNAPSHOT_NOT_FOUND`
- `ACTIVE_TARIFF_SNAPSHOT_CONFLICT`
- `APPLIED_TARIFF_SNAPSHOT_CREATION_FAILED`
- `EVIDENCE_REQUIRED_NOT_CAPTURED`
- `PAYABLE_BASIS_APPLICATION_FAILED`

Suggested HTTP behavior should remain consistent with #188:

- access denied can return `200` with access-denied response envelope;
- validation not found returns `404`;
- deterministic state conflicts return `409`;
- business ineligibility can return `200` or `422` only if the project chooses a broader convention change;
- `500` is reserved for unexpected failures only.

## Required Future Tests

Unit/application tests:

- access denied prevents APPLIED transition;
- validation not found;
- not approved validation cannot apply;
- evidence required but not captured blocks apply;
- requested application transitions to applied;
- already applied replay returns existing applied snapshot;
- active payment attempt blocks apply;
- any terminal payment attempt also blocks apply;
- session closed/expired/invalidated blocks apply;
- original snapshot amount fields are not changed;
- original snapshot lifecycle changes to `SUPERSEDED`;
- applied snapshot amount fields match application components.

Integration tests:

- endpoint creates exactly one applied tariff snapshot;
- application row moves `REQUESTED -> APPLIED`;
- validation row links to applied snapshot;
- original snapshot points to applied snapshot and is not payment-eligible;
- payment attempt creation using old snapshot fails;
- payment attempt creation using applied snapshot succeeds, if payment creation is in scope for the test;
- replay does not create a second applied snapshot;
- concurrent apply creates only one applied snapshot;
- no provider outcomes, payment confirmations, exit authorizations, gate consumptions, coupon applications, reconciliation items, settlement records, or AUB objects are created.

Swagger/API tests:

- existing apply endpoint contract remains discoverable;
- response maps `appliedTariffSnapshotId`, `applicationStatus = APPLIED`, and `alreadyApplied` correctly.

## Manual Coverage Plan

After implementation, Bruno/manual coverage should include:

- create draft, approve, apply, and verify `APPLIED`;
- replay apply returns same payable-basis application and applied snapshot;
- verify original snapshot is `SUPERSEDED` and amount fields unchanged;
- verify applied snapshot is `ACTIVE`;
- verify validation `tariff_snapshot_id` points to applied snapshot;
- verify stale original snapshot cannot create a payment attempt;
- verify applied snapshot can be used by payment creation only in the intended payment slice;
- verify payment-attempt-existing guardrail;
- verify session-closed guardrail;
- verify no provider/gate/coupon/reconciliation/AUB rows are created.

## Recommended Implementation Sequence

1. `#199` Add DB support for final `APPLIED` statutory discount tariff snapshot lifecycle.
2. `#200` Implement `APPLIED` tariff snapshot transition in the existing apply-payable-basis endpoint.
3. `#201` Add Bruno/manual coverage for the `APPLIED` snapshot lifecycle.
4. `#202` Update WebPay/session summary effective payable-basis read model, if inspection shows it does not already resolve the active applied snapshot.
5. `#203` Align payment attempt payable-basis guardrails and stale-tariff rejection tests, if needed.

## DB Support Decision From #199

Patch path:

- `infra/db/patches/ExitPass_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql`

Validation script:

- `infra/db/patches/validation/Validate_StatutoryDiscountAppliedTariffSnapshotLifecycle_v1.2.sql`

Final DB support decision:

- Keep `core.tariff_snapshots` schema unchanged.
- Use the existing `superseded_by_tariff_snapshot_id`, `snapshot_status`, `statutory_discount_validation_id`, and amount fields.
- Add the final lifecycle transition as a database routine:
  - `discounts.apply_statutory_discount_payable_basis(p_statutory_discount_payable_basis_application_id uuid, p_actor_user_id uuid, p_correlation_id uuid)`
- The routine transitions one `REQUESTED` application to `APPLIED` by:
  - locking the application, validation, parking session, and original tariff snapshot;
  - blocking non-approved validations;
  - blocking evidence-required validations without captured evidence;
  - blocking inactive sessions;
  - blocking non-active, consumed, expired, or already-superseded original snapshots;
  - blocking any existing payment attempt for the session/original snapshot;
  - transitioning the original snapshot from `ACTIVE` to `SUPERSEDED`;
  - preserving the original snapshot amount fields;
  - inserting one new statutory-discount-adjusted `ACTIVE` tariff snapshot;
  - linking the application to the new applied snapshot;
  - setting `application_status = APPLIED` and `applied_at`;
  - linking the validation to the applied snapshot and copied applied amount fields.

Added guardrails:

- `ux_tariff_snapshots__statutory_discount_validation_applied`
  - unique `core.tariff_snapshots(statutory_discount_validation_id)` where `statutory_discount_validation_id IS NOT NULL`
  - prevents duplicate statutory-discount-adjusted snapshots for one validation.
- `ck_tariff_snapshots__statutory_discount_link_has_discount`
  - requires a tariff snapshot linked to a statutory discount validation to carry a positive statutory discount amount.

Existing guardrails reused:

- `ux_tariff_snapshots__active_by_session`
  - keeps one active tariff snapshot per parking session.
- `ux_sd_pba__validation_active`
  - keeps one active/requested application per statutory discount validation.
- `ux_sd_pba__session_active`
  - keeps one active/requested statutory discount payable-basis application per parking session.
- `ux_sd_pba__applied_tariff_snapshot`
  - prevents reuse of one applied tariff snapshot by multiple application rows.
- `ck_sd_pba__applied_fields`
  - requires `APPLIED` applications to have `applied_tariff_snapshot_id` and `applied_at`.
- `ck_sd_pba__distinct_snapshots`
  - prevents original and applied tariff snapshots from being the same row.
- `trg_sd_pba__enforce`
  - keeps APPLIED application rows tied to approved validations, matching sessions, captured evidence when required, active applied snapshots, positive statutory discount amounts, and no payment attempts.

Known limitations:

- The routine is DB support only. No C# endpoint/service code invokes it yet.
- The routine does not create payment attempts or payment provider state.
- The routine does not create coupon/reconciliation/gate/vendor/AUB state.
- The routine uses existing tariff snapshot amount fields; no new VAT component fields were added.
- Failure outcomes are returned by the routine as deterministic `outcome_code`/`failure_code` values for future service mapping.

## Open Decisions

- Whether the future C# implementation should call the DB routine directly or keep equivalent transaction logic in the application writer with the DB routine reserved for validation/admin use.
- Whether WebPay/vendor re-resolution may retire an applied statutory snapshot, or must fail/reuse it.
- Whether payment attempt creation should reject a stale base snapshot with a more specific `TARIFF_SNAPSHOT_SUPERSEDED` outcome.
- Whether any existing terminal payment attempt should permanently block apply or a separate requote/refund design will exist.
- Whether payable-basis application should publish an outbox/audit event in a later slice.
- Exact rollback/reporting behavior if an application row exists in `REQUESTED` but the original snapshot has already become non-active outside the apply transaction.
