# ExitPass Central PMS Fiscal Issuance State Transition and Exception Taxonomy Plan v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass Central PMS Fiscal Issuance State Transition and Exception Taxonomy Plan |
| Version | v1.0 |
| Product scope | ExitPass v1.3 Central PMS |
| Status | Documentation-only state/taxonomy planning |
| Target implementation branch | `feature/central-pms-fiscal-reference-state` |
| Repository | `D:\SourceCodes\ExitPass` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |
| Output format | Markdown only |

This document defines candidate fiscal issuance state and exception taxonomy rules for future Central PMS implementation. It does not define final enum declarations, SQL DDL, migrations, endpoint contracts, source code, retry scheduler implementation, or runtime behavior.

## 2. Purpose and Scope

This plan defines:

- candidate fiscal issuance states
- allowed candidate transitions
- terminal and non-terminal classification
- retry eligibility
- replay handling
- unknown outcome handling
- manual review and reconciliation states
- ExitAuthorization gating eligibility
- exception reason taxonomy
- `errorPosture` to state mapping
- HTTP/code to state mapping
- Operator Console queue mapping
- Management Dashboard metric/state mapping
- audit/event mapping

The next real implementation slice remains persistence/state only, with no POS Server network calls.

## 3. Source Documentation Baseline

Source-of-truth chain:

1. POS Server runtime numbered fiscal issuance.
2. POS Server API Contract.
3. POS Server response/status contract update.
4. Central PMS to POS Server Fiscal Issuance Integration Contract.
5. Central PMS fiscal issuance persistence/exception-state planning note.
6. Central PMS Fiscal Issuance Engineering Pack Outline.
7. Central PMS Fiscal Issuance Engineering Pack Detail.
8. Central PMS Fiscal Reference Persistence Database Delta Plan.

Documents inspected:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/01-database-state-delta-plan.md`
- `docs/v1.3/central-pms/engineering-pack/04-success-replay-recording-plan.md`
- `docs/v1.3/central-pms/engineering-pack/05-failure-errorposture-plan.md`
- `docs/v1.3/central-pms/engineering-pack/06-unknown-outcome-readback-plan.md`
- `docs/v1.3/central-pms/engineering-pack/07-exitauthorization-gating-plan.md`
- `docs/v1.3/central-pms/engineering-pack/08-operator-console-queues-plan.md`
- `docs/v1.3/central-pms/engineering-pack/09-dashboard-visibility-plan.md`
- `docs/v1.3/central-pms/engineering-pack/10-audit-events-correlation-plan.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
- `docs/v1.3/central-pms/engineering-pack/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Detail_v1.0.md`
- `docs/v1.3/central-pms/database-delta/ExitPass_Central_PMS_Fiscal_Reference_Persistence_Database_Delta_Plan_v1.0.md`
- `docs/v1.3/central-pms/database-delta/ExitPass_Central_PMS_Fiscal_Reference_Persistence_Database_Delta_Plan_v1.0_Review.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Runtime references inspected read-only:

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 4. Current Runtime / API Assumptions

Current POS Server runtime/API assumptions:

- `POST /v1/fiscal-documents/` exists.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` exists.
- POS Server uses `payableBasis.upstreamFinalityRef` as current idempotency key.
- POS Server computes semantic request hash server-side.
- POS Server resolves fiscal identity and fiscal sequence policy server-side.
- POS Server allocates fiscal document number transactionally.
- POS Server returns `resultClassification`, `fiscalIssuanceEvidenceStatus`, `fiscalNumberAssignmentState`, and `fiscalDocumentStatusCodeId`.
- `resultClassification = newly_created` indicates a newly persisted numbered fiscal document.
- `resultClassification = idempotent_replay` indicates replay of the original persisted numbered fiscal document for the same idempotency key and semantic hash.
- Missing complete fiscal numbering evidence fails closed with `fiscal_number_assignment_incomplete`.

## 5. State Taxonomy Objectives

The future Central PMS implementation should use the state taxonomy to:

- persist clear fiscal issuance state.
- prevent normal ExitAuthorization before complete fiscal evidence is recorded.
- classify retry eligibility.
- classify manual review and reconciliation needs.
- classify Operator Console queue entries.
- classify Management Dashboard metric groups.
- preserve audit traceability.
- prevent replay, conflict, and unknown outcomes from creating duplicate fiscal references or duplicate exit decisions.

## 6. Non-Goals

This plan does not include:

- final enum implementation.
- SQL DDL.
- migrations.
- source code.
- endpoint OpenAPI specs.
- retry scheduler implementation.
- Operator Console implementation.
- Dashboard implementation.
- ExitAuthorization gating implementation.
- POS Server runtime changes.
- final queue labels or dashboard UI labels.

## 7. Authority Boundaries

The taxonomy must preserve:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console supports review/governance only.
- Management Dashboard is visibility/reporting only.

## 8. Candidate Fiscal Issuance State Catalog

| Candidate state | Planning meaning | Typical entry trigger | Retry eligibility | Manual review eligibility | Normal ExitAuthorization eligibility | Terminal classification | Dashboard grouping | Operator queue grouping |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `not_required` | Fiscal issuance is not required under approved policy. | Policy marks fiscal issuance not required. | Not applicable. | Usually no, unless policy review is needed. | Eligible only if approved policy explicitly allows. | Terminal for fiscal issuance. | not required / excluded | none or policy review |
| `pending_fiscal_issuance` | Payment finality exists and fiscal issuance is waiting to start. | Payment finality recorded. | Can initiate first request. | No, unless stuck. | Not eligible. | Non-terminal. | pending | pending fiscal issuance |
| `fiscal_issuance_requested` | Fiscal issuance request has started or is in-flight. | Fiscal issuance request started. | No duplicate concurrent request unless timeout/lease policy allows. | No, unless stale. | Not eligible. | Non-terminal. | pending | pending fiscal issuance |
| `fiscal_issuance_recorded` | Complete POS Server fiscal evidence is durably recorded by Central PMS. | 202 newly-created success or readback success with complete evidence and durable record. | No retry needed. | Only if later mismatch occurs. | Eligible if all other exit conditions pass. | Terminal successful state. | success | none |
| `fiscal_issuance_replayed` | Idempotent replay returned original complete fiscal evidence and Central PMS reconciled/recorded it. | 202 idempotent replay with complete evidence and durable record/reconciliation. | No retry needed. | Only if replay mismatch occurs. | Eligible only when complete, reconciled, and no mismatch exists. | Terminal successful state when reconciled. | replay | none or replay review |
| `fiscal_issuance_conflict` | Same idempotency key with different semantic request facts. | 409 idempotency conflict. | No automatic retry. | Yes. | Not eligible. | Non-terminal exception until reviewed/reconciled. | conflict | idempotency conflict |
| `fiscal_issuance_failed_request` | Request/data validation failed. | 400 request/data validation failure. | Retry only after request correction. | Yes if customer-impacting or repeated. | Not eligible. | Non-terminal exception. | request failure | retry needed |
| `fiscal_issuance_failed_configuration` | Fiscal identity, policy, sequence state, or configuration requires correction. | 400 fiscal setup/configuration failure. | Retry only after configuration correction. | Yes. | Not eligible. | Non-terminal exception. | configuration failure | configuration correction required |
| `fiscal_issuance_failed_service` | Persistence/service recovery is required. | 503 service/persistence failure or incomplete numbering evidence without confirmed commit. | Retry only after service recovery. | Yes if prolonged or customer-impacting. | Not eligible. | Non-terminal exception. | service failure | retry needed / incomplete numbering evidence |
| `fiscal_issuance_unknown` | Outcome is unknown or readback is inconclusive. | Timeout, network disconnect, uncertain 503, inconclusive readback. | Retry same semantic request with same upstream finality reference or GET readback where possible. | Yes when unresolved. | Not eligible. | Non-terminal exception. | unknown | unknown outcome |
| `fiscal_issuance_manual_review` | Case is under operator/supervisor/reconciliation review. | Manual escalation, mismatch, unresolved exception. | Retry only according to review decision. | Yes. | Not eligible for normal flow. | Non-terminal review. | manual review | fiscal reference mismatch / manual review |
| `fiscal_issuance_exception_released` | Approved exception/manual release occurred. | Approved manual exception release. | No retry for normal gating unless reconciliation resumes separately. | Subject to post-review. | Not normal ExitAuthorization; exception/manual release only. | Terminal exception state until reconciled. | exception release | manual release requested after fiscal failure |
| `fiscal_issuance_reconciled` | Exception or unknown case was reconciled/closed. | Reconciliation closure. | Usually no. | Closed, unless reopened. | Eligible only if reconciliation confirms complete fiscal evidence and policy permits. | Terminal closure state. | reconciled | reconciled/closed exceptions |

