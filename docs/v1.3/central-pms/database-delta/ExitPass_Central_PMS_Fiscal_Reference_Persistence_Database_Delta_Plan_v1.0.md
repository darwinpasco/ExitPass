# ExitPass Central PMS Fiscal Reference Persistence Database Delta Plan v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass Central PMS Fiscal Reference Persistence Database Delta Plan |
| Version | v1.0 |
| Product scope | ExitPass v1.3 Central PMS |
| Status | Documentation-only database delta planning |
| Target first implementation slice | `feature/central-pms-fiscal-reference-state` |
| Repository | `D:\SourceCodes\ExitPass` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |
| Output format | Markdown only |

This document is a database delta planning document only. It does not write SQL DDL, create migrations, modify schema, modify source code, generate artifacts, create endpoint contracts, or implement runtime behavior.

## 2. Purpose and Scope

This plan converts the approved Central PMS fiscal issuance planning chain into a detailed persistence/state plan for the first Central PMS implementation slice.

The first slice is persistence/state only. It should prepare Central PMS to durably store POS Server fiscal issuance evidence, fiscal issuance state, exception state, attempt history, readback/reconciliation data, audit/correlation fields, and future read/query needs.

This plan does not include:

- POS Server network calls.
- orchestration service behavior beyond state-readiness planning.
- ExitAuthorization gating implementation.
- Operator Console implementation.
- Management Dashboard implementation.
- SQL.

## 3. Source Documentation Baseline

Source-of-truth chain:

1. POS Server runtime numbered fiscal issuance.
2. POS Server API Contract.
3. POS Server response/status contract update.
4. Central PMS to POS Server Fiscal Issuance Integration Contract.
5. Central PMS fiscal issuance persistence/exception-state planning note.
6. Central PMS Fiscal Issuance Engineering Pack Outline.
7. Central PMS Fiscal Issuance Engineering Pack Detail.

Documents inspected:

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
- `docs/v1.3/central-pms/engineering-pack/01-database-state-delta-plan.md`
- `docs/v1.3/central-pms/engineering-pack/02-orchestration-service-plan.md`
- `docs/v1.3/central-pms/engineering-pack/04-success-replay-recording-plan.md`
- `docs/v1.3/central-pms/engineering-pack/05-failure-errorposture-plan.md`
- `docs/v1.3/central-pms/engineering-pack/06-unknown-outcome-readback-plan.md`
- `docs/v1.3/central-pms/engineering-pack/07-exitauthorization-gating-plan.md`
- `docs/v1.3/central-pms/engineering-pack/10-audit-events-correlation-plan.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
- `docs/v1.3/central-pms/engineering-pack/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Detail_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Detail_v1.0_Review.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Runtime reference documents inspected read-only:

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 4. Current Runtime / API Assumptions

The current POS Server runtime/API baseline for this database planning task:

- `POST /v1/fiscal-documents/` exists.
- `GET /v1/fiscal-documents/{fiscalDocumentId}` exists.
- POS Server fiscal document creation uses `payableBasis.upstreamFinalityRef` as the current idempotency key.
- POS Server computes semantic request hash server-side.
- POS Server resolves fiscal identity server-side.
- POS Server resolves fiscal sequence policy server-side.
- POS Server locks selected fiscal sequence state and allocates fiscal document number transactionally.
- POS Server returns fiscal identity and fiscal numbering fields after durable commit.
- POS Server returns `resultClassification`, `fiscalIssuanceEvidenceStatus`, `fiscalNumberAssignmentState`, and `fiscalDocumentStatusCodeId`.
- Duplicate same-key/same-hash requests return `resultClassification = idempotent_replay`.
- Same-key/different-hash requests fail closed as idempotency conflict.
- Missing complete fiscal numbering evidence fails closed with `fiscal_number_assignment_incomplete`.

## 5. Database Delta Objectives

The future Central PMS database delta must enable Central PMS to:

- durably store POS Server fiscal issuance reference evidence.
- track current fiscal issuance integration state.
- track retry, replay, and conflict attempt history.
- track exception/review state and reasons.
- track unknown outcome and GET readback results.
- support future ExitAuthorization gating.
- support future Operator Console fiscal exception queues.
- support future Management Dashboard fiscal visibility.
- support audit and reconciliation.

