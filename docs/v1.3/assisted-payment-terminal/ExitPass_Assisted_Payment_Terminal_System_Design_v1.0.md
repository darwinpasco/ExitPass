# ExitPass Assisted Payment Terminal System Design v1.0

Status: Draft companion technical design for v1.3

## 1. Document Control

### Version History

| Version | Date | Description |
| --- | --- | --- |
| v1.0 | 2026-07-02 | Initial companion System Design for the Assisted Payment Terminal app family, covering Cashier-Assisted Terminal mode, Continuity Terminal mode, terminal trust, device identity, payment/fiscal/exit status display, statutory discount capture, Android-first field posture, fixed station eligibility, audit, observability, and authority guardrails. |

### Document Ownership

| Role | Owner |
| --- | --- |
| Documentation stream | ExitPass v1.3 documentation |
| Lead design owner | Lead Assisted Payment Terminal Design agent |
| Downstream consumers | API Contract Pack, Database Delta, Engineering Pack, Test/UAT Pack, Operations Runbook Pack, and terminal implementation planning |

### Approval Posture

This document is a companion System Design. It structures terminal architecture and authority boundaries but does not approve final endpoint contracts, DTOs, database objects, Android implementation, browser/PWA packaging, native bridge APIs, device SDK calls, printer commands, deployment scripts, UAT scripts, runbook procedures, or secrets.

## 2. Executive Summary

The Assisted Payment Terminal is a separate payment-capable terminal app family for staffed and degraded parking operations. It supports two approved operating modes:

- Cashier-Assisted Terminal mode for normal staffed assisted-payment operations.
- Continuity Terminal mode for restricted degraded/BCP operation.

The terminal captures user input, presents backend status, coordinates cashier workflows, and hands off governance cases. It does not own platform payment finality, Sales Invoice issuance, statutory discount policy decisions, payable-basis updates, ExitAuthorization, or gate execution.

The design uses an Android-first hardened posture as the preferred reference for field-deployed terminals. Android-first is not Android-exclusive. Fixed cashier station browser/PWA or desktop-compatible variants may be eligible only if they satisfy equivalent device trust, security, audit, POS, and operating controls.

## 3. Design Purpose and Scope

This design defines the system boundary and workflows for the Assisted Payment Terminal.

In scope:

- Logical architecture and backend dependency model.
- Cashier-Assisted Terminal and Continuity Terminal mode boundaries.
- Terminal/device identity, cashier authentication, shift/session context, and Site/Site Group binding.
- Ticket-first lookup posture where vendor support permits.
- HCP `cardNum` and ticket-only lookup uncertainty.
- Session lookup through Central PMS and Vendor PMS/HCP connector.
- Payable-basis display from Central PMS.
- Statutory discount capture and Central PMS / Discount workflow handoff.
- Payment initiation and provider/Central PMS status display.
- POS Server fiscal issuance status and Central PMS fiscal reference status display.
- ExitAuthorization status display.
- Fiscal issuance failure, pending exit, and manual release governance handoff.
- Continuity Terminal activation, restricted operation, projection-based context display, and fail-closed rules.
- Device trust, security, Android-first posture, fixed station eligibility, local storage limits, evidence privacy, payment security, audit, observability, and reconciliation.

Out of scope:

- Source code changes.
- Database schema changes.
- API contract changes.
- Engineering implementation classes.
- Android package structure, WebView framework, native bridge API, SDK calls, printer commands, or deployment scripts.
- POS Server System Design, Operator Console System Design, Continuity System Design, Test/UAT Pack, or Runbook Pack.

## 4. Approved Baseline Inputs

| Source | Use |
| --- | --- |
| `docs/v1.3/assisted-payment-terminal/system-design/ExitPass_Assisted_Payment_Terminal_System_Design_Orchestration_Plan.md` | Scope, guardrails, file ownership, and review gates. |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/01_architecture_scope_guard.md` | Authority model, terminology, non-authority matrix, and deferrals. |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/02_terminal_workflow_and_state.md` | Workflow and conceptual state guidance for normal, continuity, exception, and handoff paths. |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/03_device_trust_security_android_posture.md` | Device trust, Android-first posture, fixed station controls, payment security, local storage, evidence, and privacy posture. |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/04_diagram_planning.md` | Diagram set, purpose, component expectations, and authority labels. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 business authority model and APT positioning. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System-level trust boundaries, APT backend boundary, continuity architecture, and audit posture. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approved BRD baseline status and downstream open-question posture. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Primary APT business source. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Continuity Terminal activation, restricted operation, and post-restoration controls. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console separation and governance handoff. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Visibility/reporting boundary and source labeling. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | POS Server fiscal authority and digital Sales Invoice/QR presentation posture. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Vendor PMS connector resolve, fee, projection, and health boundaries. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HCP identifier uncertainty, passageway projection, fee calculation, and conditional acknowledgment posture. |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved APT app-family, statutory discount, fiscal routing, and separation decisions. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Open questions to preserve. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | APT, continuity, API, database, and engineering impact mapping. |

