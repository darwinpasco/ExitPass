# ExitPass Management Platform Current State and Target Scope Audit v1.0

## Result

Result: CURRENT STATE AND TARGET SCOPE DEFINED.

This audit found no v1.3 document conflict with using **ExitPass Management Platform** as the umbrella for administrative, dashboard, reporting, reconciliation, audit, monitoring, and configuration surfaces. Current source code does not yet implement that umbrella platform or a user/role administration UI/API. Current implemented functionality is concentrated in Central PMS backend enforcement and the Operator Console workflow UI.

## Executive summary

ExitPass v1.3 should use **ExitPass Management Platform** as the umbrella application name.

Recommended target structure:

- ExitPass Management Platform
  - Central PMS Admin
  - Identity & RBAC Administration
  - Site / Site Group / Device Administration
  - POS Server / Fiscal Configuration Administration
  - Policy Administration
  - Management Dashboard & Reporting
  - Audit / Reconciliation
  - Operational Monitoring

Central PMS should remain the system of record and enforcement authority for identity/RBAC, user/site/device/shift context, policy catalog enforcement, workflow authorization, audit, and platform control records. The Management Platform should be the admin/reporting UI surface over Central PMS-backed authority. Operator Console should remain the operational workflow surface. POS Server should remain fiscal authority only and should not own normal operator users or RBAC.

Roles should be responsibility bundles, not one role per button/action. Granular access rights should be assigned to manageable role bundles. The proposed v1.3 UAT role model uses seven role bundles: System / RBAC Administrator, Platform Administrator, Operations Supervisor, Operator / Support Staff, Finance / Reconciliation Analyst, Compliance / Policy Administrator, and Executive / Management.

## v1.3 documents inspected

Recursive v1.3 search was performed across `docs/v1.3` for Management Dashboard, Reporting, Management Platform, Central PMS Admin, Admin, Operator Console, RBAC, role, permission, user, identity, device binding, shift, site, site group, POS Server, fiscal configuration, statutory discount, policy, audit, reconciliation, dashboard, monitoring, and UAT.

| Document | Relevant finding |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Defines Central PMS as payment-linked platform control authority; POS Server as fiscal authority; Site Group as lookup/payment scope; Site as reporting, contract, Vendor PMS mapping, POS Server, and operational boundary. Dashboard/reporting must distinguish operational projection visibility from financial truth. Exact permission matrix remains open. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Confirms authority separation across Central PMS, POS Server, Operator Console, Management Dashboard, connectors, payment, and gate systems. |
| `docs/v1.3/system-design/input-packs/04_security_trust_and_rbac_input.md` | Defines human roles, device trust, RBAC domains, audit scope, and Management Dashboard as visibility/reporting only. Administrator may manage approved user, role, device, Site/Site Group, policy, reporting access, export permission, and dashboard configuration where in scope. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console is internal operator/supervisor governance, not executive dashboard or broad admin. It includes statutory discount, evidence, supervisor review, fiscal exception visibility, continuity/manual release governance, audit, and operational reporting where scoped. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` | Operator Console is an operations/governance surface. It is not payment, fiscal, exit, or gate authority. It uses ExitPass identity services, RBAC, site/device/shift checks, and Central PMS-backed services. Administrator role is mentioned, but implementation details remain deferred. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Management Dashboard and Reporting is a companion business domain for visibility, monitoring, reports, exports, and audit support. It is not Operator Console, Central PMS, POS Server, payment authority, fiscal authority, exit authority, or discount authority. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_System_Design_SDD_v1.0.md` | Defines dashboard as read-model/reporting layer with optional Dashboard API/BFF, RBAC/report-scope guard, report catalog, export service, and access logging. It does not mutate source-of-truth records. |
| `docs/v1.3/management-dashboard-reporting/reviews/ExitPass_Management_Dashboard_and_Reporting_System_Design_SDD_Review_v1.0.md` | Confirms dashboard visibility-only boundary, no direct POS Server/provider/vendor/gate calls, RBAC/export audit, and Operator Console handoff without workflow execution. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | POS Server owns fiscal issuance, fiscal reports, fiscal retention, and fiscal exports. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` and POS Server API/DB docs | POS Server is site fiscal authority and idempotency/fiscal document system. It is not Central PMS identity/RBAC owner. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` and HikCentral connector docs | Vendor/HikCentral identity remains vendor-side and maps through AdapterMapping. Connector reports health/projection facts; Central PMS owns platform state and reporting attribution. |
| `docs/v1.3/assisted-payment-terminal/*` | Assisted Payment Terminal is a terminal workflow surface with user/device/shift accountability and backend-controlled discount/payment flow. It does not approve statutory entitlement independently. |
| `docs/v1.3/central-pms/*` fiscal and engineering docs | Central PMS records fiscal references and exposes read/status surfaces; dashboard visibility is read-only and cannot mutate fiscal state. |

