# Security, Trust, and RBAC Input Pack

## 1. Purpose

This input pack summarizes security, trust boundary, identity, device trust, RBAC, privacy, export, credential, audit, and non-repudiation inputs for ExitPass System Design v1.3.

It is intended to help the System Design Lead draft architecture-level security sections while preserving the approved v1.3 BRD authority model and the v1.2 System Design trust-boundary style. It does not define final endpoint authentication schemes, OAuth scopes, certificate implementation, mTLS topology, secrets storage internals, database design, QR token implementation, device enrollment mechanics, API DTOs, or implementation classes.

## 2. Source Documents Reviewed

Approved v1.3 baseline sources:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
  - Sections 3.4, 3.7, 7.1-7.9, 8.5-8.9, 9.1-9.15, 10.4-10.12, 11.4-11.5, 12.1-12.5, 13.9-13.12, 14.1-14.4, 15.1-15.5, 16.1-16.3, 17.1-17.6, 19.1-19.5.
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
  - Sections 6, 9, 10, 12-13, 16-24, 26-29.
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
  - Sections 6-12, 14-16, 19-28, 30-33.
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
  - Sections 4-7, 9-14, 19-28, 31-34.
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
  - Sections 6-17, 20-30, 33-36.
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
  - Sections 6-12, 19-34, 36-39.
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
  - Sections 1-8.
- `docs/v1.3/system-design/ExitPass_System_Design_v1.3_Orchestration_Plan.md`
  - Sections 2-9.

Style and posture baseline:

- `D:\Docs\ExitPass\v1.2\ExitPass System Design v1.2.docx`
  - Trust Boundaries and Security Architecture posture: explicit trust zones, controlled boundary crossings, no direct public access to trusted core services, external provider verification, vendor adapter isolation, service identity, site/device identity, least privilege, audit logging, non-repudiation, and fail-closed authority separation.

## 3. Trust Boundary Overview

The v1.3 security architecture should preserve the v1.2 trust-zone framing:

- Public customer access remains untrusted until it crosses approved platform ingress.
- Platform edge protects ingress but does not own tariff, payment finality, fiscal issuance, discount policy, or exit authorization.
- Trusted core platform services own canonical platform control decisions only within their assigned authority.
- External payment providers, Vendor PMS/HCP systems, POS Server, gate devices, terminals, and reporting consumers are bounded actors, not implicit trusted-core peers.
- Service-to-service and device-to-platform traffic should be authenticated and authorized by actor, service, device, Site/Site Group scope, and operation.
- Every security-significant, financially meaningful, or operationally meaningful boundary crossing should be attributable and auditable.

Architecture-level trust boundaries to carry into System Design v1.3:

| Boundary | Confirmed posture |
| --- | --- |
| Public WebPay / customer browser to platform edge | Public URL entry is allowed, but lookup must be scoped to configured Site Group, Site, or payment-scope context. WebPay does not declare payment finality or issue ExitAuthorization. |
| WebPay payment-scope URL to Site Group / Site resolution | URL resolution must bind the customer to the configured lookup/payment scope and prevent lookup escape. Exact slug registry and whether slugs resolve to Site Group, Site, or both remain open. |
| Central PMS trusted core | Central PMS remains payment-linked platform control authority, including payment finality, fiscal issuance reference recording, and ExitAuthorization. |
| Payment Orchestrator to payment providers | Payment Orchestrator interacts with providers and reports verified outcomes. Providers and Payment Orchestrator do not declare platform payment finality. |
| Vendor PMS/HCP connector boundary | Vendor PMS/HCP remains normal session lifecycle and tariff authority. Vendor-specific auth, payloads, identifiers, retries, and semantics should stay isolated in the connector/adapter boundary. |
| HikCentral mapping boundary | HCP ParkingLotIndexCode must not be treated as ExitPass `site_id`; mapping must use AdapterMapping and runtime vendor object identity. |
| Site POS Server boundary | POS Server is fiscal issuance authority for the resolved Site and does not own payment finality or ExitAuthorization. |
| Assisted Payment Terminal boundary | Terminal is a cashier/continuity workflow and evidence capture surface. It does not approve statutory entitlement, mutate payable basis directly, declare payment finality, become fiscal authority, or issue ExitAuthorization. |
| Operator Console boundary | Console is an internal governance web/PWA surface. It is non-payment, non-fiscal, non-gate-execution, and non-ExitAuthorization authority. |
| Continuity boundary | Continuity must fail closed by default, activate explicitly, remain scoped/time-bound, and preserve audit/reconciliation. It does not replace Vendor PMS/Central PMS/POS authority. |
| Gate/device boundary | Gate/exit systems consume Central PMS-issued authorization and must not bypass Central PMS except under a formally approved manual emergency process. |
| Management Dashboard boundary | Dashboard/reporting is visibility only. It must not perform payment, fiscal, exit, discount, coupon, or gate-control actions. |

