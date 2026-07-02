# ExitPass Central PMS Fiscal Reference State Implementation Slice Plan v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document title | ExitPass Central PMS Fiscal Reference State Implementation Slice Plan |
| Version | v1.0 |
| Product scope | ExitPass v1.3 Central PMS |
| Status | Documentation-only implementation-slice planning package |
| Target implementation branch | `feature/central-pms-fiscal-reference-state` |
| Repository | `D:\SourceCodes\ExitPass` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on branch `dev` |
| Output format | Markdown only |

This document prepares the first Central PMS fiscal issuance implementation slice. It is not source code, SQL, migration DDL, generated artifact, endpoint contract, or runtime implementation.

## 2. Purpose and Scope

The first implementation slice should add or confirm Central PMS persistence/state structures needed to store fiscal issuance references and fiscal issuance state.

This first slice is persistence/state only:

- no POS Server network calls
- no retry scheduler
- no Operator Console implementation
- no Management Dashboard implementation
- no ExitAuthorization gating enforcement
- no runtime fiscal issuance orchestration

The package provides a non-SQL implementation checklist for candidate persistence objects, candidate fields, state values, exception values, linkage points, uniqueness/idempotency rules, validation rules, tests, and rollout safety.

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
9. Central PMS Fiscal Issuance State Transition and Exception Taxonomy Plan.

Documents inspected:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Reference_and_Exception_State_Implementation_Planning_Note_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/01-database-state-delta-plan.md`
- `docs/v1.3/central-pms/engineering-pack/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Detail_v1.0.md`
- `docs/v1.3/central-pms/database-delta/ExitPass_Central_PMS_Fiscal_Reference_Persistence_Database_Delta_Plan_v1.0.md`
- `docs/v1.3/central-pms/state-taxonomy/ExitPass_Central_PMS_Fiscal_Issuance_State_Transition_and_Exception_Taxonomy_Plan_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Runtime reference documents inspected read-only:

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 4. Repository / Schema Inspection Summary

Repository inspection found:

- Central PMS implementation under `src/Services/CentralPms`.
- Layered projects for API, Application, Contracts, Domain, and Infrastructure.
- Central PMS test projects for Unit, Integration, and Contract tests.
- API endpoints under `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints`.
- Application payment handlers and gateway abstractions under `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Payments` and `Application/Abstractions/Persistence`.
- Infrastructure gateways using Npgsql and DB routines under `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure`.
- Domain enums and entities under `src/Services/CentralPms/src/ExitPass.CentralPms.Domain`.
- DB patch conventions under `infra/db/patches`.
- Existing validation patch conventions under `infra/db/patches/validation`.
- Existing v1.2 DDL baseline in `ExitPass_Full_Database_Creation_DDL_v1.2.sql`.

Relevant existing core database objects observed in the v1.2 DDL baseline:

- `core.payment_attempts`
- `core.payment_confirmations`
- `core.exit_authorizations`
- `core.parking_sessions`
- `core.tariff_snapshots`

Relevant existing enum/type posture in v1.2 DDL:

- `core.payment_attempt_status_enum`
- `core.payment_confirmation_status_enum`
- `core.exit_authorization_status_enum`
- `core.parking_session_status_enum`
- `core.tariff_snapshot_status_enum`

## 5. Current Central PMS Implementation Observations

Current implementation observations:

- Payment finality is recorded through `RecordPaymentConfirmationService` and `RecordPaymentConfirmationGateway`.
- `RecordPaymentConfirmationGateway` calls `core.record_payment_confirmation(...)` through Npgsql.
- Payment confirmation endpoint requires `X-Correlation-Id` and `Idempotency-Key` headers.
- Payment confirmation logic validates payable-basis consistency before recording provider evidence.
- ExitAuthorization is issued through `IssueExitAuthorizationHandler` and a DB-backed gateway.
- Current `IssueExitAuthorizationHandler` documents payment finality as the current gating invariant; future fiscal reference gating is not yet implemented.
- Existing domain enum classes use PascalCase values with numeric assignments, while database enum values use uppercase strings.
- Existing DB routines and integration tests use deterministic gateway tests against seeded database state.
- Existing event type names are centralized in `IntegrationEventTypes`.
- Existing business metrics are centralized in `CentralPmsMetrics`.
- No Central PMS fiscal issuance reference persistence object was found in source inspection.
- No Central PMS POS Server fiscal issuance network client was found, which matches this slice's non-goal.

