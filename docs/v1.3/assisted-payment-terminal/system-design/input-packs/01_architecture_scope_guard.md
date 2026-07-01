# Assisted Payment Terminal Architecture and Scope Guard Input Pack

Status: Specialist input pack  
Target: Assisted Payment Terminal System Design v1.0  
Owner: Architecture and scope guard specialist  
Branch: `docs/v1.3-assisted-payment-terminal-system-design`

## 1. Purpose

This input pack provides architecture scope guardrails for the later Assisted Payment Terminal System Design. It is not the final system design.

The pack exists to prevent:

- Authority drift from Central PMS, Vendor PMS/HCP, POS Server, Payment Orchestrator, Operator Console, Continuity, and Management Dashboard boundaries.
- Terminology drift between Assisted Payment Terminal, Cashier-Assisted Terminal, Continuity Terminal, Operator Console, POS Server, Vendor PMS Connector, and HikCentral Connector.
- Premature API, database, Android, WebView/PWA, native bridge, SDK, runbook, UAT, or implementation detail.

The later Lead draft should use this pack as a guardrail while synthesizing the final Assisted Payment Terminal System Design from the approved v1.3 baseline.

## 2. Source Documents Reviewed

| Source | Relevance to this pack |
| --- | --- |
| `docs/v1.3/assisted-payment-terminal/system-design/ExitPass_Assisted_Payment_Terminal_System_Design_Orchestration_Plan.md` | Defines specialist ownership, target scope, required guardrails, operating modes, deferrals, and validation rules. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 authority model, APT positioning, statutory discount boundary, projection limits, POS routing, fiscal-before-exit choreography, and companion scope anchors. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System-level authority matrix, trust boundaries, workflow sequencing, assisted terminal boundary, continuity architecture, API/data deferrals, and non-negotiable invariants. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Confirms the BRD set as approved System Design input and preserves downstream open items. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Primary APT business source for app family, modes, workflow surface, non-authority scope, Android-first posture, and open questions. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Continuity Terminal activation, disabled-by-default posture, fail-closed behavior, projection freshness, manual release, audit, and reconciliation controls. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console separation, non-payment governance, supervisor review, evidence review, continuity governance, and manual release governance. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Dashboard/reporting visibility boundary, source/freshness labels, projection non-authority, and reporting-only constraints. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal authority, Sales Invoice routing, fiscal issuance before ExitAuthorization, channel/terminal model, and fiscal deferrals. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Vendor PMS Connector authority, normal live resolve, projection, vendor acknowledgment, health, and connector non-authority posture. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HCP-specific projection, fee calculation, ParkingLotIndexCode mapping, `cardNum` uncertainty, payment acknowledgment caution, and source-gap posture. |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved decisions for APT app family, statutory discount capture, POS/fiscal routing, Operator Console separation, connector identity, and authority boundaries. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Global v1.3 open questions to preserve, including degraded freshness, continuity activation authority, connector topology, and acknowledgment behavior. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Impact mapping for APT model, APT discount capture, Operator Console separation, continuity restrictions, API/database deferrals, and test/UAT sequencing. |

## 3. Approved Terminology

Use these terms consistently in the final Assisted Payment Terminal System Design:

| Term | Approved meaning |
| --- | --- |
| Assisted Payment Terminal | Separate payment-capable terminal app family for staffed assisted payment and approved degraded/BCP terminal operation. |
| Cashier-Assisted Terminal | Normal staffed assisted-payment mode of the Assisted Payment Terminal app family. |
| Continuity Terminal | Restricted degraded/BCP mode of the Assisted Payment Terminal app family; disabled by default. |
| Operator Console | Separate internal non-payment governance and operations module. It may review, supervise, and govern, but it is not a payment terminal. |
| Central PMS | Platform control authority for payment-linked state, payment finality, TariffSnapshot/payable-basis control, fiscal issuance reference recording, degraded resolve decisions under approved policy, and ExitAuthorization. |
| Central PMS / Discount workflow | Authority for statutory discount policy resolution, validation persistence, and payable-basis update. |
| Payment Orchestrator | Payment provider interaction and verified provider outcome reporting boundary. It does not declare platform payment finality. |
| Site POS Server | Fiscal issuance authority for the resolved Site. It issues Sales Invoices and owns fiscal records/reports. |
| Vendor PMS / HCP | Normal authority for raw parking session lifecycle and tariff computation. |
| Vendor PMS Connector | Integration boundary that reports vendor facts, health, projection freshness, and normalized outcomes without becoming an authority. |
| HikCentral Connector Profile | HCP-specific connector profile subordinate to the Vendor PMS Connector design. |
| Projection | Operational visibility and controlled degraded support input only. Projection is not financial truth, normal tariff truth, payment finality, fiscal truth, discount approval, or exit authority. |
| ExitAuthorization | Central PMS-issued authorization consumed by gate/exit infrastructure. Terminals, POS Server, Operator Console, dashboards, connectors, and gates do not issue it. |
| Sales Invoice | Primary parking fiscal output issued by the resolved Site POS Server. |