## 4. Human Roles

Confirmed human roles and security posture:

- Parker / public customer: may use WebPay or another public/customer payment channel within the configured payment scope. May resolve eligible session context and initiate payment flow, but may not access internal services, consume ExitAuthorization directly, or alter payment/fiscal/discount records.
- Cashier / assisted terminal user: may operate Cashier-Assisted Terminal only after authentication, assigned Site/Site Group context, trusted terminal context, and shift/session accountability. May capture statutory discount input/evidence and process assisted payment workflow through approved backend flows.
- Continuity operator: may use Continuity Terminal only during approved degraded/BCP operation, with incident/audit/reconciliation tags and restricted workflows.
- Site Operator: may perform allowed session lookup, evidence capture, statutory discount workflow actions, and exception routing within assigned Site/Site Group and active shift context.
- Site Supervisor: may review operator actions, approve or reject overrides where policy allows, review continuity activation/manual release requests, and support post-incident review.
- Operations Manager / Site Manager: may view operational dashboards, exception queues, continuity state, connector health, projection freshness, and Site/Site Group operating status within assigned scope.
- Finance / Revenue Assurance User: may access payment, fiscal, settlement, reconciliation, statutory discount, coupon, exception, and export reports according to authorization.
- Compliance Auditor: may review audit trails, evidence access, statutory discount decisions, manual release records, continuity-origin activity, and exports subject to privacy controls.
- Technical Operations / Support: may review device trust, connector health, projection freshness, service health, incident context, and vendor acknowledgment backlog without payment, fiscal, discount, or exit authority.
- Administrator: may manage approved user, role, device, Site/Site Group, policy, reporting access, export permission, and dashboard configuration functions where in scope.
- Executive / Management Viewer and read-only client / lessor viewer: may access aggregated or contract-scoped management views only within authorized scope; sensitive evidence, audit, and financial details require explicit permission.

## 5. Service Identities / Non-Human Actors

System Design v1.3 should identify non-human actors and bind each to least-authority operations:

- Central PMS: canonical authority for payment-linked platform control state, payment finality, fiscal issuance reference recording, and ExitAuthorization.
- Payment Orchestrator: payment provider interaction, provider abstraction, callback handling, and verified provider outcome reporting only. It must not create platform payment finality or ExitAuthorization.
- Vendor PMS/HCP connector instance: vendor session lookup, live tariff retrieval, projection polling, and vendor paid-state acknowledgment where required. It must not mutate canonical payment finality or bypass adapter mapping.
- HikCentral/HCP adapter codebase: reusable adapter implementation behind the connector boundary. HikCentral credentials and vendor authentication behavior should remain isolated from public, WebPay, Operator Console, terminal, and reporting surfaces.
- Site POS Server: resolved-Site fiscal issuance, fiscal numbering/counters, Sales Invoice, Electronic Journal, POSLog, fiscal reports, reprint controls, adjustment controls, retention, exports, and fiscal audit trail. It must not issue ExitAuthorization.
- Gate / barrier / exit device: physical execution consumer of Central PMS authorization. It must not infer payment success, fiscal success, or exit eligibility locally.
- Assisted Payment Terminal device: bound terminal/channel actor with cashier or continuity mode. It requires device identity, Site/Site Group context, shift/user accountability, and device trust checks appropriate to hardened terminal deployment.
- Operator Console client device / browser/PWA context: internal governance client that may require registered device access, browser key binding, mTLS, or approved device trust controls where required. Exact mechanism remains open.
- Management Dashboard / reporting consumer: read/report/export actor only, with scope-aware and role-aware data access.
- Audit/Event or approved audit workflow: evidence, event, traceability, and audit query surface. It should preserve append-only/non-repudiation posture at architecture level without defining storage internals here.

