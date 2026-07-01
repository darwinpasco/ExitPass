# ExitPass System Design v1.3 Input Pack 07: Scope Guard and Consistency Review

## 1. Purpose

This input pack gives the System Design Lead a guardrail review for ExitPass System Design v1.3. It is intended to prevent terminology drift, authority leakage, premature API/database/engineering detail, and contradictions against the approved v1.3 BRD baseline.

This input pack does not draft final System Design content. It identifies approved language, risky language, required boundaries, deferment rules, contradiction watches, and final review checks.

System Design v1.3 should remain a controlled successor to ExitPass System Design v1.2, preserving the v1.2 posture, section order, and engineering tone while updating authority, Site/Site Group, connector, POS/Invoicing, Continuity, Operator Console, Assisted Payment Terminal, and reporting concepts to the approved v1.3 baseline.

## 2. Source Documents Reviewed

Primary approved v1.3 sources reviewed:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
- `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md`
- `docs/v1.3/ExitPass_v1.3_Open_Questions.md`
- `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md`
- `docs/v1.3/system-design/ExitPass_System_Design_v1.3_Orchestration_Plan.md`

Style and scope baseline reviewed:

- `D:\Docs\ExitPass\v1.2\ExitPass System Design v1.2.docx`

Most relevant source sections:

- ExitPass BRD v1.3: Sections 3.4, 3.5, 5.1, 5.2, 7.2 to 7.9, 8.4, 8.9, 9.1 to 9.15, 10.5 to 10.12, 11, 12, 13, 14, 15, 16, 17, 18, 19, Appendix A.
- Assisted Payment Terminal BRD: Sections 6, 7, 9, 10 to 13, 18 to 20, 21, 22, 26 to 30, Appendix A.
- Continuity BRD: Sections 6, 7, 9, 10, 12, 13 to 16, 21 to 24, 26 to 34, Appendix A.
- Operator Console BRD: Sections 6, 7, 10 to 14, 17, 21 to 25, 31 to 36, Appendix A.
- Management Dashboard and Reporting BRD: Sections 6, 7, 10 to 17, 21 to 31, 33 to 37, Appendix A.
- POS/Invoicing BRD: Sections 6, 7, 10 to 22, 30 to 39, 41, Appendix A.
- Approval Baseline: Sections 4, 5, 7, 8.
- Documentation Decision Log: Authority Model, Product and Site Model, Vendor PMS and Connector, Resolve Mode, POS/Fiscal/Payment Channel, Platform Module, Writing Order, Non-Decisions.
- Open Questions: V13-Q001 to V13-Q014.
- Orchestration Plan: Sections 2 to 9.

## 3. Approved Terminology List