Existing APT BRD diagrams under `docs/v1.3/assisted-payment-terminal/diagrams/` were used as business input only.

## 5. Assisted Payment Terminal Architecture Overview

The Assisted Payment Terminal is a terminal/channel workflow surface connected to approved ExitPass backend services.

Primary logical components:

- Assisted Payment Terminal app family.
- Cashier-Assisted Terminal mode.
- Continuity Terminal mode.
- Terminal device identity and trust controls.
- Cashier/operator identity and shift/session context.
- Assigned Site and Site Group context.
- Central PMS backend workflow.
- Central PMS / Discount workflow.
- Payment Orchestrator or approved payment integration.
- Resolved Site POS Server.
- Vendor PMS connector and Vendor PMS/HCP.
- Operator Console or approved operations workflow for governance handoff.
- Management Dashboard and Reporting for visibility.
- Audit/event and reconciliation consumers.

The terminal must submit device, cashier, shift, Site, Site Group, and operating-mode context to backend workflows where relevant. Backend services return displayable workflow state and authority decisions. The terminal presents those statuses without creating authoritative records outside its local workflow state.

## 6. Authority Model

| Function | Authority | APT posture |
| --- | --- | --- |
| Terminal UI and workflow coordination | Assisted Payment Terminal | Captures input, presents backend status, and coordinates cashier workflow. |
| Device identity and terminal assignment | Identity/platform controls | Terminal presents and enforces trust prerequisites. |
| Cashier authentication and shift/session context | Identity/shift governance with platform audit correlation | Terminal blocks workflows when required context is invalid. |
| Normal raw parking session lifecycle | Vendor PMS/HCP | Terminal requests lookup through Central PMS. |
| Normal tariff computation | Vendor PMS/HCP through Central PMS and connector | Terminal displays backend-approved payable basis. |
| Platform payable basis and TariffSnapshot | Central PMS | Terminal does not calculate or alter payable basis. |
| Statutory discount policy resolution | Central PMS / Discount workflow | Terminal captures and submits inputs only. |
| Payment provider interaction | Payment Orchestrator or approved payment integration | Terminal initiates or presents approved flow. |
| Platform payment finality | Central PMS | Terminal displays finality status returned by backend. |
| Sales Invoice issuance | Resolved Site POS Server | Terminal displays or presents returned fiscal output where allowed. |
| Fiscal issuance reference recording | Central PMS | Terminal displays returned reference status. |
| Degraded resolve decision | Central PMS under approved Continuity policy | Terminal displays degraded context and restrictions. |
| ExitAuthorization | Central PMS | Terminal displays status returned by backend. |
| Manual release governance | Operator Console or approved operations workflow | Terminal hands off or displays governance status. |
| Dashboard/reporting visibility | Management Dashboard and Reporting | Terminal supplies event/status context where approved. |

## 7. Non-Authority Scope

The Assisted Payment Terminal shall not:

- Act as Operator Console.
- Declare platform payment finality.
- Issue Sales Invoices independently.
- Issue ExitAuthorization.
- Directly open gates.
- Approve statutory entitlement.
- Mutate payable basis directly.
- Invent tariff from projection or passageway records.
- Treat projection as financial, fiscal, payment, discount, tariff, or exit authority.
- Store unmanaged secrets, unmanaged evidence, raw card data, or terminal-local finality records.
- Treat provider success, vendor state, fiscal display, or cashier judgment as a substitute for Central PMS-controlled state.

## 8. Operating Modes

The Assisted Payment Terminal has two approved modes.

| Mode | Purpose | Default posture |
| --- | --- | --- |
| Cashier-Assisted Terminal | Normal staffed assisted-payment mode. | Available only to trusted assigned terminals, authenticated cashiers, valid shifts, and authorized Sites/Site Groups. |
| Continuity Terminal | Restricted degraded/BCP mode. | Disabled by default; enabled only under approved Continuity controls and activation scope. |

Mode switching must preserve audit and permission boundaries. Continuity Terminal availability must never be an implicit fallback from normal failures.

