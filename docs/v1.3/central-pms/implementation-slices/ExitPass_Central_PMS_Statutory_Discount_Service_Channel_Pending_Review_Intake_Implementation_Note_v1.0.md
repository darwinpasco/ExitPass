# ExitPass Central PMS Statutory Discount Service-Channel Pending Review Intake Implementation Note

## Purpose

This slice implements the first bounded step from `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Service_Channel_Decision_Authority_Design_Decision_v1.0.md`: authenticated `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` identities may submit permitted statutory-discount facts through the retained shared route and create or resolve one canonical decision-v2 command that waits for Operator Console review.

## Retained Routes

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

No new public intake or review route is added.

## Service-Channel Field Boundary

For initial pending-review intake, WebPay and Assisted Payment Terminal submit entitlement and reference facts only.

Required service-channel facts:

- `parkingSessionId`
- `entitlementType`
- `maskedIdReference`
- `idDocumentType`
- `issuingAuthority`
- `requesterAttestation`
- `requestReference`
- `Idempotency-Key`
- `X-Correlation-Id`

Optional facts:

- `siteId`
- `siteGroupId`
- `expiryDate`
- `evidenceReferences`
- `attestationNotes`
- `originalTariffSnapshotId`
- safe beneficiary/session display metadata already present on the shared DTO

Server-derived facts:

- authenticated service identity
- effective source channel
- authorization context
- canonical decision command ID
- server timestamps
- decision command status

Prohibited facts:

- `decision`
- `decisionReasonCode`
- `reviewerUserId`
- `reviewerAttestation`
- `operatorDeviceBindingId`
- `operatorShiftId`
- raw evidence, images, Base64 payloads, full statutory IDs, and authoritative payable-basis amounts

`applyPayableBasis=true` does not bypass review in this slice. Service-channel requests without a decision outcome are classified as pending review, no application command is created, and no payable-basis mutation is performed. Post-approval service-channel application intent remains deferred.

## Pending Review Lifecycle

The staged decision command model now includes:

- decision command status: `AWAITING_REVIEW`
- decision result: `NOT_DECIDED`
- recovery classification: `AWAITING_REVIEW`
- recovery action: `WAIT_FOR_REVIEW` for decision state and `POLL_READBACK` for application/readback polling posture where applicable
- retryable: `false`
- one-shot completion: `false`

`AWAITING_REVIEW` is durable, not a technical failure, not an approval, not a rejection, and not equivalent to `PROCESSING`.

## Canonical Identity and Semantics

Decision-v2 business identity remains:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

Decision-v2 semantic source remains:

```text
statutory-discount-decision:sha256:v2
```

Source channel, request reference, correlation ID, idempotency key, transport headers, generated command IDs, and server timestamps remain non-semantic. For service-channel intake, operator/reviewer/device/shift facts are not accepted as decision-stage facts. The service-channel actor is authenticated and enforced by the endpoint, but is not asserted into the decision-v2 material legal facts for pending-review equality.

## Replay, Conflict, and Concurrency

Equivalent WebPay or APT pending-review submissions resolve to the same canonical decision command. Changing request reference, correlation ID, idempotency key, or source channel does not create a second decision. Changing material entitlement, identity metadata, evidence, attestation, tariff, or decision-stage facts produces canonical semantic conflict.

Concurrent equivalent WebPay, APT, or cross-channel submissions converge through the existing decision business-identity lock and unique index. They create one command in `AWAITING_REVIEW`, no application-v1 command, and no payable-basis mutation.

## RBAC and Source Channel

The endpoint continues to derive effective source channel from authenticated RBAC permissions:

- `statutory-discounts.decision.submit.webpay`
- `statutory-discounts.decision.submit.assisted-payment-terminal`
- `statutory-discounts.decision.submit.operator-console`

Request-body `sourceChannel` must match the authenticated channel. Ambiguous identities with more than one submit-channel permission are rejected. Readback remains governed by the existing statutory-discount read permission.

## Persistence

No new table or command namespace is introduced. The additive patch `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql` expands existing decision-command check constraints to allow:

- `command_status = 'AWAITING_REVIEW'`
- `result_classification = 'AWAITING_REVIEW'`
- `recovery_classification = 'AWAITING_REVIEW'`

The validation patch `infra/db/patches/validation/Validate_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql` verifies the expanded constraints, existing decision uniqueness, application uniqueness, and absence of raw evidence or full-identity columns.

## Privacy

Evidence remains reference-only. This slice does not accept, store, hash, log, or return raw ID images, Base64 evidence, raw evidence bytes, full statutory ID numbers, or unmasked identity values. Evidence retention policy remains unresolved and is not defined by this slice.

## Authority Boundaries

This slice preserves:

- Vendor PMS or HikCentral as raw parking-session and live-tariff authority.
- WebPay and APT as fact-submission channels only.
- Operator Console as the future human-review workflow.
- Central PMS as canonical decision persistence authority.
- Central PMS as future payable-basis application authority.
- POS Server as finalized-fact fiscal consumer.

This slice does not approve or reject entitlement, apply payable basis, create application-v1, mark payment final, issue ExitAuthorization, trigger fiscal issuance, call HikCentral, call a payment provider, control gates, change VAT behavior, or activate local ordinances.

## Deferred Work

- Operator Console review linkage for service-channel-originated canonical decisions.
- Post-approval service-channel application intent.
- WebPay runtime integration.
- APT runtime or desktop integration.
- Channel authorization review after the required implementation slices merge.
- Canonical database promotion beyond the additive app-local patch posture.
- Privacy retention policy approval.
