# ExitPass Central PMS Statutory Discount Channel Contract Readiness Implementation Note v1.0

## Purpose

This slice stabilizes the shared Central PMS statutory-discount decision contract for later WebPay and Assisted Payment Terminal integration. It does not implement WebPay or APT flows, change Operator Console UI behavior, add local ordinance support, or alter statutory calculation, VAT, payment-finality, ExitAuthorization, fiscal issuance, or gate behavior.

## Stable Request Contract

The shared command route remains:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

The request DTO remains `StatutoryDiscountDecisionRequest`. The authenticated server context derives the effective source channel and actor identity before building `StatutoryDiscountDecisionCommand`; body `sourceChannel` is now a required assertion that must match the authenticated channel, not an authority grant.

## Request-Field Matrix

| Field | OPERATOR_CONSOLE | WEBPAY | ASSISTED_PAYMENT_TERMINAL |
| --- | --- | --- | --- |
| `parkingSessionId` | REQUIRED | REQUIRED | REQUIRED |
| `siteId` | OPTIONAL | OPTIONAL | OPTIONAL |
| `siteGroupId` | OPTIONAL | OPTIONAL | OPTIONAL |
| `entitlementType` | REQUIRED | REQUIRED | REQUIRED |
| beneficiary metadata | REQUIRED where current workflow requires it | REQUIRED where current workflow requires it | REQUIRED where current workflow requires it |
| masked statutory ID metadata | REQUIRED | REQUIRED | REQUIRED |
| evidence references | OPTIONAL metadata only | OPTIONAL metadata only | OPTIONAL metadata only |
| evidence verification outcomes | OPTIONAL metadata only | OPTIONAL metadata only | OPTIONAL metadata only |
| `actorUserId` | SERVER_DERIVED; body value must match if supplied | SERVER_DERIVED; body value must be omitted or match service identity | SERVER_DERIVED; body value must be omitted or match service identity |
| `reviewerUserId` | OPTIONAL | PROHIBITED | PROHIBITED |
| device reference | OPTIONAL | PROHIBITED | PROHIBITED |
| shift reference | OPTIONAL | PROHIBITED | PROHIBITED |
| attestation facts | OPTIONAL/REQUIRED by current workflow state | OPTIONAL | OPTIONAL |
| decision facts | OPTIONAL | PROHIBITED | PROHIBITED |
| `applyPayableBasis` | OPTIONAL | PROHIBITED | PROHIBITED |
| `tariffSnapshotId` / `originalTariffSnapshotId` | OPTIONAL when applying payable basis | PROHIBITED until channel approval workflow is implemented | PROHIBITED until channel approval workflow is implemented |
| `requestReference` | REQUIRED caller reference; not semantic identity | REQUIRED caller reference; not semantic identity | REQUIRED caller reference; not semantic identity |
| correlation ID | REQUIRED header; transport only | REQUIRED header; transport only | REQUIRED header; transport only |
| `Idempotency-Key` | REQUIRED header | REQUIRED header | REQUIRED header |
| `sourceChannel` | REQUIRED assertion; server-verified | REQUIRED assertion; server-verified | REQUIRED assertion; server-verified |

Raw ID images, Base64 evidence payloads, raw document payloads, and full statutory ID numbers remain prohibited.

## Stable Result Vocabulary

`StatutoryDiscountDecisionResponse` now exposes explicit client fields:

- `commandStatus`
- `clientResultStatus`
- `resultClassification`
- `errorCode`
- `retryable`
- `recoveryClassification`
- `recoveryAction`
- `statutoryDiscountDecisionCommandId`
- `correlationId`

Stable `clientResultStatus` values are:

- `CREATED_DURABLY_COMPLETED`
- `IDEMPOTENT_REPLAY`
- `SEMANTIC_CONFLICT`
- `IN_PROGRESS`
- `RECOVERABLE_USING_ORIGINAL_KEY`
- `APPROVED`
- `REJECTED_OR_NON_APPROVED`
- `VALIDATION_FAILURE`
- `UNSAFE_IDENTITY_INPUT`
- `NOT_FOUND`
- `TEMPORARILY_UNAVAILABLE`
- `RETRYABLE_FAILURE`
- `NON_RETRYABLE_FAILURE`

## HTTP And Error Mappings

