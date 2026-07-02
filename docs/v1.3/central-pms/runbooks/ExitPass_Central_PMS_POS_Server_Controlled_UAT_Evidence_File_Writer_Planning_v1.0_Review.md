# ExitPass Central PMS POS Server Controlled UAT Evidence File Writer Planning Review v1.0

## Branch Name

`feature/central-pms-pos-server-controlled-uat-harness-evidence-file-writer-planning`

## Files Created

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_File_Writer_Planning_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_File_Writer_Planning_v1.0_Review.md`

## Docs Inspected

- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Retention_and_Governance_Plan_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Evidence_Template_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Call_Operator_Runbook_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Harness_Planning_v1.0.md`
- `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_Fiscal_Gating_Enforcement_Planning_Freeze_Review_v1.0.md`
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

## Options Summary

The planning document compares:

- Option A: no writer / manual external save.
- Option B: application-level explicit file writer.
- Option C: CLI writer.
- Option D: endpoint/tooling-managed writer.
- Option E: future evidence registry writer.

Option A is safest immediately because it avoids file system side effects in Central PMS application code. Options B and C are future candidates only after output location, redaction, hash, access control, run ID, and approval controls are approved. Options D and E are deferred because they require stronger endpoint, role, registry, retention, and UI governance.

## Recommendation Summary

Recommended immediate posture:

- Keep evidence saving manual/external.
- Use approved shared/document repository and ticket/change linkage.
- Continue generating safe JSON through the existing evidence exporter.
- Do not implement an automatic file writer yet.

Recommended later posture:

- Consider an application-level explicit writer or CLI writer only after a dedicated approval checklist is completed.
- Do not recommend endpoint/tooling-managed writer yet.

## File Safety Rules Summary

The plan requires any future writer to:

- use a configured allow-listed root directory;
- reject path traversal;
- reject writing inside the source repository unless explicitly configured for a gitignored local dry-run folder;
- reject system/temp/default directories unless explicitly configured for local test mode;
- require per-run subdirectories;
- separate redacted and unredacted evidence;
- derive file names from normalized run IDs;
- never write secrets, raw sensitive payloads, or POS Server runtime files.

## Redaction / Hash / Overwrite Summary

Redaction posture:

- refuse export if sensitive markers are reported;
- mark redaction required when metadata or attachments need human review;
- never write unredacted logs/screenshots automatically;
- require redaction owner signoff before approved status.

Hash posture:

- compute SHA-256 over exact saved bytes;
- record hash in hash file and review/signoff document;
- hash original and redacted files separately;
- fail if hash computation fails.

Overwrite posture:

- fail if target evidence/review/hash files exist;
- never overwrite approved evidence;
- corrections require revision suffix and supersession metadata;
- retain superseded evidence.

## Authority Boundaries Preserved

The plan preserves:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.

## Non-Goals Preserved

The plan does not:

- implement evidence file-writing;
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

Validation completed:

- `git diff --check`: passed.
- `git status --short --untracked-files=all`: only the two requested runbook files are untracked.
- Obsolete terminology search on changed files: no matches.
- Source, SQL, generated, DOCX, and POS Server runtime files changed: none.

No dotnet tests are required because this is a documentation-only planning slice.

## Blockers / Open Items

No implementation blockers were identified for this planning slice.

Open items before any writer implementation:

- official evidence repository owner;
- approved output location;
- retention period;
- redaction owner;
- hash/signature requirements;
- run ID sequence owner;
- access control owners;
- decision between application-level writer and CLI writer.

## Recommended Next Branch / Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-evidence-writer-approval-checklist`

Purpose: define the approval checklist that must be completed before implementing any evidence file writer.
