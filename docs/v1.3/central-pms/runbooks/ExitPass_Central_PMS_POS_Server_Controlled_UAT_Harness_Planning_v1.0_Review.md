# ExitPass Central PMS POS Server Controlled UAT Harness Planning Review v1.0

## Branch Name

`feature/central-pms-pos-server-controlled-uat-harness-planning`

## Files Created

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Harness_Planning_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Harness_Planning_v1.0_Review.md`

## Docs Inspected

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Template_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Gating_Enforcement_Planning_Freeze_Review_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Gating_Enforcement_Rollout_Runbook_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/03-pos-server-client-mapper-plan.md`
- `docs/v1.3/central-pms/engineering-pack/04-success-replay-recording-plan.md`
- `docs/v1.3/central-pms/engineering-pack/05-failure-errorposture-plan.md`
- `docs/v1.3/central-pms/engineering-pack/06-unknown-outcome-readback-plan.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- current Central PMS diagnostic seam, tests, API DI, endpoints folder, and contracts folder for context only

## Runtime Repo Inspected

Read-only POS Server runtime repository inspected:

- `D:\SourceCodes\ExitPass-PoSServer`
- branch confirmed as `dev`
- runtime fiscal issuance idempotency, identity/policy resolution, sequence allocation, and response/status hardening references inspected
- `src\ExitPass.PosServer.Api\FiscalDocuments\` inspected for fiscal document endpoint context

## Option Comparison Summary

Options compared:

- Option A: application-level test harness;
- Option B: internal CLI tool;
- Option C: internal diagnostic API endpoint;
- Option D: Operator Console action, future only;
- Option E: scheduled/job-based diagnostic, not recommended now.

The comparison assessed safety, complexity, authorization requirements, evidence capture ease, fiscal-number allocation risk, suitability before endpoint/tooling, default-disabled fit, UAT usability, and recommended timing.

## Recommended Option

Recommended first option: Option A, application-level internal test harness.

Reason:

- safest before endpoint/tooling;
- uses the existing application seam;
- avoids public or remote exposure;
- allows controlled evidence capture;
- limits execution to engineering/UAT environment;
- preserves no payment/exit production wiring;
- avoids introducing auth/RBAC surface before requirements are settled.

Option B or C should be considered only after successful Option A evidence and after role/auth/evidence controls are finalized. Operator Console action is future-only. Scheduled/job diagnostics are not recommended.

## Implementation Sequence Summary

Recommended next implementation sequence:

- create application-level UAT harness/test fixture invoking `RunPosServerFiscalIssuanceDiagnosticAsync(...)`;
- require explicit live-call and diagnostic guard configuration;
- require valid POS Server base URL and timeout;
- require payment/exit flow guards false and fiscal gating enforcement false;
- require run id and evidence template path/reference;
- require safe input model;
- validate Site/Site POS Server and upstream finality reference semantics;
- execute one controlled diagnostic run;
- capture evidence template fields;
- confirm no payment finality mutation;
- confirm no ExitAuthorization or gate behavior;
- disable diagnostic config after run;
- review evidence and reconcile fiscal reference outcome.

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

- implement a harness/tool/endpoint;
- execute live calls;
- enable production payment/exit flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry;
- implement a GET readback worker;
- implement Operator Console queues;
- implement Dashboard projections;
- modify source code;
- modify SQL;
- modify migrations;
- modify generated artifacts;
- modify DOCX files;
- modify POS Server runtime files.

## Validation Results

Documentation-safe validation was run:

- `git diff --check` - passed.
- `git status --short --untracked-files=all` - expected two new planning files only.
- changed-file obsolete primary terminology search - no matches.
- source/SQL/generated/DOCX change check - no such files changed.

No dotnet tests were required because this was documentation-only.

## Blockers/Open Items

- No approved invocation harness exists yet.
- Final harness input file format is undecided.
- Final evidence output location and retention policy are undecided.
- Final approval evidence format is undecided.
- Site allow-list approach is not finalized.
- Manual GET readback workflow for unknown outcomes remains outside this plan.

## Recommended Next Branch/Task

`feature/central-pms-pos-server-controlled-uat-application-harness`

Purpose: implement the first safe application-level UAT harness for invoking the controlled diagnostic seam using approved config/test data, without adding endpoint/tooling and without wiring payment/exit flows.
