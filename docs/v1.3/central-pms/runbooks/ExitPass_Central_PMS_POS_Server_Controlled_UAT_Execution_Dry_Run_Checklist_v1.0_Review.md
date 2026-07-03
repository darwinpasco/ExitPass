# ExitPass Central PMS POS Server Controlled UAT Execution Dry-Run Checklist v1.0 Review

## Branch name

`feature/central-pms-pos-server-controlled-uat-execution-dry-run-checklist`

## Files created

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Dry_Run_Checklist_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Dry_Run_Checklist_v1.0_Review.md`

## Docs inspected

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_First_Run_Readiness_Refresh_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_First_Run_Readiness_Refresh_v1.0_Review.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Record_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Fill_v1.0_Review.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Review_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Approved_Test_Data_Plan_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Manual_Save_Procedure_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Template_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Harness_Planning_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/03-pos-server-client-mapper-plan.md`
- `docs/v1.3/central-pms/engineering-pack/04-success-replay-recording-plan.md`
- `docs/v1.3/central-pms/engineering-pack/05-failure-errorposture-plan.md`
- `docs/v1.3/central-pms/engineering-pack/06-unknown-outcome-readback-plan.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`

## Code inspected for config names and invocation method

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatHarness.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuancePosServerLiveIntegrationService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceExitAuthorizationGatingReadiness.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/PosServerFiscalDocumentRequestMapper.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Program.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/appsettings.json`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/appsettings.Development.json`
- `infra/docker/docker-compose.yml`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatHarnessTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporterTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuancePosServerLiveIntegrationServiceTests.cs`

## POS Server repo inspected read-only

- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\Program.cs`
- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\FiscalDocuments\`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.site_pos_servers.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.site_pos_server_fiscal_identity_history.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.fiscal_identities.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.fiscal_sequence_policies.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.fiscal_sequence_states.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.controlled_code_sets.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\state\tables\pos.controlled_codes.sql`
- `D:\SourceCodes\ExitPass-PoSServer\db\rebuild\pos_sql_apply_order.txt`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

## Purpose summary

The checklist translates the readiness-refresh blockers into concrete pre-execution verification steps with exact commands or inspection methods, expected pass results, failure results, corrective actions, stop/abort rules, and evidence capture requirements.

## Checklist decision

Decision:

`dry_run_checklist_created_but_execution_invocation_blocked`

Reason:

The checklist is ready for pre-execution infrastructure/configuration checks, but repository inspection found no safe existing runtime invocation surface for the application-level controlled UAT harness.

## Blocker resolution summary

The checklist covers these unresolved blockers:

- POS Server startup on `http://localhost:8091`;
- host and Docker connectivity;
- Central PMS fiscal integration config;
- controlled UAT flags;
- payment-flow and exit-flow guards;
- fiscal gating enforcement;
- POS Server fiscal identity, sequence policy, sequence state, and document type availability;
- test transaction and upstream finality stability;
- evidence folder, manual-save, and hash readiness;
- sensitive-data exclusion;
- no endpoint/CLI/tooling;
- no payment/exit/gate behavior;
- safe invocation method reality;
- Darwin explicit execution approval.

## Exact command summary

The checklist includes commands for:

- `Get-NetTCPConnection -LocalPort 8091`;
- `dotnet run --project src\ExitPass.PosServer.Api\ExitPass.PosServer.Api.csproj --urls "http://localhost:8091"`;
- `Invoke-WebRequest -Uri "http://localhost:8091" -UseBasicParsing`;
- `docker ps --filter "name=exitpass-central-pms"`;
- `docker inspect` network discovery;
- temporary `curlimages/curl:8.8.0` container connectivity;
- Central PMS container environment inspection;
- evidence folder creation and `Test-Path`;
- `Get-FileHash -Algorithm SHA256`;
- `git status`, `git diff --name-only`, and source invocation searches.

## Actual config key names discovered