## 6. Slice Objective

The first implementation slice should add or confirm Central PMS persistence/state structures needed to store fiscal issuance references and fiscal issuance state.

It must prepare durable storage and validation for fiscal reference data but must not call POS Server, enforce ExitAuthorization gating, or expose new Operator Console/Dashboard behavior.

## 7. Strict Non-Goals

This slice must not include:

- POS Server client implementation
- POS Server network calls
- request mapper implementation
- retry scheduler
- GET readback worker
- ExitAuthorization gating enforcement
- Operator Console queues
- Dashboard projections
- SQL DDL in this document
- source code changes in this documentation task
- migrations in this documentation task
- final endpoint OpenAPI specs
- final enum implementation declarations in this document

## 8. Authority Boundaries

The implementation must preserve:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- Operator Console remains governance/review only.
- Management Dashboard remains visibility/reporting only.

## 9. Candidate Persistence Objects to Implement Later

Candidate implementation objects:

1. Fiscal issuance reference
2. Fiscal issuance attempt/history
3. Fiscal issuance exception/review state
4. Fiscal readback/reconciliation record

Implementation options:

| Option | Description | Recommendation |
| --- | --- | --- |
| New dedicated persistence objects | Add separate fiscal reference, attempt/history, exception/review, and readback/reconciliation storage. | Safer for auditability and avoids overloading payment confirmation or exit authorization rows. |
| Extend existing `core.payment_confirmations` | Add fiscal reference/state fields directly to payment confirmation. | Lower object count, but risks mixing payment finality with fiscal issuance state. Not recommended as the primary approach. |
| Hybrid approach | Dedicated fiscal reference object linked to payment confirmation, with lightweight read-optimized fields later if needed. | Recommended if read performance or gating checks later require denormalized fields. |

Safer implementation path: dedicated fiscal reference persistence linked to payment confirmation, payment attempt, parking session, Site, and Site POS Server.

## 10. Candidate Fiscal Reference Fields

Candidate fields:

- payment confirmation id/ref
- payment attempt id/ref
- parking session id/ref
- Site id
- Site POS Server id
- payable basis ref
- upstream finality reference
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
- current fiscal issuance integration state
- latest exception reason
- latest error code
- latest `errorPosture`
- request/correlation id
- POS Server response timestamp
- first recorded at
- last updated at
- recorded by service ref

Implementation must keep these as normalized safe fields, not raw POS Server payload storage.

## 11. Candidate Attempt / History Fields

Candidate fields:

- attempt id
- fiscal issuance reference id or fiscal issuance context id
- attempt sequence number
- trigger source: automatic, operator-triggered, reconciliation-triggered
- action type: create, retry, replay, readback, reconciliation close, manual review escalation
- request correlation id
- upstream finality reference
- request semantic hash reference if available
- POS Server HTTP status
- POS Server response code
- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- fiscalDocumentId if returned
- error code
- `errorPosture`
- raw response retained? No, not by default; store normalized safe fields only
- attempted at
- completed at
- actor/service identity
- outcome classification
- notes/reference id if operator-triggered

## 12. Candidate Exception / Review Fields

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

## 13. Candidate Readback / Reconciliation Fields

Candidate fields:

- readback id
- fiscal document id used for readback
- readback requested at/completed at
- readback HTTP status/result code
- readback fiscal document number
- readback evidence status
- readback assignment state
- comparison result: matched, mismatched, inconclusive, not_found, service_failed
- mismatch reason
- reconciliation action
- reconciliation closure reference
- actor/service identity

This slice may define storage readiness for readback/reconciliation, but it must not implement GET readback behavior.

## 14. Candidate Fiscal Issuance State Values

Candidate state values:

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

Implementation note:

- C# enum values should likely follow existing PascalCase conventions if implemented in Domain/Application.
- Database values should follow existing controlled code/enum conventions after schema convention review.
- Final names are implementation decisions and must remain aligned with the taxonomy plan.

## 15. Candidate Exception Reason Values

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

## 16. Candidate Uniqueness / Idempotency Rules

Implementation checklist:

- Confirm upstream finality reference uniqueness within the fiscal issuance idempotency scope.
- Align scope with POS Server behavior: fiscal document creation operation + Site POS Server id + fiscal document type code id + upstream finality reference.
- Prevent duplicate active fiscal reference creation on replay.
- Ensure POS Server fiscal document id does not map to multiple active Central PMS fiscal references.
- Plan fiscal document number uniqueness within Site POS Server / fiscal identity / sequence policy context, subject to final business rules.
- Introduce uniqueness safeguards only after data profile review.
- Do not allow a new upstream finality reference to bypass a conflict without supervised correction policy.

## 17. Linkage Points to Existing Central PMS Objects

Candidate linkage points:

- `core.payment_confirmations.payment_confirmation_id`
- `core.payment_attempts.payment_attempt_id`
- `core.parking_sessions.parking_session_id`
- `core.tariff_snapshots.tariff_snapshot_id` or future payable basis reference
- `core.exit_authorizations.exit_authorization_id` for future linkage only
- Site identifier from existing Site/Site Group model after schema confirmation
- Site POS Server identifier after fiscal routing configuration exists
- payment channel or terminal context where available
- service identity references following existing created/updated by convention
- correlation id following existing payment/exit routines

## 18. Candidate Validation Rules

Planning validation rules:

- `fiscal_issuance_recorded` requires complete fiscal document identity, number, evidence status, assignment state, payment confirmation link, Site, and Site POS Server.
- `fiscal_issuance_replayed` requires complete fiscal evidence and replay reconciliation against existing fiscal context.
- incomplete evidence must not satisfy any gating-ready state.
- unknown, conflict, request failure, configuration failure, service failure, and manual review states must not be marked as gating eligible.
- exception states require exception reason.
- replay cannot duplicate active fiscal reference.
- attempt/history must not retain raw sensitive payloads.
- audit fields must be populated for created/updated records.
- state transition compatibility should be validated where implemented, even if full orchestration is deferred.

## 19. Candidate Test Checklist

Future tests should cover:

- create fiscal reference persistence object.
- create attempt/history record.
- create exception/review state.
- create readback/reconciliation record shell if included in slice.
- persist all candidate POS Server fiscal fields.
- persist state value.
- persist exception reason.
- duplicate upstream finality scope blocked or flagged.
- replay does not duplicate fiscal reference.
- POS Server fiscal document id cannot map to multiple active references.
- incomplete evidence remains not assigned / not gating ready.
- unknown outcome persisted.
- sensitive payload fields absent.
- audit fields populated.
- future ExitAuthorization gating data-readiness.
- repository/query by payment confirmation id.
- repository/query by upstream finality reference.
- repository/query by POS Server fiscal document id.

Do not write test code in this planning task.

## 20. Migration / DDL Implementation Checklist Without SQL

Implementation checklist:

- Confirm Central PMS database schema conventions before naming objects.
- Decide whether fiscal state values are database enum, reference data/code list, or text constrained by application and validation.
- Decide dedicated fiscal reference object versus extension of existing object; preferred path is dedicated object.
- Plan nullable/non-enforcing initial rollout.
- Plan attempt/history object after or with fiscal reference object.
- Plan exception/review state storage.
- Plan readback/reconciliation storage as shell only, without worker behavior.
- Plan validation script conventions under `infra/db/patches/validation`.
- Plan integration test seed/cleanup updates.
- Plan data-profile check before hard uniqueness constraints.
- Avoid writing SQL in this document.

