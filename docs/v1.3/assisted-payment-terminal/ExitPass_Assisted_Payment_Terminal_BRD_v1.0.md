# ExitPass Assisted Payment Terminal BRD v1.0

Version: v1.0  
Status: Draft for review  
Generated: 2026-07-01  
Document type: Companion Business Requirements Document  
Product scope: ExitPass Assisted Payment Terminal app family

## 1. Document Control

### 1.1 Version History

| Version | Date | Author / owner | Summary |
| --- | --- | --- | --- |
| v1.0 | 2026-07-01 | ExitPass documentation stream | Initial companion BRD for the Assisted Payment Terminal app family covering Cashier-Assisted Terminal mode, Continuity Terminal mode, statutory discount validation capture, payment-capable terminal workflows, Site POS Server fiscal routing, hardened terminal application posture, and authority boundaries. |

### 1.2 Approvals

| Role | Name | Approval status | Date |
| --- | --- | --- | --- |
| Product owner | TBD | Pending review | TBD |
| Parking operations owner | TBD | Pending review | TBD |
| Finance / revenue assurance owner | TBD | Pending review | TBD |
| Technical architecture owner | TBD | Pending review | TBD |
| Compliance / audit owner | TBD | Pending review | TBD |

### 1.3 Document Ownership

This BRD is owned by the ExitPass product and documentation stream. It defines business requirements for the Assisted Payment Terminal companion module in the ExitPass v1.3 documentation set.

This document is not a System Design, API Contract, Database Design, Engineering Pack, Operator Console BRD, Continuity BRD, or POS/Invoicing BRD.

### 1.4 Relationship to ExitPass BRD v1.3

ExitPass BRD v1.3 is the core authority and business baseline. This companion BRD expands only the Assisted Payment Terminal business scope.

The Assisted Payment Terminal BRD shall preserve the ExitPass BRD v1.3 authority model:

- Vendor PMS / HCP remains authority for parking session lifecycle and tariff computation in normal mode.
- Central PMS remains authority for session projection, platform control state, payment finality, TariffSnapshot, fiscal issuance reference recording, and ExitAuthorization.
- Central PMS / Discount workflow remains authority for statutory discount policy resolution and validation persistence.
- POS Server remains fiscal issuance authority for the resolved Site.
- Gate/exit execution consumes Central PMS ExitAuthorization.

## 2. Executive Summary

The Assisted Payment Terminal is a payment-capable terminal app family for staffed and degraded parking operations. It supports cashier-assisted payment during normal staffed operations and restricted continuity workflows during approved degraded/BCP events.

The module is separate from Operator Console. Operator Console remains the internal non-payment governance and operations console. Assisted Payment Terminal is the cashier/continuity terminal workflow surface for session lookup, payable-basis display, statutory discount validation capture where allowed, payment collection, fiscal routing through the resolved Site POS Server, customer messaging, and terminal accountability.

The Assisted Payment Terminal shall not declare payment finality, issue ExitAuthorization, become an independent statutory discount policy engine, or become an independent fiscal authority outside the Site POS Server model.

## 3. Business Context

Some parking operations continue to require staffed payment lanes, lessor-operated cashier points, controlled assisted payment, or fallback payment operations during degradation. ExitPass v1.3 recognizes this operational need without weakening the platform authority model.

The Assisted Payment Terminal fills the business gap between customer self-service channels and internal governance tooling. It gives cashiers a controlled terminal workflow while relying on Central PMS, Vendor PMS/HCP, Payment Orchestrator, POS Server, and Operator Console for their respective authority domains.

## 4. Problem Statement

Without a dedicated Assisted Payment Terminal module, cashier-assisted and continuity workflows risk being handled through tools that are not designed for payment collection, device accountability, fiscal routing, or degraded-mode controls.

The business risks include:

- Operator Console being incorrectly expanded into payment collection.
- Cashier terminals becoming local policy engines for discounts.
- Fiscal issuance being treated as terminal-local rather than Site POS Server-owned.
- Payment success being confused with Central PMS payment finality.
- Continuity operation becoming an uncontrolled replacement for normal Vendor PMS/Central PMS authority.
- Weak audit linkage between cashier, device, shift, session, payment, fiscal issuance, and exit authorization.

## 5. Product Purpose

The Assisted Payment Terminal shall provide a controlled terminal workflow for:

- Cashier login and authenticated operator context.
- Assigned Site / Site Group context.
- Terminal/device identity and channel accountability.
- Shift/session accountability.
- Ticket/card number scan or manual entry.
- Parking session lookup.
- Payable-basis display.
- Cashier-facing statutory discount validation capture.
- Payment collection flow.
- POS Server fiscal issuance routing.
- Customer instruction after payment.
- Central PMS ExitAuthorization status display.
- Continuity operations under approved degraded-mode controls.

## 6. Product Boundary

The Assisted Payment Terminal is a separate terminal app family with two operating modes:

| Mode | Purpose | Default posture |
| --- | --- | --- |
| Cashier-Assisted Terminal | Normal operating mode for cashier-assisted parking operations. | Enabled only for authorized terminals, cashiers, Sites, and shifts. |
| Continuity Terminal | Restricted degraded/BCP operating mode. | Disabled by default and activated only under approved degraded-mode controls. |

The Assisted Payment Terminal may share backend services, identity, audit, evidence, and design-system components with Operator Console or other ExitPass modules, but it serves a different operating context and shall preserve separate permission boundaries.

## 7. Explicit Non-Authority Scope

The Assisted Payment Terminal shall not:

- Replace Central PMS.
- Replace Operator Console.
- Operate as a separate POS system per terminal.
- Act as an independent fiscal authority.
- Declare payment finality.
- Issue ExitAuthorization.
- Independently approve statutory entitlement.
- Bypass Central PMS / Discount workflow.
- Mutate payable basis directly.
- Use terminal-local policy logic as authority.
- Weaken evidence, privacy, RBAC, or audit requirements.

## 8. Stakeholders and Users

| Stakeholder / user | Interest |
| --- | --- |
| Cashier | Uses the terminal to resolve sessions, capture eligible discount workflow inputs, collect payment, and instruct the customer. |
| Parker | Receives assisted payment support, statutory discount handling where applicable, fiscal document status, and exit instructions. |
| Supervisor | Approves escalations, reviews continuity activation, and handles exceptions through Operator Console or approved operations workflow. |
| Parking operations manager | Requires cashier/device/shift accountability and continuity control. |
| Finance / revenue assurance | Requires payment, fiscal, cashier, and reconciliation traceability. |
| Compliance / audit | Requires evidence handling, statutory discount auditability, fiscal routing, and degraded-mode records. |
| Technical support | Monitors device trust, terminal health, integration availability, and exception conditions. |

## 9. Operating Modes

### 9.1 Cashier-Assisted Terminal Mode

Cashier-Assisted Terminal mode is the normal operating mode for cashier-assisted parking operations. It is used where a Site still has human cashiers or a lessor/operator requires assisted payment instead of fully cashierless operation.

The Cashier-Assisted Terminal shall support:

- Cashier login / authenticated operator context.
- Device/terminal identity.
- Assigned Site / Site Group context.
- Shift/session accountability at the business level.
- Ticket/card number scan or manual entry.
- Parking session lookup.
- Payable-basis display.
- Statutory discount validation capture.
- Payment collection flow.
- POS Server fiscal issuance routing.
- Customer instruction after payment.
- Central PMS ExitAuthorization status display.
- Audit and reconciliation metadata.

### 9.2 Continuity Terminal Mode

Continuity Terminal mode is the restricted degraded/BCP operating mode. It is used only when approved degraded-mode conditions exist, such as Vendor PMS outage, network degradation, WebPay/APM degradation, or other approved continuity events.

Continuity Terminal mode shall be:

- Disabled by default.
- Activated only under approved BCP/degraded-mode controls.
- Supervisor-approved where policy requires.
- Incident-tagged.
- Audit-tagged.
- Reconciliation-tagged.
- Subject to post-restoration review.

The Continuity Terminal may support:

- Ticket/card number lookup using available projection or approved continuity source.
- Degraded payable-basis calculation only under approved policy.
- Restricted statutory discount handling only under approved degraded-mode policy.
- Payment collection where allowed.
- POS Server fiscal issuance where available and allowed.
- Controlled manual/assisted exit handling where approved.

Continuity Terminal mode must not silently replace normal Vendor PMS/Central PMS authority.

## 10. Application Platform Position

The Assisted Payment Terminal shall be designed as a hardened terminal application. The preferred implementation posture is Android-first for field terminal deployments, with a web-based workflow core and native device integration where required.

Final implementation details, including Android shell, WebView/PWA core, native app model, hybrid model, kiosk mode, scanner/camera/printer integration, cash drawer integration, certificate/key storage, device health, and offline/degraded safeguards, shall be defined in the Assisted Payment Terminal System Design.

