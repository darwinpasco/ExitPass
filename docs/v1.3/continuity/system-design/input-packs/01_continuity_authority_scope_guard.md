# Continuity Authority and Scope Guard Input Pack

Status: Specialist input pack for later Lead synthesis

Branch: `docs/v1.3-continuity-system-design`

Assigned focus: Continuity authority boundaries, source contradictions, non-authority scope, terminology normalization, and approved/deferred decisions.

## 1. Purpose

This input pack provides authority and scope guardrails for the future ExitPass Continuity System Design v1.0. It is not the final Continuity System Design.

The Lead must use this pack to prevent:

- Authority drift away from the approved v1.3 model.
- Terminology drift around Continuity, projection, degraded resolve, fiscal issuance, and ExitAuthorization.
- Silent fallback assumptions.
- Premature API, database, engineering, event, monitoring, UAT, or runbook detail.

Continuity must remain explicit controlled degraded operation. It must be approved where policy requires, time-bound, audited, incident-tagged, reconciliation-tagged, and subject to post-restoration review.

## 2. Source Documents Reviewed

Reviewed sources:

- `docs/v1.3/continuity/system-design/ExitPass_Continuity_System_Design_Orchestration_Plan.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md`
- `docs/v1.3/ExitPass_v1.3_Open_Questions.md`
- `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md`

No source contradictions requiring edits to approved documents were found. The reviewed sources consistently preserve the same authority model. Differences are mostly scope-specific emphasis: the Continuity BRD is the primary business source for degraded operation; ExitPass System Design v1.3 is the architecture authority; companion documents constrain their own modules without changing the core authority model.

## 3. Approved Terminology

Use these terms consistently:

| Term | Approved usage |
| --- | --- |
| Continuity | Explicit controlled degraded-operation capability, not an alternate normal mode. |
| Degraded operation | Approved limited operation under defined scope, controls, audit, incident, reconciliation, and review. |
| Degraded resolve | Central PMS decisioning under approved Continuity policy that may use projection and approved degraded tariff basis where allowed. |
| Continuity Terminal | Restricted degraded/BCP mode of Assisted Payment Terminal, disabled by default. |
| Assisted Payment Terminal | Payment-capable terminal app family containing Cashier-Assisted Terminal and Continuity Terminal modes. |
| Operator Console | Internal non-payment governance and operations module. |
| Vendor PMS / HCP | Normal raw parking session lifecycle and normal tariff computation authority. |
| Vendor PMS Connector / HikCentral Connector | Integration boundary that reports vendor facts, health, projection freshness, availability, and normalized outcomes. |
| Parking Session Projection / Projection | Central PMS operational projection of vendor data for visibility and controlled degraded support only. |
| TariffSnapshot | Central PMS-owned immutable payable-basis record from live vendor calculation or approved degraded computation. |
| PaymentConfirmation / payment finality | Central PMS-owned platform payment finality concept. |
| Payment Orchestrator | Provider interaction boundary and verified provider outcome reporter; not platform finality authority. |
| POS Server / Site POS Server | Resolved Site fiscal issuance authority for Sales Invoice and fiscal records. |
| Fiscal issuance reference | Central PMS record linking platform payment/session context to POS Server-issued fiscal identity/status. |
| ExitAuthorization | Central PMS-issued authorization consumed by gate/exit execution. |
| Manual release | Last-resort governed exception, not normal ExitAuthorization and not payment finality. |
| Management Dashboard and Reporting | Visibility/reporting module with source, freshness, and authority labels. |

## 4. Continuity Scope

The future Continuity System Design may cover, at system-design level only:

- Continuity capability architecture and component boundaries.
- Explicit continuity activation/deactivation posture.
- Conceptual operating states.
- Degraded-watch and degraded-active behavior.
- Vendor PMS/HCP outage, connector outage, stale projection, and ambiguity handling.
- Projection freshness, sufficiency, and controlled degraded support.
- Central PMS degraded resolve decisioning under approved policy.
- Approved degraded tariff basis posture without final thresholds or owner mechanics.
- Continuity Terminal activation and restricted operation.
- Continuity-mode statutory discount restrictions.
- Payment uncertainty handling.
- Fiscal issuance failure, timeout, pending-exit, and fiscal exception posture.
- Vendor payment acknowledgment failure and reconciliation posture.
- Manual release governance handoff.
- Gate/exit issue handling without bypassing Central PMS.
- Operator Console governance touchpoints.
- Management Dashboard and Reporting visibility touchpoints.
- Audit, incident, reconciliation, and post-restoration review posture.
- Fail-closed behavior and downstream deferrals.

