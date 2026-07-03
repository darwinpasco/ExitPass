# ExitPass Fiscal Exception Queue / Readback / Retry Design Review v1.0

## Document control

| Field | Value |
| --- | --- |
| Review target | `docs/v1.3/fiscal-exception-queue/ExitPass_Fiscal_Exception_Queue_Readback_Retry_Design_v1.0.md` |
| Version | v1.0 |
| Branch | `docs/v1.3-fiscal-exception-queue-readback-retry-design` |
| Review type | Documentation/design boundary review |
| Status | ready_for_review |
| Last updated | 2026-07-03 |

## Scope reviewed

The review covered fiscal exception queue purpose, system boundary, authority model, lifecycle, categories, readback strategy, retry strategy, idempotency/duplicate protection, unknown outcome recovery, mismatch handling, manual review, closure, reconciliation, fiscal-gated ExitAuthorization interaction, Operator Console handoff, Management Dashboard projection handoff, audit/evidence/security/privacy posture, failure modes, roadmap, acceptance criteria, traceability, and the seven FEQ diagrams.

## Files inspected

| File | Purpose |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 fiscal-before-exit, fail-closed, exception, audit, and authority baseline. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System authority model and component responsibilities. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` | Operator Console read-only fiscal visibility and FEQ handoff. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_System_Design_SDD_v1.0.md` | Dashboard read-only projection posture and deferral of retry/readback/writeback mechanics. |
| `docs/v1.3/central-pms/engineering-pack/08-operator-console-queues-plan.md` | Fiscal exception queue categories and handoff candidates. |
| `docs/v1.3/central-pms/engineering-pack/09-dashboard-visibility-plan.md` | Dashboard fiscal visibility and retry/readback reporting posture. |
| `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Post_Run_Review_v1.0.md` | Controlled UAT outcome and deferred FEQ work. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | POS Server fiscal authority and failure/retry posture. |
| `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md` | Central PMS integration rules, idempotency, readback, retry, and gating. |
| `docs/v1.3/pos-server-api/ExitPass_POS_Server_API_Contract_v1.0.md` | POS Server API contract, semantic request hash, GET readback, error posture, and authority boundaries. |
| `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md` | Manual release, continuity, and post-restoration separation. |

## Boundary checks

| Boundary | Result |
| --- | --- |
| Central PMS owns payment finality, fiscal reference recording, and normal ExitAuthorization. | Passed |
| POS Server owns fiscal issuance and fiscal numbering only. | Passed |
| FEQ coordinates recovery but does not become fiscal/payment/exit/gate authority. | Passed |
| Operator Console remains visibility/governance handoff only. | Passed |
| Management Dashboard remains visibility/reporting only. | Passed |
| Gate/Exit cannot bypass Central PMS authorization. | Passed |
| Manual release remains separate from normal ExitAuthorization. | Passed |

## Fiscal authority check

Passed. FEQ does not own fiscal issuance or numbering. It can orchestrate approved backend readback/retry only when safety, idempotency, and audit controls pass.

## Central PMS authority check

Passed. Central PMS remains owner of payment finality, fiscal reference recording, and normal ExitAuthorization. FEQ updates fiscal reference outcomes only through approved Central PMS workflow/state machine.

## POS Server authority check

Passed. POS Server remains fiscal issuance and numbering authority. The design prohibits fiscal number editing, arbitrary manual document creation, and direct UI mutation of POS Server records.

## Operator Console handoff check

Passed. Operator Console consumes assigned/scoped exception visibility and governed handoff only. It does not call POS Server directly or trigger arbitrary retry.

## Management Dashboard handoff check

Passed. Management Dashboard receives projections only and cannot trigger readback, retry, writeback, closure, manual release, ExitAuthorization, or gate execution.

## Readback safety check

Passed. Readback is non-mutating, backend-only, audited, and required before retry for unknown outcomes.

## Retry safety/idempotency check

Passed. Retry is allowed only with original request availability, stable semantic request hash, understood idempotency behavior, no matching readback result, valid configuration, audit readiness, retry limit allowance, and authorization.

## Unknown outcome check

