# Assisted Payment Terminal Device Trust, Security, and Android-First Posture Input Pack

Status: Specialist input pack for Lead synthesis

Branch: `docs/v1.3-assisted-payment-terminal-system-design`

Assigned focus: Device trust, terminal identity, Android-first field posture, fixed station eligibility, kiosk lockdown, key storage questions, privacy, evidence, payment security, and offline/degraded safeguards.

## 1. Purpose

This input pack provides design-level device trust and security posture for the Assisted Payment Terminal System Design. It preserves the ExitPass v1.3 authority model while identifying controls the Lead should carry into the final design for Cashier-Assisted Terminal mode and Continuity Terminal mode.

This pack does not define final certificate model, mTLS topology, OAuth scopes, local key storage implementation, Android SDK calls, MDM product, kiosk package, endpoint authentication scheme, implementation classes, deployment scripts, secrets, keys, tokens, or credential values.

## 2. Source Documents Reviewed

| Source | Security-relevant use |
| --- | --- |
| `docs/v1.3/assisted-payment-terminal/system-design/ExitPass_Assisted_Payment_Terminal_System_Design_Orchestration_Plan.md` | Input-pack ownership, Android-first guardrails, authority guardrails, and non-implementation scope. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Terminal identity, cashier authentication, Site/Site Group binding, Android-first posture, continuity restrictions, evidence, privacy, audit, and open APT questions. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System trust boundaries, device trust posture, audit/event posture, continuity principles, deployment non-decisions, and deferred security details. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core authority model, Site/Site Group semantics, fiscal-before-exit choreography, continuity activation posture, and statutory discount privacy controls. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Continuity activation, disabled-by-default posture, projection freshness, fail-closed controls, manual release governance, and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console separation, device trust references, evidence controls, supervisor review, and governance boundary. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Reporting/export controls, operational visibility versus financial truth, evidence redaction, and audit/export security posture. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal authority, terminal/channel fiscal boundary, digital Sales Invoice URL/QR presentation, offline fiscal restrictions, and fiscal audit posture. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Open continuity activation, projection freshness, POS Server boundary, connector health, and unresolved planning questions. |

## 3. Trust Boundary Overview

The Assisted Payment Terminal is a trusted workflow surface only after both device and human context are established. The terminal boundary should be treated as higher risk than centralized backend services because it is physically exposed to cashier lanes, lessor counters, temporary continuity setups, and field support activity.

The Lead design should show these trust boundaries:

| Boundary | Design posture |
| --- | --- |
| Terminal device to backend | Requires trusted device identity, assigned Site/Site Group context, authenticated cashier context, active shift/session accountability, and backend authorization before payment workflow use. |
| Terminal UI to Central PMS | Terminal requests lookup, displays payable basis/status, submits capture inputs, and displays backend decisions; it does not own payment finality, discount approval, fiscal issuance, or ExitAuthorization. |
| Terminal to Payment Orchestrator/provider flow | Terminal may initiate or present approved payment flow, but card/payment-sensitive handling must remain inside approved hosted checkout or provider-controlled flow. |
| Terminal to Site POS Server fiscal path | Terminal may present, print, or display fiscal output returned through approved backend/POS flow; it does not issue Sales Invoices independently. |
| Terminal to Operator Console governance | Supervisor review, continuity approval, evidence review, manual release governance, and post-restoration review remain Operator Console or approved operations workflow responsibilities. |
| Terminal local storage boundary | Local state is restricted to managed, minimal, policy-approved operational data. The terminal must not store unmanaged secrets or unmanaged evidence. |

Wrong Site/Site Group context is a trust failure. The terminal should block processing or require authorized correction when the assigned terminal context, cashier scope, resolved session Site, or requested workflow scope does not align.

## 4. Android-First Hardened Terminal Posture

Android-first hardened terminal posture is the preferred reference posture for field-deployed Assisted Payment Terminals. The posture should be framed as a design target for exposed cashier lanes, portable/field terminals, continuity terminals, and terminal hardware requiring camera, scanner, printer, cash drawer, kiosk, device health, or native integration boundaries.

The Android-first posture should include:

- Managed device enrollment or equivalent approved trust registration at the design level, without naming a final MDM product.
- Device identity and assignment before terminal use.
- Device trust or attestation concept before payment workflow use, without selecting the final mechanism.
- Kiosk or lockdown concept for field deployments, without naming a final kiosk package.
- Restricted ability to install, side-load, debug, screen scrape, export data, or switch away from approved terminal workflow where policy requires.
- Clear separation between web-based workflow core, Android shell, WebView/PWA container, native bridge, and hardware integration responsibilities at design level.
- No Android SDK calls, package structure, native bridge API, Java/Kotlin class, device command, or deployment script detail.

Android-first does not mean Android-exclusive. It means the security bar for field terminals should be set by a hardened, managed, purpose-built terminal posture rather than by an ordinary unmanaged browser session.

## 5. Non-Android / Fixed Cashier Station Eligibility Posture

Fixed cashier station variants, including browser/PWA or desktop-compatible deployments, may be eligible only when they satisfy equivalent device trust, security, audit, and operational controls.

Eligibility should require:

- Registered device or workstation identity.
- Approved assignment to Site/Site Group and terminal role.
- Authenticated cashier/operator identity.
- Active shift/session accountability.
- Local storage restrictions equivalent to field terminals.
- Evidence handling controls equivalent to field terminals.
- Payment flow controls that prevent raw card capture.
- Audit logging of device, user, shift, Site/Site Group, workflow, evidence, payment-status display, fiscal-status display, and exception actions.
- Tamper-resistance and administrative control appropriate to a staffed fixed station.

The System Design should not allow fixed station compatibility to dilute Android-first field-terminal hardening. A fixed station should be a controlled deployment variant, not an unmanaged fallback.

## 6. Terminal Device Identity

Each Assisted Payment Terminal must have a durable device identity and platform assignment before workflow access. The final identity and trust mechanism remains open, but the design should require that device identity is established and evaluated before cashier payment activity.

Minimum device identity posture:

- Device is enrolled, registered, or otherwise approved before activation.
- Device is assigned to an allowed terminal mode, Site/Site Group scope, and operational status.
- Device trust status is evaluated before normal workflow use and before Continuity Terminal activation.
- Device identity is included in audit records, payment attempt context, discount capture context, fiscal presentation context, continuity activity, manual release messaging where applicable, and support events.
- Untrusted, retired, misassigned, duplicated, or context-mismatched devices deny workflow use or route to support according to approved policy.

The pack intentionally does not decide certificate hierarchy, token binding, mTLS topology, hardware-backed storage, browser key binding, or attestation provider.

## 7. Cashier / Operator Identity

Cashier/operator authentication is required before terminal workflow use. The terminal should not support anonymous payment, discount capture, fiscal presentation, continuity use, or exception handling.

Design posture:

- Cashier identity must be authenticated and associated with the active terminal session.
- Role and permission checks must be scoped by Site/Site Group, terminal mode, active shift/session, and action type.
- Supervisor approval is required for high-risk actions where policy requires, such as continuity activation, manual release messaging, evidence exception handling, fiscal exception escalation, or override-style workflows.
- Cashier identity should be recorded alongside device identity and shift/session context for non-repudiation.
- Failed login, unauthorized Site/Site Group access, no active shift, and untrusted device states should deny or restrict terminal workflow and be auditable where policy requires.

## 8. Site / Site Group / Shift Binding

The terminal must bind every workflow to assigned Site, Site Group, cashier/operator, and shift/session context. Site Group is the customer lookup/payment scope. Site is the reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary.

Required posture:

- Terminal startup or workflow entry establishes assigned Site/Site Group context.
- Cashier workflow is limited to authorized Site/Site Group and active shift/session scope.
- Parking session lookup and resolved Site should be checked against terminal and cashier scope.
- The resolved Site determines POS Server routing and fiscal attribution.
- Wrong Site/Site Group context blocks processing or requires authorized correction.
- Shift/session accountability must link terminal access, lookup, payable-basis display, discount capture, payment initiation/status display, fiscal-status display, ExitAuthorization status display, continuity activity, and escalation actions.

## 9. Service-to-Service and Terminal-to-Backend Trust

The final trust mechanism is deferred, but the System Design should state that terminal-to-backend and backend-to-backend trust must preserve authority boundaries and prevent terminal-local finality.

Design-level requirements:

- Terminal requests must carry enough authenticated device, cashier, Site/Site Group, mode, and shift/session context for backend authorization and audit.
- Backend services should validate terminal assignment and user scope before returning sensitive session, payable-basis, payment, fiscal, evidence, or continuity information.
- Payment Orchestrator reports verified provider outcomes, but Central PMS remains payment finality authority.
- POS Server remains fiscal issuance authority for the resolved Site.
- Central PMS remains authority for payment finality, fiscal reference recording, degraded resolve decisions under approved policy, and ExitAuthorization.
- Service-to-service trust should protect against replay, impersonation, context confusion, and unauthorized cross-Site/Site Group use, without this pack defining the exact protocol, certificate, token, endpoint, or scope model.

