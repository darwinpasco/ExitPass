# ExitPass Central PMS Fiscal Issuance Reference and Exception-State Implementation Planning Note v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass Central PMS Fiscal Issuance Reference and Exception-State Implementation Planning Note |
| Version | v1.0 |
| Product scope | ExitPass v1.3 Central PMS |
| Status | Implementation planning note |
| Output format | Markdown only |
| Repository | `D:\SourceCodes\ExitPass` |
| Runtime reference inspected | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |

This document is a planning bridge. It does not implement source code, SQL, migrations, generated artifacts, runtime API contracts, OpenAPI specifications, deployment scripts, UAT scripts, or runbook procedures.

## 2. Purpose and Scope

This note identifies what Central PMS must add, confirm, or update before implementing fiscal issuance integration with POS Server.

It translates the approved Central PMS to POS Server Fiscal Issuance Integration Contract into Central PMS implementation planning items for:

- fiscal issuance reference persistence
- payment finality to fiscal issuance linkage
- retry, replay, and conflict history
- fiscal issuance exception states
- unknown outcome handling
- Operator Console review queue needs
- Management Dashboard visibility needs
- ExitAuthorization gating state
- API, service, database, event, job, and test planning for later engineering pack work

This note must not be read as final database design, API design, or implementation specification.

## 3. Inputs and Baseline References

Primary documentation inputs:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Alignment_Review.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0_Review.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Runtime reference inputs:

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 4. Current Implementation Assumption

Planning assumption:

- POS Server runtime currently supports `POST /v1/fiscal-documents/` and `GET /v1/fiscal-documents/{fiscalDocumentId}`.
- POS Server fiscal document creation uses `payableBasis.upstreamFinalityRef` as the current idempotency key.
- POS Server computes semantic request hash server-side.
- POS Server performs fiscal identity resolution, fiscal sequence policy resolution, sequence-state row locking, fiscal number allocation, and response/status hardening.
- Central PMS implementation work for fiscal issuance reference persistence, exception-state handling, queues, jobs, events, and ExitAuthorization gating still requires planning and later engineering implementation.

If any current Central PMS implementation already supports part of this plan, the later engineering pack must reconcile this planning note with actual code and schema.

## 5. Target Central PMS Responsibility

Central PMS remains responsible for:

- payment finality
- payment-linked platform control state
- approved payable-basis readiness
- fiscal issuance request orchestration
- fiscal issuance reference recording
- retry/replay/conflict tracking
- fiscal issuance exception state
- normal ExitAuthorization gating
- reconciliation coordination
- audit and correlation across payment, fiscal issuance, and exit authorization

Central PMS must not become POS Server fiscal issuer. POS Server remains fiscal issuance and numbering authority only.

## 6. Fiscal Issuance Reference Persistence Requirements

Central PMS should persist at least the following fiscal issuance reference fields when POS Server returns complete fiscal issuance evidence.

Planning fields:

| Planning field | Purpose |
| --- | --- |
| POS Server fiscal document id | Links Central PMS to POS Server fiscal document. |
| fiscal identity id | Records POS Server-resolved fiscal identity. |
| fiscal sequence policy id | Records POS Server-resolved numbering policy. |
| fiscal sequence value | Records assigned sequence value. |
| fiscal document number | Records assigned Sales Invoice/fiscal document number. |
| fiscal series | Records fiscal series at assignment time. |
| fiscal number prefix text | Records prefix at assignment time. |
| fiscal number suffix text | Records suffix at assignment time. |
| fiscal number assigned at | Records POS Server assignment timestamp. |
| fiscal number assigned by ref | Records POS Server assignment actor reference. |
| fiscal document status code id | Records POS Server fiscal document status code id. |
| result classification | Distinguishes `newly_created` from `idempotent_replay`. |
| fiscal issuance evidence status | Records `fiscal_document_number_assigned` when complete. |
| fiscal number assignment state | Records `assigned` or `not_assigned`. |
| upstream finality reference | Stores POS Server idempotency key source. |
| Central PMS payment confirmation ref | Links fiscal issuance to Central PMS payment finality. |
| Central PMS payment attempt ref | Links to payment attempt history. |
| Central PMS parking session ref | Links to parking session/control state. |
| Site id | Supports Site attribution and reporting. |
| Site POS Server id | Supports fiscal routing and reconciliation. |
| request/correlation id | Supports traceability across services. |
| request hash or semantic hash reference if available | Supports later reconciliation. |
| POS Server response timestamp | Supports timing/reconciliation. |
| retry/replay/conflict history | Preserves integration history. |
| current fiscal issuance integration state | Supports gating and queues. |
| exception state/reason if applicable | Supports manual review and reconciliation. |