No v1.3 source inspected defines "ExitPass Management Platform" as an existing formal product, but the term is consistent with v1.3 authority boundaries if used as the umbrella above Management Dashboard & Reporting and Central PMS Admin.

## Current-state inventory

| Area | Current source/doc state | Owner today | Mutating? | RBAC/security posture | Target home |
| --- | --- | --- | --- | --- | --- |
| Operator Console UI | Implemented in `src/Services/OperatorConsoleUi`. Routes include ticket lookup, fiscal status, statutory discounts, audit/reporting, fiscal view audit, Sales Invoice void audit, vendor acknowledgments, projection health, and policy import review. | Operator Console UI over Central PMS | Mixed: lookup/read plus controlled statutory discount and fiscal void actions | Local/dev fallback context plus Central PMS access evaluation and policy catalog | Operator Console |
| Operator Console access evaluation | Implemented in Central PMS `/v1/ops/operator-console/access/*`; uses identity user, site, device, shift, action metadata, and action logs. | Central PMS | Persists access/action evidence only | RBAC/action catalog, device/site/shift checks | Central PMS backend; admin visibility in Management Platform |
| RBAC policy catalog | Implemented as `CentralPmsRbacPolicyCatalog`, mapping policies to permissions. | Central PMS | No admin mutation API found | Header/claim-based permission evaluation; explicit policy names | Central PMS enforcement; Management Platform admin UI/API needed |
| User/operator identity context | Source reads `identity.users` and local fixture scripts seed users, HR mappings, device bindings, shifts. No source-backed user management UI/API found. | Central PMS DB/source model | Fixture scripts mutate local DB | Local/dev/header-driven in current UI; backend read checks exist | Central PMS Admin / Identity & RBAC Administration |
| Site / Site Group / device / shift context | Existing tables/fixtures and access evaluation read model; no admin UI/API found. | Central PMS | Fixture scripts only in current repo inspection | Required for Operator Console authorization | Management Platform admin modules over Central PMS |
| Fiscal status lookup | Operator Console route `/operator-console/fiscal-issuance-status`; backend facade under `/v1/ops/operator-console/fiscal-issuance/*`. | Central PMS facade; POS Server source readback where configured | Read-only status; fiscal void action is controlled mutation | Fiscal status read and void command permissions | Operator Console workflow; reporting summaries in Management Platform |
| Sales Invoice fiscal void action/audit | Implemented Operator Console action endpoint and audit review page. | Central PMS calls POS Server command client | Controlled mutation for void; audit page read-only | `FiscalIssuanceVoidCommand`, `FiscalVoidActionAuditReview` | Operator Console workflow and audit review; aggregate reporting in Management Platform |
| Statutory discounts | Implemented draft, evidence, decision, apply payable basis, policy resolution, audit report, tests, UAT smoke fixture. | Central PMS discount workflow exposed through Operator Console | Mutating workflow | Explicit statutory discount permissions and requester/approver segregation | Operator Console workflow; policy/admin/reporting in Management Platform |
| Policy import review | Implemented Operator Console route and Central PMS endpoints/services. | Central PMS | Mutating policy review workflow | Separate policy import permissions | Management Platform policy administration target; temporary Operator Console route acceptable |
| Vendor acknowledgments | Operator Console read/monitoring route; Central PMS ops endpoints. | Central PMS / vendor connector read models | Read-only monitoring in UI | Reconciliation/view permissions | Operator Console operations and Management Platform reporting |
| Projection health | Operator Console route and Central PMS `/v1/ops/vendor-session-projections/*` endpoints. | Central PMS connector/projection state | Read-only ops visibility | Projection health/view permissions | Operator Console technical ops and Management Platform monitoring |
| Reconciliation | Central PMS `/v1/ops/reconciliation/*` endpoints and services exist for workflow/evaluation/lifecycle. No dedicated management UI shell found. | Central PMS | Mixed: review, lifecycle, notes, evaluation | Reconciliation policies and permissions | Management Platform Audit/Reconciliation; focused operator surfaces where needed |
| Management Dashboard & Reporting | v1.3 BRD/SDD exist and are approved as visibility/reporting design. No source-backed Management Dashboard UI module found from current inspection. | Documented future/target domain | Should be read/report/export only | Must have RBAC/report-scope/export audit | Module under ExitPass Management Platform |
| Central PMS Admin | No implemented admin app/module found. Central PMS backend has authority models and enforcement. | Central PMS | Not implemented as admin product | N/A beyond existing enforcement | Module under ExitPass Management Platform |
| POS Server fiscal configuration references | Central PMS and POS docs define Site POS Server/fiscal identity/fiscal routing; current repo has fiscal reference/status logic. No full admin UI found. | Central PMS config plus POS Server fiscal authority | Not confirmed as admin UI | Must be high-risk audited config | Management Platform admin module; POS Server owns fiscal execution |