| Term | Approved meaning / usage guard |
| --- | --- |
| ExitPass v1.3 | Minor controlled update to v1.2, not a v2.0 redesign. |
| Central PMS | Platform control authority for payment-linked state, payment finality, fiscal issuance reference recording, ParkingSession projection, TariffSnapshot, PaymentAttempt, PaymentConfirmation, and ExitAuthorization. |
| Vendor PMS / HCP | Authority for raw parking session lifecycle and tariff computation in normal mode. HCP may also provide passageway records for operational projection. |
| Site Group | Customer lookup/payment scope. Default case is one Site Group to one Site; special case is one Site Group containing multiple Sites. |
| Site | Reporting, contract, Vendor PMS mapping, POS Server, fiscal routing, and operational boundary. |
| Physical parking lot | Physical or operational facility concept only. A physical lot is not automatically an ExitPass Site. |
| VendorSystem | Configured Vendor PMS/HCP instance. It is separate from Site, connector instance, and adapter codebase. |
| AdapterMapping | Mapping between an ExitPass Site and a vendor-side parking object. |
| Adapter codebase | Reusable vendor integration implementation, such as a HikCentral adapter. |
| Connector instance | Deployed/configured runtime connector for a specific Vendor PMS/HCP instance. Each Vendor PMS/HCP instance should have a connector instance. |
| HCP ParkingLotIndexCode | Vendor-side identifier only. It must not be treated as ExitPass `site_id`. |
| Runtime vendor object identity | Use the approved compound identity concept: `vendorSystemId + vendorObjectType + vendorObjectRef`. Do not convert this into final API or database design in the SDD. |
| Parking Session Projection | Central PMS operational projection of vendor session data. It supports visibility and controlled degraded operation only. |
| TariffSnapshot | Immutable fee result recorded by Central PMS from live vendor calculation or approved degraded tariff computation. |
| Payment Orchestrator | Payment-provider interaction component that reports verified provider outcomes. It does not declare platform payment finality. |
| PaymentConfirmation / payment finality | Central PMS-owned platform finality after verified provider outcome is accepted and recorded. |
| POS Server / Site POS Server | Site-level fiscal issuance authority. It issues Sales Invoices and owns fiscal reports, counters, EJ, POSLog, audit, retention, and export for the resolved Site. |
| Sales Invoice / SI | Primary parking fiscal output for v1.3. |
| ExitAuthorization | Central PMS-issued authorization for gate/exit execution. |
| WebPay | Centralized customer payment surface with site-specific/payment-scope URLs. It is not payment finality authority, fiscal authority, or exit authority. |
| Assisted Payment Terminal | Terminal app family supporting Cashier-Assisted Terminal mode and Continuity Terminal mode. It is payment-capable but not finality, exit, fiscal, or discount-policy authority. |
| Cashier-Assisted Terminal | Assisted Payment Terminal mode for normal cashier-assisted workflows, including statutory discount validation capture where policy allows. |
| Continuity Terminal | Restricted degraded/BCP mode of Assisted Payment Terminal, disabled by default and activated only under approved degraded/BCP controls. |
| Operator Console | Internal non-payment governance and operations module. It may display and supervise approved workflows but must not collect payment or issue finality, Sales Invoice, ExitAuthorization, or gate commands. |
| Management Dashboard and Reporting | Visibility/reporting module only. It consumes authorized operational, financial, fiscal, audit, and reconciliation records but does not become an authority workflow. |
| Operational visibility | Projection, connector health, freshness, occupancy approximation, exception visibility, and other operational facts. It is not financial truth. |
| Financial truth | Canonical payment, provider, fiscal, settlement, and reconciliation records. Projection data is excluded. |
| Continuity | Explicit, controlled, audited, reconciliation-tagged, time-bound degraded operation. It is not silent fallback. |

## 4. Deprecated or Risky Terminology

| Risky term or phrase | Why risky | Required treatment |
| --- | --- | --- |
| `EC Device` | Historical/planning term can blur Continuity Terminal and Assisted Payment Terminal mode boundaries. | Use `Continuity Terminal` or `Assisted Payment Terminal` mode unless explicitly referencing historical source terminology. |
| `Cashier POS` | Can imply a separate POS authority or cashier-owned fiscal/payment authority. | Use `Cashier-Assisted Terminal` where the intended meaning is assisted terminal workflow. |
| `OR` / `Official Receipt` as primary parking fiscal output | POS/Invoicing v1.3 approves `Sales Invoice` as the primary parking fiscal output. | Use `Sales Invoice`, `SI`, or `Sales Invoice Number`; mention OR only as historical or future-accounting-decision terminology. |
| `parking lot` when ExitPass `Site` is intended | Physical parking lots are not automatically ExitPass Sites. | Use `ExitPass Site` for the operational/vendor/POS/reporting boundary; use `physical parking lot` only for facility-level context. |
| `HCP site` | Can confuse Vendor PMS parking objects, HikCentral parking lot index, and ExitPass Site. | Use `Vendor PMS/HCP parking object`, `HCP ParkingLotIndexCode`, or `ExitPass Site` precisely. |
| `projection as source of truth` | Projection is operational visibility and degraded support, not financial truth, fee truth, payment finality, or exit authority. | State source labels and freshness; use canonical payment/fiscal records for financial truth. |
| `payment success equals exit authorization` | Violates the required chain: verified payment, Central PMS finality, POS fiscal issuance, Central PMS ExitAuthorization. | Always include fiscal issuance and Central PMS authorization prerequisites. |
| `payment completed by WebPay` | WebPay is a channel, not finality authority. | Say WebPay initiates or presents the payment flow; Central PMS records finality after verified outcome. |
| `POS Server confirms payment` | POS Server owns fiscal issuance, not payment finality. | Say POS Server issues fiscal documents after Central PMS payment finality. |
| `Operator Console override payment` | Operator Console is non-payment governance. | Limit to review, approval capture, evidence, incident/audit/reconciliation tagging, and read-only status display. |
| `Dashboard reconciliation truth` | Dashboard may display or summarize reconciliation, but does not become the canonical record or authority unless future policy says so. | Label reports by source, freshness, and authority level. |
| `automatic fallback` / `silent fallback` | Continuity must be explicit, approved, audited, tagged, and time-bound. | Use `controlled degraded operation`, `degraded-watch`, `degraded-active`, or `Continuity Terminal active` where applicable. |
| `offline fiscal issuance approved` | POS/Invoicing keeps offline fiscal issuance restricted/open pending BIR/accounting and POS Server design approval. | Keep offline issuance as deferred/open unless an approved later artifact closes it. |
| `BIR approved` | BRD approval is documentation baseline approval, not BIR accreditation or accounting/tax approval. | Use `BIR/accounting confirmation pending` where applicable. |

