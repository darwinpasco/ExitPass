# Central PMS Fiscal Issuance Engineering Pack Detail Plan 02: Orchestration Service Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Orchestration Service Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines the Central PMS fiscal issuance orchestration service boundary. It does not implement source code, endpoint contracts, SQL, migrations, or job scripts.

## Purpose

Central PMS needs an internal orchestration boundary that decides when fiscal issuance should be requested from POS Server, validates preconditions, manages fiscal issuance state transitions, and blocks normal ExitAuthorization until fiscal issuance reference recording is complete.

## Service Responsibility

The fiscal issuance orchestration service should be responsible for:

- receiving or detecting the post-payment-finality trigger
- validating payment finality and payable-basis readiness
- validating Site and Site POS Server routing
- validating statutory discount references where applicable
- transitioning Central PMS fiscal issuance state
- invoking the POS Server client in later slices
- recording success, replay, failure, conflict, unknown, and readback outcomes
- emitting audit and domain events
- exposing fiscal issuance status for Operator Console and Management Dashboard projections

The service must not issue Sales Invoices itself. POS Server remains fiscal issuer.

## Triggering Point

The orchestration service should trigger only after Central PMS has verified payment finality. It must not run speculatively before payment finality, and it must not infer payment finality from POS Server responses.

Planning trigger sources:

- payment confirmation recorded by Central PMS
- replay/retry workflow after uncertain fiscal issuance result
- reconciliation/readback job
- supervised operator retry where policy allows

## Preconditions

Central PMS must confirm:

- Site is resolved.
- Site POS Server context is available.
- parking session and payment context are known.
- payment finality is verified by Central PMS.
- payable basis is approved and stable.
- statutory discount validation reference exists where applicable.
- fiscal facts needed for POS Server request construction are ready.
- stable `payableBasis.upstreamFinalityRef` is available.
- Central PMS persistence is ready to record fiscal issuance evidence.

If any precondition fails, the service should fail closed into a planned fiscal issuance exception state.

## Payable-Basis Readiness Checks

The service should confirm:

- payable basis has an approved source.
- payable amount, currency, lines, tenders, tax details, discounts, and totals are internally consistent.
- no unapproved statutory discount or entitlement reference is present.
- payable basis has not changed since payment finality in a way that would make fiscal issuance semantically different.

## Statutory Discount Reference Checks

Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, and payable-basis update. The orchestration service should only pass approved references and fiscal treatment facts. It must not approve entitlement independently.

## Site / Site POS Server Routing Checks

The service should validate:

- resolved Site is the fiscal attribution boundary.
- resolved Site POS Server is configured and active for the Site.
- payment channel or terminal is not treated as an independent POS system.
- Site Group is not used as fiscal issuance authority.

## State Transition Responsibilities

Candidate transitions:

- `pending_fiscal_issuance` after payment finality and before request.
- `fiscal_issuance_requested` when a POS Server create call is initiated.
- `fiscal_issuance_recorded` after `newly_created` success and durable reference recording.
- `fiscal_issuance_replayed` after replay success and reconciliation.
- `fiscal_issuance_conflict` after idempotency conflict.
- `fiscal_issuance_failed_request`, `fiscal_issuance_failed_configuration`, or `fiscal_issuance_failed_service` after classified errors.
- `fiscal_issuance_unknown` after timeout or inconclusive outcome.
- `fiscal_issuance_manual_review` when operational review is required.
- `fiscal_issuance_reconciled` after closure.

Final names and transition constraints remain deferred.

## ExitAuthorization Boundary

The orchestration service must enforce or feed the gating rule that normal ExitAuthorization cannot proceed until:

1. payment finality is verified by Central PMS.
2. POS Server returns complete fiscal issuance evidence.
3. `fiscalNumberAssignmentState = assigned`.
4. Central PMS durably records the fiscal issuance reference.

Manual release remains a separate governed exception, not normal ExitAuthorization.

## Dependency Boundaries

The orchestration service depends on:

- payment finality state
- payable-basis state
- Site / Site POS Server configuration
- POS Server client in later slices
- fiscal reference persistence
- audit/event infrastructure
- Operator Console queue projections
- Management Dashboard visibility projections

It must not depend on Operator Console or Management Dashboard to decide fiscal issuance success.

## Error Handling Boundaries

The service should classify outcomes from POS Server and internal persistence into:

- request correction required
- fiscal configuration correction required
- service recovery required
- unknown outcome requiring replay/readback
- idempotency conflict requiring manual review
- Central PMS persistence failure after POS Server success

Retry scheduling details are deferred.

## Events to Emit

Candidate events:

- `FiscalIssuanceRequested`
- `FiscalIssuanceRecorded`
- `FiscalIssuanceReplayed`
- `FiscalIssuanceConflictDetected`
- `FiscalIssuanceFailedRequest`
- `FiscalIssuanceFailedConfiguration`
- `FiscalIssuanceFailedService`
- `FiscalIssuanceUnknownOutcome`
- `FiscalIssuanceManualReviewRequired`
- `ExitAuthorizationBlockedByFiscalState`

Event names are placeholders pending engineering conventions.

## Idempotency Handoff to POS Server Client

The service must pass a stable `payableBasis.upstreamFinalityRef` to the POS Server client. It must not mutate fiscal request semantics between retries for the same upstream finality reference.

## Slice 1 Boundary

Slice 1 should create or confirm persistence/state only. It must not make a POS Server network call. The orchestration service shell can be designed in Slice 2 and wired to network integration in later slices.

## Future Implementation Slices

- Slice 3 adds POS Server client and request mapper.
- Slice 4 handles success and replay.
- Slice 5 handles conflict/failure/error posture.
- Slice 6 handles unknown outcome and readback.
- Slice 7 updates ExitAuthorization gating.
- Slices 8 and 9 expose queue and dashboard visibility.
- Slices 10 and 11 harden audit and validation.

## Risks and Open Questions

- Final Central PMS service boundaries and transaction boundaries are unknown.
- Payment finality event timing may need refactoring.
- Existing ExitAuthorization flow may need gating interception points.
- Retry ownership remains deferred.

## Authority Boundary

Central PMS orchestrates fiscal issuance but does not issue Sales Invoices. POS Server fiscal evidence does not declare payment finality or authorize exit.