## Existing Management Dashboard and Reporting status

Management Dashboard and Reporting is currently a documented v1.3 product/design domain, not a source-backed UI module confirmed from current inspection.

It should remain a module under ExitPass Management Platform, not become the umbrella app by itself. Its v1.3 boundary is visibility/reporting/export/audit only. It should not absorb user management, RBAC administration, policy administration, fiscal configuration administration, or operational workflow actions.

## Recommended target structure

Use **ExitPass Management Platform** as the umbrella.

| Module | Scope | Backend authority | UAT priority |
| --- | --- | --- | --- |
| Central PMS Admin | Administrative control over Central PMS-owned platform records and configuration. | Central PMS | Now |
| Identity & RBAC Administration | Users, roles, permissions, assignments, role-to-permission mapping, local UAT identities, external IAM mapping. | Central PMS | Now |
| Site / Site Group / Device Administration | Site hierarchy, device bindings, operator site assignments, shift/session controls. | Central PMS | Now |
| POS Server / Fiscal Configuration Administration | Site POS Server mapping, fiscal identity/policy assignment, service trust references; no fiscal execution. | Central PMS config; POS Server fiscal execution | Later, before production fiscal rollout |
| Policy Administration | Statutory discount policy, evidence rules, policy import review, effective dates. | Central PMS | Now/later split |
| Management Dashboard & Reporting | Operational, financial, fiscal, discount, compliance, audit, export, and dashboard reporting. | Central PMS/read models/audit/reconciliation/POS evidence | Later for broad dashboard; now for targeted audit reports |
| Audit / Reconciliation | Audit review, reconciliation exception views/workflows, export controls. | Central PMS/audit/reconciliation | Now |
| Operational Monitoring | Connector health, projection freshness, vendor acknowledgment backlog, system health. | Central PMS/connectors | Now/later split |

## Central PMS Admin scope

Central PMS Admin should be a module inside ExitPass Management Platform, not the umbrella app name.

It should manage Central PMS-owned administrative state:

- Identity/RBAC policy catalog administration.
- User and role assignment.
- Site/Site Group assignment.
- Device binding and shift/operator context governance.
- Service identity and trusted integration configuration where Central PMS owns it.
- Policy registry and statutory discount policy administration.
- Site POS Server routing/fiscal configuration assignment, without becoming POS Server fiscal execution.
- Audit of admin changes.

No current source-backed Central PMS Admin UI/API was confirmed.

## Management Dashboard and Reporting scope

Management Dashboard & Reporting should remain a Management Platform module. Its scope is dashboards, reports, monitoring, exports, and reporting audit.

It should provide:

- Operational visibility over sessions, connector health, projection freshness, exceptions, and continuity posture.
- Finance/revenue visibility from canonical payment, provider, fiscal, and reconciliation records.
- Fiscal visibility from Central PMS fiscal references and POS Server evidence recorded through Central PMS.
- Statutory discount compliance and audit reporting.
- RBAC-scoped exports with source/freshness labels.

It should not create payment finality, fiscal documents, ExitAuthorization, gate actions, statutory discount decisions, payable-basis changes, continuity activation, manual release, or reconciliation closure unless a later approved design explicitly assigns a workflow action.

