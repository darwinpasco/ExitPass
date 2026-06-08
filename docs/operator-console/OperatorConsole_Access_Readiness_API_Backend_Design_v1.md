# Operator Console Access Readiness API Backend Design v1

## 1. Title and Purpose

This document is the API/backend design for Operator Console production access readiness.

It converts the #239 device enrollment/readiness design and #240 shift/site validation workflow rules into a backend contract for controlled Operator Console actions. The contract combines authenticated operator context, RBAC/action authorization, trusted device readiness, active shift readiness, site/site-group readiness, workflow-state permission, audit/correlation behavior, and stable denial reason output.

Local/dev fallback headers are not production trust. They are acceptable only for Development, Test, and controlled Sandbox validation.

## 2. Scope

In scope:

- Access readiness API contract.
- Backend access evaluation service design.
- Request/response DTO expectations.
- Denial reason model.
- Action code model.
- Persistence and audit expectations.
- Device readiness integration.
- Shift/site readiness integration.
- Local/dev fallback boundary.
- Rollout path from the current implementation.

Out of scope:

- Actual code implementation.
- Database migration.
- UI implementation.
- WebPay.
- Payment provider routing.
- AUB.
- Coupon validation.
- Reconciliation.
- HikCentral/gate implementation.
- Raw evidence, OCR, or automated ID validation.

## 3. Source Artifacts Reviewed

Found and reviewed:

- `docs/operator-console/OperatorConsole_Device_Enrollment_Readiness_Design_v1.md`
- `docs/operator-console/OperatorConsole_Shift_Site_Validation_Workflow_Design_v1.md`
- `docs/operator-console/OperatorConsole_Production_Readiness_Gap_Review_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Readiness_Signoff_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Validation_Runbook_v1.md`
- `docs/operator-console/OperatorConsole_Statutory_Discount_Pilot_Triage_Guide.md`
- `docs/operator-console/statutory-validation-and-access-contract.md`
- `docs/operator-console/operator-console-schema-extension-design.md`
- `docs/operator-console/operator-console-db-patch-validation.md`
- `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleAccessEvaluationEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Contracts/OperatorConsole/OperatorConsoleAccessEvaluationDtos.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/**`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/OperatorConsole/**`
- `src/Services/CentralPms/tests/**` Operator Console access evaluation tests
- `src/Services/OperatorConsoleUi/src/**` Operator Console access context/API client usage

Standalone Operator Console BRD, ExitPass BRD, ExitPass API Contract Pack, and ExitPass Engineering Pack documents were not found in the repository by filename search. This design uses repo-available artifacts only and does not invent requirements from unavailable packs.

No SQL was run for this document.

## 4. Current Implementation Summary

Current access evaluation endpoint:

- `POST /v1/ops/operator-console/access/evaluate`
- Implemented by `OperatorConsoleAccessEvaluationEndpoints`.
- Accepts `OperatorConsoleAccessEvaluationRequest`.
- Resolves request/header identity through `OperatorConsoleIdentityContext`.
- Calls `IOperatorConsoleAccessEvaluationService`.
- Persists the result through `IOperatorConsoleAccessEvaluationWriter`.
- Returns HTTP 200 with `OperatorConsoleAccessEvaluationResponse`.
- Returns HTTP 400 for invalid request shape.
- Returns HTTP 500 if access evaluation persistence fails.

Current request fields:

- `UserId`
- `OperatorDeviceBindingId`
- `SiteId`
- `SiteGroupId`
- `OperatorShiftId`
- `WorkflowCode`
- `ControlledActionCode`
- `ParkingSessionId`
- `EvidenceAccessIntent`
- `IdempotencyKey`
- `CorrelationId`

Current response fields:

- `EvaluationId`
- `Allowed`
- `Decision`
- `DenialReasons`
- `EffectiveRole`
- `DeviceTrust`
- `ShiftContext`
- `SiteContext`
- `EvaluatedAt`
- `Persisted`
- `CorrelationId`

Current action codes:

- `SESSION_LOOKUP`
- `CREATE_STATUTORY_DISCOUNT_DRAFT`
- `VIEW_STATUTORY_DISCOUNT_DRAFT`
- `DECIDE_STATUTORY_DISCOUNT`
- `CAPTURE_EVIDENCE`
- `VIEW_EVIDENCE`
- `APPLY_STATUTORY_DISCOUNT_PAYABLE_BASIS`
- `VIEW_POLICY_RESOLUTION`

Current supported workflow:

- `STATUTORY_DISCOUNT_VALIDATION`

Current service behavior:

- Allows only supported workflow/action codes.
- Requires an active HR identity mapping read model.
- Requires an active trusted device binding.
- Requires an active device assignment matching request site/site group.
- Requires an active shift for the operator and site/site group.
- Denies revoked shifts and conflicting active takeovers.
- Treats `BROWSER_KEY_ONLY`, `MTLS_ONLY`, and `BROWSER_KEY_AND_MTLS` as trusted device levels.
- Returns `ALLOWED` or `DENIED`.
- Does not persist by itself.

Current read repository behavior:

- Reads `identity.users`.
- Reads `sites.sites` when `siteId` is present.
- Synthesizes an HR identity mapping from `identity.users`.
- Synthesizes an active trusted device binding from the request device ID and site.
- Synthesizes an active device assignment.
- Synthesizes an active shift from the request shift ID and site.
- Does not yet read real `operator_console.hr_identity_mappings`, `operator_console.operator_device_bindings`, `operator_console.operator_device_assignment_history`, `operator_console.operator_shifts`, `operator_console.shift_revocations`, or `operator_console.shift_takeovers`.

Current writer behavior:

- Persists a decision snapshot to `operations.operator_action_logs`.
- Uses `CONTROLLED_RECHECK` action type and `SUCCESS` or `DENIED` action status.
- Does not currently write `operator_console.operator_access_evaluations` or `operator_console.operator_access_evaluation_reasons`.

Current statutory discount access usage:

- Session lookup evaluates `SESSION_LOOKUP`.
- Policy resolution evaluates `VIEW_POLICY_RESOLUTION`.
- Draft creation evaluates `CREATE_STATUTORY_DISCOUNT_DRAFT`.
- Draft read/list evaluates `VIEW_STATUTORY_DISCOUNT_DRAFT`.
- Evidence capture evaluates `CAPTURE_EVIDENCE`.
- Evidence read/list evaluates `VIEW_EVIDENCE`.
- Decision evaluates `DECIDE_STATUTORY_DISCOUNT`.
- Apply-payable-basis evaluates `APPLY_STATUTORY_DISCOUNT_PAYABLE_BASIS`.

Current local/dev fallback:

- Operator Console UI sends deterministic local operator context headers and body fields using Vite environment values or fallback GUIDs.
- These include operator user, operator device binding, and operator shift IDs.
- This supports sandbox validation only and must be blocked as production trust.

Current tests:

- Unit tests cover allowed access and denial reasons for identity, device, assignment, shift, takeover, unsupported workflow, and unsupported action.
- Writer tests verify action-log persistence mapping.
- Session lookup tests cover denied access propagation.
- Integration tests verify the read repository is registered and missing rows return safe empty context.

Current schema artifacts:

- Older `statutory-validation-and-access-contract.md` treated dedicated operator-console access tables as a future DDL gap.
- Newer `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql` creates the proposed `operator_console` schema tables and enums in a patch.
- `operator-console-db-patch-validation.md` records local non-production validation of that patch.
- The patch is not baseline DDL and production promotion requires separate approval.

Synthetic/locked-schema behavior to replace for production:

- Synthesized device binding.
- Synthesized device assignment.
- Synthesized active shift.
- Synthesized HR identity mapping from `identity.users` alone.
- Action-log-only persistence for access evaluation details.
- Local/dev fallback-only trust.

## 5. Production Access Readiness Model

Readiness dimensions:

- Operator identity readiness.
- Role/action readiness.
- Device readiness.
- Shift readiness.
- Site/site-group readiness.
- Workflow-state readiness.
- Audit/correlation readiness.

All required dimensions must pass for controlled write actions. Read-only actions may have narrower requirements only when explicitly configured by role and scope. The readiness decision is authoritative only for the evaluated action at the evaluation time; it must not be reused as a long-lived permission grant.

Controlled write actions must fail closed when required readiness data is missing, stale, invalid, or unauditable.

## 6. Endpoint Contract Proposal

### Primary Endpoint: `POST /v1/ops/operator-console/access/readiness/evaluate`

Purpose:

- Evaluate whether the current operator can perform a requested Operator Console action against a target entity.
- Return UI-ready readiness dimensions and stable denial reasons.
- Persist controlled-action and denied-readiness evidence.

Request shape:

- Uses the canonical request DTO in section 7.

Response shape:

- Uses the canonical response DTO in section 8.

Auth/trust expectation:

- Requires authenticated principal.
- Requires production-resolved operator identity.
- Requires real production device/shift/site readiness records in production.
- Rejects fallback-only trust in production.

Persistence expectation:

- Persist denied evaluations.
- Persist controlled write-action evaluations.
- Persist sensitive read evaluations, including evidence read/list.
- Do not persist harmless navigation unless explicitly required by compliance.

Idempotency expectation:

- `idempotencyKey` required for controlled writes and recommended for sensitive reads.
- Repeated evaluation with the same idempotency key and identical request should return the original persisted evaluation when practical.
- Conflicting reuse should return a validation or conflict error.

Audit expectation:

- Correlation ID required.
- Persist access evaluation and reason rows when target tables are available.
- Include operator, device, shift, site, action, target, decision, reason codes, and evaluated timestamp.

Error behavior:

- HTTP 200 with `accessAllowed=false` for ordinary denied readiness.
- HTTP 400 for invalid request shape.
- HTTP 401 for missing authentication.
- HTTP 500 or fail-closed error for audit persistence failure on controlled writes.

### Supporting Endpoint: `GET /v1/ops/operator-console/access/readiness/current`

Purpose:

- Return the current operator/device/shift/site readiness summary for the Operator Console shell.

Request shape:

- Query/body should be avoided where possible; use authenticated principal, device identity, current site, and current shift context.
- Optional `siteId`, `siteGroupId`, and `correlationId`.

Response shape:

- Current readiness dimensions, allowed actions, denial reasons, and next operator action.

Auth/trust expectation:

- Requires authenticated principal.
- Production requires real device identity and shift/site reads.

Persistence expectation:

- Usually read-only summary; persist only denied or suspicious states by policy.

Idempotency expectation:

- Not required.

Audit expectation:

- Log unusual denial states, fallback use, and production trust failures.

Error behavior:

- 200 with readiness status where possible.
- 401 if unauthenticated.
- 403 if the shell itself is blocked by production trust rules.

### Supporting Endpoint: `GET /v1/ops/operator-console/access/actions`

Purpose:

- Return the supported action catalog and action metadata for UI/state hardening.

Request shape:

- Optional workflow code and correlation ID.

Response shape:

- Action codes, labels, read/write classification, role expectations, device/shift/site requirements, and audit classification.

Auth/trust expectation:

- Authenticated support/admin/operator context.

Persistence expectation:

- None unless used for controlled export.

Idempotency expectation:

- Not applicable.

Audit expectation:

- Not required for ordinary catalog reads.

Error behavior:

- 401 if unauthenticated.
- 403 if role cannot view the catalog.

### Supporting Endpoint: `GET /v1/ops/operator-console/access/denial-reasons`

Purpose:

- Return denial reason metadata for UI copy, support training, and documentation.

Request shape:

- Optional reason code filter and correlation ID.

Response shape:

- Reason code, severity, retryability, message category, and support note.

Auth/trust expectation:

- Authenticated support/admin/operator context.

Persistence expectation:

- None.

Idempotency expectation:

- Not applicable.

Audit expectation:

- Not required unless exported.

Error behavior:

- 401 if unauthenticated.
- 403 if role cannot view operational metadata.

### Supporting Endpoint: `GET /v1/ops/operator-console/operators/{operatorId}/readiness`

Purpose:

- Support supervisor or operations review of an operator's readiness.

Request shape:

- Path `operatorId`.
- Optional `siteId`, `siteGroupId`, `operatorDeviceBindingId`, `operatorShiftId`, and `correlationId`.

Response shape:

- Operator-scoped readiness status, active shift summary, device/site match, allowed actions, and denial reasons.

Auth/trust expectation:

- Supervisor, operations, or compliance role.
- Ordinary operators may access only their own readiness if allowed.

Persistence expectation:

- Persist supervisor/admin review if policy requires.

Idempotency expectation:

- Not required.

Audit expectation:

- Audit supervisor or compliance reads by policy.

Error behavior:

- 401 if unauthenticated.
- 403 if role/scope is insufficient.
- 404 if the operator is not found or not visible to the caller scope.

## 7. Canonical Request DTO

Proposed readiness evaluation request:

```json
{
  "operatorUserId": "77000000-0000-0000-0000-000000000010",
  "operatorDeviceBindingId": "77000000-0000-0000-0000-000000000030",
  "operatorShiftId": "77000000-0000-0000-0000-000000000050",
  "siteId": "77000000-0000-0000-0000-000000000002",
  "siteGroupId": "77000000-0000-0000-0000-000000000001",
  "requestedAction": "DECIDE_STATUTORY_DISCOUNT",
  "targetEntityType": "STATUTORY_DISCOUNT_VALIDATION",
  "targetEntityId": "b84541dc-4929-4f53-bdcc-22b145dd7c41",
  "workflowState": "PENDING_OPERATOR_REVIEW",
  "correlationId": "52883917-a776-4656-8d0a-b87087d646b1",
  "idempotencyKey": "operator-console-readiness-example",
  "clientContext": {
    "uiModule": "statutory-discount",
    "screenState": "evidence-satisfied"
  },
  "devModeContext": {
    "enabled": false,
    "source": null
  }
}
```

Field expectations:

| Field | Requirement |
| --- | --- |
| `operatorUserId` | Required in the DTO, but production must resolve it from authenticated principal or trusted server-side mapping. |
| `operatorDeviceBindingId` | Required for controlled production actions. |
| `operatorShiftId` | Required for controlled production operator actions. |
| `siteId` | Required for site-scoped controlled actions. |
| `siteGroupId` | Required when site-group scoping is part of the action. |
| `requestedAction` | Required stable action code. |
| `targetEntityType` | Required for controlled writes and sensitive reads. |
| `targetEntityId` | Required when the action targets an existing entity. |
| `workflowState` | Required when action permission depends on workflow state. |
| `correlationId` | Required for all readiness evaluations. |
| `idempotencyKey` | Required for controlled writes; recommended for sensitive reads. |
| `clientContext` | Optional, bounded, non-authoritative UI/support context. |
| `devModeContext` | Optional in non-production; production-blocked when used as trust. |

## 8. Canonical Response DTO

Proposed readiness evaluation response:

```json
{
  "accessEvaluationId": "11111111-1111-1111-1111-111111111111",
  "accessAllowed": false,
  "accessDecision": "DENIED",
  "requestedAction": "DECIDE_STATUTORY_DISCOUNT",
  "readinessStatus": "BLOCKED",
  "readinessDimensions": [
    { "dimension": "operator", "status": "READY", "required": true },
    { "dimension": "device", "status": "READY", "required": true },
    { "dimension": "shift", "status": "BLOCKED", "required": true },
    { "dimension": "site", "status": "READY", "required": true },
    { "dimension": "workflow", "status": "READY", "required": true }
  ],
  "denialReasons": [
    {
      "code": "SHIFT_NOT_ACTIVE",
      "severity": "BLOCKING",
      "retryable": true,
      "messageCategory": "SHIFT_REQUIRED"
    }
  ],
  "operatorReadiness": {
    "operatorUserId": "77000000-0000-0000-0000-000000000010",
    "status": "ACTIVE",
    "roleAllowed": true
  },
  "deviceReadiness": {
    "operatorDeviceBindingId": "77000000-0000-0000-0000-000000000030",
    "status": "ACTIVE",
    "trustLevel": "BROWSER_KEY_AND_MTLS",
    "trusted": true,
    "siteMatch": true
  },
  "shiftReadiness": {
    "operatorShiftId": "77000000-0000-0000-0000-000000000050",
    "status": "ENDED",
    "active": false,
    "siteMatch": true
  },
  "siteReadiness": {
    "siteId": "77000000-0000-0000-0000-000000000002",
    "siteGroupId": "77000000-0000-0000-0000-000000000001",
    "operatorSiteAllowed": true,
    "deviceSiteMatch": true,
    "shiftSiteMatch": true
  },
  "workflowReadiness": {
    "workflowState": "PENDING_OPERATOR_REVIEW",
    "actionAllowedForState": true
  },
  "auditPersisted": true,
  "evaluatedAt": "2026-06-08T00:00:00Z",
  "correlationId": "52883917-a776-4656-8d0a-b87087d646b1",
  "retryable": true,
  "nextOperatorAction": "Clock in or contact a supervisor before continuing."
}
```

Required response fields:

- `accessEvaluationId`
- `accessAllowed`
- `accessDecision`
- `requestedAction`
- `readinessStatus`
- `readinessDimensions`
- `denialReasons`
- `operatorReadiness`
- `deviceReadiness`
- `shiftReadiness`
- `siteReadiness`
- `workflowReadiness`
- `auditPersisted`
- `evaluatedAt`
- `correlationId`
- `retryable`
- `nextOperatorAction`

## 9. Readiness Dimensions Detail

| Dimension | Input required | Pass condition | Fail condition | Denial reason codes | Audit fields | UX message category |
| --- | --- | --- | --- | --- | --- | --- |
| Operator | Authenticated principal, `operatorUserId` | Operator resolves to active identity/user mapping | Missing, not found, inactive, suspended, revoked, expired | `OPERATOR_ID_MISSING`, `OPERATOR_NOT_FOUND`, `OPERATOR_INACTIVE` | operator user ID, identity mapping ID, status | `OPERATOR_NOT_READY` |
| Role/action | Requested action, roles/permissions | Role allows action and action is supported | Missing role, unsupported action, role mismatch | `ROLE_NOT_ALLOWED`, `ACTION_NOT_ALLOWED_FOR_ROLE` | role, requested action, workflow | `ROLE_BLOCKED` |
| Device | Device binding ID, trust material, assignment | Device is enrolled, active, trusted, assigned to site | Missing, unenrolled, inactive, untrusted, site mismatch | `DEVICE_ID_MISSING`, `DEVICE_NOT_ENROLLED`, `DEVICE_NOT_ACTIVE`, `DEVICE_SITE_MISMATCH` | device binding ID, trust level, status, site | `DEVICE_NOT_READY` |
| Shift | Shift ID, active window, revocation/takeover | Shift is active/current for operator and site | Missing, not found, ended, suspended, revoked, taken over, wrong site | `SHIFT_ID_MISSING`, `SHIFT_NOT_FOUND`, `SHIFT_NOT_ACTIVE`, `SHIFT_SITE_MISMATCH` | shift ID, status, active window, takeover ID | `SHIFT_REQUIRED` |
| Site/site-group | Site ID, site group ID, assignments | Operator, device, shift, and target match site/scope | Missing site, missing group, cross-site mismatch | `SITE_ID_MISSING`, `SITE_GROUP_ID_MISSING`, `OPERATOR_SITE_NOT_ALLOWED`, `DEVICE_SITE_MISMATCH`, `SHIFT_SITE_MISMATCH` | site ID, site group ID, assignment references | `SITE_BLOCKED` |
| Workflow state | Requested action, workflow state, target | Action is valid for current workflow state | Action out of sequence or final state | `ACTION_NOT_ALLOWED_FOR_WORKFLOW_STATE` | action, target, workflow state | `WORKFLOW_BLOCKED` |
| Audit/correlation | Correlation ID, audit writer availability | Correlation present and required audit persisted | Missing correlation or persistence failed for controlled write | `CORRELATION_ID_MISSING`, `AUDIT_PERSISTENCE_FAILED` | correlation ID, audit ID, persistence status | `SUPPORT_REQUIRED` |
| Local/dev boundary | Environment, dev-mode context, fallback source | Non-production fallback is visibly flagged; production uses real trust | Fallback-only trust in production | `LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION` | environment, fallback source, request source | `PRODUCTION_TRUST_REQUIRED` |

## 10. Denial Reason Catalog

| Code | Meaning | Severity | Retryable | Operator-facing message category | Support/audit note |
| --- | --- | --- | --- | --- | --- |
| `OPERATOR_ID_MISSING` | No operator identity was supplied or resolved. | Blocking | Yes | `OPERATOR_NOT_READY` | Check auth principal and context resolver. |
| `OPERATOR_NOT_FOUND` | Operator identity was not found. | Blocking | No | `OPERATOR_NOT_READY` | Verify `identity.users` and HR mapping. |
| `OPERATOR_INACTIVE` | Operator exists but is inactive, suspended, revoked, expired, or retired. | Blocking | Conditional | `OPERATOR_NOT_READY` | Supervisor/identity admin review required. |
| `ROLE_NOT_ALLOWED` | Operator lacks the required role for the workflow. | Blocking | Conditional | `ROLE_BLOCKED` | Review role assignment and site scope. |
| `DEVICE_ID_MISSING` | No device binding ID was supplied or resolved. | Blocking | Yes | `DEVICE_NOT_READY` | Device enrollment/context issue. |
| `DEVICE_NOT_ENROLLED` | Device is not enrolled or binding was not found. | Blocking | Conditional | `DEVICE_NOT_READY` | Start enrollment or use approved device. |
| `DEVICE_NOT_ACTIVE` | Device is suspended, revoked, lost, expired, retired, inactive, or untrusted. | Blocking | Conditional | `DEVICE_NOT_READY` | Inspect device lifecycle state and trust material. |
| `DEVICE_SITE_MISMATCH` | Device assignment does not match the requested site or site group. | Blocking | Conditional | `SITE_BLOCKED` | Verify assignment history and requested site. |
| `SHIFT_ID_MISSING` | No shift ID was supplied or resolved. | Blocking | Yes | `SHIFT_REQUIRED` | Operator may need to clock in. |
| `SHIFT_NOT_FOUND` | Shift was not found. | Blocking | Conditional | `SHIFT_REQUIRED` | Check HR import and shift record. |
| `SHIFT_NOT_ACTIVE` | Shift is not active/current for controlled actions. | Blocking | Conditional | `SHIFT_REQUIRED` | Could be scheduled, paused, ended, suspended, revoked, or cancelled. |
| `SHIFT_SITE_MISMATCH` | Shift site or site group does not match the requested action. | Blocking | Conditional | `SITE_BLOCKED` | Verify shift/site assignment. |
| `SITE_ID_MISSING` | Site ID is required but missing. | Blocking | Yes | `SITE_BLOCKED` | Check request context and target resolution. |
| `SITE_GROUP_ID_MISSING` | Site group ID is required but missing. | Blocking | Yes | `SITE_BLOCKED` | Check site lookup and request context. |
| `OPERATOR_SITE_NOT_ALLOWED` | Operator is not assigned to the requested site or site group. | Blocking | Conditional | `SITE_BLOCKED` | Requires assignment or supervisor scope review. |
| `ACTION_NOT_ALLOWED_FOR_ROLE` | Role does not allow the requested action. | Blocking | Conditional | `ROLE_BLOCKED` | Role/action matrix denied. |
| `ACTION_NOT_ALLOWED_FOR_WORKFLOW_STATE` | Workflow state does not allow the requested action. | Blocking | Conditional | `WORKFLOW_BLOCKED` | Preserve existing statutory discount sequencing controls. |
| `LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION` | Request relied on development fallback context in production. | Blocking | No | `PRODUCTION_TRUST_REQUIRED` | Security/platform incident if seen in production. |
| `CORRELATION_ID_MISSING` | Correlation ID is missing or empty. | Blocking | Yes | `SUPPORT_REQUIRED` | Required for support and audit traceability. |
| `AUDIT_PERSISTENCE_FAILED` | Required audit/access persistence failed. | Blocking | Yes | `SUPPORT_REQUIRED` | Fail closed for controlled writes. |

Existing implementation reasons such as `HR_IDENTITY_MAPPING_NOT_FOUND`, `HR_IDENTITY_MAPPING_INACTIVE`, `DEVICE_BINDING_NOT_FOUND`, `DEVICE_BINDING_INACTIVE`, `DEVICE_NOT_TRUSTED`, `DEVICE_SITE_ASSIGNMENT_NOT_FOUND`, `DEVICE_SITE_ASSIGNMENT_INVALID`, `NO_ACTIVE_SHIFT`, `SHIFT_REVOKED`, `SHIFT_TAKEOVER_ACTIVE`, `WORKFLOW_NOT_SUPPORTED`, and `ACTION_NOT_SUPPORTED` should be mapped or versioned into this production catalog.

## 11. Action Code Catalog

| Action | Role expectation | Device required | Active shift required | Site match required | Classification | Audit classification | Production blocker if not enforced |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `SESSION_LOOKUP` | Operator or supervisor | Yes | Yes for operator floor use | Yes | Read/sensitive lookup | Persist denied and controlled lookup | Yes |
| `CREATE_STATUTORY_DISCOUNT_DRAFT` | Operator or supervisor | Yes | Yes | Yes | Write | Persist every attempt | Yes |
| `VIEW_STATUTORY_DISCOUNT_DRAFT` | Operator, supervisor, auditor read-only | Yes for console operator use | Yes for operator floor use | Yes or scoped read | Sensitive read | Persist denied and policy-sensitive reads | Conditional |
| `DECIDE_STATUTORY_DISCOUNT` | Operator approver or supervisor | Yes | Yes | Yes | Write/decision | Persist every attempt | Yes |
| `CAPTURE_EVIDENCE` | Operator or supervisor | Yes | Yes | Yes | Write/sensitive | Persist every attempt | Yes |
| `VIEW_EVIDENCE` | Operator during workflow, supervisor, compliance | Yes unless back-office compliance path differs | Yes for operator workflow view | Yes or scoped read | Sensitive read | Persist every attempt | Yes |
| `APPLY_STATUTORY_DISCOUNT_PAYABLE_BASIS` | Operator approver or supervisor | Yes | Yes | Yes | Write/backend mutation | Persist every attempt | Yes |
| `VIEW_POLICY_RESOLUTION` | Operator, supervisor, auditor read-only | Yes for console operator use | Yes for operator floor use | Yes | Read | Persist denied; allowed persistence by policy | Conditional |
| `SUPERVISOR_REVIEW` | Supervisor | Yes unless back-office policy differs | Conditional | Site group scope | Read/decision prep | Persist by policy | Conditional |
| `SUPERVISOR_OVERRIDE` | Supervisor or operations admin | Yes | Conditional by break-glass policy | Yes or explicit scope | Write/override | Persist every attempt with justification | Yes |
| `VIEW_AUDIT_REPORT` | Supervisor, operations, compliance auditor | Conditional | No for historical reporting if scoped | Scoped | Read/export | Persist export and sensitive reads | Conditional |

`SUPERVISOR_REVIEW`, `SUPERVISOR_OVERRIDE`, and `VIEW_AUDIT_REPORT` are proposed future actions and are not in the current `OperatorConsoleActionCodes` implementation.

## 12. Backend Service Design

### `OperatorConsoleAccessReadinessEndpoint`

Responsibility:

- Expose readiness evaluation endpoints.
- Resolve authenticated principal and request context.
- Validate request shape.
- Call readiness service.
- Return UI-safe response.

Inputs:

- HTTP request, authenticated principal, canonical request DTO.

Outputs:

- Canonical response DTO or bounded error response.

Dependencies:

- `OperatorConsoleAccessReadinessService`, logging, tracing, auth context.

Must not do:

- Mutate payments, providers, gates, coupons, reconciliation, WebPay, or statutory discount payable amounts.
- Trust fallback headers in production.
- Expose stack traces or raw internal failures.

### `OperatorConsoleAccessReadinessService`

Responsibility:

- Combine operator, role/action, device, shift, site, workflow, and audit readiness.
- Produce deterministic allow/deny result and next operator action.

Inputs:

- Readiness command, current time, environment/trust policy.

Outputs:

- Readiness result with dimensions and denial reasons.

Dependencies:

- Readiness repository, action catalog, denial reason catalog, clock, environment policy.

Must not do:

- Persist directly unless explicitly designed as the owner.
- Compute statutory discount payable-basis amounts.
- Bypass statutory discount evidence/approval gates.

### `OperatorConsoleAccessReadinessRepository`

Responsibility:

- Load real production readiness records.
- Resolve operator identity, roles, device binding, assignment history, shift state, site/site group, and target workflow state.

Inputs:

- Operator, device, shift, site, action, target, evaluated-at timestamp.

Outputs:

- Aggregate readiness read model.

Dependencies:

- `identity`, `sites`, `operator_console`, and workflow-specific read tables.

Must not do:

- Synthesize active device or shift in production.
- Overload gate device or payment tables for Operator Console trust.

### `OperatorConsoleAccessAuditWriter`

Responsibility:

- Persist access evaluations and denial reasons.
- Fail closed for required controlled writes when persistence fails.

Inputs:

- Readiness result and persistence context.

Outputs:

- Persisted access evaluation ID, audit status, optional audit event ID.

Dependencies:

- `operator_console.operator_access_evaluations`, `operator_console.operator_access_evaluation_reasons`, `operations.operator_action_logs`, and/or `audit.audit_events`.

Must not do:

- Store raw secrets, private keys, raw evidence, full ID numbers, or unnecessary personal data.

### `OperatorConsoleActionCatalog`

Responsibility:

- Centralize supported action codes, role expectations, read/write classification, readiness requirements, and audit classification.

Inputs:

- Workflow code, requested action, role context.

Outputs:

- Action metadata and allowability requirements.

Dependencies:

- Static config or controlled code tables in a later implementation.

Must not do:

- Encode site-specific policy in scattered endpoint code.

### `OperatorConsoleDenialReasonCatalog`

Responsibility:

- Centralize denial reason metadata.
- Provide UI-safe message categories and support notes.

Inputs:

- Reason codes from readiness service.

Outputs:

- Severity, retryability, category, support/audit metadata.

Dependencies:

- Static config or controlled code tables in a later implementation.

Must not do:

- Return sensitive operational internals to ordinary operators.

## 13. Persistence and Schema Considerations

Actual current state from repo artifacts:

- Current runtime writer persists access evaluation snapshots to `operations.operator_action_logs`.
- Current runtime read repository reads `identity.users` and `sites.sites`, then synthesizes device and shift context.
- `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql` defines `operator_console` schema support in a local validated patch.
- `operator-console-db-patch-validation.md` confirms local non-production execution of that patch.
- The patch is not baseline DDL and does not imply production rollout.

Potential/current patch tables:

- `operator_console.operator_access_evaluations`
- `operator_console.operator_access_evaluation_reasons`
- `operator_console.operator_device_bindings`
- `operator_console.operator_shifts`
- `operator_console.operator_shift_versions`
- `operator_console.shift_revocations`
- `operator_console.shift_takeovers`
- `operator_console.operator_device_assignment_history`
- `operator_console.hr_identity_mappings`

Potential future tables/entities not clearly present in the current patch:

- `operator_console.operator_devices`
- `operator_console.operator_site_assignments`
- `operator_console.operator_shift_events`
- `operator_console.operator_device_enrollment_requests`

Migration path:

1. Keep the current `/access/evaluate` endpoint stable for sandbox/pilot compatibility.
2. Add the production readiness contract behind `/access/readiness/evaluate` or version the existing endpoint after explicit API review.
3. Add repository reads from real `operator_console` tables when the schema patch is promoted to the target environment.
4. Move access persistence from action-log-only snapshots to dedicated `operator_console.operator_access_evaluations` and `operator_console.operator_access_evaluation_reasons`, while optionally retaining `operations.operator_action_logs` for operational audit summary.
5. Add production environment guardrails that reject fallback-only trust.
6. Update statutory discount endpoints to depend on the readiness contract for controlled actions.

No schema changes are made by this document.

## 14. Local/Dev Fallback Boundary

Required boundary:

- Local/dev header fallback is allowed only in Development, Test, and controlled Sandbox environments.
- Production must reject fallback-only trust.
- Production must require a resolved authenticated principal and real device, shift, site, and role readiness records.
- Non-production fallback usage must be visibly logged or flagged.
- UI dev-mode context may help debugging but is never authoritative.
- Production access must not be granted solely because `X-Operator-User-Id`, `X-Operator-Device-Binding-Id`, or `X-Operator-Shift-Id` was supplied.

Recommended production denial:

- Return `LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION`.
- Persist a denied readiness evaluation where audit persistence is available.
- Surface operator-safe copy: production device trust could not be verified.

## 15. Integration with Existing Statutory Discount Endpoints

Access readiness should be used by:

- Session lookup: require operator, role, device, shift, and site readiness before returning site-scoped session data.
- Policy resolution: require readiness before returning operational policy context.
- Draft creation: require readiness and workflow permission before creating a statutory discount validation draft.
- Draft read: require scoped readiness or read-only supervisor/auditor scope.
- Evidence capture: require readiness, workflow-state permission, and evidence metadata-only controls.
- Evidence list/read: require readiness and sensitive evidence read authorization.
- Decision: require readiness, role/action permission, evidence-satisfied workflow state, and approval/rejection state permission.
- Apply-payable-basis: require readiness, approved validation state, original tariff snapshot, and idempotent apply contract.

No changes to payable-basis computation are intended. The readiness contract gates who may request the action; it must not change the statutory discount amount, VAT calculation, tariff snapshot mutation rules, payment state, coupon behavior, or gate behavior.

## 16. Error Handling and HTTP Behavior

Recommended behavior:

- Protected statutory discount endpoints return HTTP 403 when access readiness denies the requested action.
- The readiness evaluate endpoint may return HTTP 200 with `accessAllowed=false`.
- Invalid request shape returns HTTP 400.
- Missing authentication returns HTTP 401.
- Target entity not found returns HTTP 404 where appropriate.
- Idempotency conflicts return HTTP 409 where implemented.
- Audit persistence failure must fail closed for controlled write actions.
- Responses must not expose stack traces, raw SQL errors, raw policy internals, private keys, raw evidence, or sensitive identifiers.

Recommended error shape for protected endpoints:

```json
{
  "errorCode": "ACCESS_READINESS_DENIED",
  "message": "Operator Console access readiness denied the requested action.",
  "correlationId": "52883917-a776-4656-8d0a-b87087d646b1",
  "retryable": true,
  "details": {
    "accessEvaluationId": "11111111-1111-1111-1111-111111111111",
    "denialReasons": ["SHIFT_NOT_ACTIVE"],
    "nextOperatorAction": "Clock in or contact a supervisor before continuing."
  }
}
```

## 17. Testing Strategy

Required future tests:

- Unit tests for each denial reason.
- Unit tests for every action in the role/action matrix.
- Integration tests for allowed and denied readiness paths.
- Production-mode test proving local/dev fallback is rejected.
- Statutory discount endpoint tests proving readiness gates are enforced.
- Audit persistence tests for allowed controlled actions and denied actions.
- Audit persistence failure tests proving controlled writes fail closed.
- Site mismatch tests.
- Site group mismatch tests.
- Device missing, inactive, revoked, lost, and untrusted tests.
- Shift missing, inactive, ended, suspended, revoked, taken-over, and wrong-site tests.
- Workflow-state tests for approval before evidence and apply before approval.
- Read-only auditor tests proving no decision/apply authority.
- Supervisor scope tests for read/review and future override behavior.

## 18. Implementation Roadmap

Recommended bounded implementation slices after #241:

- #242 Operator Console access readiness backend foundation.
- #243 Operator Console access readiness endpoint and DTOs.
- #244 Operator Console production-mode local fallback blocking.
- #245 Operator Console device/shift/site repository wiring.
- #246 Operator Console readiness UX states.
- #247 Operator Console audit/reporting read model and screens.

Recommended immediate next slice: #242 Operator Console access readiness backend foundation.

Reason: the service/catalog/readiness model should be introduced before adding a public readiness endpoint or replacing repository reads. That lets the implementation preserve current pilot behavior while building the production contract behind focused tests.

## Implementation Status After #245

`#245 Operator Console device/shift/site repository wiring` added repository-backed readiness capability detection and read-only operator, device, shift, and site readiness checks for the `/v1/ops/operator-console/access/readiness/evaluate` path.

The inspected local `exitpass_v12_dev` database did not contain the `operator_console` schema or tables. The repository therefore fails closed in Production when required readiness tables are unavailable, while Development/Test/Sandbox fallback validation remains usable according to `OperatorConsoleLocalDevFallbackPolicy`.

The repo patch `infra/db/patches/ExitPass_OperatorConsoleSchema_v1.2.sql` contains the target `operator_console` tables used by the repository wiring, including `hr_identity_mappings`, `operator_device_bindings`, `operator_device_assignment_history`, `operator_shifts`, `operator_access_evaluations`, and `operator_access_evaluation_reasons`.

Remaining work:

- Apply or migrate the operator-console schema through the approved database change path.
- Add production fixture or integration coverage once the schema is present in the validation database.
- Wire dedicated access-evaluation persistence once the production schema is available.

## 19. Go/No-Go Position

- GO for continued controlled sandbox/pilot validation.
- CONDITIONAL GO for limited pilot only if access readiness is manually controlled, documented, and monitored.
- NO-GO for full production rollout until the access readiness contract is implemented or equivalent operational controls are formally accepted.

## 20. Boundary Confirmations

- No backend code changes.
- No frontend code changes.
- No database/DDL/migration/seed changes.
- No Docker/CI/CD changes.
- No WebPay changes.
- No payment/provider routing changes.
- No AUB changes.
- No coupon/reconciliation/HikCentral/gate changes.
- No sensitive credentials, production IDs, private keys, raw evidence, or personal data added.