The design must remain a Continuity System Design. It must not become an API Contract, Database Design, Engineering Pack, Runbook Pack, UAT Pack, POS Server design, Operator Console design, Assisted Payment Terminal design, Vendor PMS Connector design, or HikCentral profile.

## 5. Non-Authority Matrix

| Actor / surface | Must not do |
| --- | --- |
| Projection | Must not be financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority. |
| Vendor PMS Connector / HikCentral Connector | Must not approve degraded resolve, choose stale projection use, invent tariff, declare payment finality, issue fiscal documents, issue ExitAuthorization, or operate gates. |
| HikCentral `parkingfee/confirm` / vendor paid state | Must not be treated as ExitPass payment finality. It is a vendor acknowledgment area only where explicitly approved. |
| Payment Orchestrator | Must not declare platform payment finality, issue fiscal documents, issue ExitAuthorization, or open gates. |
| POS Server | Must not declare platform payment finality or issue ExitAuthorization. |
| Assisted Payment Terminal | Must not declare payment finality, approve statutory entitlement, mutate payable basis directly, issue Sales Invoices independently, issue ExitAuthorization, or open gates. |
| Continuity Terminal | Must not become normal mode, always-available mode, independent fiscal authority, independent payment finality authority, or unmanaged offline workflow. |
| Operator Console | Must not collect payment, declare payment finality, issue Sales Invoices, issue ExitAuthorization, directly open gates, or bypass Central PMS / Discount workflow. |
| Management Dashboard and Reporting | Must not activate continuity, approve manual release, close reconciliation, declare payment finality, issue fiscal documents, approve discounts, mutate payable basis, issue ExitAuthorization, or open gates unless later approved policy explicitly assigns a limited workflow action. |
| Manual release | Must not be described as normal ExitAuthorization or as a cure for incomplete payment, fiscal issuance, vendor acknowledgment, stale projection, or reconciliation. |
| Gate/exit execution | Must not bypass Central PMS authorization. |

## 6. Relationship to Central PMS

Central PMS remains the platform authority for:

- Payment-linked platform control state.
- ParkingSession projection as operational/platform context.
- TariffSnapshot recording.
- PaymentAttempt and PaymentConfirmation/platform payment finality.
- Fiscal issuance reference recording.
- Degraded resolve decisioning under approved Continuity policy.
- Payable-basis effect after approved statutory discount validation.
- ExitAuthorization.
- Control-state audit and reconciliation coordination.

The Lead must not describe Central PMS as replacing Vendor PMS/HCP normal raw session lifecycle or normal tariff authority. Central PMS also does not replace POS Server fiscal issuance authority.

## 7. Relationship to Vendor PMS / HCP

Vendor PMS / HCP remains authority for:

- Raw parking session lifecycle in normal mode.
- Normal tariff computation where live fee calculation is available and confirmed.

Continuity does not replace this normal authority model. If Vendor PMS/HCP is unavailable, degraded resolve may proceed only through Central PMS under approved Continuity policy, freshness controls, approved degraded tariff basis, and audit/reconciliation tagging.

HCP-specific cautions:

- HCP ParkingLotIndexCode is vendor-side identity and must not become ExitPass `site_id`.
- HCP `cardNum` remains an open vendor/deployment question.
- Ticket-only HCP fee calculation remains unconfirmed until deployment/vendor validation confirms the correct lookup key and barcode/QR behavior.

## 8. Relationship to Vendor PMS Connector / HikCentral

Vendor PMS Connector and HikCentral Connector are integration boundaries. They may:

- Authenticate to vendor systems through approved connector controls.
- Request live vendor session and fee facts where capability is confirmed.
- Poll or receive projection facts.
- Report vendor availability, connector health, projection freshness, mapping status, ambiguity, timeout, unknown, and normalized outcomes.
- Report vendor acknowledgment outcomes and backlog where enabled.
- Provide evidence and inputs to Central PMS, Operator Console, Management Dashboard, audit, and reconciliation workflows.

They must not:

- Approve degraded resolve.
- Decide degraded tariff basis.
- Decide if stale projection may be used.
- Treat vendor paid state or HCP `parkingfee/confirm` as ExitPass payment finality.
- Invent a session, tariff, discount approval, fiscal state, payment finality, or exit eligibility.

One-minute HCP passageway polling is a planning baseline only. It is not a final projection freshness threshold, not proof that projection is current, and not approval for degraded tariff computation.

## 9. Relationship to Assisted Payment Terminal / Continuity Terminal

Continuity Terminal is a restricted degraded/BCP mode of Assisted Payment Terminal. It is disabled by default and may be enabled only under approved Continuity activation scope for authorized terminals, users, Sites/Site Groups, and workflows.

Continuity Terminal may display backend-returned degraded context, projection freshness, payable-basis status, payment status, fiscal status, ExitAuthorization status, and escalation guidance. It must rely on Central PMS, Central PMS / Discount workflow, Payment Orchestrator, POS Server, Operator Console/governance workflows, audit, and reconciliation for their respective authority domains.

Continuity Terminal statutory discount handling is restricted to approved degraded-mode policy. If entitlement, policy basis, evidence requirements, projection freshness, or payable-basis recalculation cannot be safely validated, the flow must fail closed or route to supervisor/manual review.

The Lead must not describe Continuity Terminal as normal mode, always available, a separate product family, a local policy engine, a local fiscal authority, a local payment finality record, or an offline workflow unless a later approved policy explicitly allows a bounded exception.

## 10. Relationship to Operator Console

Operator Console is the separate non-payment governance and operations module.

It may support or display, according to role and policy:

- Continuity activation approval where policy requires.
- Continuity deactivation review.
- Incident/BCP reference entry or review.
- Continuity Terminal activation review.
- Connector health and projection freshness visibility.
- Stale projection warnings.
- Fiscal issuance exception review.
- Manual release governance.
- Post-restoration review.
- Statutory discount review through Central PMS / Discount workflow.
- Audit and reporting of operator/supervisor activity.

Operator Console must not collect payment, declare payment finality, issue Sales Invoices, mutate fiscal documents, issue ExitAuthorization, directly open gates, or become the Continuity Terminal.

## 11. Relationship to POS Server

The resolved Site POS Server remains fiscal issuance authority for Sales Invoice and fiscal records. It owns fiscal treatment, fiscal numbering, fiscal counters, fiscal reports, Electronic Journal, POSLog, fiscal audit trail, exports, and fiscal recovery posture at the fiscal-authority level.

Continuity does not create a silent alternate fiscal mode. Fiscal issuance should still route through the resolved Site POS Server where available and allowed.

Fiscal issuance must succeed before normal Central PMS ExitAuthorization unless a separately approved exception/manual-release policy applies. Fiscal issuance failure or timeout:

- Does not automatically reverse payment finality.
- Does not automatically authorize exit.
- Must enter controlled exception/retry/review posture.
- Requires clear customer/operator messaging that fiscal issuance or exit authorization is pending.

Offline fiscal issuance remains restricted/open until BIR/accounting/POS Server design approves a sequence/counter model. Unmanaged offline fiscal issuance is not approved.

## 12. Relationship to Payment Orchestrator

Payment Orchestrator performs payment provider interaction, provider abstraction, callback handling, verification posture, and verified provider outcome reporting.

Payment Orchestrator does not declare platform payment finality. Provider success or unknown provider state is evidence for Central PMS evaluation, not final platform truth.

In Continuity, unknown payment outcomes must remain pending/exception until verified through approved payment workflow and accepted by Central PMS. Payment uncertainty must fail closed for exit unless an approved exception/manual-release policy applies.

## 13. Relationship to Management Dashboard and Reporting

Management Dashboard and Reporting is visibility/reporting only.

It may show:

- Normal, degraded-watch, degraded-active, Continuity Terminal activation, restoration, post-restoration review, and reconciliation visibility.
- Connector health, HCP/Vendor PMS availability, projection freshness, stale warnings, poll latency, and mapping ambiguity where available.
- Fiscal exception backlog, manual release counts, payment uncertainty, vendor acknowledgment backlog, reconciliation status, and continuity-origin records.

It must label operational projection, financial truth, fiscal truth, audit/evidence records, reconciliation records, source category, freshness, and authority level.

Financial and revenue reporting must use canonical payment, provider, fiscal, settlement, and reconciliation records. Projection-only data must be excluded from financial truth or separately labeled as operational context.

Management Dashboard must not activate continuity, approve manual release, close reconciliation, declare payment finality, issue fiscal documents, approve discounts, mutate payable basis, issue ExitAuthorization, or open gates unless a later approved policy explicitly assigns a limited workflow action.

## 14. Continuity Operating States

Preserve these as conceptual design-level states only:

| Conceptual state | Guardrail |
| --- | --- |
| Normal | Normal authority model applies; Vendor PMS/HCP live session/tariff, Central PMS control/finality/ExitAuthorization, POS Server fiscal issuance. |
| Degraded-watch | Dependency is degraded, stale, at risk, or under observation, but continuity workflows are not active. |
| Degraded-active | Approved degraded controls are active for defined Site/Site Group/dependency scope. |
| Continuity Terminal active | Continuity Terminal mode is enabled only for authorized terminals, users, Sites/Site Groups, and workflows within activation scope. |
| Restoration-in-progress | Affected dependency is returning to service; continuity-only workflows are being disabled or limited and activity is prepared for review. |
| Post-restoration review | Continuity-origin activity is under reconciliation, audit review, exception review, and closure checks. |
| Closed / reconciled | Continuity event is closed and required reconciliation/review is complete. |

Do not convert these states into final database enum values, API statuses, event payloads, timer rules, alert thresholds, workflow transition rules, or runbook procedures.

## 15. Scope Boundaries and Deferrals

Do not finalize in the Continuity System Design:

- Exact continuity activation authority.
- Exact activation/deactivation workflow.
- Exact projection freshness thresholds.
- Exact degraded tariff basis and owner.
- Exact offline payment policy.
- Exact offline fiscal issuance policy.
- Exact manual release policy.
- Exact fiscal exception release policy.
- Exact vendor acknowledgment retry/exit-block policy.
- Exact reconciliation SLA and closure states.
- Endpoint paths.
- DTOs.
- Database tables/columns.
- Database enum values.
- Event payloads.
- Queue names.
- Retry counts.
- Timers.
- Alert thresholds.
- Implementation classes.
- UAT scripts.
- Runbook procedures.

The Lead may identify these as downstream decisions, but must not close them unless an approved source already closes them.

## 16. Risky Terminology and Misuse Cases

Flag or correct these phrases and assumptions:

| Risky phrase / assumption | Required correction |
| --- | --- |
| automatic fallback | Use explicit controlled degraded operation. |
| silent fallback | Prohibited. Continuity must be explicit and audited. |
| fallback payment without activation | Payment under degradation requires approved policy and Central PMS control. |
| projection source of truth | Projection is operational visibility and controlled degraded support only. |
| projection as tariff truth | Normal tariff truth remains Vendor PMS/HCP; degraded basis requires Central PMS policy decision. |
| projection as payment/fiscal/exit truth | Projection is never payment finality, fiscal truth, or exit authority. |
| connector approves degraded resolve | Central PMS owns degraded resolve decisioning under approved policy. |
| connector authorizes exit | ExitAuthorization is Central PMS-owned. |
| Continuity Terminal normal mode | Continuity Terminal is restricted degraded/BCP mode only. |
| Continuity Terminal always available | Disabled by default; enabled only under approved scope. |
| Operator Console collects payment | Operator Console is non-payment governance. |
| Operator Console opens gate | Operator Console must not directly open gates absent separately approved emergency process. |
| manual release equals ExitAuthorization | Manual release is last-resort governed exception, not normal ExitAuthorization. |
| fiscal failure authorizes exit | Fiscal failure blocks normal ExitAuthorization unless approved exception/manual release applies. |
| vendor paid state means ExitPass payment finality | Vendor state or acknowledgment is not Central PMS payment finality. |
| parkingfee/confirm means ExitPass payment finality | HCP confirmation is conditional vendor acknowledgment only. |
| offline fiscal issuance approved | Unmanaged offline fiscal issuance is not approved. |
| offline payment approved | Exact offline payment policy remains open. |