## Operator Console boundary

Operator Console should remain the operational workflow surface.

It should include:

- Ticket lookup.
- Statutory discount draft/evidence/review/apply workflows.
- Sales Invoice status lookup.
- Controlled Sales Invoice void action.
- Operational audit views tied to operator workflows.
- Vendor acknowledgments.
- Projection health.
- Operational/fiscal exception views where workflow-specific.

It should not be the primary place for:

- User management.
- Role/permission management.
- Global permission matrix administration.
- Site/POS Server/fiscal configuration administration.
- Executive dashboarding.
- Broad reporting administration.

Current temporary admin-like pages in Operator Console, such as policy import review and audit reports, can remain during UAT but should be candidates for migration or cross-linking into Management Platform modules once the platform shell exists.

## POS Server boundary

POS Server should own:

- Fiscal document issuance.
- Fiscal sequence authority.
- Fiscal identity and fiscal policy execution assigned to it.
- Fiscal idempotency.
- Fiscal document readback.
- Fiscal reports and fiscal exports.
- Service trust for Central PMS fiscal calls.

POS Server should not own:

- Operator users.
- Normal RBAC roles/permissions.
- Statutory discount approval.
- Payment finality.
- Gate/ExitAuthorization.
- User management UI.
- Central reporting authority.

POS Server may own or trust service identity, Site POS Server identity, mTLS/service credentials, and fiscal authority configuration assigned by Central PMS.

## Identity and RBAC ownership

Recommended ownership:

| Capability | System of record / authority | UI surface |
| --- | --- | --- |
| User identity | Central PMS identity model, with future external IAM mapping | Management Platform Identity Admin |
| Roles and permissions | Central PMS RBAC policy catalog/enforcement | Management Platform RBAC Admin |
| Role-to-permission mapping | Central PMS | Management Platform RBAC Admin |
| Site/Site Group assignments | Central PMS | Management Platform Site/User Admin |
| Device bindings | Central PMS | Management Platform Device Admin |
| Shift/active operator context | Central PMS | Operator Console consumes; Management Platform administers/reports |
| Local UAT users | Central PMS local/dev fixture data | Management Platform or fixture scripts until admin UI exists |
| External IAM integration | Future identity provider mapped into Central PMS permissions and scopes | Management Platform config/admin |
| User/role/permission audit | Central PMS audit/event model | Management Platform Audit |

Central PMS owns identity/RBAC enforcement and should remain the authority behind user, role, permission, assignment, device, shift, and site-scope checks. Management Platform provides the admin UI for that authority. Operator Console consumes permissions for workflow actions and displays safe access denial reasons. POS Server does not own operator users or normal RBAC.

User, role, permission, and assignment administration belongs under ExitPass Management Platform backed by Central PMS. RBAC administration does not automatically grant business workflow authority; workflow permissions must be explicitly assigned.

## Minimum v1.3 UAT user/role model

