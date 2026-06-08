# Operator Console Shift/Site Validation Workflow Design v1

## Purpose

This document defines the production shift/site validation workflow design for the ExitPass Operator Console.

It follows the statutory discount pilot-readiness sign-off, the production readiness gap review, the UX flow hardening slice, and the device enrollment/readiness design. Device trust and shift/site readiness are coupled controls: a trusted device is not sufficient unless the operator, shift, site, and site group also match the controlled action.

Local/dev fallback context is acceptable only for sandbox validation. It must not be treated as production trust.

## Scope

In scope:

- Operator shift validation.
- Operator site assignment validation.
- Site group scoping.
- Device-site consistency.
- Operator-device consistency.
- Shift lifecycle.
- Access evaluation integration.
- Denial reasons.
- UX states.
- Supervisor support and escalation.
- Audit trail.
- Operational readiness.

Out of scope:

- Payment provider routing.
- AUB.
- WebPay.
- Coupon validation.
- Reconciliation.
- HikCentral/gate implementation.
- Raw evidence, OCR, or automated ID validation.
- Final implementation code.

## Source Artifacts Reviewed

Found and reviewed:

- `docs/operator-console/OperatorConsole_Device_Enrollment_Readiness_Design_v1.md`
- `docs/operator-console/OperatorConsole_Production_Readiness_Gap_Review_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md`
- `src/Services/OperatorConsoleUi/src/**`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/**`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/**`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/**`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.UnitTests/Application/OperatorConsoleAccessEvaluationServiceTests.cs`
- `src/Services/CentralPms/tests/ExitPass.CentralPms.IntegrationTests/Api/OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests.cs`
- `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`
- `docs/operator-console/operator-console-db-patch-validation.md`

Standalone Operator Console BRD, ExitPass BRD v1.2, ExitPass API Contract Pack v1.2, and ExitPass Engineering Pack v1.2 were not found in the repository by filename search. This design uses repo-available artifacts only and does not invent requirements from unavailable packs.

## Current State Summary

The current Operator Console validation path uses deterministic sandbox fixture IDs for operator user, device binding, shift, site, and site group during local validation.

The pilot validation used local/dev fallback headers or context. The Operator Console UI currently provides deterministic local defaults for operator user, device binding, shift, site, and site group so the sandbox validation can run against an empty local database fixture.

The backend access evaluation service can process operator context and return allow/deny decisions for Operator Console statutory discount actions. It already models denial reasons for missing or inactive identity mapping, missing or inactive device binding, untrusted device, invalid device assignment, missing active shift, revoked shift, active takeover, unsupported workflow, and unsupported action.

The current access evaluation read repository reads `identity.users` and `sites.sites`, then synthesizes operator identity, active trusted device binding, device assignment, and active shift context from the request context. It does not yet read real production operator shift, device binding, device assignment history, revocation, takeover, or access evaluation tables.

Repo schema artifacts include an `operator_console` schema patch with tables and enums for HR identity mappings, operator shifts, shift versions, shift revocations, shift takeovers, operator device bindings, device assignment history, access evaluations, and access evaluation reasons. The local baseline observed during the #235A fixture work did not have `operator_console.*` tables available. The patch validation artifact is non-production validation evidence and does not by itself make production shift/site workflow operational.

Shift/site readiness remains a production rollout blocker.

## Production Access Prerequisites

Before an operator can perform controlled Operator Console actions in production, all of these prerequisites must pass:

- Authenticated operator.
- Authorized Operator Console role.
- Trusted and enrolled active device.
- Active shift.
- Shift assigned to the same site and site group as the action context.
- Operator assigned to the requested site or allowed site group.
- Device assigned to the same site and site group as the action context.
- Operator/device consistency where operator-bound devices are used.
- Action allowed for the operator role and current workflow state.
- Correlation and audit context present.

Failure of any required prerequisite denies controlled actions. Denial must be explicit, auditable, and surfaced to the UI with a stable reason code and operator-safe message.

## Shift Lifecycle Model

Required production shift states:

- `SCHEDULED`: Shift exists but is not yet active.
- `ACTIVE`: Operator is currently authorized to perform controlled actions for the shift site.
- `PAUSED`: Operator is temporarily unavailable; controlled write actions are denied unless policy explicitly allows limited read-only operations.
- `ENDED`: Shift has been clocked out or ended.
- `SUSPENDED`: Shift was suspended by an authorized supervisor or operations actor.
- `CANCELLED`: Shift was cancelled before or during planned use.

Allowed transitions:

| Transition | From | To | Actor |
| --- | --- | --- | --- |
| Schedule shift | None | `SCHEDULED` | Supervisor, workforce admin, or authorized operations actor |
| Clock in | `SCHEDULED` | `ACTIVE` | Assigned operator, supervisor-assisted operator |
| Pause | `ACTIVE` | `PAUSED` | Assigned operator or supervisor |
| Resume | `PAUSED` | `ACTIVE` | Assigned operator or supervisor |
| Clock out / end | `ACTIVE`, `PAUSED` | `ENDED` | Assigned operator or supervisor |
| Supervisor suspend | `SCHEDULED`, `ACTIVE`, `PAUSED` | `SUSPENDED` | Supervisor or operations admin |
| Supervisor cancel | `SCHEDULED`, `ACTIVE`, `PAUSED` | `CANCELLED` | Supervisor or operations admin |
| Supervisor manual start with justification | `SCHEDULED` | `ACTIVE` | Supervisor or operations admin |
| Supervisor manual end with justification | `ACTIVE`, `PAUSED` | `ENDED` | Supervisor or operations admin |

The schema patch also defines states such as `REVOKED`, `TAKEN_OVER`, and `IMPORT_CONFLICT` for operational events. Production readiness should map those states into the access rules before implementation. A revoked or taken-over shift must not authorize ordinary operator controlled actions.

## Site Assignment Model

The production model should support these concepts:

- Operator home site: the default operating site for a regular operator.
- Operator allowed site group: a broader scope where the operator may be assigned temporarily or permanently.
- Temporary site assignment: time-bounded authorization for a site outside the home site.
- Supervisor multi-site oversight: broader read/review authority configured by role and site group.
- Site-shared device: a trusted device assigned to a site, usable by any authorized active operator at that site if policy allows.
- Operator-bound device: a trusted device bound to one operator, one site, or both.
- Site mismatch: requested action site does not match operator, shift, or device context.
- Site group mismatch: requested action site group does not match the operator, shift, or device context.
- Cross-site denial: controlled action is denied because the operator, shift, device, or action context crosses configured site boundaries.

Site assignment should be evaluated at the narrowest required scope. A site-level controlled statutory discount action should require a same-site match unless a supervisor review-only scope or explicit temporary assignment permits broader access.

## Access Evaluation Rules

Deterministic production rules:

- Missing operator user ID denies access.
- Unrecognized operator denies access.
- Inactive, suspended, revoked, expired, or superseded operator identity mapping denies access.
- Missing required role denies access.
- Missing device ID denies controlled write actions.
- Unenrolled device denies controlled actions.
- Inactive, suspended, revoked, lost, expired, or retired device denies controlled actions.
- Untrusted device denies controlled actions.
- Missing device assignment denies controlled actions.
- Device site mismatch denies controlled actions.
- Missing shift ID denies controlled actions.
- Missing active shift denies controlled actions.
- Non-current shift denies controlled actions.
- Revoked, suspended, cancelled, ended, taken-over, or import-conflict shift denies controlled actions.
- Shift site mismatch denies controlled actions.
- Missing site ID denies controlled actions.
- Missing site group ID denies controlled actions when site group scoping is required.
- Operator not assigned to the site denies controlled actions.
- Operator not assigned to the site group denies site-group scoped controlled actions.
- Operator-bound device used by a different operator denies controlled actions.
- Supervisor can access broader read/review scope only if configured.
- Compliance auditor can read only and cannot perform decision or apply actions.
- Action must be allowed for both role and workflow state.
- Local/dev fallback context denies controlled actions in production.

## Action-to-Readiness Matrix

| Action | Operator role required | Device trust required | Active shift required | Site match required | Supervisor allowed? | Auditor allowed? | Denial behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Session lookup | Operator or supervisor | Required for production controlled lookup | Required for operator use | Yes | Yes, if scoped | Read-only if configured | Deny with readiness reason; do not expose unrelated sessions |
| Policy resolution | Operator or supervisor | Required | Required | Yes | Yes, if scoped | Read-only if configured | Deny before policy details are returned |
| Draft creation | Operator or supervisor | Required | Required | Yes | Yes, if configured for workflow | No | Deny write action and preserve correlation ID |
| Evidence capture | Operator or supervisor | Required | Required | Yes | Yes, if configured for workflow | No | Deny capture; do not accept raw evidence or metadata |
| Approval/rejection | Operator approver or supervisor | Required | Required | Yes | Yes, if configured | No | Deny decision and show workflow or readiness reason |
| Apply-payable-basis | Operator approver or supervisor | Required | Required | Yes | Yes, if configured | No | Deny apply action; do not create tariff mutation |
| Final verification/read model | Operator, supervisor, or auditor | Required for operator/supervisor controlled access | Required for operator workflow view | Yes | Yes, if scoped | Yes, read-only | Deny or limit to scoped read-only view |
| Supervisor review | Supervisor | Required unless back-office policy allows non-device review | Required if supervisor is acting as floor operator | Site group or assigned scope | Yes | Read-only if configured | Deny if supervisor scope is not configured |
| Override, if future | Supervisor or operations admin | Required | Required unless break-glass policy says otherwise | Yes or explicit scoped override | Yes | No | Deny by default; require justification if allowed later |
| Audit/report view, if future | Supervisor, operations, compliance auditor | Required for console access unless back-office path differs | Not required for historical reports if policy allows | Scoped by role | Yes | Yes, read-only | Deny or filter outside scope |

## Denial Reason Catalog

The following stable reason codes should be considered for the production contract. They are design candidates and are not implemented by this document.

| Reason code | Meaning |
| --- | --- |
| `OPERATOR_ID_MISSING` | Request did not include a production operator identity. |
| `OPERATOR_NOT_FOUND` | Operator identity could not be resolved. |
| `OPERATOR_INACTIVE` | Operator identity exists but is inactive, suspended, revoked, expired, or superseded. |
| `ROLE_NOT_ALLOWED` | Operator lacks the required role for the requested action. |
| `DEVICE_ID_MISSING` | Request did not include a production device identity. |
| `DEVICE_NOT_ENROLLED` | Device is not enrolled or has no active binding. |
| `DEVICE_NOT_ACTIVE` | Device is suspended, revoked, lost, expired, retired, or otherwise inactive. |
| `DEVICE_SITE_MISMATCH` | Device assignment does not match the requested site or site group. |
| `SHIFT_ID_MISSING` | Request did not include an active shift identity. |
| `SHIFT_NOT_FOUND` | Shift could not be resolved. |
| `SHIFT_NOT_ACTIVE` | Shift is not active or current for controlled actions. |
| `SHIFT_SITE_MISMATCH` | Shift site or site group does not match the requested action context. |
| `SITE_ID_MISSING` | Request did not include a site identity. |
| `SITE_GROUP_ID_MISSING` | Request did not include a required site group identity. |
| `OPERATOR_SITE_NOT_ALLOWED` | Operator is not assigned to the requested site or site group. |
| `ACTION_NOT_ALLOWED_FOR_ROLE` | Role does not permit the requested action. |
| `ACTION_NOT_ALLOWED_FOR_WORKFLOW_STATE` | Workflow state does not permit the requested action. |
| `LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION` | Request relied on sandbox/local fallback context in a production environment. |

The implementation should also map existing service reasons, such as `HR_IDENTITY_MAPPING_NOT_FOUND`, `DEVICE_BINDING_NOT_FOUND`, `DEVICE_NOT_TRUSTED`, `DEVICE_SITE_ASSIGNMENT_INVALID`, `NO_ACTIVE_SHIFT`, `SHIFT_REVOKED`, and `SHIFT_TAKEOVER_ACTIVE`, to operator-facing messages without exposing raw backend internals.

## UX Implications

Required Operator Console UX states:

- Ready for controlled actions.
- No active shift.
- Wrong site.
- Device not trusted.
- Operator not authorized.
- Supervisor review-only.
- Auditor read-only.
- Degraded/local-dev mode indicator.
- Access denied with clear reason.
- Escalation/support instruction.

Required UX guardrails:

- Statutory discount workflow must not start unless shift, site, and device readiness pass in production.
- Denied state must show the reason and the next action, such as clock in, change site, contact supervisor, or use an enrolled device.
- UI must never expose raw internal stack traces or raw policy failures to operators.
- UI must preserve and display the audit correlation ID for support.
- Read-only users must not see enabled decision, evidence capture, or payable-basis apply controls.
- Supervisor and auditor modes must be visually distinct from ordinary operator workflow mode.
- Local/dev mode must be visibly marked outside production and blocked in production.

## Supervisor and Escalation Workflow

Supervisor support should follow these rules:

- Supervisor can start, end, pause, resume, suspend, or cancel a shift only if authorized by role and site scope.
- Supervisor can reassign an operator or device only through an approved process.
- Temporary site assignment requires supervisor or operations approval and an expiry.
- Override requires justification.
- Break-glass access, if allowed later, must be audited, time-limited, scoped, and reviewed.
- Support escalation must not bypass backend access evaluation.
- Support staff should use the correlation ID and denial reason to diagnose readiness failures.
- Supervisor actions that alter shift/site/device readiness should create audit records before the operator retries the controlled action.

## Audit and Evidence Requirements

The production workflow must log:

- Shift start, end, pause, resume, suspend, and cancel.
- Supervisor manual start/end with justification.
- Access evaluation result.
- Denial reasons.
- Device/site/operator mismatch.
- Supervisor intervention.
- Temporary site assignment.
- Local/dev fallback use.
- Correlation ID.
- Affected site and site group.
- Action attempted.
- Decision outcome.
- Actor identity and role.
- Shift/device/operator context used for the decision.

Audit records should not store raw credentials, private keys, raw evidence bytes, raw ID numbers, or unnecessary personal data.

## Security and Privacy Requirements

- Local/dev fallback must be disabled or blocked in production.
- Production access must not be granted based only on request headers.
- Raw credentials and private keys must not be stored in logs or database fields.
- Logs should avoid personal data beyond necessary operator identity references.
- Device trust does not bypass statutory discount evidence gating.
- Shift readiness does not bypass statutory discount approval or payable-basis controls.
- Supervisor override, if implemented later, must not bypass statutory discount evidence and payable-basis invariants.
- Device identity is not payment authority and must not imply payment, exit authorization, coupon, reconciliation, HikCentral, or gate authority.

## Schema/API Design Considerations

Repo artifacts indicate that `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql` defines candidate `operator_console` tables and enums for production access evaluation, including:

- `operator_console.hr_identity_mappings`
- `operator_console.operator_shifts`
- `operator_console.operator_shift_versions`
- `operator_console.shift_revocations`
- `operator_console.shift_takeovers`
- `operator_console.operator_device_bindings`
- `operator_console.operator_device_assignment_history`
- `operator_console.operator_access_evaluations`
- `operator_console.operator_access_evaluation_reasons`

The current access evaluation read repository does not yet read those tables. It reads `identity.users` and `sites.sites`, then synthesizes active operator identity, trusted device binding, active device assignment, and active shift read models when context IDs are present. That is acceptable for local validation support but is not production shift/site validation.

Existing `identity.users` and `sites.sites` can support interim identity and site lookup, but they are not sufficient by themselves for production shift, device, and site assignment readiness.

Potential future entities:

- `operator_console.operator_shifts`
- `operator_console.operator_shift_events`
- `operator_console.operator_site_assignments`
- `operator_console.operator_access_evaluations`
- `operator_console.operator_access_evaluation_reasons`

Endpoint candidates, not implemented here:

- `POST /v1/ops/operator-console/shifts/clock-in`
- `POST /v1/ops/operator-console/shifts/{shiftId}/clock-out`
- `POST /v1/ops/operator-console/shifts/{shiftId}/pause`
- `POST /v1/ops/operator-console/shifts/{shiftId}/resume`
- `GET /v1/ops/operator-console/access/readiness`
- `GET /v1/ops/operator-console/operators/{operatorId}/site-assignments`

The next backend design should decide whether to reuse the existing access evaluation endpoint contract, add a dedicated readiness endpoint, or expose both a strict allow/deny evaluation and a UI-friendly readiness summary.

## Operational Readiness Checklist

- Define who creates shifts.
- Define who approves shifts.
- Define how operators clock in and clock out.
- Define how paused shifts behave for read and write actions.
- Define how site assignment is verified.
- Define how temporary assignments are approved and expired.
- Define how access denial is escalated.
- Define how local/dev mode is prevented in production.
- Define how audit logs are reviewed.
- Define how operational reports are generated.
- Define how failed readiness checks are monitored.
- Define how supervisor interventions are reviewed.
- Define how pilot shift/device/operator data is separated from production data.
- Define how production support handles wrong-site and missing-shift incidents.

## Gap List

1. `OC-SHIFT-GAP-001`: Production access repository does not read real operator shift/device tables.
   - Description: Current repository synthesizes active shift and trusted device context from request/site data.
   - Risk: Production access could rely on fixture-style context if not replaced.
   - Recommended owner: Backend/Architecture.
   - Recommended next slice: #241 Operator Console access readiness API contract and backend design.
   - Production blocker classification: Yes.

2. `OC-SHIFT-GAP-002`: Shift lifecycle operations are not implemented as production workflows.
   - Description: Clock-in, clock-out, pause, resume, suspend, cancel, and supervisor manual actions need defined API and persistence behavior.
   - Risk: Operators cannot prove active authorized shift status in production.
   - Recommended owner: Backend/Operations.
   - Recommended next slice: #241 Operator Console access readiness API contract and backend design.
   - Production blocker classification: Yes.

3. `OC-SHIFT-GAP-003`: Operator site assignment model is not operationalized.
   - Description: Home site, allowed site group, temporary assignment, and supervisor scope need persistence and enforcement.
   - Risk: Cross-site controlled actions may be allowed or denied inconsistently.
   - Recommended owner: Product/Backend/Operations.
   - Recommended next slice: #241 Operator Console access readiness API contract and backend design.
   - Production blocker classification: Yes.

4. `OC-SHIFT-GAP-004`: Production denial reason contract is not finalized.
   - Description: Existing backend reasons need a stable UI/API catalog and operator-safe copy.
   - Risk: Operators and support staff may receive unclear denial states.
   - Recommended owner: Backend/Frontend/Support.
   - Recommended next slice: #241 Operator Console access readiness API contract and backend design.
   - Production blocker classification: Conditional.

5. `OC-SHIFT-GAP-005`: UI readiness states depend on a future readiness contract.
   - Description: The hardened statutory discount UI can gate workflow sequencing, but production shift/site/device readiness needs explicit API data.
   - Risk: UI cannot accurately guide operators through production readiness failures.
   - Recommended owner: Frontend/Backend.
   - Recommended next slice: #242 Operator Console shift/site readiness UX states.
   - Production blocker classification: Conditional.

6. `OC-SHIFT-GAP-006`: Supervisor escalation and override workflows are not implemented.
   - Description: Supervisor intervention, justification, temporary assignment, and break-glass policy need formal controls.
   - Risk: Support incidents may be handled outside audited workflow.
   - Recommended owner: Product/Operations/Compliance.
   - Recommended next slice: #243 Operator Console supervisor review and override workflow.
   - Production blocker classification: Conditional.

7. `OC-SHIFT-GAP-007`: Audit/reporting screens for readiness failures are not available.
   - Description: Access evaluation outcomes and denial reasons need searchable operational visibility.
   - Risk: Production support cannot monitor readiness failures or detect abuse patterns.
   - Recommended owner: Backend/Frontend/Operations.
   - Recommended next slice: #244 Operator Console audit/reporting read model and screens.
   - Production blocker classification: Conditional.

8. `OC-SHIFT-GAP-008`: Production local/dev fallback blocking is not specified in deployment controls.
   - Description: Sandbox fallback context must be prevented in production runtime configuration.
   - Risk: Production trust boundary could be weakened by non-production context mechanisms.
   - Recommended owner: DevOps/Security/Backend.
   - Recommended next slice: #246 Operator Console deployment/observability readiness.
   - Production blocker classification: Yes.

## Recommended Implementation Slices

Recommended bounded slices after #240:

- #241 Operator Console access readiness API contract and backend design.
- #242 Operator Console shift/site readiness UX states.
- #243 Operator Console supervisor review and override workflow.
- #244 Operator Console audit/reporting read model and screens.
- #245 Operator Console production policy registry readiness.
- #246 Operator Console deployment/observability readiness.

Recommended immediate next slice: #241 Operator Console access readiness API contract and backend design.

Reason: production shift/site/device readiness needs a concrete backend contract and persistence design before the UI can reliably present production readiness states. The current repository synthesis must be replaced or explicitly bounded before rollout.

## Go/No-Go Position

- GO for continued controlled sandbox/pilot validation.
- CONDITIONAL GO for limited operational pilot only if shift/site/device controls are manually controlled, documented, and monitored.
- NO-GO for full production rollout until shift/site validation and device trust are implemented or formally accepted as operational controls.

## Boundary Confirmations

- No backend code changes.
- No frontend code changes.
- No database/DDL/migration/seed changes.
- No Docker/CI/CD changes.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon/reconciliation/HikCentral/gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