Android-first is the preferred field terminal posture, not an exclusive business requirement. Non-Android deployments are not prohibited by this BRD. A fixed cashier station variant, such as browser/PWA or desktop-compatible deployment, may be evaluated later if approved by System Design, device trust, POS Server, and security requirements.

This BRD does not define Android package structure, WebView framework, Java/Kotlin implementation, native bridge APIs, local storage details, endpoint paths, DTOs, database objects, printer command formats, or device SDKs.

## 11. Relationship to Operator Console

Operator Console is the internal non-payment governance and operations console. It supports review, supervision, evidence review, reporting, configuration, device/shift controls, and compliance workflows.

Operator Console must not collect payments, declare payment finality, or issue ExitAuthorization.

Assisted Payment Terminal is the separate payment-capable terminal app family. It supports cashier/continuity payment workflows, payable-basis display, discount capture, payment collection, POS Server fiscal routing, and terminal accountability.

Supervisor/compliance review belongs to Operator Console or an approved operations workflow. Cashier-facing capture belongs to Assisted Payment Terminal.

## 12. Relationship to POS/Invoicing and Site POS Server

The Assisted Payment Terminal is a channel/terminal under the Site POS Server fiscal model. It is not a separate POS system per terminal.

The resolved Site determines POS Server routing. POS Server issues the Sales Invoice. Fiscal issuance must succeed before Central PMS issues ExitAuthorization.

If fiscal issuance fails or times out, the terminal shall not imply that exit is authorized. If payment succeeds but fiscal issuance or exit authorization is pending, the terminal shall show a clear pending/exception message.

## 13. Relationship to Central PMS, Payment Orchestrator, Vendor PMS, and Gate/Exit

| Function | Owner |
| --- | --- |
| Terminal UI and cashier workflow | Assisted Payment Terminal |
| Terminal/device identity and channel context | Assisted Payment Terminal with Identity / platform controls |
| Parking session authority in normal mode | Vendor PMS / HCP |
| Session projection and control state | Central PMS |
| Discount policy resolution | Central PMS / Discount workflow |
| Statutory validation record | Central PMS / Discount workflow |
| Payable-basis recalculation / TariffSnapshot | Central PMS with Vendor PMS or approved degraded-mode tariff basis |
| Payment provider interaction | Payment Orchestrator or approved payment channel integration |
| Payment finality | Central PMS |
| Fiscal treatment and Sales Invoice issuance | Resolved Site POS Server |
| Fiscal issuance reference recording | Central PMS |
| ExitAuthorization | Central PMS |
| Gate/exit execution | Gate/exit system consuming Central PMS authorization |
| Supervisor/compliance review | Operator Console / approved operations workflow |

## 14. High-Level Business Process Overview

### 14.1 Cashier-Assisted Payment Flow

1. The cashier logs into an approved terminal.
2. The terminal establishes cashier identity, device identity, assigned Site/Site Group context, and shift/session context.
3. The cashier scans or manually enters the ticket/card number.
4. The terminal requests session lookup through the approved backend flow.
5. Central PMS resolves the session and payable basis using Vendor PMS/HCP in normal mode.
6. The terminal displays the payable basis.
7. If statutory discount applies, the cashier initiates validation capture and submits it to Central PMS / Discount workflow.
8. Central PMS / Discount workflow returns validation result and payable-basis effect.
9. The terminal displays the updated payable basis only after approved backend recalculation/refresh.
10. The cashier collects payment through the approved payment flow.
11. Central PMS records payment finality after verified outcome.
12. Central PMS requests fiscal issuance from the resolved Site POS Server.
13. POS Server issues the Sales Invoice and returns fiscal identity/status.
14. Central PMS records fiscal issuance reference.
15. Central PMS issues ExitAuthorization if eligible.
16. The terminal displays customer instruction, fiscal status, and exit status.

### 14.2 Continuity Terminal Flow

1. Approved degraded/BCP condition is recognized.
2. Continuity Terminal mode is activated under policy.
3. Activation context includes supervisor approval where required, incident reference, audit tag, and reconciliation tag.
4. The cashier scans or manually enters ticket/card number.
5. The terminal requests lookup using available projection or approved continuity source.
6. Central PMS determines whether the degraded basis is fresh, unambiguous, and allowed.
7. If safe, approved degraded payable basis is displayed.
8. If unsafe, the terminal fails closed or routes to supervisor/manual review.
9. Payment, fiscal issuance, and exit handling proceed only where approved by degraded-mode policy.
10. All activity is included in post-restoration review where applicable.

## 15. Functional Requirements

