# Central PMS Fiscal Issuance Engineering Pack Detail Plan 11: Test / UAT Evidence Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Test / UAT Evidence Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines future test and UAT planning for Central PMS fiscal issuance integration. It does not create test scripts, source code, generated artifacts, or UAT execution evidence.

## Purpose

Central PMS fiscal issuance integration requires test coverage for persistence, orchestration, POS Server interaction, idempotency, replay, conflict, unknown outcomes, ExitAuthorization gating, Operator Console queues, Management Dashboard visibility, and audit/security controls.

## Unit Test Categories

Future unit tests should cover:

- fiscal issuance state transition rules
- precondition validation
- payable-basis readiness validation
- statutory discount reference validation
- Site / Site POS Server routing validation
- request mapper output shape and sensitive payload exclusion
- POS Server response classification
- `errorPosture` mapping
- duplicate prevention
- ExitAuthorization gating decisions

## Integration Test Categories

Future integration tests should cover:

- Central PMS orchestration to POS Server client boundary
- fiscal reference persistence after success
- replay reconciliation
- failure state persistence
- GET readback/reconciliation flow
- audit/event emission
- Operator Console queue projections
- Management Dashboard projection updates

## Mocked POS Server Fixtures

Fixtures should include:

- `202 accepted` + `newly_created`
- `202 accepted` + `idempotent_replay`
- `409 fiscal_document_idempotency_conflict`
- 400 request correction failure
- 400 configuration correction failure
- 503 service recovery failure
- `fiscal_number_assignment_incomplete`
- GET success with complete fiscal numbering evidence
- GET not found
- GET service unavailable
- response missing required fiscal evidence fields

## Retry / Readback / Replay Matrix

The test matrix should include:

- POST timeout then retry returns idempotent replay.
- POST timeout then retry returns conflict.
- 503 with fiscal document id then GET success.
- 503 with fiscal document id then GET inconclusive.
- 503 without fiscal document id then same-request retry.
- POS Server success then Central PMS reference persistence failure.
- replay after persistence failure recovers same fiscal reference.
- readback mismatch routes to manual review.

## ExitAuthorization Gating Regression Tests

Required scenarios:

- normal ExitAuthorization blocked until fiscal reference recorded.
- payment finality without fiscal issuance remains blocked.
- POS Server success without Central PMS durable reference remains blocked.
- incomplete numbering evidence remains blocked.
- conflict remains blocked.
- unknown outcome remains blocked.
- replay success does not create duplicate ExitAuthorization.
- manual release exception remains separate, tagged, and auditable.

## Operator Console Queue Visibility Tests

Future tests should verify queue visibility for:

- pending fiscal issuance
- retry needed
- configuration correction required
- idempotency conflict
- unknown outcome
- incomplete numbering evidence
- fiscal reference mismatch
- manual release requested after fiscal failure
- reconciled/closed exceptions

Operator Console tests must preserve non-payment and non-gate authority.

## Management Dashboard Projection Tests

Future tests should verify:

- fiscal issuance success rate projection
- failure by category
- replay count
- conflict count
- unknown outcome count
- pending exception count
- manual release count tied to fiscal issuance exception
- average time from payment finality to fiscal reference recording
- Site / Site POS Server breakdown
- source-of-truth and freshness labels

Dashboard remains read-only visibility.

## Security Logging Checks

Tests should confirm logs/audit/events exclude:

- secrets
- credentials
- PAN/CVV
- tokens
- raw provider callback payloads
- raw statutory entitlement evidence
- unmanaged sensitive evidence images

## UAT Evidence Checklist

Future UAT evidence should include:

- successful newly-created fiscal issuance
- successful idempotent replay
- replay after timeout
- 409 conflict
- 400 request correction
- 400 configuration correction
- 503 service recovery
- `fiscal_number_assignment_incomplete`
- GET readback after unknown POST
- fiscal reference recording failure after POS success
- normal ExitAuthorization blocked until fiscal reference recorded
- manual release exception path after fiscal failure
- Operator Console queue visibility
- Dashboard metrics visibility
- no duplicate fiscal reference after replay
- no duplicate ExitAuthorization after replay

## Acceptance Evidence Planning

Each implementation slice should produce:

- test summary
- evidence of no source authority boundary violation
- evidence of fail-closed behavior
- audit/correlation evidence
- regression result for payment-to-exit flow
- unresolved defects and accepted risks

## Risks and Open Questions

- Test environment POS Server availability and seeded fiscal configuration are not defined.
- Final service authentication test posture remains open.
- UAT data privacy controls require review before evidence capture.

## Authority Boundary

Testing must verify that Central PMS owns payment finality, fiscal reference recording, and normal ExitAuthorization; POS Server owns fiscal issuance/numbering only; Operator Console is governance only; Management Dashboard is visibility only.
