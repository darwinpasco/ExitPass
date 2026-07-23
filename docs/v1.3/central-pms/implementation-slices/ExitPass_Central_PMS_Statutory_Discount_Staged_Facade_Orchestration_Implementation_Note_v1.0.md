# ExitPass Central PMS Statutory-Discount Staged Facade Orchestration Implementation Note v1.0

## Purpose

This slice refactors the existing shared Central PMS one-shot statutory-discount facade so `POST /v1/statutory-discounts/decisions` orchestrates the merged staged canonical commands:

- statutory-discount decision-v2
- optional statutory-discount payable-basis-application-v1

The public route family is retained. This slice does not converge Operator Console legacy routes, implement WebPay or APT clients, change statutory calculations, change VAT treatment, alter payment finality, issue ExitAuthorization, trigger fiscal issuance, call HikCentral, call a payment provider, or control gates.

## Retained Public Routes

The shared route family remains:

- `POST /v1/statutory-discounts/decisions`
- `GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}`

No public payable-basis-application route is added. The POST route remains one-shot for clients while using separate internal durable stage boundaries.

## Decision-v2 Orchestration

Every new shared one-shot submission creates or resolves a canonical decision-v2 command through `IStatutoryDiscountStagedCommandService`.

Decision business identity remains:

```text
statutory-discount-decision:{parkingSessionId}:{entitlementType}
```

Decision semantic source version:

```text
statutory-discount-decision:sha256:v2
```

Decision-v2 owns the decision-stage facts only: parking session, Site/Site Group, entitlement type, safe beneficiary and masked identity metadata, evidence references and verification outcomes, attestation facts, actor/reviewer references, decision facts, policy-resolution and original tariff facts where current behavior provides them, and server-derived source-channel attribution for audit.

`applyPayableBasis` is not part of decision-v2 semantics. A decision-only request can later request payable-basis application without creating another statutory decision when the decision-stage facts are unchanged.

## Application-v1 Orchestration

When `applyPayableBasis` is `true` and the completed decision result is `APPROVED`, the facade creates or resolves a canonical payable-basis-application-v1 command through `IStatutoryDiscountStagedCommandService`.

Application business identity:

```text
statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}
```

Application semantic source version:

```text
statutory-discount-payable-basis-application:sha256:v1
```

Application material facts are reconstructed from the completed canonical decision and the approved payable-basis facts already produced by the existing Central PMS statutory workflow. Rejected decisions never create an application command.

## Stage Idempotency Key Derivation

The one-shot Idempotency-Key is not reused raw across both staged command identities. The facade derives deterministic stage keys:

```text
{stage}:sha256:{sha256("statutory-discount-one-shot:{stage}:{stageIdentity}:{oneShotIdempotencyKey}")}
```

Stages:

- `decision-v2`, with `parkingSessionId` as the stage identity
- `payable-basis-application-v1`, with `statutoryDiscountDecisionCommandId` as the stage identity

The derived keys contain no request body, raw evidence, full statutory ID, or beneficiary-sensitive data. Different client keys cannot create duplicate authoritative decisions or applications because database business identity remains independent of source channel, request reference, and correlation ID.

## Decision-Only Completion

When `applyPayableBasis` is `false`, the facade completes decision-v2 and returns a decision-only result. Response fields expose:

- `decisionCommandStatus`
- `decisionResultStatus`
- `applicationRequested = false`
- `applicationCommandStatus = NOT_REQUESTED`
- `oneShotComplete = true`

No payable-basis application command is created.

## Optional Application Behavior

When `applyPayableBasis` is `true` and the decision is approved, the facade creates or resolves application-v1, calls the existing authoritative payable-basis mutation path, and marks application-v1 `APPLIED` only after the durable mutation succeeds.

A later `false` to `true` transition reuses the completed decision-v2 command and creates only the missing application-v1 command. Replays of completed application-v1 read durable state and do not call the payable-basis mutation again.

## Status Vocabulary

Decision state, application state, and one-shot orchestration state remain separate.

Decision command fields:

- `decisionCommandStatus`
- `decisionResultStatus`
- `decisionRetryable`
- `decisionRecoveryClassification`
- `decisionRecoveryAction`

Application command fields:

- `statutoryDiscountPayableBasisApplicationCommandId`
- `applicationRequested`
- `applicationCommandStatus`
- `applicationResultClassification`
- `applicationSemanticHashSourceVersion`
- `applicationRetryable`
- `applicationRecoveryClassification`
- `applicationRecoveryAction`