## 4. Assisted Payment Terminal Scope

The final design may cover Assisted Payment Terminal as a terminal surface and boundary model for:

- Cashier/operator authentication context.
- Trusted terminal/device identity.
- Assigned Site and Site Group context.
- Shift/session accountability.
- Ticket/card scan or manual lookup through approved backend flow.
- Payable-basis display from backend authority.
- Statutory discount validation input capture and evidence reference handling where policy requires.
- Submission to Central PMS / Discount workflow.
- Payment initiation and status display through approved backend flow.
- Fiscal issuance routing/status display through the resolved Site POS Server and Central PMS.
- ExitAuthorization status display from Central PMS.
- Continuity Terminal restricted workflow under approved degraded/BCP controls.
- Incident, audit, reconciliation, and post-restoration review tagging.
- Supervisor escalation handoff to Operator Console or approved operations workflow.
- Terminal health, device trust, evidence/privacy posture, and reporting handoff at design level.

The final design must keep terminal-local behavior to capture, presentation, workflow coordination, status display, and controlled escalation. It must not promote the terminal into a backend authority.

## 5. Non-Authority Matrix

| Function or decision | Approved authority | Assisted Payment Terminal guardrail |
| --- | --- | --- |
| Raw parking session lifecycle in normal mode | Vendor PMS / HCP | Terminal requests lookup through backend flow and displays returned status only. |
| Normal tariff computation | Vendor PMS / HCP | Terminal does not calculate normal tariff or invent fee from projection. |
| Site Group / Site resolution | Central PMS using approved configuration | Terminal carries assigned context and displays resolved context; it does not override scope resolution. |
| Parking session projection | Central PMS / connector projection flow | Terminal may display freshness/status where returned; projection is not source of truth. |
| TariffSnapshot and payable basis | Central PMS with Vendor PMS or approved degraded basis | Terminal displays payable basis only after backend authority returns it. |
| Statutory discount policy resolution | Central PMS / Discount workflow | Terminal captures inputs and evidence references; it does not approve entitlement. |
| Statutory validation persistence | Central PMS / Discount workflow | Terminal does not create authoritative validation records outside approved workflow. |
| Payable-basis update after discount | Central PMS / Discount workflow | Terminal does not mutate payable basis directly. |
| Payment provider interaction | Payment Orchestrator or approved payment channel integration | Terminal initiates/presents flow through backend and displays status. |
| Platform payment finality | Central PMS | Terminal never declares payment finality, including after apparent provider success. |
| Sales Invoice issuance | Resolved Site POS Server | Terminal does not issue Sales Invoices independently. |
| Fiscal issuance reference recording | Central PMS | Terminal displays fiscal status/reference only where returned. |
| ExitAuthorization | Central PMS | Terminal does not issue ExitAuthorization or imply exit is authorized until returned by Central PMS. |
| Gate/exit execution | Gate/exit system consuming Central PMS authorization | Terminal does not directly open gates. |
| Continuity activation/governance | Approved Continuity / Operator Console / operations workflow | Terminal mode remains disabled by default and activates only under approved controls. |
| Manual release governance | Operator Console / approved operations workflow | Terminal may display controlled messaging only where policy allows. |
| Reporting visibility | Management Dashboard and Reporting | Terminal health/status may feed visibility; dashboard remains reporting only. |

## 6. Relationship to Central PMS

Central PMS remains the core platform control authority. The final APT design must state that Central PMS owns:

- Payment-linked platform control state.
- TariffSnapshot recording and payable-basis state.
- PaymentAttempt and PaymentConfirmation/platform payment finality.
- Fiscal issuance reference recording.
- Degraded resolve decisions under approved Continuity policy.
- ExitAuthorization.
- Control-state audit and reconciliation coordination.