| ID | Requirement |
| --- | --- |
| APT-FR-001 | The Assisted Payment Terminal shall require terminal authentication before use. |
| APT-FR-002 | The Assisted Payment Terminal shall establish cashier/operator identity. |
| APT-FR-003 | The Assisted Payment Terminal shall establish terminal/device identity. |
| APT-FR-004 | The Assisted Payment Terminal shall bind operation to assigned Site and Site Group context. |
| APT-FR-005 | The Assisted Payment Terminal shall support shift/session accountability. |
| APT-FR-006 | The Assisted Payment Terminal shall follow a hardened terminal application posture. |
| APT-FR-007 | The Assisted Payment Terminal shall capture Android-first as the preferred field terminal posture while deferring final implementation architecture to System Design. |
| APT-FR-008 | The Assisted Payment Terminal may use a web-based workflow core where appropriate. |
| APT-FR-009 | The Assisted Payment Terminal may require native device integration for scanner, camera, printer, cash drawer, device identity, kiosk mode, certificate/key storage, and local device health. |
| APT-FR-010 | The Assisted Payment Terminal shall support ticket/card number scan or manual entry where allowed. |
| APT-FR-011 | The Assisted Payment Terminal shall request parking session lookup through the approved backend flow. |
| APT-FR-012 | The Assisted Payment Terminal shall display normal payable basis from approved Vendor PMS/Central PMS flow. |
| APT-FR-013 | The Assisted Payment Terminal shall display degraded payable basis only under approved degraded-mode controls. |
| APT-FR-014 | The Assisted Payment Terminal shall support statutory discount validation capture in Cashier-Assisted Terminal mode. |
| APT-FR-015 | The Assisted Payment Terminal shall restrict statutory discount handling in Continuity Terminal mode. |
| APT-FR-016 | The Assisted Payment Terminal shall support payment collection flow. |
| APT-FR-017 | The Assisted Payment Terminal shall display payment result or pending status from the approved backend flow. |
| APT-FR-018 | The Assisted Payment Terminal shall not own or declare payment finality. |
| APT-FR-019 | The Assisted Payment Terminal shall route fiscal issuance through the resolved Site POS Server via the approved backend flow. |
| APT-FR-020 | The Assisted Payment Terminal shall display Sales Invoice result or pending exception status where returned by the approved backend flow. |
| APT-FR-021 | The Assisted Payment Terminal shall display Central PMS ExitAuthorization result or pending exception status. |
| APT-FR-022 | The Assisted Payment Terminal shall show vendor acknowledgment visibility where relevant and available. |
| APT-FR-023 | The Assisted Payment Terminal shall support supervisor escalation. |
| APT-FR-024 | The Assisted Payment Terminal shall display manual release messaging where allowed by policy. |
| APT-FR-025 | The Assisted Payment Terminal shall preserve audit logging. |
| APT-FR-026 | The Assisted Payment Terminal shall support evidence handling where required by policy. |
| APT-FR-027 | The Assisted Payment Terminal shall display privacy notice where required. |
| APT-FR-028 | The Assisted Payment Terminal shall restrict offline/degraded behavior to approved policy. |
| APT-FR-029 | The Assisted Payment Terminal may display or request fiscal reference reprint where allowed by POS Server policy. |
| APT-FR-030 | The Assisted Payment Terminal shall preserve channel/terminal accountability. |
| APT-FR-031 | The Assisted Payment Terminal shall provide clear customer messaging. |

## 16. Cashier-Assisted Terminal Requirements

| ID | Requirement |
| --- | --- |
| CAT-001 | The Cashier-Assisted Terminal shall operate as the normal staffed payment mode. |
| CAT-002 | The cashier shall authenticate before starting payment activity. |
| CAT-003 | The terminal shall show assigned Site/Site Group context to reduce wrong-site processing. |
| CAT-004 | The cashier shall scan or manually enter ticket/card number. |
| CAT-005 | The terminal shall retrieve the parking session through the approved backend flow. |
| CAT-006 | The terminal shall display payable basis before payment. |
| CAT-007 | The terminal shall support cashier-facing statutory discount validation capture after valid session resolution. |
| CAT-008 | The terminal shall collect payment only after payable basis is established. |
| CAT-009 | The terminal shall route fiscal issuance through the resolved Site POS Server. |
| CAT-010 | The terminal shall display clear customer instruction after payment, fiscal issuance, and exit authorization status are known. |

## 17. Statutory Discount Validation Requirements

The Cashier-Assisted Terminal shall support statutory discount validation as part of the assisted payment workflow.

The terminal may:

- Initiate Senior Citizen/PWD statutory discount validation after resolving a valid parking session.
- Capture required structured entitlement details.
- Capture supporting evidence where required by policy.
- Capture cashier attestation.
- Submit validation request to Central PMS / Discount workflow.
- Show approved, rejected, failed, expired, or pending review result.
- Request or display updated payable basis after approved validation.
- Prevent payment from proceeding with an unapproved discount.
- Route approved fiscal treatment to the resolved Site POS Server through the approved backend flow.

The terminal must not:

- Independently approve statutory entitlement.
- Bypass Central PMS / Discount workflow.
- Mutate payable basis directly.
- Use terminal-local policy logic as authority.
- Create payment finality.
- Issue ExitAuthorization.
- Weaken evidence, privacy, RBAC, or audit requirements.

For Continuity Terminal mode, statutory discount handling is restricted. If entitlement, policy basis, evidence requirements, projection freshness, or payable-basis recalculation cannot be safely validated, the terminal shall fail closed or route to supervisor/manual review.

## 18. Payment Collection and Payment Finality Requirements

| ID | Requirement |
| --- | --- |
| PAY-001 | The Assisted Payment Terminal shall support payment collection through approved payment flow. |
| PAY-002 | The Assisted Payment Terminal shall display payment initiation, pending, failed, cancelled, or completed status as returned by the approved backend flow. |
| PAY-003 | Central PMS shall remain payment finality authority. |
| PAY-004 | The Assisted Payment Terminal shall not declare platform payment finality. |
| PAY-005 | If provider outcome is unknown, the terminal shall not imply payment finality. |
| PAY-006 | If payment succeeds but fiscal issuance or ExitAuthorization is pending, the terminal shall show a clear pending/exception message. |

## 19. Fiscal Issuance and Sales Invoice Routing Requirements

| ID | Requirement |
| --- | --- |
| FIS-001 | The resolved Site shall determine POS Server routing. |
| FIS-002 | POS Server shall issue the Sales Invoice. |
| FIS-003 | The Assisted Payment Terminal shall not act as fiscal authority. |
| FIS-004 | Fiscal issuance must succeed before Central PMS issues ExitAuthorization. |
| FIS-005 | If fiscal issuance fails, the terminal shall not show exit authorized. |
| FIS-006 | The terminal shall display fiscal issuance reference, Sales Invoice status, or pending exception status where allowed by POS Server and Central PMS policy. |
| FIS-007 | Exact fiscal reprint/display behavior from the terminal remains open for POS Server policy and technical design. |

## 20. Continuity Terminal Requirements

| ID | Requirement |
| --- | --- |
| CON-001 | Continuity Terminal mode shall be disabled by default. |
| CON-002 | Continuity Terminal mode shall activate only under approved BCP/degraded-mode controls. |
| CON-003 | Continuity Terminal activation shall be supervisor-approved where policy requires. |
| CON-004 | Continuity Terminal activity shall be incident-tagged. |
| CON-005 | Continuity Terminal activity shall be audit-tagged. |
| CON-006 | Continuity Terminal activity shall be reconciliation-tagged. |
| CON-007 | Continuity Terminal activity shall be subject to post-restoration review where applicable. |
| CON-008 | Continuity Terminal may use available projection or approved continuity source for lookup only under approved policy. |
| CON-009 | Continuity Terminal may support degraded payable-basis calculation only under approved policy. |
| CON-010 | Continuity Terminal shall restrict statutory discount handling to approved degraded-mode policy. |
| CON-011 | Continuity Terminal shall fail closed or route to supervisor/manual review where validation or payable basis is unsafe. |
| CON-012 | Continuity Terminal shall not silently replace normal Vendor PMS/Central PMS authority. |

## 21. Exception and Failure Handling

| Scenario | Required behavior |
| --- | --- |
| Invalid cashier login | Deny terminal workflow and log the failed attempt where policy requires. |
| Untrusted terminal/device | Deny payment workflow or route to support according to policy. |
| Wrong Site/Site Group context | Block processing or require authorized correction. |
| Session not found | Show clear customer/cashier message and provide approved escalation path. |
| Ambiguous session | Fail closed or route to supervisor/manual review. |
| Vendor PMS unavailable | Use only approved degraded flow; otherwise fail closed. |
| Projection stale | Block degraded payable basis or route to supervisor/manual review. |
| Discount validation rejected | Do not apply discount as payable basis. |
| Discount validation pending review | Do not proceed with discounted payable basis until approved. |
| Payment outcome unknown | Do not declare payment finality; show pending/exception message. |
| Fiscal issuance failed or timed out | Do not show exit authorized; start controlled exception/retry or escalation. |
| ExitAuthorization pending | Show pending/exception message; do not instruct exit as authorized. |

