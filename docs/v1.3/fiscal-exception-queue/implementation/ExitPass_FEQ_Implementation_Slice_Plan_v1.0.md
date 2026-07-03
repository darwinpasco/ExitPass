# ExitPass FEQ Implementation Slice Plan v1.0

## 1. Document control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Implementation Slice Plan |
| Version | v1.0 |
| ExitPass baseline | v1.3 |
| Status | Ready for review |
| Branch | `docs/v1.3-feq-implementation-slice-plan` |
| Scope | Implementation slice planning only |
| Owner | ExitPass platform documentation stream |
| Last updated | 2026-07-03 |

## 2. Purpose

This plan converts the merged Fiscal Exception Queue / Readback / Retry Design into ordered, reviewable, implementation-ready slices for Central PMS, POS Server integration touchpoints, Operator Console visibility, Management Dashboard projection, audit, tests, and release gates.

The plan is intentionally not a runtime implementation. It defines sequencing, dependencies, safety gates, and task packaging so implementation can proceed without violating the FEQ design boundary.

## 3. Source baseline and inspected files

| Source | Usage |
| --- | --- |
| `docs/v1.3/fiscal-exception-queue/ExitPass_Fiscal_Exception_Queue_Readback_Retry_Design_v1.0.md` | Primary FEQ design source. |
| `docs/v1.3/fiscal-exception-queue/reviews/ExitPass_Fiscal_Exception_Queue_Readback_Retry_Design_Review_v1.0.md` | FEQ review posture and open decisions. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` | Operator Console visibility/handoff boundary. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_System_Design_SDD_v1.0.md` | Dashboard projection/reporting boundary. |
| `docs/v1.3/central-pms/engineering-pack/08-operator-console-queues-plan.md` | Original Operator Console queue field/action candidates. |
| `docs/v1.3/central-pms/engineering-pack/09-dashboard-visibility-plan.md` | Dashboard fiscal visibility and projection candidates. |
| `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md` | Central PMS to POS Server retry/readback/idempotency/gating contract. |
| `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md` | POS Server API contract, GET readback, idempotency source, semantic request hash, error posture. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | POS Server authority and fiscal failure/retry design posture. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/FiscalIssuance/*` | Existing fiscal issuance orchestration, POS Server live integration, request mapping, response parsing, controlled UAT harness, and gating readiness areas for future implementation inspection. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/FiscalIssuance/*` | Existing fiscal issuance reference repository and HTTP POS Server fiscal document client for future implementation inspection. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Domain/FiscalIssuance/*` | Existing fiscal issuance states, evidence status, error posture, result classification, and exception reason domain values. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Eventing/*` and `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Eventing/*` | Existing event/outbox/recovery patterns for audit/projection planning. |
| `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Reconciliation/ReconciliationEventPersistence.cs` and reconciliation tests | Existing reconciliation persistence/event handoff references. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/FiscalIssuance/*` | Existing unit test coverage for mapper/parser/orchestration/live integration/gating/controlled UAT. |
| `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Persistence/FiscalIssuanceReferenceRepositoryTests.cs` | Existing fiscal issuance reference persistence test coverage. |

No database DDL was changed or finalized by this plan. Persistence names below are implementation planning concepts unless an implementation slice confirms or creates actual schema.

## 4. Implementation posture

- Documentation and planning only.
- No runtime code in this branch.
- No test code changes in this branch.
- No SQL, migration, Docker, runtime config, POS Server repo, UAT, local evidence, secret, or environment changes.
- Implementation must be delivered in small reviewable slices with explicit tests and release gates.
- Retry execution is not allowed until readback and retry eligibility slices are complete.

## 5. Authority boundaries and non-negotiables

| Rule | Implementation consequence |
| --- | --- |
| Central PMS owns payment finality. | FEQ may read payment finality context but must not mutate payment state. |
| Central PMS owns fiscal reference recording. | FEQ must update fiscal reference state only through Central PMS-approved application/repository paths. |
| Central PMS owns normal ExitAuthorization. | FEQ may expose blocked/eligible context but must not issue normal ExitAuthorization. |
| POS Server owns fiscal issuance and fiscal numbering only. | FEQ may call approved backend readback/retry paths but must not edit numbers or fabricate documents. |
| Readback before retry for unknown outcomes. | Unknown outcome slices must implement readback classification before retry execution exists. |
| Retry is controlled, idempotent, audited, and eligibility-based. | Retry scheduler depends on original request facts, semantic hash, idempotency, config, audit, RBAC/service identity, and retry limits. |
| Matching readback evidence is reconciled instead of retried. | Readback matched path updates Central PMS evidence; retry is blocked. |
| Mismatch routes to manual review. | No automatic retry/closure on mismatch. |
| Operator Console remains visibility/handoff only. | No direct POS Server calls and no arbitrary retry UI action. |
| Management Dashboard remains visibility/reporting only. | Projection only; no command execution. |
| Manual release remains separate evidence. | FEQ may link manual release context but does not convert it into normal ExitAuthorization. |

## 6. Current-state inventory checklist

| Area | Current known reference | Slice 0 inventory question |
| --- | --- | --- |
| Fiscal reference records | `PostgresFiscalIssuanceReferenceRepository.cs`, `FiscalIssuanceReferenceModels.cs`, repository tests. | What fields already support FEQ identity, source request facts, evidence, states, and duplicate lookup? |
| Fiscal issuance orchestration | `FiscalIssuanceOrchestrationService.cs`, `FiscalIssuancePosServerLiveIntegrationService.cs`. | Where should FEQ intake be called without changing payment/exit flow authority? |
| POS Server client | `HttpPosServerFiscalDocumentClient.cs`, `PosServerFiscalDocumentClientModels.cs`. | Does current client expose GET readback or only POST? |
| Request mapper | `PosServerFiscalDocumentRequestMapper.cs`. | Which original request facts can be persisted or reconstructed for retry eligibility? |
| Response parser | `PosServerFiscalDocumentResponseParser.cs`. | Which outcomes map to FEQ categories, error posture, and readback requirement? |
| Domain states | `FiscalIssuanceIntegrationState.cs`, `FiscalIssuanceExceptionReason.cs`, `FiscalIssuanceErrorPosture.cs`. | Which existing states can be reused versus new FEQ case states? |
| Gating readiness | `FiscalIssuanceExitAuthorizationGatingReadiness.cs`, gate evaluator tests. | How should FEQ state surface as fiscal prerequisite context without enabling enforcement? |
| Eventing/outbox | `Application/Eventing/*`, `Infrastructure/Eventing/*`. | Which outbox/audit patterns should FEQ use for projection and traceability? |
| Reconciliation | Reconciliation application/infrastructure/tests. | How should FEQ closure evidence hand off without taking reconciliation authority? |
| Operator Console | Operator Console services/tests. | What read model and permission patterns should queue visibility follow? |
| Dashboard projection | Management Dashboard SDD and dashboard visibility plan. | What projection shape is required later without command authority? |
| Tests | FiscalIssuance unit/integration tests. | Which tests become release gates per slice? |

## 7. Slice map and dependency order

| Slice | Name | Depends on | Behavior impact |
| --- | --- | --- | --- |
| 0 | Inventory and implementation constraints | None | No behavior changes. |
| 1 | FEQ persistence and intake design-to-code preparation | Slice 0 | Planning/schema proposal only or reviewed persistence prep; no retry execution. |
| 2 | Readback worker and classification | Slices 0-1 | Adds backend-only readback classification; no retry execution. |
| 3 | Retry eligibility evaluator only | Slices 0-2 | Adds retry allow/block evaluation; no scheduler execution. |
| 4 | Controlled retry scheduler | Slices 0-3 | Adds controlled retry execution after readback and eligibility gates. |
| 5 | Manual review and closure workflow | Slices 0-4 | Adds assignment/reason/closure workflow without fiscal number editing. |
| 6 | Operator Console scoped handoff | FEQ visibility read model from prior slices | Adds scoped visibility/governed requests only. |
| 7 | Management Dashboard projection | FEQ projection events/read model | Adds visibility/reporting only. |
| 8 | Reconciliation handoff | Closure/evidence model | Links FEQ closure evidence to reconciliation without closure authority transfer. |
| 9 | Fiscal-gated ExitAuthorization integration | Future enforcement approval | Deferred; current production fiscal gating remains disabled. |

## 8. Recommended first vertical slice

Recommended first implementation slice:

**FEQ Inventory + Persistence/Intake Plan + Readback Contract Preparation**

This slice should include:

- Central PMS implementation inventory.
- Existing fiscal reference persistence and state inventory.
- FEQ case identity and persistence proposal marked for review.
- Intake trigger plan from Central PMS fiscal issuance failure/unknown/config/mismatch paths.
- Duplicate case collapse/linking plan.
- POS Server GET/readback contract inventory.
- Readback input/output classification plan.
- Test plan for future slice implementation.

It must not include retry execution. Retry before readback is unsafe and violates the FEQ design.

## 9. Persistence and state planning

Slice 1 must decide whether FEQ state is:

- embedded in existing fiscal issuance reference persistence,
- modeled as separate FEQ case persistence linked to fiscal issuance reference,
- or split between reference state and FEQ workflow state.

Required persistence concepts:

- stable FEQ case identity,
- source fiscal reference identity,
- payment confirmation/payment attempt/payable basis/upstream finality context where available,
- Site/Site POS Server context,
- fiscal document type context,
- original request facts or reference to reconstruct them,
- semantic request hash or stored/recomputed comparison input,
- idempotency context,
- category,
- lifecycle state,
- priority/SLA,
- assignment,
- readback attempt summary,
- retry eligibility summary,
- retry count summary,
- evidence references,
- audit references,
- closure reason and approver.

Do not invent final table or column names in implementation tasks unless the slice explicitly creates a reviewed schema/migration.

## 10. FEQ intake planning

FEQ intake should create or update one stable case when:

- POS Server is unavailable,
- POS Server times out,
- POS Server returns HTTP failure,
- Central PMS recording/commit fails after POS Server may have succeeded,
- outcome is unknown,
- idempotency conflict occurs,
- semantic request hash mismatch is detected,
- fiscal document exists but is not recorded,
- Central PMS and POS Server evidence mismatch,
- fiscal number is missing or conflicting,
- fiscal identity/sequence/configuration is missing,
- Central PMS mapping fails,
- fiscal-gated ExitAuthorization is blocked,
- manual release is requested after fiscal failure,
- retry is exhausted,
- audit/evidence write fails.

Duplicate handling:

- Same fiscal reference/upstream finality/idempotency context should update or link to the existing case.
- Duplicate cases must not create independent retry opportunities.
- Intake should be idempotent and safe for repeated exception signals.

## 11. Readback planning

Slice 2 should add backend-only readback classification.

Readback inputs to inventory/plan:

- known POS Server fiscal document ID when available,
- upstream finality reference,
- Site POS Server identity,
- fiscal document type identity,
- original request facts needed for semantic comparison,
- Central PMS fiscal reference ID,
- correlation/audit identifiers.

Readback classifications:

- `matched`,
- `not_found`,
- `mismatch`,
- `failed`,
- `unavailable`,
- `unknown`.

Readback constraints:

- Must use approved backend service identity.
- Operator Console and Dashboard must not call POS Server.
- Must not mutate POS Server.
- Must audit every attempt.
- Matched readback reconciles Central PMS evidence instead of retrying.

## 12. Retry eligibility planning, no retry execution in slice 1

Retry eligibility belongs in Slice 3 and must not execute retry.

Eligibility checks:

- original request exists and is complete,
- semantic request hash is stable or recomputable,
- upstream finality/idempotency context is stable,
- readback result is `not_found`,
- no matched POS Server document exists,
- no mismatch/manual review blocker exists,
- fiscal identity/sequence/policy/configuration is valid,
- audit/event persistence is available,
- retry limit/SLA policy allows retry,
- service identity/RBAC allows retry.

Block reasons:

- matched readback evidence,
- mismatch,
- semantic request hash mismatch,
- idempotency conflict,
- missing config,
- audit unavailable,
- retry limit exceeded,
- unauthorized action,
- attempt to change upstream finality reference to bypass conflict.

## 13. Audit/evidence planning

Audit/evidence records must exist for:

- intake,
- category/state transition,
- assignment/reassignment,
- readback request/result,
- retry eligibility decision,
- retry scheduled/executed/result in later slices,
- mismatch review,
- closure,
- supersession/cancellation,
- manual release association,
- dashboard/operator projection publication.

Evidence handling:

- Prefer evidence references and hashes over raw payloads.
- Redact sensitive payment/customer/provider details.
- Audit privileged evidence access.
- Block recovery actions if audit persistence is unavailable unless a later explicitly approved break-glass policy exists.

## 14. Operator Console handoff planning

Slice 6 should add scoped visibility and governed handoff only:

- assigned fiscal exception list,
- case detail,
- category/state/age/SLA,
- readback/retry status,
- evidence references,
- manual review assignment,
- escalation reason,
- manual release association if present.

Operator Console must not:

- call POS Server,
- execute arbitrary retry,
- edit fiscal numbers,
- create fiscal documents,
- issue ExitAuthorization,
- open gates.

## 15. Management Dashboard projection planning

Slice 7 should expose read-only FEQ projection:

- backlog by state/category/Site/Site Group,
- age/SLA,
- readback outcome trends,
- retry eligibility/success/failure trends once retry exists,
- mismatch/manual review backlog,
- retry exhausted count,
- config-blocked count,
- closure time,
- manual release association count,
- reconciliation handoff status.

Dashboard must remain visibility/reporting only and must not trigger readback, retry, writeback, closure, manual release, ExitAuthorization, or gate behavior.

## 16. Reconciliation handoff planning

Slice 8 should link FEQ closure evidence to reconciliation:

- matched readback evidence,
- retry success evidence,
- mismatch closure reason,
- unrecoverable closure reason,
- supersession/cancellation reference,
- manual release association,
- audit/evidence references.

Reconciliation retains reconciliation authority. FEQ supplies evidence and state only.

## 17. RBAC and service identity planning

Required identity model:

- FEQ service identity for intake/readback/retry workers.
- Support operator assignment permissions.
- Supervisor approval permissions.
- Engineering/config owner correction permissions.
- POS Server owner review permissions.
- Central PMS owner fiscal reference review permissions.
- Compliance/accounting dual-control permissions for sensitive closure.
- Auditor read-only permissions.
- Dashboard and Operator Console read permissions scoped by Site/Site Group.

No public or unauthenticated FEQ actions are allowed.

## 18. Configuration and feature-flag planning

Recommended flags/gates:

| Flag/gate | Default | First enabling slice |
| --- | --- | --- |
| FEQ intake enabled | off | Slice 1 or later |
| FEQ readback worker enabled | off | Slice 2 |
| FEQ retry eligibility enabled | off | Slice 3 |
| FEQ retry scheduler enabled | off | Slice 4 |
| FEQ Operator Console visibility enabled | off | Slice 6 |
| FEQ Dashboard projection enabled | off | Slice 7 |
| Fiscal-gated ExitAuthorization enforcement | off/current production disabled | Slice 9 only after explicit approval |
| Break-glass recovery without audit | off | Not planned; requires separate approval |

## 19. Observability planning

Metrics/logging to plan:

- FEQ case count by state/category,
- intake rate,
- duplicate case collapse count,
- readback attempt count/result/latency,
- retry eligibility allow/block counts,
- retry attempts and outcomes once Slice 4 exists,
- mismatch/manual review backlog,
- SLA breach count,
- config-blocked count,
- audit write failure count,
- projection publication lag,
- POS Server readback availability,
- fiscal-gated ExitAuthorization blocked count once enforcement is approved.

## 20. Test strategy and release gates

Test strategy by slice:

| Slice | Required tests/gates |
| --- | --- |
| 0 | Inventory doc reviewed; no behavior changes. |
| 1 | Persistence/intake unit and integration tests if schema/code changes are introduced; duplicate intake/collapse tests; no retry execution tests. |
| 2 | Readback client tests, classification tests, no POS mutation tests, audit write tests, matched/not_found/mismatch/failed/unavailable/unknown tests. |
| 3 | Retry eligibility allow/block tests; no scheduler execution; semantic hash/idempotency/readback prerequisite tests. |
| 4 | Controlled retry scheduler tests; same upstream finality/same semantic facts tests; conflict/timeout/unknown/audit unavailable tests. |
| 5 | Assignment/manual review/closure tests; no number editing/no manual document creation tests. |
| 6 | Operator Console RBAC/scope/read-only tests. |
| 7 | Dashboard projection/read-only/export/freshness tests. |
| 8 | Reconciliation handoff tests. |
| 9 | Fiscal gating integration tests only after explicit enforcement approval. |

Release gates:

- `dotnet test` relevant Central PMS unit tests.
- Central PMS integration tests for persistence slices.
- Contract tests for API slices where endpoint/DTO changes exist.
- `git diff --check`.
- No POS Server repo changes unless a separate Codex Z task approves them.
- No retry execution until readback and eligibility release gates pass.

## 21. Risks and blockers

| Risk/blocker | Mitigation |
| --- | --- |
| Schema design rushed before inventory | Make Slice 0 mandatory and require review before Slice 1 implementation. |
| Retry implemented before readback | Hard block; readback and eligibility must precede retry scheduler. |
| Existing fiscal reference model insufficient for FEQ | Use reviewed schema proposal; do not overload ambiguous fields. |
| POS Server readback identifiers insufficient | Inventory contract and involve Codex Z only if POS Server API/runtime work is required. |
| Audit pattern unclear | Inventory existing event/outbox/reconciliation audit patterns before implementing FEQ actions. |
| Operator Console becomes action authority | Keep actions as governed handoff only and RBAC scoped. |
| Dashboard becomes workflow authority | Keep Dashboard projection only. |
| Fiscal gating accidentally enabled | Keep fiscal-gated ExitAuthorization integration deferred and disabled by default. |

## 22. Open decisions

| ID | Decision | Status |
| --- | --- | --- |
| FEQ-IMPL-OQ-001 | FEQ persistence approach: embedded, separate case table, or hybrid. | Open |
| FEQ-IMPL-OQ-002 | Final FEQ lifecycle enum/state names. | Open |
| FEQ-IMPL-OQ-003 | Intake source hooks in Central PMS orchestration. | Open |
| FEQ-IMPL-OQ-004 | POS Server readback identifier strategy when fiscal document ID is unavailable. | Open |
| FEQ-IMPL-OQ-005 | Original request fact retention strategy and semantic hash comparison source. | Open |
| FEQ-IMPL-OQ-006 | Retry count/backoff/SLA values. | Open |
| FEQ-IMPL-OQ-007 | Audit/event type names and projection event contracts. | Open |
| FEQ-IMPL-OQ-008 | Operator Console permissions for request-readback/request-retry handoff. | Open |
| FEQ-IMPL-OQ-009 | Dashboard projection schema and refresh cadence. | Open |
| FEQ-IMPL-OQ-010 | Reconciliation closure ownership and FEQ evidence handoff. | Open |
| FEQ-IMPL-OQ-011 | Fiscal-gated ExitAuthorization enforcement enablement timing. | Deferred |

## 23. Proposed PR sequence

| PR | Branch recommendation | Scope |
| --- | --- | --- |
| PR 0 | `feature/central-pms-feq-inventory-and-constraints` | Inventory existing Central PMS fiscal issuance, reference, POS Server client, audit/outbox, reconciliation, tests, and constraints. |
| PR 1 | `feature/central-pms-feq-persistence-intake-plan` | Persistence/intake implementation plan or schema proposal; no retry execution. |
| PR 2 | `feature/central-pms-feq-persistence-intake` | Implement reviewed FEQ persistence/intake and duplicate collapse. |
| PR 3 | `feature/central-pms-feq-readback-worker` | Implement backend-only POS Server readback and classification. |
| PR 4 | `feature/central-pms-feq-retry-eligibility` | Implement eligibility evaluator only. |
| PR 5 | `feature/central-pms-feq-controlled-retry-scheduler` | Implement scheduler after readback/eligibility gates pass. |
| PR 6 | `feature/central-pms-feq-manual-review-closure` | Implement assignment, manual review, closure, and audit. |
| PR 7 | `feature/operator-console-feq-visibility` | Implement scoped Operator Console visibility/handoff. |
| PR 8 | `feature/management-dashboard-feq-projection` | Implement dashboard projection/reporting only. |
| PR 9 | `feature/central-pms-feq-reconciliation-handoff` | Implement reconciliation evidence handoff. |
| PR 10 | `feature/central-pms-fiscal-gated-exit-feq-integration` | Deferred until fiscal gating enforcement is approved. |

## 24. Codex task assignment matrix

| Workstream | Persona |
| --- | --- |
| ExitPass platform docs and orchestration | Codex v1.3 |
| Central PMS application/infrastructure/tests | Codex v1.3 |
| Operator Console FEQ visibility | Codex v1.3 |
| Management Dashboard FEQ projection | Codex v1.3 |
| Reconciliation/audit handoff planning | Codex v1.3 |
| POS Server runtime/API/database changes | Codex Z only, separate task |
| POS Server repo inspection for readback contract | Codex v1.3 for docs inspection; Codex Z for runtime/database/API tasks |

## 25. Acceptance criteria

| ID | Acceptance criterion |
| --- | --- |
| FEQ-PLAN-AC-001 | Slice order keeps readback before retry. |
| FEQ-PLAN-AC-002 | First implementation slice excludes retry execution. |
| FEQ-PLAN-AC-003 | FEQ authority boundaries are preserved. |
| FEQ-PLAN-AC-004 | Operator Console and Dashboard remain visibility/handoff surfaces. |
| FEQ-PLAN-AC-005 | POS Server ownership of fiscal issuance/numbering is preserved. |
| FEQ-PLAN-AC-006 | Central PMS ownership of payment finality, fiscal reference recording, and normal ExitAuthorization is preserved. |
| FEQ-PLAN-AC-007 | Persistence planning avoids unreviewed table/column claims. |
| FEQ-PLAN-AC-008 | Retry eligibility is separated from retry scheduler execution. |
| FEQ-PLAN-AC-009 | Audit/evidence planning is included before recovery actions. |
| FEQ-PLAN-AC-010 | Test and release gates are defined per slice. |

## 26. Review checklist

| Check | Status |
| --- | --- |
| Docs-only; no runtime changes. | ready_for_review |
| Slice 0 inventory exists. | ready_for_review |
| Slice 1 excludes retry execution. | ready_for_review |
| Readback precedes retry. | ready_for_review |
| Retry eligibility precedes scheduler. | ready_for_review |
| Central PMS authority preserved. | ready_for_review |
| POS Server authority preserved. | ready_for_review |
| Operator Console visibility boundary preserved. | ready_for_review |
| Dashboard projection boundary preserved. | ready_for_review |
| Manual release separation preserved. | ready_for_review |
| Test/release gates included. | ready_for_review |