Assisted Payment Terminal interacts with Central PMS through approved backend flows. It must submit cashier/device/shift/Site context where needed and display backend state, but it must not declare finality, update payable basis, issue authorization, or bypass Central PMS.

## 7. Relationship to POS Server

The Assisted Payment Terminal is a payment channel/terminal under the resolved Site POS Server fiscal model. It is not a separate POS system per terminal.

The final design must preserve:

- The resolved Site determines Site POS Server routing.
- POS Server issues Sales Invoices and owns fiscal records, fiscal numbering, reports, counters, Electronic Journal, POSLog, fiscal audit, reprint controls, adjustments, fiscal retention, and fiscal export.
- Fiscal issuance must succeed before Central PMS issues normal ExitAuthorization.
- If fiscal issuance fails or times out, the terminal must not imply exit is authorized.
- Channel/terminal presentation, printing, QR display, or status display does not make the terminal fiscal authority.

## 8. Relationship to Payment Orchestrator

Payment Orchestrator performs provider interaction, provider flow coordination, callback handling, verification, and verified provider outcome reporting.

The final APT design must preserve:

- Provider success or terminal-visible payment completion is evidence, not platform finality.
- Payment Orchestrator does not declare platform payment finality.
- Central PMS records platform payment finality after applying required validation and controls.
- Unknown provider outcomes remain pending/exception states and fail closed for exit.
- Terminal messaging must distinguish initiated, pending, failed, cancelled, completed by provider, and platform-final states where backend flow exposes those distinctions.

## 9. Relationship to Vendor PMS Connector / HikCentral

Vendor PMS/HCP remains authority for raw parking session lifecycle and normal tariff computation. The Vendor PMS Connector and HikCentral Connector Profile are integration boundaries, not platform authorities.

The final APT design must preserve:

- Normal lookup and payable-basis retrieval go through Central PMS and the configured connector boundary.
- Projection supports lookup acceleration, operational visibility, dashboards, stale alerts, and controlled degraded evaluation only.
- Projection does not replace live Vendor PMS/HCP fee calculation in normal mode.
- HCP ParkingLotIndexCode is vendor-side identity and must map through AdapterMapping; it must not become ExitPass `site_id`.
- HCP one-minute passageway polling is a planning baseline, not a final freshness threshold or approval for degraded use.
- HCP `cardNum` meaning and ticket-only lookup support remain unresolved unless later vendor/deployment validation confirms them.
- HCP `parkingfee/confirm` is a mutating vendor acknowledgment area, disabled by default unless explicitly approved; its result is vendor acknowledgment, not ExitPass payment finality.

## 10. Relationship to Operator Console

Operator Console and Assisted Payment Terminal are separate modules/apps with separate permission boundaries.

Operator Console may support:

- Supervisor review and override where policy allows.
- Statutory discount review and evidence review.
- Continuity activation/deactivation review.
- Fiscal issuance exception review.
- Manual release governance.
- Connector health and projection freshness visibility.
- Audit, reporting, device, shift, and operations controls.

Operator Console must not:

- Become the payment terminal.
- Collect payment.
- Declare payment finality.
- Issue Sales Invoices.
- Issue or consume ExitAuthorization.
- Directly open gates.
- Bypass Central PMS / Discount workflow, POS Server, or Continuity controls.

Assisted Payment Terminal handles cashier/continuity payment workflow surfaces. Supervisor/compliance governance belongs to Operator Console or approved operations workflow.

## 11. Relationship to Continuity

Continuity Terminal is a mode of the Assisted Payment Terminal app family. It is restricted degraded/BCP operation, not a separate product family and not a silent fallback.

The final design must preserve:

- Continuity Terminal is disabled by default.
- Activation occurs only under approved degraded/BCP controls.
- Supervisor approval applies where policy requires.
- Activation must include scope, affected dependency, incident or BCP reference, allowed and restricted workflows, audit tagging, reconciliation tagging, and deactivation/restoration criteria.
- Continuity workflows fail closed when projection, policy, entitlement, evidence, fiscal, payment, payable basis, or exit state is stale, ambiguous, unsafe, insufficient, or unknown.
- Continuity-origin activity moves into reconciliation and post-restoration review.
- Manual release, if allowed, is last resort and must be supervisor-approved where required, incident-tagged, audit-tagged, reconciliation-tagged, reason-coded, attributable, and reviewed.

Continuity does not replace normal Vendor PMS/Central PMS authority and does not approve unmanaged offline payment, discount, fiscal issuance, or exit behavior.

