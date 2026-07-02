# Central PMS Fiscal Issuance Engineering Pack Detail Plan 10: Audit, Events, and Correlation Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Audit, Events, and Correlation Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines audit, event, and correlation planning for Central PMS fiscal issuance integration. It does not implement event payloads, queue names, SQL, source code, or generated artifacts.

## Purpose

Fiscal issuance integration must be traceable from payment finality through POS Server fiscal evidence, Central PMS fiscal reference recording, ExitAuthorization gating, exception handling, and reconciliation.

## Candidate Events

Candidate event names pending engineering conventions:

- `FiscalIssuanceRequested`
- `FiscalIssuanceRecorded`
- `FiscalIssuanceReplayed`
- `FiscalIssuanceConflictDetected`
- `FiscalIssuanceFailedRequest`
- `FiscalIssuanceFailedConfiguration`
- `FiscalIssuanceFailedService`
- `FiscalIssuanceUnknownOutcome`
- `FiscalIssuanceReadbackRequested`
- `FiscalIssuanceReconciled`
- `FiscalIssuanceManualReviewRequired`
- `ExitAuthorizationBlockedByFiscalState`

These names are placeholders only. Final event names, payloads, and delivery mechanisms remain deferred.

## Audit Record Expectations

Audit records should capture:

- payment confirmation reference
- parking session reference
- Site
- Site POS Server
- upstream finality reference
- POS Server fiscal document id when available
- fiscal document number when available
- fiscal issuance state
- exception reason
- response status and error code
- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- `errorPosture`
- retry/readback action
- operator/supervisor action where applicable
- ExitAuthorization blocked or allowed decision
- reconciliation closure

## Correlation ID Propagation

Correlation should connect:

- Central PMS payment finality event
- fiscal issuance orchestration attempt
- POS Server create/read request
- POS Server response
- Central PMS fiscal reference persistence
- Operator Console queue item
- Management Dashboard projection
- ExitAuthorization decision
- manual release exception if applicable

## Sensitive Data Exclusions

Logs, events, and audit records must exclude:

- secrets
- credentials
- card PAN/CVV
- raw tokens
- raw provider callback payloads
- raw statutory entitlement evidence
- unmanaged sensitive evidence images
- uncontrolled customer personal data beyond approved references

References and hashes may be used only where policy allows.

## Event Ownership

Central PMS should own fiscal issuance integration events. POS Server owns fiscal issuance persistence internally and returns API evidence. Operator Console and Management Dashboard consume Central PMS state/projections and should not independently author fiscal issuance truth.

## Event Ordering Considerations

Planning considerations:

- payment finality must precede fiscal issuance request.
- fiscal issuance reference recording must precede normal ExitAuthorization.
- replay must not emit duplicate business-success events without idempotency handling.
- failed/unknown events must not be overwritten by later success without preserving history.
- reconciliation closure must reference the prior exception state.

## Replay / Idempotency Audit Rules

Audit should preserve:

- original fiscal document id and number
- replay attempt timestamp
- replay result classification
- whether Central PMS had an existing fiscal reference
- whether replay matched or mismatched that reference
- whether ExitAuthorization had already been issued

Replay must be auditable without appearing as duplicate fiscal issuance.

## Traceability Requirement

The future implementation should support traceability from:

1. Central PMS payment confirmation
2. approved payable basis
3. POS Server fiscal document create/replay/readback
4. Central PMS fiscal reference recording
5. ExitAuthorization gating decision
6. Operator Console review where applicable
7. Management Dashboard visibility
8. reconciliation closure

## Risks and Open Questions

- Existing event/audit infrastructure capabilities are not confirmed.
- Final outbox/inbox, delivery, and idempotency patterns remain engineering decisions.
- Retention and access controls require policy confirmation.

## Authority Boundary

Audit and events record decisions; they do not create payment finality, issue fiscal documents, issue ExitAuthorization, open gates, or approve manual release.
