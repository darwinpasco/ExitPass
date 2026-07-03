# ExitPass Central PMS POS Server Controlled UAT Approved Test Data Plan Review v1.0

## Branch Name

`feature/central-pms-pos-server-controlled-uat-approved-test-data-plan`

## Files Created

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Approved_Test_Data_Plan_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Approved_Test_Data_Plan_v1.0_Review.md`

## Docs Inspected

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Manual_Save_Procedure_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Template_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Harness_Planning_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Writer_Approval_Record_Review_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/03-pos-server-client-mapper-plan.md`
- `docs/v1.3/central-pms/engineering-pack/04-success-replay-recording-plan.md`
- `docs/v1.3/central-pms/engineering-pack/05-failure-errorposture-plan.md`
- `docs/v1.3/central-pms/engineering-pack/06-unknown-outcome-readback-plan.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`

## Runtime Repo Inspected

Read-only POS Server references inspected:

- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\FiscalDocuments\`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

Central PMS implementation context inspected:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatHarness.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuancePosServerLiveIntegrationService.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatHarnessTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporterTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuancePosServerLiveIntegrationServiceTests.cs`

## Purpose Summary

The plan defines the approved test data posture, required Site/Site POS Server values, upstream finality reference rules, and safe fiscal request facts needed before the first controlled UAT diagnostic execution.

## Approved Test Data Plan Summary

The plan provides:

- environment checklist;
- Site/Site POS Server approval requirements;
- POS Server fiscal configuration requirements;
- Central PMS fiscal reference requirements;
- test transaction data requirements;
- parking session, payment attempt, payment confirmation, and payable basis requirements;
- upstream finality reference rules;
- fiscal request fact requirements;
- line/tender/tax/totals rules;
- idempotency/replay, conflict, failure, and unknown outcome data rules;
- approved data table template;
- first UAT run placeholder record with no invented IDs;
- pre-run validation checklist;
- abort criteria;
- post-run evidence expectations.

## Approved Data Posture

The plan prohibits uncontrolled production customer data, raw provider callback payloads, PAN/CVV/tokens/secrets, unmanaged PII, raw entitlement evidence, uncontrolled images/files, arbitrary sensitive notes, and unmasked plate/ticket data unless explicitly approved.

Non-production fiscal sequence/test policy is preferred. Production fiscal sequence use requires explicit POS Server owner and compliance/accounting approval.

## Site / Site POS Server Requirements

The plan requires explicit approval for:

- Site id/ref;
- Site name;
- Site POS Server id/ref;
- Site POS Server environment;
- Site POS Server base URL reference;
- expected fiscal identity;
- expected fiscal sequence policy;
- expected fiscal sequence state;
- Site owner approval;
- POS Server owner approval;
- engineering lead approval.

## Upstream Finality Rules

The plan requires stable upstream finality references:

- one semantic request per upstream finality ref;
- replay uses same upstream finality ref and same semantic facts;
- conflict uses same upstream finality ref with changed semantic facts only if separately approved;
- do not create a new upstream finality ref to bypass a conflict;
- do not reuse upstream finality refs across unrelated runs.

Suggested pattern:

`CPS-POS-UAT:<run-id>:<scenario>:<sequence>`

## Safe Request Facts Summary

Safe request facts include approved Site/Site POS Server refs, parking session ref, payment attempt ref, payment confirmation ref, payable basis ref, upstream finality ref, business day date, currency, amount minor units, line count, tender count, tax detail presence, totals, and correlation id.

Line/tender/tax/totals must be synthetic or approved test facts, internally consistent, and free of PAN, wallet token, provider secret, customer account data, raw provider payload, or unmanaged PII.

## Authority Boundaries Preserved

The plan preserves:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- UAT evidence and test data are audit artifacts only and do not create operational authority.

## Non-Goals Preserved

The plan does not:

- execute UAT;
- execute live POS Server calls;
- create real fiscal documents;
- add endpoint/tooling;
- implement file-writing;
- enable payment/exit production flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry;
- implement GET readback worker;
- implement Operator Console queue;
- implement Dashboard projection;
- modify source code;
- modify SQL;
- modify POS Server runtime.

## Validation Results

Validation completed:

- `git diff --check`: passed.
- `git status --short --untracked-files=all`: only the two requested approved test data plan files are untracked.
- Obsolete terminology search on changed files: no matches.
- Source, SQL, generated, DOCX, and POS Server runtime files changed: none.

No dotnet tests are required because this is a documentation-only approved test data plan.

## Blockers / Open Items

Open items:

- first approved environment;
- first approved Site and Site POS Server;
- first approved fiscal identity/policy/sequence values;
- first approved test parking/payment/payable references;
- first approved upstream finality reference;
- final evidence save Mode A location;
- whether first run includes replay immediately after newly-created success;
- whether conflict/failure/unknown scenarios are deferred until after first successful run.

## Recommended Next Branch / Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-first-run-readiness-review`

Purpose: review whether approved test data, Site/Site POS Server mapping, POS Server fiscal config, Central PMS config, and evidence manual-save path are ready for the first controlled UAT diagnostic execution.
