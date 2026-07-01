# Assisted Payment Terminal System Design Input Pack 04: Diagram Planning

## 1. Purpose

This input pack plans the diagram set for the future Assisted Payment Terminal System Design. It inventories relevant v1.3 diagrams, recommends System Design diagram intents, identifies expected components and authority labels, and records diagram risks for the Lead integration pass.

This pack does not create final diagrams, PlantUML files, JPG files, database diagrams, API route diagrams, implementation class diagrams, Android package diagrams, device SDK diagrams, or endpoint maps.

## 2. Source Documents and Diagram Folders Reviewed

Source documents reviewed:

| Source | Diagram-planning relevance |
| --- | --- |
| `docs/v1.3/assisted-payment-terminal/system-design/ExitPass_Assisted_Payment_Terminal_System_Design_Orchestration_Plan.md` | Defines specialist scope, output ownership, authority guardrails, mode guardrails, implementation-posture guardrails, and no-final-diagram constraint. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Primary APT source for Cashier-Assisted Terminal, Continuity Terminal, statutory discount capture, payment collection, POS Server fiscal routing, ExitAuthorization status display, Android-first posture, and business diagrams. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System-level architecture, trust boundaries, authority matrix, APT backend boundary, payment/fiscal/exit sequence, continuity, observability, and system-design diagram style. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core authority model, Site/Site Group semantics, projection boundary, fiscal-before-exit choreography, statutory discount capture boundary, continuity restrictions, and audit requirements. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Continuity Terminal disabled-by-default posture, activation controls, restricted degraded operation, fail-closed behavior, manual release governance, and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console separation, non-payment governance, supervisor review, fiscal exception review, continuity activation review, and manual release governance boundary. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal authority, Sales Invoice issuance, fiscal issuance before ExitAuthorization, fiscal exception handling, and terminal/channel non-fiscal-authority posture. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Vendor PMS/HCP connector boundary, normal live resolve, fee calculation, projection freshness, degraded handoff, vendor acknowledgment, and connector health. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HCP-specific object identity, authentication boundary, passageway projection, ticket/card uncertainty, live fee calculation, conditional vendor acknowledgment, and health/source-gap posture. |

Diagram folders reviewed:

| Folder | Relevant existing coverage |
| --- | --- |
| `docs/v1.3/assisted-payment-terminal/diagrams/` | APT business context, operating modes, cashier-assisted discount/payment flow, payment/fiscal/exit authority flow, continuity terminal flow, and Android-first posture. |
| `docs/v1.3/diagrams/system-design/` | v1.3 logical architecture, authority boundary, topology, payment-to-exit sequence, payment finality/fiscal/ExitAuthorization sequence, projection freshness, continuity, APT context/modes, governance boundary, dashboard source-of-truth boundary, and audit/event flow. |
| `docs/v1.3/continuity/diagrams/` | Continuity context, activation/deactivation, degraded resolve, continuity payment/fiscal/exit flow, Continuity Terminal restricted operation, and post-restoration reconciliation. |
| `docs/v1.3/operator-console/diagrams/` | Operator Console context, module boundary, statutory discount review, continuity governance, fiscal exception review, and manual release governance. |
| `docs/v1.3/pos-invoicing/diagrams/` | POS/Invoicing context, Site POS Server model, payment-to-exit fiscal sequence, channel/terminal fiscal routing, fiscal reporting model, and fiscal issuance failure exception flow. |
| `docs/v1.3/vendor-pms-connector/diagrams/` | Generic connector context, connector instance model, normal live resolve, fee calculation, projection freshness, vendor acknowledgment, degraded handoff, and connector health reporting. |
| `docs/v1.3/hikcentral-connector/diagrams/` | HCP object identity, HCP authentication boundary, fee API use map, passageway projection, ticket-only fee calculation, conditional vendor acknowledgment, and health/stale projection flow. |

## 3. Existing Relevant v1.3 Diagrams

The future APT System Design should reuse the authority language and visual conventions from these existing diagrams, but should not modify or copy them as final output during specialist work.