Overall orchestration fields:

- `overallResultClassification`
- `oneShotComplete`
- existing `commandStatus`, `clientResultStatus`, `resultClassification`, `retryable`, `recoveryClassification`, `recoveryAction`, and `safeErrorCode`

The vocabulary does not conflate approved discount decision, payable-basis application, payment finality, fiscal issuance, ExitAuthorization, or gate control.

## Historical Decision-v1 Compatibility

Historical `statutory-discount-decision:sha256:v1` command readback remains available through the existing repository fallback. V1 hashes are not recalculated as v2, v1 rows are not converted, and no v1 command receives an application-v1 command automatically.

New submissions use decision-v2. A completed v2 decision is never recreated because response adaptation failed.

## Readback Behavior

Readback first resolves staged decision-v2 by canonical command ID. If found, it reads any linked application-v1 command and returns separate decision and application state. If no staged record exists, it falls back to historical decision-v1 readback.

Readback does not expose raw evidence, full statutory IDs, Base64 evidence, internal persistence payloads, payment-finality state, fiscal issuance state, ExitAuthorization state, or gate state.

## Exactly-Once Payable-Basis Mutation

The facade reuses the existing Central PMS payable-basis mutation service. Application-v1 enters processing before mutation and is marked `APPLIED` only after durable mutation success.

The staged application business identity and repository constraints allow one canonical application command per approved decision. Replays read durable application state. A concurrent or replayed request cannot create a second authoritative application command or repeat the payable-basis mutation in the normal completed path.

## Transaction Boundaries and Recovery

The one-shot workflow is not forced into one long transaction. It uses durable boundaries:

1. decision-v2 create or resolve
2. decision-v2 processing
3. existing draft/evidence/decision workflow
4. decision-v2 completion
5. application-v1 create or resolve when requested
6. application-v1 processing
7. existing payable-basis mutation
8. application-v1 applied or failure completion
9. one-shot response adaptation

Failure before command creation leaves no canonical command. Failure after decision creation is recoverable through the original derived decision-stage key. Failure after decision completion but before application creation replays from the completed decision. Failure after application creation is recoverable through the derived application-stage key. Failure after payable-basis mutation but before response adaptation replays from durable application state.

## RBAC and Source Channel

The route keeps the merged authenticated-channel behavior. Effective source channel is server-derived and a request body/source-channel mismatch is rejected. Source channel remains attribution, not business identity.

Supported source channels remain:

- `OPERATOR_CONSOLE`
- `WEBPAY`
- `ASSISTED_PAYMENT_TERMINAL`

This slice does not broaden shared facade permissions and does not converge legacy Operator Console routes.

## Security and Privacy

The facade continues to reject unsafe full-ID-like values and accepts only safe evidence references, masked identifiers, hashes, verification outcomes, reason codes, actor/reviewer references, and approved metadata. It does not accept, store, hash, log, or return raw ID images, Base64 evidence, raw evidence bytes, full statutory ID numbers, unmasked identity values, or beneficiary-sensitive values in error messages.

## Authority Boundaries

Authority remains unchanged:

- Vendor PMS or HikCentral owns raw parking-session lifecycle and live tariff computation.
- Central PMS owns statutory decision and payable-basis authority.
- Operator Console remains a staged controlled workflow.
- WebPay and APT remain future submit-and-display channels.
- POS Server remains a finalized-fact fiscal consumer.

This slice does not mark payment final, issue ExitAuthorization, trigger fiscal issuance, call HikCentral, call a payment provider, control gates, modify POS Server, modify APT, implement WebPay client behavior, activate local ordinances, change VAT calculation, or add multiple-beneficiary behavior.

## Deferred Behavior

Deliberately deferred:

- Operator Console legacy route convergence
- WebPay client integration
- APT client integration
- Management Platform policy configuration
- local ordinance activation
- multiple-beneficiary or group-transaction allocation
- public payable-basis-application endpoint
- POS Server runtime modification

## Remaining Blockers

Before Operator Console convergence, legacy decision and apply routes must map to the staged decision-v2 and application-v1 operations without changing UI-visible workflow behavior.

Before WebPay or APT integration, live authenticated one-shot submission, replay, conflict, and readback must be exercised in an environment with seeded statutory-discount payable-basis fixtures.
