# ExitPass Central PMS Fiscal Reference Persistence Database Delta Plan v1.0 Review

## Review Summary

The Central PMS Fiscal Reference Persistence Database Delta Plan v1.0 was created as a documentation-only planning artifact for the first implementation slice, `feature/central-pms-fiscal-reference-state`.

The plan defines candidate Central PMS storage objects, candidate fields, POS Server response field mapping, linkage, uniqueness/idempotency considerations, state planning, exception taxonomy, migration sequencing, and future readiness for ExitAuthorization gating, Operator Console queues, and Management Dashboard projections. It does not write SQL, create migrations, modify schema, or implement runtime behavior.

## Branch Name

`docs/v1.3-central-pms-fiscal-reference-db-delta-plan`

## Files Created

- `docs/v1.3/central-pms/database-delta/ExitPass_Central_PMS_Fiscal_Reference_Persistence_Database_Delta_Plan_v1.0.md`
- `docs/v1.3/central-pms/database-delta/ExitPass_Central_PMS_Fiscal_Reference_Persistence_Database_Delta_Plan_v1.0_Review.md`

## Runtime Repo Inspected

Runtime repository inspected read-only:

`D:\SourceCodes\ExitPass-PoSServer`

Runtime branch:

`dev`

Runtime reference documents inspected:

- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `docs/v1.3/runtime/ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

The runtime repository was not modified.

## Docs Inspected

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

## Candidate Storage Objects Summary

Candidate objects defined:

- Fiscal issuance reference
- Fiscal issuance attempt/history
- Fiscal issuance exception/review state
- Fiscal readback/reconciliation record
- Optional projection/source extension fields

Final table names, columns, keys, indexes, constraints, and migrations remain deferred.

## Field Mapping Summary

The plan maps POS Server response fields into Central PMS candidate persistence fields, including:

- `fiscalDocumentId`
- `fiscalIdentityId`
- `fiscalSequencePolicyId`
- `fiscalSequenceValue`
- `fiscalDocumentNumber`
- `fiscalSeries`
- `fiscalNumberPrefixText`
- `fiscalNumberSuffixText`
- `fiscalNumberAssignedAt`
- `fiscalNumberAssignedByRef`
- `fiscalDocumentStatusCodeId`
- `resultClassification`
- `fiscalIssuanceEvidenceStatus`
- `fiscalNumberAssignmentState`
- `errorPosture`
- `code`
- HTTP status
- safe normalized `message` where appropriate

The plan explicitly avoids requiring full raw payload storage.

## Uniqueness / Idempotency Planning Summary

Candidate uniqueness/idempotency posture:

- upstream finality reference should be unique within the POS Server-compatible fiscal issuance idempotency scope.
- candidate scope includes fiscal document creation operation + Site POS Server id + fiscal document type code id + upstream finality reference.
- replay should not create duplicate active fiscal reference records.
- POS Server fiscal document id should not map to multiple active Central PMS fiscal references.
- fiscal document number should not duplicate within the same Site POS Server/fiscal identity/sequence policy context, subject to final rules.
- uniqueness safeguards should be introduced after data profile review.
- no new upstream finality reference should bypass conflict without supervised correction policy.

## State Model Planning Summary

Candidate states included:

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

The plan states that only recorded fiscal evidence and reconciled successful replay states can satisfy normal ExitAuthorization gating, and only when evidence is complete and durably recorded.

## Exception Taxonomy Summary

The exception taxonomy includes request, configuration, service, idempotency, timeout, readback, mismatch, persistence, and manual release request categories. It includes reason buckets such as missing payable basis, missing upstream finality reference, unapproved discount reference, fiscal identity/policy/state failures, fiscal number assignment incomplete, POST timeout, GET readback mismatch, and Central PMS fiscal reference persistence failure.

## Migration Sequencing Summary

Recommended sequence:

1. Add candidate fiscal reference/state storage in nullable/non-enforcing posture.
2. Add attempt/history and exception state storage.
3. Add read/query surfaces for diagnostics.
4. Backfill neutral state for existing transactions.
5. Add uniqueness/idempotency safeguards after data profile review.
6. Add write paths in service layer.
7. Add Operator Console/Dashboard projection sources.
8. Enable fiscal-before-ExitAuthorization gating behind feature flag.
9. Harden constraints after production observation and reconciliation readiness.

No SQL was written.

## Authority Boundaries Preserved

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console remains governance/review only.
- Management Dashboard remains visibility/reporting only.

## Non-Goals Preserved

No source code, SQL, migrations, generated artifacts, DOCX files, POS Server runtime modifications, network calls, orchestration implementation, ExitAuthorization gating implementation, Operator Console implementation, or Management Dashboard implementation were created.

## Validation Results

Validation executed:

- `git diff --check` completed with no whitespace errors.
- `git status --short --untracked-files=all` showed only the two new Markdown files under `docs/v1.3/central-pms/database-delta/`.
- Obsolete primary terminology scan completed with no matches in the created database-delta files.
- Runtime repository status check showed no changes in `D:\SourceCodes\ExitPass-PoSServer`.

## Blockers or Mismatches

No blocker or source contradiction was found during drafting. Central PMS schema conventions remain uninspected in this documentation-only task and must be verified before implementation.

## Recommended First Implementation Branch

`feature/central-pms-fiscal-reference-state`

The first implementation branch should implement persistence/state only, with no POS Server network calls and no ExitAuthorization gating enforcement.

## Recommended Next Task

Draft the Central PMS Fiscal Issuance State Transition and Exception Taxonomy Plan, documentation-only, defining candidate transition rules, terminal states, retry eligibility, manual review states, and gating eligibility.