| Existing diagram | Existing role for Lead reuse |
| --- | --- |
| APT BRD D-01 `Assisted Payment Terminal Context Diagram` | Business-level APT context and dependency set. Useful seed for a technical logical architecture diagram. |
| APT BRD D-02 `Assisted Payment Terminal Operating Modes` | Business mode split between Cashier-Assisted Terminal and Continuity Terminal. Useful seed for terminal mode model. |
| APT BRD D-03 `Cashier-Assisted Payment with Statutory Discount Validation Flow` | Business cashier flow covering lookup, discount capture, payable-basis refresh, payment, fiscal routing, and ExitAuthorization status display. |
| APT BRD D-04 `Payment, Fiscal Issuance, and ExitAuthorization Authority Flow` | Business authority chain for payment finality, POS Server fiscal issuance, and Central PMS ExitAuthorization. |
| APT BRD D-05 `Continuity Terminal Activation and Restricted Operation Flow` | Business continuity terminal activation and restricted operation. |
| APT BRD D-06 `Android-first Hardened Terminal Posture Diagram` | Business-level Android-first posture, hardened field terminal expectations, and implementation deferral boundary. |
| System Design D-01 `ExitPass v1.3 Logical Architecture` | Overall platform context and boundary labels. |
| System Design D-02 `Authority Boundary Model` | Canonical authority ownership and non-authority warnings. |
| System Design D-03 `Site Group / Site / VendorSystem / Connector Instance / POS Server Topology` | Site/Site Group, vendor connector, and POS routing topology. |
| System Design D-04 `Normal Payment-to-Exit Sequence` | Cross-channel payment-to-exit choreography. |
| System Design D-05 `Payment Finality to Fiscal Issuance to ExitAuthorization Sequence` | Central PMS payment finality, POS Server fiscal issuance, and Central PMS ExitAuthorization sequence. |
| System Design D-06 `Vendor PMS Connector Projection and Freshness Flow` | Projection freshness and visibility boundary. |
| System Design D-07 `Degraded Resolve and Continuity Sequence` | Degraded handling and continuity sequence. |
| System Design D-08 `Assisted Payment Terminal Context and Modes` | Existing high-level System Design APT context and modes. The APT-specific System Design can refine this without contradicting it. |
| System Design D-09 `Operator Console Governance Boundary` | Non-payment governance boundary and separation from APT. |
| System Design D-10 `Management Dashboard Source-of-Truth Boundary` | Operational visibility versus financial/fiscal truth labels. |
| System Design D-11 `Audit, Event, and Outbox Conceptual Flow` | Audit/event/outbox posture for traceability and observability. |
| Continuity D-02/D-05/D-06 | Activation/deactivation, restricted Continuity Terminal operation, and post-restoration reconciliation. |
| Operator Console D-03/D-04/D-05/D-06 | Statutory discount review, continuity governance, fiscal exception review, and manual release governance handoffs. |
| POS/Invoicing D-03/D-04/D-06 | Fiscal sequence, channel/terminal routing, and fiscal issuance failure handling. |
| Vendor PMS Connector VPC-D03/VPC-D04/VPC-D05/VPC-D07/VPC-D08 | Live resolve, fee calculation, projection freshness, degraded handoff, and health reporting. |
| HikCentral HCP-D01 through HCP-D07 | HCP-specific identity, authentication, projection, fee calculation, acknowledgment, and stale health constraints where HCP appears as an example vendor connector. |

## 4. Recommended Assisted Payment Terminal System Design Diagram Set

Recommended diagram set for the later Lead-created System Design diagrams:

| Proposed ID | Recommended diagram | Diagram type | Why it belongs in the APT System Design |
| --- | --- | --- | --- |
| APT-SD-D01 | Assisted Payment Terminal logical architecture | Component/context | Establishes APT as a terminal/channel app family with backend dependencies and authority boundaries. |
| APT-SD-D02 | Terminal mode model: Cashier-Assisted vs Continuity Terminal | State/model | Distinguishes normal staffed mode from disabled-by-default continuity mode and prevents silent fallback assumptions. |
| APT-SD-D03 | Terminal trust boundary and device identity model | Boundary/model | Shows cashier identity, device identity, shift, Site/Site Group binding, backend trust, and field/station boundary. |
| APT-SD-D04 | Cashier authentication, shift, Site/Site Group binding flow | Sequence/flow | Clarifies pre-payment context establishment before lookup, discount capture, payment, fiscal, or exit status display. |
| APT-SD-D05 | Normal cashier-assisted payment sequence | Sequence | Shows the normal APT customer assistance path while preserving Central PMS, Payment Orchestrator, POS Server, and ExitAuthorization authorities. |
| APT-SD-D06 | Statutory discount capture and payable-basis refresh sequence | Sequence | Separates cashier capture from Central PMS / Discount workflow policy resolution and payable-basis update. |
| APT-SD-D07 | Payment finality, fiscal issuance, and ExitAuthorization status display sequence | Sequence | Emphasizes that the terminal displays backend status; it does not declare finality, issue Sales Invoice, or issue ExitAuthorization. |
| APT-SD-D08 | Fiscal issuance failure / pending exit handling flow | Exception flow | Shows paid-but-fiscal-pending/failed states, controlled retry/escalation, and no normal ExitAuthorization until allowed. |
| APT-SD-D09 | Continuity Terminal activation and restricted operation flow | Flow | Shows activation authority, incident/audit/reconciliation tags, restricted workflows, projection limitations, and fail-closed paths. |
| APT-SD-D10 | Manual release governance handoff flow | Handoff/exception flow | Shows terminal request or status display handing off to Operator Console / approved governance workflow, without gate control in APT. |
| APT-SD-D11 | Android-first hardened terminal posture | Boundary/posture | Captures design-level field-terminal hardening while avoiding Android implementation diagrams. |
| APT-SD-D12 | Terminal observability and audit event flow | Event/observability flow | Shows terminal health, device/cashier/shift/site context, audit events, status events, and reporting handoff without making dashboards authority. |

