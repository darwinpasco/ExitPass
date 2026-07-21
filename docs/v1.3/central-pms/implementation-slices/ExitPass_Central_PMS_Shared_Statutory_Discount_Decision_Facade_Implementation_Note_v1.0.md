# ExitPass Central PMS Shared Statutory Discount Decision Facade Implementation Note v1.0

## Purpose

This slice adds a shared, channel-neutral Central PMS statutory-discount command and readback facade after the system-wide baseline audit in `docs/v1.3/operator-console/reviews/ExitPass_Statutory_Discount_System_Wide_Baseline_Audit_v1.0.md`.

The facade is not a statutory-discount engine rewrite. It reuses the merged Operator Console statutory-discount draft, metadata-only evidence, decision, payable-basis application, policy-resolution, and readback application path.

## Route And Contract Ownership

- Command: `POST /v1/statutory-discounts/decisions`
- Readback: `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`
- Contract namespace: `ExitPass.CentralPms.Contracts.StatutoryDiscounts`
- Application namespace: `ExitPass.CentralPms.Application.StatutoryDiscounts`

Existing Operator Console routes under `/v1/ops/operator-console/statutory-discounts/*` remain backward-compatible.

## Reused Implementation Path

The facade maps the canonical command to these existing services:

- `IOperatorConsoleStatutoryDiscountDraftService`
- `IOperatorConsoleStatutoryDiscountEvidenceService`
- `IOperatorConsoleStatutoryDiscountDecisionService`
- `IOperatorConsoleStatutoryDiscountApplyPayableBasisService`
- `IOperatorConsoleStatutoryDiscountReadService`

It does not introduce a channel-local calculator or a separate policy resolver.

## Source-Channel Attribution

Supported source-channel values are:

- `OPERATOR_CONSOLE`
- `WEBPAY`
- `ASSISTED_PAYMENT_TERMINAL`

Source channel is attribution and workflow context only. It does not grant calculation, legal interpretation, approval, payment finality, fiscal issuance, ExitAuthorization, or gate authority to the source channel.

## Idempotency And Semantic Hash

Durable idempotency is recorded in `discounts.statutory_discount_decision_commands`.

- Idempotency scope: `statutory-discount-decision:{parkingSessionId}:{entitlementType}`
- Semantic hash version: `statutory-discount-decision:sha256:v1`
- Hash algorithm: SHA-256 over normalized material request facts.

Command identity is the durable `statutoryDiscountDecisionCommandId` assigned by Central PMS. `RequestReference` remains a caller-supplied business/correlation reference and is unique for readback/audit correlation, but it is not part of the business idempotency scope and cannot create a second authoritative decision for the same parking session and entitlement.

Source channel is attribution and workflow context only. It is persisted on the first accepted command, returned on replay, and is not part of semantic equality or uniqueness.

Semantic hash inputs include parking session, Site context, safe ticket/plate references, entitlement type, document type, issuing authority, expiry date, masked ID reference, metadata-only evidence references and verification outcomes, actor/reviewer references, attestations, requested decision, payable-basis application flag, and original tariff snapshot reference.

Semantic hash exclusions include `SourceChannel`, `RequestReference`, `Idempotency-Key`, `X-Correlation-Id`, generated command IDs, timestamps, raw evidence payloads, raw ID images, full statutory ID numbers, logs, and response-only values.

## Replay And Conflict Behavior

- Same business scope and same semantic hash returns the original canonical command result as `IDEMPOTENT_REPLAY`, even when the replay comes from another source channel, another idempotency key, or another request reference.
- Same business scope with different material facts returns `IDEMPOTENCY_SEMANTIC_CONFLICT`.
- Same request reference reused for a different parking-session/entitlement command resolves to the original command and conflicts when the semantic facts differ.
- A command still in `PROCESSING` can be recovered only by the same idempotency key. A different key for the same business scope receives `STATUTORY_DISCOUNT_DECISION_IN_PROGRESS`.
- Replay does not call the existing payable-basis application path again.

Cross-channel concurrency is serialized by a PostgreSQL session advisory lock over the business idempotency scope for the full facade operation. The database also enforces `ux_statutory_discount_decision_commands__business_identity` on `(parking_session_id, entitlement_type)`, `ux_statutory_discount_decision_commands__idempotency` on `(idempotency_scope, idempotency_key)`, and `ux_statutory_discount_decision_commands__request_reference` on `request_reference`.

The facade operation is recoverable rather than a single database transaction across all reused Operator Console services. It does not report success until the canonical command is completed with the durable result. If orchestration fails after command creation, the command remains `PROCESSING`; exact same-key replay may recover through existing downstream idempotency keys, while different-key replay is blocked to avoid duplicate draft/evidence/payable-basis side effects.

Patch validation is covered by `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql` and `StatutoryDiscountDecisionFacadeRepositoryTests`.

## RBAC Posture

Both shared routes use Central PMS RBAC metadata:

- `CentralPmsStatutoryDiscountDecisionSubmit`
- `CentralPmsStatutoryDiscountDecisionRead`

Submit callers must carry a source-channel-specific permission matching the request body:

- `statutory-discounts.decision.submit.operator-console`
- `statutory-discounts.decision.submit.webpay`
- `statutory-discounts.decision.submit.assisted-payment-terminal`

An active service identity by itself is not sufficient for the shared statutory-discount submit/read policies. Source channel in the body does not grant authority.

## Privacy Posture

The shared request accepts metadata-only evidence references. It does not accept raw ID-image bytes, raw evidence payloads, or full unmasked statutory ID numbers. Full ID-like numeric values are rejected before statutory-discount workflow execution.

Readback exposes canonical decision, policy, payable-basis, and evidence posture only. It does not expose restricted evidence content.

## Authority Boundaries

Preserved boundaries:

- Vendor PMS/HikCentral remains authoritative for raw parking-session lifecycle and live tariff computation.
- Central PMS remains the statutory-discount decision and payable-basis authority.
- WebPay and Assisted Payment Terminal submit facts and display approved results only.
- Operator Console remains a controlled workflow surface.
- POS Server fiscalizes finalized facts and renders authoritative Sales Invoice output only.
- The shared facade does not mark payment final, issue ExitAuthorization, control gates, or call HikCentral directly.

## Deliberately Unsupported

This slice does not add or activate:

- local ordinance engine behavior
- ordinance seed data
- resident-only or driver/passenger rules
- free parking duration, capped exemption, overnight, valet, facility, or standalone parking-business exclusions
- multiple-beneficiary allocation
- coupon stacking changes
- VAT treatment changes
- Management Platform configuration surfaces
- POS Server authority changes

## Bruno And Manual Validation Posture

The Bruno proof files demonstrate submit, replay, semantic conflict, readback, and unsafe ID rejection scenarios. Local automated Bruno execution remains dependent on an available Bruno CLI or supported repository runner; when unavailable, run the collection manually against an authenticated Central PMS instance and verify the captured `statutoryDiscountDecisionCommandId` is reused on replay.

Manual validation must still cover authenticated Operator Console, WebPay-service, and APT-service submissions; same-key replay; same-scope cross-channel replay; semantic conflict; readback; absence of raw evidence/full IDs in logs and responses; payment initiation consuming the resulting payable basis; and absence of payment finality, fiscal issuance, ExitAuthorization, or gate side effects from the statutory-discount command itself.