## 5. Scope Boundaries for ExitPass System Design v1.3

System Design v1.3 should cover the system-level architecture and control model needed to translate the approved BRD baseline into a coherent successor to v1.2. It should remain at the level of architecture, runtime components, authority boundaries, trust boundaries, workflows, states, events, failure modes, data architecture posture, API architecture posture, security posture, deployment posture, observability, business continuity, and operational posture.

The SDD should explicitly preserve these authority boundaries:

- Vendor PMS / HCP remains normal raw session lifecycle and tariff authority.
- Central PMS remains payment finality and ExitAuthorization authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform payment finality.
- POS Server remains Site-level fiscal issuance authority and does not declare payment finality or issue ExitAuthorization.
- WebPay remains a centralized customer payment surface and does not declare payment finality, issue fiscal documents, or issue ExitAuthorization.
- Assisted Payment Terminal is payment-capable but not payment finality, exit, fiscal, or statutory-discount-policy authority.
- Continuity Terminal is a restricted degraded/BCP mode of Assisted Payment Terminal, disabled by default.
- Operator Console remains non-payment governance and operations tooling.
- Management Dashboard remains visibility/reporting only and must distinguish projection visibility from canonical financial/fiscal records.
- Gate/exit execution consumes Central PMS ExitAuthorization and does not bypass Central PMS except under a formally approved manual emergency process.

The SDD should preserve the v1.2 top-level outline unless a v1.3 requirement justifies a controlled addition:

- Document Control
- System Overview
- System Context
- System Architecture
- Trust Boundaries
- Core Workflows
- Event Architecture
- State Machines
- Data Architecture
- API Architecture
- Security Architecture
- Failure Mode Architecture
- Deployment Architecture
- Observability
- Business Continuity
- Operational Runbooks
- Appendix

The SDD should describe data and API architecture at system-design level only. It may name ownership, sequencing, trust boundaries, logical records, and conformance rules. It must not freeze final endpoint paths, DTOs, table/column names, SQL routines, implementation classes, device SDK details, or deployment scripts.

## 6. Items That Must Not Be Included in System Design v1.3

The final SDD must not include:

- New product decisions not present in the approved BRDs or decision log.
- Changes to approved BRD authority boundaries.
- Final API endpoint paths.
- Final DTO schemas or payload field lists.
- Final database tables, columns, indexes, constraints, SQL routines, migrations, or DDL.
- Final implementation classes, package structures, service code layout, SDK calls, native bridge details, printer commands, or device implementation mechanics.
- BIR accreditation submission content or final accreditation sample package.
- Final accounting/tax treatment decisions.
- Final Sales Invoice numbering pattern, taxpayer/Site/branch identity assignment, MIN/PTU/serial/software/supplier assignment, or offline fiscal issuance sequence/counter model.
- Dashboard wireframes, BI implementation specifications, reporting database schema, or dashboard-as-authority workflows.
- UAT scripts, detailed test cases, Test/UAT Pack content, or acceptance-test procedure detail.
- Operations Runbook Pack procedures beyond high-level operational posture inherited from v1.2.
- New diagrams or modified diagram source/content created by the System Design Lead unless separately assigned and approved.
- Any language that says payment provider success, WebPay success, APM success, or terminal payment success directly authorizes exit.
- Any language that treats projection data, HCP passageway records, occupancy estimates, or connector health as financial truth, tariff truth, payment finality, fiscal truth, discount approval, or exit authority.
- Any language that makes Continuity a silent fallback or default alternate operating mode.