These are planning requirements, not DDL. Final names, types, indexes, constraints, and ownership belong in the later database/API/engineering pack.

## 7. Fiscal Issuance State Model Planning

Central PMS needs an explicit fiscal issuance integration state model. Candidate state names for later Engineering Pack confirmation:

| Candidate state | Planning meaning |
| --- | --- |
| `not_required` | Fiscal issuance is not required for this transaction under approved policy. |
| `pending_fiscal_issuance` | Payment/fiscal flow is waiting for fiscal issuance initiation. |
| `fiscal_issuance_requested` | Central PMS has called or is calling POS Server. |
| `fiscal_issuance_recorded` | POS Server returned complete fiscal evidence and Central PMS recorded fiscal reference. |
| `fiscal_issuance_replayed` | POS Server replayed a prior successful fiscal issuance and Central PMS reconciled/recorded it. |
| `fiscal_issuance_conflict` | POS Server returned idempotency conflict. |
| `fiscal_issuance_failed_request` | Request construction or semantic request validation failed. |
| `fiscal_issuance_failed_configuration` | Fiscal identity, policy, state, allocation, or format setup requires correction. |
| `fiscal_issuance_failed_service` | Persistence/service recovery is required. |
| `fiscal_issuance_unknown` | Outcome is unknown because response/readback is unavailable or inconclusive. |
| `fiscal_issuance_manual_review` | Case is under operator/supervisor/reconciliation review. |
| `fiscal_issuance_exception_released` | Approved exception/manual release occurred after fiscal issue. |
| `fiscal_issuance_reconciled` | Exception or unknown fiscal state was reconciled/closed. |

These are conceptual planning labels only. Final state names, enum values, API statuses, and transition rules remain deferred.

## 8. Fiscal Issuance Exception-State Planning

Central PMS should plan exception buckets for:

- idempotency conflict
- request construction error
- unapproved discount reference
- sensitive payload rejection
- fiscal identity missing, ambiguous, or not effective
- fiscal sequence policy missing, ambiguous, or not effective
- fiscal sequence state missing or not effective
- allocation or format failure
- persistence unavailable
- incomplete numbering evidence
- unknown POST outcome
- GET readback inconclusive
- Central PMS fiscal reference mismatch
- POS Server readback mismatch
- manual release request after fiscal issuance failure

Exception state must preserve:

- payment finality reference
- fiscal issuance attempt reference
- POS Server response/error details
- `errorPosture` where provided
- correlation id
- operator/manual review status
- reconciliation status
- reason code
- incident/manual release tag where applicable

## 9. Retry/Replay/Conflict History Planning

Central PMS should record retry/replay/conflict history as a first-class planning concern.

Planning rules:

- retry the same semantic request with the same `payableBasis.upstreamFinalityRef` after uncertain outcome
- treat idempotent replay as success only if `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned` and `fiscalNumberAssignmentState = assigned`
- treat conflict as fail-closed and requiring review
- do not use a new upstream finality reference to bypass conflict
- preserve each attempt with timestamp, response code, `resultClassification`, `errorPosture`, and correlation id
- preserve whether a retry was automatic, operator-triggered, or reconciliation-triggered

Retry scheduler details, retry counts, backoff, queue names, and timeout values remain deferred.

## 10. Unknown Outcome Handling Planning

Central PMS must plan unknown outcome handling for these cases:

| Scenario | Planning response |
| --- | --- |
| POST times out before response | Preserve same upstream finality reference; avoid semantic request mutation; schedule safe retry or readback if an id can be inferred. |
| POST returns `503` with `fiscalDocumentId` | Do not record fiscal evidence from failed response; perform controlled GET readback when safe. |
| POST returns `503` without `fiscalDocumentId` | Keep state unknown/service-failed; retry same request only after service recovery. |
| Network disconnect occurs after POS Server may have committed | Treat outcome as unknown; retry same semantic request to get replay or use GET when id is known. |
| Central PMS records payment finality but cannot reach POS Server | Keep fiscal issuance pending/failed service; block normal ExitAuthorization. |
| GET readback succeeds with complete evidence | Record/reconcile fiscal reference if fields match expected payment/session context. |
| GET readback fails | Keep unknown or service-failed state; escalate if operationally sensitive. |
| replay later succeeds | Record or reconcile fiscal reference; avoid duplicate reference and duplicate ExitAuthorization. |
| fiscal reference recording fails after POS Server success | Mark Central PMS persistence failure/unknown-reference state; use replay/readback to recover without issuing duplicate fiscal document. |

Unknown outcome must not become implicit fiscal success.

## 11. ExitAuthorization Gating Planning

Normal ExitAuthorization must remain blocked until:

1. payment finality is verified by Central PMS
2. POS Server fiscal issuance succeeds or replays successfully
3. POS Server returns `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`
4. POS Server returns `fiscalNumberAssignmentState = assigned`
5. Central PMS durably records fiscal issuance reference

If any required condition is missing, Central PMS must not issue normal ExitAuthorization.

Manual release or exception release must be separately approved, auditable, incident-tagged, and reconciliation-tagged. Manual release must not silently become normal ExitAuthorization.

The ExitAuthorization gating check must explicitly evaluate fiscal issuance state and fiscal reference recording state before issuing normal authorization.

## 12. GET Readback/Reconciliation Planning

Central PMS should plan GET readback behavior for:

- uncertain POST outcome
- `503` with known `fiscalDocumentId`
- reconciliation
- fiscal reference verification
- duplicate/replay ambiguity
- operator/manual review
- fiscal reference recording failure recovery

Readback planning rules:

- GET readback is persisted POS Server fiscal document evidence only.
- GET readback does not create payment finality.
- GET readback does not issue ExitAuthorization.
- GET readback does not imply gate permission.
- GET readback with `fiscalNumberAssignmentState = not_assigned` must not satisfy normal fiscal issuance gating.
- GET readback must be correlated to Central PMS payment/session/Site context before recording or reconciling fiscal reference.

## 13. Operator Console Review Queue Planning

Central PMS should plan Operator Console queue/review views for:

- fiscal issuance pending
- fiscal issuance retry needed
- fiscal configuration correction required
- idempotency conflict
- unknown outcome
- incomplete numbering evidence
- fiscal reference mismatch
- manual release requested after fiscal failure
- reconciled/closed exceptions

Queue item planning fields:

- queue category
- Site and Site POS Server
- parking session reference
- payment confirmation reference
- upstream finality reference
- latest POS Server response code
- `errorPosture`
- retry count
- age/time since payment finality
- assigned operator/supervisor where applicable
- manual release request/approval status where applicable
- reconciliation status

Operator Console remains non-payment and non-gate-authority. It must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.

## 14. Management Dashboard Visibility Planning

Central PMS should expose planning-level data for Management Dashboard visibility:

- fiscal issuance success rate
- fiscal issuance failures by category
- replay count
- conflict count
- unknown outcome count
- pending exception count
- manual release count tied to fiscal issuance exception
- average time from payment finality to fiscal reference recording
- Site breakdown
- Site POS Server breakdown
- open fiscal exception age
- retry backlog
- reconciliation backlog

Management Dashboard remains visibility/reporting only. It must not mutate fiscal state, declare payment finality, issue Sales Invoices, issue ExitAuthorization, open gates, approve manual release, or activate continuity.

## 15. Audit/Logging/Correlation Planning

Central PMS should plan audit and correlation across:

- Central PMS request id
- POS Server request/correlation id where available
- upstream finality reference
- payment attempt ref
- payment confirmation ref
- parking session ref
- Site id
- Site POS Server id
- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- POS Server fiscal document id
- fiscal document number
- fiscal sequence value
- fiscal identity id
- fiscal sequence policy id
- fiscal document status code id
- response code
- `errorPosture`
- retry attempt number
- operator/manual review id where applicable
- reconciliation close reference where applicable

