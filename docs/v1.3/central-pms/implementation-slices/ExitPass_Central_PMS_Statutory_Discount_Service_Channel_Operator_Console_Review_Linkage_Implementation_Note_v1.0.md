# ExitPass Central PMS Statutory Discount Service-Channel Operator Console Review Linkage Implementation Note

## Purpose

This slice implements the Operator Console backend linkage required by the review-mediated service-channel authority model in `docs/v1.3/central-pms/reviews/ExitPass_Statutory_Discount_Service_Channel_Decision_Authority_Design_Decision_v1.0.md`.

Authenticated WebPay or Assisted Payment Terminal intake can already create one canonical statutory-discount decision-v2 in `AWAITING_REVIEW` with result `NOT_DECIDED`. This slice allows an authorized Operator Console reviewer to discover, inspect, approve, or reject that same canonical decision.

## Retained Boundaries

Retained shared routes:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

Retained Operator Console workflow routes remain unchanged. The existing draft, evidence, policy-resolution, validation, decision, apply-payable-basis, and readback behavior is preserved for Operator Console-originated workflows.

This slice adds only backend Operator Console review-linkage routes under the existing operational namespace:

- `GET /v1/ops/operator-console/statutory-discounts/reviews/pending`
- `GET /v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId}`
- `POST /v1/ops/operator-console/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId}/decision`

These routes are not service-channel routes and do not alter WebPay, APT, or Operator Console UI behavior.

## Review Discovery

The selected representation is an additive read model and linkage row keyed by `statutoryDiscountDecisionCommandId`. It stores only safe submitted service-channel facts and later reviewer attribution.

The queue exposes only decisions with:

- source channel `WEBPAY` or `ASSISTED_PAYMENT_TERMINAL`
- canonical command status `AWAITING_REVIEW`
- canonical decision result `NOT_DECIDED`
- review status `PENDING_REVIEW`

Supported filters are bounded to Site, Site Group, source channel, entitlement type, parking session, submitted time range, and pagination. The list operation does not mutate any decision or application state.

## Review Detail

Review detail exposes safe facts required by the Operator Console reviewer:

- canonical decision command ID
- parking session reference
- Site and Site Group
- entitlement type
- masked statutory identity reference
- document type
- issuing authority
- expiry date
- metadata-only evidence references
- requester attestation and notes
- source-channel attribution
- original tariff snapshot and amount facts where available
- submitted timestamp
- current canonical decision status and result
- current review status

It does not expose raw images, Base64 evidence, raw evidence bytes, full statutory IDs, unmasked identity data, internal staged-command table details, payment authority, fiscal authority, exit authorization, or gate state.

## Canonical Linkage

The canonical decision identity remains:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

The semantic source remains:

```text
statutory-discount-decision:sha256:v2
```

Source channel remains attribution only. The review linkage does not create a new decision table, decision namespace, semantic hash, idempotency namespace, application command, or payable-basis authority.

## Approval and Rejection

The review-decision route reuses the staged command service to complete the same canonical decision-v2:

- `APPROVE` transitions `AWAITING_REVIEW` / `NOT_DECIDED` to `COMPLETED` / `APPROVED`.
- `REJECT` transitions `AWAITING_REVIEW` / `NOT_DECIDED` to `COMPLETED` / `REJECTED`.
- same terminal replay returns the durable canonical result.
- opposite terminal retry returns deterministic conflict.

Reviewer identity, reviewer attestation, Site scope, device, shift, and existing Operator Console decision permission remain required where enforced by the access service. Original service-channel attribution is preserved separately from reviewer attribution.

## Replay and Recovery

Repeated review submission for an already completed canonical decision returns the durable canonical result when the requested terminal result matches. If canonical completion succeeded but review-linkage persistence was interrupted, a retry with the same requested terminal decision repairs the review linkage and returns the existing canonical decision.

Transport-only values do not create another canonical decision. Polling shared readback or Operator Console detail does not mutate review state.

## Conflict and Concurrency

