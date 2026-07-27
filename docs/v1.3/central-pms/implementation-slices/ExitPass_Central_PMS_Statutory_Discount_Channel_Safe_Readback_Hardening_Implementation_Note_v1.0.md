# ExitPass Central PMS Statutory Discount Channel-Safe Readback Hardening Implementation Note v1.0

## Purpose

This note records the bounded Central PMS shared statutory-discount readback hardening slice for WebPay and Assisted Payment Terminal consumers.

The slice adds durable, channel-safe Site, VAT, payable-basis, and readiness facts to the retained shared statutory-discount routes:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

No WebPay client, APT desktop, Operator Console UI, statutory calculation, VAT rule, payment-finality, fiscal-issuance, ExitAuthorization, gate, or canonical database change is included.

## Governing Gaps

The canonical-only runtime revalidation and concurrency-recovery work proved that decision-v2, application-v1, review-mediated approval, payable-basis application, replay, restart recovery, and payment initiation are durable after canonical database promotion.

The remaining shared readback gaps were:

- `siteId`
- `siteGroupId`
- explicit VAT-exclusive basis facts
- explicit VAT amount facts
- channel-safe readiness adaptation for WebPay and Assisted Payment Terminal consumers

## Durable Sources

`siteId` and `siteGroupId` are exposed from the durable service-channel review linkage when present. The shared facade enriches POST and GET results through `IStatutoryDiscountServiceChannelReviewRepository.GetAsync(statutoryDiscountDecisionCommandId, correlationId, cancellationToken)`.

The review linkage is created from canonical service-channel intake and Operator Console review records. It preserves original source-channel attribution separately from reviewer attribution and is not a second decision authority.

The VAT and payable-basis values are exposed from canonical decision/application rows:

- original amount: decision-v2 gross amount
- VAT-exclusive basis amount: application-v1 approved VAT-exclusive amount, falling back to decision-v2 VAT-exclusive amount
- VAT amount: application-v1 approved VAT amount, falling back to decision-v2 VAT amount
- statutory discount amount: application-v1 approved discount amount, falling back to decision-v2 statutory discount amount
- final payable amount: application-v1 approved final payable amount, falling back to decision-v2 net payable amount
- currency: application-v1 currency, falling back to decision-v2 currency

The shared adapter does not recalculate statutory discount or VAT. `vatTreatment` is reported as `VAT_EXCLUSIVE` only when durable VAT-exclusive or VAT amount facts are present.

## Response Additions

The shared response DTO adds backwards-compatible nullable fields:

- `siteId`
- `siteGroupId`
- `vatExclusiveBasisAmountMinorUnits`
- `vatAmountMinorUnits`
- `vatTreatment`
- `payableBasisReady`
- `payableBasisReadinessStatus`
- `payableBasisReadinessAction`

Amounts follow the existing Central PMS minor-unit convention and remain paired with the existing `currency` field. Missing historical facts remain `null`; they are not represented as zero.

## POST and GET Parity

POST response adaptation and GET readback now use the same facade enrichment path. For the same durable decision/application state, both routes report the same Site context, VAT-exclusive amount, VAT amount, original amount, discount amount, final payable amount, currency, and readiness posture.

GET remains read-only and does not mutate decision, application, tariff-snapshot, payment, fiscal, or gate state.

## Readiness Adaptation

The hardened response derives readiness from durable decision/application state:

- `AWAITING_REVIEW`: `payableBasisReady=false`, action `POLL_READBACK`
- `COMPLETED / APPROVED` with no application requested: `payableBasisReady=false`, action `SUBMIT_APPLICATION_INTENT`
- application `RECEIVED` or `PROCESSING`: `payableBasisReady=false`, existing recovery action
- application `APPLIED`: `payableBasisReady=true` only when applied tariff snapshot, final payable amount, and currency are present
- application `FAILED_RETRYABLE`: `payableBasisReady=false`, retryable recovery posture
- application `FAILED_NON_RETRYABLE`: `payableBasisReady=false`, terminal recovery posture
- `COMPLETED / REJECTED`: `payableBasisReady=false`, action `DO_NOT_RETRY`
- applied historical rows missing required durable facts: `payableBasisReady=false`, status `REQUIRED_FACTS_UNAVAILABLE`

