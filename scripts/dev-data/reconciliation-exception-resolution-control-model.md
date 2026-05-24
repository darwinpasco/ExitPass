# Reconciliation Exception Resolution Control Model

Status: design only  
Scope: WebPay PayMongo QRPH/PHP reconciliation exceptions  
Date: 2026-05-24

This document defines the control model for reviewing and resolving persisted PayMongo WebPay reconciliation exceptions. It does not implement exception mutation, settlement, payout, financial posting, or any provider-routing change.

## Current-State Schema Findings

Live schema inspection was performed against `exitpass_v12_dev` before writing this document.

### Reconciliation Runs

`reconciliation.reconciliation_runs` supports durable run identity and summary:

- Identity and scope: `reconciliation_run_id`, `run_code`, `run_type`, `scope_type`, `source_batch_ref`, `window_start_at`, `window_end_at`.
- Status: `run_status` enum with `STARTED`, `PROCESSING`, `COMPLETED`, `FAILED`, `CANCELLED`, `REPROCESSING`.
- Counts: `item_count`, `matched_count`, `exception_count`, `rejected_count`, `disputed_count`.
- Audit fields: initiated/created/updated user and service identity ids, timestamps, `correlation_id`, `row_version`.

This table is sufficient for run listing and readback. It is not sufficient for exception-level review notes or approvals.

### Reconciliation Items

`reconciliation.reconciliation_items` links reconciliation evidence to business-control rows:

- References: `reconciliation_run_id`, `payment_attempt_id`, `payment_confirmation_id`, `provider_outcome_id`, `target_entity_type`, `target_entity_id`.
- Comparison: `comparison_basis`, `item_status`, `match_status`, `expected_amount`, `actual_amount`, `currency_code`, `variance_amount`, `exception_reason_code`.
- Resolution basics: `resolved_at`, `resolved_by_user_id`, `resolved_by_service_identity_id`.
- Audit fields: created/updated identities and timestamps, `correlation_id`, `row_version`.

The item table can mark an item resolved at a coarse level, but it has no note history, approval status, or recommendation record.

### Reconciliation Exceptions

`reconciliation.reconciliation_exceptions` supports basic assignment, status, severity, and closure:

- Classification/status: `exception_type`, `exception_severity`, `exception_status`, `exception_reason_code`.
- Description: `exception_summary`, `exception_detail`.
- Assignment: `assigned_to_user_id`, `assigned_to_service_identity_id`, `assigned_at`.
- Resolution and closure: `resolved_at`, `closed_at`, `resolution_reason_code`, `closure_reason_code`, resolved/closed user and service identity ids.
- Audit fields: created/updated identities and timestamps, `correlation_id`, `row_version`.

Current enum values:

- `exception_status`: `OPEN`, `ASSIGNED`, `UNDER_REVIEW`, `RESOLVED`, `REJECTED`, `ESCALATED`, `CLOSED`, `CANCELLED`.
- `exception_severity`: `LOW`, `MEDIUM`, `HIGH`, `CRITICAL`.
- `exception_type`: `AMOUNT_MISMATCH`, `MISSING_PAYMENT_CONFIRMATION`, `MISSING_PROVIDER_OUTCOME`, `MISSING_MOPS_RECORD`, `DUPLICATE_RECORD`, `MANUAL_GATE_WITHOUT_PAYMENT`, `SETTLEMENT_MISMATCH`, `COUPON_WALLET_MISMATCH`, `UNRESOLVED_CONTINUITY_RECORD`, `POLICY_EXCEPTION`, `UNKNOWN_EXCEPTION`.

The current table can support assignment, under-review, resolved, rejected, escalated, closed, and cancelled states. It cannot fully support maker-checker resolution because it has no dedicated recommendation, approval, rejection reason history, free-form note history, or immutable per-action review log.

### Audit, Events, and Identity

`audit.audit_events` can represent immutable reconciliation actions using `event_category = RECONCILIATION`, actor fields, target/related entity fields, `summary`, `details_ref`, `details_hash`, `occurred_at`, `recorded_at`, `correlation_id`, and `causation_id`.

`audit.audit_trail_entries` can record field-level change evidence with `change_type`, target entity, field name, before/after redacted values or hashes, change reason, actor fields, `approval_reference_type`, `approval_reference_id`, and `correlation_id`.

`events.domain_events` and `events.outbox_events` can publish best-effort integration evidence. They must not become prerequisites for payment finality, gate finality, or exception resolution persistence.

`identity.roles`, `identity.permissions`, `identity.user_roles`, and `identity.role_permissions` support RBAC. `roles.requires_elevated_approval` exists, but no reconciliation-specific approval workflow table exists.