Final state names and enum values remain deferred.

## 9. State Transition Rules

Candidate transitions:

| From | Trigger | To |
| --- | --- | --- |
| no fiscal state | payment finality recorded and fiscal issuance required | `pending_fiscal_issuance` |
| no fiscal state | policy says fiscal issuance not required | `not_required` |
| `pending_fiscal_issuance` | fiscal issuance request started | `fiscal_issuance_requested` |
| `fiscal_issuance_requested` | POS Server 202 newly-created + complete evidence + durable Central PMS record | `fiscal_issuance_recorded` |
| `fiscal_issuance_requested` or `fiscal_issuance_unknown` | POS Server 202 idempotent replay + complete evidence + durable record/reconciliation | `fiscal_issuance_replayed` |
| `fiscal_issuance_requested` | POS Server 409 idempotency conflict | `fiscal_issuance_conflict` |
| `fiscal_issuance_requested` | POS Server 400 request correction failure | `fiscal_issuance_failed_request` |
| `fiscal_issuance_requested` | POS Server 400 fiscal identity/policy/state/configuration failure | `fiscal_issuance_failed_configuration` |
| `fiscal_issuance_requested` | POS Server 503 persistence/service failure with no commit evidence | `fiscal_issuance_failed_service` |
| `fiscal_issuance_requested` | POS Server 503 with uncertain commit or fiscal document id needing readback | `fiscal_issuance_unknown` |
| `fiscal_issuance_requested` | POST timeout or network disconnect | `fiscal_issuance_unknown` |
| `fiscal_issuance_unknown` | GET readback complete evidence and reference recorded | `fiscal_issuance_recorded` or `fiscal_issuance_replayed`, based on context |
| any unresolved failure/unknown/conflict | mismatch or inconclusive readback | `fiscal_issuance_manual_review` |
| any unresolved failure/unknown/conflict | operator escalates unresolved case | `fiscal_issuance_manual_review` |
| `fiscal_issuance_manual_review` | approved manual exception release | `fiscal_issuance_exception_released` |
| unresolved or released exception | reconciliation closure | `fiscal_issuance_reconciled` |

Final transition constraints, concurrency rules, and persistence enforcement remain deferred to implementation.

## 10. Terminal and Non-Terminal State Classification

Terminal successful states:

- `fiscal_issuance_recorded`
- `fiscal_issuance_replayed`, only when complete and reconciled

Terminal policy states:

- `not_required`, only under explicit approved policy

Terminal closure/exception states:

- `fiscal_issuance_exception_released`, as exception/manual release only
- `fiscal_issuance_reconciled`, when closure is approved

Non-terminal working states:

- `pending_fiscal_issuance`
- `fiscal_issuance_requested`

Non-terminal exception/review states:

- `fiscal_issuance_conflict`
- `fiscal_issuance_failed_request`
- `fiscal_issuance_failed_configuration`
- `fiscal_issuance_failed_service`
- `fiscal_issuance_unknown`
- `fiscal_issuance_manual_review`

## 11. ExitAuthorization Gating Eligibility by State

Eligible for normal ExitAuthorization only when:

- Central PMS payment finality is verified.
- fiscal evidence is complete.
- `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`.
- `fiscalNumberAssignmentState = assigned`.
- Central PMS fiscal reference is durably recorded.
- no mismatch, conflict, or unresolved exception exists.

Candidate eligible states:

- `fiscal_issuance_recorded`
- `fiscal_issuance_replayed`, only when reconciled and complete
- `fiscal_issuance_reconciled`, only if reconciliation confirms complete fiscal evidence and policy permits
- `not_required`, only if approved policy explicitly states fiscal issuance is not required

