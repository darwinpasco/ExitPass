# ExitPass Central PMS Fiscal Issuance State Transition and Exception Taxonomy Plan v1.0 Review

## Review Summary

The Central PMS Fiscal Issuance State Transition and Exception Taxonomy Plan v1.0 was created as a documentation-only planning artifact for future Central PMS implementation. It defines candidate states, transitions, terminal classification, retry eligibility, replay handling, unknown outcome handling, manual review/reconciliation states, gating eligibility, exception reasons, `errorPosture` mapping, queue mapping, dashboard grouping, audit/event mapping, and test implications.

No source code, SQL, migrations, endpoint specifications, final enum declarations, generated artifacts, DOCX files, POS Server runtime changes, staging, or commits were created.

## Branch Name

`docs/v1.3-central-pms-fiscal-state-taxonomy-plan`

## Files Created

- `docs/v1.3/central-pms/state-taxonomy/ExitPass_Central_PMS_Fiscal_Issuance_State_Transition_and_Exception_Taxonomy_Plan_v1.0.md`
- `docs/v1.3/central-pms/state-taxonomy/ExitPass_Central_PMS_Fiscal_Issuance_State_Transition_and_Exception_Taxonomy_Plan_v1.0_Review.md`

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

## Candidate States Summary

Candidate states defined:

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

Each state includes planning meaning, entry trigger, retry eligibility, manual review eligibility, normal ExitAuthorization eligibility, terminal/non-terminal classification, dashboard grouping, and operator queue grouping.

## State Transition Summary

The plan defines candidate transitions from payment finality to pending fiscal issuance, request start, success/replay recording, conflict, request/configuration/service failure, unknown outcome, readback recovery, manual review escalation, exception release, and reconciliation closure.

Final transition constraints remain deferred to implementation.

## Terminal / Non-Terminal State Summary

Terminal successful states:

- `fiscal_issuance_recorded`
- `fiscal_issuance_replayed`, only when complete and reconciled

Terminal policy/closure states:

- `not_required`, only under approved policy
- `fiscal_issuance_exception_released`, as exception/manual release only
- `fiscal_issuance_reconciled`, when closure is approved

Non-terminal states include pending, requested, conflict, failed request/configuration/service, unknown, and manual review states.

## ExitAuthorization Gating Eligibility Summary

Eligible candidate states:

- `fiscal_issuance_recorded`
- `fiscal_issuance_replayed`, only when reconciled and complete
- `fiscal_issuance_reconciled`, only if reconciliation confirms complete fiscal evidence and policy permits
- `not_required`, only if approved policy explicitly says fiscal issuance is not required

All unresolved, failed, conflict, unknown, manual review, and exception release states remain not eligible for normal ExitAuthorization.

## Retry Eligibility Summary

The plan defines retry posture by state:

- first request from pending state.
- no duplicate concurrent request from requested state unless timeout/lease policy allows.
- request failures retry only after request correction.
- configuration failures retry only after configuration correction.
- service failures retry only after service recovery.
- unknown states retry same semantic request with same upstream finality reference or GET readback where possible.
- conflict has no automatic retry.
- recorded/replayed states do not need retry.

## Exception Taxonomy Summary

The taxonomy includes request/data, configuration/fiscal setup, conflict/replay, service/unknown, and review/manual release reason groups. Candidate reasons include missing payable basis, missing upstream finality reference, unapproved discount reference, fiscal identity/policy/state failures, idempotency conflict, replay mismatch, persistence failures, fiscal number assignment incomplete, timeout, readback failures, Central PMS reference persistence failure, manual review required, and reconciliation closure.

## ErrorPosture Mapping Summary

Mapping created:

- `do_not_retry_without_request_change` -> `fiscal_issuance_failed_request` or `fiscal_issuance_conflict`; no automatic retry.
- `retry_after_configuration_correction` -> `fiscal_issuance_failed_configuration`; retry after correction only.
- `retry_after_service_recovery` -> `fiscal_issuance_failed_service` or `fiscal_issuance_unknown`; retry after recovery or controlled readback.

## Operator Console Queue Mapping Summary

States/reasons were mapped to:

- pending fiscal issuance
- retry needed
- configuration correction required
- idempotency conflict
- unknown outcome
- incomplete numbering evidence
- fiscal reference mismatch
- manual release requested after fiscal failure
- reconciled/closed exceptions

Operator Console remains governance/review only.

## Dashboard Mapping Summary

States were mapped to metric groups:

- success
- replay
- request failure
- configuration failure
- service failure
- conflict
- unknown
- manual review
- exception release
- reconciled
- pending
- not required / excluded

Management Dashboard remains read-only visibility.

## Audit / Event Mapping Summary

Candidate event mappings include:

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
- `ExitAuthorizationBlockedByFiscalState`

Event names remain candidate placeholders.

## Validation Results

Validation executed:

- `git diff --check` completed with no whitespace errors.
- `git status --short --untracked-files=all` showed only the two new Markdown files under `docs/v1.3/central-pms/state-taxonomy/`.
- Obsolete primary terminology scan completed with no matches in the created state-taxonomy files.
- Runtime repository status check showed no changes in `D:\SourceCodes\ExitPass-PoSServer`.

## Blockers or Mismatches

No blocker or source contradiction was found during drafting. Final enum names, transition constraints, retry scheduler behavior, and implementation conventions remain deferred.

## Recommended First Implementation Branch

`feature/central-pms-fiscal-reference-state`

The first implementation slice remains persistence/state only, with no POS Server network calls.

## Recommended Next Task

Start the first implementation slice planning package for `feature/central-pms-fiscal-reference-state`, beginning with repository/schema inspection and a non-SQL implementation checklist for candidate persistence objects, fields, state values, and validation tests.