## 21. Backfill / Data-Profile Checklist

Before implementation enforcement:

- Identify existing paid transactions with payment confirmation but no fiscal reference.
- Decide initial state for historical records: `not_required`, `pending_fiscal_issuance`, or migration-specific neutral state.
- Confirm all active Sites have Site POS Server mapping if enforcement will later apply.
- Confirm fiscal identity/policy/sequence readiness is not required for this first slice unless referenced as nullable future fields.
- Identify any historical fiscal references outside this integration.
- Confirm retention posture for attempt/history.
- Check duplicate provider/payment references that could affect fiscal idempotency planning.
- Confirm stable payment confirmation, payment attempt, parking session, and tariff snapshot identifiers.

## 22. Implementation Sequencing Checklist

Recommended sequence for real implementation:

1. Inspect current Central PMS schema conventions and naming standards.
2. Define candidate domain/application models for fiscal reference state if needed.
3. Add database delta in nullable/non-enforcing posture.
4. Add persistence model/gateway/repository shell.
5. Add write/read methods for state/reference only, without POS Server network calls.
6. Add validation tests for field persistence and sensitive data exclusion.
7. Add uniqueness/idempotency safeguards after data profile review.
8. Add integration tests against seeded DB state.
9. Add documentation update recording implementation reality.

## 23. Rollback / Safety Checklist

Rollback and safety planning:

- Keep initial objects nullable and non-enforcing where possible.
- Do not change existing payment confirmation or ExitAuthorization behavior in this slice.
- Do not alter existing DB routines for payment finality or ExitAuthorization unless explicitly required by later implementation.
- Do not block existing exit flows in this slice.
- Avoid deleting or backfilling irreversible data without reviewed migration plan.
- Ensure new persistence paths can be disabled or unused without affecting existing flows.
- Ensure no raw sensitive payload columns are introduced.

## 24. Risks and Blockers

- Central PMS schema conventions require deeper implementation-time inspection.
- Site POS Server identity/storage may not yet exist in Central PMS.
- Fiscal document type code id may not yet exist in Central PMS.
- Final database representation of candidate states is undecided.
- Existing payment/exit routines may later need changes, but not in this slice.
- Uniqueness constraints should wait for data profile review.
- Future gating implementation must not be started in this slice.

## 25. Recommended Implementation Branch

`feature/central-pms-fiscal-reference-state`

## 26. Recommended First Codex Implementation Task

Implement Central PMS fiscal reference state persistence scaffolding only: inspect schema conventions, create the database delta and validation plan, add candidate persistence models/repository or gateway interfaces, and add tests for storing fiscal reference state without POS Server network calls or ExitAuthorization gating enforcement.

## 27. Requirements Traceability Summary

| Source | Slice planning coverage |
| --- | --- |
| POS Server API Contract | Candidate fields for fiscal document id, numbering, evidence status, assignment state, result classification, and error posture. |
| Central PMS to POS Server Integration Contract | Idempotency scope, fiscal reference recording, replay/conflict handling, and safe normalized persistence. |
| Database Delta Plan | Candidate objects, fields, linkage, uniqueness/idempotency, migration sequencing, and data-profile needs. |
| State Taxonomy Plan | Candidate state values, exception reason values, gating readiness, retry/replay/unknown posture. |
| Current repository inspection | Existing Central PMS layered architecture, DB routine gateway pattern, enum conventions, test project structure, and core payment/exit database objects. |
| ExitPass authority model | Central PMS remains payment finality, fiscal reference recording, and ExitAuthorization authority; POS Server remains fiscal issuance authority only. |
