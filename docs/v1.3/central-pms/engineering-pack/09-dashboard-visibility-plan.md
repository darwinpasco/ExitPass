# Central PMS Fiscal Issuance Engineering Pack Detail Plan 09: Management Dashboard Visibility Plan

## Document Control

| Field | Value |
| --- | --- |
| Document | Management Dashboard Visibility Plan |
| Parent pack | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0 |
| Scope | Central PMS planning only |
| Status | Detail design plan |

This plan defines Central PMS projection and reporting planning for fiscal issuance visibility. It does not implement dashboards, BI models, APIs, SQL, or export files.

## Purpose

Management Dashboard should provide read-only visibility into fiscal issuance health and exceptions without becoming payment, fiscal, exit, or operational authority.

## Planned Metrics

Candidate metrics:

- fiscal issuance success rate
- fiscal issuance failures by category
- idempotent replay count
- idempotency conflict count
- unknown outcome count
- pending exception count
- manual release count tied to fiscal issuance exception
- average time from payment finality to fiscal reference recording
- average time from fiscal exception to reconciliation closure
- Site breakdown
- Site POS Server breakdown
- payment channel breakdown
- open exception age
- retry backlog
- reconciliation backlog

## Projection Source

Dashboard projections should use Central PMS fiscal issuance reference, attempt/history, exception, and reconciliation state. POS Server readback may support reconciliation, but Management Dashboard should not query or interpret POS Server as its own authority unless future architecture explicitly provides a reporting feed.

## Freshness Labels

Dashboard views should show freshness labels for:

- last projection update
- last successful fiscal issuance event ingestion
- last exception queue update
- last reconciliation status update

Freshness labels must distinguish operational visibility from authoritative fiscal evidence.

## Source-of-Truth Labels

Reports should label:

- Central PMS payment finality as Central PMS authority.
- POS Server fiscal document number as POS Server fiscal issuance evidence recorded by Central PMS.
- Central PMS fiscal reference recording as ExitAuthorization gating input.
- reconciliation status as operations/reconciliation workflow result.

Dashboard must not present projection-only or operational counts as fiscal truth.

## Site / Site POS Server Breakdown

Views should support:

- Site-level fiscal attribution.
- Site POS Server health and exception aggregation.
- cross-site views only where authorized.
- Site Group only as reporting/governance grouping, not fiscal authority.

## Export and Audit Expectations

Exports should be:

- permission-controlled.
- scoped by user authorization.
- labeled with source and freshness.
- audited with requester, timestamp, filter criteria, and export type.
- redacted where sensitive payment or evidence-related data is not authorized.

## Read-Only Boundary

Management Dashboard must not:

- create fiscal issuance requests
- retry POS Server calls
- close exceptions
- approve manual release
- issue Sales Invoices
- declare payment finality
- issue ExitAuthorization
- open gates

Workflow actions remain Central PMS/Operator Console/reconciliation responsibilities.

## Projection Data Candidates

Future projections may include:

- current fiscal issuance state
- latest exception reason
- `errorPosture`
- latest POS Server response classification
- fiscal document number presence
- assigned/not assigned state
- retry/readback status
- manual release association
- reconciliation closure state

Final projection names and storage remain deferred.

## Risks and Open Questions

- Dashboard implementation technology and reporting store are not defined.
- Exact refresh interval remains open.
- Export approval controls remain open.
- Cross-site access rules require RBAC confirmation.

## Authority Boundary

Management Dashboard is visibility/reporting only. It does not change Central PMS fiscal state and does not become fiscal, payment, exit, gate, continuity, or manual release authority.
