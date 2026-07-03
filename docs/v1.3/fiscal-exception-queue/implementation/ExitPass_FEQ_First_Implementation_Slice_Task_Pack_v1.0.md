# ExitPass FEQ First Implementation Slice Task Pack v1.0

## Purpose

Prepare the first real FEQ implementation slice:

**FEQ Inventory + Persistence/Intake Plan + Readback Contract Preparation**

This task pack is for implementation preparation only. It does not implement retry execution and does not modify POS Server runtime.

## Target repo

`D:\SourceCodes\ExitPass`

## Branch naming recommendation

`feature/central-pms-feq-inventory-persistence-intake-readback-prep`

If the first slice is split further, use:

- `feature/central-pms-feq-inventory-and-constraints`
- `feature/central-pms-feq-persistence-intake-plan`

## Exact scope

- Inspect Central PMS fiscal issuance implementation and tests.
- Inventory fiscal reference persistence fields, states, lookup paths, and tests.
- Inventory POS Server client support for POST and GET/readback.
- Inventory existing event/outbox/audit/reconciliation patterns.
- Produce an implementation inventory and reviewed persistence/intake/readback preparation plan.
- Define FEQ case identity approach and duplicate case collapse strategy.
- Define intake points from failure/unknown/config/mismatch paths.
- Define readback contract inputs/outputs and classification plan.
- Define tests required for the next implementation slice.

## Files/areas to inspect

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/FiscalIssuance/`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Domain/FiscalIssuance/`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Eventing/`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Eventing/`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Reconciliation/`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/FiscalIssuanceReferenceRepositoryTests.cs`
- `docs/v1.3/fiscal-exception-queue/ExitPass_Fiscal_Exception_Queue_Readback_Retry_Design_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md`
- `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md`
- Operator Console and Management Dashboard SDDs for visibility boundary checks.

## Non-goals

- Do not implement retry execution.
- Do not add retry scheduler.
- Do not add Operator Console UI.
- Do not add Management Dashboard projection.
- Do not enable fiscal-gated ExitAuthorization enforcement.
- Do not modify POS Server runtime repository.
- Do not call POS Server.
- Do not execute controlled UAT.
- Do not create fiscal documents.
- Do not modify production runtime config.
- Do not permit arbitrary manual fiscal document creation.
- Do not permit manual fiscal number editing.

## Deliverables

- Implementation inventory document or section.
- FEQ persistence/intake approach proposal.
- Readback contract preparation summary.
- Duplicate case collapse/linking plan.
- Test plan for persistence/intake/readback.
- Open decision list.
- Review notes confirming no retry execution is introduced.

## Safety rules

- Preserve Central PMS payment finality, fiscal reference recording, and normal ExitAuthorization authority.
- Preserve POS Server fiscal issuance and numbering authority.
- Readback must precede retry for unknown outcomes.
- Retry eligibility planning must not execute retry.
- Matching readback evidence must reconcile Central PMS evidence instead of retrying.
- Mismatch must route to manual review.
- Operator Console and Dashboard remain visibility/handoff surfaces.
- Manual release remains separate evidence and not normal ExitAuthorization.

## Validation commands

Use documentation-safe validation for planning-only work:

```powershell
git status --short --untracked-files=all
git diff --check
```

If implementation code is changed in a later slice, run at minimum:

```powershell
dotnet test src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj --filter FiscalIssuance --no-restore --logger "console;verbosity=minimal"
```

Add integration/contract tests when persistence or API surfaces are modified.

## Completion summary format

Include:

- branch name,
- files changed,
- inventory areas inspected,
- persistence/intake proposal summary,
- readback contract preparation summary,
- duplicate case strategy,
- retry execution confirmation: not implemented,
- authority boundaries preserved,
- validation results,
- open decisions/blockers,
- recommended next slice.

## Suggested Codex terminal/persona

Use Codex v1.3 for ExitPass platform, Central PMS, documentation, orchestration, Operator Console, Management Dashboard, and FEQ planning.

## Codex Z notes

Use Codex Z only for ExitPass-PoSServer runtime/database/API tasks.

The first FEQ implementation planning task should not modify `D:\SourceCodes\ExitPass-PoSServer`. If the readback contract inventory proves POS Server API/runtime changes are required, create a separate Codex Z task for the POS Server repo.
