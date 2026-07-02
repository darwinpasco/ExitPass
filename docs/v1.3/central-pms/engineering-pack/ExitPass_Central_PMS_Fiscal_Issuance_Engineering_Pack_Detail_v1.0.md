# ExitPass Central PMS Fiscal Issuance Engineering Pack Detail v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass Central PMS Fiscal Issuance Engineering Pack Detail |
| Version | v1.0 |
| Product scope | ExitPass v1.3 Central PMS |
| Status | Detail design orchestration pack |
| Repository | `D:\SourceCodes\ExitPass` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |
| Output format | Markdown only |

This document consolidates the Central PMS fiscal issuance implementation slice detail plans. It is documentation-only and does not implement Central PMS, POS Server, Operator Console, Management Dashboard, SQL, APIs, generated artifacts, UAT scripts, or runbook procedures.

## 2. Purpose and Scope

This Engineering Pack Detail expands the approved Central PMS Fiscal Issuance Engineering Pack Outline into coordinated implementation handoff plans for 11 future Central PMS slices.

It covers future planning for:

- Central PMS fiscal reference persistence and state.
- fiscal issuance orchestration.
- POS Server client/request mapping.
- success and replay recording.
- failure and `errorPosture` handling.
- unknown outcome and GET readback reconciliation.
- ExitAuthorization gating.
- Operator Console fiscal exception queues.
- Management Dashboard fiscal visibility.
- audit, events, and correlation.
- tests and UAT evidence.

It does not implement any system behavior.

## 3. Source Documentation Chain

Source-of-truth chain:

1. POS Server runtime numbered fiscal issuance.
2. POS Server API Contract.
3. POS Server response/status contract update.
4. Central PMS to POS Server Fiscal Issuance Integration Contract.
5. Central PMS fiscal issuance persistence/exception-state planning note.
6. Central PMS Fiscal Issuance Engineering Pack Outline.

Documents inspected as inputs:

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
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0_Review.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Runtime reference documents inspected:

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 4. Current Runtime / API Baseline

The current POS Server runtime/API baseline used for this pack:

- `POST /v1/fiscal-documents/` exists.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` exists.
- POS Server fiscal document creation uses `payableBasis.upstreamFinalityRef` as the current idempotency key.
- POS Server computes semantic request hash server-side.
- POS Server resolves fiscal identity server-side.
- POS Server resolves fiscal sequence policy server-side.
- POS Server locks the selected fiscal sequence state and allocates fiscal document number transactionally.
- POS Server returns fiscal identity and fiscal numbering fields after durable commit.
- POS Server returns `resultClassification`, `fiscalIssuanceEvidenceStatus`, `fiscalNumberAssignmentState`, and `fiscalDocumentStatusCodeId`.
- Duplicate same-key/same-hash requests return `resultClassification = idempotent_replay`.
- Same-key/different-hash requests fail closed as idempotency conflict.
- Missing complete fiscal numbering evidence fails closed with `fiscal_number_assignment_incomplete`.

## 5. Authority Boundaries

This pack preserves the following boundaries:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console supports review/governance only; it must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.
- Management Dashboard is visibility/reporting only.
- Cashier-Assisted Terminal and Continuity Terminal are channels/terminal surfaces, not fiscal authorities.

## 6. Cross-Slice Dependency Map

| Slice | Dependency posture |
| --- | --- |
| Slice 1: Database / state delta | Blocks most implementation because durable state and reference storage are prerequisites. |
| Slice 2: Orchestration service | Depends on Slice 1 for durable state and transition storage. |
| Slice 3: POS Server client / mapper | Can be designed now; implementation depends on Slice 1 and Slice 2. |
| Slices 4-6: success, failure, unknown outcome | Depend on Slices 1, 2, and 3. |
| Slice 7: ExitAuthorization gating | Depends on Slice 1 and the success/failure/readback handling from Slices 4-6. |
| Slices 8-9: Operator Console and Dashboard | Can be designed now; implementation depends on fiscal state/projections from earlier slices. |
| Slices 10-11: audit/events and tests | Can be designed now; implementation depends on earlier slices for final event and behavior surfaces. |

## 7. Implementation Sequencing Recommendation

Recommended sequencing:

1. Implement fiscal reference persistence/state first.
2. Add orchestration service shell without POS Server network calls.
3. Add POS Server client and request mapper.
4. Add success and replay recording.
5. Add conflict/failure/`errorPosture` handling.
6. Add unknown outcome and GET readback reconciliation.
7. Update ExitAuthorization gating.
8. Add Operator Console fiscal exception queue projections.
9. Add Management Dashboard visibility projections.
10. Harden audit/events/correlation.
11. Add integration tests and UAT evidence.

## 8. Summary of Detail Plans

### Plan 01: Database / State Delta

Defines candidate Central PMS storage objects, fiscal reference fields, candidate fiscal issuance states, exception reasons, attempt/history model, idempotency considerations using `payableBasis.upstreamFinalityRef`, unknown outcome/readback fields, linkage to payment/session/Site/Site POS Server, audit fields, migration sequencing, and open migration questions without writing SQL.

### Plan 02: Orchestration Service

Defines the Central PMS service boundary after payment finality, precondition checks, payable-basis readiness, statutory discount reference checks, Site POS Server routing, state transitions, ExitAuthorization boundary, dependency boundaries, event candidates, and a no-network-call boundary for Slice 1.

### Plan 03: POS Server Client / Mapper

Defines the client abstraction, configuration planning, request construction, field mapping, upstream finality reference rules, sensitive payload exclusions, correlation propagation, response parsing, GET readback behavior, and test fixture needs.

### Plan 04: Success / Replay Recording

Defines handling for `202 accepted` with `newly_created`, `202 accepted` with `idempotent_replay`, fiscal reference recording, duplicate prevention, replay reconciliation, mismatch detection, no duplicate ExitAuthorization, state transitions, audit events, operator visibility, and tests.

### Plan 05: Failure / ErrorPosture

Defines handling for `409 fiscal_document_idempotency_conflict`, 400 request correction, 400 fiscal configuration correction, 503 service failures, `fiscal_number_assignment_incomplete`, `errorPosture` mapping, exception states, manual review escalation, retry blocking rules, and audit/reporting.

### Plan 06: Unknown Outcome / Readback

Defines handling for POST timeout, network disconnect after possible commit, 503 with or without fiscal document id, fiscal reference recording failure after POS success, safe retry with the same upstream finality reference, GET readback decision matrix, reconciliation transitions, mismatch handling, operator review escalation, audit events, and tests.

### Plan 07: ExitAuthorization Gating

Defines gating rules requiring Central PMS payment finality, POS Server fiscal issuance evidence, assigned fiscal number state, and durable Central PMS fiscal reference. It also defines fail-closed behavior, candidate blocked authorization reasons, manual release boundary, incident/reconciliation tagging, flow impact, regression risks, and UAT scenarios.

### Plan 08: Operator Console Queues

Defines queue categories, display fields, filters/sorting, role-based access planning, review actions, supervisor escalation, manual release visibility, reconciliation close workflow, audit logging, and boundaries preserving Operator Console as governance only.

### Plan 09: Management Dashboard Visibility

Defines read-only fiscal visibility metrics, projection sources, freshness labels, source-of-truth labels, Site/Site POS Server breakdown, export/audit expectations, and dashboard non-authority boundaries.

### Plan 10: Audit / Events / Correlation

Defines candidate events, audit record expectations, correlation id propagation, sensitive data exclusions, event ownership, event ordering considerations, replay/idempotency audit rules, and traceability from payment finality to fiscal reference to ExitAuthorization decision.

### Plan 11: Test / UAT Evidence

Defines unit and integration test categories, mocked POS Server fixtures, retry/readback/replay matrix, ExitAuthorization gating regression tests, Operator Console queue visibility tests, Management Dashboard projection tests, security logging checks, UAT evidence checklist, and acceptance evidence planning.

## 9. First Implementation Slice Recommendation

Recommended first implementation branch:

`feature/central-pms-fiscal-reference-state`

If the repository uses task-number naming, use:

`feature/<task-number>-central-pms-fiscal-reference-state`

The first implementation slice should be persistence/state only. It should add or confirm the Central PMS fiscal reference and fiscal issuance state model without POS Server network calls.

## 10. Non-Goals

This pack does not:

- implement Central PMS source code.
- implement POS Server runtime changes.
- write SQL DDL.
- create migrations.
- create endpoint OpenAPI specs.
- define DTO classes.
- modify generated artifacts.
- modify DOCX files.
- implement Digital SI.
- implement printable Sales Invoice rendering.
- implement QR presentation.
- implement X-read, Z-read, BIR Sales Summary, Annex E, Electronic Journal, or POSLog.
- implement reprints, adjustments, reset counter, Z-counter, or Grand Total Amount mechanics.
- implement recovery automation.
- create gate integration endpoints.
- make Operator Console a payment/fiscal/exit authority.
- make Management Dashboard an operational authority.

## 11. Risks and Open Questions

- Central PMS schema may not yet support all required fiscal reference fields.
- Final state names, storage objects, and uniqueness constraints require database design confirmation.
- Final service-to-service authentication and credential posture remain open.
- Final retry scheduler ownership remains open.
- Handling POST timeout without fiscal document id remains sensitive.
- Durable post-commit gap/recovery policy remains POS Server/BIR/accounting dependent.
- Operator Console queue APIs and Dashboard projection APIs are not yet defined.
- ExitAuthorization gating requires careful regression testing.
- Manual release policy after fiscal issuance failure remains a separate governance decision.

## 12. Validation / Acceptance Approach

Future implementation acceptance should verify:

- fiscal reference persistence is durable and queryable.
- same-key/same-hash replay does not create duplicate fiscal references.
- same-key/different-hash conflict fails closed.
- incomplete fiscal evidence fails closed.
- unknown outcomes preserve same upstream finality reference and use safe retry/readback.
- normal ExitAuthorization is blocked until fiscal reference recording succeeds.
- manual release remains a separate governed exception.
- Operator Console and Management Dashboard remain non-authority surfaces.
- logs/audit exclude secrets, PAN/CVV, tokens, raw provider callbacks, and raw entitlement evidence.

## 13. Requirements Traceability Summary

| Requirement source | Detail coverage |
| --- | --- |
| POS Server API Contract | Plans 03-06 cover create/read, response fields, idempotency, replay, conflict, and error posture. |
| Central PMS integration contract | Plans 01-07 cover preconditions, request construction, fiscal reference recording, retry/readback, and ExitAuthorization gating. |
| Implementation planning note | Plans 01, 05, 06, 08, 09, and 10 expand persistence, exception states, queues, dashboard visibility, and audit. |
| Engineering Pack Outline | All 11 plans correspond to the approved implementation slice roadmap. |
| ExitPass v1.3 authority model | Authority boundaries are preserved in every plan. |

## 14. Appendix: Created Detail Plan Files

- `docs/v1.3/central-pms/engineering-pack/01-database-state-delta-plan.md`
- `docs/v1.3/central-pms/engineering-pack/02-orchestration-service-plan.md`
- `docs/v1.3/central-pms/engineering-pack/03-pos-server-client-mapper-plan.md`
- `docs/v1.3/central-pms/engineering-pack/04-success-replay-recording-plan.md`
- `docs/v1.3/central-pms/engineering-pack/05-failure-errorposture-plan.md`
- `docs/v1.3/central-pms/engineering-pack/06-unknown-outcome-readback-plan.md`
- `docs/v1.3/central-pms/engineering-pack/07-exitauthorization-gating-plan.md`
- `docs/v1.3/central-pms/engineering-pack/08-operator-console-queues-plan.md`
- `docs/v1.3/central-pms/engineering-pack/09-dashboard-visibility-plan.md`
- `docs/v1.3/central-pms/engineering-pack/10-audit-events-correlation-plan.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