## 9. Cashier-Assisted Terminal Mode

Cashier-Assisted Terminal mode supports:

- Cashier login.
- Terminal/device trust evaluation.
- Assigned Site/Site Group display and enforcement.
- Shift/session accountability.
- Ticket/card scan or manual entry.
- Central PMS-mediated session lookup.
- Payable-basis display from backend authority.
- Statutory discount capture.
- Evidence reference capture where required.
- Payment initiation.
- Provider outcome and Central PMS finality display.
- POS Server fiscal status display.
- Central PMS fiscal reference status display.
- ExitAuthorization status display.
- Customer/operator messaging.
- Supervisor escalation handoff.

Payment must not proceed using an unapproved discounted payable basis. The terminal should reset customer/session-specific display state after completion, governed cancellation, or handoff to exception processing.

## 10. Continuity Terminal Mode

Continuity Terminal mode is restricted degraded/BCP operation.

Design rules:

- Disabled by default.
- Available only under approved activation scope.
- Requires authorized terminal, cashier, Site/Site Group, and shift/session context.
- Requires incident/BCP context where policy requires.
- Allows only workflows permitted by active Continuity policy.
- Displays projection freshness, degraded context, fiscal restrictions, payment restrictions, and escalation guidance returned by backend authority.
- Restricts statutory discount handling to approved degraded-mode policy.
- Fails closed or routes to supervisor/manual review when entitlement, evidence, projection freshness, payable basis, payment, fiscal, or exit state is unsafe.
- Supports post-restoration review handoff.

Vendor PMS/HCP outage, WebPay/APM outage, connector stale state, or network degradation does not by itself authorize payment, fiscal issuance, degraded tariff, manual release, or exit.

## 11. Terminal Trust Boundary and Device Identity

The terminal device is a trust boundary. A terminal must establish device identity and platform assignment before payment workflow use.

Required design posture:

- Durable terminal/device identity.
- Site and Site Group assignment validation.
- Operating mode entitlement by device and user role.
- Device trust evaluation before cashier workflow access.
- Trust failure blocks workflow or routes to support/governance.
- Device identity is included in lookup, discount capture, payment initiation, fiscal display, continuity, exception, audit, and reconciliation context.
- Device identity does not grant backend authority.

Exact certificate, token, key, attestation, mTLS, browser key binding, rotation, revocation, and break-glass mechanisms remain deferred.

## 12. Cashier Authentication, Shift, Site, and Site Group Binding

Before lookup, discount capture, payment initiation, fiscal display, continuity activity, or exception handoff, the terminal must validate:

- Cashier/operator identity.
- Device trust and assignment.
- Authorized Site and Site Group scope.
- Active shift/session where policy requires.
- Operating mode permission.
- Continuity activation scope when using Continuity Terminal mode.

Site Group is lookup/payment scope. Site is reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary. Wrong-site or wrong-scope processing must block or route to authorized correction.

## 13. Ticket / Session Lookup Design

The terminal uses ticket-first/ticket-only lookup posture where vendor support permits.

Lookup flow:

1. Cashier scans or manually enters the customer reference.
2. Terminal classifies the input at user-action level only, such as ticket/card/manual lookup.
3. Terminal sends lookup context through Central PMS-approved backend workflow with cashier, device, shift, Site, Site Group, and mode context.
4. Central PMS resolves Site/Site Group context and uses Vendor PMS connector where live vendor lookup or fee calculation is available.
5. Vendor PMS/HCP provides live session/tariff facts where capability and identifier policy are confirmed.
6. Central PMS returns resolved, not found, ambiguous, degraded, blocked, or escalation display context.

HCP-specific caution:

- HCP `cardNum` appears in passageway and fee contexts.
- Local sources do not prove that a physical printed ticket number maps to HCP `cardNum`.
- HCP ticket-only fee calculation remains unconfirmed until vendor/deployment validation confirms the correct lookup key and barcode/QR payload behavior.
- HCP `parkingfee/confirm` is a mutating vendor acknowledgment area and is not part of terminal lookup.

The terminal must not choose among ambiguous sessions by heuristic.

## 14. Normal Cashier-Assisted Payment Workflow

Normal workflow:

1. Cashier signs in on a trusted assigned terminal.
2. Terminal validates Site/Site Group and shift/session context.
3. Cashier scans or manually enters ticket/card reference.
4. Terminal submits lookup through Central PMS.
5. Central PMS resolves session and payable basis using Vendor PMS/HCP through connector where available.
6. Terminal displays payable basis returned by backend authority.
7. Cashier captures statutory discount inputs if requested and policy allows.
8. Central PMS / Discount workflow resolves validation and payable-basis effect.
9. Terminal displays approved payable-basis refresh or non-approved validation status.
10. Cashier initiates payment only after payable basis is established.
11. Payment Orchestrator or approved payment integration handles provider interaction.
12. Terminal displays provider outcome as returned through backend workflow.
13. Central PMS records platform payment finality after verified outcome.
14. Central PMS requests Sales Invoice issuance from resolved Site POS Server.
15. POS Server returns fiscal status/identity.
16. Central PMS records fiscal issuance reference.
17. Central PMS issues ExitAuthorization if eligible.
18. Terminal displays fiscal, exit, and customer instruction status returned by backend.

## 15. Statutory Discount Capture and Payable-Basis Refresh

The terminal is the cashier-facing capture surface.

It may capture:

- Entitlement type requested by the customer.
- Required structured details.
- Evidence references where policy requires.
- Cashier attestation.
- Device, shift, Site, Site Group, and session context.
- Privacy notice acknowledgment where required.

It submits captured inputs to Central PMS / Discount workflow. Central PMS / Discount workflow owns policy resolution, validation persistence, evidence reference governance, and payable-basis effect.

If validation is approved, Central PMS refreshes payable basis before payment. If validation is rejected, failed, expired, or pending review, the terminal must not apply discounted payable basis. Supervisor/compliance review is handed off to Operator Console or approved operations workflow where policy requires.

## 16. Payment Initiation and Provider Outcome Display

The terminal may initiate or present approved payment flow after backend-approved payable basis exists.

Payment security posture:

- No raw card capture by the terminal.
- Card, eWallet, or QR payment should use hosted checkout or provider-controlled payment posture unless later design approves another compliant integration.
- Provider success is displayed as provider/backend status, not as platform finality.
- Central PMS displays or returns finality state only after verified outcome and platform controls.
- Duplicate submissions should show in-progress, pending, or already-submitted status based on backend state.

Whether cash payment is supported in Cashier-Assisted Terminal v1.0 remains open.

## 17. Fiscal Issuance Status Display

After Central PMS records platform payment finality, Central PMS routes fiscal issuance to the resolved Site POS Server.

The terminal may display:

- Fiscal issuance requested.
- Sales Invoice issued.
- Fiscal reference received by Central PMS.
- Fiscal issuance pending.
- Fiscal issuance failed.
- Fiscal issuance timed out.
- Fiscal exception under review.
- Digital Sales Invoice URL or QR presentation where allowed.

The terminal does not issue Sales Invoices independently. Exact fiscal reprint/display behavior, POS Server X-read/Z-read or cashier shift report handoff, and fiscal output presentation rules remain open.

## 18. ExitAuthorization Status Display

Central PMS evaluates eligibility after payment finality, fiscal issuance success, fiscal reference recording, and other control conditions.

The terminal may display:

- ExitAuthorization issued.
- ExitAuthorization pending.
- ExitAuthorization blocked because payment finality is not recorded.
- ExitAuthorization blocked because fiscal issuance is pending/failed.
- ExitAuthorization blocked because lookup/session state is unresolved.
- Governed manual release status where formally approved.

The terminal must not infer exit eligibility from provider success, fiscal display, vendor state, or cashier judgment.

## 19. Fiscal Issuance Failure / Pending Exit Handling

If payment finality is recorded but fiscal issuance fails or times out:

- Payment finality is not automatically reversed.
- Central PMS does not issue normal ExitAuthorization yet.
- Terminal displays that payment was received but fiscal issuance and/or exit authorization is pending.
- The case enters controlled fiscal exception, retry, or review workflow.
- Operator Console or approved operations workflow supports review/escalation where policy allows.
- If fiscal issuance later succeeds, Central PMS records fiscal reference and reevaluates exit eligibility.
- If manual release policy applies, governance is handled separately with supervisor approval where required, incident/audit/reconciliation tagging, reason, attribution, and post-review.

Customer/operator messaging must distinguish payment received, fiscal issuance complete, and exit authorized.

## 20. Continuity Terminal Activation and Restricted Operation

Continuity Terminal activation is a controlled event.

Activation context should include:

- Affected Site and Site Group.
- Affected dependency.
- Incident or BCP reference.
- Activation reason.
- Activation scope.
- Approval actor where required.
- Allowed and restricted workflows.
- Activation time and expected review interval.
- Audit and reconciliation tags.

