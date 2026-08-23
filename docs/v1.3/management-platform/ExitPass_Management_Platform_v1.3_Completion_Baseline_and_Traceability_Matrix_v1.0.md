\# ExitPass Management Platform v1.3 Completion Baseline and Traceability Matrix v1.0



\## 1. Document Control



| Field | Value |

| --- | --- |

| Document | ExitPass Management Platform v1.3 Completion Baseline and Traceability Matrix |

| Version | 1.0 |

| Status | Proposed completion baseline for review |

| Prepared | 2026-08-20 |

| Product | ExitPass Management Platform |

| Frontend repository | `darwinpasco/ExitPass-ManagementPlatform` |

| Frontend baseline | `develop` at `eed4aeef1b16116e847de701df843b14fb416516` |

| Central PMS repository | `darwinpasco/ExitPass` |

| Central PMS baseline | `dev` at `89ddf5db462fab1e1694d2cf8320d37d5f6cbf5d` |

| Overall result | `PARTIAL` |



\## 2. Purpose



This document defines what â€œManagement Platform v1.3 completeâ€ means and traces that completion baseline to the authoritative v1.3 requirements, current frontend, Central PMS backend, tests, acceptance evidence, and remaining work.



The baseline prevents a completed implementation slice, such as H-007 User Administration, from being mistaken for completion of the entire Management Platform.



This document is a delivery control and traceability artifact. It does not change authority boundaries, business rules, API behavior, database objects, or approved component responsibilities.



\## 3. Source Hierarchy



The following sources control this baseline, in descending order:



1\. Approved ExitPass v1.3 BRDs and authority decisions.

2\. Approved companion BRDs, especially Management Dashboard and Reporting.

3\. v1.3 System Design and companion technical designs.

4\. v1.3 identity, authentication, user/role/scope, statutory, fiscal, reconciliation, and operational contracts.

5\. Merged Central PMS and Management Platform implementation contracts, source, tests, proof records, and direct manual validation.

6\. Current-state audits and planning notes where they do not conflict with later merged implementation evidence.



The attached v1.2 document pack is not used as the functional source of truth for this baseline.



\### 3.1 Authoritative references



\- `docs/v1.3/ExitPass\_BRD\_v1.3.md`

\- `docs/v1.3/ExitPass\_System\_Design\_v1.3.md`

\- `docs/v1.3/ExitPass\_v1.3\_BRD\_Approval\_Baseline.md`

\- `docs/v1.3/management-platform/ExitPass\_Management\_Platform\_Current\_State\_and\_Target\_Scope\_Audit\_v1.0.md`

\- `docs/v1.3/management-dashboard-reporting/ExitPass\_Management\_Dashboard\_and\_Reporting\_BRD\_v1.0.md`

\- `docs/v1.3/management-dashboard-reporting/ExitPass\_Management\_Dashboard\_and\_Reporting\_System\_Design\_SDD\_v1.0.md`

\- `docs/v1.3/identity/ExitPass\_Human\_Identity\_Authentication\_and\_Session\_Contract\_v1.0.md`

\- `docs/v1.3/identity/ExitPass\_User\_Role\_and\_Scope\_Administration\_Contract\_v1.0.md`

\- `docs/v1.3/identity/ExitPass\_Central\_PMS\_Human\_Identity\_Administration\_APIs\_Implementation\_Note\_v1.0.md`

\- `docs/v1.3/identity/i022/ExitPass\_Cross\_Application\_Authentication\_Authorization\_Traceability\_Matrix\_v1.0.md`



\### 3.2 Current implementation references



\- `ExitPass-ManagementPlatform/src/App.tsx`

\- `ExitPass-ManagementPlatform/src/HumanAuthenticationShell.tsx`

\- `ExitPass-ManagementPlatform/src/IdentityAdministrationPage.tsx`

\- `ExitPass-ManagementPlatform/src/SalesInvoiceProfilesPage.tsx`

\- `ExitPass-ManagementPlatform/src/RbacInventoryPage.tsx`

\- `ExitPass-ManagementPlatform/src/PolicyCoveragePage.tsx`

\- `ExitPass-ManagementPlatform/src/EvidenceGovernancePage.tsx`

\- `ExitPass-ManagementPlatform/contracts/management-platform/\*.json`

\- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/ManagementPlatformIdentityAdministrationEndpoints.cs`

\- `src/Services/CentralPms/src/ExitPass.CentralPms.Api/Endpoints/ManagementPlatformSalesInvoiceProfileAdministrationEndpoints.cs`

\- Management Platform pull request 7 and ExitPass pull request 634.



\## 4. Authority and Product Boundary



ExitPass Management Platform is the browser-facing umbrella for administrative, dashboard, reporting, reconciliation, audit, monitoring, and configuration functions.



Central PMS remains authoritative for:



\- human identity and sessions;

\- roles, permissions, assignments, and scope;

\- Site, Site Group, device, and shift context;

\- payment-linked platform control state;

\- policy enforcement and platform configuration;

\- authorization, audit, and administrative mutation enforcement.



POS Server remains authoritative for Sales Invoice issuance, fiscal document lifecycle, sequence control, fiscal reports, and fiscal evidence.



Operator Console remains the operational workflow surface. WebPay and APT remain payment-channel surfaces. The Management Platform must not declare payment finality, issue ExitAuthorization, open gates, issue Sales Invoices, approve statutory entitlement through dashboard actions, or treat projection data as financial truth.



\## 5. Completion Status Vocabulary



| Status | Meaning |

| --- | --- |

| `IMPLEMENTED\_AND\_ACCEPTED` | Backend and frontend behavior are implemented where applicable, automated evidence passes, and required manual or controlled acceptance exists. |

| `IMPLEMENTED\_NOT\_ACCEPTED` | Required behavior is implemented and tested, but final manual, integration, controlled UAT, or production-readiness acceptance is absent. |

| `PARTIAL` | Some required behavior exists, but one or more material capabilities, enforcement paths, UI workflows, or tests are missing. |

| `NOT\_IMPLEMENTED` | No conforming implementation was found at the audited baselines. |

| `FORMALLY\_DEFERRED` | An authoritative v1.3 decision explicitly excludes the capability from the completion baseline. An unresolved question is not a formal deferral. |

| `NOT\_APPLICABLE` | The requirement does not belong to the Management Platform under the approved authority boundary. |



\## 6. Completion Rule



Management Platform v1.3 may be declared complete only when all of the following are true:



1\. Every applicable traceability row is `IMPLEMENTED\_AND\_ACCEPTED` or `FORMALLY\_DEFERRED`.

2\. No Critical or High gap remains open.

3\. Every unsafe operation is enforced by Central PMS or the authoritative backend, not by browser presentation state.

4\. Every module has positive, negative, scope, stale-state, session-loss, and audit tests appropriate to its risk.

5\. Cross-Site and cross-Site-Group denial is proven for scoped functions.

6\. Human and service identities remain separate.

7\. Dashboard and report metrics identify source, freshness, and authority classification.

8\. Sensitive exports and evidence access are permissioned and audited.

9\. Integrated headed validation covers all delivered modules.

10\. A final requirements trace confirms zero unclassified applicable rows.



\## 7. Current Module Summary



| Module | Current status | Current evidence | Completion finding |

| --- | --- | --- | --- |

| Platform shell and human authentication | `IMPLEMENTED\_AND\_ACCEPTED` | H-006 implementation, I-020/I-022 integration, merged H-007 regression evidence | Complete for v1.3 current scope |

| User Management | `PARTIAL` | H-007 list, detail, Add User, profile, lifecycle, MFA, sessions, access review, activity log | Credential-reset UI and final activation/delivery posture remain |

| Role Management | `PARTIAL` | Role catalog, user-role assignment/revocation, elevated access request/decision | Role definition lifecycle is absent |

| Permission Management | `PARTIAL` | Read-only permission and RBAC inventory | Role-permission binding administration is absent |

| Assignment Management | `PARTIAL` | User-role and Site/Site Group grants | Device and shift assignment are absent |

| Site/Site Group Administration | `PARTIAL` | Existing scopes can be assigned to users | Site hierarchy and membership administration are absent |

| Device Binding Administration | `NOT\_IMPLEMENTED` | No Management Platform module found | Required |

| Shift and Operator Context Administration | `NOT\_IMPLEMENTED` | No Management Platform module found | Required |

| Sales Invoice Configuration | `IMPLEMENTED\_NOT\_ACCEPTED` | Registered Business and Sales Invoice Setup read/manage/approve/retire workflows and proofs | Requires final integrated acceptance |

| POS Server and Fiscal Configuration | `PARTIAL` | Sales Invoice configuration only | Site POS Server routing and trust/configuration are absent |

| Policy Administration | `PARTIAL` | Policy coverage and evidence governance are read-only | Governed write lifecycle is absent |

| Management Dashboard and Reporting | `PARTIAL` | Runtime-accepted core dashboard, Payment and Reconciliation, and Fiscal Exception backend/UI foundations | Management activity reporting, remaining partial operational sources, exports, schedules, and wider dashboard acceptance remain |

| Audit and Reconciliation | `PARTIAL` | User activity and limited governance views | Cross-domain audit and reconciliation modules are absent |

| Operational Monitoring | `PARTIAL` | Central PMS exposes scoped Site status and vendor projection-health aggregates | Management Platform module and broader monitoring sources remain |

| Service Identity Administration | `NOT\_IMPLEMENTED` | No Management Platform module found | Required before production administration completion |



Overall conclusion: `MANAGEMENT\_PLATFORM\_V1\_3\_PARTIAL`.



\### 7.1 Trace row distribution



| Status | Rows |

| --- | ---: |

| `IMPLEMENTED\_AND\_ACCEPTED` | 29 |

| `IMPLEMENTED\_NOT\_ACCEPTED` | 7 |

| `PARTIAL` | 18 |

| `NOT\_IMPLEMENTED` | 31 |

| Total | 85 |



These counts are an inventory, not a completion percentage. The rows have different risk and delivery weight. In particular, the missing Dashboard and Reporting domain contains an approved BRD with 42 functional requirements and cannot be treated as equivalent to one minor UI control.



\## 8. Detailed Requirements Traceability Matrix



The `MP-\*` identifiers below are trace identifiers created by this baseline. They do not replace source requirement identifiers.



\### 8.1 Foundation, authentication, and browser boundary



| Trace ID | Requirement | Source | Backend | Frontend | Evidence | Status | Remaining action |

| --- | --- | --- | --- | --- | --- | --- | --- |

| MP-FND-001 | Provide a standalone Management Platform shell and permission-aware navigation. | Target scope audit | Central PMS current-session authority | `App.tsx` | Shell Vitest/Playwright proof | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-FND-002 | Authenticate staff through the shared human authentication/session boundary. | I-020, I-022 | Central PMS | `HumanAuthenticationShell.tsx` | H-006 proof and cross-application integration | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-FND-003 | Enforce a 30-minute sliding idle limit and eight-hour absolute limit. | I-020 correction | Central PMS | Session-expiry presentation | PR 634 and PR 7 validation | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-FND-004 | Require CSRF protection for unsafe browser requests. | I-020/I-021 | Central PMS antiforgery enforcement | Shared API client decorator | H-006/H-007 tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-FND-005 | Derive actor, permissions, and scope from the authoritative session, never browser headers. | Identity contracts | Central PMS | Browser sends no authority headers | Security scans and tests | `IMPLEMENTED\_AND\_ACCEPTED` | Reprove for every new module |

| MP-FND-006 | Keep credentials, tokens, MFA material, and authority data out of browser storage. | Identity contracts | Central PMS returns safe DTOs | Storage-free frontend | Unit and Playwright storage checks | `IMPLEMENTED\_AND\_ACCEPTED` | Reprove for every new module |

| MP-FND-007 | Distinguish 401 session loss, 403 denial, 404 anti-enumeration, 409 conflict, and safe availability failures. | I-020/I-021 | Central PMS | Shared safe-error handling | H-006/H-007 tests | `IMPLEMENTED\_AND\_ACCEPTED` | Reuse shared behavior |

| MP-FND-008 | Treat an uncertain mutation as stale and never replay it automatically. | H-007 contract | Central PMS idempotency and state | Stale-state lockout | H-007 unit and browser tests | `IMPLEMENTED\_AND\_ACCEPTED` | Reuse for every unsafe module |



\### 8.2 User lifecycle and access administration



| Trace ID | Requirement | Source | Backend | Frontend | Evidence | Status | Remaining action |

| --- | --- | --- | --- | --- | --- | --- | --- |

| MP-USR-001 | List and search scoped users using bounded pagination and masked contact data. | User/Role/Scope Contract section 5 | I-021 | H-007 User Administration | H-007 proof and manual test | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-002 | Read a scoped user and show roles and scope grants without enumeration leakage. | User/Role/Scope Contract sections 5 and 8 | I-021 | H-007 | Unit/integration/browser evidence | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-003 | Create an invited user with an atomic compatible initial role and Site or Site Group grant. | I-021 correction | I-021 | H-007 Add User | PR 634 and PR 7 direct headed test | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-004 | Update profile and access effectivity with optimistic concurrency and audit. | User/Role/Scope Contract | I-021 | H-007 | Focused tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-005 | Activate, suspend, inactivate, retire, lock, and unlock through governed lifecycle transitions. | User/Role/Scope Contract section 8.1 | I-021/I-020 | H-007 | Route and component tests | `IMPLEMENTED\_AND\_ACCEPTED` | Final integrated regression |

| MP-USR-006 | Revoke sessions when lifecycle policy requires it. | User/Role/Scope Contract sections 6 and 7 | I-020/I-021 | H-007 account status | Integration tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-007 | Initiate a credential-reset challenge without exposing credentials or delivery secrets. | User/Role/Scope Contract sections 5 and 8.1 | Implemented in I-021 | No frontend control found | Backend proof only | `PARTIAL` | Add permission-gated UI and headed validation |

| MP-USR-008 | Show privacy-safe MFA status. | User/Role/Scope Contract section 5 | I-021/I-020 | H-007 Security | H-007 tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-009 | Reset or remove MFA with elevated permission, reason, freshness, ceiling checks, session handling, and audit. | User/Role/Scope Contract section 5 | I-021/I-020 | H-007 Security | Backend and UI tests | `IMPLEMENTED\_AND\_ACCEPTED` | Final integrated regression |

| MP-USR-010 | Govern MFA requirement and user-completed enrollment without exposing provisioning material to administrators. | User/Role/Scope Contract section 5 | I-020 boundary | No complete administration flow found | Contract only | `PARTIAL` | Close requirement posture and implement required UI/API behavior |

| MP-USR-011 | List and revoke user sessions without exposing token material. | User/Role/Scope Contract section 8.3 | I-021/I-020 | H-007 Security | H-007 tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-012 | Record access reviews without silently extending access. | User/Role/Scope Contract sections 5 and 6.4 | I-021 | H-007 Access Review | H-007 tests | `IMPLEMENTED\_AND\_ACCEPTED` | Add review campaigns only if later approved |

| MP-USR-013 | Display privacy-safe identity audit history. | User/Role/Scope Contract section 8.3 | I-021 | H-007 Activity Log | H-007 tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-USR-014 | Prevent self-role, self-scope, self-lifecycle, self-unlock, self-MFA, and self-approval escalation. | User/Role/Scope Contract section 6.1 | I-021 | No bypass controls | Backend security tests | `IMPLEMENTED\_AND\_ACCEPTED` | Reprove in integrated UAT |

| MP-USR-015 | Prevent concurrent removal of the last eligible identity administrator. | User/Role/Scope Contract section 6.1 | I-021 | Safe conflict presentation | Backend concurrency proof | `IMPLEMENTED\_NOT\_ACCEPTED` | Include in final controlled security UAT |



\### 8.3 Role, permission, privilege, and scope



| Trace ID | Requirement | Source | Backend | Frontend | Evidence | Status | Remaining action |

| --- | --- | --- | --- | --- | --- | --- | --- |

| MP-RBAC-001 | Read the authoritative role catalog. | User/Role/Scope Contract section 8.2 | I-021 | H-007 and Access Control | Tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-RBAC-002 | Read the authoritative permission catalog and sensitivity/audit posture. | User/Role/Scope Contract section 8.2 | I-021 | H-007 and Access Control | Tests | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-RBAC-003 | Assign and revoke non-privileged roles within the actor delegation ceiling. | User/Role/Scope Contract sections 5 and 6.2 | I-021 | H-007 | Tests and manual walkthrough | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-RBAC-004 | Create, update, clone, inactivate, and retire manageable role definitions. | Target scope audit required modules | No conforming mutation contract found | No UI | None | `NOT\_IMPLEMENTED` | Define backend contract, persistence rules, UI, and tests |

| MP-RBAC-005 | Bind and revoke role permissions with separate authority and sensitive-permission approval. | User/Role/Scope Contract sections 5 and 6.3 | No conforming mutation contract found | Read-only only | None | `NOT\_IMPLEMENTED` | Implement governed binding lifecycle |

| MP-RBAC-006 | Preserve protected system roles and prevent unsafe retirement or mutation. | Security/RBAC input and privilege ceiling | No complete administration lifecycle found | No UI | None | `NOT\_IMPLEMENTED` | Define protected-role policy and tests |

| MP-RBAC-007 | Request and independently decide privileged access without self-approval. | User/Role/Scope Contract section 7.2 | I-021 | H-007 Elevated Access | Tests | `IMPLEMENTED\_AND\_ACCEPTED` | None for evidence decision |

| MP-RBAC-008 | Activate approved privileged authority through an explicit governed operation. | User/Role/Scope Contract decision gate | Approval evidence exists but does not activate | No activation UI | DR-10 unresolved | `PARTIAL` | Approve and implement activation policy |

| MP-RBAC-009 | Enforce incompatible-duty rules and privilege ceilings. | User/Role/Scope Contract sections 6.2 and 6.3 | Partial I-021 enforcement | Limited presentation | Backend proof for current slice | `PARTIAL` | Freeze incompatible-duty catalog and prove all admin mutations |

| MP-RBAC-010 | Grant and revoke Site scope on a user-role assignment. | User/Role/Scope Contract sections 3 and 8.2 | I-021 | H-007 | Tests and manual walkthrough | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-RBAC-011 | Grant and revoke Site Group scope without browser expansion of member Sites. | User/Role/Scope Contract sections 3 and 8.2 | I-021 | H-007 | Tests and manual walkthrough | `IMPLEMENTED\_AND\_ACCEPTED` | None |

| MP-RBAC-012 | Grant explicit organization-wide scope only to approved eligible roles and actors. | User/Role/Scope Contract sections 3, 4, and 9 | Fail-closed rejection | Read-only returned posture | `GLOBAL\_SCOPE\_POLICY\_NOT\_APPROVED` tests | `PARTIAL` | Approve allowlist or formally defer global grants |

| MP-RBAC-013 | Apply periodic review and stale-grant policy to privileged and ordinary grants. | User/Role/Scope Contract section 6.4 | Access-review recording only | Access Review form | Partial | `PARTIAL` | Approve expiry/suspension behavior and automate enforcement |



\### 8.4 Site, Site Group, device, and shift administration



| Trace ID | Requirement | Source | Backend | Frontend | Evidence | Status | Remaining action |

| --- | --- | --- | --- | --- | --- | --- | --- |

| MP-OPSADM-001 | List canonical Site Groups and Sites according to actor scope. | Target scope audit | Existing canonical data, no dedicated admin contract confirmed | Authorized site selector only | Partial shell tests | `PARTIAL` | Add bounded inventory API and page |

| MP-OPSADM-002 | Create and update Management Platform-owned Site/Site Group administrative metadata. | Target scope audit | No admin mutation contract found | No UI | None | `NOT\_IMPLEMENTED` | Define ownership, contract, audit, and UI |

| MP-OPSADM-003 | Manage canonical Site membership in a Site Group with impact validation. | Site model and target scope audit | No admin mutation contract found | No UI | None | `NOT\_IMPLEMENTED` | Implement governed membership lifecycle |

| MP-OPSADM-004 | Show Site relationships to VendorSystem, connector, POS Server, reporting, and operational boundaries. | Core BRD/Site model | Data exists across domains | No consolidated view | None | `NOT\_IMPLEMENTED` | Add Site detail/read model |

| MP-OPSADM-005 | List trusted devices and their Site, purpose, status, trust, and last-activity posture. | Security/RBAC input and target scope audit | No dedicated Management Platform contract found | No UI | None | `NOT\_IMPLEMENTED` | Add device inventory API and UI |

| MP-OPSADM-006 | Register, bind, disable, revoke, and retire trusted devices with audit. | Target scope audit | No admin mutation contract found | No UI | None | `NOT\_IMPLEMENTED` | Implement device lifecycle |

| MP-OPSADM-007 | List active and historical shifts with user, Site, device, and custody context. | Target scope audit | Operational shift data exists | No Management Platform page | None | `NOT\_IMPLEMENTED` | Add privacy-safe shift inventory |

| MP-OPSADM-008 | Govern forced shift end/revocation and abandoned-shift handling. | Target scope audit | No admin contract confirmed | No UI | None | `NOT\_IMPLEMENTED` | Define authority and implement workflow |

| MP-OPSADM-009 | Prove cross-Site and cross-Site-Group denial for every new administration route. | User/Role/Scope Contract section 4 | Implemented for I-021 scope | Current H-007 presentation | I-021 scope tests | `PARTIAL` | Extend proof matrix to all future modules |



\### 8.5 Sales Invoice, POS Server, and configuration administration



| Trace ID | Requirement | Source | Backend | Frontend | Evidence | Status | Remaining action |

| --- | --- | --- | --- | --- | --- | --- | --- |

| MP-FISC-001 | Read and manage Registered Business records. | POS/Invoicing and Management Platform contracts | Central PMS profile APIs | Sales Invoice Configuration | Unit/E2E/proof scripts | `IMPLEMENTED\_NOT\_ACCEPTED` | Include in integrated UAT |

| MP-FISC-002 | Read, create, edit, validate, activate, and retire Sales Invoice Setup versions. | Management Platform contracts | Central PMS profile APIs | Sales Invoice Configuration | Unit/E2E/proof scripts | `IMPLEMENTED\_NOT\_ACCEPTED` | Include in integrated UAT |

| MP-FISC-003 | Preserve history, readiness, issuance usage, concurrency, and uncertain-result safety. | Management Platform contracts | Central PMS | Sales Invoice Configuration | Focused tests | `IMPLEMENTED\_NOT\_ACCEPTED` | Integrated UAT |

| MP-FISC-004 | Map each Site to its assigned POS Server and show readiness. | Core BRD FR-030/031 and target scope audit | No complete Management Platform admin contract found | No UI | None | `NOT\_IMPLEMENTED` | Define read and mutation contract |

| MP-FISC-005 | Manage fiscal routing and Registered Business assignment with effectivity and approval. | POS/Invoicing boundary | Partial profile configuration | No consolidated routing UI | None | `PARTIAL` | Implement routing/configuration module |

| MP-FISC-006 | Show service trust references and expiry metadata without exposing private material. | Security and POS Server boundary | No Management Platform admin contract found | No UI | None | `NOT\_IMPLEMENTED` | Add safe trust-reference read model |

| MP-FISC-007 | Preview impact and preserve audit history before high-risk reassignment. | Target scope audit risk posture | No complete workflow found | No UI | None | `NOT\_IMPLEMENTED` | Implement approval and audit workflow |

| MP-FISC-008 | Never issue Sales Invoices or mutate fiscal documents from the Management Platform. | MDR-FR-041/042 | Authority remains POS Server | Browser has no issuance action | Boundary tests/current design | `IMPLEMENTED\_AND\_ACCEPTED` | Preserve boundary |



\### 8.6 Statutory policy and evidence governance



| Trace ID | Requirement | Source | Backend | Frontend | Evidence | Status | Remaining action |

| --- | --- | --- | --- | --- | --- | --- | --- |

| MP-POL-001 | Read canonical statutory policy coverage by authorized scope. | Statutory Management Platform handoff | Central PMS read API | Policy Coverage | Unit/E2E/proof | `IMPLEMENTED\_NOT\_ACCEPTED` | Integrated UAT |

| MP-POL-002 | Read evidence-governance readiness without exposing protected evidence. | Statutory evidence handoff | Central PMS read API | Evidence Governance | Unit/E2E/proof | `IMPLEMENTED\_NOT\_ACCEPTED` | Integrated UAT |

| MP-POL-003 | Create and edit Draft policy versions and evidence rules. | Target scope audit | No Management Platform mutation contract found | Read-only | None | `NOT\_IMPLEMENTED` | Define governed write contract and UI |

| MP-POL-004 | Validate, compare, submit, approve, reject, activate, and retire policy versions. | Policy administration target | No complete workflow found | No UI | None | `NOT\_IMPLEMENTED` | Implement lifecycle and independent approval |

| MP-POL-005 | Import and review LGU ordinance configuration with source and jurisdiction. | Target scope audit | Policy import exists in Operator Console area | No Management Platform mutation page | Partial backend evidence | `PARTIAL` | Move or cross-link governed workflow into Management Platform |

| MP-POL-006 | Audit policy and evidence-governance administrative changes. | Audit and statutory requirements | Domain audit exists | No consolidated admin history | Partial | `PARTIAL` | Add module audit history and export controls |

| MP-POL-007 | Keep statutory decision and payable-basis authority outside Management Platform reporting/admin views. | Core BRD and statutory authority | Central PMS/Discount workflow | No decision action in current pages | Boundary posture | `IMPLEMENTED\_AND\_ACCEPTED` | Preserve boundary |



\### 8.7 Management Dashboard and Reporting



| Trace ID | Source requirement | Required capability | Current implementation | Status | Required delivery |

| --- | --- | --- | --- | --- | --- |

| MP-MDR-001 | MDR-FR-001 to 004 | Role-based Site, Site Group, cross-site, and portfolio dashboard scope | Runtime-accepted `/management-platform/overview`, `GET /v1/management-platform/dashboard/catalog`, and `GET /v1/management-platform/dashboard/operational-overview`; `reports.view` and `dashboard.view` remain separate; explicit authorized `SITE`/`SITE_GROUP` scope and concealed cross-scope denial passed in acceptance `MDR-DASHBOARD-ACCEPT-20260823T090629Z-1AC8AB9B` at backend `6168b8a58437419ce54a3b6e4076b33328b2e683` and frontend `70c59d3ed375670afea9c0e4a7b866f43ccdb110`; evidence manifest `35ef916fed739244ced0a0f0f0a8981679167ed15d7a8437269912f1622dd6b7` | `IMPLEMENTED\_AND\_ACCEPTED` | Preserve server-owned permission and explicit scope enforcement in later dashboard slices |

| MP-MDR-002 | MDR-FR-005 to 008 | Active sessions, active vehicles, occupancy approximation, session age, and long-stay visibility | The scoped active vendor-projection aggregate and its SITE/SITE_GROUP UI presentation are runtime accepted; vehicle identifiers, occupancy approximation, session age, and long-stay views remain absent | `PARTIAL` | Approve and implement the remaining read models without exposing unnecessary vehicle or session data |

| MP-MDR-003 | MDR-FR-009 to 012 | Connector health, projection freshness, Vendor PMS availability, last poll, and latency | Scoped connector-target health, vendor projection freshness, source authority, data-as-of semantics, and responsive UI presentation are runtime accepted; raw diagnostics and latency remain deferred | `PARTIAL` | Add only approved latency and broader availability read models while preserving partial-source disclosure |

| MP-MDR-004 | MDR-FR-013 to 016 | Degraded-watch, degraded-active, Continuity Terminal, manual release, and fiscal exception visibility | Fiscal exception backend/UI foundation is runtime accepted for aggregate failed, conflict, outcome-unavailable, and lifecycle states from Central PMS coordination references; continuity and deadline-based overdue detection remain absent | `PARTIAL` | Separately approve authoritative continuity and overdue exception sources |

| MP-MDR-005 | MDR-FR-017 to 021 | Payment attempts, confirmations, provider outcomes, payment-rail performance, and uncertainty | Runtime-accepted `GET /v1/management-platform/dashboard/payment-reconciliation-summary` and Management Platform UI; contract `management-platform-payment-reconciliation-reporting:v1`; `reconciliation.view` / `ManagementPlatformPaymentReconciliationSummaryRead`; deterministic SITE/SITE_GROUP, period-boundary, multiple-currency, no-activity, disabled-feature, responsive, and security evidence in acceptance `MDR-PAY-ACCEPT-20260822T061921Z-8BA8B13B` | `PARTIAL` | Provider-live performance, settlement, payout, bank, custody, fees, refunds, disputes, chargebacks, and fiscal facts remain unavailable |

| MP-MDR-006 | MDR-FR-022 to 024 | Sales Invoice issuance, fiscal status, and authorized report-reference visibility | `GET /v1/management-platform/dashboard/fiscal-exception-summary`; contract `management-platform-fiscal-exception-reporting:v1`; `sales-invoice-report.view` / `ManagementPlatformFiscalExceptionSummaryRead`; typed feature `ManagementPlatform:DashboardReporting:FiscalExceptions:Enabled`; Central PMS `core.fiscal_issuance_references` and linked confirmations; merged Management Platform route `/management-platform/reports/fiscal-exceptions`; acceptance `MDR-FISCAL-ACCEPT-20260823T073441Z-8299A68F` at backend `771eeac3aaab0e4a760a7b80ee2f1a8f108291b4` and frontend `70c59d3ed375670afea9c0e4a7b866f43ccdb110`; evidence manifest `527733eae8d399de6619a4347e85e89ed50d0190b6ebc95084f79485418d4f63`; authenticated SITE/SITE_GROUP, period, lifecycle, exception, currency, availability, responsive, security, and cleanup proofs | `IMPLEMENTED\_AND\_ACCEPTED` | Aggregate foundation accepted with `PARTIAL` source coverage; printing, delivery, overdue, retry-exhaustion, adjustment, void, reprint, BIR report facts, exports, schedules, drill-down, and exception-resolution workflows remain unavailable or deferred |

| MP-MDR-007 | MDR-FR-025 to 027 | Reconciliation run/item and settlement comparison status | The runtime-accepted UI reports five internally provable consistency categories across canonical attempts, confirmations, and verified outcomes; no settlement comparison or reconciliation mutation is claimed | `PARTIAL` | Separately approve any reconciliation-run, settlement, payout, or bank comparison model |

| MP-MDR-008 | MDR-FR-028 to 031 | Statutory discount, coupon, evidence access, operator, and supervisor reporting | No dashboard | `NOT\_IMPLEMENTED` | Compliance reporting API and UI |

| MP-MDR-009 | MDR-FR-032 to 037 | Export control, filters, freshness, source labels, access/export audit, privacy | Core dashboard, Payment and Reconciliation, and Fiscal Exception backend/UI foundations are runtime accepted with explicit scope, source/freshness labels, warnings, privacy boundaries, and controlled failure behavior; no export capability is implemented | `PARTIAL` | Separately approve and implement controlled export and its audit/privacy acceptance |

| MP-MDR-010 | MDR-FR-038 to 042 | Preserve non-authority for payment finality, ExitAuthorization, gates, Sales Invoice issuance, and mutations | Runtime acceptance proved GET-only reporting, separate attempts and confirmations, no provider/POS Server call, no browser-owned authority, and explicit settlement, payout, bank, custody, fiscal, gate, and mutation disclaimers | `IMPLEMENTED\_AND\_ACCEPTED` | Preserve boundary in later reporting slices |

| MP-MDR-011 | MDR-AC-001 to 020 | Satisfy the approved dashboard acceptance criteria | Core operational dashboard acceptance `MDR-DASHBOARD-ACCEPT-20260823T090629Z-1AC8AB9B`, Payment and Reconciliation acceptance `MDR-PAY-ACCEPT-20260822T061921Z-8BA8B13B`, and Fiscal Exception acceptance `MDR-FISCAL-ACCEPT-20260823T073441Z-8299A68F` prove their bounded backend/UI foundations; broader operational metrics, management activity, export, and schedule acceptance remain incomplete | `PARTIAL` | Complete and accept only the remaining approved dashboard/reporting slices |



\### 8.8 Audit, reconciliation, export, and monitoring



| Trace ID | Requirement | Source | Current implementation | Status | Remaining action |

| --- | --- | --- | --- | --- | --- |

| MP-AREC-001 | Provide consolidated cross-domain audit search and correlation drill-down. | Target scope audit, MDR-FR-030/031/036 | User activity and isolated governance views only | `PARTIAL` | Cross-domain audit API and module |

| MP-AREC-002 | Show reconciliation runs, items, lifecycle, aging, and ownership. | MDR-FR-025/026 | Backend reconciliation services exist, no Management Platform UI | `PARTIAL` | Read API/BFF and UI |

| MP-AREC-003 | Show payment, provider, Sales Invoice, and settlement comparison with zero authority leakage. | MDR-FR-017 to 027 | No Management Platform module | `NOT\_IMPLEMENTED` | Canonical comparison read model |

| MP-AREC-004 | Support controlled review notes and workflow handoff without silently closing reconciliation. | MDR boundary | No module | `NOT\_IMPLEMENTED` | Define action boundary and UI |

| MP-AREC-005 | Export authorized reports with source, generation time, filters, freshness, and audit. | MDR-FR-032 to 036 | No export service/module | `NOT\_IMPLEMENTED` | Export contract, storage, audit, and UI |

| MP-MON-001 | Monitor connector and Vendor PMS availability. | MDR-FR-009/011 | Operator Console has limited operational views | `NOT\_IMPLEMENTED` in Management Platform | Consolidated monitoring module |

| MP-MON-002 | Monitor polling time, latency, failure count, and projection freshness. | MDR-FR-010/012 | No Management Platform module | `NOT\_IMPLEMENTED` | Monitoring read model and UI |

| MP-MON-003 | Monitor vendor acknowledgment backlog and incident context. | Target scope audit | Operator Console/backend partial | `PARTIAL` | Management Platform consolidation and handoff |

| MP-MON-004 | Monitor Central PMS, Payment Orchestrator, POS Server, and connector service readiness. | Operational monitoring target | No consolidated module | `NOT\_IMPLEMENTED` | Define health aggregation without creating business authority |



\### 8.9 Service identity administration



| Trace ID | Requirement | Source | Current implementation | Status | Remaining action |

| --- | --- | --- | --- | --- | --- |

| MP-SVC-001 | List service identities with purpose, audience, environment, status, and scope metadata. | Target scope audit and identity boundary | No Management Platform module | `NOT\_IMPLEMENTED` | Add safe inventory contract and UI |

| MP-SVC-002 | Enable, disable, and retire service identities with governed approval and audit. | Identity/security target | No module | `NOT\_IMPLEMENTED` | Implement lifecycle |

| MP-SVC-003 | Show trust-reference and expiry metadata without secret or private-key material. | Security boundary | No module | `NOT\_IMPLEMENTED` | Add privacy-safe DTOs and UI |

| MP-SVC-004 | Record rotation metadata and events without rotating secrets in the browser. | Security boundary | No module | `NOT\_IMPLEMENTED` | Backend-owned rotation workflow and read-only browser status |

| MP-SVC-005 | Prevent human roles from being assigned to service identities and vice versa. | Identity authority separation | Separate persistence exists, but no administration proof was found | `IMPLEMENTED\_NOT\_ACCEPTED` | Add negative integration and UAT coverage |



\## 9. Decision Register



Unresolved decisions must not be labeled `FORMALLY\_DEFERRED`. Until approved, affected mutations remain fail-closed.



| Decision ID | Decision required | Current posture | Recommended v1.3 decision | Affected rows |

| --- | --- | --- | --- | --- |

| MP-DR-001 | Organization-wide scope eligibility | Direct GLOBAL grants rejected | Allow only approved central security and operations role allowlist | MP-RBAC-012 |

| MP-DR-002 | Privileged approval activation | Approval records evidence but does not activate authority | Require a separate atomic activation after independent approval | MP-RBAC-008 |

| MP-DR-003 | Credential challenge delivery | Backend delivery policy unresolved/disabled | Use approved corporate delivery channel; never return secret through Management Platform | MP-USR-007 |

| MP-DR-004 | MFA requirement administration | Privileged login TOTP is enforced; admin requirement flow incomplete | Keep enrollment user-owned and expose only requirement status/admin control | MP-USR-010 |

| MP-DR-005 | Overdue access review behavior | Review can be recorded, enforcement not final | Privileged access expires at 90 days; ordinary access reviewed at 180 days | MP-RBAC-013 |

| MP-DR-006 | Role definition and sensitive binding approval | No complete mutation lifecycle | Protect system roles and require independent approval for sensitive bindings | MP-RBAC-004 to 006 |

| MP-DR-007 | Dashboard v1.3 delivery slice | Approved BRD has 42 FRs, exact first delivery remains open | Deliver all MDR-FR rows through bounded read-only modules before calling v1.3 complete | MP-MDR-001 to 011 |



\## 10. Ordered Completion Backlog



| Order | Slice | Primary owner | Required outcome | Exit condition |

| --- | --- | --- | --- | --- |

| 1 | Completion baseline adoption | Product/architecture | Approve this matrix and update stale current-state statements | Every requirement has one trace row and owner |

| 2 | User Administration closure | Central PMS + Management Platform | Credential-reset UI and final MFA/activation posture | MP-USR rows accepted or formally deferred |

| 3 | Role and Permission Administration | Central PMS + Management Platform | Role lifecycle, permission binding, protected-role rules, SOD | MP-RBAC rows accepted or formally deferred |

| 4 | Site, Site Group, Device, and Shift Administration | Central PMS + Management Platform | Inventory and governed lifecycle workflows | MP-OPSADM rows accepted |

| 5 | Dashboard and Reporting foundation | Central PMS/reporting + Management Platform | Scope 

