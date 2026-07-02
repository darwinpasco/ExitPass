# ExitPass Central PMS POS Server Controlled UAT Call Operator Runbook Review v1.0

## Branch Name

`feature/central-pms-pos-server-controlled-uat-call-operator-runbook`

## Files Created

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0_Review.md`

## Docs Inspected

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Gating_Enforcement_Planning_Freeze_Review_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Gating_Enforcement_Rollout_Runbook_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/03-pos-server-client-mapper-plan.md`
- `docs/v1.3/central-pms/engineering-pack/04-success-replay-recording-plan.md`
- `docs/v1.3/central-pms/engineering-pack/05-failure-errorposture-plan.md`
- `docs/v1.3/central-pms/engineering-pack/06-unknown-outcome-readback-plan.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- current Central PMS fiscal live integration source and tests for context only

## Runtime Repo Inspected

Read-only POS Server runtime repository inspected:

- `D:\SourceCodes\ExitPass-PoSServer`
- branch confirmed as `dev`
- runtime fiscal issuance idempotency, identity/policy resolution, sequence allocation, and response/status hardening references inspected
- `src\ExitPass.PosServer.Api\FiscalDocuments\` inspected for fiscal document endpoint context

## Runbook Summary

The runbook defines a controlled operator/UAT procedure for the Central PMS application-level POS Server fiscal issuance diagnostic seam. It covers authorized roles, approvals, environment/configuration/POS Server/Central PMS prerequisites, request/test data requirements, sensitive data exclusions, invocation posture, expected diagnostic statuses, evidence capture, success and abort criteria, idempotency/replay/conflict/unknown handling, reconciliation, rollback, communications, and open risks.

The runbook explicitly documents that the current implementation has no endpoint, no CLI/tooling, no payment confirmation wiring, no ExitAuthorization wiring, no fiscal gating enforcement, no retry scheduler, and no GET readback worker.

## Authority Boundaries Preserved

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.

## Non-Goals Preserved

The task did not:

- expose an endpoint;
- expose operator tooling;
- execute a live call;
- enable production payment or exit flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry;
- implement readback worker;
- implement Operator Console queues;
- implement Dashboard projections;
- implement BIR reports, Digital SI, or gate behavior;
- modify source code, SQL, migrations, generated artifacts, DOCX files, or POS Server runtime files.

## Validation Results

Documentation-safe validation was run:

- `git diff --check` - passed.
- `git status --short --untracked-files=all` - expected two new runbook files only.
- changed-file obsolete primary terminology search - no matches.
- source/SQL/generated/DOCX change check - no such files changed.

No dotnet tests were required because this was documentation-only.

## Blockers/Open Items

- No approved operator endpoint, CLI, or harness exists yet.
- Final role/permission model for any future invocation mechanism remains undefined.
- Final UAT evidence repository/location remains undefined.
- Pilot Site and Site POS Server are not selected by this document.
- Manual GET readback approval workflow remains operationally defined outside this runbook.

## Recommended Next Branch/Task

`feature/central-pms-pos-server-controlled-uat-evidence-template`

Purpose: create a structured UAT evidence template for controlled POS Server fiscal issuance diagnostic runs before exposing tooling or endpoints.