## 6. Device and Terminal Trust

Confirmed device posture:

- Assisted Payment Terminal is a separate terminal app family with Cashier-Assisted Terminal and Continuity Terminal modes.
- Cashier-Assisted Terminal is enabled only for authorized terminals, cashiers, Sites, and shifts.
- Continuity Terminal is disabled by default and may activate only under approved BCP/degraded controls.
- Field terminal deployments require hardened device posture.
- Android-first is the preferred field-terminal posture, with a web-based workflow core and native integration where required, but it is not an exclusive business requirement.
- Exact Android shell, WebView/PWA core, hybrid model, kiosk mode, scanner/camera/printer/cash drawer integration, local storage, certificate/key storage, offline evidence behavior, endpoint paths, DTOs, and device SDKs are deferred.
- Operator Console is an internal web/PWA-oriented operations console and may use browser key binding, mTLS, or approved device trust controls where required; exact mechanism remains open.

Architecture guidance:

- Device trust should be expressed at system-design level as a boundary requirement: device identity, assignment to Site/Site Group where applicable, health/trust posture, user authentication, active shift/session context, and auditability.
- Do not lock the final enrollment, certificate, local key storage, or browser binding implementation in the v1.3 System Design unless another approved downstream design closes it.
- Device or terminal possession alone must not grant payment finality, fiscal authority, discount approval, or exit authority.
- Wrong Site/Site Group context should block processing or require authorized correction.
- Untrusted terminal/device attempts should deny workflow or route to support according to policy and audit requirements.

## 7. RBAC Domains

RBAC should be modeled as multiple domains rather than one flat permission list:

| RBAC domain | Architecture-level controls |
| --- | --- |
| Public customer / WebPay | Payment-scope URL, Site Group/Site binding, abuse controls, customer-safe session/payment flow, no internal authority. |
| Assisted Payment Terminal | Cashier authentication, terminal identity, assigned Site/Site Group, active shift/session, payment workflow permission, statutory discount capture permission, fiscal display/reprint-display permissions where allowed, supervisor escalation, continuity-mode restrictions. |
| Operator Console | User role, Site/Site Group, shift, device trust, action type, evidence access, supervisor override, continuity activation review, fiscal exception escalation, manual release governance, reporting/export actions. |
| Continuity | Activation/deactivation, affected scope, allowed workflows, restricted workflows, supervisor approval where required, incident/BCP reference, manual release, fiscal exception handling, post-restoration reconciliation closure. |
| POS/Invoicing | Fiscal issuance, fiscal adjustment, reprint, reset, recovery, export, taxpayer/fiscal configuration, and fiscal evidence access must be role-restricted and audited. |
| Management Dashboard / Reporting | Site Group, Site, portfolio, role, report domain, sensitive evidence/audit data, export permission, cross-site access, client/lessor scope. |
| Service-to-service | Service identity, operation authority, Site/Site Group/Site scope where applicable, route policy, and auditable state changes. |
| Device / gate | Device identity, Site/lane/context binding, validate/consume-only authority, manual emergency exception tagging where approved. |

High-risk permissions requiring elevated authorization and audit:

- Evidence access and evidence export.
- Supervisor override.
- Continuity activation/deactivation review.
- Continuity Terminal activation.
- Manual release approval/governance.
- Fiscal issuance exception escalation.
- Fiscal reprint, void, refund, cancel, return, reset, recovery, and export.
- Reporting exports and cross-site/portfolio reporting.
- Tax/fiscal configuration changes.
- Device registration, trust changes, and Site/Site Group assignment.

## 8. Evidence and Privacy Controls

Confirmed evidence and privacy posture:

- Statutory discount capture must preserve structured entitlement details, supporting evidence where required, cashier attestation, device identity, shift/session identity, supervisor action where applicable, and audit metadata.
- Assisted Payment Terminal may capture required details/evidence but must submit to Central PMS / Discount workflow and avoid terminal-local policy approval or unmanaged evidence retention.
- Operator Console preserves evidence controls: structured ID details by default, cropped ID image only where required, privacy notice, no unmanaged local device storage, evidence hash/reference, retention policy, access restrictions, and audit of evidence access.
- Continuity-mode evidence access and discount activity must be incident-tagged, audit-tagged, reconciliation-tagged, and included in post-restoration review.
- Management Dashboard evidence reports must protect sensitive evidence details. Sensitive evidence and personally identifiable information require elevated permissions and privacy controls.
- POS/Invoicing evidence and personal data for Senior Citizen, PWD, NAAC, Solo Parent, and Diplomat VAT Privilege / VAT Exemption must follow approved privacy, retention, access, and audit policy.

Architecture guidance:

- Treat evidence as controlled data, not ordinary operational text.
- Prefer evidence references, hashes, redacted views, and role-gated access over broad replication of sensitive data into dashboards, terminals, logs, or exports.
- Logs and reports must avoid unnecessary sensitive personal data.
- Evidence capture requirements should be policy-driven by entitlement type, Site jurisdiction, and approved compliance posture.
- Diplomat VAT Privilege / VAT Exemption evidence requirements remain open for compliance/accounting confirmation.

## 9. Export and Reporting Controls

Confirmed reporting/export posture:

- Management Dashboard and Reporting is role-based and scope-aware by Site Group, Site, and portfolio where authorized.
- It is a visibility/reporting domain, not a payment, fiscal, tariff, discount, coupon, exit authorization, or gate-control authority.
- Operational dashboards may show projection data, connector health, stale alerts, active sessions, occupancy approximation, continuity state, manual release counts, and exception backlogs, but projection must be labeled as operational visibility and not financial truth.
- Financial/revenue dashboards must use canonical payment, fiscal, and reconciliation records.
- Fiscal dashboards must reconcile to POS Server-issued fiscal documents and Central PMS fiscal issuance references.
- Report export must be controlled by role and scope. Export activity must be audited.
- Exported reports should include source, generation time, filter criteria, and data freshness labels where applicable.
- Sensitive evidence, audit data, and personally identifiable or evidence-related reporting require elevated permission, redaction/privacy controls, and audit.
- POS Server owns fiscal exports and fiscal audit trail for the resolved Site. Dashboard visibility must not replace POS Server fiscal records or BIR-authoritative fiscal reports unless later approved in POS Server design.

Open export details include exact export formats, approval controls, evidence redaction rules, exported report retention, privacy controls for sensitive reporting, BI/reporting technology, source tables/aggregation rules, and fiscal report visibility.

## 10. Secrets and Credential Posture

Architecture-level posture:

- Secrets, credentials, certificates, and keys should be treated as platform-substrate controls with least-privilege, service-specific access.
- Vendor PMS/HCP credentials, including HikCentral credential handling, should remain isolated within the vendor connector/adapter boundary and must not leak into WebPay, Operator Console, Assisted Payment Terminal, Management Dashboard, or public client contexts.
- Payment provider credentials and webhook verification material should remain within Payment Orchestrator/payment integration boundaries.
- POS Server credentials and fiscal signing/identity material, if applicable, should remain within POS Server/fiscal boundaries and not be available to payment channels as independent fiscal authority.
- Terminal/device credentials or keys may be required for hardened deployments, but exact terminal certificate/key storage and enrollment implementation are explicitly deferred.
- Operator Console device trust may use browser key binding, mTLS, or other approved controls, but exact mechanism remains open.
- Service-to-service credentials should reinforce service role authority; network location alone should not imply authorization.

Do not define in System Design v1.3 unless separately approved:

- Final certificate implementation.
- Exact mTLS topology.
- OAuth scopes or endpoint-specific auth schemes.
- Secrets storage implementation.
- Device enrollment implementation.
- Terminal certificate/key storage internals.
- QR token implementation or digital Sales Invoice URL token model.

## 11. Audit and Non-Repudiation Requirements

Confirmed audit scope:

- URL slug/site resolution.
- Site Group to Site resolution.
- Connector health and polling state.
- Projection freshness.
- Degraded resolve activation and use.
- Fiscal issuance request/result/failure/retry.
- Manual release under fiscal or continuity exception.
- Continuity Terminal activation and use.
- Operator Console actions.
- Assisted Payment Terminal actions.
- Cashier-assisted statutory discount capture, cashier attestation, validation request/result, payable-basis refresh, supervisor escalation, and evidence references.
- Continuity-mode statutory discount activity, including incident, audit, reconciliation, and post-restoration review tags.
- Dashboard/report access and export actions where relevant.
- POS fiscal issuance, failed/timeout issuance, reprints, void/refund/cancel/return, X-read, Z-read, BIR Sales Summary, fiscal exports, reset/recovery, taxpayer/fiscal identity changes, terminal/channel changes, privileged actions, statutory entitlement/VAT privilege evidence, continuity fiscal exceptions, and manual release under fiscal exception.

Architecture-level audit attributes should include:

- Correlation ID.
- Actor identity, service identity, or device identity.
- Site Group, Site, channel, terminal, and shift/session context where applicable.
- Route or operation name at architecture level.
- Timestamp.
- Source context or authenticated source identity.
- Subject identifier where applicable.
- Decision result, denial, exception, or outcome.
- Reason code for override, manual release, fiscal exception, export, or privileged action where applicable.
- Incident/BCP, audit, and reconciliation tags for continuity-origin activity.

Non-repudiation posture:

- Manual release must be attributable to a human supervisor/operator and device/context where applicable.
- Supervisor override must be reason-coded, attributable, audit-tagged, and limited by role, Site/Site Group, shift, and policy scope.
- Continuity activation/deactivation must record affected scope, dependency, reason, approver where required, allowed/restricted workflows, expected duration/review interval, and restoration criteria.
- Fiscal state must be tamper-evident, append-only, and recoverable without silent rollback.
- Payment, fiscal issuance, degraded operation, operator actions, evidence access, exports, and exit authorization must be reconstructible end to end.

## 12. Security Risks and Mitigations

| Risk | Security / trust impact | Recommended mitigation for System Design v1.3 |
| --- | --- | --- |
| Site Group and Site confusion | Customer lookup escape, wrong vendor mapping, wrong POS routing, reporting leakage. | Make Site Group the lookup/payment scope and Site the reporting/vendor/POS/operational boundary; require traceable resolution and scoped authorization. |
| Public URL treated as authority | URL possession could be confused with permission to access unrelated sessions or internal functions. | Treat WebPay URLs as scoped entry points only; enforce payment-scope binding and edge policy. Exact slug/token details remain open. |
| Vendor identifier misuse | HCP ParkingLotIndexCode could be treated as ExitPass Site identity. | Require AdapterMapping and runtime vendor object key; isolate vendor identifiers inside connector boundary. |
| HikCentral/vendor credential leakage | Vendor credentials could leak to public, operator, terminal, or reporting surfaces. | Keep vendor credentials inside connector/adapter boundary; expose only normalized, authorized platform results. |
| Projection misuse | Projection could become fee truth, payment finality, fiscal truth, or exit basis. | Label projection as operational visibility; enforce freshness controls and fail-closed behavior for stale/ambiguous data. |
| Payment provider callback spoofing or replay | False provider outcome could trigger incorrect payment workflow. | Payment Orchestrator verifies provider outcome before reporting; provider does not directly mutate canonical state. Implementation details deferred. |
| Payment Orchestrator authority creep | Provider integration layer could declare platform finality or issue exit authorization. | Explicitly bound Payment Orchestrator to provider interaction and verified outcome reporting only. |
| POS Server authority creep | Fiscal issuer could be treated as payment or exit authority. | Preserve Central PMS payment finality and ExitAuthorization authority; POS Server owns fiscal issuance only. |
| Terminal treated as policy authority | Cashier terminal could approve statutory discount or mutate payable basis directly. | Terminal captures and submits; Central PMS / Discount workflow owns policy resolution and validation persistence. |
| Weak terminal device trust | Field terminal tampering or wrong-Site processing could affect payments/evidence. | Require hardened posture, terminal identity, Site/Site Group assignment, shift accountability, and device trust checks; keep implementation details deferred. |
| Operator Console authority leakage | Governance console could be used for payment, fiscal mutation, gate control, or ExitAuthorization. | Preserve non-payment/non-fiscal/non-gate boundary and enforce RBAC by role, scope, shift, device trust, and action type. |
| Evidence overexposure | Sensitive entitlement or identity evidence could be leaked through console, terminal, dashboard, logs, or exports. | Use privacy notices, evidence references/hashes, redaction, elevated permissions, retention controls, and access audit. |
| Continuity overuse or silent fallback | Degraded operation could bypass normal authority and reconciliation. | Fail closed by default; require explicit activation, scope, incident/audit/reconciliation tags, supervisor approval where required, and post-restoration review. |
| Manual release overuse | Revenue leakage, fraud, and weak non-repudiation. | Treat as last resort; require supervisor approval where policy requires, reason code, incident/audit/reconciliation tags, attribution, and review. |
| Fiscal exception bypass | Paid customer could exit before fiscal issuance control is satisfied. | Normal ExitAuthorization blocked until fiscal issuance succeeds; exceptions require approved retry/escalation/manual policy. |
| Reporting/export data leakage | Cross-site, sensitive evidence, or financial data could be overexposed. | Enforce Site/Site Group/portfolio scope, export permission, redaction, audit, data freshness labels, and retention controls. |
| Gate/device bypass | Gate may open based on local assertion, provider signal, or stale token. | Gate consumes Central PMS authorization only; formal manual emergency process must remain separately approved and audited. |