| Role | Purpose | Typical access rights | Default restrictions | Scope | Surface |
| --- | --- | --- | --- | --- | --- |
| System / RBAC Administrator | Owns platform-level identity and access administration, user management, role/permission administration, assignment governance, access configuration, and access audit visibility. | `user.view`, `user.manage`, `rbac.view`, `rbac.manage`, `role.view`, `role.manage`, `permission.view`, `permission.manage`, `assignment.view`, `assignment.manage`, `access-audit.view`, `device-assignment.manage`, `site-assignment.manage`, `shift-assignment.manage` where needed. | No automatic statutory discount approval, Sales Invoice void authority, payment/reconciliation mutation, policy approval, fiscal configuration mutation, or business workflow authority. Business workflow permissions must be separately granted. | Platform-wide or delegated admin scope. | ExitPass Management Platform -> Identity & RBAC Administration |
| Platform Administrator | Manages operational platform configuration needed for ExitPass to run across sites, site groups, devices, POS Server assignments, vendor connector settings, operational readiness, and environment/UAT setup. | `site.view`, `site.manage`, `site-group.view`, `site-group.manage`, `device.view`, `device.manage`, `device-binding.view`, `device-binding.manage`, `shift.view`, `shift.manage`, `pos-server-config.view`, `pos-server-config.manage`, `connector-config.view`, `connector-config.manage`, `operational-monitoring.view`, `platform-config.view`, `platform-config.manage`, `environment-config.view`, `uat-fixture.manage` where applicable. | No user/RBAC administration, statutory discount approval, Sales Invoice void authority, payment/fiscal/gate authority, or report export unless separately granted. | Platform configuration scope, usually site/site-group constrained where possible. | ExitPass Management Platform -> Central PMS Admin / Platform Configuration |
| Operations Supervisor | Supervises operational workflows and reviews higher-trust operational decisions such as statutory discount approval, payable-basis application, operational exceptions, and controlled Sales Invoice void authority where allowed. | `statutory-discounts.draft.view`, `statutory-discounts.evidence.view`, `statutory-discounts.decision.review`, `statutory-discounts.decision.approve`, `statutory-discounts.decision.reject`, `statutory-discounts.payable-basis.apply`, `fiscal-issuance.status.read`, `fiscal-issuance.void.command` where allowed, `vendor-acknowledgments.view`, `projection-health.view`, `operator-workflow-audit.view`. | No user/RBAC administration, global platform configuration administration, policy import approval, or report export unless separately granted. Cannot approve own statutory discount request due to workflow segregation. | Assigned site/site-group, shift/device where operational. | Operator Console for workflow; Management Platform for supervisor reports |
| Operator / Support Staff | Performs site-scoped operational lookup and support workflows, including ticket/session lookup, draft initiation where allowed, metadata-only evidence capture where allowed, and status viewing. | `statutory-discounts.session.lookup`, `statutory-discounts.draft.view`, `statutory-discounts.draft.create` where allowed, `statutory-discounts.evidence.view`, `statutory-discounts.evidence.capture` where allowed, `fiscal-issuance.status.read`, `ticket.lookup`, `vendor-acknowledgments.view` where allowed, `projection-health.view` where allowed. | Cannot approve own statutory discount; no statutory discount approval, payable-basis apply, Sales Invoice void, admin/RBAC/configuration authority, payment provider, gate, ExitAuthorization, refund/reversal, or fiscal issuance authority unless separately granted by approved policy. | Assigned site/site-group, active shift, trusted device. | Operator Console |
| Finance / Reconciliation Analyst | Reviews financial, payment, fiscal, discount, and reconciliation records. Handles reconciliation workflows and financial reporting according to assigned permissions. | `reconciliation.view`, `reconciliation.manage` where allowed, `payment-report.view`, `fiscal-report.view`, `sales-invoice-report.view`, `statutory-discount-report.view`, `revenue-report.view`, `variance-report.view`, `reports.view`, `reports.export` where allowed. | No operational statutory discount approval, Sales Invoice void, user/RBAC administration, platform configuration administration, or gate/ExitAuthorization authority unless separately granted. | Finance/reconciliation reporting scope by site, site group, portfolio, or assigned queue. | ExitPass Management Platform -> Audit / Reconciliation and Management Dashboard & Reporting |
| Compliance / Policy Administrator | Owns compliance oversight, audit review, statutory discount policy governance, evidence rule governance, policy import/review workflows, regulatory reports, and policy lifecycle controls. | `statutory-discounts.audit.read`, `fiscal-issuance.void.audit.read`, `fiscal-view-audit.read`, `audit-report.view`, `policy-import.submit`, `policy-import.review`, `policy-import.approve` where allowed, `policy-import.manage` where allowed, `statutory-discount-policy.view`, `statutory-discount-policy.manage`, `evidence-rule-policy.view`, `evidence-rule-policy.manage`, `compliance-report.view`, `reports.export` where allowed. | No automatic operator workflow mutation, user/RBAC administration, payment/gate/fiscal issuance authority, or Sales Invoice void command unless separately granted. | Compliance, policy, and audit scope. | ExitPass Management Platform -> Policy Administration and Audit / Compliance |
| Executive / Management | Provides read-only executive and management visibility for dashboards, KPIs, reports, operational performance, financial/reconciliation summaries, exception trends, statutory discount summaries, fiscal issuance summaries, and site/POS Server performance. | `dashboard.view`, `reports.view`, `executive-summary.view`, `site-performance.view`, `site-group-performance.view`, `revenue-summary.view`, `payment-summary.view`, `fiscal-summary.view`, `statutory-discount-summary.view`, `exception-trend.view`, `operational-monitoring.view`, `reports.export` only if explicitly granted. | Read-only by default. No operator workflow mutation, statutory discount approval, payable-basis apply, Sales Invoice void, user/RBAC administration, platform configuration administration, payment/gate/fiscal authority, or report export unless explicitly granted. | Portfolio, site group, site, or contracted management scope. | ExitPass Management Platform -> Management Dashboard & Reporting |