Continuity Terminal mode becomes available only for authorized terminals, cashiers, Sites/Site Groups, shifts, and activation scope. Deactivation disables continuity-only workflows and sends continuity-origin activity into post-restoration review where applicable.

Exact activation authority and approval workflow remain open.

## 21. Degraded Resolve / Projection-Based Context Handling

Projection can support operational visibility and controlled degraded context only.

When Vendor PMS/HCP live resolve or fee calculation is unavailable:

1. Central PMS evaluates whether approved Continuity policy is active for the affected scope.
2. Central PMS checks projection freshness, ambiguity, mapping status, and approved degraded tariff basis.
3. If allowed, Central PMS returns degraded context/payable basis with source, freshness, and restriction labels.
4. Terminal displays the returned degraded/projection-based context.
5. If projection is stale, ambiguous, insufficient, or outside policy, the terminal fails closed or routes to supervisor/manual review.

The terminal must not invent tariff from projection, passageway records, or local history.

## 22. Manual Release Governance Handoff

Manual release is a last-resort governance process, not normal ExitAuthorization.

Terminal posture:

- Detect or display fiscal/exit/continuity exception context returned by backend.
- Present supervisor assistance or approved handoff instructions.
- Send exception context to Operator Console or approved operations workflow where enabled.
- Display governance status or instruction returned by backend.
- Preserve cashier, terminal, shift, Site/Site Group, session, payment, fiscal, incident, audit, and reconciliation context.

Gate/physical release execution remains outside the terminal workflow unless a later approved emergency process defines a controlled boundary.

## 23. Device Trust, Security, and Android-First Posture

Android-first hardened terminal posture is the preferred reference posture for field-deployed terminals.

Design posture:

- Managed purpose-built terminal profile for field devices.
- Device trust evaluation before workflow access.
- Kiosk/lockdown concept for exposed deployments.
- Controlled peripheral boundaries for scanner, camera, printer, cash drawer, and related devices.
- Clear separation between workflow core, native shell/container, native bridge boundary, and hardware integration boundary.
- Local storage minimized and non-authoritative.
- Evidence and privacy controls enforced.
- Offline/degraded safeguards enforced.

Android-first is not Android-exclusive. Exact Android shell, WebView/PWA core, native bridge, hardware integration, MDM/kiosk product, local key storage, and deployment packaging remain deferred.

## 24. Fixed Cashier Station / Browser-PWA Eligibility

Fixed cashier station browser/PWA or desktop-compatible variants may be eligible only if they satisfy equivalent controls:

- Managed workstation or locked-down device posture.
- Durable device identity.
- Site/Site Group assignment enforcement.
- Cashier authentication and shift/session accountability.
- Peripheral controls appropriate to the deployment.
- No unmanaged browser or unmanaged shared workstation use for payment workflows.
- No raw card capture.
- Evidence privacy controls.
- Audit of device, cashier, shift, Site/Site Group, workflow, payment, fiscal display, and exception actions.

Fixed station support must not dilute field-terminal hardening.

## 25. Evidence Capture and Privacy Controls

The terminal is a capture surface, not an unmanaged evidence repository.

Evidence posture:

- Collect minimum required statutory discount or continuity evidence.
- Display privacy notices where required.
- Capture references rather than unmanaged local copies where possible.
- Avoid unmanaged local evidence storage.
- Associate evidence capture with cashier, device, shift, Site/Site Group, session, mode, and policy context.
- Protect evidence access through RBAC and audit.
- Hand off supervisor/compliance review to Operator Console or approved operations workflow.

Exact retention periods, redaction rules, offline evidence behavior, and jurisdiction-specific handling remain open.

## 26. Payment Security Posture

The terminal must not store, log, display, export, or transmit raw card data outside approved provider-controlled flows.

Payment design posture:

- Use hosted checkout or provider-controlled flow for card/eWallet/QR unless later design approves another compliant model.
- Separate provider-facing status from Central PMS finality status.
- Include cashier, device, shift, Site/Site Group, session, and payment attempt context for audit.
- Treat unknown provider outcome as pending/exception until verified through approved payment workflow.
- Avoid duplicate payment initiation through backend state correlation.

Exact payment rail integration model remains deferred.

## 27. Fiscal / QR / Digital Sales Invoice Presentation Security

The terminal may present fiscal output returned through approved backend/POS flow.

Design posture:

- POS Server issues Sales Invoice.
- Central PMS records fiscal reference.
- POS Server may return a digital Sales Invoice URL where QR presentation is supported.
- Terminal/channel presentation may render that URL as a QR code where allowed.
- QR presentation does not make the terminal fiscal authority.
- Fiscal URL access, expiry, authentication, tokenization, privacy, and anti-tampering controls remain downstream design items.