## 17. Required Statements for Final Design

The final Continuity System Design must include statements equivalent to:

- Continuity is explicit controlled degraded operation, not silent fallback.
- Continuity Terminal is disabled by default.
- Continuity Terminal is a restricted degraded/BCP mode of Assisted Payment Terminal.
- Central PMS owns degraded resolve decisioning under approved policy.
- Central PMS remains authority for payment-linked state, TariffSnapshot, payment finality, fiscal issuance reference recording, degraded resolve decision under approved policy, and ExitAuthorization.
- Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, and payable-basis update.
- Vendor PMS/HCP remains normal raw session lifecycle and tariff computation authority.
- Vendor PMS Connector / HikCentral Connector reports vendor facts, health, projection freshness, availability, and normalized outcomes, but does not approve degraded resolve.
- Projection is operational visibility and controlled degraded support only.
- Projection is not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- POS Server remains resolved Site fiscal issuance authority.
- Fiscal issuance failure or timeout does not automatically authorize exit.
- Manual release is last-resort governed exception, not normal ExitAuthorization.
- Gate/exit execution consumes Central PMS authorization and must not bypass Central PMS.
- Operator Console is separate non-payment governance.
- Management Dashboard is visibility/reporting only.
- Continuity-origin activity requires audit, incident, reconciliation, and post-restoration review tagging.

## 18. Open Questions to Preserve

Preserve these open questions and do not resolve them silently:

- What is the exact BCP / continuity activation authority?
- What is the exact activation/deactivation workflow?
- What is the exact projection freshness threshold?
- What exact connector health states, freshness labels, stale thresholds, and alert rules are approved?
- Who owns exact degraded tariff configuration?
- What are exact degraded tariff rounding and grace rules?
- What is the exact offline payment policy, if any?
- What is the exact offline fiscal issuance policy, if any?
- What is the exact fiscal issuance exception release policy?
- What is the exact manual release policy and emergency override boundary?
- Is vendor payment acknowledgment synchronous, asynchronous, queued/retried, exit-blocking, or Site/vendor-profile dependent?
- How should unknown vendor acknowledgment outcome be confirmed safely without duplicate vendor-side payment effects?
- What is the exact reconciliation SLA, closure authority, and closure labels?
- What exact HCP `cardNum` meaning and ticket-only fee calculation key apply in the target deployment?
- Whether HCP `parkingfee/confirm` is required before exit and what vendor state it changes.
- What exact POS Server deployment, registration, and service boundary applies?
- What exact API endpoints, DTOs, database changes, event payloads, engineering implementation, UAT scripts, and runbook procedures are required in later packs?

## 19. Summary for Lead

Use this pack as the authority and terminology guard for the future Continuity System Design.

The final design should state that Continuity is controlled degraded operation and should explain how the existing authority model survives degraded conditions. It should not invent authority, enum values, endpoint contracts, payloads, tables, timers, retry policies, UAT scripts, or runbook steps.

The safest synthesis posture is:

- Vendor PMS/HCP remains normal session and tariff authority.
- Central PMS remains platform control, degraded decision, payment finality, TariffSnapshot, fiscal reference recording, and ExitAuthorization authority.
- POS Server remains fiscal issuance authority.
- Payment Orchestrator reports verified outcomes only.
- Connectors report facts, health, projection freshness, availability, and normalized outcomes only.
- Operator Console governs and reviews without collecting payment or issuing exit authority.
- Management Dashboard reports and labels without becoming an authority surface.
- Assisted Payment Terminal supports payment-capable workflows; Continuity Terminal is its disabled-by-default restricted degraded mode.
- Projection helps visibility and controlled degraded support, but never becomes truth for money, fiscal output, discount approval, normal tariff, payment finality, or exit.
- Manual release and fiscal exceptions remain governed exceptions with incident, audit, reconciliation, and post-restoration review tags.
