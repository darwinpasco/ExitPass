# ExitPass Central PMS POS Server Controlled UAT Manual Save Procedure Review v1.0

## Branch Name

`feature/central-pms-pos-server-controlled-uat-manual-save-procedure`

## Files Created

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Manual_Save_Procedure_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Manual_Save_Procedure_v1.0_Review.md`

## Docs Inspected

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Writer_Approval_Record_Review_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Writer_Approval_Record_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Writer_Approval_Checklist_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_File_Writer_Planning_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Retention_and_Governance_Plan_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Template_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Harness_Planning_v1.0.md`
- `docs/v1.3/central-pms/engineering-pack/11-test-uat-evidence-plan.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`

## Runtime Repo Inspected

Read-only POS Server references inspected:

- `D:\SourceCodes\ExitPass-PoSServer\src\ExitPass.PosServer.Api\FiscalDocuments\`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Idempotency_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Policy_Identity_Resolution_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Sequence_Allocation_Slice.md`
- `D:\SourceCodes\ExitPass-PoSServer\docs\v1.3\runtime\ExitPass_POS_Server_Fiscal_Issuance_Response_Status_Hardening_Slice.md`

Central PMS implementation context inspected:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporter.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/FiscalIssuanceControlledUatHarness.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatEvidenceExporterTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/FiscalIssuanceControlledUatHarnessTests.cs`

## Procedure Summary

The procedure documents how an approved actor manually saves controlled UAT evidence JSON while automatic writer implementation remains unapproved.

It covers:

- current manual-save-only posture;
- who may perform manual save;
- preconditions;
- evidence generation source;
- official and temporary storage modes;
- temporary local handling;
- file/folder naming;
- step-by-step save process;
- sensitive-data and redaction checks;
- manual SHA-256 hash procedure;
- ticket/change/approval linkage;
- evidence review package assembly;
- reviewer signoff;
- abort, supersession, cleanup, and post-save verification.

## Manual Save Modes Summary

Mode A: official approved location available.

- Use approved shared/document repository or ticket/change attachment location.
- Use approved folder naming.
- Link to approval reference.
- Retain according to approved retention policy.

Mode B: official location not yet approved.

- Do not mark evidence as official.
- Save only to a temporary controlled evidence location approved for the run.
- Mark as temporary/manual-save evidence.
- Do not use as final writer-approval evidence until repository owner approves.

## Step-by-Step Summary

The procedure requires:

1. confirm run approval and run ID;
2. confirm evidence owner and storage mode;
3. generate JSON from controlled exporter/harness;
4. review exporter status;
5. confirm no sensitive marker rejection;
6. create approved folder path;
7. save evidence JSON with exact file name;
8. avoid overwrites;
9. create review/redaction notes;
10. compute hash if required;
11. link evidence to ticket/change/approval reference;
12. notify reviewers;
13. update UAT evidence template;
14. confirm no payment finality, ExitAuthorization, or gate side effect.

## Redaction / Hash / Linkage Summary

Redaction:

- confirm no PAN, CVV, tokens, credentials, secrets, raw provider callback payloads, raw entitlement evidence, uncontrolled files/images, unmanaged customer PII, or free-form sensitive blobs.
- stop and restrict access if sensitive data is detected.

Hash:

- use `Get-FileHash -Algorithm SHA256 "<path-to-evidence.json>"` when SHA-256 is available and required/recommended.
- record algorithm, hash, file name, run ID, timestamp, computed by, and command used.

Linkage:

- link evidence to run approval, change/ticket, UAT evidence template, operator runbook checklist, manual-save procedure, reviewer signoff, redaction note, and hash reference.

## Authority Boundaries Preserved

The procedure preserves:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- Evidence files are audit artifacts only and do not create operational authority.

## Non-Goals Preserved

The procedure does not:

- modify source code;
- modify SQL;
- create migrations;
- modify generated artifacts;
- modify DOCX files;
- modify POS Server runtime repository;
- add file-writing code;
- add an API endpoint;
- add CLI/tooling;
- execute a live POS Server call;
- wire anything into payment confirmation;
- wire anything into ExitAuthorization;
- enable fiscal gating enforcement;
- add retry scheduler;
- add GET readback worker;
- implement Operator Console queues;
- implement Management Dashboard projections.

## Validation Results

Validation completed:

- `git diff --check`: passed.
- `git status --short --untracked-files=all`: only the two requested manual-save procedure files are untracked.
- Obsolete terminology search on changed files: no matches.
- Source, SQL, generated, DOCX, and POS Server runtime files changed: none.

No dotnet tests are required because this is a documentation-only manual-save procedure.

## Blockers / Open Items

Open items:

- final official evidence repository owner;
- final official evidence location;
- required hash/signature posture for all UAT runs;
- whether Mode B temporary evidence can be promoted after repository approval;
- exact retention period for fiscal-number allocated evidence;
- final redaction owner and SLA.

## Recommended Next Branch / Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-approved-test-data-plan`

Purpose: define approved test data, Site/Site POS Server values, upstream finality references, and safe fiscal request facts required for the first controlled UAT diagnostic execution.
