# Authority Model Review Input Pack

## 1. Purpose

This input pack summarizes the approved ExitPass v1.3 authority boundaries that the System Design Lead must preserve when drafting `docs/v1.3/ExitPass_System_Design_v1.3.md`.

It is scoped to authority ownership, non-authority constraints, cross-module boundary notes, leakage risks, and required System Design statements. It does not define API endpoint paths, DTOs, database tables or columns, SQL routines, implementation classes, certificate implementation, deployment scripts, diagrams, or companion technical designs.

The v1.2 System Design was reviewed only for document posture and authority style. The relevant carry-forward posture is controlled authority separation, canonical payment finality, fail-closed control, trust-boundary discipline, and explicit workflow ownership.

## 2. Source Documents Reviewed

Approved v1.3 baseline documents reviewed:

| Source document | Sections relied on |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | 3.3, 3.4, 3.7, 7.5-7.9, 8.4-8.9, 9.5-9.15, 12.1-12.5, 13.6-13.12, 14.4, 15.1, 16.1-16.3, 17.1-17.5, 18, 19.1 |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | 1.4, 2, 7, 9.1-9.2, 11-14, 17-21, 27-29, 30 |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | 1.4-1.5, 6-12, 14-17, 21-25, 31-34 |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | 1.4-1.7, 6-7, 10-15, 18, 20-24, 31-35 |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | 1.4-1.7, 6-7, 10-13, 16-17, 20-26, 34-37 |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | 2-3, 6-18, 20-22, 25, 30-34, 37-40 |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | 1-8 |
| `docs/v1.3/system-design/ExitPass_System_Design_v1.3_Orchestration_Plan.md` | 2-9 |

Style and authority context only:

| Source document | Use |
| --- | --- |
| `D:\Docs\ExitPass\v1.2\ExitPass System Design v1.2.docx` | Controlled successor posture, section family, authority-separation tone, fail-closed language, and canonical ownership style only. No v1.2 implementation details are imported here. |

## 3. Confirmed Authority Matrix

