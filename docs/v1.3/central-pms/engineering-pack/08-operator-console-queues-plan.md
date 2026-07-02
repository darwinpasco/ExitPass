# Central PMS Fiscal Issuance Engineering Pack Detail Plan 08: Operator Console Queue Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Operator Console Queue Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines Central PMS fiscal exception queue planning for Operator Console. It does not modify Operator Console BRD/design, create UI wireframes, implement APIs, or write source code.

## Purpose

Operator Console should expose fiscal issuance exceptions for review and governance while preserving its non-payment, non-fiscal, non-exit authority boundary.

## Queue Categories

Planned queue categories:

- pending fiscal issuance
- retry needed
- configuration correction required
- idempotency conflict
- unknown outcome
- incomplete numbering evidence
- fiscal reference mismatch
- manual release requested after fiscal failure
- reconciled/closed exceptions

## Fields to Display

Candidate display fields:

- fiscal issuance state
- exception reason
- Site
- Site POS Server
- parking session reference
- payment confirmation reference
- upstream finality reference
- POS Server fiscal document id, when available
- fiscal document number, when available
- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- `errorPosture`
- retry/readback status
- age since payment finality
- age since last attempt
- operator/supervisor assignment
- manual release request status
- reconciliation closure status

Sensitive evidence and payment payloads must not be displayed unless specifically authorized by role and policy.

## Filters and Sorting

Candidate filters:

- Site / Site Group scope where authorized
- Site POS Server
- exception category
- `errorPosture`
- age
- payment channel
- manual release requested
- reconciliation status
- assigned reviewer

Candidate sorting:

- oldest unresolved first
- customer-impacting first
- manual release requested first
- configuration correction required first

## Role-Based Access

Access planning:

- site operators may see assigned-site fiscal exception context as permitted.
- supervisors may review and escalate fiscal exceptions.
- compliance/audit users may review closed/reconciled cases subject to privacy policy.
- administrators may manage configuration-related correction workflows where assigned.
- support users may see technical posture without sensitive evidence.

Final permission matrix remains deferred.

## Review Actions

Potential actions to plan:

- acknowledge exception
- assign reviewer
- request retry after correction
- request GET readback
- mark configuration correction needed
- attach incident/reference note
- escalate to supervisor
- link manual release request
- close as reconciled where policy allows

Operator Console actions must call Central PMS governance flows. Operator Console must not issue Sales Invoices or ExitAuthorization directly.

## Supervisor Escalation

Supervisor escalation should be required for:

- idempotency conflict
- fiscal reference mismatch
- manual release requested after fiscal failure
- repeated unknown outcome
- incomplete numbering evidence
- fiscal exception release

## Manual Release Request Visibility

Manual release request visibility should show:

- payment finality present or absent
- fiscal issuance exception reason
- ExitAuthorization blocked reason
- incident/reconciliation tagging
- supervisor approval status
- customer/operator messaging status

Manual release remains a governed exception, not normal ExitAuthorization.

## Reconciliation Close Workflow

Closure planning should require:

- fiscal evidence recorded, or approved exception path
- mismatch resolved or documented
- incident/reference notes
- approver attribution
- audit event
- Management Dashboard status update

## Audit Logging

Operator Console queue interactions should be audited:

- view/access
- assignment
- retry/readback request
- escalation
- manual release request review
- closure/reconciliation
- evidence access where applicable

## Boundaries

Operator Console must not:

- collect payment
- issue Sales Invoices
- declare payment finality
- issue ExitAuthorization
- open gates
- approve statutory entitlement independently
- mutate POS Server fiscal documents

## Risks and Open Questions

- Final Operator Console API surface is not defined.
- Exact permissions matrix is open.
- Manual release governance workflow remains policy-dependent.
- Queue SLA and closure labels remain open.

## Authority Boundary

Operator Console is a governance/review surface only. Central PMS owns payment finality, fiscal reference recording, and normal ExitAuthorization. POS Server owns fiscal issuance and numbering.