## 6. Non-Goals

This plan does not:

- write SQL.
- create migrations.
- define final table names, column names, indexes, constraints, triggers, stored routines, or enums.
- modify Central PMS source code.
- make POS Server network calls.
- implement fiscal issuance orchestration.
- implement ExitAuthorization gating.
- implement Operator Console queues.
- implement Management Dashboard projections.
- store raw sensitive provider or evidence payloads.
- modify POS Server runtime behavior.
- modify DOCX files or generated artifacts.

## 7. Authority Boundaries

The future persistence model must preserve:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console supports review/governance only.
- Management Dashboard is visibility/reporting only.

Database storage must not convert POS Server fiscal evidence into payment finality or gate authority.

## 8. Candidate Storage Object Overview

Candidate objects for later database implementation confirmation:

1. Fiscal issuance reference
2. Fiscal issuance attempt/history
3. Fiscal issuance exception/review state
4. Fiscal readback/reconciliation record
5. Projection/source extension fields if needed

These are candidate object names only. Final names must follow Central PMS repository conventions and be confirmed during the later database delta implementation.

## 9. Candidate Object: Fiscal Issuance Reference

Purpose:

- Store one durable fiscal reference per successful Central PMS fiscal issuance context.
- Prevent duplicate Central PMS fiscal reference records when POS Server returns idempotent replay.
- Provide data needed for future ExitAuthorization gating, review, reporting, audit, and reconciliation.

Candidate fields:

| Candidate field | Planning purpose |
| --- | --- |
| fiscal issuance reference id | Central PMS internal identifier for the fiscal reference record. |
| payment confirmation id/ref | Links to Central PMS payment finality. |
| payment attempt id/ref | Links to payment attempt history. |
| parking session id/ref | Links to parking session/control context. |
| Site id | Site fiscal/reporting attribution. |
| Site POS Server id | Resolved fiscal issuance route. |
| payable basis ref | Links to approved payable basis / TariffSnapshot equivalent. |
| upstream finality reference | POS Server idempotency key source. |
| POS Server fiscal document id | POS Server fiscal document identity. |
| fiscal identity id | POS Server-resolved fiscal identity. |
| fiscal sequence policy id | POS Server-resolved sequence policy. |
| fiscal sequence value | Assigned fiscal sequence value. |
| fiscal document number | Assigned Sales Invoice/fiscal document number. |
| fiscal series | Fiscal series at assignment time. |
| fiscal number prefix text | Prefix at assignment time. |
| fiscal number suffix text | Suffix at assignment time. |
| fiscal number assigned at | Assignment timestamp from POS Server. |
| fiscal number assigned by ref | Assignment actor/reference from POS Server. |
| fiscal document status code id | POS Server persisted fiscal document status code id. |
| result classification | `newly_created` or `idempotent_replay`. |
| fiscal issuance evidence status | Expected complete value: `fiscal_document_number_assigned`. |
| fiscal number assignment state | Expected complete value: `assigned`. |
| current fiscal issuance integration state | Central PMS current fiscal state. |
| latest exception reason | Latest normalized exception reason, if any. |
| latest error code | Latest normalized POS Server or Central PMS error code. |
| latest errorPosture | Latest POS Server retry guidance, if present. |
| request/correlation id | Cross-service traceability. |
| POS Server response timestamp | Response timing. |
| first recorded at | First Central PMS durable record timestamp. |
| last updated at | Last state/reference update timestamp. |
| recorded by service ref | Central PMS service identity. |
| is active / superseded / reconciled indicators | Optional lifecycle flags if repository conventions require them. |

All fields are candidate fields. Final naming, type, nullability, and constraints remain deferred.

## 10. Candidate Object: Fiscal Issuance Attempt / History

Purpose:

- Preserve every fiscal issuance create, retry, replay, readback, reconciliation close, and manual review escalation action.
- Make replay and conflict auditable without creating duplicate fiscal references.

Candidate fields:

| Candidate field | Planning purpose |
| --- | --- |
| attempt id | Internal attempt identifier. |
| fiscal issuance reference id or fiscal issuance context id | Links attempt to reference or pre-reference context. |
| attempt sequence number | Ordering within fiscal issuance context. |
| trigger source | `automatic`, `operator-triggered`, or `reconciliation-triggered`. |
| action type | `create`, `retry`, `replay`, `readback`, `reconciliation close`, `manual review escalation`. |
| request correlation id | Request traceability. |
| upstream finality reference | Idempotency key source used for the attempt. |
| request semantic hash reference if available | Safe reference to semantic hash, if exposed or computed by Central PMS. |
| POS Server HTTP status | Normalized HTTP status. |
| POS Server response code | Normalized response code. |
| resultClassification | `newly_created`, `idempotent_replay`, or absent on failure. |
| fiscalIssuanceEvidenceStatus | Evidence status if returned. |
| fiscalNumberAssignmentState | Assignment state if returned. |
| fiscalDocumentId if returned | POS Server fiscal document id when available. |
| error code | Normalized error code. |
| errorPosture | POS Server retry guidance where provided. |
| raw response retained? | No by default; store normalized safe fields only. |
| attempted at | Start timestamp. |
| completed at | Completion timestamp. |
| actor/service identity | Service or approved operator actor. |
| outcome classification | success, replay, conflict, request failure, configuration failure, service failure, unknown, or readback result. |
| notes/reference id if operator-triggered | Safe note or reference id, not raw sensitive data. |

## 11. Candidate Object: Fiscal Issuance Exception / Review State

Purpose:

- Track the current review and exception posture for unresolved fiscal issuance cases.
- Feed future Operator Console queues and reconciliation workflows.

Candidate fields:

- current exception state
- exception reason code
- exception category
- review status
- assigned reviewer
- supervisor escalation flag
- manual release requested flag
- manual release reference id if applicable
- incident reference
- reconciliation status
- reconciliation closed at/by
- latest readback status
- latest mismatch reason
- customer-impacting flag if needed

The exception/review state may be stored as a separate candidate object or as an extension to the fiscal issuance reference depending on Central PMS conventions.

## 12. Candidate Object: Fiscal Readback / Reconciliation Record

Purpose:

- Preserve GET readback attempts, readback outcomes, comparison results, mismatch handling, and reconciliation closure.

Candidate fields:

| Candidate field | Planning purpose |
| --- | --- |
| readback id | Internal readback identifier. |
| fiscal document id used for readback | POS Server fiscal document id used for GET. |
| readback requested at/completed at | Timing. |
| readback HTTP status/result code | Normalized readback result. |
| readback fiscal document number | Returned fiscal document number, if found. |
| readback evidence status | Returned evidence status. |
| readback assignment state | Returned assignment state. |
| comparison result | `matched`, `mismatched`, `inconclusive`, `not_found`, or `service_failed`. |
| mismatch reason | Normalized mismatch reason. |
| reconciliation action | Follow-up action or closure type. |
| reconciliation closure reference | Closure record/reference. |
| actor/service identity | Service or approved reviewer. |

## 13. Field Mapping from POS Server Response to Central PMS Persistence

Candidate mapping:

| POS Server field | Central PMS candidate field |
| --- | --- |
| `fiscalDocumentId` | POS Server fiscal document id |
| `fiscalIdentityId` | fiscal identity id |
| `fiscalSequencePolicyId` | fiscal sequence policy id |
| `fiscalSequenceValue` | fiscal sequence value |
| `fiscalDocumentNumber` | fiscal document number |
| `fiscalSeries` | fiscal series |
| `fiscalNumberPrefixText` | fiscal number prefix text |
| `fiscalNumberSuffixText` | fiscal number suffix text |
| `fiscalNumberAssignedAt` | fiscal number assigned at |
| `fiscalNumberAssignedByRef` | fiscal number assigned by ref |
| `fiscalDocumentStatusCodeId` | fiscal document status code id |
| `resultClassification` | result classification |
| `fiscalIssuanceEvidenceStatus` | fiscal issuance evidence status |
| `fiscalNumberAssignmentState` | fiscal number assignment state |
| `errorPosture` | latest errorPosture and attempt error posture |
| `code` | latest response code / attempt response code |
| HTTP status | latest HTTP status / attempt HTTP status |
| `message`, if safe and normalized | normalized safe message or diagnostic note |