| Authority area | Confirmed authority | Source basis |
| --- | --- | --- |
| Vendor PMS / HikCentral Professional | Authority for raw parking session lifecycle and normal tariff computation. HCP/Vendor PMS may also provide passageway or session data for projection through connector polling. | ExitPass BRD v1.3 sections 3.3, 7.5, 7.8, 9.5, 17.1.1; Continuity BRD sections 1.4, 16; POS/Invoicing BRD section 12; Approval Baseline section 4 |
| Central PMS | Authority for payment-linked platform control state, session projection/control state, TariffSnapshot/payable basis, PaymentAttempt, PaymentConfirmation/payment finality, fiscal issuance reference recording, degraded resolve decision under approved policy, and ExitAuthorization. | ExitPass BRD v1.3 sections 7.8-7.9, 9.9-9.12, 12.3-12.4, 17.1.2; Assisted Payment Terminal BRD sections 1.4, 13; Continuity BRD sections 1.4, 16, 23; POS/Invoicing BRD section 12; Approval Baseline section 4 |
| Payment Orchestrator | Authority for payment provider interaction, provider abstraction, callback handling, provider session handling where applicable, and verified provider outcome reporting to Central PMS. | ExitPass BRD v1.3 sections 3.7, 7.8-7.9, 12.1-12.4, 17.2; Continuity BRD sections 16, 23; POS/Invoicing BRD section 12 |
| POS Server | Resolved Site POS Server is fiscal issuance authority for Sales Invoice issuance, fiscal treatment, fiscal numbering/counters, X-read, Z-read, BIR Sales Summary, Electronic Journal, POSLog, fiscal reports, fiscal exports, retention, audit, and related fiscal controls. | ExitPass BRD v1.3 sections 9.8-9.10, 12.4, 17.3; POS/Invoicing BRD sections 2, 6, 9-12, 18, 20-22, 34, 40; Management Dashboard BRD sections 1.7, 16 |
| WebPay | Centralized customer payment surface using site-specific or payment-scope URLs. It participates as a payment channel and must route through Central PMS and resolved Site POS Server authority. | ExitPass BRD v1.3 sections 5.1.1, 7.10, 9.1, 18; POS/Invoicing BRD sections 11, 20.1 |
| Assisted Payment Terminal | Payment-capable terminal app family for cashier-assisted and continuity modes. It owns terminal UI, cashier workflow, terminal/device context, payable-basis display, discount input capture, payment collection flow surface, POS Server fiscal routing surface, terminal accountability, and status display. | ExitPass BRD v1.3 sections 7.7, 9.11-9.12; Assisted Payment Terminal BRD sections 2, 7, 9, 12-14, 17-19, 30; POS/Invoicing BRD sections 14, 20.3-20.4 |
| Cashier-Assisted Terminal | Normal assisted-payment mode of Assisted Payment Terminal. It may capture statutory discount validation inputs, evidence where required, cashier attestation, device/shift context, and submit them to Central PMS / Discount workflow. | ExitPass BRD v1.3 sections 8.5, 9.11-9.12, 18; Assisted Payment Terminal BRD sections 9.1, 14.1, 17, 30; POS/Invoicing BRD sections 14, 20.3 |
| Continuity Terminal | Restricted degraded/BCP mode of Assisted Payment Terminal. It may operate only under approved degraded controls and may support lookup, degraded payable-basis display, payment collection, fiscal routing, and controlled assisted/manual exit handling only where policy allows. | ExitPass BRD v1.3 sections 8.9, 9.11, 9.14, 13.9-13.10, 18; Assisted Payment Terminal BRD sections 9.2, 14.2, 20; Continuity BRD sections 1.5, 9-12, 13, 21-24 |
| Operator Console | Internal non-payment governance and operations module for review, supervision, evidence review, audit, device/shift controls, continuity activation review, fiscal exception review, manual release governance, and operational visibility. | ExitPass BRD v1.3 sections 7.6, 9.13, 14.1; Operator Console BRD sections 1.4-1.7, 6-7, 10-15, 18, 20-24, 35; POS/Invoicing BRD section 16 |
| Management Dashboard and Reporting | Visibility and reporting domain for operational, financial, fiscal, compliance, reconciliation, executive, and portfolio dashboards using authorized source records and scope-aware access. | ExitPass BRD v1.3 sections 9.15, 14.4, 18; Management Dashboard BRD sections 1.4-1.7, 6-7, 10-13, 16-17, 20-26, 37; POS/Invoicing BRD section 17 |
| Gate / exit execution | Gate/exit system executes exit only by consuming Central PMS-issued ExitAuthorization. | ExitPass BRD v1.3 sections 7.8-7.9, 9.10, 12.4, 17.4, 18; Continuity BRD sections 12.8, 16, 24; Approval Baseline section 4 |
| Discount workflow | Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, payable-basis effect, validation status, and statutory discount traceability. | ExitPass BRD v1.3 sections 7.7-7.8, 8.5, 9.12, 18; Assisted Payment Terminal BRD sections 1.4, 13, 17; Operator Console BRD section 18; POS/Invoicing BRD sections 12, 25 |
| Reconciliation / operations workflow | Operations / Reconciliation workflow owns reconciliation and post-restoration review of payment, fiscal, vendor acknowledgment, continuity-origin, manual release, and exception records. | ExitPass BRD v1.3 sections 12.5, 13.8-13.11, 14.3; Continuity BRD sections 16, 17.2, 25; Management Dashboard BRD sections 13, 17, 25; POS/Invoicing BRD sections 18, 30, 34 |

## 4. Confirmed Non-Authority Matrix

