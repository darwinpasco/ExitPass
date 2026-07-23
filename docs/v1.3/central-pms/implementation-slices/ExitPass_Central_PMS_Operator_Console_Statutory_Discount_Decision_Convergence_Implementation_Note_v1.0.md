# ExitPass Central PMS Operator Console Statutory Discount Decision Convergence Implementation Note

## Purpose

This slice converges the legacy Operator Console statutory-discount decision route onto the canonical staged statutory-discount decision-v2 command while preserving the existing staged Operator Console workflow.

Retained legacy route:

- `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision`

The slice does not converge or change:

- `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis`
- Operator Console UI behavior
- WebPay or Assisted Payment Terminal behavior
- statutory calculation, VAT treatment, or legal-policy scope

## Preparatory Routes Preserved

The draft, evidence-reference, policy-resolution, validation-readback, and apply-payable-basis routes remain separate workflow stages. Draft and evidence capture still prepare safe decision facts before a reviewer submits the final decision.

The decision route now reads the draft, evidence metadata, policy-resolution fields, and tariff facts needed for decision-v2 canonicalization. It does not approximate missing facts and does not accept raw evidence or full identity values.

## Canonical Decision-v2 Mapping

The legacy decision route creates or resolves the same internal command used by the shared façade:

- business identity: `statutory-discount-decision:{parkingSessionId}:{entitlementType}`
- semantic source version: `statutory-discount-decision:sha256:v2`
- source channel: server-derived `OPERATOR_CONSOLE`

Decision-v2 material facts include the parking session, entitlement type, Site/Site Group, safe draft identity metadata, safe evidence references, reviewer attestation, decision facts, policy-resolution linkage, original tariff snapshot linkage, and supported tariff facts.

Decision-v2 semantic equality excludes apply-payable-basis facts, future application command facts, request reference, correlation ID, transport headers, generated legacy identifiers, and server timestamps.

## Canonical Linkage

Legacy decision responses now expose the safe canonical identifier:

- `statutoryDiscountDecisionCommandId`

Operator Console draft detail readback also exposes this identifier when a canonical decision-v2 command is linked to the validation record. Historical records without a canonical linkage remain readable through the existing legacy readback path.

## Legacy Response Adaptation

The route still returns the existing Operator Console decision response fields, including access outcome, decision outcome, legacy draft ID, validation status, decision reason, replay flags, error code, and correlation ID. The canonical command ID is additive.

The legacy decision writer still updates the legacy validation status for workflow compatibility, but the canonical decision-v2 command is the authoritative decision namespace used for cross-route replay and conflict control.

## Idempotency and Replay

The legacy route derives a stable decision-stage idempotency key from the caller's legacy idempotency key and the parking session. The business identity remains channel-neutral and excludes source channel and request reference.

Replay behavior:

- exact legacy replay resolves the existing canonical decision-v2 command
- changed correlation ID does not create another decision
- changed request reference does not create another decision
- shared-route submissions with the same business and semantic facts resolve the same canonical decision
- completed canonical decisions survive response-adaptation failure and remain readback-safe

## Cross-route Conflict

Changed material decision facts produce a deterministic canonical semantic conflict. Opposite terminal decisions are rejected through the existing conflict envelope and do not create another authoritative decision.

## Concurrency

The route reuses the staged command service and repository locking/unique business identity posture. Concurrent legacy and shared submissions resolve through the canonical decision-v2 command identity so only one authoritative decision can complete for the same parking session and entitlement type.

## Payable-basis Boundary

This slice deliberately does not create `statutory-discount-payable-basis-application:sha256:v1` commands.

The legacy decision route does not:

- apply the payable basis
- mutate tariff snapshots
- create payable-basis application records
- mark payment final
- issue `ExitAuthorization`
- trigger fiscal issuance
- call HikCentral
- call payment providers
- control gates

Approved decision and payable-basis applied remain separate states.

## Database Posture

An additive patch stores safe draft facts needed to reconstruct canonical decision-v2 semantics from the legacy staged workflow:

- `infra/db/patches/ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`
- `infra/db/patches/validation/Validate_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql`

The patch adds only safe metadata columns and constraints to `discounts.statutory_discount_validations`; it does not create another decision table, another application table, another idempotency namespace, or a backfill.

## RBAC and Source-channel Enforcement

The existing Operator Console access evaluation remains in force for authenticated user, Site, device, shift, reviewer, supervisor, and controlled-action checks.

The server derives `OPERATOR_CONSOLE` attribution. A legacy Operator Console caller cannot provide `WEBPAY` or `ASSISTED_PAYMENT_TERMINAL` as the decision source through this route.

## Privacy

The route and persistence changes continue to use safe references, masked identifiers, hashes, verification outcomes, and reason codes. The shared canonicalization path rejects unsafe raw evidence payloads and full identity values.

This slice does not store raw ID images, Base64 evidence, raw evidence bytes, full statutory ID numbers, or unmasked identity values. Ordinary readback does not expose restricted evidence.

## Authority Boundaries

Vendor PMS/HikCentral remains authoritative for raw parking-session lifecycle and live tariff calculation. Central PMS remains the statutory-decision and payable-basis authority. Operator Console remains a controlled staged workflow. WebPay and APT remain future submit-and-display channels. POS Server remains a finalized-fact fiscal consumer.

## Deferred Work

Deferred to later bounded slices:

- legacy apply-payable-basis route convergence onto application-v1
- WebPay statutory-discount integration
- Assisted Payment Terminal statutory-discount integration
- canonical database promotion where required by deployment ownership
- POS fiscal-linkage completion where required by channel-readiness audit
- privacy-retention policy approval
- any local ordinance, residency, driver/passenger, free-period, exemption, stacking, group-allocation, or multi-beneficiary behavior
