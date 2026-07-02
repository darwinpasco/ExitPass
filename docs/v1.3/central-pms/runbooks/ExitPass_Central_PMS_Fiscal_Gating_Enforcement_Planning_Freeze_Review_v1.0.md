# ExitPass Central PMS Fiscal Gating Enforcement Planning Freeze Review v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS Fiscal Gating Enforcement Planning Freeze Review |
| Version | v1.0 |
| Date | 2026-07-02 |
| Status | Planning freeze review |
| Branch | `feature/central-pms-exitauthorization-fiscal-gating-enforcement-planning-freeze-review` |
| Scope | Documentation/review only before any production fiscal-before-ExitAuthorization blocking branch |

## 2. Purpose and Scope

This review freezes the current planning baseline before any future production blocking branch is considered for fiscal-before-ExitAuthorization enforcement.

The review confirms:

- what has been implemented in Central PMS;
- what remains non-enforcing;
- which prerequisites are complete;
- which prerequisites remain incomplete;
- whether the project is ready for production blocking enforcement;
- what must happen next before enforcement.

This document does not implement enforcement, change ExitAuthorization behavior, call POS Server, introduce workers, or add operational UI/reporting.

## 3. Authority Boundaries

The following authority boundaries remain unchanged:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Operator Console remains governance/review only.
- Management Dashboard remains visibility/reporting only.
- Manual release is not normal ExitAuthorization.

## 4. Current Implementation Baseline

The reviewed Central PMS baseline includes the following completed fiscal-gating-adjacent slices:

- fiscal reference persistence state;
- fiscal reference DB harness/repository tests;
- fiscal issuance orchestration shell;
- POS Server client abstraction and request mapper;
- success/replay handling;
- failure/`errorPosture` handling;
- unknown/readback planning hooks;
- fiscal gating dry-run evaluator;
- shadow observability;
- fiscal reference context lookup for shadow evaluation;
- structured shadow audit/event evidence;
- feature-flag/readiness scaffolding;
- future enforcement decision contract;
- future enforcement decision shadow evidence;
- pre-enforcement UAT/preflight coverage;
- rollout runbook.

The current implementation remains preparatory. It can model fiscal readiness and emit shadow evidence, but it is not a production enforcement implementation.

## 5. Implemented Central PMS Slices Reviewed

| Slice | Review Finding |
| --- | --- |
| Fiscal reference persistence state | Central PMS has a storage foundation for fiscal issuance reference and state evidence. |
| DB harness/repository tests | Fiscal reference persistence has disposable DB/repository test coverage. |
| Orchestration shell | Central PMS can prepare and transition local fiscal issuance states without live POS Server calls. |
| POS Server client abstraction and mapper | Request/response mapping abstractions exist, but payment and exit flows do not use live POS Server calls. |
| Success/replay handling | Parsed success and replay results can be recorded into fiscal reference state. |
| Failure/`errorPosture` handling | Parsed conflicts and failures can be mapped into fiscal exception states. |
| Unknown/readback planning hooks | Unknown outcome and readback planning state can be represented without a live worker. |
| Dry-run gating evaluator | Fiscal state can be evaluated for normal ExitAuthorization readiness without enforcement. |
| Shadow observability | ExitAuthorization path can observe fiscal gating readiness without changing the outcome. |
| Fiscal context lookup | Existing fiscal references can be looked up for shadow evaluation when local context exists. |
| Structured shadow audit/event evidence | Shadow observations emit structured evidence for readiness and future decision outcomes. |
| Feature-flag/readiness scaffolding | Enforcement is default-off and readiness-only. Blocking is not wired. |
| Future enforcement decision contract | Future allow/block decision semantics are modeled separately from production behavior. |
| Preflight/UAT coverage | Tests and checklist coverage exist for default-off posture and shadow evidence. |
| Rollout runbook | Operational rollout, rollback, monitoring, approvals, and go/no-go controls are documented. |

## 6. Implemented POS Server Runtime Baseline Reviewed

The POS Server runtime baseline was reviewed read-only from the `dev` branch of `D:\SourceCodes\ExitPass-PoSServer`.

Reviewed runtime planning slices:

- fiscal issuance idempotency;
- fiscal policy identity resolution;
- fiscal sequence allocation;
- fiscal issuance response/status hardening.

The reviewed POS Server baseline establishes:

- numbered fiscal issuance ownership in POS Server;
- idempotency based on stable upstream finality reference semantics;
- fiscal identity, policy, and sequence allocation behavior;
- response/status fields for Central PMS evidence recording;
- fail-closed response posture for incomplete fiscal number assignment.

This review does not certify a live Central PMS-to-POS Server integration path. The Central PMS payment and ExitAuthorization flows still do not call POS Server.

## 7. Non-Enforcing Status Confirmation

Current status is non-enforcing.

Confirmed:

- production ExitAuthorization behavior remains unchanged;
- `IssueExitAuthorizationHandler` has no production fiscal blocking branch;
- enforcement default is OFF;
- `EnforcementWiredForBlocking = false`;
- shadow evaluation may observe and emit diagnostics only;
- no live POS Server calls are made from payment confirmation or ExitAuthorization flows;
- no retry scheduler is implemented;
- no GET readback worker is implemented;
- no Operator Console fiscal exception queues are implemented;
- no Management Dashboard fiscal visibility projections are implemented.

## 8. Current Feature Flag/Readiness Posture

Current feature flag/readiness posture:

- `EnableFiscalBeforeExitAuthorizationEnforcement = false` by default;
- `EnableShadowEvaluation = true`;
- `ReadinessMode = readiness_only`;
- `EnforcementWiredForBlocking = false`;
- future enforcement decisions can be computed as shadow-only evidence;
- the production handler must not use the computed decision to block ExitAuthorization until a future approved enforcement branch explicitly wires blocking behavior.

This posture is appropriate for pre-enforcement observation, but not sufficient to enable production blocking.

## 9. Shadow Evidence Readiness

Shadow evidence readiness is partially complete.

Available evidence classes:

- `evaluated_ready`;
- `evaluated_blocked`;
- `not_evaluated_missing_fiscal_context`;
- `evaluation_failed_non_blocking`;
- future decision `allow`;
- future decision `block`;
- future decision `not_required_by_policy`;
- future decision `exception_release_only`;
- future decision `manual_review_required`;
- future decision `not_evaluable`.

The shadow evidence path is suitable for pre-enforcement observation. It is not yet supported by a completed production observation window, site-level readiness record, Operator Console exception workflow, or Dashboard projection.

## 10. Preflight/UAT Evidence Readiness

Preflight coverage exists for:

- enforcement default-off posture;
- blocking not wired;
- current ExitAuthorization behavior unchanged;
- shadow decision evidence emitted for major decision classes;
- readiness/decision outcomes;
- safe payload posture;
- no POS Server live calls;
- no retry/readback worker.

Remaining UAT evidence still required before enforcement:

- production-like shadow observation window results;
- Site/Site POS Server rollout evidence;
- live POS Server connectivity and API smoke evidence in the intended environment;
- fiscal reference persistence and lookup evidence for pilot Site traffic;
- operational support and manual exception rehearsal evidence;
- business/compliance owner sign-off.

## 11. Runbook Readiness

The rollout runbook exists and covers:

- authority boundaries;
- current implementation baseline;
- non-goals;
- rollout principles;
- feature flag posture;
- rollout phases;
- pre-production readiness;
- Site and Site POS Server readiness;
- POS Server readiness;
- Central PMS fiscal reference readiness;
- shadow and future decision evidence review;
- operational go/no-go criteria;
- rollback;
- manual exception/release procedure;
- monitoring and alerting;
- communications;
- production enablement approvals;
- post-enablement review.

Runbook readiness is complete as a planning artifact. Operational execution evidence is not yet complete.

## 12. POS Server Readiness Assessment

Planning/runtime baseline readiness is partially complete.

Complete or available:

- POS Server fiscal issuance responsibility is documented;
- idempotency behavior is documented;
- fiscal identity/policy/sequence behavior is documented;
- response/status contract supports Central PMS evidence recording;
- GET readback contract exists for future reconciliation flows.

Incomplete before enforcement:

- live Central PMS environment connectivity to POS Server has not been validated in this freeze review;
- POST `/v1/fiscal-documents/` smoke evidence for the target enforcement environment is not recorded here;
- GET `/v1/fiscal-documents/{fiscalDocumentId}` smoke evidence is not recorded here;
- Site POS Server mapping and fiscal identity/policy/sequence readiness are not collected for the pilot Site;
- failure/replay/conflict behavior has not completed an operational observation window with Central PMS.

## 13. Central PMS Readiness Assessment

Central PMS is ready for continued shadow/readiness work.

Central PMS is not ready for production blocking enforcement.

Ready:

- fiscal reference persistence foundation exists;
- fiscal state taxonomy and exception mappings exist;
- orchestration shell exists;
- POS Server client abstraction and request mapper exist;
- parsed success/replay/failure/unknown results can be represented;
- dry-run gating evaluator exists;
- shadow evaluation and structured evidence exist;
- future enforcement decision contract exists;
- enforcement defaults off and blocking is not wired.

Not ready:

- live fiscal issuance call path is not wired into an enabled orchestration flow;
- retry scheduler is not implemented;
- GET readback worker is not implemented;
- Operator Console fiscal exception queues are not implemented;
- Management Dashboard fiscal visibility projections are not implemented;
- operational go/no-go evidence is not complete;
- production shadow observation window is not complete.

## 14. Site / Site POS Server Readiness Assessment

Site and Site POS Server readiness is not complete for production blocking enforcement.

Required evidence still missing:

- pilot Site selection;
- active Site configuration confirmation;
- Site POS Server configuration confirmation;
- Site-to-Site POS Server mapping;
- channel mapping for cashier-assisted terminal, APM, WebPay, and continuity contexts where applicable;
- fiscal identity, sequence policy, and sequence state readiness for the pilot Site;
- Site rollout owner and rollback contact;
- operations/support training confirmation;
- accepted thresholds for missing fiscal context and shadow blocked rates.