## 10. Local Storage and Offline Data Posture

Local terminal storage must be minimized, controlled, and non-authoritative. The terminal must not store unmanaged secrets, unmanaged evidence, raw card data, or terminal-local payment/fiscal/discount finality records.

Recommended posture:

- Persist only policy-approved operational state needed for session continuity, display recovery, device health, or audit correlation.
- Treat local cached data as temporary operational support, not financial truth, fiscal truth, discount approval, tariff authority, or exit authority.
- Avoid local retention of entitlement evidence. Where capture occurs, submit through approved backend workflow and retain references/hashes where possible.
- If offline behavior is later approved, restrict it to explicit continuity policy, incident/audit/reconciliation tags, freshness controls, and post-restoration review.
- If offline behavior is not approved for a workflow, fail closed or route to approved supervisor/manual review.

Open local storage items include final local key storage model, whether any offline evidence capture is permitted, offline payment policy, offline fiscal issuance policy, and exact retention/disposal behavior.

## 11. Evidence Capture and Privacy Controls

Evidence capture must follow minimization, privacy notice, RBAC, access audit, retention, and redaction principles from the approved BRDs. The terminal is a capture surface, not an unmanaged evidence repository.

Required posture:

- Capture only required statutory discount, identity, entitlement, VAT privilege, and exception evidence under approved policy.
- Prefer structured entitlement details, evidence references, hashes, and redacted/cropped evidence where possible.
- Display privacy notices where required.
- Submit evidence to approved backend workflow for Central PMS / Discount workflow and governance review.
- Do not retain unmanaged local evidence on the terminal.
- Restrict evidence access by role, Site/Site Group, shift/session, and workflow need.
- Audit evidence capture, access, submission, reference creation, and exception handling.
- Continuity-mode evidence activity should carry incident, audit, reconciliation, and post-restoration review context.

Exact evidence retention periods, redaction rules, offline evidence behavior, and jurisdiction-specific policy remain open.

## 12. Payment Security Posture

The terminal must not capture raw card data or become a payment card data collection surface. Card payment, if any, must use approved hosted checkout or approved payment provider-controlled flow.

Design requirements:

- Terminal may initiate, launch, display, or coordinate approved payment flow, but payment-sensitive handling must remain with approved Payment Orchestrator/provider-controlled surfaces.
- Terminal must not log, store, display, export, or transmit raw card data.
- Provider success or terminal UI success must not be shown as platform payment finality unless Central PMS records payment finality.
- Unknown provider outcome must remain pending/exception state and must not authorize exit.
- Terminal should display payment status as returned through approved backend flow: initiated, pending, failed, cancelled, completed, or exception as appropriate.
- Cash/eWallet/QR/card support and exact provider model remain downstream design questions.

## 13. Fiscal / QR / Digital Sales Invoice Presentation Security

The terminal may present fiscal status, Sales Invoice reference, digital Sales Invoice URL, QR code, or reprint/display option only where returned by approved backend/POS flow and allowed by policy. The terminal does not issue Sales Invoices independently.

Design posture:

- Resolved Site determines Site POS Server routing.
- POS Server issues the Sales Invoice and returns fiscal identity/status.
- POS Server returns only the digital Sales Invoice URL where QR-capable presentation is supported; terminal/channel presentation generates or renders the QR code.
- QR presentation does not make the terminal fiscal authority.
- Digital SI URL access, expiry, authentication, tokenization, privacy, and anti-tampering controls remain open for POS Server System Design and compliance confirmation.
- Terminal display should avoid exposing unnecessary sensitive fiscal, customer, or entitlement data.
- Fiscal issuance failure or timeout must not be presented as exit authorized.

## 14. Continuity Mode Security Controls

Continuity Terminal mode is disabled by default. It may activate only under approved degraded/BCP controls and only for explicitly scoped Site/Site Group, dependency, incident/BCP reference, allowed workflows, approval context, and duration/review interval.

Required continuity security posture:

- No silent fallback to Continuity Terminal mode.
- Activation requires approved authority or policy condition; exact authority remains open.
- Continuity activity must be incident-tagged, audit-tagged, reconciliation-tagged, and subject to post-restoration review.
- Projection use must be limited to approved degraded controls and freshness requirements.
- Stale, ambiguous, or insufficient projection fails closed or routes to approved supervisor/manual review.
- Continuity statutory discount handling is restricted; if entitlement, policy basis, evidence, projection freshness, or payable-basis recalculation is unsafe, fail closed or route to supervisor/manual review.
- Payment, fiscal, and exit handling remain under Central PMS, Payment Orchestrator/provider, POS Server, and approved manual release governance.
- Offline payment, offline fiscal issuance, and offline evidence behavior remain restricted/open unless later approved by policy and design.

## 15. Kiosk / Lockdown / Tamper Resistance Posture

Field terminals should be treated as physically exposed and potentially hostile environments. Kiosk, lockdown, and tamper resistance are design-level requirements for the preferred Android-first field posture and may be required for eligible fixed stations through equivalent workstation controls.

Design controls to carry forward:

- Restrict terminal to approved workflow surfaces and approved device integrations.
- Restrict access to operating system settings, unsupported browsers/apps, developer/debug functions, local file browsing, screen capture/export, and unauthorized peripheral use where policy requires.
- Monitor or report terminal health, trust state, enrollment/assignment state, app integrity state, and mode state at design level.
- Deny or restrict workflow on suspected tamper, unknown device, retired device, assignment mismatch, or unsupported environment.
- Treat scanner, camera, printer, cash drawer, and other peripheral integration as controlled input/output boundaries, not independent authorities.
- Preserve audit for device trust changes, terminal assignment changes, cashier login/logout, shift open/close, continuity activation/use, evidence capture, payment flow initiation/status display, fiscal presentation, and escalation events.

Final kiosk package, MDM product, Android API, attestation vendor, hardware-specific command, and desktop lockdown tool are deferred.

## 16. Secrets, Certificates, and Key Storage Open Questions

This input pack includes no secrets, key values, certificates, tokens, credential values, or environment variable names.

Open questions for Lead carry-forward:

| ID | Open question |
| --- | --- |
| SEC-OQ-001 | What is the final terminal device trust mechanism for Android field terminals and fixed cashier stations? |
| SEC-OQ-002 | What is the final certificate, token, browser key binding, or equivalent device identity model? |
| SEC-OQ-003 | What is the final local key or credential storage model for Android and fixed station variants? |
| SEC-OQ-004 | What is the final enrollment, assignment, rotation, revocation, retirement, and support recovery process for terminals? |
| SEC-OQ-005 | What is the final kiosk/lockdown product or control set for Android field deployments and fixed stations? |
| SEC-OQ-006 | What offline evidence capture behavior, if any, is allowed? |
| SEC-OQ-007 | What offline payment policy, if any, is allowed? |
| SEC-OQ-008 | What offline fiscal issuance policy, if any, is allowed? |
| SEC-OQ-009 | What are the final privacy, redaction, retention, and disposal rules for statutory and continuity evidence? |
| SEC-OQ-010 | What is the final digital Sales Invoice URL access, expiry, authentication, privacy, and anti-tampering model? |
| SEC-OQ-011 | What is the final permission matrix across cashier, supervisor, support, administrator, auditor, and reconciliation roles? |
| SEC-OQ-012 | What is the exact Continuity Terminal activation authority and approval workflow? |

## 17. Audit and Non-Repudiation Requirements

The Assisted Payment Terminal should support reconstruction of who did what, on which trusted terminal, during which shift, under which Site/Site Group, against which session/payment/fiscal/continuity context, and with what backend result.

Audit should include:

- Cashier authentication, failed access, logout, and session expiry.
- Device identity, trust status, assignment, health, and tamper-related events.
- Site/Site Group context and wrong-context blocks or corrections.
- Shift/session start, end, and workflow association.
- Ticket/card lookup and manual entry events.
- Payable-basis display and refresh source.
- Statutory discount capture, cashier attestation, evidence references/hashes, validation submission, and validation result display.
- Payment initiation and payment status display without terminal-owned finality.
- Fiscal issuance request/status display, fiscal reference/QR/digital SI URL presentation where allowed, and fiscal exception display.
- ExitAuthorization status display without terminal-issued authorization.
- Continuity activation/use, incident/audit/reconciliation tags, projection freshness where used, and post-restoration review linkage.
- Supervisor escalation and manual release messaging where allowed by policy.

