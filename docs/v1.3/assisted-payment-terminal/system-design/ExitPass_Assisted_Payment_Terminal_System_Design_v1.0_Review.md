# ExitPass Assisted Payment Terminal System Design v1.0 Review

Review date: 2026-07-02

## 1. Review Summary

The Assisted Payment Terminal System Design v1.0 draft is aligned with the orchestration plan, specialist input packs, approved v1.3 BRD baseline, ExitPass System Design v1.3 authority model, POS/Invoicing boundary, Operator Console boundary, Management Dashboard visibility boundary, Vendor PMS Connector design, and HikCentral Connector Profile.

No required fixes were found. The draft preserves APT as a separate payment-capable terminal app family, keeps Cashier-Assisted Terminal as normal staffed assisted-payment mode, keeps Continuity Terminal as restricted degraded/BCP mode disabled by default, and keeps backend authorities responsible for payment finality, fiscal issuance, statutory discount resolution, payable-basis update, ExitAuthorization, and gate execution.

Workspace note: before this review note was created, `git status --short --untracked-files=all` already showed the draft APT System Design and 24 diagram artifacts as untracked. This review task added only this review note.

## 2. Files Reviewed

- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/system-design/ExitPass_Assisted_Payment_Terminal_System_Design_Orchestration_Plan.md`
- `docs/v1.3/assisted-payment-terminal/system-design/input-packs/01_architecture_scope_guard.md`
- `docs/v1.3/assisted-payment-terminal/system-design/input-packs/02_terminal_workflow_and_state.md`
- `docs/v1.3/assisted-payment-terminal/system-design/input-packs/03_device_trust_security_android_posture.md`
- `docs/v1.3/assisted-payment-terminal/system-design/input-packs/04_diagram_planning.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/system-design/diagrams/`

## 3. APT Architecture Review

Pass. The draft positions Assisted Payment Terminal as a terminal/channel workflow surface and separate payment-capable app family, not as Central PMS, Operator Console, POS Server, Payment Orchestrator, Vendor PMS Connector, Management Dashboard, or gate infrastructure.

The architecture section correctly lists backend dependencies and states that the terminal submits device, cashier, shift, Site, Site Group, and mode context while presenting backend-returned workflow state. It does not create authoritative payment, fiscal, discount, tariff, exit, or gate records locally.

## 4. Operating Mode Review

Pass. The draft preserves the two approved modes:

- Cashier-Assisted Terminal as normal staffed assisted-payment mode.
- Continuity Terminal as restricted degraded/BCP mode, disabled by default.

The draft explicitly states that Continuity Terminal availability must never be an implicit fallback from normal failures. This satisfies the guardrail against automatic or silent fallback.

## 5. Authority Boundary Review

Pass. The authority model is consistent with the v1.3 baseline:

- Central PMS remains payment finality, payable-basis/TariffSnapshot, fiscal reference recording, degraded decisioning under approved policy, and ExitAuthorization authority.
- Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, evidence reference governance, and payable-basis effect.
- POS Server remains resolved Site fiscal issuance authority.
- Vendor PMS/HCP remains normal raw parking session and tariff authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- Operator Console remains governance, not payment.
- Management Dashboard remains visibility/reporting only.

## 6. Central PMS / POS Server / Payment Orchestrator Boundary Review

Pass. The draft keeps payment orchestration, platform finality, fiscal issuance, and ExitAuthorization in the correct order:

1. Payment Orchestrator or approved integration handles provider interaction.
2. Central PMS records platform payment finality after verified outcome.
3. Central PMS requests Sales Invoice issuance from the resolved Site POS Server.
4. POS Server returns fiscal identity/status.
5. Central PMS records the fiscal reference.
6. Central PMS issues ExitAuthorization only if eligible.

The draft does not let APT, POS Server, or Payment Orchestrator issue ExitAuthorization, and it does not let APT or Payment Orchestrator declare platform finality.

## 7. Vendor PMS Connector / HikCentral Boundary Review

Pass. The draft preserves vendor and connector boundaries:

- Vendor PMS/HCP provides normal live session and tariff facts through the connector where capability and identifier policy are confirmed.
- Projection is operational visibility and controlled degraded support only.
- APT does not invent tariff from projection, passageway records, or local history.
- HCP `cardNum` and ticket-only lookup uncertainty remain open.
- HCP `parkingfee/confirm` remains identified as mutating vendor acknowledgment behavior and is not part of terminal lookup.

The design does not convert HCP endpoint areas into terminal API contracts or terminal authority.

## 8. Operator Console and Management Dashboard Boundary Review

Pass. The draft separates Operator Console and Management Dashboard responsibilities:

- APT hands off supervisor, compliance, fiscal exception, continuity, and manual release governance to Operator Console or an approved operations workflow.
- Operator Console is not treated as the payment terminal.
- Management Dashboard and Reporting may consume terminal health and workflow visibility where authorized, but remains visibility/reporting only.
- Projection visibility and dashboard/reporting facts do not become source-of-truth authority for payment, fiscal, tariff, discount, continuity activation, reconciliation closure, or exit decisions.

## 9. Terminal Workflow and State Review

Pass. The draft covers cashier authentication, device trust, shift/session context, Site/Site Group binding, lookup, discount capture, payment initiation, fiscal display, ExitAuthorization display, exception handoff, duplicate/pending payment status, and fail-closed behavior.

The terminal state posture is correctly local and non-authoritative. Backend state controls payment finality, payable basis, fiscal reference, degraded decisioning, and ExitAuthorization.

## 10. Device Trust / Security / Android-First Posture Review

Pass. The draft preserves Android-first as the preferred field-terminal posture without making it Android-exclusive.

It also preserves conditional eligibility for fixed cashier station browser/PWA or desktop-compatible variants only when equivalent controls exist: managed or locked-down posture, durable device identity, Site/Site Group enforcement, cashier authentication, shift accountability, appropriate peripheral controls, no unmanaged browser/shared workstation use for payment workflows, no raw card capture, privacy controls, and audit.

Certificate, token, key, attestation, mTLS, browser key binding, rotation, revocation, break-glass, Android shell, WebView/PWA core, native bridge, hardware integration, MDM/kiosk product, and packaging details remain deferred.

## 11. Evidence / Privacy / Payment Security Review

Pass. The draft treats APT as a capture surface rather than an unmanaged evidence repository. It requires minimum evidence capture, privacy notices where required, reference capture where possible, RBAC/audit protection, and handoff to Operator Console or approved governance workflow.

Payment security posture is correct: no raw card capture or storage, hosted checkout/provider-controlled flow unless later approved otherwise, provider status separated from Central PMS finality, unknown provider outcomes held as pending/exception, and duplicate payment initiation mitigated through backend state correlation.

## 12. Continuity Terminal and Degraded Operation Review

Pass. Continuity Terminal is restricted, disabled by default, scoped by approved activation, and requires authorized terminal, cashier, Site/Site Group, shift/session, and incident/BCP context where policy requires.

Degraded/projection handling remains controlled by Central PMS under approved Continuity policy. Vendor PMS/HCP outage, WebPay/APM outage, connector stale state, or network degradation does not itself authorize payment, fiscal issuance, degraded tariff, manual release, or exit.

## 13. Fiscal Issuance / Pending Exit / Manual Release Review

Pass. The draft preserves fiscal issuance before normal ExitAuthorization. If payment finality is recorded but fiscal issuance fails or times out, Central PMS does not issue normal ExitAuthorization yet, and the case enters controlled fiscal exception, retry, or review workflow.

Manual release remains last-resort governed handoff, not normal ExitAuthorization. The draft requires supervisor approval where required, incident/audit/reconciliation tagging, reason, attribution, and post-review.

## 14. Diagram Coverage Review

Pass. Diagram folder check:

- `.puml` files: 12
- `.jpg` files: 12
- `.png` files: 0

The 12 diagrams match the diagram-planning input pack:

- APT-SD-D01 logical architecture
- APT-SD-D02 terminal mode model
- APT-SD-D03 terminal trust boundary/device identity
- APT-SD-D04 cashier authentication/shift/Site/Site Group binding
- APT-SD-D05 normal cashier-assisted payment sequence
- APT-SD-D06 statutory discount capture/payable-basis refresh
- APT-SD-D07 payment finality/fiscal issuance/ExitAuthorization status display
- APT-SD-D08 fiscal issuance failure/pending exit handling
- APT-SD-D09 Continuity Terminal activation/restricted operation
- APT-SD-D10 manual release governance handoff
- APT-SD-D11 Android-first hardened terminal posture
- APT-SD-D12 terminal observability/audit event flow

PUML scan found the diagrams conceptual. They do not include secrets, DTO definitions, database tables, endpoint maps, implementation classes, Android package internals, device SDK calls, or printer commands. Authority mentions in diagrams assign finality/ExitAuthorization to Central PMS, fiscal issuance to POS Server, governance to Operator Console/approved workflow, and visibility to dashboard/reporting.

## 15. Open Questions and Deferrals Review

Pass. The draft preserves downstream deferrals and does not finalize:

- Endpoint paths, DTOs, database changes, event payloads, or engineering implementation.
- Android shell/WebView/PWA/native bridge split.
- Fixed station browser/PWA eligibility.
- Terminal hardware, scanner/camera/printer/cash drawer integration.
- Kiosk lockdown and device trust details.
- Terminal certificate/key storage model.
- Offline evidence, payment, and fiscal behavior.
- Continuity activation authority.
- Projection freshness threshold and degraded tariff basis.
- Manual release and fiscal exception release policy.
- Cash payment support.
- Payment rail integration model.
- Fiscal reprint/display behavior.
- POS Server handoff for X-read/Z-read or cashier shift reports.
- HCP `cardNum`, ticket-only lookup key, and `parkingfee/confirm` behavior.
- UAT scripts and runbook procedures.

## 16. Risky Terminology Scan

Pass. Risky terminology was searched in the draft and APT system-design diagram folder.

No unsafe use found:

- `EC Device`: not found.
- `Cashier POS`: not found.
- `Operator Console as payment terminal`: not found.
- `terminal payment finality`: not found as an affirmative claim.
- `terminal fiscal issuance`: not found as an affirmative claim.
- `terminal ExitAuthorization`: not found as an affirmative claim.
- `terminal opens gate`: not found.
- `terminal approves discount`: not found.
- `terminal recalculates payable basis`: not found.
- `projection source of truth`: not found.
- `automatic fallback`: not found.
- `silent fallback`: not found.
- `Android-only`: not found.
- `browser/PWA as acceptable field hardening without controls`: not found.
- `Official Receipt`: not found.
- Exact uppercase `OR`: not found in the draft/diagram scan.

Contextual or explicitly safe uses:

- `secret`, `token`, `key`, and `certificate` appear only in deferred security posture, prohibition, or open-question context. No actual secrets or credential values are included.
- `Payment Orchestrator`, `Operator Console`, `Sales Invoice`, `ExitAuthorization`, `projection`, `fiscal issuance`, and related terms appear in authority-preserving context.
- `parkingfee/confirm` appears as mutating HCP acknowledgment behavior, not as terminal lookup.
- `cardNum` appears only as unresolved HCP identifier uncertainty.

## 17. Issues Found

No required review issues were found.

## 18. Required Fixes, if any

None.

## 19. Nice-to-Have Fixes, if any

None required for this review. Future downstream packs should continue carrying the open questions already listed in the draft.

## 20. Recommendation

Recommendation: approve the Assisted Payment Terminal System Design v1.0 draft for its current documentation purpose, subject to the preserved downstream deferrals and without treating it as an API contract, database design, implementation pack, runbook, UAT pack, Android implementation guide, POS Server design, Operator Console design, or Continuity System Design.

Validation to run after this note is created:

- `git status --short --untracked-files=all`
- `git diff --check`