| Component or area | Confirmed non-authority constraints | Source basis |
| --- | --- | --- |
| Vendor PMS / HCP | Does not own ExitPass platform payment finality, fiscal issuance reference recording, or ExitAuthorization. During degraded mode, projection or passageway data must not become ad hoc tariff authority or financial truth. | ExitPass BRD v1.3 sections 7.5, 8.9, 9.6, 12.5, 16.2-16.3; Continuity BRD sections 7, 9, 12.1-12.2 |
| Central PMS | Does not replace Vendor PMS normal tariff authority and does not replace POS Server fiscal issuance authority. It records fiscal issuance references after POS Server issuance. | ExitPass BRD v1.3 sections 3.3, 7.8-7.9, 9.5, 9.9-9.10, 12.4; POS/Invoicing BRD sections 2, 9, 12, 22 |
| Payment Orchestrator | Does not declare platform payment finality, issue fiscal documents, issue ExitAuthorization, or open gates. | ExitPass BRD v1.3 sections 7.9, 12.1-12.4, 15.1, 18; Continuity BRD sections 12.6, 23; Approval Baseline section 4 |
| POS Server | Does not declare payment finality, issue ExitAuthorization, open gates, replace Central PMS, replace Vendor PMS normal tariff authority, approve statutory entitlement outside Central PMS / Discount workflow, or become a gate authority. | ExitPass BRD v1.3 sections 7.9, 9.9-9.10, 12.4, 17.3, 18; POS/Invoicing BRD sections 6-7, 12, 20-22, 32, 40; Approval Baseline section 4 |
| WebPay | Does not declare platform payment finality, act as fiscal authority, issue ExitAuthorization, or bypass Central PMS or resolved Site POS Server. | ExitPass BRD v1.3 sections 7.9, 12.3, 18; POS/Invoicing BRD sections 20.1, 40; Approval Baseline section 4 |
| Assisted Payment Terminal | Does not declare payment finality, issue Sales Invoices independently, issue ExitAuthorization, become an independent fiscal authority, approve statutory entitlement independently, mutate payable basis directly, or use terminal-local policy logic as authority. | ExitPass BRD v1.3 sections 7.7-7.9, 8.5, 9.11-9.12, 18; Assisted Payment Terminal BRD sections 7, 12-13, 17-19, 27-30; POS/Invoicing BRD sections 14, 20.3 |
| Cashier-Assisted Terminal | Does not own discount policy resolution, validation persistence, payable-basis update, payment finality, fiscal issuance, or ExitAuthorization. | ExitPass BRD v1.3 sections 8.5, 9.12, 18; Assisted Payment Terminal BRD sections 13, 17-19, 30; POS/Invoicing BRD sections 14, 20.3 |
| Continuity Terminal | Does not silently replace normal Vendor PMS/Central PMS authority, does not declare payment finality, does not issue ExitAuthorization, does not approve unmanaged offline discount decisions, and does not approve unmanaged offline fiscal issuance. | ExitPass BRD v1.3 sections 8.9, 9.11, 9.14, 13.9-13.10; Assisted Payment Terminal BRD sections 9.2, 20, 27, 30; Continuity BRD sections 7, 9, 13, 21-24, 31; POS/Invoicing BRD sections 15, 20.4, 31 |
| Operator Console | Does not collect payment, act as cashier payment terminal, act as POS terminal, issue Sales Invoices, mutate fiscal records, declare payment finality, manually mark payments paid, issue or consume ExitAuthorization, open gates directly, or bypass Central PMS / Discount workflow, POS Server, or continuity controls. | ExitPass BRD v1.3 sections 7.6, 9.13, 18; Operator Console BRD sections 1.4-1.7, 7, 11-14, 18, 20-24, 32, 35; POS/Invoicing BRD section 16 |
| Management Dashboard and Reporting | Does not create payment finality, issue ExitAuthorization, open gates, issue Sales Invoices, mutate fiscal documents, approve statutory discounts, apply coupons, alter payable basis, activate continuity, approve manual release, close reconciliation unless later approved, or treat projection as financial truth. | ExitPass BRD v1.3 sections 9.15, 14.4, 18; Management Dashboard BRD sections 1.4-1.7, 6-7, 12, 16, 20-26, 34, 37; POS/Invoicing BRD section 17 |
| Gate / exit execution | Does not bypass Central PMS authorization. Gate issues do not alter payment or fiscal truth. Manual emergency processes, if approved, must remain governed, tagged, auditable, and reconciled. | ExitPass BRD v1.3 sections 7.9, 13.11, 17.4, 18; Continuity BRD sections 12.8, 24; Approval Baseline section 4 |
| Projection data | Does not replace live Vendor PMS tariff calculation in normal mode, does not establish payment finality, does not authorize exit, and is not financial truth, fiscal truth, settlement truth, Sales Invoice truth, or discount approval. | ExitPass BRD v1.3 sections 7.5, 9.6, 12.5, 14.4, 16.1-16.3, 18; Continuity BRD sections 7, 9, 12.1-12.2, 31; Management Dashboard BRD sections 1.4, 7, 12-13, 20-21, 34 |

## 5. Cross-Module Boundary Notes