Not eligible:

- `pending_fiscal_issuance`
- `fiscal_issuance_requested`
- `fiscal_issuance_conflict`
- `fiscal_issuance_failed_request`
- `fiscal_issuance_failed_configuration`
- `fiscal_issuance_failed_service`
- `fiscal_issuance_unknown`
- `fiscal_issuance_manual_review`
- `fiscal_issuance_exception_released` as normal ExitAuthorization; this is exception/manual release only

## 12. Retry Eligibility by State

| Candidate state | Retry posture |
| --- | --- |
| `not_required` | No fiscal issuance retry. |
| `pending_fiscal_issuance` | Can initiate first request. |
| `fiscal_issuance_requested` | No duplicate concurrent request unless timeout/lease policy allows. |
| `fiscal_issuance_recorded` | No retry needed; readback only for diagnostics/reconciliation. |
| `fiscal_issuance_replayed` | No retry needed; readback only for diagnostics/reconciliation. |
| `fiscal_issuance_conflict` | No automatic retry; manual review only. |
| `fiscal_issuance_failed_request` | Retry only after request correction. |
| `fiscal_issuance_failed_configuration` | Retry only after operator/configuration correction. |
| `fiscal_issuance_failed_service` | Retry only after service recovery. |
| `fiscal_issuance_unknown` | Retry same semantic request with same upstream finality reference or GET readback where possible. |
| `fiscal_issuance_manual_review` | Retry only according to review decision. |
| `fiscal_issuance_exception_released` | No normal retry unless reconciliation policy reopens case. |
| `fiscal_issuance_reconciled` | No retry unless case is reopened under approved policy. |

## 13. Replay Handling by State

Replay handling rules:

- Replay from `fiscal_issuance_unknown` can recover fiscal evidence when same-key/same-hash retry returns original numbered document.
- Replay from `fiscal_issuance_requested` can complete the in-flight case when duplicate delivery occurs.
- Replay after `fiscal_issuance_recorded` should reconcile against existing Central PMS reference and not create duplicates.
- Replay mismatch should move to `fiscal_issuance_manual_review`.
- Replay must not allocate another fiscal number.
- Replay must not trigger duplicate normal ExitAuthorization.

## 14. Unknown Outcome Handling by State

Unknown outcome handling:

- `fiscal_issuance_requested` can move to `fiscal_issuance_unknown` on timeout, disconnect, or uncertain response.
- `fiscal_issuance_failed_service` can move to `fiscal_issuance_unknown` when commit status is unclear.
- `fiscal_issuance_unknown` should preserve same upstream finality reference.
- `fiscal_issuance_unknown` may proceed to `fiscal_issuance_recorded` or `fiscal_issuance_replayed` after successful readback/replay and durable reference recording.
- `fiscal_issuance_unknown` should proceed to `fiscal_issuance_manual_review` when readback is inconclusive, mismatched, or operationally sensitive.

Unknown outcome must never be treated as fiscal success.

## 15. Manual Review and Exception Release States

Manual review states are for unresolved or sensitive cases:

- conflict
- mismatch
- inconclusive readback
- repeated request failure
- repeated configuration failure
- repeated service failure
- manual release requested after fiscal failure

`fiscal_issuance_manual_review` is not eligible for normal ExitAuthorization.

`fiscal_issuance_exception_released` means an approved exception/manual release occurred. It is not normal ExitAuthorization and must remain incident-tagged, audit-tagged, reconciliation-tagged, and subject to closure review.

## 16. Reconciliation States

`fiscal_issuance_reconciled` should mean the case has been reviewed and closed.

Reconciliation closure should record:

- closure result
- evidence used for closure
- whether fiscal evidence was ultimately confirmed
- whether manual exception release occurred
- approver/reviewer
- closure timestamp
- incident/reconciliation reference

Reconciled state can satisfy normal gating only if it confirms complete fiscal evidence and policy permits. Otherwise it is closure visibility only.

## 17. Exception Reason Taxonomy

Request/data reasons:

- `missing_payable_basis`
- `missing_upstream_finality_reference`
- `unapproved_discount_reference`
- `unsupported_fiscal_document_request`
- `invalid_fiscal_tender`
- `missing_fiscal_tender`
- `invalid_fiscal_tax_detail`
- `invalid_fiscal_discount_privilege_detail`
- `invalid_fiscal_total`
- `sensitive_payload_rejected`
- `request_construction_error`

Configuration/fiscal setup reasons:

- `fiscal_identity_not_found`
- `fiscal_identity_ambiguous`
- `fiscal_identity_not_effective`
- `fiscal_sequence_policy_not_found`
- `fiscal_sequence_policy_ambiguous`
- `fiscal_sequence_policy_not_effective`
- `fiscal_sequence_state_not_found`
- `fiscal_sequence_state_not_effective`
- `fiscal_number_allocation_failed`
- `fiscal_document_number_format_failed`

Conflict/replay reasons:

- `fiscal_document_idempotency_conflict`
- `replay_mismatch`
- `duplicate_reference_detected`

Service/unknown reasons:

- `persistence_not_configured`
- `invalid_persistence_configuration`
- `persistence_write_failed`
- `fiscal_number_assignment_incomplete`
- `post_timeout`
- `network_disconnect_after_possible_commit`
- `get_readback_not_found`
- `get_readback_service_failed`
- `get_readback_inconclusive`
- `central_pms_reference_persistence_failed`

Review/manual release reasons:

- `manual_review_required`
- `manual_release_requested_after_fiscal_failure`
- `fiscal_reference_mismatch`
- `reconciliation_required`
- `reconciliation_closed`

Reason names are candidate values only.

## 18. ErrorPosture-to-State Mapping

| `errorPosture` | Candidate state | Retry posture |
| --- | --- | --- |
| `do_not_retry_without_request_change` | `fiscal_issuance_failed_request` or `fiscal_issuance_conflict`, depending on code | No automatic retry. Correct request or investigate conflict. |
| `retry_after_configuration_correction` | `fiscal_issuance_failed_configuration` | Retry after operator/configuration correction only. |
| `retry_after_service_recovery` | `fiscal_issuance_failed_service` or `fiscal_issuance_unknown` | Retry after service recovery or controlled readback. |

`errorPosture` is guidance, not a retry scheduler.

## 19. HTTP / Code-to-State Mapping

| Runtime response | Candidate state |
| --- | --- |
| 202 accepted + `newly_created` + complete evidence | `fiscal_issuance_recorded` |
| 202 accepted + `idempotent_replay` + complete evidence | `fiscal_issuance_replayed` |
| 409 `fiscal_document_idempotency_conflict` | `fiscal_issuance_conflict` |
| 400 request/data validation codes | `fiscal_issuance_failed_request` |
| 400 fiscal identity/policy/state/configuration codes | `fiscal_issuance_failed_configuration` |
| 503 persistence/service codes | `fiscal_issuance_failed_service` |
| 503 `fiscal_number_assignment_incomplete` | `fiscal_issuance_failed_service` or `fiscal_issuance_unknown`, depending on fiscalDocumentId/readback availability |
| timeout/no response | `fiscal_issuance_unknown` |

Complete evidence means `fiscalIssuanceEvidenceStatus = fiscal_document_number_assigned`, `fiscalNumberAssignmentState = assigned`, and durable Central PMS fiscal reference recording.

## 20. Operator Console Queue Mapping

| State/reason | Queue category |
| --- | --- |
| `pending_fiscal_issuance`, stale `fiscal_issuance_requested` | pending fiscal issuance |
| `fiscal_issuance_failed_request` after correction possible | retry needed |
| `fiscal_issuance_failed_configuration` | configuration correction required |
| `fiscal_issuance_conflict` | idempotency conflict |
| `fiscal_issuance_unknown` | unknown outcome |
| `fiscal_number_assignment_incomplete` reason | incomplete numbering evidence |
| `replay_mismatch`, `fiscal_reference_mismatch`, readback mismatch | fiscal reference mismatch |
| `manual_release_requested_after_fiscal_failure` | manual release requested after fiscal failure |
| `fiscal_issuance_reconciled`, closed exception | reconciled/closed exceptions |