## 22. Security, RBAC, Device Trust, and Shift Accountability

The Assisted Payment Terminal shall enforce:

- Cashier authentication.
- Supervisor authorization where required.
- Terminal/device identity.
- Assigned Site/Site Group context.
- Shift/session accountability.
- Role-based access to payment, discount capture, continuity, reprint/display, and escalation functions.
- Device trust checks appropriate to hardened terminal deployment.
- Separate permission boundaries from Operator Console.

Exact permission matrix, terminal certificate/key storage, device enrollment, and local trust controls are deferred to Assisted Payment Terminal System Design and security design.

## 23. Audit, Evidence, and Reporting

The Assisted Payment Terminal shall preserve audit metadata for:

- Cashier login/logout.
- Terminal/device identity.
- Site/Site Group context.
- Shift/session context.
- Ticket/card lookup.
- Payable-basis display.
- Statutory discount capture and cashier attestation.
- Evidence capture references where required.
- Validation request and result.
- Payment initiation and result display.
- Fiscal issuance request/status display.
- ExitAuthorization status display.
- Continuity activation and use.
- Supervisor escalation.
- Manual release messaging where allowed.

Reporting and compliance review may be performed through Operator Console, Management Dashboard and Reporting, POS/Invoicing reports, or approved operations workflows. This BRD does not define reporting implementation.

## 24. Data Privacy Requirements

The Assisted Payment Terminal shall collect only the statutory discount, identity, evidence, and payment-related information required by approved policy and backend workflow.

The terminal shall display privacy notices where required. Evidence capture, evidence references, retention, access, and disposal shall follow Central PMS / Discount workflow, Operator Console, compliance, and privacy policies.

The terminal shall not retain unmanaged entitlement evidence outside approved workflows.

## 25. Non-Functional Requirements

| Area | Requirement |
| --- | --- |
| Availability | The terminal workflow should be available during approved operating hours subject to backend, network, POS Server, and payment provider availability. |
| Usability | Cashier workflows shall be clear enough for repeated operational use under queue pressure. |
| Performance | Lookup, payable-basis display, validation result display, payment status, fiscal status, and exit status should return within operationally acceptable targets defined later. |
| Reliability | Unknown payment, fiscal, discount, or exit states shall be handled conservatively. |
| Security | Hardened terminal posture, device identity, RBAC, and audit controls shall be enforced. |
| Auditability | The terminal shall support end-to-end cashier/device/shift/session/payment/fiscal/exit traceability. |
| Privacy | Evidence and entitlement data shall be minimized and protected. |
| Degraded behavior | Offline and degraded workflows shall be restricted to approved continuity policy. |

## 26. Assumptions

| ID | Assumption |
| --- | --- |
| APT-A-001 | Central PMS and approved backend workflows are available for normal cashier-assisted operation. |
| APT-A-002 | Vendor PMS/HCP remains session and tariff authority in normal mode. |
| APT-A-003 | POS Server is available for fiscal issuance in normal payment-to-exit flow. |
| APT-A-004 | Operator Console or approved operations workflow exists for supervisor/compliance review. |
| APT-A-005 | Field terminal deployments require hardened device posture. |

## 27. Constraints

| ID | Constraint |
| --- | --- |
| APT-C-001 | Assisted Payment Terminal shall not declare payment finality. |
| APT-C-002 | Assisted Payment Terminal shall not issue ExitAuthorization. |
| APT-C-003 | Assisted Payment Terminal shall not independently approve statutory entitlement. |
| APT-C-004 | Assisted Payment Terminal shall not become a separate POS system per terminal. |
| APT-C-005 | Assisted Payment Terminal shall route fiscal issuance through resolved Site POS Server. |
| APT-C-006 | Continuity Terminal mode shall remain disabled by default. |
| APT-C-007 | Final UI implementation stack is deferred to Assisted Payment Terminal System Design. |

## 28. Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Terminal treated as policy authority | Incorrect discounts and audit exposure. | State Central PMS / Discount workflow authority and block unapproved discounts. |
| Operator Console and terminal boundary blurred | Non-payment console may be used for payment workflows. | Preserve separate modules and permission boundaries. |
| Fiscal authority leakage | Terminal may be treated as independent POS. | Route fiscal issuance through resolved Site POS Server. |
| Payment finality confusion | Cashier may think provider response is finality. | Display backend status and preserve Central PMS finality authority. |
| Continuity overuse | Degraded mode may bypass normal controls. | Keep disabled by default and require incident/audit/reconciliation controls. |
| Weak evidence handling | Statutory discount data may be over-collected or mishandled. | Enforce approved backend workflow, privacy notices, and evidence references. |
| Device trust weakness | Field terminal may be exposed to tampering. | Use hardened terminal posture and define trust controls in System Design. |