## 7. Items That Must Be Deferred to Companion Technical Designs

Defer the following to companion technical designs or module-specific system designs:

- POS Server System Design: fiscal architecture, fiscal state model, fiscal counters, recovery/failover, fiscal export mechanics, fiscal report generation mechanics, POS Server deployment model, and POS Server service packaging.
- POS Server API Contract: final POS Server endpoint ownership, endpoint names, request/response DTOs, error structures, and service boundary details.
- Vendor PMS Connector System Design: connector topology, push/pull behavior, connector runtime lifecycle, polling implementation, retry behavior, vendor acknowledgment mechanics, security model, and connector operational model.
- HikCentral Connector Profile: HCP-specific object mappings, one-minute polling profile details, ParkingLotIndexCode handling, passageway record semantics, vendor object references, health/freshness metrics, and stale behavior.
- Assisted Payment Terminal System Design: terminal app architecture, supported deployment variants, device trust model, local storage posture, printer/QR presentation implementation, terminal UX flows, and device SDK integration.
- Continuity System Design: activation/deactivation workflow implementation, degraded state model, exact freshness thresholds, approval workflow implementation, degraded tariff computation design, post-restoration reconciliation mechanics, and manual emergency process mechanics.
- Management Dashboard / Reporting Technical Design: reporting data model, aggregation mechanics, source labeling implementation, export controls, BI tooling, dashboard layouts, and report delivery implementation.
- Operator Console Technical Design: console implementation, evidence access implementation, workflow screens, approval capture mechanics, device/shift controls, and manual release governance implementation.

## 8. Items That Must Be Deferred to Database/API/Engineering Pack

Defer the following to the Database Design / Database Delta, API Contract Pack, Engineering Pack, Test/UAT Pack, or Runbook Pack:

- Exact database deltas for Site Group, Site, VendorSystem, AdapterMapping, connector instance, projection, fiscal references, continuity incidents, reporting records, or audit records.
- Exact table names, column names, indexes, foreign keys, constraints, stored functions, triggers, outbox tables, and migration scripts.
- Exact API endpoint paths, HTTP verbs, route hierarchy, query parameters, DTO fields, status codes, error codes, idempotency keys, and event payload schemas.
- Exact service boundaries where open questions remain, including whether POS Server is a Central PMS module or separate service.
- Exact WebPay URL slug registry structure and whether slugs resolve to Site Group, Site, or both.
- Exact HCP connector push/pull topology.
- Exact vendor payment acknowledgment sequencing, queueing, retry, and escalation mechanics.
- Exact connector health and projection freshness state modeling.
- Exact event catalog revisions, queue/exchange topology, topic names, and delivery semantics.
- Exact RBAC permission matrix and role-to-action mapping.
- Exact observability instrumentation fields, metric names, log schemas, alert thresholds, and dashboard implementation.
- Exact deployment topology, infrastructure manifests, environment variables, secrets distribution, scaling rules, and failover scripts.
- Exact test cases, UAT scenarios, regression scripts, and operational runbook steps.

## 9. Potential Contradictions to Watch

No direct contradiction was found that blocks System Design drafting, but these are high-risk contradiction points the System Design Lead must watch:

| Watch area | Risk | Required consistency stance |
| --- | --- | --- |
| Site Group vs Site | Lookup/payment scope may be confused with operational/vendor/POS/reporting boundary. | Site Group is customer lookup/payment scope. Site is reporting, contract, Vendor PMS mapping, POS Server, and operational boundary. |
| Physical parking lot vs ExitPass Site | Physical lots or clusters may be modeled as first-class Sites without approval. | Physical lots are not automatically ExitPass Sites; first-class modeling remains open. |
| VendorSystem vs connector instance vs adapter codebase | Design may collapse configured vendor instance, deployed runtime connector, and reusable implementation into one concept. | Keep all three distinct. VendorSystem is a Vendor PMS/HCP instance; connector instance is runtime deployment/configuration; adapter codebase is reusable implementation. |
| HCP ParkingLotIndexCode | HCP lot code may be reused as ExitPass `site_id`. | Prohibit reuse as ExitPass `site_id`; use AdapterMapping and runtime vendor object identity. |
| Projection data | Projection may be treated as tariff, payment, fiscal, or financial truth. | Projection is operational visibility and controlled degraded support only. |
| Vendor PMS / HCP authority | SDD may imply Central PMS normally calculates tariff or owns raw lifecycle. | Vendor PMS/HCP remains normal raw session lifecycle and tariff authority. |
| Central PMS authority | Payment Orchestrator, WebPay, POS Server, APT, dashboard, or Operator Console may be described as declaring finality/exit. | Central PMS owns platform payment finality and ExitAuthorization. |
| Payment Orchestrator | Verified provider outcome may be equated with platform finality. | Payment Orchestrator reports verified outcomes only; Central PMS records finality. |
| POS Server | Fiscal authority may drift into payment finality or exit authority. | POS Server issues Sales Invoice and fiscal records only; it does not own payment finality or ExitAuthorization. |
| WebPay | Customer-facing success may be equated with finality or exit authorization. | WebPay is a channel. Finality and ExitAuthorization remain Central PMS decisions. |
| Assisted Payment Terminal | Payment-capable terminal may be treated as an independent POS, discount authority, or exit authority. | APT captures and presents approved workflows only; it relies on Central PMS, POS Server, and approved backend workflows. |
| Continuity Terminal | Continuity mode may be described as automatic fallback or normal alternate payment path. | Continuity Terminal is disabled by default and restricted to approved degraded/BCP controls. |
| Operator Console | Governance UI may drift into payment collection, fiscal mutation, or gate control. | Operator Console is non-payment governance and review only. |
| Management Dashboard | Reports may become workflow authority or financial source of truth. | Dashboard is visibility/reporting only and labels source/freshness/authority. |
| Fiscal issuance sequence | ExitAuthorization may appear before Sales Invoice issuance or fiscal reference recording. | Normal ExitAuthorization requires successful fiscal issuance and Central PMS fiscal reference recording unless a separately approved exception policy applies. |
| Manual release | Manual release may be normalized as payment finality or normal ExitAuthorization. | Manual release is last resort, supervisor/policy controlled, incident-tagged, audit-tagged, and reconciliation-tagged. |
| BRD approval meaning | Approval may be misread as closing BIR/accounting/API/database/engineering questions. | BRD approval is documentation baseline approval only. Preserve downstream open items. |
| v1.2 SDD style | v1.2 detail level may tempt premature v1.3 API/database/engineering lock-in. | Preserve v1.2 outline and authority tone, but defer unresolved v1.3 specifics to companion packs. |

## 10. Required Consistency Checks

The System Design Lead should run these consistency checks before finalizing the SDD:

1. Search for `Site Group`, `Site`, `parking lot`, `physical lot`, `HCP site`, `ParkingLotIndexCode`, and confirm each usage preserves the approved semantic boundary.
2. Search for `VendorSystem`, `Vendor System`, `connector instance`, `adapter`, `AdapterMapping`, and confirm configured vendor instance, runtime connector, reusable adapter, and Site mapping are not collapsed.
3. Search for `projection`, `passageway`, `occupancy`, `freshness`, and confirm projection is never described as financial truth, tariff truth, payment finality, fiscal truth, discount approval, or exit authority.
4. Search for `payment finality`, `PaymentConfirmation`, `ProviderOutcome`, `Payment Orchestrator`, and confirm finality is recorded only by Central PMS after verified provider outcome.
5. Search for `WebPay`, `APM`, `Assisted Payment Terminal`, `Cashier-Assisted Terminal`, `Continuity Terminal`, and confirm channels/terminals do not declare platform finality, fiscal authority, or ExitAuthorization.
6. Search for `POS Server`, `Sales Invoice`, `fiscal`, `ExitAuthorization`, and confirm POS Server is fiscal authority only and fiscal issuance precedes normal ExitAuthorization.
7. Search for `Operator Console`, `manual release`, `override`, `gate`, and confirm Operator Console remains non-payment governance and does not directly open gates unless a future approved System Design changes the boundary.
8. Search for `Management Dashboard`, `report`, `financial`, `reconciliation`, and confirm dashboards consume/layer/report on records without becoming canonical authority.
9. Search for `Continuity`, `fallback`, `degraded`, `BCP`, and confirm continuity is explicit, controlled, audited, reconciliation-tagged, time-bound, and disabled by default where terminal mode is involved.
10. Search for `OR`, `Official Receipt`, `Sales Invoice`, `SI`, and confirm Sales Invoice is the primary parking fiscal output.
11. Search for `endpoint`, `DTO`, `table`, `column`, `schema`, `class`, `implementation`, `SDK`, and confirm final technical details are deferred unless already approved at system-design posture only.
12. Search for open-question topics from V13-Q001 to V13-Q014 and confirm the SDD preserves unresolved status instead of silently resolving them.

