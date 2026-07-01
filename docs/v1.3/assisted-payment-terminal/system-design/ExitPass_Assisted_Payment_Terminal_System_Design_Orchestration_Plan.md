# ExitPass Assisted Payment Terminal System Design Orchestration Plan

Status: Orchestration setup only

Date: 2026-07-01

## 1. Purpose

This orchestration plan prepares the workspace for the future Assisted Payment Terminal System Design v1.0.

The plan defines source inputs, authority guardrails, operating-mode guardrails, implementation-posture guardrails, specialist input-pack ownership, Lead integration rules, review gates, and validation commands. It does not draft the final Assisted Payment Terminal System Design.

The Assisted Payment Terminal System Design must translate the approved v1.3 business baseline into a companion technical design while preserving the ExitPass v1.3 authority model. The terminal is payment-capable, but it is not a platform finality authority, fiscal authority, statutory discount policy engine, ExitAuthorization authority, gate-control app, or Operator Console replacement.

## 2. Target Document

| Target | Path | Status |
| --- | --- | --- |
| Assisted Payment Terminal System Design v1.0 | `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` | To be drafted later by the Lead after specialist input packs exist. |
| Assisted Payment Terminal System Design diagrams | `docs/v1.3/assisted-payment-terminal/system-design/diagrams/` | To be created later by the Lead during final design drafting. |
| Specialist input packs | `docs/v1.3/assisted-payment-terminal/system-design/input-packs/` | Folder prepared by this setup task; files are to be created later by specialists. |

## 3. Approved Baseline Inputs

The later Lead synthesis must use these approved v1.3 inputs:

| Source | Use |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core business authority model, APT positioning, statutory discount capture boundary, Site/Site Group semantics, fiscal-before-exit choreography, and degraded-mode guardrails. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System-level trust boundaries, APT backend boundary, continuity architecture, device trust posture, audit posture, and deferred implementation-stack scope. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approval posture for BRDs as System Design inputs and downstream open-question discipline. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Primary business source for APT modes, workflows, Android-first preferred field posture, statutory discount capture, fiscal routing, payment collection, and open questions. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Continuity Terminal activation, restricted degraded operation, fail-closed behavior, projection freshness, manual release governance, reconciliation, and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console separation, non-payment governance boundary, supervisor review, evidence review, continuity governance, and manual release governance. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Visibility/reporting boundary, operational visibility versus financial truth, terminal health/reporting considerations, and audit/export controls. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal authority, Sales Invoice routing, fiscal issuance before ExitAuthorization, terminal/channel non-fiscal-authority posture, and fiscal exception handling. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Vendor PMS connector authority, normal live resolve, fee calculation, projection, vendor acknowledgment, and connector health boundaries. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HCP-specific projection, fee calculation, identifier uncertainty, health, and source-gap posture that may affect terminal lookup/status display. |

Planning artifacts:

| Source | Use |
| --- | --- |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved decisions for APT app family, statutory discount capture, Operator Console separation, fiscal routing, and authority boundaries. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Open questions for continuity activation authority and downstream technical/design topics. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Impact mapping for APT model, APT statutory discount validation, Operator Console separation, continuity restrictions, and API/database deferrals. |

Business diagram input:

| Source | Use |
| --- | --- |
| `docs/v1.3/assisted-payment-terminal/diagrams/` | Business-level diagram input only. These diagrams should inform final System Design diagram planning but must not be modified by specialists during input-pack work. |

## 4. Assisted Payment Terminal System Design Scope

The future System Design should cover:

- APT component and boundary model.
- Cashier-Assisted Terminal mode technical workflow.
- Continuity Terminal mode technical workflow.
- Terminal authentication, cashier/operator identity, shift/session context, and assigned Site/Site Group context.
- Device identity, device trust, hardened terminal controls, and field-terminal posture.
- Android-first preferred reference posture for field deployments.
- Web-based workflow core, native shell, native bridge, and device integration boundaries at design level.
- Fixed cashier station variant eligibility and constraints.
- Ticket/card scan/manual entry and approved backend lookup flow.
- Payable-basis display and backend authority boundaries.
- Statutory discount capture and Central PMS / Discount workflow handoff.
- Payment collection flow and backend payment-status display.
- Site POS Server fiscal routing/status display through approved backend flow.
- Central PMS ExitAuthorization status display.
- Continuity/degraded restrictions, fail-closed behavior, incident/audit/reconciliation tagging, and post-restoration review handoff.
- Evidence capture, privacy, RBAC, audit, device/shift accountability, and supervisor escalation boundaries.
- Observability, terminal health, operational status display, and dashboard/reporting handoff.
- Open questions and downstream API/database/engineering deferrals.

The design must remain a System Design. It must not become an API Contract, Database Design, Engineering Pack, Runbook Pack, Android implementation guide, POS Server design, Operator Console design, or Continuity System Design.

## 5. Authority Model Guardrails