## 29. Open Questions

These are open design or implementation questions. They do not reopen approved business decisions.

| ID | Open question |
| --- | --- |
| APT-OQ-001 | What are the final implementation architecture details, including Android shell composition, WebView/PWA core approach, native bridge scope, browser/PWA or desktop-compatible variant eligibility, and hybrid deployment rules? |
| APT-OQ-002 | What are the final terminal hardware integration requirements? |
| APT-OQ-003 | What camera, scanner, printer, and cash drawer integrations are required by terminal type? |
| APT-OQ-004 | What kiosk lockdown requirements apply to field-deployed terminals? |
| APT-OQ-005 | What is the terminal certificate/key storage model? |
| APT-OQ-006 | What offline evidence capture behavior, if any, is allowed? |
| APT-OQ-007 | What are the Continuity Terminal activation authority details? |
| APT-OQ-008 | What is the exact degraded payable-basis freshness threshold? |
| APT-OQ-009 | What is the exact permission matrix between cashier, supervisor, support, and admin roles? |
| APT-OQ-010 | Is cash payment supported in Cashier-Assisted Terminal v1.0? |
| APT-OQ-011 | Are card/eWallet/QR payments hosted checkout only or terminal-integrated? |
| APT-OQ-012 | What fiscal reprint/display behavior is allowed from the terminal? |
| APT-OQ-013 | What handoff to POS Server for X-read/Z-read or cashier shift reports, if any, is required? |
| APT-OQ-014 | What is the exact relationship to Operator Console for supervisor escalation? |
| APT-OQ-015 | Is a fixed cashier station browser/PWA or desktop-compatible variant allowed in v1.0? |
| APT-OQ-016 | What exact endpoint paths and DTOs are needed? Deferred to API Contract. |
| APT-OQ-017 | What exact database changes are needed? Deferred to Database Delta. |

Android-first for field terminal deployment is not open at the business level. The preferred posture is decided; exact implementation architecture remains open for System Design.

## 30. Acceptance Criteria

| ID | Acceptance criterion |
| --- | --- |
| APT-AC-001 | Cashier can log into a valid terminal with assigned Site context. |
| APT-AC-002 | Cashier can scan or manually enter a valid ticket/card number. |
| APT-AC-003 | Terminal resolves a parking session through the approved backend flow. |
| APT-AC-004 | Terminal displays payable basis from approved Vendor PMS or approved degraded source. |
| APT-AC-005 | Cashier can initiate statutory discount validation after resolving a valid session. |
| APT-AC-006 | Terminal captures required entitlement details, evidence, and cashier attestation. |
| APT-AC-007 | Validation request is processed by Central PMS / Discount workflow. |
| APT-AC-008 | Terminal does not apply an unapproved discount. |
| APT-AC-009 | Approved discount updates payable basis through backend workflow before payment. |
| APT-AC-010 | Terminal supports payment collection but does not declare payment finality. |
| APT-AC-011 | Central PMS remains payment finality authority. |
| APT-AC-012 | Fiscal issuance is routed to the resolved Site POS Server. |
| APT-AC-013 | POS Server issues the Sales Invoice. |
| APT-AC-014 | Central PMS records fiscal issuance reference. |
| APT-AC-015 | ExitAuthorization is issued only by Central PMS. |
| APT-AC-016 | If fiscal issuance fails, terminal does not show exit authorized. |
| APT-AC-017 | Continuity Terminal mode is disabled by default. |
| APT-AC-018 | Continuity Terminal activation requires approved degraded-mode controls. |
| APT-AC-019 | Continuity-mode discount handling is restricted and audit/reconciliation tagged. |
| APT-AC-020 | Operator Console remains separate and non-payment. |
| APT-AC-021 | Assisted Payment Terminal is described as a hardened terminal application. |
| APT-AC-022 | Android-first is captured as the preferred field terminal posture, with final implementation details deferred to System Design. |

## 31. Requirements Traceability Matrix