Operator Console remains review/governance only.

## 21. Management Dashboard Metric / State Mapping

| State | Dashboard metric group |
| --- | --- |
| `fiscal_issuance_recorded` | success |
| `fiscal_issuance_replayed` | replay |
| `fiscal_issuance_failed_request` | request failure |
| `fiscal_issuance_failed_configuration` | configuration failure |
| `fiscal_issuance_failed_service` | service failure |
| `fiscal_issuance_conflict` | conflict |
| `fiscal_issuance_unknown` | unknown |
| `fiscal_issuance_manual_review` | manual review |
| `fiscal_issuance_exception_released` | exception release |
| `fiscal_issuance_reconciled` | reconciled |
| `pending_fiscal_issuance`, `fiscal_issuance_requested` | pending |
| `not_required` | not required / excluded from fiscal issuance denominator unless policy includes |

Dashboard remains read-only visibility.

## 22. Audit / Event Mapping

Candidate event mappings:

| Transition / condition | Candidate event |
| --- | --- |
| request started | `FiscalIssuanceRequested` |
| complete newly-created evidence recorded | `FiscalIssuanceRecorded` |
| complete idempotent replay recorded/reconciled | `FiscalIssuanceReplayed` |
| idempotency conflict detected | `FiscalIssuanceConflictDetected` |
| request/data failure | `FiscalIssuanceFailedRequest` |
| fiscal configuration failure | `FiscalIssuanceFailedConfiguration` |
| service/persistence failure | `FiscalIssuanceFailedService` |
| timeout/disconnect/uncertain outcome | `FiscalIssuanceUnknownOutcome` |
| GET readback requested | `FiscalIssuanceReadbackRequested` |
| reconciliation closed | `FiscalIssuanceReconciled` |
| manual review required | `FiscalIssuanceManualReviewRequired` |
| ExitAuthorization blocked due to fiscal state | `ExitAuthorizationBlockedByFiscalState` |

Event names are candidate placeholders pending engineering conventions.

## 23. Test and UAT Planning Implications

Future tests should cover:

- every candidate state entry path.
- disallowed transitions.
- retry eligibility by state.
- replay from unknown and recorded states.
- conflict has no automatic retry.
- unknown outcome blocks normal ExitAuthorization.
- manual review blocks normal ExitAuthorization.
- exception release does not equal normal ExitAuthorization.
- successful replay does not create duplicate fiscal reference.
- successful replay does not create duplicate ExitAuthorization.
- queue mapping for each exception category.
- dashboard metric grouping for each state.
- audit/event mapping for key transitions.

## 24. Risks and Open Questions

- Final state names and enum values remain open.
- Final retry scheduler and lease/concurrency policy remain open.
- Final manual review authority and closure rules remain open.
- Final reconciliation closure labels remain open.
- Exact Central PMS repository conventions are not inspected in this documentation-only task.
- Operator Console queue implementation and Dashboard projection implementation are future tasks.
- Gating implementation requires a separate regression-focused slice.

## 25. Recommended First Implementation Branch

Recommended first implementation branch:

`feature/central-pms-fiscal-reference-state`

The first implementation slice should implement persistence/state only:

- no POS Server network calls.
- no retry scheduler.
- no Operator Console implementation.
- no Dashboard implementation.
- no ExitAuthorization gating enforcement.

## 26. Requirements Traceability Summary

| Requirement source | State/taxonomy coverage |
| --- | --- |
| POS Server API Contract | HTTP/code mapping, response classification, evidence status, assignment state, and error posture mapping. |
| Central PMS integration contract | Retry/replay/conflict handling, unknown outcome handling, and gating eligibility. |
| Central PMS implementation planning note | Candidate states, exception reasons, queue/readback/reconciliation behavior. |
| Engineering Pack Detail | State catalog, queue mapping, dashboard mapping, audit/event mapping, and test implications. |
| Database Delta Plan | Candidate state fields, exception fields, idempotency scope, and data readiness. |
| ExitPass v1.3 authority model | Central PMS remains payment/fiscal reference/ExitAuthorization authority; POS Server remains fiscal issuance authority only. |