## 12. Relationship to Management Dashboard and Reporting

Management Dashboard and Reporting is visibility/reporting only. It may consume authorized APT-related operational, fiscal, audit, continuity, and reconciliation records, but it must not become a workflow authority.

The final APT design should allow terminal health/status and activity to be visible through approved reporting paths while preserving:

- Projection and terminal health views are operational visibility.
- Financial/revenue dashboards use canonical payment, provider, fiscal, and reconciliation records.
- Fiscal dashboards reconcile to POS Server fiscal records and Central PMS fiscal issuance references.
- Dashboard/reporting users cannot declare payment finality, issue Sales Invoices, approve discounts, alter payable basis, issue ExitAuthorization, or open gates.
- Reports and exports must label source, freshness, and authority level.

## 13. Operating Modes

### Cashier-Assisted Terminal Mode

Cashier-Assisted Terminal mode is the normal staffed assisted-payment mode.

It supports:

- Cashier workflow.
- Authenticated cashier/operator context.
- Trusted terminal/device identity.
- Assigned Site/Site Group context.
- Shift/session accountability.
- Ticket/card/manual lookup through backend flow.
- Payable-basis display from approved backend authority.
- Statutory discount validation input capture and evidence references where policy requires.
- Submission to Central PMS / Discount workflow.
- Backend validation result display and refreshed payable-basis display.
- Payment initiation/status display through approved backend flow.
- Fiscal issuance status display through Central PMS and resolved Site POS Server flow.
- ExitAuthorization status display from Central PMS.
- Supervisor escalation handoff.
- Audit and reconciliation metadata.

It must not:

- Become a local POS/fiscal authority.
- Declare payment finality.
- Approve entitlement or mutate payable basis directly.
- Issue Sales Invoices independently.
- Issue ExitAuthorization.
- Open gates directly.
- Bypass Central PMS, Discount workflow, Payment Orchestrator, POS Server, or Vendor PMS/HCP authority.

### Continuity Terminal Mode

Continuity Terminal mode is restricted degraded/BCP mode.

It supports only policy-approved restricted workflows:

- Disabled-by-default posture.
- Activation under approved degraded/BCP controls.
- Supervisor approval where required.
- Incident, audit, and reconciliation tagging.
- Restricted ticket/card lookup using available projection or approved continuity source only where policy allows.
- Restricted degraded payable-basis display only when Central PMS determines the basis is safe and allowed.
- Restricted statutory discount handling only under approved degraded policy.
- Payment collection only where continuity policy and backend/fiscal prerequisites allow.
- POS Server fiscal routing where available and allowed.
- Controlled manual/assisted release messaging only where approved.
- Post-restoration review handoff.

It must fail closed when:

- Projection is stale, ambiguous, insufficient, or unavailable.
- Policy basis is missing or unsafe.
- Entitlement cannot be safely validated.
- Evidence requirement cannot be safely met.
- Payable-basis recalculation cannot be safely performed.
- Payment outcome is unknown.
- Fiscal issuance is failed, timed out, unknown, or unsafe.
- ExitAuthorization state is pending, missing, or unsafe.

It must not silently replace normal Vendor PMS/Central PMS authority.

## 14. Scope Boundaries and Deferrals

The final APT System Design must remain a System Design. It must not become an API Contract, Database Design, Engineering Pack, Android implementation guide, POS Server design, Operator Console design, Continuity System Design, Test/UAT Pack, or Runbook Pack.

Do not finalize in the APT System Design:

- API endpoint paths.
- DTOs.
- Database tables, columns, constraints, indexes, or migrations.
- Event payloads, queue names, or outbox schemas.
- Terminal implementation classes.
- Android shell internals.
- WebView/PWA core implementation.
- Native bridge implementation.
- Scanner, camera, printer, or cash drawer SDK calls.
- Local storage schema.
- Certificate/key storage implementation.
- Kiosk lockdown implementation.
- Exact fixed-station/browser/PWA eligibility rules.
- Exact RBAC permission matrix.
- Final UAT scripts.
- Runbook procedures.

The design may name these as future design topics and describe authority and trust-boundary posture, but must not prescribe implementation artifacts.

## 15. Risky Terminology and Misuse Cases

The Lead draft should flag or correct the following:

| Risky term or misuse | Required correction |
| --- | --- |
| `EC Device` when Continuity Terminal is intended | Use `Continuity Terminal` for the APT restricted degraded/BCP mode. Legacy `EC Device` may appear in broad POS/channel source context but should not become APT terminology. |
| `Cashier POS` when Cashier-Assisted Terminal is intended | Use `Cashier-Assisted Terminal` for normal staffed APT mode. Do not imply an independent POS system per terminal. |
| Operator Console as payment terminal | Operator Console is non-payment governance; Assisted Payment Terminal is the payment-capable terminal family. |
| Terminal payment finality | Central PMS declares platform payment finality; terminal displays backend status. |
| Terminal fiscal issuance | Site POS Server issues Sales Invoices; terminal may present/display/print where policy allows. |
| Terminal ExitAuthorization | Central PMS issues ExitAuthorization; terminal displays status. |
| Terminal opens gate | Gate/exit infrastructure consumes Central PMS authorization; terminal does not directly open gates. |
| Terminal approves discount | Central PMS / Discount workflow owns policy resolution and validation persistence. |
| Terminal recalculates payable basis | Central PMS owns payable-basis update after approved validation or degraded decision. |
| Projection as source of truth | Projection is operational visibility and controlled degraded support only. |
| Automatic fallback or silent fallback | Continuity must be explicit, controlled, audited, and reconciled; no silent fallback. |
| Android-only | Approved posture is Android-first for field terminal reference, not Android-exclusive for every deployment. |
| Browser/PWA as acceptable field hardening without controls | Fixed-station/browser/PWA eligibility remains open and must satisfy device trust, security, POS, and field-hardening controls before acceptance. |

Source issue noted: planning and POS/channel documents retain legacy terms such as `Cashier POS` and `EC Device` in broad channel lists. The APT final design should normalize those to `Cashier-Assisted Terminal` and `Continuity Terminal` when referring to approved APT modes.

## 16. Required Statements for Final Design

The final Assisted Payment Terminal System Design should include these statements or equivalent language:

- Assisted Payment Terminal is a separate payment-capable terminal app family.
- Assisted Payment Terminal is not Operator Console.
- Assisted Payment Terminal supports two approved modes: Cashier-Assisted Terminal and Continuity Terminal.
- Cashier-Assisted Terminal is normal staffed assisted-payment mode.
- Continuity Terminal is restricted degraded/BCP mode and is disabled by default.
- Assisted Payment Terminal captures, presents, coordinates, and escalates; it does not own backend authority decisions.
- Assisted Payment Terminal does not declare platform payment finality.
- Assisted Payment Terminal does not issue Sales Invoices independently.
- Assisted Payment Terminal does not issue ExitAuthorization.
- Assisted Payment Terminal does not directly open gates.
- Assisted Payment Terminal does not approve entitlement or mutate payable basis directly.
- Central PMS remains payment finality and ExitAuthorization authority.
- Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, and payable-basis update.
- POS Server remains resolved Site fiscal issuance authority.
- Vendor PMS/HCP remains normal raw session lifecycle and tariff computation authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- Projection is operational visibility and controlled degraded support only.
- Operator Console remains separate and non-payment governance.
- Management Dashboard remains visibility/reporting only.
- Fiscal issuance must succeed before Central PMS issues normal ExitAuthorization, unless a formally approved exception/manual-release policy applies.
- Unknown payment, fiscal, projection, entitlement, payable-basis, or exit states must fail closed or route to approved review.
- Android-first is the preferred field-terminal posture; it is not Android-exclusive.
- API, database, event, DTO, terminal implementation, device SDK, runbook, and UAT specifics remain deferred to the correct downstream documents.

## 17. Open Questions to Preserve

The final design must preserve these open questions and must not resolve them by assumption:

| ID / source | Open question to preserve |
| --- | --- |
| APT-OQ-001 | Final terminal implementation architecture, including Android shell composition, WebView/PWA core approach, native bridge scope, browser/PWA or desktop-compatible variant eligibility, and hybrid deployment rules. |
| APT-OQ-002 / APT-OQ-003 | Final terminal hardware integration requirements and exact camera, scanner, printer, and cash drawer integrations by terminal type. |
| APT-OQ-004 | Kiosk lockdown requirements for field-deployed terminals. |
| APT-OQ-005 | Terminal certificate/key storage model. |
| APT-OQ-006 | Offline evidence capture behavior, if any. |
| APT-OQ-007 / CON-OQ-001 / V13-Q008 | Exact Continuity Terminal and BCP activation authority. |
| APT-OQ-008 / CON-OQ-003 / V13-Q004 / VPC-OQ-003 / HCP-OQ-007 | Exact degraded payable-basis and projection freshness thresholds, stale warning labels, and degraded eligibility rules. |
| APT-OQ-009 / POS-OQ-019 | Exact permission matrix/RBAC across cashier, supervisor, support, admin, and fiscal roles. |
| APT-OQ-010 | Whether cash payment is supported in Cashier-Assisted Terminal v1.0. |
| APT-OQ-011 | Whether card/eWallet/QR payments are hosted checkout only or terminal-integrated. |
| APT-OQ-012 / POS-OQ-001 | Fiscal reprint/display behavior and fiscal identity assignment for terminals/channels. |
| APT-OQ-013 | Any handoff to POS Server for X-read/Z-read or cashier shift reports. |
| APT-OQ-014 / OC-OQ-003 / OC-OQ-004 | Exact relationship to Operator Console for supervisor escalation, continuity activation, and manual release approval. |
| APT-OQ-015 | Whether a fixed cashier station browser/PWA or desktop-compatible variant is allowed in v1.0. |
| APT-OQ-016 / CON-OQ-015 / POS-OQ-015 / POS-OQ-016 / OC-OQ-012 / MDR-OQ-017 | Exact endpoint paths and DTOs, deferred to API Contract. |
| APT-OQ-017 / CON-OQ-016 / POS-OQ-017 / OC-OQ-013 / MDR-OQ-018 | Exact database changes, deferred to Database Design / Database Delta. |
| CON-OQ-007 | Exact offline payment policy, if any. |
| CON-OQ-008 / POS continuity questions | Exact offline fiscal issuance policy, if any; unmanaged offline fiscal issuance is not approved. |
| CON-OQ-009 | Exact fiscal issuance exception release policy. |
| CON-OQ-010 | Exact manual release policy and emergency override boundary. |
| CON-OQ-012 / VPC-OQ-010 / MDR-OQ-011 | Exact reconciliation SLA, closure states, and status labels after restoration. |
| V13-Q009 / VPC-OQ-001 / HCP-OQ-008 | Connector push/pull topology and scheduler ownership. |
| V13-Q010 / VPC-OQ-004 / HCP-OQ-003 / HCP-OQ-010 | Vendor payment acknowledgment timing, retry, idempotency, exit-blocking, and reconciliation policy. |
| HCP-OQ-001 / HCP-OQ-002 | Exact `cardNum` meaning and correct ticket-only fee calculation lookup key/barcode behavior. |
| HCP-OQ-004 / VPC-OQ-006 | Final HCP/vendor error code contract and normalized error categories exposed to platform users. |
| HCP-OQ-005 / HCP-OQ-006 / HCP-OQ-009 | HCP limits, license/module permissions, and signing implementation details. |
| MDR-OQ-016 | BI/reporting technology or embedded dashboard approach. |
| Approval baseline open items | BIR/accounting confirmation items, MIN/PTU/serial/software/supplier assignment, tax/VAT treatment, digital Sales Invoice URL security model, POS Server technical design details, exact API/database/engineering implementation details. |

## 18. Summary for Lead

The Assisted Payment Terminal final design should describe a hardened, payment-capable terminal app family with two modes: Cashier-Assisted Terminal for normal staffed assisted payment and Continuity Terminal for restricted degraded/BCP operation. The terminal is a workflow and presentation surface, not an authority.

The Lead should anchor every workflow around backend authorities:

- Vendor PMS/HCP supplies normal raw session lifecycle and tariff computation.
- Central PMS owns payment-linked control state, platform payment finality, payable-basis control, degraded resolve decisioning under policy, fiscal reference recording, and ExitAuthorization.
- Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, and payable-basis update.
- Payment Orchestrator reports verified provider outcomes without declaring platform finality.
- Site POS Server issues Sales Invoices for the resolved Site.
- Operator Console governs and reviews but does not collect payment.
- Management Dashboard reports and labels source/freshness but does not decide.

Continuity Terminal must remain disabled by default, explicit, audited, reconciliation-tagged, and fail-closed when state is unsafe. Projection must remain operational visibility and controlled degraded input only. The Lead should normalize legacy `Cashier POS` / `EC Device` wording into the approved APT mode names and carry all endpoint, DTO, database, event, Android, WebView/PWA, native bridge, device SDK, UAT, and runbook specifics forward as deferrals.
