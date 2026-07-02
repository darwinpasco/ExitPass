# ExitPass Central PMS Fiscal Issuance Engineering Pack Outline v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass Central PMS Fiscal Issuance Engineering Pack Outline |
| Version | v1.0 |
| Product scope | ExitPass v1.3 Central PMS |
| Status | Engineering Pack outline |
| Output format | Markdown only |
| Repository | `D:\SourceCodes\ExitPass` |
| Runtime reference inspected | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |

This document is an engineering pack outline only. It does not implement source code, SQL, migrations, generated artifacts, endpoint OpenAPI specifications, deployment scripts, UAT scripts, or runbook procedures.

## 2. Purpose and Scope

This outline translates the approved fiscal issuance documentation chain into future Central PMS implementation slices.

Current documentation chain:

1. POS Server runtime numbered fiscal issuance.
2. POS Server API Contract.
3. POS Server response/status contract update.
4. Central PMS to POS Server Fiscal Issuance Integration Contract.
5. Central PMS Fiscal Issuance Reference and Exception-State Implementation Planning Note.

The outline identifies implementation areas, sequencing, deliverables, validation approach, and non-goals for future Central PMS development.

## 3. Inputs and Baseline References

Documentation references:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0_Alignment_Review.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0_Review.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0_Review.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Runtime references:

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 4. Current Runtime/API Baseline

Current POS Server runtime/API baseline:

- `POST /v1/fiscal-documents/` is implemented.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` is implemented.
- POS Server uses `payableBasis.upstreamFinalityRef` as the current fiscal issuance idempotency key.
- POS Server computes semantic request hash server-side.
- POS Server resolves fiscal identity server-side.
- POS Server resolves fiscal sequence policy server-side.
- POS Server locks fiscal sequence state and allocates fiscal document number transactionally.
- POS Server returns `resultClassification`, `fiscalIssuanceEvidenceStatus`, `fiscalNumberAssignmentState`, `fiscalDocumentStatusCodeId`, and fiscal numbering fields.
- POS Server fails closed on same-key/different-hash conflict.
- POS Server fails closed with `fiscal_number_assignment_incomplete` when complete fiscal numbering evidence is missing.

Central PMS engineering work remains future implementation.

## 5. Engineering Objectives

Central PMS implementation must enable:

- durable fiscal reference recording after POS Server success/replay
- safe handling of idempotent replay
- fail-closed handling of idempotency conflict
- fail-closed handling of request/configuration/service failures
- unknown outcome handling
- GET readback and reconciliation
- ExitAuthorization gating based on fiscal reference persistence
- operator visibility for fiscal exceptions
- dashboard visibility for fiscal issuance health and exceptions
- audit/correlation from payment finality to fiscal reference to ExitAuthorization decision

## 6. Non-Goals

This Engineering Pack outline does not include:

- implementing POS Server runtime changes
- implementing Digital SI
- implementing printable SI
- implementing QR presentation
- implementing X-read/Z-read
- implementing BIR Sales Summary/Annex E
- implementing EJ/POSLog
- implementing reprints
- implementing adjustments
- implementing reset/Z-counter/GTA
- implementing recovery automation
- implementing gate opening from POS Server
- making Operator Console a payment/fiscal authority
- making Management Dashboard operational authority
- writing SQL DDL
- writing source code
- creating endpoint OpenAPI specs

## 7. Authority Boundaries

The Engineering Pack must preserve:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console supports review/governance only; it must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.
- Management Dashboard is visibility/reporting only.

## 8. Proposed Implementation Slice Roadmap

Recommended implementation sequence:

| Slice | Name | Primary outcome |
| --- | --- | --- |
| 1 | Central PMS fiscal reference persistence model | Central PMS can store fiscal reference and integration state fields. |
| 2 | Fiscal issuance orchestration service shell | Central PMS has the internal boundary and precondition checks. |
| 3 | POS Server client and request mapper | Central PMS can build safe POS Server create/read requests. |
| 4 | Successful issuance and replay handling | Central PMS records success and reconciles replay. |
| 5 | Conflict/failure/errorPosture handling | Central PMS maps fail-closed outcomes to exception states. |
| 6 | Unknown outcome and GET readback reconciliation | Central PMS can recover/reconcile uncertain outcomes safely. |
| 7 | ExitAuthorization gating update | Normal ExitAuthorization requires fiscal reference persistence. |
| 8 | Operator Console fiscal exception queues | Governance users can review fiscal exception cases. |
| 9 | Management Dashboard fiscal visibility projections | Reporting users can see fiscal issuance health. |
| 10 | Audit/events/correlation hardening | Traceability is complete across payment, fiscal, and exit. |
| 11 | Integration tests and UAT evidence | The integration has documented verification coverage. |

Each slice should be independently reviewable and must not implement deferred POS Server features as current capability.

## 9. Slice 1: Central PMS Fiscal Reference Persistence Model

Objective:

- Add or confirm Central PMS persistence for fiscal issuance references and current fiscal issuance integration state.

Planning deliverables:

- database delta design for fiscal reference storage
- state field or state model design
- attempt/history persistence design
- uniqueness/idempotency guard for upstream finality reference where applicable
- audit retention and queryability plan

Fields to plan:

- POS Server fiscal document id
- fiscal identity id
- fiscal sequence policy id
- fiscal sequence value
- fiscal document number
- fiscal series
- fiscal number prefix text
- fiscal number suffix text
- fiscal number assigned at
- fiscal number assigned by ref
- fiscal document status code id
- result classification
- fiscal issuance evidence status
- fiscal number assignment state
- upstream finality reference
- payment/session references
- Site/Site POS Server references
- retry/replay/conflict history
- exception state/reason

Do not write DDL in this outline. This is future database delta work.

## 10. Slice 2: Fiscal Issuance Orchestration Service Shell

Objective:

- Create the Central PMS service boundary responsible for deciding when fiscal issuance should be requested.

Planning deliverables:

- service responsibility definition
- precondition validation logic outline
- payment finality check
- payable-basis readiness check
- statutory discount validation reference check
- Site/Site POS Server routing check
- no-ExitAuthorization-before-fiscal-reference rule

The service shell must not issue fiscal documents itself. It orchestrates calls to POS Server.

## 11. Slice 3: POS Server Client and Request Mapper

Objective:

- Build the Central PMS integration boundary for POS Server fiscal document create/read APIs.

Planning deliverables:

- POS Server client abstraction
- request mapper from Central PMS payment/session/payable-basis state
- stable `payableBasis.upstreamFinalityRef` mapping
- document line/tender/tax/total mapping plan
- approved discount reference mapping plan
- sensitive payload exclusion rules
- correlation id propagation
- service configuration and timeout planning

The mapper must not send raw sensitive evidence, raw provider callbacks, PAN/CVV, tokens, secrets, credentials, or uncontrolled raw payment payloads.

## 12. Slice 4: Successful Issuance and Replay Handling

Objective:

- Record successful POS Server fiscal issuance and reconcile idempotent replay.

Planning deliverables:

- handler for `202 accepted` + `resultClassification = newly_created`
- handler for `202 accepted` + `resultClassification = idempotent_replay`
- fiscal reference recording logic
- duplicate fiscal reference prevention
- replay reconciliation rule
- mismatch detection for existing Central PMS fiscal references
- no duplicate ExitAuthorization rule

Central PMS may treat replay as successful only when `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned` and `fiscalNumberAssignmentState = assigned`.

## 13. Slice 5: Conflict/Failure/errorPosture Handling

Objective:

- Map POS Server failures to Central PMS exception states and review queues.

Planning deliverables:

- `409 fiscal_document_idempotency_conflict` fail-closed handling
- `400` request correction handling
- `400` fiscal configuration correction handling
- `503` persistence/service failure handling
- `fiscal_number_assignment_incomplete` handling
- `errorPosture` mapping
- exception-state transition plan
- operator/manual review escalation plan

Central PMS must not issue normal ExitAuthorization for unresolved fiscal issuance failures.

## 14. Slice 6: Unknown Outcome and GET Readback Reconciliation

Objective:

- Safely resolve cases where fiscal issuance outcome is uncertain.

Planning deliverables:

- POST timeout handling
- network disconnect handling
- POST `503` with `fiscalDocumentId`
- POST `503` without `fiscalDocumentId`
- GET readback job
- replay-after-timeout handling
- fiscal reference recording failure recovery after POS Server success
- reconciliation of Central PMS reference vs POS Server readback

Unknown outcome must not become implicit fiscal success.

## 15. Slice 7: ExitAuthorization Gating Update

Objective:

- Update normal ExitAuthorization eligibility to require fiscal issuance reference persistence.

Planning gating rule:

Normal ExitAuthorization is blocked until:

1. payment finality is verified by Central PMS
2. POS Server success/replay returned fiscal issuance evidence
3. `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`
4. `fiscalNumberAssignmentState = assigned`
5. Central PMS durably recorded fiscal reference

Planning deliverables:

- eligibility check update
- fiscal state integration into exit authorization decision
- exception/manual release boundary
- regression tests for pre-existing exit paths
- audit event for blocked ExitAuthorization due to fiscal state

Exception/manual release remains separate, auditable, incident-tagged, and reconciliation-tagged.

## 16. Slice 8: Operator Console Fiscal Exception Queues

Objective:

- Provide governance visibility and review workflows for fiscal issuance exceptions.

Queue categories:

- pending fiscal issuance
- retry needed
- configuration correction required
- idempotency conflict
- unknown outcome
- incomplete numbering evidence
- fiscal reference mismatch
- manual release requested after fiscal failure
- reconciled/closed exceptions

Planning deliverables:

- queue projection contract
- queue filter/sort fields
- role-based access model
- review action list
- manual release escalation visibility
- audit logging for review actions

Operator Console remains review/governance only.

## 17. Slice 9: Management Dashboard Fiscal Visibility Projections

Objective:

- Provide read-only fiscal issuance health and exception visibility.

Metrics:

- success rate
- failures by category
- replay count
- conflict count
- unknown outcome count
- pending exception count
- manual release count tied to fiscal issuance exception
- average time from payment finality to fiscal reference recording
- Site breakdown
- Site POS Server breakdown

Planning deliverables:

- dashboard projection inputs
- source-of-truth labels
- freshness/latency expectations
- access scope by Site/Site POS Server
- export/audit expectations

Management Dashboard remains visibility only.

## 18. Slice 10: Audit/Events/Correlation Hardening

Objective:

- Ensure traceability from payment finality to fiscal issuance reference to ExitAuthorization decision.

Candidate events:

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

Planning deliverables:

- event list and ownership
- audit record expectations
- correlation id propagation
- sensitive data exclusion checks
- access-controlled audit review
- traceability verification checklist

Candidate event names remain implementation placeholders.

## 19. Slice 11: Integration Tests and UAT Evidence

Objective:

- Prove Central PMS handles current POS Server fiscal issuance behavior safely.

Scenarios:

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
- Dashboard metrics visibility
- no duplicate fiscal reference after replay
- no duplicate ExitAuthorization after replay

Deliverables:

- integration test plan
- mocked POS Server response fixtures
- readback/retry test matrix
- UAT scenario checklist
- evidence capture format

## 20. Database/State Planning Summary

Candidate fiscal issuance state names:

- `not_required`
- `pending_fiscal_issuance`
- `fiscal_issuance_requested`
- `fiscal_issuance_recorded`
- `fiscal_issuance_replayed`
- `fiscal_issuance_conflict`
- `fiscal_issuance_failed_request`
- `fiscal_issuance_failed_configuration`
- `fiscal_issuance_failed_service`
- `fiscal_issuance_unknown`
- `fiscal_issuance_manual_review`
- `fiscal_issuance_exception_released`
- `fiscal_issuance_reconciled`

These are candidate names pending Engineering Pack/database delta confirmation.

Database/state planning must include:

- fiscal reference persistence
- current state
- attempt/history
- exception reason
- retry/replay/conflict tracking
- readback/reconciliation results
- review assignment/status
- dashboard projection source

## 21. API/Service Planning Summary

Future Central PMS service/API needs:

- fiscal issuance orchestration service
- POS Server client abstraction
- request mapper
- result handler
- retry scheduler
- readback/reconciliation worker
- fiscal exception query APIs for Operator Console
- dashboard projection feed
- ExitAuthorization gating check update
- audit/event publisher
- correlation id propagation
- `errorPosture` mapping

Final endpoint paths, DTOs, and service classes are not defined in this outline.

## 22. Event/Job/Queue Planning Summary

Candidate jobs/queues:

- fiscal issuance retry job
- fiscal issuance readback job
- fiscal issuance reconciliation job
- Operator Console fiscal exception queue projection
- Management Dashboard fiscal visibility projection
- stale exception escalation job

Candidate events:

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

Names are placeholders pending engineering conventions.

## 23. Operator Console Planning Summary

Operator Console implementation planning should include:

- fiscal exception queue API
- queue categories and filters
- role-based queue access
- review notes and reason capture
- supervisor escalation path
- manual release request visibility
- reconciliation close visibility
- audit log for all review actions

Operator Console must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.

## 24. Management Dashboard Planning Summary

Management Dashboard implementation planning should include:

- fiscal issuance health metrics
- replay/conflict/unknown outcome metrics
- pending exception metrics
- manual release count tied to fiscal issuance exception
- Site/Site POS Server breakdown
- average payment-finality-to-fiscal-reference time
- dashboard freshness labels
- source-of-truth labels
- export and access audit rules

Management Dashboard must remain read-only visibility/reporting.

## 25. Security/Access Control Planning

Security/access planning must include:

- only Central PMS service identity may call POS Server fiscal issuance path
- service-to-service auth/mTLS/token model remains open
- Operator Console role-based fiscal exception review
- Management Dashboard read-only visibility
- logs must exclude secrets, PAN/CVV, tokens, raw provider callbacks, raw entitlement evidence
- audit records must be access-controlled
- retry/manual review actions must be attributable
- fiscal exception access must be scoped by Site/Site POS Server where applicable

## 26. Test/UAT Planning

Test/UAT planning should produce:

- unit test plan for fiscal issuance state transitions
- integration test plan for POS Server client behavior
- mocked POS Server response fixtures
- retry/readback/replay matrix
- ExitAuthorization gating regression tests
- Operator Console queue visibility tests
- Management Dashboard projection tests
- security logging checks
- UAT evidence checklist

Test artifacts must prove fiscal failures do not issue normal ExitAuthorization.

## 27. Rollout/Feature Flag Planning

Rollout planning should include:

- feature flag for fiscal-before-ExitAuthorization enforcement
- environment/site-level rollout
- shadow-readiness mode if needed
- dry-run must not issue fiscal numbers unless explicitly wired to non-production POS Server/test policy
- production enablement requires configured Site POS Server/fiscal identity/sequence policy/sequence state
- rollback must not break existing payment finality records
- rollback must preserve recorded fiscal references
- manual exception procedure must be approved before production enforcement
- operational readiness check for Operator Console and Dashboard visibility

## 28. Risks and Open Questions

Risks and open questions:

- Central PMS schema may not yet support all fiscal reference fields.
- Final retry scheduler ownership is unknown.
- Final service auth model is unknown.
- Handling POST timeout without `fiscalDocumentId` remains sensitive.
- Durable post-commit gap/recovery policy remains POS Server/BIR/accounting dependent.
- Operator Console and Dashboard APIs are not yet defined.
- ExitAuthorization gating implementation needs careful regression testing.
- Feature-flag rollout must avoid fiscal numbering in non-production dry-run unless explicitly configured.
- Manual release policy after fiscal failure remains separately governed.
- Cross-service error envelope standard remains open.

## 29. Recommended First Implementation Branch

Recommended first implementation branch:

`feature/central-pms-fiscal-reference-state`

If repository branch naming uses numbered tasks:

`feature/<task-number>-central-pms-fiscal-reference-state`

The first implementation slice should be persistence/state only, with no POS Server network calls yet.

## 30. Requirements Traceability Summary

| Requirement area | Source / outline sections |
| --- | --- |
| Fiscal issuance before ExitAuthorization | ExitPass BRD v1.3; ExitPass System Design v1.3; POS/Invoicing BRD v1.0; Sections 5, 15, 19, 26 |
| POS Server API runtime behavior | POS Server API Contract v1.0; Sections 4, 11, 12, 13, 14 |
| Central PMS integration behavior | Central PMS to POS Server Fiscal Issuance Integration Contract; Sections 8-18 |
| Fiscal reference persistence | Implementation Planning Note; Sections 9, 20 |
| Fiscal exception state | Implementation Planning Note; Sections 13, 20, 22 |
| Operator Console queues | Operator Console BRD v1.1; Sections 16, 23 |
| Management Dashboard visibility | Management Dashboard BRD v1.0; Sections 17, 24 |
| Security and audit | ExitPass System Design v1.3; Sections 18, 25 |
| Test/UAT evidence | Sections 19, 26 |

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