The later final design and all specialist input packs must preserve these rules:

- Assisted Payment Terminal is a payment-capable terminal app family, not Operator Console.
- Assisted Payment Terminal has two approved modes:
  - Cashier-Assisted Terminal mode for normal staffed assisted payment.
  - Continuity Terminal mode for restricted degraded/BCP operation.
- Continuity Terminal is disabled by default.
- Assisted Payment Terminal does not declare platform payment finality.
- Assisted Payment Terminal does not issue Sales Invoices independently.
- Assisted Payment Terminal does not issue ExitAuthorization.
- Assisted Payment Terminal does not directly open gates.
- Assisted Payment Terminal does not approve statutory entitlement or mutate payable basis directly.
- Central PMS remains authority for payment-linked state, TariffSnapshot, payment finality, fiscal issuance reference recording, degraded resolve decision under approved policy, and ExitAuthorization.
- Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, and payable-basis update.
- POS Server remains resolved Site fiscal issuance authority.
- Vendor PMS / HCP remains normal raw parking session lifecycle and tariff computation authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- Operator Console is separate and non-payment governance.
- Management Dashboard is visibility/reporting only.
- Projection data is operational visibility and controlled degraded support only.

The design must show terminal-local actions as capture, presentation, and workflow coordination only. Backend authorities own the final decisions and records.

## 6. Operating Mode Guardrails

### Cashier-Assisted Terminal Mode

Cashier-Assisted Terminal mode is the normal staffed assisted payment mode.

The final design should cover:

- Authenticated cashier/operator context.
- Trusted terminal/device identity.
- Assigned Site and Site Group context.
- Shift/session accountability.
- Ticket/card scan or manual entry.
- Backend session lookup and payable-basis retrieval.
- Cashier-facing statutory discount validation capture.
- Evidence capture where required by policy.
- Submission to Central PMS / Discount workflow.
- Display of validation status and updated payable basis from backend workflow.
- Payment collection flow through approved payment integration.
- Payment status display without terminal-owned finality.
- Fiscal issuance routing/status display through the resolved Site POS Server via approved backend flow.
- ExitAuthorization status display from Central PMS.
- Supervisor escalation and exception messaging.

Cashier-Assisted Terminal mode must not become a terminal-local discount policy engine, independent POS system, payment-finality authority, or exit authority.

### Continuity Terminal Mode

Continuity Terminal mode is the restricted degraded/BCP operating mode.

The final design should cover:

- Disabled-by-default posture.
- Activation only under approved degraded/BCP controls.
- Supervisor approval where policy requires.
- Incident, audit, and reconciliation tagging.
- Restricted lookup and payable-basis display.
- Restricted statutory discount handling under approved degraded-mode policy.
- Fail-closed behavior where entitlement, policy basis, evidence, projection freshness, or payable-basis recalculation is unsafe.
- Payment collection only where policy and backend/fiscal prerequisites allow.
- POS Server fiscal routing where available and allowed.
- Controlled manual/assisted release messaging only where approved.
- Post-restoration review handoff.

Continuity Terminal mode must not silently replace normal Vendor PMS/Central PMS authority or weaken fiscal, discount, audit, reconciliation, or exit controls.

### Implementation Posture Guardrails

The later System Design must preserve:

- Android-first hardened terminal posture is the preferred field-terminal reference posture.
- Android-first does not mean Android-exclusive for every possible deployment.
- Final Android shell / WebView / PWA core / native bridge / hardware integration details remain for the System Design to structure, but not for Engineering Pack implementation.
- Browser/PWA/desktop-compatible fixed cashier station variant eligibility remains a design topic, not a reason to weaken field-terminal hardening.
- Field terminal posture must address device identity, key/certificate storage, kiosk/lockdown needs, scanner/camera/printer/cash drawer integration boundaries, local storage restrictions, evidence/privacy controls, and offline/degraded safeguards at design level.
- The design must not over-specify implementation classes, API endpoints, DTOs, database tables, device SDK calls, printer commands, or deployment scripts.

## 7. Specialist Input-Pack List

Specialist agents should create these files later, one file per assigned specialist:

| Input pack | Assigned focus | Expected output |
| --- | --- | --- |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/01_architecture_scope_guard.md` | Architecture scope, authority boundaries, module separation, source contradictions, and non-authority guardrails. | Guardrail matrix, component/boundary recommendations, contradiction log, deferred decisions. |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/02_terminal_workflow_and_state.md` | Cashier-Assisted and Continuity Terminal workflows, state concepts, exception paths, statutory discount capture, payment/fiscal/exit status display, supervisor escalation, and fail-closed behavior. | Workflow/state recommendations without endpoint, DTO, table, or implementation detail. |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/03_device_trust_security_android_posture.md` | Device trust, terminal identity, Android-first field posture, WebView/PWA/native bridge boundary, fixed station eligibility, kiosk lockdown, key storage, hardware integration boundary, privacy, evidence, and offline/degraded safeguards. | Security/device posture recommendations and open questions without Android code, SDK calls, or deployment scripts. |
| `docs/v1.3/assisted-payment-terminal/system-design/input-packs/04_diagram_planning.md` | Diagram inventory, existing APT BRD diagram review, proposed System Design diagrams, authority labels, and diagram risk controls. | Diagram plan only; no final diagram files unless later authorized by Lead. |

## 8. File Ownership Rules

- Specialist agents may create only their assigned input-pack file.
- Specialist agents must not edit final documents.
- Specialist agents must not edit approved BRDs, ExitPass System Design, connector designs, or diagrams.
- Specialist agents must not modify `docs/v1.3/assisted-payment-terminal/diagrams/`.
- Specialist agents must not create API/database/engineering implementation details.
- Specialist agents must not create final System Design diagrams.
- Lead integrates the final document only after all input packs exist.
- Any contradiction must be reported in the relevant input pack, not silently corrected in approved sources.

## 9. Lead Integration Rules

The Lead integration pass shall:

- Verify that all four specialist input packs exist before drafting the final System Design.
- Preserve the approved BRD and System Design authority model.
- Keep Operator Console, POS Server, Vendor PMS Connector, HikCentral Connector, Management Dashboard, Payment Orchestrator, Central PMS, and Assisted Payment Terminal boundaries distinct.
- Use the Assisted Payment Terminal BRD as the primary module business source.
- Use Continuity BRD for Continuity Terminal activation, restrictions, and post-restoration controls.
- Use POS/Invoicing BRD for Site POS Server fiscal routing and Sales Invoice authority.
- Use Operator Console BRD for supervisor/governance separation.
- Use Management Dashboard BRD for visibility/reporting boundaries.
- Use Vendor PMS Connector and HikCentral Connector designs for vendor lookup, fee, projection, health, and source-gap posture.
- Treat existing APT BRD diagrams as business context only.
- Carry unresolved decisions forward instead of inventing final stack, endpoint, DTO, database, or device SDK details.
- Create final System Design diagrams only during the later Lead synthesis task.

## 10. Out-of-Scope Items

This orchestration task and the specialist input packs must not:

- Draft the final Assisted Payment Terminal System Design.
- Modify source code.
- Modify database schema.
- Modify API contracts.
- Create DOCX files.
- Modify approved BRDs.
- Modify ExitPass System Design v1.3.
- Modify Vendor PMS Connector System Design.
- Modify HikCentral Connector Profile.
- Draft Database/API/Engineering Pack.
- Draft Test/UAT Pack.
- Draft Runbook Pack.
- Create final System Design diagrams.
- Over-specify Android package structure, WebView framework, native bridge APIs, Java/Kotlin implementation, printer command formats, device SDK calls, endpoint paths, DTOs, database objects, queue names, event payloads, or deployment scripts.

## 11. Review Gates

| Gate | Requirement |
| --- | --- |
| Gate 1: Workspace setup | Orchestration plan exists and `input-packs/` folder is prepared. |
| Gate 2: Specialist ownership | Each specialist creates only the assigned input-pack file. |
| Gate 3: Source alignment | Each input pack cites approved v1.3 sources and reports contradictions instead of editing approved documents. |
| Gate 4: Authority review | Input packs preserve terminal non-finality, non-fiscal-authority, non-discount-authority, non-exit-authority, and non-gate-authority posture. |
| Gate 5: Mode review | Cashier-Assisted and Continuity Terminal mode boundaries remain distinct, with Continuity Terminal disabled by default. |
| Gate 6: Implementation posture review | Android-first hardened field-terminal posture is preserved without making Android exclusive or turning the design into engineering implementation. |
| Gate 7: Deferral review | Endpoint, DTO, database, device SDK, printer command, and deployment script details remain deferred. |
| Gate 8: Lead readiness | All four input packs exist and are internally consistent enough for Lead synthesis. |

## 12. Validation Commands

Run these commands after orchestration setup:

```powershell
git status --short --untracked-files=all
git diff --check
```

Expected result for this setup task:

- Only Markdown orchestration files/folders under `docs/v1.3/assisted-payment-terminal/system-design/` are added.
- No source code changes.
- No database/schema changes.
- No API contract changes.
- No DOCX files.
- No approved BRD changes.
- No ExitPass System Design changes.
- No connector design changes.
- No final Assisted Payment Terminal System Design draft.
- No diagram generation.
- No commit.

## 13. Next Step

Create the four specialist input-pack files in the assigned folder:

1. `01_architecture_scope_guard.md`
2. `02_terminal_workflow_and_state.md`
3. `03_device_trust_security_android_posture.md`
4. `04_diagram_planning.md`

After all four input packs exist and pass the review gates, the Lead may draft `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` and create the System Design diagram set under `docs/v1.3/assisted-payment-terminal/system-design/diagrams/`.

