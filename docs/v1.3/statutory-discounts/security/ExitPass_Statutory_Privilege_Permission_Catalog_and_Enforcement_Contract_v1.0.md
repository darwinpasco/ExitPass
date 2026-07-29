# ExitPass Statutory Privilege Permission Catalog and Enforcement Contract v1.0

## Executive Decision

ExitPass v1.3 statutory parking privileges use a separated authority model:

1. A human or authorized service channel may submit a statutory privilege request only under a local-ordinance availability gate.
2. Operator Console reviewers approve or reject beneficiary eligibility only.
3. Operator Console reviewers do not apply the privilege, calculate the final payable amount, create payment intent, or fiscalize the transaction.
4. WebPay or the Cashier-Assisted Terminal requests payable-basis application at payment time.
5. Central PMS revalidates the approved privilege against the authoritative parking session and tariff before application.

This document freezes the backend permission catalog, named policy contract, route-policy matrix, actor boundary, scope semantics, and separation-of-duties rules for the v1.3 statutory parking privilege workflow.

## Evidence Inventory

Primary worktree inspected: `D:\SourceCodes\ExitPass-I-StatutoryPermissions`, branch `feature/statutory-privilege-permission-catalog-freeze`, HEAD `d291dfe0cac1f726e36700da788ae823bdf32aff`.

Canonical DB inspected read-only: `D:\SourceCodes\exitpassdb_v1.2`, branch `develop`, HEAD `7a785fd93d592b019fbb6ac6bbdf4fc82d8485dc`, generated authority `build\generated\exitpass-full-object.generated.sql`.

Management Platform inspected read-only: `D:\SourceCodes\ExitPass-ManagementPlatform`, branch `develop`, HEAD `488771f51eb358e384f94dcedd1209fd3d775519`.

Central PMS files inspected include:

- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/Security/CentralPmsRbacPolicyCatalog.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Security/CentralPmsRbacMiddleware.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Infrastructure/Security/CentralPmsRbacRepository.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/OperatorConsoleStatutoryDiscountDraftEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/StatutoryDiscountDecisionEndpoints.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleStatutoryDiscountDecisionService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/OperatorConsole/OperatorConsoleServiceChannelStatutoryDiscountReviewService.cs`
- `src/Services/CentralPms/src/ExitPass.CentralPms.Application/ManagementPlatform/ManagementPlatformIdentityRbacInventoryService.cs`
- `scripts/management-platform/Seed-ManagementPlatformUatIdentityRbac.sql`
- `scripts/management-platform/Verify-ManagementPlatformUatIdentityRbac.sql`

## Frozen Actor Classes

| Actor class | Allowed statutory capability | Prohibited statutory capability |
|---|---|---|
| Human request initiator | Submit a request on behalf of a customer where policy allows. | Approve or reject the same request; apply payable basis. |
| Human reviewer | Read scoped queue/detail and approve or reject with the matching permission. | Apply payable basis, select tariff, change policy authority, impersonate service channel. |
| Evidence viewer | View protected evidence only when separately authorized. | Mutate decision, policy, payment, fiscal, or application state. |
| Auditor | Read immutable decision, application, reviewer, and policy-reference history. | Operational mutation. |
| Policy administrator | Manage ordinance/statutory policy where separately authorized. | Automatic operational review or application authority. |
| WebPay service principal | Submit WebPay-originated request/application commands with WebPay-specific service permission. | Approve, reject, view protected evidence, administer policy. |
| APT service principal | Submit APT-originated application commands with APT-specific service permission. | Approve, reject, view protected evidence, administer policy. |
| Reconciliation/support principal | Read only the specific reconciliation or support surfaces granted. | Broad administrator authority by default. |

## Frozen Permission Catalog

| Concern | Permission identifier | Actor class | Status |
|---|---|---|---|
| Session lookup | `statutory-discounts.session.lookup` | Human request initiator | Implemented |
| Request/draft read | `statutory-discounts.draft.view` | Human request initiator, reviewer | Implemented |
| Request/draft create | `statutory-discounts.draft.create` | Human request initiator | Implemented |
| Review queue read | `statutory-discounts.review.queue.read` | Human reviewer | Implemented in catalog and endpoint metadata; canonical DB reference data pending |
| Review detail read | `statutory-discounts.review.detail.read` | Human reviewer | Implemented in catalog and endpoint metadata; canonical DB reference data pending |
| Decision review/read context | `statutory-discounts.decision.review` | Human reviewer | Implemented as read/context only |
| Approve eligibility | `statutory-discounts.decision.approve` | Human reviewer | Implemented and independently enforced |
| Reject eligibility | `statutory-discounts.decision.reject` | Human reviewer | Implemented and independently enforced |
| Evidence metadata read | `statutory-discounts.evidence.view` | Evidence viewer or reviewer with evidence permission | Implemented |
| Evidence metadata capture | `statutory-discounts.evidence.capture` | Human request initiator or evidence capture actor | Implemented |
| Policy resolution/read | `statutory-discounts.policy.resolve` | Human operational policy reader | Implemented |
| Policy read/admin target | `statutory-discount-policy.view`, `statutory-discount-policy.manage` | Policy administrator | Target/legacy Management Platform catalog, not runtime policy-management API |
| Audit read | `statutory-discounts.audit.read` | Auditor/compliance | Implemented |
| Payment-time payable-basis application | `statutory-discounts.payable-basis.apply` | WebPay/APT service boundary target | Existing endpoint permission remains; human UAT supervisor grants removed in this worktree |
| Application status read | `statutory-discounts.application.read` | Service/support/audit target | Target-only; no separate Central PMS named policy found |
| Reconciliation read | `reconciliation.view` | Reconciliation/support | Implemented |

Compatibility note: `statutory-discounts.decision.review` remains for read/context compatibility. It must not imply approve or reject.

## Named Policy Catalog

| Named policy | Accepted actor class | Permissions | Scope requirement | SoD rule | Endpoint consumers | Enforcement status |
|---|---|---|---|---|---|---|
| `OperatorConsoleStatutoryDiscountSessionLookup` | Human | `statutory-discounts.session.lookup` or `reconciliation.manage` | Site/Site Group evaluated by Operator Console access service, not RBAC middleware | None in policy | Session lookup | Implemented |
| `OperatorConsoleStatutoryDiscountDraftView` | Human | `statutory-discounts.draft.view` or `reconciliation.manage` | Operator Console access evaluation | None in policy | Draft queue/detail | Implemented |
| `OperatorConsoleStatutoryDiscountDraftCreate` | Human | `statutory-discounts.draft.create` or `reconciliation.manage` | Operator Console access evaluation | Request initiator captured | Draft create | Implemented |
| `OperatorConsoleStatutoryDiscountReviewQueueRead` | Human | `statutory-discounts.review.queue.read`, `statutory-discounts.decision.review`, or `reconciliation.manage` | Operator Console access evaluation | None in policy | Service-channel review queue | Implemented |
| `OperatorConsoleStatutoryDiscountReviewDetailRead` | Human | `statutory-discounts.review.detail.read`, `statutory-discounts.decision.review`, or `reconciliation.manage` | Operator Console access evaluation | None in policy | Service-channel review detail | Implemented |
| `OperatorConsoleStatutoryDiscountDecisionMutate` | Human | approve or reject permission to enter route; handler verifies exact decision permission | Operator Console access evaluation | Self-review only where durable initiator identity is available | Legacy draft decision and service-channel review decision | Implemented with second-layer decision-specific check |
| `OperatorConsoleStatutoryDiscountDecisionApprove` | Human | `statutory-discounts.decision.approve` or `reconciliation.manage` | Same as decision mutate | Same as decision mutate | Catalog only | Implemented in catalog |
| `OperatorConsoleStatutoryDiscountDecisionReject` | Human | `statutory-discounts.decision.reject` or `reconciliation.manage` | Same as decision mutate | Same as decision mutate | Catalog only | Implemented in catalog |
| `OperatorConsoleStatutoryDiscountEvidenceView` | Evidence viewer/human | `statutory-discounts.evidence.view` or `reconciliation.manage` | Operator Console access evaluation | Evidence access separate from queue/detail | Evidence metadata GET | Implemented |
| `OperatorConsoleStatutoryDiscountEvidenceCapture` | Human capture actor | `statutory-discounts.evidence.capture` or `reconciliation.manage` | Operator Console access evaluation | Evidence capture separate from approval | Evidence metadata POST | Implemented |
| `OperatorConsoleStatutoryDiscountPayableBasisApply` | Payment-time service target; legacy human route remains | `statutory-discounts.payable-basis.apply` or `reconciliation.manage` | Operator Console access evaluation if legacy route used | Must not be invoked by approval/rejection | Legacy Operator Console apply route | Contradictory legacy route remains; UI removed and human UAT grants removed |
| `OperatorConsoleStatutoryDiscountPolicyResolve` | Human policy reader | `statutory-discounts.policy.resolve` or `reconciliation.manage` | Operator Console access evaluation | Policy read does not grant review | Policy resolve | Implemented |
| `OperatorConsoleStatutoryDiscountAuditRead` | Auditor/compliance | `statutory-discounts.audit.read` or `reconciliation.manage` | Operator Console access evaluation filters | Read-only | Audit report | Implemented |
| `CentralPmsStatutoryDiscountDecisionSubmit` | Service or operator submitter | channel-specific submit permission or `reconciliation.manage` | Source channel maps to required permission; browser fields do not grant auth | Service channel cannot approve/reject | `POST /v1/statutory-discounts/decisions` | Implemented |
| `CentralPmsStatutoryDiscountDecisionRead` | Service/support/human read | `statutory-discounts.decision.read`, `statutory-discounts.draft.view`, or `reconciliation.manage` | Not resource-scoped in RBAC middleware | Read-only | decision read and availability | Implemented |
| `ManagementPlatformIdentityRbacInventoryRead` | Admin/MP read | `management-platform.identity-rbac.inventory.read` | Not resource-scoped | Read-only | MP inventory endpoint | Implemented |

## Route-Policy Matrix

| Route | Method | Purpose | Actor type | Current policy | Required policy | Required permissions | Scope | SoD rule | Status |
|---|---|---|---|---|---|---|---|---|---|
| `/v1/statutory-discounts/decisions/availability` | POST | Availability | Service/support reader | `CentralPmsStatutoryDiscountDecisionRead` | Same | `statutory-discounts.decision.read` or compatible read | No resource-scope middleware | Read-only | Implemented |
| `/v1/statutory-discounts/decisions` | POST | Submit request/decision command | Service/human submitter | `CentralPmsStatutoryDiscountDecisionSubmit` | Same | channel-specific submit permission | Source-channel permission map | Service channels cannot approve/reject | Implemented |
| `/v1/statutory-discounts/decisions/{id}` | GET | Decision readback | Service/support reader | `CentralPmsStatutoryDiscountDecisionRead` | Same | `statutory-discounts.decision.read` or compatible read | Not fully resource-scoped | Read-only | Implemented |
| `/v1/ops/operator-console/sessions/lookup` | POST | Ticket/session lookup | Human | `OperatorConsoleStatutoryDiscountSessionLookup` | Same | `statutory-discounts.session.lookup` | Access evaluation | None | Implemented |
| `/v1/ops/operator-console/statutory-discounts/draft` | POST | Legacy OC request draft | Human request initiator | `OperatorConsoleStatutoryDiscountDraftCreate` | Same | `statutory-discounts.draft.create` | Access evaluation | Initiator captured | Implemented |
| `/v1/ops/operator-console/statutory-discounts/drafts` | GET | Legacy draft queue | Human reader | `OperatorConsoleStatutoryDiscountDraftView` | Same | `statutory-discounts.draft.view` | Access evaluation | Read-only | Implemented |
| `/v1/ops/operator-console/statutory-discounts/drafts/{draftId}` | GET | Legacy draft detail | Human reader | `OperatorConsoleStatutoryDiscountDraftView` | Same | `statutory-discounts.draft.view` | Access evaluation | Read-only | Implemented |
| `/v1/ops/operator-console/statutory-discounts/reviews/pending` | GET | Service-channel review queue | Human reviewer | `OperatorConsoleStatutoryDiscountReviewQueueRead` | Same | queue/detail/read compatibility | Access evaluation | Read-only | Implemented |
| `/v1/ops/operator-console/statutory-discounts/reviews/{id}` | GET | Service-channel review detail | Human reviewer | `OperatorConsoleStatutoryDiscountReviewDetailRead` | Same | detail/read compatibility | Access evaluation | Read-only | Implemented |
| `/v1/ops/operator-console/statutory-discounts/reviews/{id}/decision` | POST | Service-channel approve/reject | Human reviewer | `OperatorConsoleStatutoryDiscountDecisionMutate` | Same plus handler decision check | approve for APPROVE, reject for REJECT | Access evaluation | No service identity; frozen policy authority enforced by service | Implemented |
| `/v1/ops/operator-console/statutory-discounts/{draftId}/decision` | POST | Legacy OC approve/reject | Human reviewer | `OperatorConsoleStatutoryDiscountDecisionMutate` | Same plus handler decision check | approve for APPROVE, reject for REJECT | Access evaluation | Self-review guard exists in legacy service | Implemented |
| `/v1/ops/operator-console/statutory-discounts/{draftId}/evidence` | GET | Evidence metadata read | Evidence viewer | `OperatorConsoleStatutoryDiscountEvidenceView` | Same | `statutory-discounts.evidence.view` | Access evaluation | Evidence access separate | Implemented |
| `/v1/ops/operator-console/statutory-discounts/{draftId}/evidence` | POST | Evidence metadata capture | Capture actor | `OperatorConsoleStatutoryDiscountEvidenceCapture` | Same | `statutory-discounts.evidence.capture` | Access evaluation | Capture separate from approval | Implemented |
| `/v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis` | POST | Legacy payable-basis application | Legacy operator route | `OperatorConsoleStatutoryDiscountPayableBasisApply` | Future service-only payment-time route | `statutory-discounts.payable-basis.apply` | Access evaluation | Must not be side effect of approval | Contradictory legacy route remains |
| `/v1/ops/operator-console/statutory-discounts/resolve-policy` | POST | Policy resolution read | Human policy reader | `OperatorConsoleStatutoryDiscountPolicyResolve` | Same | `statutory-discounts.policy.resolve` | Access evaluation | Read-only | Implemented |
| `/v1/ops/operator-console/audit/statutory-discounts` | GET | Audit report | Auditor/compliance | `OperatorConsoleStatutoryDiscountAuditRead` | Same | `statutory-discounts.audit.read` | Access evaluation filters | Read-only | Implemented |
| Policy import review routes under `/v1/ops/operator-console/statutory-discounts/policies/import` | POST/GET | Policy import workflow | Policy admin/reviewer | `OperatorConsolePolicyImportReview*` | Same | `operator-console.policy-import-review.*` | Import workflow rules | Policy admin does not imply runtime review | Implemented and separated from runtime statutory policies |

## Human-Versus-Service Rules

- Human review decision routes now reject any request carrying `X-ExitPass-Service-Identity-Id` or service identity claims with `OPERATOR_CONSOLE_HUMAN_REVIEWER_REQUIRED`.
- `APPROVE` requires `statutory-discounts.decision.approve`; `REJECT` requires `statutory-discounts.decision.reject`.
- `WEBPAY` and `ASSISTED_PAYMENT_TERMINAL` source-channel values remain business attribution, not authentication.
- Browser DTO fields for Site, Site Group, source channel, or permissions do not create authorization.
- Service-channel submit/read authorization remains on shared Central PMS statutory endpoints.

## Site And Site Group Scope Contract

Frozen semantics:

- Authorization scope must come from server-side grants or access-evaluation facts.
- Request `siteId` and `siteGroupId` are resource facts, not permission claims.
- Site authority permits access only to the assigned Site.
- Site Group authority permits access only to Sites in that Site Group.
- Global authority is exceptional and explicit.
- Missing, unknown, or mismatched scope fails closed.
- Site Group membership changes must not silently broaden stale cached grants.
- Evidence and audit access must honor resource scope unless a separate approved exception exists.

Current enforcement status:

- Operator Console routes use `IOperatorConsoleAccessEvaluationService` and persisted `operator_console.operator_access_evaluations`.
- `CentralPmsRbacMiddleware` enforces permission presence but does not perform Site/Site Group resource-scope matching.
- Canonical DB contains identity role/permission structures and operator access evaluations, but a full durable RBAC grant-scope model with cache invalidation semantics is not implemented in Central PMS runtime.

## Separation Of Duties

| Rule | Current status |
|---|---|
| Self-review prohibition | Partially enforced. Legacy draft decision service compares durable requester/reviewer user ids. Service-channel request initiator comparison depends on durable submitted actor identity and is not fully proven. |
| Policy administration separation | Enforced by catalog separation between `operator-console.policy-import-review.*`, `statutory-discount-policy.manage`, and runtime review permissions. Full production admin grant workflows are absent. |
| Evidence separation | Implemented at permission-policy level: evidence read/capture are separate from queue/detail and approve/reject. Raw evidence storage/viewing is not implemented here. |
| Service separation | Implemented for human review decision routes: service identity is rejected. Shared service submit/read routes remain service-capable. |
| Approve/reject distinction | Implemented by handler-level decision-specific permission check and tests. |
| Application separation | Implemented for approval/rejection side effects and local UAT human role grants. Legacy Operator Console apply endpoint remains as a compatibility risk. |

## Fail-Closed Behavior

- Missing route permission returns `CENTRAL_PMS_RBAC_FORBIDDEN` before the endpoint service is invoked.
- Service identity on human review decision route returns `OPERATOR_CONSOLE_HUMAN_REVIEWER_REQUIRED`.
- Approve with only reject permission, and reject with only approve permission, returns `OPERATOR_CONSOLE_DECISION_PERMISSION_REQUIRED`.
- Approval/rejection does not create payable-basis application-v1.
- Application-v1 remains a separate payment-time authority.

## Compatibility And Migration Notes

- No existing public route was removed.
- `OperatorConsoleStatutoryDiscountDecisionReview` is preserved but narrowed to read/context only.
- `OperatorConsoleStatutoryDiscountDecisionMutate` is an endpoint metadata compatibility policy because ASP.NET endpoint metadata cannot vary by request body decision. The endpoint handler performs the exact approve/reject permission check.
- Local/UAT role bundles were updated to remove human reviewer `statutory-discounts.payable-basis.apply`.
- Canonical generated DB reference data still includes an older UAT role mapping with `OPERATIONS_SUPERVISOR` granted `statutory-discounts.payable-basis.apply`; this requires canonical reference-data promotion before production RBAC administration can be final.

## Automated Enforcement Status

Implemented and tested:

- Review read does not imply approve/reject.
- Approve does not imply reject.
- Reject does not imply approve.
- Service identity cannot call human review decision route.
- Human apply permission does not grant approve.
- Runtime statutory permissions do not grant policy-import review.
- Policy-import review permissions do not grant runtime statutory actions.
- WebPay/APT source-channel submit policies remain separate on shared statutory endpoints.

Not implemented in this task:

- New RBAC admin APIs.
- New role/permission assignment APIs.
- Canonical DB migrations.
- Evidence-object access retrieval.
- Service identity lifecycle management.
- Full Site/Site Group scoped authorization in the RBAC middleware.
- Removal of legacy Operator Console payable-basis apply endpoint.

## Gaps And Blockers

| Gap | Severity | Blocking effect |
|---|---|---|
| Canonical generated DB reference data still grants UAT `OPERATIONS_SUPERVISOR` payable-basis apply and lacks new queue/detail/application-read permission rows. | High | Blocks production RBAC canonicalization and full H-002 write support. |
| Legacy Operator Console apply-payable-basis route still exists. UI no longer calls it and local UAT human grants are removed, but the route remains callable with the apply permission. | High | Blocks declaring Operator Console application authority fully retired. |
| RBAC middleware does not enforce durable Site/Site Group grant scope. | High | Blocks production Site-scoped RBAC administration. |
| Service-channel application permissions are not split by WebPay/APT in the current application endpoint policy. | Medium | Blocks final service-principal least-privilege contract. |
| Service-channel self-review comparison is not fully proven. | Medium | Blocks full SoD certification. |
| Evidence protected-object read is not implemented. | Medium | Evidence access catalog is frozen, but viewing/storage remains future work. |

## Authorization Posture

Central PMS statutory RBAC enforcement contract for the implemented route metadata and approve/reject split is complete.

Management Platform RBAC administration UI is blocked from production writes until canonical reference data, scope grants, grant audit history, service identity lifecycle, and mutation APIs are available.