| Condition | HTTP | Safe error/status |
| --- | --- | --- |
| newly accepted/completed command | `201 Created` | response `clientResultStatus` from durable result |
| idempotent replay | `200 OK` | `IDEMPOTENT_REPLAY` |
| semantic conflict | `409 Conflict` | `IDEMPOTENCY_SEMANTIC_CONFLICT`, `SEMANTIC_CONFLICT` |
| in-progress different key | `409 Conflict` | `STATUTORY_DISCOUNT_DECISION_IN_PROGRESS`, `IN_PROGRESS` |
| in-progress original key | `201 Created` with processing result | `RECOVERABLE_USING_ORIGINAL_KEY` |
| unsupported channel | `400 Bad Request` | `UNSUPPORTED_SOURCE_CHANNEL` |
| channel mismatch | `403 Forbidden` | `CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH` |
| prohibited channel field | `400 Bad Request` | `STATUTORY_DISCOUNT_CHANNEL_FIELD_PROHIBITED` |
| unsafe identifier | `400 Bad Request` | `UNSAFE_IDENTIFIER_REJECTED`, `UNSAFE_IDENTITY_INPUT` |
| missing readback reference | `404 Not Found` | `STATUTORY_DISCOUNT_DECISION_NOT_FOUND`, `NOT_FOUND` |

## Retryability And Recovery

Exact completed replay returns the original canonical result. A processing command replayed with the original idempotency key returns a recoverable processing result without re-executing draft, evidence, decision, or payable-basis stages. A different key for the same in-progress business identity receives `STATUTORY_DISCOUNT_DECISION_IN_PROGRESS`.

The idempotency business identity remains:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

`sourceChannel`, `requestReference`, correlation ID, generated IDs, and transport facts remain excluded from business uniqueness. Material beneficiary, evidence, attestation, decision, and payable-basis facts remain semantic-hash inputs.

## Authenticated Channel Derivation

The effective channel is derived from authenticated permissions:

- `statutory-discounts.decision.submit.operator-console` -> `OPERATOR_CONSOLE`
- `statutory-discounts.decision.submit.webpay` -> `WEBPAY`
- `statutory-discounts.decision.submit.assisted-payment-terminal` -> `ASSISTED_PAYMENT_TERMINAL`

The request body cannot grant channel authority. If body `sourceChannel` differs from the authenticated channel, Central PMS rejects the request with `CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH`. If an authenticated identity maps to multiple source channels, Central PMS rejects it as ambiguous instead of allowing the body to choose.

## Payable-Basis Linkage

Vendor parking payable-basis readback now exposes safe canonical statutory-discount linkage when the effective applied tariff snapshot is backed by the shared façade:

- `statutoryDiscountDecisionCommandId`
- `statutoryDiscountValidationId`
- `statutoryDiscountApplicationId`
- `originalTariffSnapshotId`
- `effectiveTariffSnapshotId`
- `appliedTariffSnapshotId`
- `statutoryDiscountPolicyReferenceId`
- `policyResolutionBasis`
- `statutoryDiscountEntitlementType`
- `statutoryDiscountAmountMinorUnits`
- `statutoryDiscountFinalPayableMinorUnits`
- `statutoryDiscountDecisionTimestamp`

Payment initiation still consumes the effective applied tariff snapshot. It does not recalculate statutory discounts.

## POS Fiscal Linkage

Central PMS POS fiscal request mapping now carries safe finalized statutory-discount reference fields on discount references:

- canonical statutory decision command reference
- entitlement type
- applied policy reference
- original and applied tariff snapshot references
- original amount
- VAT-exclusive basis amount
- VAT treatment
- discount amount
- final payable amount
- decision timestamp
- source-channel attribution

These facts participate in the existing POS fiscal semantic hash when present. Known correlation-style transport dictionary keys are excluded from fiscal semantic hash calculation. POS Server remains a finalized-fact consumer and does not determine entitlement or recalculate statutory discounts.

## Database Baseline Posture

The shared façade still depends on:

- `infra/db/patches/ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql`
- `infra/db/patches/validation/Validate_StatutoryDiscountDecisionFacade_v1.3.sql`

The repository’s existing disposable PostgreSQL tests apply and validate that patch. This slice did not add a migration or duplicate database objects. Promotion into the canonical clean-environment database baseline remains a release/database-owner action outside this implementation slice.

## Security And Privacy

The shared contract continues to reject unsafe full statutory ID-like values and does not accept raw ID images, Base64 evidence, raw document payloads, or full statutory ID numbers. Readback exposes only safe references, identifiers, status, and amount facts.

## Remaining WebPay And APT Blockers

WebPay and APT integration must still implement authenticated service identity, request construction against this matrix, evidence-reference handling, manual/live route validation, and environment Bruno execution. Neither channel may calculate, approve, or apply the statutory discount independently.