| Area | Actual section/key | Environment variable form |
| --- | --- | --- |
| POS Server integration section | `FiscalIssuance:PosServerIntegration` | `FiscalIssuance__PosServerIntegration__*` |
| Live-call seam | `EnablePosServerFiscalIssuanceLiveCall` | `FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall` |
| Controlled UAT diagnostic guard | `EnableControlledUatDiagnosticPath` | `FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath` |
| POS Server base URL | `PosServerBaseUrl` | `FiscalIssuance__PosServerIntegration__PosServerBaseUrl` |
| Timeout | `TimeoutSeconds` | `FiscalIssuance__PosServerIntegration__TimeoutSeconds` |
| Payment-flow guard | `EnableLiveFiscalIssuanceFromPaymentFlow` | `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow` |
| Exit-flow guard | `EnableLiveFiscalIssuanceFromExitFlow` | `FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow` |
| ExitAuthorization gating section | `FiscalIssuance:ExitAuthorizationGating` | `FiscalIssuance__ExitAuthorizationGating__*` |
| Fiscal gating enforcement | `EnableFiscalBeforeExitAuthorizationEnforcement` | `FiscalIssuance__ExitAuthorizationGating__EnableFiscalBeforeExitAuthorizationEnforcement` |
| Shadow evaluation | `EnableShadowEvaluation` | `FiscalIssuance__ExitAuthorizationGating__EnableShadowEvaluation` |
| Readiness mode | `ReadinessMode` | `FiscalIssuance__ExitAuthorizationGating__ReadinessMode` |

## Fiscal config verification summary

The checklist uses actual POS Server DB state table names:

- `pos.site_pos_servers`
- `pos.site_pos_server_fiscal_identity_history`
- `pos.fiscal_identities`
- `pos.fiscal_sequence_policies`
- `pos.fiscal_sequence_states`
- `pos.controlled_code_sets`
- `pos.controlled_codes`

The checklist provides SQL guidance for verifying:

- Site POS Server exists and is active;
- fiscal identity exists and is active;
- current effective Site POS Server to fiscal identity assignment exists;
- sequence policy exists, is effective, and is linked to the Site POS Server;
- sequence state exists and values are non-negative;
- `sales_invoice` controlled code exists and is active/effective where applicable;
- production sequence is not used.

## Invocation method outcome

Outcome:

`Outcome B: No safe existing invocation method exists.`

Inspection found:

- application-level `FiscalIssuanceControlledUatHarness.ExecuteAsync(...)`;
- application-level `RunPosServerFiscalIssuanceDiagnosticAsync(...)`;
- unit tests exercising these paths;
- no API endpoint, CLI/tooling, hosted service, or operator action that invokes the harness.

## Remaining blockers

- No safe runtime invocation surface exists for the controlled UAT harness.
- Runtime POS Server startup still must be performed later.
- Runtime Central PMS Docker connectivity still must be verified later.
- Runtime Central PMS config still must be verified later.
- Runtime POS Server fiscal config rows still must be verified later.
- Evidence folder still must be created later.
- Darwin explicit execution approval remains required later.

## Authority boundaries preserved

- Central PMS payment finality ownership is preserved.
- Central PMS fiscal reference recording ownership is preserved.
- Central PMS normal ExitAuthorization ownership is preserved.
- POS Server remains fiscal issuance and numbering only.
- POS Server evidence is not treated as payment, exit, gate, entitlement, manual release, or continuity approval.
- Manual release remains distinct from normal ExitAuthorization.

## Non-goals preserved

No source code, SQL, migrations, generated artifacts, DOCX files, POS Server runtime files, endpoint, CLI/tooling, file-writing code, live POS Server call, fiscal document creation, payment/exit wiring, fiscal gating enforcement, retry scheduler, GET readback worker, Operator Console queue, or Dashboard projection was added by this checklist task.

## Validation results

Validation completed:

| Command/check | Result |
| --- | --- |
| `git diff --check` | Passed; no whitespace errors reported. |
| `git status --short --untracked-files=all` | Passed; only the two allowed checklist files are untracked. |
| Obsolete primary terminology search in changed files | Passed; no matches. |
| Changed-file scope confirmation | Passed; no source, SQL, generated artifact, DOCX, or POS Server runtime file changed. |

No dotnet tests were run because this is a documentation/checklist-only task.

No substantial manual test is needed for this documentation/checklist creation task.

## Recommended next task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-invocation-surface`

Purpose:

Add the smallest safe controlled invocation surface for the application-level UAT harness without exposing production payment/exit flows, without enabling fiscal gating enforcement, and without adding uncontrolled operator capability.