This is an adapter over existing durable statuses, not a parallel state machine.

## WebPay Consumer Posture

The shared response now gives WebPay durable facts to:

- show awaiting-review, approved, rejected, processing, applied, retryable-failure, and terminal-failure states
- recover after browser refresh by polling GET
- request application only after approval
- wait for `payableBasisReady=true` before payment initiation
- display authoritative Site context and VAT/discount breakdown
- avoid local statutory or VAT calculation
- avoid duplicate application on replay

WebPay integration remains subject to a separate readiness re-authorization audit.

## APT Consumer Posture

The shared response now gives APT durable facts to:

- recover after process or terminal restart by polling GET
- distinguish pending review, approval, rejection, processing, applied, retryable failure, and terminal failure
- confirm Site and Site Group scope
- obtain authoritative VAT and discount breakdown
- confirm that the payable basis is durably ready before any future cash-readiness decision
- avoid local statutory or VAT calculation
- avoid dependence on Operator Console-only reviewer, device, or shift facts

This slice does not authorize APT cash acceptance.

## Security and Privacy

Service-channel readback still excludes:

- full statutory ID values
- raw ID images
- Base64 evidence
- raw evidence bytes
- reviewer-sensitive notes
- Operator Console device or shift identity
- permission internals
- database row details
- SQL text or table names
- payment-provider payloads
- HikCentral data
- secrets or stack traces

Masked identity and evidence references remain reference-only and are exposed only through existing safe contracts.

## Reviewer Attribution

Reviewer identity, reviewer notes, Operator Console device binding, and Operator Console shift are not added to service-channel readback. WebPay and APT require the canonical decision outcome, decision timestamp, Site scope, durable application result, and authoritative payable-basis facts, not Operator Console-only review facts.

## Payment Initiation Consistency

Payment initiation continues to consume the effective applied tariff snapshot. The readback hardening surfaces the same applied snapshot and final payable amount already produced by the payable-basis writer. It does not recalculate the discount, recalculate VAT privilege, apply the discount twice, change payment-provider behavior, or alter payment finality.

## Concurrency Recovery Preservation

The SQLSTATE `40P01` recovery posture from the application-intent concurrency fix remains unchanged:

- applied winner is replayed
- processing state returns safe recovery posture
- no durable winner returns retryable temporary-unavailable posture
- semantic conflict remains conflict
- terminal failure remains terminal

Readiness adaptation follows those recovery states and does not change lock ordering or mutation behavior.

## Database Posture

No database patch or canonical schema change is included. The hardened fields are read from already promoted canonical rows:

- decision-v2 command
- application-v1 command
- service-channel review linkage
- statutory validation and payable-basis linkage
- applied tariff snapshot and payable-basis application rows

If a historical row lacks one of the newly exposed facts, the response remains nullable and not payable-ready instead of fabricating data.

## Tests and Validation

Focused coverage was added or extended for:

- additive shared response contract serialization
- nullable historical readback posture
- POST/GET Site parity
- POST/GET VAT-exclusive and VAT amount parity
- POST/GET original, discount, final payable, and currency parity
- pending-review readiness
- approved-not-applied readiness
- rejected readiness
- applied payable-ready posture
- restart-style GET reconstruction
- payment initiation using the applied snapshot
- no reviewer-sensitive fields in shared contract

The canonical disposable PostgreSQL fixture remains the proof-grade database baseline. Retired statutory application-local patches are not required.

## Deferred Work

Deferred work remains:

- docs-only WebPay/APT readiness re-authorization audit
- any WebPay client implementation
- any APT desktop implementation
- any future channel-specific UX or cash-readiness changes authorized by the readiness audit

## Authorization

This implementation note does not authorize channel integration.

WebPay integration: not authorized yet
APT integration: not authorized yet