## 5. Diagram Purpose and Intended Section

| Proposed ID | Purpose | Intended System Design section |
| --- | --- | --- |
| APT-SD-D01 | Introduce APT logical architecture, external dependencies, backend authority services, and channel/terminal boundary. | Architecture Overview / System Context |
| APT-SD-D02 | Define the two approved terminal modes and allowed transitions, including Continuity Terminal disabled-by-default posture. | Operating Modes |
| APT-SD-D03 | Show trust zones and identity boundaries for terminal device, cashier, shift, Site/Site Group, backend services, and field peripherals. | Trust Boundary and Device Identity |
| APT-SD-D04 | Show authentication and binding prerequisites before terminal workflow use. | Cashier Session, Shift, and Site Binding |
| APT-SD-D05 | Show normal staffed payment flow from lookup through payment status display. | Cashier-Assisted Payment Workflow |
| APT-SD-D06 | Show statutory discount capture and backend payable-basis refresh. | Statutory Discount Capture and Payable-Basis Handling |
| APT-SD-D07 | Show payment finality, fiscal issuance, fiscal reference recording, and ExitAuthorization status display. | Payment, Fiscal, and Exit Status Handling |
| APT-SD-D08 | Show fiscal issuance failure, timeout, pending exit, retry/escalation, and customer/operator messaging. | Exception Handling |
| APT-SD-D09 | Show Continuity Terminal activation, scope, restricted operation, fail-closed paths, and post-restoration handoff. | Continuity Terminal Mode |
| APT-SD-D10 | Show manual release governance handoff from terminal exception context to approved supervisor/governance workflow. | Governance Handoffs / Manual Release |
| APT-SD-D11 | Show Android-first hardened terminal posture, native boundary concepts, peripherals, local storage restrictions, and fixed-station eligibility. | Device Posture and Deployment Model |
| APT-SD-D12 | Show audit, observability, health, and reporting event paths with source-of-truth labels. | Observability, Audit, and Reporting Handoff |

## 6. Key Components Per Diagram