System / RBAC Administrator controls who can access what. Platform Administrator controls what the platform is configured to operate.

Requester-vs-approver segregation is a workflow rule enforced through backend decision logic and permissions. It should not be represented by creating excessive micro-roles. A role may contain draft-create and evidence-capture permissions while approval requires a different user with approval permission. Evidence-capturer-vs-approver separation remains a policy decision unless later required by v1.3 policy or compliance approval.

## Permissions and access rights catalog

These are granular access rights, not roles. They should be assigned through manageable role bundles and scoped by site, site group, device, shift, report sensitivity, and policy where applicable.

| Category | Access rights |
| --- | --- |
| Statutory discount | `statutory-discounts.session.lookup`, `statutory-discounts.draft.view`, `statutory-discounts.draft.create`, `statutory-discounts.evidence.view`, `statutory-discounts.evidence.capture`, `statutory-discounts.decision.review`, `statutory-discounts.decision.approve`, `statutory-discounts.decision.reject`, `statutory-discounts.payable-basis.apply`, `statutory-discounts.policy.resolve`, `statutory-discounts.audit.read` |
| Fiscal / Sales Invoice | `fiscal-issuance.status.read`, `fiscal-issuance.void.command`, `fiscal-issuance.void.audit.read`, `fiscal-view-audit.read`, `sales-invoice-report.view` |
| Policy | `policy-import.submit`, `policy-import.review`, `policy-import.approve`, `policy-import.manage`, `statutory-discount-policy.view`, `statutory-discount-policy.manage`, `evidence-rule-policy.view`, `evidence-rule-policy.manage` |
| Audit / reconciliation / reporting | `reconciliation.view`, `reconciliation.manage`, `audit-report.view`, `reports.view`, `reports.export`, `dashboard.view`, `executive-summary.view` |
| Administration | `user.view`, `user.manage`, `rbac.view`, `rbac.manage`, `role.view`, `role.manage`, `permission.view`, `permission.manage`, `assignment.view`, `assignment.manage`, `site.view`, `site.manage`, `site-group.view`, `site-group.manage`, `device.view`, `device.manage`, `device-binding.view`, `device-binding.manage`, `shift.view`, `shift.manage`, `pos-server-config.view`, `pos-server-config.manage`, `connector-config.view`, `connector-config.manage`, `platform-config.view`, `platform-config.manage` |
| Operational monitoring | `projection-health.view`, `vendor-acknowledgments.view`, `operational-monitoring.view` |

## Required admin modules

| Module | Purpose | Location | Backend authority | UAT necessity | Risk | First slice |
| --- | --- | --- | --- | --- | --- | --- |
| User Management | Create/activate/deactivate users and manage identity attributes. | Management Platform | Central PMS | Now | High | Read-only user inventory API before mutation APIs |
| Role Management | Define manageable responsibility bundles and role labels. | Management Platform | Central PMS | Now | High | Read-only role catalog using the simplified seven-role UAT model |
| Permission Management | Expose granular permissions/access rights and role-to-permission mapping. | Management Platform | Central PMS | Now | High | Permission catalog API with no mutation first |
| Assignment Management | Manage user-role-site/device/shift assignments. | Management Platform | Central PMS | Now | High | Read-only assignment inventory before mutation APIs |
| Site/Site Group Management | Manage site hierarchy and assignments. | Management Platform | Central PMS | Now/later | High | Read-only site/site group inventory and assignment report |
| Device Binding Management | Register and assign trusted devices. | Management Platform | Central PMS | Now for UAT stability | High | Device binding inventory/read API |
| Shift / Operator Session Management | View/start/end/revoke shifts where approved. | Management Platform admin/report; Operator Console consumes | Central PMS | Now/later | High | Shift visibility and two-user UAT support |
| POS Server / Fiscal Configuration Management | Assign Site POS Server, fiscal identity, and fiscal routing. | Management Platform | Central PMS config + POS Server fiscal execution | Later before production | Critical | Read-only fiscal config inventory |
| Policy Administration | Statutory discount policy versions, evidence rules, effective dates, and import review. | Management Platform | Central PMS | Now/later split | High | Move policy import review under Management Platform target |
| Audit / Reconciliation | Audit reports, reconciliation status/workflow, export audit. | Management Platform | Central PMS/audit/reconciliation | Now | High | Consolidated audit/reconciliation navigation |
| Dashboard / Reporting | Role/scope dashboards and exports. | Management Platform | Central PMS/read models | Later for broad dashboard | Medium/High | Shell + dashboard read-only landing |
| Operational Monitoring | Projection health, vendor acknowledgment backlog, service status. | Management Platform with Operator Console operations links | Central PMS/connectors | Now/later | Medium | Projection/vendor monitoring module consolidation |

