# ExitPass Central PMS Fiscal Issuance Engineering Pack Outline v1.0 Review

## 1. Review Summary

This review covers the creation of `ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0.md`.

The outline translates the approved v1.3 fiscal issuance documentation chain into future Central PMS implementation slices. It remains documentation-only and does not implement source code, SQL, migrations, generated artifacts, endpoint specs, or runtime behavior.

## 2. Repositories Inspected

| Repository | Purpose | Branch inspected |
| --- | --- | --- |
| `D:\SourceCodes\ExitPass` | Documentation repository modified by this task. | `docs/v1.3-central-pms-fiscal-issuance-engineering-pack-outline` |
| `D:\SourceCodes\ExitPass-PoSServer` | POS Server runtime repository inspected read-only. | `dev` |

## 3. Documentation References Inspected

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

## 4. Runtime References Inspected

- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## 5. Files Created

- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0.md`
- `docs/v1.3/central-pms/ExitPass_Central_PMS_Fiscal_Issuance_Engineering_Pack_Outline_v1.0_Review.md`

## 6. Engineering Objectives Covered

The outline states that Central PMS implementation must enable:

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

## 7. Implementation Slice Roadmap Summary

The outline defines eleven implementation slices:

1. Central PMS fiscal reference persistence model.
2. Fiscal issuance orchestration service shell.
3. POS Server client and request mapper.
4. Successful issuance and replay handling.
5. Conflict/failure/errorPosture handling.
6. Unknown outcome and GET readback reconciliation.
7. ExitAuthorization gating update.
8. Operator Console fiscal exception queues.
9. Management Dashboard fiscal visibility projections.
10. Audit/events/correlation hardening.
11. Integration tests and UAT evidence.

The sequence starts with persistence/state only and avoids POS Server network calls in the first implementation branch.

## 8. Database/State Planning Summary

The outline carries forward candidate fiscal issuance states:

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

It also identifies future persistence needs for fiscal references, current state, attempt/history, exception reason, retry/replay/conflict tracking, readback/reconciliation results, review assignment/status, and dashboard projection source.

## 9. API/Service Planning Summary

The outline identifies future Central PMS service/API needs:

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

No endpoint paths, DTOs, or service classes are finalized.

## 10. Event/Job/Queue Planning Summary

The outline identifies candidate jobs/queues:

- fiscal issuance retry job
- fiscal issuance readback job
- fiscal issuance reconciliation job
- Operator Console fiscal exception queue projection
- Management Dashboard fiscal visibility projection
- stale exception escalation job

It also lists candidate events such as `FiscalIssuanceRequested`, `FiscalIssuanceRecorded`, `FiscalIssuanceReplayed`, `FiscalIssuanceConflictDetected`, and related failure/readback/reconciliation events. These are explicitly marked as placeholder names pending engineering conventions.

## 11. Operator Console Planning Summary

The outline plans Operator Console support for:

- fiscal exception queue API
- queue categories and filters
- role-based queue access
- review notes and reason capture
- supervisor escalation path
- manual release request visibility
- reconciliation close visibility
- audit log for review actions

Operator Console remains review/governance only.

## 12. Management Dashboard Planning Summary

The outline plans Management Dashboard support for:

- fiscal issuance health metrics
- replay/conflict/unknown outcome metrics
- pending exception metrics
- manual release count tied to fiscal issuance exception
- Site/Site POS Server breakdown
- average payment-finality-to-fiscal-reference time
- dashboard freshness labels
- source-of-truth labels
- export and access audit rules

Management Dashboard remains read-only visibility/reporting.

## 13. Security/Access Control Planning Summary

The outline includes:

- only Central PMS service identity may call POS Server fiscal issuance path
- service-to-service auth/mTLS/token model remains open
- Operator Console role-based fiscal exception review
- Management Dashboard read-only visibility
- logs must exclude secrets, PAN/CVV, tokens, raw provider callbacks, and raw entitlement evidence
- audit records must be access-controlled
- retry/manual review actions must be attributable
- fiscal exception access must be scoped by Site/Site POS Server where applicable

## 14. Rollout/Feature Flag Planning Summary

The outline includes:

- feature flag for fiscal-before-ExitAuthorization enforcement
- environment/site-level rollout
- shadow-readiness mode if needed
- dry-run restriction against issuing fiscal numbers unless wired to non-production/test POS Server policy
- production prerequisites for configured Site POS Server/fiscal identity/sequence policy/sequence state
- rollback protections for payment finality records and fiscal references
- approved manual exception procedure before production enforcement
- Operator Console and Dashboard readiness checks

## 15. Authority Boundaries Preserved

The outline preserves:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console supports review/governance only and must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or open gates.
- Management Dashboard is visibility/reporting only.

## 16. Non-Goals Preserved

The outline explicitly excludes:

- POS Server runtime changes
- Digital SI
- printable SI
- QR presentation
- X-read/Z-read
- BIR Sales Summary/Annex E
- EJ/POSLog
- reprints
- adjustments
- reset/Z-counter/GTA
- recovery automation
- gate opening from POS Server
- Operator Console as payment/fiscal authority
- Management Dashboard as operational authority
- SQL DDL
- source code
- endpoint OpenAPI specs

## 17. Recommended First Implementation Branch

Recommended first implementation branch:

`feature/central-pms-fiscal-reference-state`

Alternative if numbered task branch names are required:

`feature/<task-number>-central-pms-fiscal-reference-state`

The first implementation slice should be persistence/state only, with no POS Server network calls yet.

## 18. Issues or Mismatches

No blockers or source contradictions were found for this outline scope.

No source code, SQL, migrations, generated artifacts, DOCX files, or runtime repository files were modified.

## 19. Recommended Next Task

Recommended next task:

> Draft the detailed Central PMS Fiscal Reference Persistence Database Delta Plan, still documentation-only, defining candidate storage objects, field mapping, uniqueness/idempotency considerations, audit fields, and migration sequencing without writing SQL.