The terminal must not construct fiscal identity, fiscal numbering, or fiscal records locally.

## 28. Local Storage and Offline Data Posture

Local terminal storage must be minimal, managed, and non-authoritative.

The terminal must not store:

- Unmanaged secrets.
- Raw card data.
- Unmanaged statutory evidence.
- Terminal-local payment finality records.
- Terminal-local fiscal authority records.
- Terminal-local discount approval records.
- Terminal-local ExitAuthorization records.

Offline behavior remains restricted/open. If a workflow is not approved for offline operation, the terminal must fail closed or route to approved supervisor/manual review. Offline evidence, payment, and fiscal policies remain deferred.

## 29. Audit, Observability, and Reconciliation Posture

The terminal must support reconstruction of who did what, on which trusted device, during which shift, under which Site/Site Group, against which session/payment/fiscal/continuity context, and with what backend result.

Audit/observability context should include:

- Terminal identity and trust state.
- Cashier/operator identity.
- Shift/session state.
- Site/Site Group assignment.
- Operating mode.
- Lookup input type and lookup outcome.
- Payable-basis display and refresh.
- Statutory discount capture and validation status.
- Evidence reference capture.
- Payment initiation and status display.
- Central PMS finality display.
- Fiscal issuance status display.
- Digital Sales Invoice URL/QR presentation where allowed.
- ExitAuthorization status display.
- Continuity activation and restricted operation tags.
- Fiscal exception and manual release handoff context.
- Terminal health and support events.

Management Dashboard and Reporting may consume terminal health and workflow visibility where authorized, but remains visibility/reporting only.

## 30. Failure Modes and Fail-Closed Rules

The terminal must fail closed or route to approved review when:

- Device trust is invalid.
- Cashier authentication fails.
- Required shift/session is missing.
- Site/Site Group assignment is invalid.
- Lookup result is not found, ambiguous, or unsafe.
- HCP ticket lookup key is unconfirmed for the requested flow.
- Vendor PMS/HCP is unavailable and no approved degraded policy applies.
- Projection is stale, ambiguous, insufficient, or outside policy.
- Statutory discount validation is rejected, failed, expired, or pending review.
- Payable basis is missing or unsafe.
- Provider outcome is unknown.
- Fiscal issuance fails or times out.
- ExitAuthorization is pending or blocked.
- Continuity activation is missing or outside scope.
- Manual release governance is not approved.
- Local storage, evidence, payment, or fiscal posture violates policy.

Fail-closed means the terminal does not imply payment finality, fiscal success, discount approval, payable-basis validity, exit eligibility, or gate release.

## 31. Deployment Posture

Deployment posture should support:

- Android-first hardened field terminals where field deployment is required.
- Eligible fixed cashier station variants only under equivalent controls.
- Site/Site Group assignment.
- Terminal provisioning and deprovisioning.
- Environment segregation.
- Device trust lifecycle.
- Cashier and shift/session controls.
- Continuity mode disabled by default.
- Controlled activation scope.
- Operational health reporting.
- Audit and reconciliation correlation.

Exact packaging, infrastructure, scaling, MDM/kiosk tooling, certificate/key provisioning, browser/PWA eligibility, deployment scripts, and support runbooks are deferred.

## 32. Open Questions and Deferred Decisions

| ID | Open question / deferred decision |
| --- | --- |
| APT-SD-OQ-001 | Final Android shell / WebView / PWA / native bridge split. |
| APT-SD-OQ-002 | Final fixed cashier station browser/PWA eligibility. |
| APT-SD-OQ-003 | Final terminal hardware integration requirements. |
| APT-SD-OQ-004 | Final scanner/camera/printer/cash drawer integration by terminal type. |
| APT-SD-OQ-005 | Final kiosk lockdown requirements. |
| APT-SD-OQ-006 | Final terminal certificate/key storage model. |
| APT-SD-OQ-007 | Final offline evidence capture behavior. |
| APT-SD-OQ-008 | Final offline payment policy. |
| APT-SD-OQ-009 | Final offline fiscal issuance policy. |
| APT-SD-OQ-010 | Final Continuity Terminal activation authority. |
| APT-SD-OQ-011 | Final degraded projection freshness threshold. |
| APT-SD-OQ-012 | Final degraded tariff basis and owner. |
| APT-SD-OQ-013 | Final manual release policy. |
| APT-SD-OQ-014 | Final fiscal exception release policy. |
| APT-SD-OQ-015 | Final permission matrix/RBAC. |
| APT-SD-OQ-016 | Whether cash payment is supported in Cashier-Assisted Terminal v1.0. |
| APT-SD-OQ-017 | Whether card/eWallet/QR payments are hosted checkout only or terminal-integrated. |
| APT-SD-OQ-018 | Fiscal reprint/display behavior from terminal. |
| APT-SD-OQ-019 | Handoff to POS Server for X-read/Z-read or cashier shift reports. |
| APT-SD-OQ-020 | HCP `cardNum` meaning and correct ticket-only lookup key. |
| APT-SD-OQ-021 | HCP `parkingfee/confirm` requirement and behavior. |
| APT-SD-OQ-022 | Exact API endpoints and DTOs. |
| APT-SD-OQ-023 | Exact database changes. |
| APT-SD-OQ-024 | Exact event payloads. |
| APT-SD-OQ-025 | Exact engineering implementation. |
| APT-SD-OQ-026 | Exact UAT scripts. |
| APT-SD-OQ-027 | Exact runbook procedures. |