Central PMS should not require storing full raw POS Server request or response payloads. Store normalized safe fields sufficient for audit, reconciliation, retry decisions, and support diagnostics.

## 14. Linkage Model to Payment Confirmation, Payment Attempt, Parking Session, Site, and Site POS Server

Fiscal issuance persistence should link to:

- payment confirmation as the payment finality anchor.
- payment attempt for payment history traceability.
- parking session for operational control state.
- payable basis / TariffSnapshot equivalent for fiscal facts used at time of issuance.
- discount validation reference where applicable.
- Site as reporting/fiscal attribution boundary.
- Site POS Server as fiscal issuance route.
- channel/terminal context where relevant, including WebPay, APM, Cashier-Assisted Terminal, or Continuity Terminal.
- future ExitAuthorization decision record.
- future manual release exception record if normal ExitAuthorization remains blocked.

Site Group may be useful for reporting/governance, but it is not fiscal issuance authority.

## 15. Candidate Fiscal Issuance State Model

Candidate states pending engineering/database confirmation:

| Candidate state | Planning meaning | Normal ExitAuthorization gating eligibility |
| --- | --- | --- |
| `not_required` | Fiscal issuance is not required under approved policy. | Eligible only if policy explicitly permits no fiscal issuance. |
| `pending_fiscal_issuance` | Payment/fiscal flow is waiting for fiscal issuance initiation. | Not eligible. |
| `fiscal_issuance_requested` | Central PMS has requested or is requesting POS Server fiscal issuance. | Not eligible. |
| `fiscal_issuance_recorded` | Complete POS Server fiscal evidence is durably recorded by Central PMS. | Eligible if all other exit conditions pass. |
| `fiscal_issuance_replayed` | Idempotent replay returned original complete fiscal evidence and Central PMS reconciled/recorded it. | Eligible only if complete evidence is durably recorded and no mismatch exists. |
| `fiscal_issuance_conflict` | Same idempotency key with different semantic request facts. | Not eligible. |
| `fiscal_issuance_failed_request` | Request construction or semantic validation failed. | Not eligible. |
| `fiscal_issuance_failed_configuration` | Fiscal identity/policy/state/configuration requires correction. | Not eligible. |
| `fiscal_issuance_failed_service` | Persistence/service recovery is required. | Not eligible. |
| `fiscal_issuance_unknown` | Outcome is unknown or readback is inconclusive. | Not eligible. |
| `fiscal_issuance_manual_review` | Case is under review. | Not eligible for normal ExitAuthorization. |
| `fiscal_issuance_exception_released` | Approved exception/manual release occurred. | Not normal ExitAuthorization; governed exception only. |
| `fiscal_issuance_reconciled` | Exception or unknown case was reconciled/closed. | Eligible only if reconciled state includes complete fiscal evidence and policy permits. |

Only `fiscal_issuance_recorded` and reconciled successful replay states can satisfy normal gating, and only when fiscal evidence fields are complete and durably recorded.

## 16. Candidate Exception Reason Taxonomy

Candidate reason buckets:

- idempotency conflict
- request construction error
- missing payable basis
- missing upstream finality reference
- unapproved discount reference
- sensitive payload rejected
- invalid fiscal request facts
- fiscal identity not found
- fiscal identity ambiguous
- fiscal identity not effective
- fiscal sequence policy not found
- fiscal sequence policy ambiguous
- fiscal sequence policy not effective
- fiscal sequence state not found
- fiscal sequence state not effective
- fiscal number allocation failed
- fiscal document number format failed
- persistence not configured
- invalid persistence configuration
- persistence write failed
- fiscal number assignment incomplete
- POST timeout
- network disconnect after possible commit
- GET readback not found
- GET readback service failed
- GET readback mismatch
- Central PMS fiscal reference persistence failed
- manual release requested after fiscal failure