## 11. Recommended Final SDD Review Checklist

- [ ] The SDD cites the approved v1.3 BRD baseline and treats the BRDs as governing business intent.
- [ ] The SDD preserves the v1.2 top-level outline and controlled-successor tone.
- [ ] Site Group is consistently lookup/payment scope.
- [ ] Site is consistently reporting, contract, Vendor PMS mapping, POS Server, and operational boundary.
- [ ] Physical parking lot language does not create unintended Site semantics.
- [ ] HCP ParkingLotIndexCode is never treated as ExitPass `site_id`.
- [ ] VendorSystem, AdapterMapping, connector instance, and adapter codebase remain distinct.
- [ ] Projection data is always labeled as operational visibility or controlled degraded support, not financial truth.
- [ ] Vendor PMS/HCP remains normal raw session lifecycle and tariff authority.
- [ ] Central PMS remains payment finality and ExitAuthorization authority.
- [ ] Payment Orchestrator only reports verified provider outcomes.
- [ ] WebPay, APM, APT, Cashier-Assisted Terminal, and Continuity Terminal are channels/terminals, not finality/fiscal/exit authorities.
- [ ] POS Server is fiscal issuance authority only and does not issue ExitAuthorization.
- [ ] Sales Invoice is the primary parking fiscal output.
- [ ] Normal fiscal issuance succeeds before Central PMS issues ExitAuthorization.
- [ ] Fiscal issuance failure or timeout starts a controlled exception workflow and does not imply exit is authorized.
- [ ] Operator Console remains non-payment governance and operations tooling.
- [ ] Management Dashboard remains visibility/reporting only and uses source/freshness/authority labels.
- [ ] Continuity is not silent fallback and Continuity Terminal remains disabled by default.
- [ ] Manual release is last resort and incident/audit/reconciliation tagged.
- [ ] BIR/accounting/API/database/engineering open questions remain open unless an approved later source closes them.
- [ ] The SDD does not become an API Contract, Database Design, Engineering Pack, Test/UAT Pack, runbook pack, or BIR accreditation submission pack.
- [ ] No endpoint paths, DTOs, final table/column names, implementation classes, or device SDK mechanics are introduced as final design.
- [ ] Any unresolved contradiction is carried forward as an open item or review note, not silently corrected.

## 12. Summary for System Design Lead

System Design v1.3 should be a controlled successor to the v1.2 SDD using the approved v1.3 BRDs as the business baseline. The most important guardrail is authority separation: Vendor PMS/HCP owns normal raw session and tariff authority; Central PMS owns payment finality and ExitAuthorization; POS Server owns fiscal issuance; Payment Orchestrator, WebPay, terminals, Operator Console, and dashboards do not declare finality or authorize exit.

The highest terminology risks are Site Group vs Site, physical parking lot vs ExitPass Site, VendorSystem vs connector instance vs adapter codebase, HCP ParkingLotIndexCode vs ExitPass `site_id`, projection visibility vs financial truth, and legacy terms such as `EC Device`, `Cashier POS`, and `Official Receipt`.

The highest scope risk is letting the SDD become a downstream technical pack. Keep System Design at architectural and authority-boundary level. Defer final endpoints, DTOs, database objects, event payloads, implementation classes, BIR accreditation artifacts, detailed UAT, and runbook procedures to the appropriate companion documents.

No blocking source contradiction was identified during this review. The review found several deliberate open items that must remain open in the SDD, including WebPay URL resolution, physical lot modeling, degraded tariff freshness, POS Server deployment/service boundary, Continuity activation authority, HCP connector topology, vendor acknowledgment mechanics, connector health/freshness modeling, and downstream BIR/accounting/API/database/engineering confirmations.