| Requirement area | Source / authority | BRD sections |
| --- | --- | --- |
| Module separation | ExitPass BRD v1.3; v1.3 decision log | Sections 6, 7, 11, 30 |
| Cashier-assisted mode | ExitPass BRD v1.3; v1.3 outline | Sections 9, 14, 16 |
| Continuity mode | ExitPass BRD v1.3; open questions | Sections 9, 14, 20, 29 |
| Statutory discount capture | ExitPass BRD v1.3; v1.3 decision log | Sections 17, 22, 23, 24 |
| Central PMS / Discount authority | ExitPass BRD v1.3; impact map | Sections 7, 13, 17 |
| Payment finality | ExitPass BRD v1.3 authority model | Sections 13, 18, 30 |
| Site POS Server fiscal routing | POS/Invoicing planning; ExitPass BRD v1.3 | Sections 12, 19 |
| Hardened terminal posture | User-approved product direction | Sections 10, 15, 25, 29 |
| Android-first preferred posture | User-approved product direction | Sections 10, 15, 29, 30 |
| Open technical details | User task scope; locked writing order | Sections 10, 29 |

## 32. Appendix A: Glossary

| Term | Definition |
| --- | --- |
| Assisted Payment Terminal | Separate payment-capable terminal app family for cashier-assisted and continuity payment workflows. |
| Cashier-Assisted Terminal | Normal Assisted Payment Terminal mode for staffed parking payment operations. |
| Continuity Terminal | Restricted Assisted Payment Terminal mode for approved degraded/BCP operation. |
| Central PMS | Platform authority for payment-linked control state, payment finality, fiscal reference recording, and ExitAuthorization. |
| Discount workflow | Approved Central PMS-backed workflow for statutory discount policy resolution and validation persistence. |
| Payable basis | Approved amount basis used before payment after tariff, entitlement, and policy effects are resolved. |
| Site POS Server | Fiscal issuance authority for the resolved Site. |
| Operator Console | Separate non-payment governance and operations console. |

## 33. Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| APM | AutoPay Machine |
| APT | Assisted Payment Terminal |
| BCP | Business Continuity Plan |
| BIR | Bureau of Internal Revenue |
| BRD | Business Requirements Document |
| DTO | Data Transfer Object |
| HCP | HikCentral Professional |
| PMS | Parking Management System |
| POS | Point of Sale |
| PWA | Progressive Web App |
| RBAC | Role-Based Access Control |

## 34. Appendix C: Diagrams

| ID | Diagram | PlantUML source |
| --- | --- | --- |
| D-01 | [Assisted Payment Terminal Context Diagram](diagrams/D-01_Assisted_Payment_Terminal_Context_Diagram.jpg) | [D-01_Assisted_Payment_Terminal_Context_Diagram.puml](diagrams/D-01_Assisted_Payment_Terminal_Context_Diagram.puml) |
| D-02 | [Assisted Payment Terminal Operating Modes](diagrams/D-02_Assisted_Payment_Terminal_Operating_Modes.jpg) | [D-02_Assisted_Payment_Terminal_Operating_Modes.puml](diagrams/D-02_Assisted_Payment_Terminal_Operating_Modes.puml) |
| D-03 | [Cashier-Assisted Payment with Statutory Discount Validation Flow](diagrams/D-03_Cashier_Assisted_Payment_with_Statutory_Discount_Validation_Flow.jpg) | [D-03_Cashier_Assisted_Payment_with_Statutory_Discount_Validation_Flow.puml](diagrams/D-03_Cashier_Assisted_Payment_with_Statutory_Discount_Validation_Flow.puml) |
| D-04 | [Payment, Fiscal Issuance, and ExitAuthorization Authority Flow](diagrams/D-04_Payment_Fiscal_Issuance_and_ExitAuthorization_Authority_Flow.jpg) | [D-04_Payment_Fiscal_Issuance_and_ExitAuthorization_Authority_Flow.puml](diagrams/D-04_Payment_Fiscal_Issuance_and_ExitAuthorization_Authority_Flow.puml) |
| D-05 | [Continuity Terminal Activation and Restricted Operation Flow](diagrams/D-05_Continuity_Terminal_Activation_and_Restricted_Operation_Flow.jpg) | [D-05_Continuity_Terminal_Activation_and_Restricted_Operation_Flow.puml](diagrams/D-05_Continuity_Terminal_Activation_and_Restricted_Operation_Flow.puml) |
| D-06 | [Android-first Hardened Terminal Posture Diagram](diagrams/D-06_Android_First_Hardened_Terminal_Posture_Diagram.jpg) | [D-06_Android_First_Hardened_Terminal_Posture_Diagram.puml](diagrams/D-06_Android_First_Hardened_Terminal_Posture_Diagram.puml) |