Terminal-state protection in the staged command service serializes review completion. Concurrent equivalent approvals converge on one `APPROVED` result. Concurrent equivalent rejections converge on one `REJECTED` result. Concurrent approve/reject attempts leave one canonical terminal result and return deterministic conflict to the opposing decision.

No application-v1 command is created in any review path.

## Shared Readback

After approval, shared GET readback returns a terminal decision posture:

- `decisionCommandStatus=COMPLETED`
- `decisionResultStatus=APPROVED`
- `applicationRequested=false`
- `applicationCommandStatus=NOT_REQUESTED`
- no applied tariff snapshot
- no final discounted payable amount unless a prior compatible application already exists

After rejection, shared GET readback returns:

- `decisionCommandStatus=COMPLETED`
- `decisionResultStatus=REJECTED`
- `applicationRequested=false`
- `applicationCommandStatus=NOT_REQUESTED`
- safe reason or error posture

The service channel can observe the completed decision through the existing shared readback route, but post-approval application intent remains deferred.

## Operator Console Readback

The new review-detail route exposes the canonical decision command ID, original source channel, review status, safe submitted facts, reviewer attribution after completion, and no application state beyond `NOT_REQUESTED` for this slice. Historical Operator Console records remain readable through their existing readback routes.

## RBAC

Review discovery, review detail, and review completion use the existing Operator Console statutory-discount access-evaluation posture. Service-channel submit permissions do not grant review authority. WebPay and Assisted Payment Terminal identities cannot use the Operator Console review routes through source-channel request values.

## Persistence

The additive patch is:

- `infra/db/patches/ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql`

The validation SQL is:

- `infra/db/patches/validation/Validate_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql`

The patch creates `operator_console.statutory_discount_service_channel_reviews` as a safe linkage/read model keyed by the canonical decision command ID. It includes source channel, Site context, safe identity and evidence-reference facts, requester attestation, original tariff snapshot linkage, review status, reviewer attribution, correlation IDs, and timestamps.

Application-layer mapping alone is insufficient because Operator Console review discovery must survive process restarts and must preserve the exact safe submitted facts without fabricating a legacy draft or reviewer event. The table is not authoritative for statutory decisions; canonical decision-v2 remains authoritative.

The patch does not add payable-basis application objects, a new decision table, raw evidence columns, full identity columns, payment state, fiscal state, exit authorization, or gate state.

## Transaction and Recovery

Durable boundaries are separate:

1. service-channel pending-review intake creates or resolves canonical decision-v2.
2. intake stores safe review-linkage facts.
3. Operator Console review list/detail reads the linkage and canonical command.
4. reviewer authorization is evaluated and persisted.
5. canonical decision completion is persisted.
6. review-linkage completion is persisted.
7. response adaptation returns the legacy-compatible decision response.

No transaction spans human review time. Ordinary retries do not require manual database repair for the expected interruption points.

## Privacy

Evidence remains reference-only. This slice does not accept, store, hash, log, or return raw ID images, Base64 evidence, raw evidence bytes, full statutory ID numbers, unmasked identity values, restricted evidence in shared readback, or sensitive beneficiary values in errors. Privacy retention remains unresolved and is not defined here.

## Authority Boundaries

Preserved boundaries:

- WebPay and APT submit facts and display results only.
- Operator Console performs human entitlement review.
- Central PMS owns canonical decision persistence and future payable-basis authority.
- Vendor PMS or HikCentral remains raw session and live-tariff authority.
- POS Server remains a finalized-fact fiscal consumer.

This slice does not automate entitlement approval, let service channels approve themselves, calculate a new discount, change VAT treatment, apply payable basis, mark payment final, issue ExitAuthorization, trigger fiscal issuance, call HikCentral, call a payment provider, control a gate, modify WebPay, modify APT, or modify POS Server.

## Deferred Work

- Service-channel post-approval application intent.
- Channel-safe application readback hardening after application intent.
- WebPay runtime integration.
- Assisted Payment Terminal runtime or desktop integration.
- Channel authorization review after the required implementation slices merge.
- Canonical database promotion beyond the additive app-local patch posture.
- Privacy retention policy approval.