## 13. Open Security Questions

Carry these as open security/design questions; do not silently resolve them in System Design v1.3:

- What is the exact WebPay public URL slug registry structure?
- Do WebPay URL slugs resolve to Site Group, Site, or both?
- Should Site Group be user-facing as Payment Scope or Lookup Scope while retaining the Site Group concept?
- What is the final public URL/payment-scope anti-enumeration, abuse, expiry, and customer session policy?
- What is the exact degraded tariff freshness threshold and stale warning rule set?
- What is the exact BCP / Continuity Terminal activation authority and approval workflow?
- What is the exact manual release approval policy?
- What is the exact fiscal exception review workflow?
- What is the exact permission matrix across cashier, operator, supervisor, auditor, administrator, support, finance, reporting, and read-only client/lessor roles?
- What is the exact device trust mechanism for Operator Console: mTLS, browser key binding, both, or another approved control?
- What is the terminal certificate/key storage model?
- What offline evidence capture behavior, if any, is allowed?
- What kiosk lockdown requirements apply to field-deployed terminals?
- Is a fixed cashier station browser/PWA or desktop-compatible Assisted Payment Terminal variant allowed in v1.0?
- What is the exact POS Server deployment and registration model?
- Is POS Server a module under Central PMS or a separate service?
- What exact MIN/PTU/serial/software/supplier/taxpayer/Site/branch/channel/terminal fiscal identity assignment applies?
- What exact VAT/tax treatment applies by Site, taxpayer, transaction type, entitlement type, and line item?
- What exact Diplomat VAT treatment, evidence, wording, reporting, and retention rules apply?
- What is the digital Sales Invoice URL token/access/expiry/authentication/privacy/anti-tampering model?
- What are exact evidence retention periods by jurisdiction/policy?
- What third-party government or cooperative database integration, if any, is needed for automated ID verification?
- What exact evidence access report redaction rules and sensitive reporting privacy controls apply?
- What are exact export formats, approval controls, and exported report retention periods?
- Does the HCP connector push to Central PMS or does Central PMS pull from connector endpoint in each deployment topology?
- How should HCP connector health and projection freshness be modeled?
- What exact service-to-service authentication and authorization topology will be used without weakening the authority model?
- What secrets storage, rotation, revocation, and operational break-glass controls are required at implementation level?

## 14. Recommended ExitPass System Design v1.3 Sections Affected

Recommended sections for the System Design Lead to update or reference:

- Document Control: note that security/trust content is a controlled successor to v1.2 and uses approved v1.3 BRDs as baseline.
- System Overview: include v1.3 security posture as authority separation across WebPay, Central PMS, Payment Orchestrator, POS Server, terminals, Operator Console, Continuity, Management Dashboard, Vendor PMS, and gates.
- System Context: show external actors and trust posture without creating diagrams here.
- System Architecture: preserve module boundaries and non-authority rules.
- Trust Boundaries: add v1.3 boundaries for WebPay payment-scope URLs, Site Group/Site scoping, Assisted Payment Terminal, Continuity Terminal, Operator Console, POS Server, Management Dashboard, HikCentral connector, and Site POS Server.
- Core Workflows: apply RBAC/trust controls to WebPay, cashier-assisted payment, statutory discount capture, continuity activation, fiscal exception, manual release, and reporting/export workflows.
- Event Architecture: require auditable security-relevant events and correlation across identity, device, Site/Site Group, payment, fiscal, discount, continuity, reporting, and gate events.
- Data Architecture: reference data classification, privacy, evidence references, retention posture, and immutable/audit records without defining database tables.
- API Architecture: state boundary authentication/authorization principles only; defer endpoint auth schemes, OAuth scopes, and DTOs.
- Security Architecture: include human roles, service identities, device trust, RBAC domains, secrets posture, credential isolation, privacy controls, export controls, audit, and non-repudiation.
- Failure Mode Architecture: include fail-closed defaults, continuity activation authority, fiscal exceptions, manual release, payment uncertainty, vendor outage, stale projection, and gate/device issues.
- Observability: distinguish operational telemetry from audit evidence and financial truth; include security/audit observability.
- Business Continuity: preserve explicit activation, scope, supervisor approval where required, audit/reconciliation tags, and post-restoration review.
- Operational Runbooks: reference manual release, fiscal exception, device trust issue, credential incident, evidence access incident, export incident, and continuity activation review as future runbook concerns without drafting runbooks here.
- Appendix: collect open security questions and deferred implementation details.

## 15. Summary for System Design Lead

The v1.3 security posture is a controlled extension of the v1.2 trust-boundary model. The main drafting rule is to preserve authority separation and make every boundary explicit.

Confirmed architecture inputs:

- WebPay is public/customer-facing but scope-bound by Site Group/Site/payment-scope URL and has no payment-finality, fiscal, or exit authority.
- Site Group is lookup/payment scope; Site is reporting, Vendor PMS mapping, POS Server routing, and operational boundary.
- Central PMS remains payment finality, fiscal reference recording, and ExitAuthorization authority.
- Payment Orchestrator verifies provider outcomes and reports them; it does not declare platform finality.
- Vendor PMS/HCP remains normal session lifecycle and tariff authority, with HikCentral/vendor details isolated behind connector mapping and adapter boundary.
- Site POS Server remains fiscal issuance authority and does not issue ExitAuthorization.
- Assisted Payment Terminal is a trusted/hardened terminal surface with cashier and continuity modes, but not a policy, payment-finality, fiscal, or exit authority.
- Android-first is the preferred field-terminal reference posture, but exact terminal implementation remains deferred.
- Operator Console is an internal web/PWA governance surface with RBAC, device trust, shift, Site/Site Group, evidence, continuity, fiscal exception, manual release, and audit controls; it is non-payment and non-authority.
- Management Dashboard is reporting/export visibility only, with strong RBAC, scope, export audit, freshness labels, and privacy controls.
- Continuity must fail closed by default, activate explicitly, remain scoped and auditable, and require reconciliation/post-restoration review.
- Manual release is last resort, supervisor-approved where required, incident/audit/reconciliation-tagged, attributable, and reviewed.
- Evidence and exports require privacy controls, elevated permissions, redaction/minimization where applicable, retention policy, and access audit.

Main drafting caution:

Do not close the open implementation questions in the System Design by inventing endpoint schemes, exact mTLS topology, OAuth scopes, secrets storage internals, device enrollment mechanics, QR/digital Sales Invoice URL token design, database tables, or implementation classes. Keep those as downstream technical design or confirmation items unless an approved source closes them.