Focused search found `operations.override_approvals`, but no reconciliation-specific approval table. That operations table should not be reused for reconciliation financial controls without a separate design review because it is outside the reconciliation bounded context.

### Missing Fields

The current reconciliation schema is missing:

- Immutable exception note history.
- Resolution recommendation records.
- Approval/rejection records for maker-checker.
- Explicit approved_by / approved_at fields on reconciliation exceptions.
- Review-status enum values such as `NEEDS_PROVIDER_CHECK`, `NEEDS_INTERNAL_CHECK`, `PENDING_APPROVAL`, `APPROVED`, or financial-adjustment-specific states.
- JSON/metadata payload fields for structured resolution evidence.
- Separate adjustment recommendation records.

### Can Resolution Be Supported Without Migration?

Limited resolution can be supported without migration:

- Assign exception.
- Mark exception `UNDER_REVIEW`.
- Mark exception `RESOLVED`, `REJECTED`, `ESCALATED`, `CLOSED`, or `CANCELLED`.
- Store a compact `resolution_reason_code` or `closure_reason_code`.
- Write immutable `audit.audit_events` and `audit.audit_trail_entries` for every change.

Full controlled resolution should require a future migration because financial-impact decisions need durable recommendation and approval records. Updating only the current exception row would overwrite the live state and leave audit trails dependent on correct application behavior.

## Exception Lifecycle

Use current enum values for the first implementation. The conceptual lifecycle below maps desired business states to current schema where possible.

| Desired state | Current schema mapping | Notes |
| --- | --- | --- |
| OPEN | `exception_status = OPEN` | Created by reconciliation run. |
| ASSIGNED | `ASSIGNED` plus `assigned_to_*`, `assigned_at` | Assignment only. |
| UNDER_REVIEW | `UNDER_REVIEW` | Reviewer is actively investigating. |
| NEEDS_PROVIDER_CHECK | `ESCALATED` with reason `NEEDS_PROVIDER_CHECK` | Current enum lacks a dedicated value. |
| NEEDS_INTERNAL_CHECK | `ESCALATED` with reason `NEEDS_INTERNAL_CHECK` | Current enum lacks a dedicated value. |
| RESOLVED_NO_ADJUSTMENT | `RESOLVED` with reason `NO_ADJUSTMENT` | Requires audit event. |
| RESOLVED_WITH_OPERATIONAL_NOTE | `RESOLVED` with reason `OPERATIONAL_NOTE` | Needs future note table for rich detail. |
| REQUIRES_FINANCIAL_ADJUSTMENT | `ESCALATED` with reason `REQUIRES_FINANCIAL_ADJUSTMENT` | Should not close until approval exists. |
| PENDING_APPROVAL | `UNDER_REVIEW` or `ESCALATED` with reason `PENDING_APPROVAL` | Current enum lacks a dedicated value. |
| APPROVED | Not directly supported | Future approval record required. |
| REJECTED | `REJECTED` | Should mean recommendation rejected, not evidence deleted. |
| CLOSED | `CLOSED` plus `closed_at`, `closed_by_*`, `closure_reason_code` | Final administrative closure. |

Closed exceptions may be reopened only through a future controlled action that creates a new audit event and, for financial-impact exceptions, requires maker-checker approval.

## Classification-Specific Handling

### EXITPASS_CONFIRMED_PROVIDER_MISSING

Impact: possible financial impact.  
Handling: verify provider session, provider callback, and PayMongo dashboard/export evidence. If provider evidence is genuinely missing, keep exception open or escalated for provider check. Do not reverse the ExitPass confirmation in place. Any correction must be a separate adjustment recommendation.

### PROVIDER_PAID_EXITPASS_MISSING

Impact: definite financial/control impact.  
Handling: verify provider-side paid evidence and absence of ExitPass confirmation. If valid, raise a controlled remediation path to record a missing confirmation through a separate approved operation. Do not create confirmations from the review tool.

### AMOUNT_MISMATCH

Impact: definite financial impact.  
Handling: compare confirmed amount, provider amount, tariff snapshot, and provider evidence. Requires maker-checker for any acceptance, adjustment, or closure. Closure without adjustment must explain why the mismatch is acceptable.

### CURRENCY_MISMATCH

Impact: definite financial impact.  
Handling: verify provider currency and payment rail currency. Requires maker-checker. Do not normalize currency silently.

### DUPLICATE_PROVIDER_EVENT

Impact: usually no financial impact if idempotency held; possible control impact.  
Handling: verify duplicate webhook/callback was ignored and only one confirmation exists. Can resolve as no adjustment if database finality is single and evidence is consistent.

### DUPLICATE_PAYMENT_CONFIRMATION

Impact: definite financial/control impact.  
Handling: inspect confirmation rows and provider references. Requires maker-checker before closure. Do not delete duplicate confirmations from the review flow.