1. Site Group and Site must remain distinct. Site Group is customer lookup/payment scope. Site is reporting, contract, Vendor PMS mapping, POS Server, and operational boundary. The resolved Site determines POS Server routing and financial/fiscal attribution.
2. Normal mode must use live Vendor PMS/HCP fee calculation where available. Central PMS may record an immutable TariffSnapshot from the vendor fee result, but projection does not replace the normal tariff authority.
3. Payment provider outcomes are evidence inputs, not platform finality. Payment Orchestrator reports verified provider outcomes; Central PMS applies platform validation and records PaymentConfirmation/payment finality.
4. Fiscal issuance is a separate authority step after Central PMS payment finality. POS Server issues the Sales Invoice for the resolved Site, returns fiscal identity/status, and Central PMS records the fiscal issuance reference.
5. Normal ExitAuthorization is downstream of payment finality and successful fiscal issuance. Fiscal issuance must succeed before normal ExitAuthorization unless a separately approved supervisor-controlled exception or manual-release policy applies.
6. Cashier-Assisted Terminal and Operator Console may both interact with statutory discount cases, but they have different boundaries. Cashier-Assisted Terminal captures cashier-facing inputs; Operator Console supports review/governance; Central PMS / Discount workflow owns policy resolution and persistence.
7. Continuity Terminal is not a separate product family. It is a restricted mode of Assisted Payment Terminal, disabled by default, activated only under approved degraded/BCP controls, and subject to incident, audit, reconciliation, and post-restoration review.
8. Operator Console may govern continuity activation, fiscal exceptions, manual release, evidence review, and post-restoration review, but this governance surface must not become payment collection, fiscal issuance, finality declaration, ExitAuthorization issuance, or direct gate operation.
9. Management Dashboard and Reporting may aggregate operational, financial, fiscal, audit, and reconciliation views, but every metric must preserve source labels and authority. Operational projection visibility must be clearly separated from canonical payment/fiscal/reconciliation truth.
10. Reconciliation should bridge canonical payment records, fiscal records, fiscal issuance references, provider outcomes, vendor acknowledgment status, continuity-origin records, and manual-release records. It must not convert projection-only or governance-only data into financial truth.

## 6. Authority Leakage Risks

| Risk | Why it matters | Required guardrail in System Design |
| --- | --- | --- |
| Projection treated as tariff, payment, fiscal, or exit truth | Projection is intended for visibility and controlled degraded support only. Misuse could produce incorrect charges, revenue reports, discount decisions, or exits. | Classify projection as operational visibility data and degraded-mode support only. Require source/freshness labels and fail-closed behavior when stale, ambiguous, or insufficient. |
| Provider success treated as platform payment finality | Provider outcomes must be verified and accepted by Central PMS before platform finality. | State that Payment Orchestrator reports verified provider outcomes but Central PMS alone records platform payment finality. |
| POS Server treated as exit authority | Fiscal issuance and ExitAuthorization are separate authority domains. | State that POS Server issues Sales Invoices and fiscal records only; Central PMS issues ExitAuthorization. |
| WebPay or terminal surfaces treated as authority | Customer and cashier surfaces may initiate flows and display statuses but must not own finality, fiscal issuance, or exit authorization. | Model WebPay, Assisted Payment Terminal, Cashier-Assisted Terminal, and Continuity Terminal as channels/surfaces under Central PMS and resolved Site POS Server authority. |
| Cashier discount capture treated as discount approval | Cashiers capture inputs; they do not own policy resolution or payable-basis mutation. | Route statutory discount validation through Central PMS / Discount workflow and block payment on unapproved discounted payable basis. |
| Operator Console becomes payment or gate tool | Console is governance/review, not payment collection or physical gate execution. | Keep Operator Console non-payment and non-gate. Manual-release governance must remain distinct from normal ExitAuthorization. |
| Dashboard becomes operational workflow authority | Reporting views could be misused to mark payments, close reconciliation, approve discounts, or activate continuity. | Restrict Management Dashboard to visibility/reporting unless a later approved policy explicitly assigns workflow actions. |
| Continuity becomes silent alternate normal mode | Degraded operations can bypass normal controls if not explicit and bounded. | Require explicit activation/recognition, approved scope, supervisor approval where policy requires, incident/audit/reconciliation tags, and post-restoration review. |
| Fiscal issuance exception releases become normal | Paid-but-not-fiscally-issued exits create compliance and reconciliation exposure. | Require fiscal issuance success before normal ExitAuthorization; any exception/manual release must be separately approved, supervisor-controlled, tagged, auditable, and reconciled. |
| Site Group/Site confusion | Incorrect Site resolution can misroute vendor calls, POS Server fiscal issuance, reporting, reconciliation, and operational ownership. | Preserve Site Group as lookup/payment scope and Site as reporting/vendor/POS/operations boundary. Use resolved Site for POS routing and financial/fiscal attribution. |

## 7. Required System Design Statements

The System Design v1.3 should include these explicit statements or equivalent controlled language:

1. Vendor PMS / HCP remains the authority for raw parking session lifecycle and normal tariff computation.
2. Central PMS remains the authority for payment-linked platform control state, payment finality, fiscal issuance reference recording, degraded resolve decisions under approved policy, and ExitAuthorization.
3. Payment Orchestrator may report verified provider outcomes but does not declare platform payment finality.
4. POS Server remains fiscal issuance authority for the resolved Site and does not issue ExitAuthorization.
5. WebPay is a customer payment surface and does not declare platform payment finality.
6. Assisted Payment Terminal is payment-capable but does not declare payment finality, issue Sales Invoices independently, or issue ExitAuthorization.
7. Cashier-Assisted Terminal may capture statutory discount validation inputs, but Central PMS / Discount workflow owns policy resolution, validation persistence, and payable-basis update.
8. Continuity Terminal is restricted degraded/BCP mode and is disabled by default.
9. Operator Console is non-payment governance and does not collect payment, issue fiscal documents, declare finality, issue ExitAuthorization, or open gates.
10. Management Dashboard and Reporting is visibility/reporting only and must not become payment, fiscal, tariff, discount, continuity activation, reconciliation closure, or exit authority unless later approved policy explicitly assigns a limited workflow action.
11. Projection data is operational visibility only and not financial truth.
12. Gate/exit execution must consume Central PMS authorization and must not bypass Central PMS.
13. Fiscal issuance must succeed before normal ExitAuthorization unless an approved exception policy applies.
14. Payment channels and terminals are channels under Central PMS payment authority and resolved Site POS Server fiscal authority; they are not independent fiscal authorities.
15. Financial and revenue reporting must use canonical payment, provider, fiscal, fiscal reference, and reconciliation records, not projection-only data.
16. Manual release, if allowed, must be supervisor-approved where policy requires, incident-tagged, audit-tagged, reconciliation-tagged, and subject to review; it must not silently become normal payment finality or normal ExitAuthorization.

## 8. Contradictions, Ambiguities, or Issues Found

No direct contradiction was found in the approved authority model. The reviewed BRDs consistently preserve the same authority boundaries.

Open or ambiguous items that the System Design Lead should carry forward without resolving by invention:

| Item | Source basis | System Design handling |
| --- | --- | --- |
| Exact Continuity/BCP activation authority remains open. | ExitPass BRD v1.3 section 19.1; Continuity BRD sections 11 and 33; Operator Console BRD section 34; Approval Baseline section 5 | Preserve the requirement for approved authority and supervisor approval where policy requires. Do not name a final role or workflow unless separately approved. |
| Exact degraded projection/tariff freshness thresholds remain open. | ExitPass BRD v1.3 sections 8.9 and 19.1; Continuity BRD sections 11, 12.2, 33; Management Dashboard BRD section 36 | Require freshness controls and fail-closed posture. Do not invent threshold values. |
| Exact POS Server deployment, registration, and service/module boundary remains open. | ExitPass BRD v1.3 section 19.1; POS/Invoicing BRD sections 10 and 39; Approval Baseline section 5 | Preserve Site-level fiscal authority and resolved Site routing without deciding implementation packaging. |
| Exact BIR/accounting/accreditation details remain open. | POS/Invoicing BRD sections 10, 21, 25, 31, 33, 39; Approval Baseline sections 2 and 5 | Carry as downstream finance/accounting/BIR confirmation items. Do not invent fiscal identity assignments, numbering, or offline issuance mechanics. |
| Exact manual release and fiscal exception release policy remains open. | ExitPass BRD v1.3 sections 13.6-13.11; Continuity BRD sections 12.5, 24, 33; Operator Console BRD sections 24 and 34 | Preserve supervisor-controlled, incident/audit/reconciliation-tagged exception posture. Do not normalize bypass behavior. |
| Exact WebPay public URL slug registry and Site Group/Site resolution detail remains open. | ExitPass BRD v1.3 sections 7.10 and 19.1 | Preserve Site Group as lookup/payment scope and Site as resolved reporting/vendor/POS/operations boundary. Do not invent URL paths. |
| Exact dashboard/reporting implementation details remain open. | ExitPass BRD v1.3 section 19.1; Management Dashboard BRD section 36; Approval Baseline section 5 | Preserve source labeling and authority separation. Do not invent reporting stores, table sources, or BI technology. |
| Exact API endpoint paths, DTOs, database deltas, and implementation details remain open. | Orchestration Plan sections 5 and 8; ExitPass BRD v1.3 sections 5.2.3, 19.1, 19.5; Approval Baseline sections 5 and 7 | Keep System Design at system-design level and route details to later API, database, and engineering packs. |

