# ExitPass Central PMS Statutory-Discount Staged Canonical Commands Implementation Note v1.0

## Purpose

This slice establishes internal Central PMS staged statutory-discount command contracts and persistence for the approved staged canonical design. It adds durable decision-v2 and payable-basis-application-v1 command boundaries without changing public shared routes, Operator Console route behavior, WebPay, APT, POS Server behavior, statutory calculations, VAT treatment, payment finality, ExitAuthorization, fiscal issuance, or gate behavior.

## Decision-v2 Identity

Decision-v2 business identity:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

The idempotency scope is the same business identity. Source channel remains audit attribution only and is not a business-identity dimension. Request reference and correlation ID are transport/correlation facts and are not semantic identity.

## Decision-v2 Semantic Hash

Decision-v2 semantic source version:

```text
statutory-discount-decision:sha256:v2
```

Semantic inputs include parking session, Site/Site Group, ticket/plate references, entitlement type, safe beneficiary metadata, masked or hashed statutory identity metadata, evidence references and verification outcomes, attestation facts, actor/reviewer references, decision facts, policy-resolution references, local-ordinance-applied flag as stored evidence only, original tariff snapshot reference, and original tariff facts needed to reproduce the decision-stage result.

Semantic exclusions include `applyPayableBasis`, application command status, application command ID, application result, request reference, correlation ID, Idempotency-Key, transport headers, generated IDs, server timestamps, response-only fields, raw evidence bytes, raw images, Base64 evidence, full statutory IDs, and display-only text.

The existing `statutory-discount-decision:sha256:v1` hash remains readable and unchanged. V1 records are not recalculated as v2 and are not backfilled by this slice.

## Decision States

Decision command states:

- `RECEIVED`
- `PROCESSING`
- `COMPLETED`
- `FAILED_RETRYABLE`
- `FAILED_NON_RETRYABLE`

Decision result states:

- `APPROVED`
- `REJECTED`
- `NOT_DECIDED`

Recovery classifications reuse the existing shared vocabulary: `NONE`, `READ_CANONICAL_RESULT`, `RETRY_ORIGINAL_IDEMPOTENCY_KEY`, `WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY`, `CORRECT_REQUEST_REQUIRED`, and `NOT_RECOVERABLE`.

## Application-v1 Identity

Payable-basis-application-v1 business identity:

```text
statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}
```

The idempotency scope is the same application business identity. One canonical application command is allowed for one canonical decision command. The database enforces this with unique business-identity and decision-command indexes.

## Application-v1 Semantic Hash

Application-v1 semantic source version:

```text
statutory-discount-payable-basis-application:sha256:v1
```

Semantic inputs include canonical decision command ID, parking session, Site, entitlement type, statutory validation reference where present, original/target/applied tariff snapshot references, policy-resolution linkage, approved discount amount, approved VAT treatment fields already stored by current behavior, approved final payable amount, and currency.

Semantic exclusions include source channel as a uniqueness dimension, request reference, correlation ID, Idempotency-Key, generated command IDs, server timestamps, transport metadata, raw evidence, beneficiary display metadata not required for payable-basis integrity, and full statutory IDs.

## Application States

Application command states:

- `RECEIVED`
- `PROCESSING`
- `APPLIED`
- `FAILED_RETRYABLE`
- `FAILED_NON_RETRYABLE`

Application result classifications:

- `APPLIED`
- `IDEMPOTENT_REPLAY`
- `SEMANTIC_CONFLICT`
- `DECISION_NOT_APPROVED`
- `DECISION_NOT_FOUND`
- `IN_PROGRESS`
- `RETRYABLE_FAILURE`
- `NON_RETRYABLE_FAILURE`

An application command can be accepted only when the referenced canonical decision exists, is `COMPLETED`, and has decision result state `APPROVED`.

## Persistence

The patch `infra/db/patches/ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql` extends `discounts.statutory_discount_decision_commands` with staged decision-v2 state/readback fields and allows both v1 and v2 decision semantic source versions. Existing v1 columns, constraints, and readback behavior remain intact.

The patch adds `discounts.statutory_discount_payable_basis_application_commands` for canonical application command identity, semantic hash, replay/conflict status, safe linkage, and result state. This table references the existing decision command table and can optionally link to existing validation, payable-basis application, and tariff snapshot rows when a later orchestration slice performs the actual payable-basis mutation.

Key indexes and constraints:

- `ux_statutory_discount_decision_commands__business_identity_text`
- `ux_stat_discount_pba_commands__business_identity`
- `ux_stat_discount_pba_commands__decision_command`
- `ux_stat_discount_pba_commands__idempotency`
- `ux_stat_discount_pba_commands__request_reference`
- source-version, hash-format, command-state, result-state, recovery, entitlement, channel, and non-negative amount checks

Application-layer mapping alone is insufficient for exactly-once application because concurrent calls must be prevented at the database boundary. The application command table therefore carries unique business and decision-command constraints.

## Idempotency, Replay, and Conflict

Decision-v2 exact replay returns the existing command when the business identity and semantic hash source/value match. A changed material fact under the same business identity returns deterministic semantic conflict. Processing decisions are recoverable with the original idempotency key; different keys receive in-progress/recovery guidance.

Application-v1 exact replay returns the existing command when the application business identity and semantic hash source/value match. A changed material application fact under the same decision identity returns deterministic semantic conflict. Processing applications are recoverable with the original idempotency key.

## Concurrency and Transactions

The repository serializes decision creation with a PostgreSQL advisory lock over the decision business identity. Application creation uses an advisory lock over the application business identity. Database unique indexes remain the final exactly-once guard. State updates are durable repository operations. This slice does not execute the actual payable-basis mutation; that remains in the existing production path until the later orchestration/convergence slice.

## Historical Compatibility

Existing `statutory-discount-decision:sha256:v1` rows remain readable and unchanged. The patch does not destructively update historical rows, does not recalculate v1 hashes as v2, does not create application commands for historical records, and does not perform backfill. V1 and v2 command records can coexist in the same decision command table.

## Privacy

The internal staged models and SQL store safe references, hashes, masked values, verification outcomes, reason codes, actor/reviewer references, and command metadata only. They do not add raw statutory ID images, Base64 evidence, raw evidence bytes, full statutory ID numbers, unmasked identity values, or raw evidence payload columns. Hash helpers reject full-ID-like unmasked values and raw evidence payload markers.

## Authority Boundaries

Central PMS remains statutory-decision and payable-basis authority. This slice does not mark payment final, issue ExitAuthorization, trigger fiscal issuance, call HikCentral, call a payment provider, command gates, modify POS Server, modify WebPay, modify APT, modify Operator Console UI or public route behavior, activate ordinances, or change VAT/statutory calculation behavior.

## Deliberately Deferred

The public one-shot routes are not refactored in this slice:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

Operator Console convergence is not implemented. WebPay and APT integration remain blocked until the shared one-shot route orchestrates staged decision/application commands and the legacy Operator Console path converges onto the same canonical staged operations.

## Validation

Focused validation for this slice covers:

- decision-v2 creation, exact replay, semantic conflict, original-key recovery, source-version-aware comparison, approved/rejected/failure persistence, and v1 coexistence
- application-v1 creation for approved decisions, exact replay, semantic conflict, rejected/missing decision rejection, one application per decision, and concurrent creation
- deterministic hash behavior and transport-only exclusions
- SQL patch application and validation against PostgreSQL

Bruno is not applicable because this slice adds no public route or public contract scenario.
