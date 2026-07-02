# ExitPass Central PMS POS Server Controlled UAT Evidence Retention and Governance Plan v1.0 Review

## Branch Name

`feature/central-pms-pos-server-controlled-uat-harness-evidence-retention-planning`

## Files Created

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Retention_and_Governance_Plan_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Retention_and_Governance_Plan_v1.0_Review.md`

## Docs Inspected

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Template_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Harness_Planning_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Gating_Enforcement_Planning_Freeze_Review_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Gating_Enforcement_Rollout_Runbook_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`

## Runtime Repo Inspected

Read-only POS Server references:

- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\FiscalDocuments\`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## Storage Options Summary

Options reviewed:

- repository-local ignored evidence folder;
- secured shared evidence folder;
- ticketing/change-management attachment;
- document management repository;
- database-backed evidence registry;
- object storage with immutable retention.

The plan rejects repository-local storage as official evidence storage and defers database/object-storage options until governance, schema, UI, and operational needs justify them.

## Recommendation Summary

Recommended approach:

- Phase 1: secured shared folder or document management repository plus ticket/change linkage, with manual save by approved actor and no automatic file writing.
- Phase 2: later internal harness or CLI writer only after redaction, hash, approval, and retention controls are finalized.
- Phase 3: future registry, Operator Console, Dashboard, or immutable archive integration if required.

## Lifecycle Summary

Lifecycle states defined:

- `planned`
- `generated`
- `submitted`
- `redaction_review`
- `approved`
- `rejected`
- `superseded`
- `archived`

Status model defined:

- `draft`
- `submitted_for_review`
- `redaction_required`
- `approved`
- `rejected`
- `superseded`
- `archived`

## Redaction / Access / Retention Summary

Redaction:

- sensitive marker scan required;
- raw logs/screenshots reviewed before sharing;
- redacted copies produced for broader review;
- unredacted evidence restricted;
- redaction owner signs off.

Access:

- engineering lead, UAT lead, Central PMS owner, POS Server owner, operations lead, and compliance/accounting observer have scoped responsibilities;
- support/helpdesk receive limited summaries only;
- ordinary parking operators have no access to raw UAT evidence.

Retention:

- retain through certification/accreditation/release decision period;
- retain longer if fiscal numbers were allocated;
- exact period remains subject to compliance/accounting/legal approval;
- archive superseded evidence but do not delete without approval.

## Authority Boundaries Preserved

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.

## Non-Goals Preserved

This task did not:

- implement evidence storage;
- write evidence files;
- expose endpoint/tooling;
- execute live POS Server calls;
- enable production payment/exit flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry;
- implement readback worker;
- implement Operator Console queue;
- implement Dashboard projection;
- modify source code;
- modify SQL;
- modify POS Server runtime.

## Validation Results

Documentation-safe validation was run:

- `git diff --check` - passed.
- `git status --short --untracked-files=all` - expected two new governance planning files only.
- changed-file obsolete primary terminology search - no matches.
- source/SQL/generated/DOCX change check - no such files changed.

No dotnet tests were required because this was documentation-only.

## Blockers / Open Items

- Official evidence repository owner is not yet assigned.
- Exact retention period is not yet approved.
- Hash/signature requirements are not yet finalized.
- Redaction owner role is not yet finalized.
- Decision remains open on whether future evidence saving should be application-level, CLI-based, or external/manual.

## Recommended Next Branch / Task

`feature/central-pms-pos-server-controlled-uat-harness-evidence-file-writer-planning`

Purpose: plan whether the application-level harness should later include an explicit file writer, or whether evidence saving should remain external/manual until CLI/tooling is approved.