## 15. Missing Prerequisites Before Enforcement

The following prerequisites must be completed before a production blocking branch is approved:

- live POS Server call path from Central PMS orchestration must be implemented behind disabled configuration first;
- live call path must remain outside payment/exit production flow until separately approved;
- retry scheduler strategy must be implemented or explicitly deferred with operational compensating controls;
- GET readback worker or equivalent reconciliation path must be implemented or explicitly deferred with approved manual reconciliation controls;
- Operator Console fiscal exception queue plan/contract must be completed before operations must manage blocked cases;
- Management Dashboard fiscal visibility projections must be planned or implemented for rollout monitoring;
- pilot Site/Site POS Server readiness evidence must be collected;
- production shadow observation window must be completed;
- operational go/no-go checklist must be signed;
- rollback procedure must be rehearsed;
- manual exception/release procedure must be approved;
- business, compliance/accounting, operations, POS Server owner, and engineering approvals must be recorded.

## 16. Enforcement Readiness Decision

Decision: not ready for production blocking enforcement yet.

Rationale:

- Central PMS has not yet wired a live POS Server call path into fiscal issuance orchestration.
- Payment confirmation and ExitAuthorization flows still do not perform live fiscal issuance.
- Retry and GET readback workers are not implemented.
- Operator Console fiscal exception queues are not implemented.
- Management Dashboard fiscal visibility projections are not implemented.
- Site/Site POS Server rollout evidence is not collected.
- Production shadow observation window evidence is not complete.
- Required operational approvals are not recorded.

The project should remain in shadow/readiness mode until the missing prerequisites are closed.

## 17. Blockers

Blocking items before any production enforcement branch:

- no live Central PMS fiscal issuance call path is available for controlled disabled integration;
- no automated retry scheduler exists for recoverable fiscal issuance failures;
- no GET readback worker exists for unknown outcomes;
- no operator exception queue exists for fiscal blocked/manual review cases;
- no dashboard projection exists for enforcement rollout monitoring;
- no pilot Site readiness evidence is attached to this review;
- no production shadow observation window summary is attached to this review;
- no signed go/no-go approval record is attached to this review.

## 18. Risks

Primary risks if enforcement is introduced before blockers are resolved:

- valid paid parking sessions could be blocked from normal ExitAuthorization due to fiscal context gaps;
- unknown POS Server outcomes could lack timely readback/reconciliation;
- recoverable service failures could accumulate without automated retry handling;
- operators could lack a governed queue for exception resolution;
- support teams could lack visibility into blocked fiscal states;
- incomplete Site POS Server configuration could cause unnecessary blocks;
- manual release could be used without sufficient audit/reconciliation discipline;
- business and compliance owners could lack evidence needed to accept rollout posture.

## 19. Required Approvals Before Enforcement

Required approvals before any production blocking implementation can be enabled:

- product/business owner approval;
- operations lead approval;
- support/helpdesk readiness approval;
- compliance/accounting owner approval;
- POS Server owner approval;
- Central PMS engineering owner approval;
- Site rollout owner approval for each pilot Site;
- rollback owner confirmation;
- manual exception/release policy approval;
- production change approval through the applicable release process.

## 20. Recommended Next Branch/Task

Recommended next branch:

`feature/central-pms-pos-server-live-call-disabled-integration`

Purpose:

Wire the live POS Server client into fiscal issuance orchestration behind disabled configuration only, not into payment confirmation or ExitAuthorization production flow.

Reason:

This is the next technical prerequisite for enforcement readiness. It lets Central PMS prove the live client/orchestration integration path safely while preserving the current non-enforcing payment-to-exit behavior. Enforcement should not be introduced directly from the current baseline.

Alternative operations-focused branch:

`feature/central-pms-fiscal-exception-operator-queue-planning`

Purpose:

Start Operator Console fiscal exception queue planning/contract before enforcement.

This alternative is useful if operations readiness is prioritized before live integration hardening.

## 21. Requirements Traceability Summary

| Requirement Area | Traceability |
| --- | --- |
| Authority boundaries | Preserved in this review and inherited from the Central PMS/POS Server integration contract. |
| Fiscal reference persistence | Covered by database delta plan, implementation slice plan, persistence state, and repository tests. |
| Fiscal state taxonomy | Covered by state transition and exception taxonomy plan. |
| POS Server evidence contract | Covered by POS Server API contract and Central PMS integration contract. |
| ExitAuthorization gating | Covered by engineering pack gating plan, dry-run evaluator, shadow observability, readiness scaffolding, and future decision contract. |
| Audit/correlation | Covered by audit/events plan and structured shadow evidence implementation. |
| UAT/preflight | Covered by test/UAT plan, preflight checklist, and rollout runbook. |
| Enforcement rollout | Covered by rollout runbook and this freeze review. |
| Current decision | Not ready for production blocking enforcement yet. |