### PENDING_PROVIDER_SESSION

Impact: possible financial impact.  
Handling: check staleness window and provider state. Resolve no-adjustment if session expired/unpaid and no confirmation exists. Escalate if provider later shows paid.

### STALE_PENDING_ATTEMPT

Impact: usually no financial impact; operational/control impact.  
Handling: verify the attempt is expired and no provider paid evidence exists. Resolve with operational note if no customer impact.

### CONFIRMED_WITHOUT_EXIT_AUTHORIZATION

Impact: operational/control impact with potential customer impact.  
Handling: verify payment confirmation exists and exit authorization issuance failed or was suppressed. Remediation should issue or repair exit authorization through a separate approved operational command, not directly from reconciliation review.

### EXIT_AUTHORIZATION_WITHOUT_CONFIRMATION

Impact: definite control impact and possible financial impact.  
Handling: verify authorization source and payment evidence. Requires maker-checker before closure. Do not invalidate or alter authorization from the reconciliation review flow.

### GATE_CONSUMED_WITHOUT_CONFIRMATION

Impact: critical control impact and possible financial impact.  
Handling: treat as high-priority incident. Verify gate consumption, authorization, and payment evidence. Requires maker-checker and likely incident linkage. Financial recovery, if any, belongs to a separate approved operation.

## Maker-Checker Control Model

Maker-checker is required for:

- Financial adjustment recommendation.
- Manual closure of financial-impact exceptions.
- Marking provider evidence as accepted despite mismatch.
- Overriding reconciliation status or classification.
- Reopening closed exceptions.
- Any action that leads to future settlement, payout, refund, write-off, or manual payment correction.

Maker-checker is not required for:

- Adding notes.
- Assigning or reassigning a reviewer.
- Moving `OPEN` to `UNDER_REVIEW`.
- Marking purely informational duplicate-provider-event exceptions as reviewed when idempotency is proven and no financial impact exists.

Rules:

- Maker and checker must be different users.
- Checker must have a role with approval authority for the exception impact level.
- Approval must reference the exact run, item, exception, recommendation, reason, amount/currency if applicable, and evidence hashes or references.
- A rejected recommendation must not close the exception automatically.

## Audit Model

Every resolution action must create immutable audit evidence before or within the same transaction as the state change:

- Actor: user id or service identity id.
- Action type: assign, note, submit recommendation, approve, reject, resolve, close, reopen.
- Previous value and new value for changed fields.
- Reason code and human summary.
- Timestamp.
- Correlation id and causation id.
- Reconciliation run id.
- Reconciliation item id.
- Reconciliation exception id.
- Related payment attempt, provider session, payment confirmation, exit authorization, and gate consumption ids when available.

Recommended persistence:

- `audit.audit_events` with category `RECONCILIATION`.
- `audit.audit_trail_entries` for field-level before/after changes.
- `audit.evidence_links` for supporting provider exports, screenshots, or hash-only references.
- `events.domain_events` and `events.outbox_events` only for best-effort notifications.

Audit rows must be append-only. Historical audit, domain, and outbox rows must not be edited by exception resolution.

## Non-Mutation Rule

Reconciliation exception resolution must not directly mutate:

- `core.payment_attempts`
- `payments.provider_sessions`
- `core.payment_confirmations`
- `core.exit_authorizations`
- `gates.gate_authorization_consumptions`
- historical audit rows
- historical domain event rows
- historical outbox rows

Any correction must be represented as one of:

- Reconciliation resolution record.
- Adjustment recommendation.
- Future settlement adjustment.
- Separate approved financial or operational command.

The control model preserves database-authoritative payment finality and gate consumption. Review status must never become a substitute for provider verification, payment confirmation, exit authorization issuance, or gate-consume validation.

## Financial Impact Rules

| Classification | Impact category | Maker-checker |
| --- | --- | --- |
| `DUPLICATE_PROVIDER_EVENT` | No financial impact if single confirmation and idempotency proven; otherwise possible impact | Not required for no-impact closure; required if accepting mismatch or financial impact |
| `PENDING_PROVIDER_SESSION` | Possible financial impact | Required if provider paid evidence appears |
| `STALE_PENDING_ATTEMPT` | Usually no financial impact; operational aging issue | Not required for no-impact closure |
| `CONFIRMED_WITHOUT_EXIT_AUTHORIZATION` | Operational/control impact | Required if manual operational remediation is requested |
| `EXITPASS_CONFIRMED_PROVIDER_MISSING` | Possible financial impact | Required |
| `PROVIDER_PAID_EXITPASS_MISSING` | Definite financial/control impact | Required |
| `AMOUNT_MISMATCH` | Definite financial impact | Required |
| `CURRENCY_MISMATCH` | Definite financial impact | Required |
| `DUPLICATE_PAYMENT_CONFIRMATION` | Definite financial/control impact | Required |
| `EXIT_AUTHORIZATION_WITHOUT_CONFIRMATION` | Definite control impact and possible financial impact | Required |
| `GATE_CONSUMED_WITHOUT_CONFIRMATION` | Critical control impact and possible financial impact | Required |