## 33. Requirements Traceability Summary

| Requirement area | Source | Design sections |
| --- | --- | --- |
| APT app family and modes | APT BRD, orchestration plan, input pack 01 | 5, 8, 9, 10 |
| Authority model | ExitPass BRD, System Design, input pack 01 | 6, 7, 30 |
| Cashier workflow and state | Input pack 02, APT BRD | 12, 13, 14, 16 |
| Statutory discount capture | APT BRD, Operator Console BRD, input packs 01 and 02 | 15, 25 |
| Payment/fiscal/exit status display | POS/Invoicing BRD, System Design, input pack 02 | 16, 17, 18, 19, 27 |
| Continuity Terminal | Continuity BRD, input pack 02 | 10, 20, 21, 22 |
| Device trust and Android-first posture | Input pack 03, System Design | 11, 23, 24, 28, 31 |
| Vendor/HCP lookup uncertainty | Vendor PMS Connector System Design, HikCentral Connector Profile, input pack 02 | 13, 21, 32 |
| Audit, observability, reconciliation | System Design, Management Dashboard BRD, input packs 02 and 03 | 29, 30 |
| Diagram planning | Input pack 04 | Appendix C |

## 34. Appendix A: Glossary

| Term | Definition |
| --- | --- |
| Assisted Payment Terminal | Payment-capable terminal app family for cashier-assisted and continuity payment workflows. |
| Cashier-Assisted Terminal | Normal staffed assisted-payment mode of the APT app family. |
| Continuity Terminal | Restricted degraded/BCP mode of the APT app family, disabled by default. |
| Central PMS | Platform control authority for payment-linked state, payable-basis recording, payment finality, fiscal reference recording, degraded decisions, and ExitAuthorization. |
| Discount workflow | Central PMS-backed statutory discount policy resolution and validation persistence workflow. |
| Site | Reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary. |
| Site Group | Customer lookup/payment scope. |
| Site POS Server | Resolved Site fiscal issuance authority. |
| TariffSnapshot | Central PMS record of accepted payable basis. |

## 35. Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| APM | AutoPay Machine |
| APT | Assisted Payment Terminal |
| BCP | Business Continuity Plan |
| BRD | Business Requirements Document |
| HCP | HikCentral Professional |
| PMS | Parking Management System |
| POS | Point of Sale |
| PWA | Progressive Web App |
| QR | Quick Response |
| RBAC | Role-Based Access Control |
| UAT | User Acceptance Testing |

## 36. Appendix C: Diagram Index