## 9. Recommended ExitPass System Design v1.3 Sections Affected

Based on the orchestration plan's v1.2 outline baseline, the System Design Lead should reflect this authority model in these sections:

| System Design section family | Authority model content to include |
| --- | --- |
| Document Control | State the approved v1.3 BRD baseline and preserve open downstream confirmation items. |
| System Overview | Summarize Central PMS, Vendor PMS/HCP, Payment Orchestrator, Site POS Server, WebPay, Assisted Payment Terminal, Operator Console, Continuity, Management Dashboard, and gate/exit roles. |
| System Context | Show modules as authority-separated participants and distinguish customer/cashier/operator/reporting surfaces from authority-owning backend systems. |
| System Architecture | Include a canonical authority matrix and non-authority invariants for payment finality, fiscal issuance, ExitAuthorization, and projection visibility. |
| Trust Boundaries | Classify WebPay, terminals, Operator Console, Management Dashboard, payment providers, POS Server, Vendor PMS/HCP, and gates by trust and authority boundary. |
| Core Workflows | Preserve normal payment-to-exit choreography: live vendor resolve, TariffSnapshot, verified provider outcome, Central PMS finality, POS Server fiscal issuance, Central PMS fiscal reference recording, ExitAuthorization, gate consumption. |
| Event Architecture | Events should report completed facts owned by their source authority and must not transfer authority to consumers. Avoid event names or payload details unless separately approved. |
| State Machines | Payment finality, fiscal issuance pending/issued/failed, ExitAuthorization, continuity activation, manual release, and reconciliation states must have single owning authorities. Avoid database enum names. |
| Data Architecture | Separate operational projection records from canonical payment, fiscal, and reconciliation records. Do not invent tables or columns. |
| API Architecture | State boundary responsibilities without endpoint paths or DTOs. |
| Security Architecture | Preserve RBAC, device trust, evidence/privacy, segregation of duties, and non-payment console boundaries. |
| Failure Mode Architecture | Carry fail-closed posture for uncertain tariff, stale projection, unknown payment outcome, fiscal issuance failure/timeout, continuity activation, manual release, and gate issues. |
| Observability | Distinguish operational visibility metrics from financial truth; include connector health/projection freshness labeling and fiscal exception visibility. |
| Business Continuity | Preserve explicit degraded/BCP activation, disabled-by-default Continuity Terminal, controlled fiscal/payment/exit handling, and mandatory post-restoration reconciliation. |
| Operational Runbooks | Include governance notes for fiscal exception, manual release, continuity activation/deactivation, post-restoration review, and reconciliation handoff without prescribing unapproved implementation details. |
| Appendix | Include glossary-level definitions for Site Group, Site, Vendor PMS/HCP, Central PMS, Payment Orchestrator, POS Server, WebPay, Assisted Payment Terminal, Cashier-Assisted Terminal, Continuity Terminal, Operator Console, Management Dashboard, ExitAuthorization, TariffSnapshot, and projection. |

## 10. Summary for System Design Lead

The approved v1.3 authority model is consistent across the core BRD, companion BRDs, and approval baseline.

The System Design should preserve the following core chain:

1. Vendor PMS/HCP owns normal session lifecycle and normal tariff computation.
2. Central PMS resolves platform control state and records TariffSnapshot/payment state.
3. Payment Orchestrator reports verified provider outcomes only.
4. Central PMS declares platform payment finality.
5. Resolved Site POS Server issues the Sales Invoice and owns fiscal records.
6. Central PMS records fiscal issuance reference.
7. Central PMS issues ExitAuthorization if eligible.
8. Gate/exit execution consumes Central PMS authorization.

The main drafting risk is authority leakage through convenience surfaces: WebPay, Assisted Payment Terminal, Cashier-Assisted Terminal, Continuity Terminal, Operator Console, and Management Dashboard must remain channels, workflow surfaces, governance surfaces, or reporting surfaces, not authority owners for payment finality, fiscal issuance, discount approval, ExitAuthorization, or gate execution.

Projection must remain operational visibility and controlled degraded support only. Fiscal issuance must succeed before normal ExitAuthorization unless a separately approved exception policy applies. Continuity must remain explicit, disabled by default at the terminal level, controlled, audited, reconciliation-tagged, and subject to post-restoration review.