Logs must not contain secrets, card PAN/CVV, tokens, raw provider callbacks, raw entitlement evidence, or unmanaged sensitive evidence payloads.

## 16. Central PMS API/Service Changes to Plan

Future Central PMS service/API planning areas:

- fiscal issuance orchestration service method
- POS Server client boundary
- fiscal issuance request builder
- fiscal issuance response interpreter
- fiscal reference recording service
- fiscal issuance state transition service
- ExitAuthorization gating check update
- exception escalation service
- retry/reconciliation service
- GET readback service
- Operator Console queue API
- Management Dashboard reporting projection API
- audit/event publisher
- correlation id propagation
- `errorPosture` mapping

This note does not define final endpoint paths, DTOs, event payloads, service classes, or implementation methods.

## 17. Central PMS Database/State Changes to Plan

Future Central PMS persistence planning areas:

- fiscal issuance reference persistence model
- fiscal issuance attempt/history table or equivalent
- fiscal issuance current state field or equivalent
- fiscal issuance exception state/reason field or equivalent
- upstream finality reference uniqueness/idempotency guard where applicable
- POS Server fiscal document id/reference mapping
- payment confirmation to fiscal reference linkage
- parking session to fiscal reference linkage
- retry/replay/conflict history persistence
- readback/reconciliation result persistence
- operator/manual review assignment/status persistence
- dashboard/reporting projection source
- audit event retention and queryability

This note does not define SQL DDL, table names, indexes, constraints, migrations, or stored routines.

## 18. Central PMS Event/Job/Queue Changes to Plan

Future event/job/queue planning areas:

- fiscal issuance requested event
- fiscal issuance succeeded event
- fiscal issuance replayed event
- fiscal issuance failed event
- fiscal issuance conflict event
- fiscal issuance unknown outcome event
- fiscal reference recorded event
- fiscal reference mismatch event
- ExitAuthorization blocked by fiscal state event
- retry job for safe retry after uncertain outcome
- readback job for known fiscal document id
- reconciliation job for unresolved fiscal issuance cases
- Operator Console queue projection job
- Management Dashboard metrics projection job

Final event names, queue names, message contracts, schedulers, and retry policies remain deferred.

## 19. Test/UAT Scenarios to Plan

Future test/UAT planning should include:

- successful `newly_created`
- successful `idempotent_replay`
- replay after timeout
- `409` conflict
- `400` request correction
- `400` configuration correction
- `503` service recovery
- `fiscal_number_assignment_incomplete`
- GET readback after unknown POST
- fiscal reference recording failure after POS success
- normal ExitAuthorization blocked until fiscal reference recorded
- manual release exception path after fiscal failure
- Operator Console queue visibility
- Management Dashboard metrics visibility
- sensitive payload rejection and audit handling
- no duplicate fiscal reference after replay
- no duplicate ExitAuthorization after replay

This note does not define final test scripts or automation.

## 20. Security/Access Control Considerations

Planning considerations:

- only Central PMS service identity should call POS Server fiscal issuance path
- service-to-service authentication model remains deferred
- Operator Console users should see/review fiscal exceptions only by role
- Management Dashboard users should see fiscal metrics only within authorized Site/Site POS Server scope
- sensitive payloads must remain excluded
- logs must not contain secrets, PAN/CVV, tokens, raw provider callbacks, or raw entitlement evidence
- audit access must be controlled
- retry/manual review actions must be attributable
- manual release review must require approved permission and audit tagging where policy requires

## 21. Deferred Items

This planning note does not implement or define detailed contracts for:

- Digital SI
- printable SI
- QR presentation
- X-read
- Z-read
- BIR Sales Summary
- Annex E
- EJ
- POSLog
- reprints
- adjustments
- reset/Z-counter/GTA mechanics
- recovery automation
- gate integration endpoint
- POS Server-side Central PMS callbacks
- final SQL DDL
- final API endpoint paths
- final DTOs
- final event payloads
- final queue names
- final UAT scripts

## 22. Open Questions