## Required Future Schema Changes

Current schema can support a minimal manual state change flow, but not a controlled resolution workflow. Before implementation, add a migration equivalent to:

### `reconciliation.reconciliation_exception_reviews`

Purpose: immutable review notes and investigation steps.

Minimum fields:

- review id, exception id, run id, item id
- review action type
- note summary and details reference/hash
- actor user/service identity id
- created timestamp
- correlation id

### `reconciliation.reconciliation_exception_resolution_requests`

Purpose: maker-submitted recommendation.

Minimum fields:

- request id, exception id, run id, item id
- requested action
- recommended final status
- financial impact category
- amount/currency if applicable
- reason code and details reference/hash
- submitted_by, submitted_at
- request status
- correlation id

### `reconciliation.reconciliation_exception_resolution_approvals`

Purpose: checker approval or rejection.

Minimum fields:

- approval id, resolution request id, exception id
- approval decision
- approved/rejected_by, approved/rejected_at
- reason code and details reference/hash
- maker user id snapshot
- checker user id snapshot
- correlation id

Do not add settlement or payout tables in this migration. Financial posting remains a separate slice.

## API and Tooling Recommendations

Future implementation should provide:

- List exceptions by run, classification, status, severity, ticket reference, provider, and date.
- Assign exception.
- Add review note.
- Submit resolution recommendation.
- Approve or reject resolution recommendation.
- Resolve or close after approval where required.
- Reopen exception.
- Export exception audit trail.
- Export evidence package with audit event ids and evidence links.

All mutation APIs must be idempotent and concurrency-safe using `row_version` or equivalent optimistic concurrency.

## Security and RBAC Recommendations

Suggested roles:

- `ReconciliationViewer`: read runs, items, exceptions, exports, and audit trail.
- `ReconciliationReviewer`: assign, add notes, move to under review, submit recommendations.
- `ReconciliationApprover`: approve or reject recommendations; cannot approve own recommendation.
- `FinanceController`: approve financial-impact closure and future adjustment handoff.
- `SystemAuditor`: read-only access to all exception, audit, event, and evidence records.

Suggested permissions:

- `reconciliation.exception.read`
- `reconciliation.exception.assign`
- `reconciliation.exception.note.create`
- `reconciliation.exception.recommend_resolution`
- `reconciliation.exception.approve_resolution`
- `reconciliation.exception.reject_resolution`
- `reconciliation.exception.close`
- `reconciliation.exception.reopen`
- `reconciliation.exception.audit.export`

Sensitive permissions should set `identity.permissions.requires_audit = true`.

## Settlement Boundary

Exception resolution does not equal settlement.

Resolution means operations has reviewed a mismatch, recorded evidence, and decided the reconciliation status. It does not create payout, refund, write-off, merchant receivable, settlement batch, or ledger posting.

Any financial adjustment requires:

- Explicit maker recommendation.
- Checker approval.
- Separate settlement or finance posting logic.
- Separate audit trail and evidence package.

## Open Decisions for Darwin

- Whether to implement the future workflow as new reconciliation tables or reuse a general approval framework if one is introduced later.
- Whether exception status enum should be extended with `NEEDS_PROVIDER_CHECK`, `NEEDS_INTERNAL_CHECK`, `PENDING_APPROVAL`, and `APPROVED`, or whether those should remain reason codes on current statuses.
- Which role owns final approval for financial-impact exceptions: reconciliation approver, finance controller, or both.
- Whether review notes may contain free text in-table or must use evidence/reference storage with hashes.
- Required retention policy for provider evidence and exception review artifacts.
- Whether low-risk duplicate-provider-event closures can be single-actor when idempotency is proven by existing rows.
- Whether reopening a closed exception always requires maker-checker or only when financial impact exists.
- Whether future settlement adjustment requests should be created from reconciliation resolution or by a separate finance workflow.

## Implementation Guardrails for Next Slice

- Keep QRPH/PHP scoped to PAYMONGO for this reconciliation flow.
- Do not mutate payment finality, provider sessions, payment confirmations, exit authorizations, or gate consumptions from exception review.
- Do not make RabbitMQ or async eventing required for resolution persistence.
- Write audit evidence for every state transition.
- Require maker-checker for every financial-impact closure or override.
- Keep settlement and payout out of scope until explicitly started.