Final reason names, codes, severity, retry eligibility, and queue labels remain deferred to the next state transition and exception taxonomy plan.

## 17. Uniqueness and Idempotency Considerations

Candidate uniqueness rules:

- upstream finality reference should be unique within the fiscal issuance idempotency scope that matches POS Server behavior.
- candidate scope includes fiscal document creation operation + Site POS Server id + fiscal document type code id + upstream finality reference.
- successful fiscal reference should not duplicate for replay.
- POS Server fiscal document id should not map to multiple active Central PMS fiscal references.
- fiscal document number should not duplicate within the same Site POS Server / fiscal identity / sequence policy context, subject to final business rules.
- uniqueness constraints should be introduced after existing data profile review.
- no new upstream finality reference should be used to bypass conflict without supervised correction policy.

Candidate uniqueness rules should be introduced in a cautious sequence so legacy data can be profiled before enforcement.

## 18. Retry / Replay / Conflict History Persistence

The database delta should support persistence of:

- every retry attempt using the same upstream finality reference.
- every idempotent replay response and whether it matched existing Central PMS reference.
- every idempotency conflict and associated safe normalized request/context references.
- retry eligibility or retry blocked reason.
- operator-triggered retry or review request where allowed.
- conflict escalation and closure status.

Replay must be visible in history without creating a duplicate active fiscal reference.

## 19. Unknown Outcome / Readback Persistence

The database delta should support:

- unknown outcome reason.
- whether POS Server fiscal document id was available.
- whether GET readback was attempted.
- readback timestamp and result.
- readback fiscal document id and number.
- evidence and assignment state returned by readback.
- comparison result.
- mismatch reason.
- reconciliation closure status and actor.

Unknown outcome must remain non-eligible for normal ExitAuthorization until complete fiscal evidence is recorded and reconciled.

## 20. Audit Fields and Correlation Fields

Candidate audit/correlation fields:

- created at / created by service ref
- updated at / updated by service ref
- first recorded at
- last retry at
- last readback at
- last exception state changed at/by
- manual review actor and decision reference
- reconciliation actor and closure timestamp
- incident reference
- request correlation id
- payment confirmation ref
- upstream finality reference
- POS Server fiscal document id
- Site and Site POS Server

Audit data must support traceability from payment finality to fiscal reference recording to future ExitAuthorization decision.

## 21. Data Retention and Archival Posture

Fiscal issuance references and attempt history should be retained according to fiscal, audit, and reconciliation policy. Soft-delete is not recommended for active fiscal evidence because it can hide audit trail. If lifecycle flags are needed, use active/superseded/reconciled indicators that preserve queryability and audit history.

Retention periods remain open for compliance, finance/accounting, and operations confirmation.

## 22. Migration Sequencing Plan Without SQL

Recommended sequence for the later implementation:

1. Add candidate fiscal reference/state storage in nullable/non-enforcing posture.
2. Add attempt/history and exception state storage.
3. Add read/query surfaces for diagnostics.
4. Backfill neutral state for existing transactions.
5. Add uniqueness/idempotency safeguards after data profile review.
6. Add write paths in service layer.
7. Add Operator Console/Dashboard projection sources.
8. Enable fiscal-before-ExitAuthorization gating behind feature flag.
9. Harden constraints after production observation and reconciliation readiness.

No SQL is included in this plan.

## 23. Backfill and Existing-Data Questions

Open backfill questions:

- How should existing paid transactions be classified before POS Server fiscal integration?
- Should existing records be `not_required`, `pending_fiscal_issuance`, or a migration-specific state?
- Are all Sites mapped to Site POS Server?
- Are fiscal identity, fiscal sequence policy, and fiscal sequence state configured for all Sites before enforcement?
- What historical fiscal references, if any, exist outside this integration?
- What retention rules apply to attempt history?
- Which existing Central PMS identifiers are stable enough for fiscal reference linkage?
- Is payment confirmation reference globally stable across retry/replay workflows?

## 24. Read / Query Planning for Later Services

Future services will need read/query support for:

- fiscal issuance state by payment confirmation.
- fiscal reference by upstream finality reference.
- fiscal reference by POS Server fiscal document id.
- fiscal exceptions by state, reason, Site, and Site POS Server.
- retry candidates.
- unknown outcome candidates.
- readback/reconciliation candidates.
- Operator Console queues.
- Management Dashboard projections.

Query design must preserve Site/Site POS Server access scoping and auditability.

## 25. ExitAuthorization Gating Data Readiness

The database delta should prepare data needed for future gating:

- payment finality linkage.
- complete POS Server fiscal evidence fields.
- fiscal assignment state.
- current fiscal issuance integration state.
- Central PMS durable recorded timestamp.
- exception/manual release indicators.
- reconciliation state.

Gating implementation is not part of the first persistence/state slice.

## 26. Operator Console Queue Data Readiness

The database delta should prepare data for future queues:

- pending fiscal issuance.
- retry needed.
- configuration correction required.
- idempotency conflict.
- unknown outcome.
- incomplete numbering evidence.
- fiscal reference mismatch.
- manual release requested after fiscal failure.
- reconciled/closed exceptions.

Operator Console remains governance/review only and must not mutate fiscal authority records as fiscal issuer.

## 27. Management Dashboard Projection Data Readiness

The database delta should prepare data for future reporting:

- fiscal issuance success count/rate.
- failures by category.
- replay count.
- conflict count.
- unknown outcome count.
- pending exception count.
- manual release count tied to fiscal issuance exception.
- time from payment finality to fiscal reference recording.
- Site/Site POS Server breakdown.
- open exception age.
- retry and reconciliation backlog.

Management Dashboard remains read-only visibility.

## 28. Security / Privacy Considerations

Persistence must exclude:

- raw provider callbacks.
- card PAN/CVV.
- secrets.
- tokens.
- credentials.
- raw statutory entitlement evidence.
- unmanaged evidence images.
- uncontrolled raw payment payloads.

Store normalized safe fields only. Access should be scoped by Site/Site POS Server and role. Audit access must be controlled. Operator notes require privacy review and should not contain sensitive raw evidence.

## 29. Test / Validation Planning for Future Database Delta

Future validation should include:

- schema inventory.
- constraint/index review.
- candidate state transition tests.
- idempotency uniqueness tests.
- replay duplicate-prevention tests.
- unknown outcome/readback persistence tests.
- fiscal reference mismatch persistence tests.
- ExitAuthorization gating data-readiness tests.
- Operator Console queue query readiness tests.
- Management Dashboard projection query readiness tests.
- sensitive data exclusion checks.

## 30. Risks and Open Questions

- Central PMS schema conventions are not inspected in this documentation-only task.
- Final object/table names and field names remain open.
- Final uniqueness scope must match POS Server idempotency behavior.
- Backfill posture for existing paid transactions is unresolved.
- Site-to-Site POS Server mapping completeness must be confirmed before enforcement.
- Retention and archival policy require compliance and operations confirmation.
- Gating implementation requires separate regression work.
- Operator Console and Management Dashboard read APIs are not yet defined.

## 31. Recommended First Implementation Slice

Recommended first implementation branch:

`feature/central-pms-fiscal-reference-state`

The first implementation slice should implement the persistence/state model only, with:

- no POS Server network calls.
- no orchestration network behavior.
- no ExitAuthorization gating enforcement.
- no Operator Console implementation.
- no Management Dashboard implementation.

## 32. Requirements Traceability Summary

| Requirement source | Database delta planning coverage |
| --- | --- |
| POS Server API Contract | Field mapping, idempotency key, response/status evidence fields, and error posture storage. |
| Central PMS to POS Server Integration Contract | Fiscal reference recording, retry/replay/conflict persistence, GET readback, and gating data readiness. |
| Central PMS Planning Note | Candidate states, exception reasons, queues, dashboard visibility, audit/correlation, and unknown outcome handling. |
| Engineering Pack Detail Plan 01 | Candidate storage objects, fields, linkage, uniqueness, migration sequencing, and open questions. |
| ExitPass v1.3 authority model | Central PMS fiscal reference recording is preserved without making POS Server payment or exit authority. |