For User Management, Role Management, Permission Management, and Assignment Management, the first implementation should be read-only inventory before mutation APIs. Admin mutation must be audited. System / RBAC Administrator authority does not imply business workflow authority.

## Gaps and risks

| Gap | Risk |
| --- | --- |
| No implemented Management Platform shell found. | Admin/reporting UX will continue to sprawl through Operator Console. |
| No source-backed user/role/admin API found. | Manual UAT remains header/local-fixture driven and awkward for reviewer/requester switching. |
| RBAC catalog exists but no admin surface. | Permissions can be hardened in code but not operated safely by administrators. |
| Management Dashboard is documented but not implemented as source UI. | Reporting and visibility will remain fragmented across Operator Console pages and backend endpoints. |
| Some Operator Console pages are doing audit/reporting and policy import review. | Operational workflow surface may keep absorbing admin/reporting duties unless target platform is created. |
| v1.3 exact role matrix remains open in docs. | UAT roles need a practical minimum now, but production role matrix still needs approval. |
| POS Server/fiscal configuration administration is not confirmed as implemented. | Fiscal routing/config changes may remain script/manual and high-risk. |
| Site/device/shift admin is fixture-based in current inspection. | Operator access failures are hard to diagnose and repeat in UAT. |
| Too many micro-roles would make UAT and production administration hard to manage. | Role assignment becomes fragile, hard to audit, and difficult for site administrators to understand. |
| Granular permissions are needed but must be assigned through manageable role bundles. | A flat permission list without role bundles will recreate the current header/local-fixture complexity. |
| Executive / Management access must be read-only by default. | Management users could unintentionally receive workflow mutation authority. |
| Platform Administrator and System / RBAC Administrator must remain distinct. | One person could both configure operations and grant themselves authority unless duties are separated and audited. |

## Recommended next high-value slices

1. Create a read-only Central PMS identity/RBAC inventory API for users, role bundles, granular permissions, assignments, site scopes, device bindings, and active shifts.
2. Define and seed the minimum v1.3 UAT user/role/permission model using the seven simplified role bundles, including requester/reviewer pairs for statutory discount UAT.
3. Add a permission catalog API that exposes access rights separately from roles.
4. Add user-role-site/device/shift assignment inventory so UAT no longer depends on implicit local headers.
5. Add Central PMS admin audit events for user/role/permission/site/device/shift changes before enabling mutation APIs.
6. Create an ExitPass Management Platform shell with modules for Central PMS Admin, Identity & RBAC Administration, Management Dashboard & Reporting, Audit/Reconciliation, and Operational Monitoring.
7. Add read-only dashboard/reporting module landing with source labels and no mutation actions.
8. Move or cross-link policy import review from Operator Console into Management Platform Policy Administration.
9. Add read-only Site/Site Group, device binding, shift, and fiscal configuration inventory pages.
10. Replace local/dev header-driven operator switching with a controlled UAT identity selector or admin-managed local login context.

## Files changed

- Updated `docs/v1.3/management-platform/ExitPass_Management_Platform_Current_State_and_Target_Scope_Audit_v1.0.md`.

No source code, tests, runtime configuration, or POS Server files were changed.

## Validation

Validation performed:

- `git diff --check`
- `git status --short --untracked-files=all`

No test suite was run because this is a doc-only current-state/target-scope audit and no source code was changed.