| Diagram | File |
| --- | --- |
| APT-SD-D01 Assisted Payment Terminal Logical Architecture | [APT-SD-D01_Assisted_Payment_Terminal_Logical_Architecture.jpg](system-design/diagrams/APT-SD-D01_Assisted_Payment_Terminal_Logical_Architecture.jpg) / [PUML](system-design/diagrams/APT-SD-D01_Assisted_Payment_Terminal_Logical_Architecture.puml) |
| APT-SD-D02 Terminal Mode Model: Cashier-Assisted vs Continuity Terminal | [APT-SD-D02_Terminal_Mode_Model_Cashier_Assisted_vs_Continuity_Terminal.jpg](system-design/diagrams/APT-SD-D02_Terminal_Mode_Model_Cashier_Assisted_vs_Continuity_Terminal.jpg) / [PUML](system-design/diagrams/APT-SD-D02_Terminal_Mode_Model_Cashier_Assisted_vs_Continuity_Terminal.puml) |
| APT-SD-D03 Terminal Trust Boundary and Device Identity Model | [APT-SD-D03_Terminal_Trust_Boundary_and_Device_Identity_Model.jpg](system-design/diagrams/APT-SD-D03_Terminal_Trust_Boundary_and_Device_Identity_Model.jpg) / [PUML](system-design/diagrams/APT-SD-D03_Terminal_Trust_Boundary_and_Device_Identity_Model.puml) |
| APT-SD-D04 Cashier Authentication, Shift, Site/Site Group Binding Flow | [APT-SD-D04_Cashier_Authentication_Shift_Site_Site_Group_Binding_Flow.jpg](system-design/diagrams/APT-SD-D04_Cashier_Authentication_Shift_Site_Site_Group_Binding_Flow.jpg) / [PUML](system-design/diagrams/APT-SD-D04_Cashier_Authentication_Shift_Site_Site_Group_Binding_Flow.puml) |
| APT-SD-D05 Normal Cashier-Assisted Payment Sequence | [APT-SD-D05_Normal_Cashier_Assisted_Payment_Sequence.jpg](system-design/diagrams/APT-SD-D05_Normal_Cashier_Assisted_Payment_Sequence.jpg) / [PUML](system-design/diagrams/APT-SD-D05_Normal_Cashier_Assisted_Payment_Sequence.puml) |
| APT-SD-D06 Statutory Discount Capture and Payable-Basis Refresh Sequence | [APT-SD-D06_Statutory_Discount_Capture_and_Payable_Basis_Refresh_Sequence.jpg](system-design/diagrams/APT-SD-D06_Statutory_Discount_Capture_and_Payable_Basis_Refresh_Sequence.jpg) / [PUML](system-design/diagrams/APT-SD-D06_Statutory_Discount_Capture_and_Payable_Basis_Refresh_Sequence.puml) |
| APT-SD-D07 Payment Finality, Fiscal Issuance, and ExitAuthorization Status Display Sequence | [APT-SD-D07_Payment_Finality_Fiscal_Issuance_and_ExitAuthorization_Status_Display_Sequence.jpg](system-design/diagrams/APT-SD-D07_Payment_Finality_Fiscal_Issuance_and_ExitAuthorization_Status_Display_Sequence.jpg) / [PUML](system-design/diagrams/APT-SD-D07_Payment_Finality_Fiscal_Issuance_and_ExitAuthorization_Status_Display_Sequence.puml) |
| APT-SD-D08 Fiscal Issuance Failure / Pending Exit Handling Flow | [APT-SD-D08_Fiscal_Issuance_Failure_Pending_Exit_Handling_Flow.jpg](system-design/diagrams/APT-SD-D08_Fiscal_Issuance_Failure_Pending_Exit_Handling_Flow.jpg) / [PUML](system-design/diagrams/APT-SD-D08_Fiscal_Issuance_Failure_Pending_Exit_Handling_Flow.puml) |
| APT-SD-D09 Continuity Terminal Activation and Restricted Operation Flow | [APT-SD-D09_Continuity_Terminal_Activation_and_Restricted_Operation_Flow.jpg](system-design/diagrams/APT-SD-D09_Continuity_Terminal_Activation_and_Restricted_Operation_Flow.jpg) / [PUML](system-design/diagrams/APT-SD-D09_Continuity_Terminal_Activation_and_Restricted_Operation_Flow.puml) |
| APT-SD-D10 Manual Release Governance Handoff Flow | [APT-SD-D10_Manual_Release_Governance_Handoff_Flow.jpg](system-design/diagrams/APT-SD-D10_Manual_Release_Governance_Handoff_Flow.jpg) / [PUML](system-design/diagrams/APT-SD-D10_Manual_Release_Governance_Handoff_Flow.puml) |
| APT-SD-D11 Android-First Hardened Terminal Posture | [APT-SD-D11_Android_First_Hardened_Terminal_Posture.jpg](system-design/diagrams/APT-SD-D11_Android_First_Hardened_Terminal_Posture.jpg) / [PUML](system-design/diagrams/APT-SD-D11_Android_First_Hardened_Terminal_Posture.puml) |
| APT-SD-D12 Terminal Observability and Audit Event Flow | [APT-SD-D12_Terminal_Observability_and_Audit_Event_Flow.jpg](system-design/diagrams/APT-SD-D12_Terminal_Observability_and_Audit_Event_Flow.jpg) / [PUML](system-design/diagrams/APT-SD-D12_Terminal_Observability_and_Audit_Event_Flow.puml) |