| Proposed ID | Key components to show |
| --- | --- |
| APT-SD-D01 | Assisted Payment Terminal UI/app family, Cashier-Assisted Terminal mode, Continuity Terminal mode, Central PMS, Central PMS / Discount workflow, Payment Orchestrator, payment provider, resolved Site POS Server, Vendor PMS/HCP connector, Vendor PMS/HCP, Operator Console, Management Dashboard/Reporting, Audit/Event capability, gate/exit integration as consumer of Central PMS ExitAuthorization. |
| APT-SD-D02 | Mode states: disabled/unassigned terminal, authenticated Cashier-Assisted Terminal, continuity-disabled default, continuity-eligible, continuity-active, restricted operation, restoration/post-review. Show activation controls, allowed workflow scope, fail-closed paths, and return to normal. |
| APT-SD-D03 | Terminal device identity, cashier/operator identity, shift/session context, Site/Site Group assignment, backend identity/RBAC, trusted core boundary, field terminal boundary, fixed station variant boundary, evidence/privacy boundary, peripheral boundary, audit logging. |
| APT-SD-D04 | Cashier, APT, identity/RBAC workflow, device trust workflow, shift validation, Site/Site Group assignment, Central PMS context binding, audit event for session start, failure states for untrusted device, no active shift, or invalid Site scope. |
| APT-SD-D05 | Cashier, parker, APT, Central PMS, Vendor PMS/HCP connector, Vendor PMS/HCP, Payment Orchestrator, payment provider, POS Server, ExitAuthorization status from Central PMS, customer instruction/status display. |
| APT-SD-D06 | Cashier, APT capture surface, evidence capture/reference, Central PMS / Discount workflow, policy validation, payable-basis recalculation/refresh, TariffSnapshot/payable-basis status from Central PMS, supervisor review handoff to Operator Console where required. |
| APT-SD-D07 | APT, Central PMS payment attempt/status, Payment Orchestrator verified outcome, Central PMS payment finality, POS Server Sales Invoice issuance, Central PMS fiscal reference recording, Central PMS ExitAuthorization, terminal display of fiscal and exit status. |
| APT-SD-D08 | APT, Central PMS, POS Server, Operator Console/governance workflow, audit/reconciliation tags, customer/operator message states: payment received, fiscal pending, fiscal failed, exit authorization pending, exception under review, resolved. |
| APT-SD-D09 | Approved continuity/governance workflow, supervisor/authorized activator, APT Continuity Terminal mode, Central PMS degraded decisioning, projection/freshness input, Vendor PMS/HCP connector health, POS Server availability, incident/audit/reconciliation tags, fail-closed decision nodes, post-restoration review. |
| APT-SD-D10 | APT exception context, cashier request/status display, Operator Console or approved operations workflow, supervisor approval/rejection, incident/reason/audit/reconciliation tags, manual release execution boundary, Central PMS record/reconciliation visibility, gate/physical release outside APT unless separately approved. |
| APT-SD-D11 | Android-first field terminal, optional fixed cashier station variant, web-based workflow core, native shell/bridge boundary at concept level, scanner/camera/printer/cash drawer integration boundary, device identity, key/certificate storage concept, kiosk/lockdown controls, local storage restriction, privacy/evidence controls, offline/degraded safeguard boundary. |
| APT-SD-D12 | APT terminal health, device trust signals, cashier/shift/site activity, workflow events, discount capture events, payment/fiscal/exit status display events, continuity tags, audit/event capability, observability/operations view, Management Dashboard/Reporting visibility with source labels, reconciliation consumers. |

## 7. Authority Notes Per Diagram

| Proposed ID | Required authority labels and warnings |
| --- | --- |
| APT-SD-D01 | Label APT as "channel/terminal workflow surface". Label Central PMS as payment finality and ExitAuthorization authority. Label POS Server as Sales Invoice/fiscal authority. Label Operator Console as non-payment governance. Label projection as operational visibility only. |
| APT-SD-D02 | Label Cashier-Assisted Terminal as normal staffed mode. Label Continuity Terminal as disabled by default and restricted to approved degraded/BCP operation. Do not show continuity as normal fallback. |
| APT-SD-D03 | Label terminal-local identity/device checks as trust prerequisites, not authority transfer. Do not imply device possession grants payment finality, fiscal, discount, continuity activation, or exit authority. |
| APT-SD-D04 | Show authentication, device trust, shift, and Site/Site Group binding as prerequisites. Failed binding must block terminal workflow or route to approved review. |
| APT-SD-D05 | Show APT initiating workflow and displaying returned status only. Central PMS resolves platform state and payment finality; Vendor PMS/HCP supplies normal session/tariff facts through connector; POS Server owns fiscal issuance. |
| APT-SD-D06 | Show APT capturing and submitting statutory discount inputs only. Central PMS / Discount workflow owns policy resolution, validation persistence, evidence handling decision, and payable-basis update. |
| APT-SD-D07 | Show Payment Orchestrator as verified provider outcome reporter, not platform finality owner. Show Central PMS recording payment finality and fiscal reference. Show POS Server issuing Sales Invoice. Show APT displaying statuses only. |
| APT-SD-D08 | Show fiscal failure as blocking normal ExitAuthorization. Manual release, if shown, must be separately governed, incident-tagged, audit-tagged, reconciliation-tagged, and not represented as normal ExitAuthorization. |
| APT-SD-D09 | Show projection/freshness as controlled degraded support only. Central PMS or approved continuity policy decides degraded handling. APT does not approve degraded tariff basis, discount entitlement, or exit. |
| APT-SD-D10 | Show Operator Console / approved operations workflow as governance handoff, not payment collection. Show APT as source of exception context and status display only. Do not draw APT directly opening a gate. |
| APT-SD-D11 | Label Android-first as preferred field-terminal reference posture, not Android-exclusive and not an implementation package design. Keep native bridge/peripheral items as boundaries, not SDK calls. |
| APT-SD-D12 | Label observability, audit, dashboard, and reporting consumers as visibility/reconstruction tools. Events communicate facts and status; they do not transfer authority. |