Audit records should be tamper-evident at platform level, correlated across backend authorities, and protected from local terminal deletion or alteration.

## 18. Security Risks and Mitigations

| Risk | Impact | Mitigation posture |
| --- | --- | --- |
| Untrusted terminal used for payment workflow | Fraud, privacy exposure, and audit gaps. | Require device identity, assignment, trust checks, and denial/restriction for unknown or misassigned devices. |
| Android-first weakened into unmanaged browser use | Field terminal hardening lost. | Preserve Android-first hardened reference posture and require equivalent controls for fixed station variants. |
| Wrong Site/Site Group processing | Incorrect lookup, POS routing, fiscal attribution, and reporting. | Bind terminal, cashier, session, and shift to Site/Site Group; block or require authorized correction on mismatch. |
| Terminal stores unmanaged secrets | Credential compromise and impersonation. | Prohibit unmanaged secrets and defer managed key/certificate/token model to security design. |
| Terminal stores unmanaged evidence | Privacy and compliance exposure. | Prefer references/hashes/redaction; submit to approved backend workflow; prohibit unmanaged local evidence retention. |
| Raw card data captured by terminal | Payment security and compliance failure. | Use hosted checkout or provider-controlled payment flow; prohibit raw card capture, logging, storage, and export. |
| Provider success confused with platform finality | Premature fiscal or exit actions. | Display backend status only; Central PMS remains payment finality authority. |
| Fiscal URL/QR tampering or overexposure | Privacy, fiscal, and customer trust risk. | Treat terminal as presenter only; carry forward digital SI URL security controls as POS Server open design item. |
| Continuity Terminal overuse | Degraded mode becomes uncontrolled alternate path. | Disabled by default; require approved activation, scope, incident/audit/reconciliation tags, and post-restoration review. |
| Offline behavior expands silently | Uncontrolled payment, fiscal, evidence, or discount activity. | Keep offline behavior restricted/open; fail closed unless explicitly approved by continuity/POS/security policy. |
| Kiosk/lockdown bypass | Tampering, data extraction, or unauthorized peripheral use. | Require kiosk/lockdown concept, tamper reporting, and workflow denial/restriction on trust failure. |

## 19. Open Security Questions

The Lead should carry these unresolved items into the final Assisted Payment Terminal System Design without inventing implementation details:

- Final Android shell, WebView, PWA core, native bridge, and hardware integration split.
- Final eligibility rules for browser/PWA/desktop-compatible fixed cashier station variant.
- Final terminal hardware integration requirements by terminal type.
- Final device trust/attestation/control mechanism.
- Final certificate/key/token storage and rotation/revocation model.
- Final kiosk/lockdown/tamper-resistance product or control set.
- Final offline evidence, payment, and fiscal policies.
- Final Continuity Terminal activation authority and approval workflow.
- Final projection freshness threshold used during degraded operation.
- Final permission matrix for cashier, supervisor, support, administrator, auditor, and reconciliation roles.
- Final digital SI URL token/access/expiry/authentication/privacy/anti-tampering model.
- Final evidence retention, redaction, disposal, and export controls.
- Final fiscal reprint/display behavior allowed from the terminal.

## 20. Summary for Lead

The final Assisted Payment Terminal System Design should treat Android-first hardened posture as the preferred field-terminal reference posture while allowing non-Android fixed cashier station variants only if equivalent device trust, security, audit, and operational controls are satisfied.

Core design carry-forward:

- Terminal must establish device identity, assignment, Site/Site Group scope, cashier identity, and shift/session accountability before workflow use.
- Wrong Site/Site Group context must block or require authorized correction.
- Terminal is a capture, presentation, and workflow coordination surface only.
- Terminal must not store unmanaged secrets, unmanaged evidence, or raw card data.
- Card payment, if supported, must use approved hosted checkout or payment provider-controlled flow.
- Evidence capture should use minimization, privacy notice, references/hashes/redaction, RBAC, and audit.
- Fiscal/QR/digital SI presentation is terminal/channel presentation only; POS Server remains fiscal issuer.
- Continuity Terminal remains disabled by default, explicitly activated, scope-bound, incident/audit/reconciliation-tagged, and post-reviewed.
- Offline behavior remains restricted/open and should fail closed unless later approved.
- Exact certificate, mTLS, OAuth, key storage, Android SDK, MDM, kiosk, endpoint auth, and implementation-class choices remain deferred.