Passed. Unknown outcomes require readback first. Matching readback results are reconciled instead of retried. Mismatch/failed/unavailable/unknown readback routes to manual review or later readback.

## Mismatch/manual review check

Passed. Mismatch cases require assignment, role/scope authorization, reason code, evidence review, audit logging, and controlled closure. Manual review cannot fabricate documents or edit fiscal numbers.

## Fiscal-gated ExitAuthorization check

Passed. FEQ can explain fiscal-prerequisite block state but does not issue ExitAuthorization. Normal ExitAuthorization remains Central PMS authority.

## Manual release/continuity separation check

Passed. Manual release and continuity evidence can be linked to FEQ cases, but manual release is not normal ExitAuthorization and FEQ does not approve manual release or open gates.

## Audit/evidence/privacy check

Passed. The design requires audit for intake, assignment, readback, retry, manual review, closure, evidence access, and projection updates. Sensitive evidence is minimized and RBAC-scoped.

## Diagram review

| Diagram | Source | Rendered JPEG | Review result |
| --- | --- | --- | --- |
| FEQ-D01 Fiscal Exception Queue System Context | `diagrams/FEQ-D01_Fiscal_Exception_Queue_System_Context.puml` | `diagrams/FEQ-D01_Fiscal_Exception_Queue_System_Context.jpg` | Shows FEQ as recovery coordinator between Central PMS and POS Server with visibility handoff to Operator Console/Dashboard. |
| FEQ-D02 Fiscal Exception Authority Boundary | `diagrams/FEQ-D02_Fiscal_Exception_Authority_Boundary.puml` | `diagrams/FEQ-D02_Fiscal_Exception_Authority_Boundary.jpg` | Preserves Central PMS/POS Server authority and FEQ non-authority posture. |
| FEQ-D03 Fiscal Exception Lifecycle State Model | `diagrams/FEQ-D03_Fiscal_Exception_Lifecycle_State_Model.puml` | `diagrams/FEQ-D03_Fiscal_Exception_Lifecycle_State_Model.jpg` | Captures candidate lifecycle without final enum/table claims. |
| FEQ-D04 Readback Sequence | `diagrams/FEQ-D04_Readback_Sequence.puml` | `diagrams/FEQ-D04_Readback_Sequence.jpg` | Shows backend-only POS Server readback and audit. |
| FEQ-D05 Retry Sequence | `diagrams/FEQ-D05_Retry_Sequence.puml` | `diagrams/FEQ-D05_Retry_Sequence.jpg` | Shows eligibility, idempotency, negative paths, and durable Central PMS recording. |
| FEQ-D06 Mismatch Manual Review Closure Flow | `diagrams/FEQ-D06_Mismatch_Manual_Review_Closure_Flow.puml` | `diagrams/FEQ-D06_Mismatch_Manual_Review_Closure_Flow.jpg` | Shows manual review without fiscal document fabrication or numbering mutation. |
| FEQ-D07 Fiscal Gated ExitAuthorization Interaction | `diagrams/FEQ-D07_Fiscal_Gated_ExitAuthorization_Interaction.puml` | `diagrams/FEQ-D07_Fiscal_Gated_ExitAuthorization_Interaction.jpg` | Preserves Central PMS ExitAuthorization authority and manual release separation. |

Diagram review result: passed.

## Gaps or open decisions

- Exact FEQ persistence model and table/column names.
- Exact queue state enum names.
- Exact endpoint paths and DTOs.
- POS Server readback identifier strategy by runtime endpoint.
- Retry count, backoff, SLA thresholds, and retry exhaustion policy.
- Dual-control policy for production fiscal-number-impacting cases.
- Operator Console action permissions for assignment/readback/retry request.
- Management Dashboard projection schema and refresh cadence.
- Reconciliation closure integration and ownership.
- Fiscal-gated ExitAuthorization production enablement policy.
- Manual release association and closure policy.

## Decision

Decision: ready_for_review.

The design is complete for review and preserves FEQ as a controlled fiscal exception recovery coordinator, not a payment, POS fiscal numbering, ExitAuthorization, gate, entitlement, dashboard, or Operator Console authority.