## 8. Diagram Risks to Avoid

Flag and prevent these risks in every future diagram:

- APT shown as Operator Console.
- APT shown as POS Server.
- APT shown as finality authority.
- APT shown issuing Sales Invoice.
- APT shown issuing ExitAuthorization.
- APT shown opening gate.
- APT shown approving statutory discount.
- APT shown recalculating payable basis.
- Continuity Terminal shown as normal fallback.
- Projection shown as financial, tariff, payment, or fiscal truth.
- Projection shown as discount approval or exit authority.
- Android-first diagrams becoming Android implementation diagrams.
- Diagrams containing endpoint paths, DTOs, database tables, implementation classes, SDK calls, printer commands, or secrets.
- Payment Orchestrator shown declaring platform payment finality.
- POS Server shown issuing ExitAuthorization.
- Operator Console shown collecting payment or becoming the terminal.
- Management Dashboard/Reporting shown mutating source-of-truth records or deciding workflow outcomes.
- Manual release shown as normal ExitAuthorization instead of governed exception handling.
- Continuity mode shown weakening audit, reconciliation, evidence, fiscal, or discount controls.

## 9. PlantUML Style Recommendations

Recommendations for the later Lead diagram pass:

- Use conceptual PlantUML only: component, sequence, state/activity, and boundary diagrams are appropriate.
- Keep titles explicit and authority-oriented, for example "APT displays status; Central PMS owns finality".
- Use stereotypes or labels such as `<<terminal/channel>>`, `<<authority>>`, `<<non-authority>>`, `<<visibility only>>`, `<<governance>>`, and `<<external authority>>`.
- Use color or line style consistently to distinguish authority flows, display/status flows, audit/observability flows, and governance handoffs.
- Put trust zones in boxes: field terminal, trusted core platform, Site POS fiscal zone, integration/vendor zone, governance/visibility zone, and gate/exit consumption boundary.
- Use notes on diagrams for the non-negotiable warnings: "APT does not issue Sales Invoice", "APT does not issue ExitAuthorization", "Projection is not financial truth", and "Continuity Terminal disabled by default".
- Prefer short component labels over endpoint-like labels. Avoid URL paths, method names, DTO names, table names, class names, SDK method names, printer commands, secret names, token examples, queue names, or payload shapes.
- For sequence diagrams, make terminal messages user-facing or conceptual, such as "submit discount capture" or "display fiscal pending status", not API calls.
- For exception diagrams, show pending, failed, fail-closed, escalation, and review outcomes explicitly.
- For Android-first posture, show hardened boundaries and deployment posture only. Do not draw Android packages, activities, WebView internals, native bridge interfaces, certificate stores, or device SDK methods.
- Reuse existing v1.3 diagram naming conventions where practical, but assign final IDs only during the Lead synthesis pass.

## 10. Summary for Lead

The recommended APT System Design diagram set is:

1. Assisted Payment Terminal logical architecture.
2. Terminal mode model: Cashier-Assisted vs Continuity Terminal.
3. Terminal trust boundary and device identity model.
4. Cashier authentication, shift, Site/Site Group binding flow.
5. Normal cashier-assisted payment sequence.
6. Statutory discount capture and payable-basis refresh sequence.
7. Payment finality, fiscal issuance, and ExitAuthorization status display sequence.
8. Fiscal issuance failure / pending exit handling flow.
9. Continuity Terminal activation and restricted operation flow.
10. Manual release governance handoff flow.
11. Android-first hardened terminal posture.
12. Terminal observability and audit event flow.

These diagrams should refine the existing APT BRD and ExitPass v1.3 System Design diagrams without changing their authority model. The Lead should keep every diagram anchored on this invariant: Assisted Payment Terminal is a payment-capable channel/terminal workflow surface, not payment finality authority, Sales Invoice issuer, ExitAuthorization issuer, gate-control app, discount policy engine, payable-basis calculator, POS Server, or Operator Console.

The most important visual controls are authority labels, disabled-by-default continuity labeling, projection source-of-truth warnings, and explicit exception/governance handoffs for fiscal failure and manual release. Final diagram creation remains a later Lead task.