| ID | Open question / deferred decision |
| --- | --- |
| CPM-FI-OQ-001 | What is the final Central PMS persistence model for fiscal issuance references? |
| CPM-FI-OQ-002 | What are the final fiscal issuance state names, transition rules, and allowed terminal states? |
| CPM-FI-OQ-003 | What is the final exception state taxonomy and reason-code list? |
| CPM-FI-OQ-004 | What Central PMS field should store or reference the semantic request hash if POS Server does not expose the hash directly? |
| CPM-FI-OQ-005 | What is the final service-to-service authentication and authorization model between Central PMS and POS Server? |
| CPM-FI-OQ-006 | What is the final retry scheduler owner, retry count, timeout, and backoff policy? |
| CPM-FI-OQ-007 | What is the final behavior when POST times out and no fiscal document id is known? |
| CPM-FI-OQ-008 | What is the final behavior when Central PMS fails to record fiscal reference after POS Server success? |
| CPM-FI-OQ-009 | What is the final Operator Console permission matrix for fiscal exception queues? |
| CPM-FI-OQ-010 | What are the final Management Dashboard metrics, refresh intervals, and alert thresholds? |
| CPM-FI-OQ-011 | What is the final manual release policy after fiscal issuance failure? |
| CPM-FI-OQ-012 | What is the final cross-API error envelope standard? |

## 23. Recommended Implementation Slices

Recommended Central PMS implementation planning slices:

1. Fiscal issuance reference persistence slice.
2. Fiscal issuance state and exception taxonomy slice.
3. POS Server client and request construction slice.
4. Response interpretation and fiscal reference recording slice.
5. Safe retry/replay/conflict history slice.
6. Unknown outcome and GET readback reconciliation slice.
7. ExitAuthorization gating update slice.
8. Operator Console fiscal exception queue projection slice.
9. Management Dashboard fiscal issuance metrics projection slice.
10. Audit/correlation/security hardening slice.
11. Test/UAT scenario pack slice.

Each slice should preserve authority boundaries and avoid implementing deferred POS Server features as if they already exist.

## 24. Requirements Traceability Summary

| Requirement area | Source / planning target |
| --- | --- |
| Fiscal issuance before ExitAuthorization | ExitPass BRD v1.3; ExitPass System Design v1.3; POS/Invoicing BRD v1.0; Sections 11, 19 |
| Central PMS payment finality authority | ExitPass System Design v1.3; Integration Contract; Sections 5, 11 |
| POS Server fiscal authority only | POS Server System Design v1.0; POS Server API Contract v1.0; Sections 5, 20 |
| Fiscal reference recording | Integration Contract; Sections 6, 15, 17 |
| Fiscal issuance state planning | ExitPass System Design v1.3; Sections 7, 8 |
| Idempotency/retry/replay/conflict | POS Server API Contract v1.0; Integration Contract; Sections 9, 10 |
| Unknown outcome and readback | Integration Contract; Sections 10, 12 |
| Operator Console fiscal exception review | Operator Console BRD v1.1; ExitPass System Design v1.3; Section 13 |
| Management Dashboard visibility | Management Dashboard BRD v1.0; ExitPass System Design v1.3; Section 14 |
| API/database/engineering pack gap | Locked v1.3 writing order; Sections 16, 17, 18, 23 |

## Appendix A: Glossary

| Term | Meaning |
| --- | --- |
| Central PMS | ExitPass platform authority for payment finality, fiscal reference recording, and normal ExitAuthorization. |
| POS Server / Site POS Server | Resolved Site fiscal issuance authority. |
| fiscal issuance reference | Central PMS linkage record to POS Server fiscal document identity and numbering evidence. |
| upstream finality reference | Current POS Server idempotency key source from approved payable basis. |
| fiscal issuance evidence | POS Server response/readback data indicating a persisted numbered fiscal document. |
| normal ExitAuthorization | Central PMS-issued authorization after normal payment and fiscal prerequisites are satisfied. |

## Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| API | Application Programming Interface |
| BIR | Bureau of Internal Revenue |
| DTO | Data Transfer Object |
| EJ | Electronic Journal |
| PMS | Parking Management System |
| POS | Point of Sale |
| UAT | User Acceptance Testing |
