# BRD Traceability Map Input Pack

Assigned agent: Agent 2 - BRD Traceability Map

Branch confirmed: `docs/v1.3-system-design`

Assigned output: `docs/v1.3/system-design/input-packs/02_traceability_map.md`

## 1. Purpose

This input pack maps the approved ExitPass v1.3 BRD baseline to the future `docs/v1.3/ExitPass_System_Design_v1.3.md`.

It is intended to help the System Design Lead preserve the ExitPass System Design v1.2 top-level structure and controlled-successor writing posture while ensuring that all approved v1.3 business requirements are covered at system-design level.

This pack does not draft the final System Design, API Contract, Database Design, Engineering Pack, companion technical design, implementation plan, diagram, test pack, or runbook.

## 2. Source Documents Reviewed

| Source | Role in this traceability pack |
| --- | --- |
| `docs/v1.3/system-design/ExitPass_System_Design_v1.3_Orchestration_Plan.md` | Defines approved inputs, v1.2 outline rule, file ownership, review gates, and deferral boundaries. |
| `D:\Docs\ExitPass\v1.2\ExitPass System Design v1.2.docx` | Provides top-level System Design outline, section posture, and controlled technical writing style baseline. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Confirms approved BRD baseline, authority model, and open downstream items. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core platform business baseline for authority model, WebPay, Site Group/Site, connector/projection, payment, fiscal issuance, continuity, audit, and acceptance criteria. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Companion baseline for Assisted Payment Terminal, Cashier-Assisted Terminal, Continuity Terminal, hardened terminal posture, payment/fiscal display, evidence, privacy, and terminal constraints. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Companion baseline for continuity activation, degraded resolve, projection freshness, continuity terminal restrictions, manual release, reconciliation, and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Companion baseline for Operator Console module boundary, non-payment posture, RBAC, session review, statutory discount review, continuity governance, fiscal exception review, and manual release governance. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Companion baseline for operational dashboards, financial truth separation, connector/projection visibility, fiscal/reconciliation reporting, exports, and reporting RBAC/privacy. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Companion baseline for platform-wide POS/Invoicing, Site-level POS Server, Sales Invoice, fiscal issuance before ExitAuthorization, statutory/VAT privilege treatment, POSLog posture, exports, and BIR-related open items. |

## 3. ExitPass System Design v1.2 Top-Level Outline Summary

The orchestration plan requires ExitPass System Design v1.3 to preserve the v1.2 document posture and top-level outline unless a controlled v1.3 requirement justifies a refinement.

The v1.2 System Design top-level sections are:

1. Document Control
2. System Overview
3. System Context
4. System Architecture
5. Trust Boundaries
6. Core Workflows
7. Event Architecture
8. State Machines
9. Data Architecture
10. API Architecture
11. Security Architecture
12. Failure Mode Architecture
13. Deployment Architecture
14. Observability
15. Business Continuity
16. Operational Runbooks
17. Appendix

The extracted v1.2 section posture is service-ownership oriented. It repeatedly names authoritative service ownership, boundary invariants, producer/consumer responsibilities, state-machine ownership, trust zones, allowed interaction paths, failure posture, observability, and alignment rules. v1.3 should retain that posture while adding business-approved v1.3 concepts such as centralized WebPay, Site Group/Site semantics, VendorSystem/AdapterMapping, projection freshness, platform-wide POS/Invoicing, Site POS Server, Assisted Payment Terminal, Operator Console, Continuity, and Management Dashboard/Reporting.

## 4. Proposed ExitPass System Design v1.3 Section Mapping

| v1.2-style System Design section | v1.3 traceability additions to carry forward |
| --- | --- |
| Document Control | Record v1.3 as a controlled successor to v1.2 and cite the approved BRD baseline and approval baseline. |
| System Overview | Summarize centralized WebPay, Central PMS authority, Vendor PMS/HCP authority, Payment Orchestrator, Site POS Server, Assisted Payment Terminal, Operator Console, Continuity, and Management Dashboard/Reporting as platform capabilities. |
| System Context | Add Site Group/Site context, VendorSystem/connector instances, Site POS Server, Assisted Payment Terminal modes, Operator Console, Management Dashboard, and continuity actors without changing authority ownership. |
| System Architecture | Map runtime services and modules to authority boundaries: Central PMS, WebPay, Payment Orchestrator, Vendor PMS connector, Site POS Server, Operator Console, Assisted Payment Terminal, Management Dashboard, gate/exit integration. |
| Trust Boundaries | Cover public WebPay, terminal/device trust, Operator Console trusted access, dashboard/report access, POS Server fiscal boundary, Vendor PMS integration boundary, payment provider boundary, and gate/site device boundary. |
| Core Workflows | Extend v1.2 workflows with URL/Site Group resolution, normal resolve, projection-assisted visibility, fiscal issuance before ExitAuthorization, cashier-assisted statutory validation, continuity activation, degraded resolve, manual release governance, reconciliation. |
| Event Architecture | Keep event treatment at ownership/taxonomy level only; trace payment, fiscal, authorization, projection, continuity, audit, and reporting events without inventing payloads or schemas. |
| State Machines | Preserve single-owner transitions for payment, fiscal issuance reference, ExitAuthorization, statutory discount validation, projection freshness/degraded state, continuity activation, manual release governance, and reconciliation status. |
| Data Architecture | Identify logical record domains and source-of-truth boundaries only; defer table, column, constraint, SQL, store, and migration details. |
| API Architecture | Identify service/API ownership and ingress groups only; defer endpoint names, DTO boundaries, routes, and contract details. |
| Security Architecture | Map RBAC, privacy, evidence handling, device trust, mTLS/browser binding options, certificate/key management as requirements and open items; do not finalize certificate model. |
| Failure Mode Architecture | Cover Vendor PMS/HCP outage, stale connector/projection, payment uncertainty, fiscal issuance failure, vendor acknowledgment failure, manual release, and fail-closed degraded mode. |
| Deployment Architecture | Keep deployment model at system level, including central WebPay and Site-level POS Server boundaries; defer deployment scripts and exact service packaging where open. |
| Observability | Cover connector health, projection freshness, last poll/latency, fiscal backlog, payment uncertainty, continuity state, manual release counts, reconciliation status, audit correlation, and dashboards. |
| Business Continuity | Integrate Continuity BRD requirements for activation/deactivation, degraded resolve, Continuity Terminal restrictions, post-restoration review, and reconciliation. |
| Operational Runbooks | Preserve runbook categories and identify procedure areas, but defer detailed runbook procedures to later runbook packs. |
| Appendix | Include glossary, source traceability, diagram index references, non-decisions, and open questions that must remain open. |

## 5. BRD-to-System-Design Traceability Table

| Traceability area | Approved BRD basis | Future System Design coverage target | Deferral / guardrail |
| --- | --- | --- | --- |
| Central PMS authority and service responsibility | Core BRD Sections 2, 3.7, 7.8, 9.9-9.10, 12, 14.3, 15.1; Approval Baseline Section 4 | System Overview, System Architecture, Core Workflows, State Machines, API Architecture, Security Architecture | Do not move payment finality, fiscal issuance authority, or ExitAuthorization authority to WebPay, Payment Orchestrator, POS Server, Operator Console, Assisted Payment Terminal, dashboard, or gate. |
| Centralized WebPay | Core BRD Sections 5.1.1, 9.1, 12.1, 18 AC-001 to AC-002 | System Context, System Architecture, Trust Boundaries, Core Workflows, API Architecture | Keep slug registry structure open. Do not invent URL registry tables, endpoint paths, or DTOs. |
| Site Group vs Site semantics | Core BRD Sections 3.4, 3.5, 5.1.2-5.1.3, 7.2-7.3, 9.2-9.3, 18 AC-003 to AC-005; MDR BRD Sections 11, 19, 37 | System Context, Data Architecture, API Architecture, Observability, Reporting-related Appendix notes | Preserve Site Group as lookup/payment scope and Site as reporting/vendor/POS/operations boundary. Keep user-facing label decision open. |
| VendorSystem / AdapterMapping / connector instance model | Core BRD Sections 7.4, 9.4, 11.1-11.2, 18 AC-006 | System Architecture, Trust Boundaries, Data Architecture, API Architecture | Do not treat HCP `ParkingLotIndexCode` as ExitPass `site_id`; defer exact database columns and API contracts. |
| HikCentral connector posture | Core BRD Sections 7.5, 9.4-9.7, 13.2-13.4, 17.1; Continuity BRD Sections 12, 19, 20 | System Context, System Architecture, Failure Mode Architecture, Observability, Business Continuity | One-minute passageway polling is business baseline; push vs pull topology and exact health model remain open. |
| Vendor PMS / HCP normal interactions | Core BRD Sections 3.3, 5.1.4, 8.4, 9.5; Continuity BRD Section 19 VDR-001 | Core Workflows, Trust Boundaries, State Machines, Failure Mode Architecture | Vendor PMS/HCP remains normal authority for raw session lifecycle and tariff computation. |
| Vendor PMS / HCP degraded interactions | Core BRD Sections 9.6, 13.2-13.5; Continuity BRD Sections 11, 12, 18, 19, 20, 34 | Failure Mode Architecture, Business Continuity, Observability, Operational Runbooks | Degraded resolve requires explicit controls; freshness threshold and degraded tariff configuration remain open. |
| Projection freshness and connector polling | Core BRD Sections 5.1.5, 9.6-9.7, 10.6-10.7, 13.3-13.4, 14.4; Continuity BRD Sections 20, 34; MDR BRD Sections 12, 19, 21, 37 | Observability, Failure Mode Architecture, Business Continuity, Dashboard/reporting coverage | Projection is operational visibility/degraded support only, not payment finality, exit authorization, or financial truth. |
| Assisted Payment Terminal | Core BRD Sections 7.7, 9.11-9.12, 18 AC-015 to AC-023; APT BRD Sections 6-15, 22-25, 30 | System Context, System Architecture, Trust Boundaries, Core Workflows, Security Architecture | Keep terminal implementation stack, native bridge, hardware integrations, and endpoint/DTO details deferred. |
| Cashier-Assisted Terminal | Core BRD Sections 8.5, 9.12; APT BRD Sections 9.1, 14.1, 16, 17, 18, 19, 30 | Core Workflows, State Machines, Security Architecture, Failure Mode Architecture | Terminal captures validation inputs and displays backend results; it does not approve entitlement, mutate payable basis directly, declare finality, or issue authorization. |
| Continuity Terminal | Core BRD Sections 13.9-13.10; APT BRD Sections 9.2, 20, 30; Continuity BRD Sections 13, 21, 22, 34 | Business Continuity, Failure Mode Architecture, Trust Boundaries, Security Architecture, Observability | Disabled by default; activation authority, exact workflow, offline behavior, and freshness threshold remain open. |
| Operator Console | Core BRD Sections 7.6, 9.13, 14.1; Operator Console BRD Sections 6-16, 17-29, 35 | System Context, System Architecture, Trust Boundaries, Security Architecture, Observability | Preserve non-payment, non-fiscal, non-ExitAuthorization boundary. Manual release is governance/review only unless later approved design changes boundary. |
| Management Dashboard and Reporting | Core BRD Sections 9.15, 14.4, 18 AC-027; MDR BRD Sections 10-19, 20-31, 37 | System Context, Observability, Data Architecture, Security Architecture, Appendix | Dashboards must label source-of-truth and separate operational projection visibility from canonical financial/fiscal/reconciliation records. |
| Platform-wide POS/Invoicing | Core BRD Sections 5.1.7, 9.8, 15.2; POS BRD Sections 9, 11, 19, 20, 40 | System Overview, System Architecture, Trust Boundaries, Core Workflows, Data Architecture | System Design should anchor POS/Invoicing boundary only; detailed BIR/accreditation package and POSLog mapping remain outside core SDD. |
| Site-level POS Server | Core BRD Sections 5.1.3, 9.9-9.10; POS BRD Sections 10, 12, 19, 22, 40 | System Context, System Architecture, Core Workflows, Trust Boundaries, Failure Mode Architecture | POS Server is fiscal issuance authority, not payment finality or ExitAuthorization authority. Exact deployment/registration model remains open. |
| Fiscal issuance before ExitAuthorization | Core BRD Sections 8.4, 9.10, 12.4, 13.6-13.7, 18 AC-010 to AC-012; POS BRD Sections 12, 18, 19, 22, 30, 40; APT BRD Section 19; Continuity BRD Section 23 | Core Workflows, State Machines, Failure Mode Architecture, Business Continuity | Do not allow normal ExitAuthorization before successful fiscal issuance. Exception/manual release policy remains open and must be explicit. |
| Statutory discounts / entitlement / VAT privilege | Core BRD Sections 8.5, 9.12, 10.12, 13.10, 15.4; APT BRD Sections 17, 20; Operator Console BRD Section 18; POS BRD Sections 25-26, 40; MDR BRD Section 26 | Core Workflows, State Machines, Security Architecture, Data Architecture, Observability | Central PMS / Discount workflow remains authority for validation state and payable-basis effect. VAT/tax treatment details remain finance/accounting/BIR confirmation items. |
| Payment orchestration | Core BRD Sections 5.1.6, 7.8, 12, 15.1; APT BRD Section 18; Continuity BRD Section 23; MDR BRD Section 23 | System Architecture, Core Workflows, State Machines, API Architecture, Failure Mode Architecture | Payment Orchestrator reports verified provider outcomes but does not declare platform payment finality. |
| Gate/exit execution | Core BRD Sections 5.1.8, 7.8-7.9, 9.10, 13.11, 17.4; Operator Console BRD Sections 7, 14, 16, 24; Continuity BRD Sections 23-24 | System Context, Trust Boundaries, Core Workflows, State Machines, Failure Mode Architecture | Gate/exit consumes Central PMS-issued ExitAuthorization and must not bypass Central PMS except under formally approved manual emergency process. |
| Audit/evidence/privacy/RBAC | Core BRD Sections 10.4, 10.11-10.12, 14, 15.4; APT BRD Sections 22-24; Operator Console BRD Sections 19, 25-28; MDR BRD Sections 28-30; POS BRD Sections 32-33 | Trust Boundaries, Security Architecture, Observability, Data Architecture, Operational Runbooks | Exact permission matrix, retention periods, evidence redaction, and certificate/key storage are open or deferred. |
| Reconciliation and post-restoration review | Core BRD Sections 12.5, 13.8, 14.3; Continuity BRD Sections 25, 34; POS BRD Section 34; MDR BRD Sections 25, 27, 37 | Failure Mode Architecture, Business Continuity, Observability, Data Architecture | Reconciliation must link payment, provider outcome, fiscal issuance, vendor acknowledgment, ExitAuthorization, continuity/manual release tags; exact SLA/status labels remain open. |
| Observability and operations | Core BRD Sections 9.7, 10.7, 14.4; Continuity BRD Sections 27-29; Operator Console BRD Sections 21-23, 27; MDR BRD Sections 20-22, 31, 37 | Observability, Business Continuity, Operational Runbooks | Keep detailed alert thresholds, dashboard refresh intervals, and runbook procedures deferred. |

## 6. Companion BRD Coverage Table

| Companion BRD | Coverage that must appear in System Design v1.3 | Primary section targets |
| --- | --- | --- |
| Assisted Payment Terminal BRD v1.0 | Terminal app family, Cashier-Assisted and Continuity modes, terminal/device/cashier identity, Site/Site Group binding, statutory validation capture, payment/fiscal/authorization display, evidence/privacy, hardened terminal posture, disabled-by-default continuity mode. | System Context, System Architecture, Trust Boundaries, Core Workflows, Security Architecture, Business Continuity |
| Continuity BRD v1.0 | Continuity activation/deactivation, affected scope/dependency/incident reference, degraded resolve eligibility, projection freshness, fail-closed behavior, continuity terminal restriction, fiscal exception handling, manual release governance, reconciliation, post-restoration review. | Failure Mode Architecture, Business Continuity, Observability, Operational Runbooks |
| Operator Console BRD v1.1 | Internal non-payment operations module, RBAC, device trust, shift validation, read-only authority-state context, statutory discount review, continuity approval/review, connector/projection visibility, fiscal exception review, manual release governance, audit/export scope. | System Context, Trust Boundaries, Security Architecture, Observability, Core Workflows |
| Management Dashboard and Reporting BRD v1.0 | Role-based dashboards, Site Group/Site views, projection freshness and connector health, operational vs financial truth labels, payment/fiscal/reconciliation reporting, statutory discount/coupon reporting, exports, evidence/privacy controls. | Observability, Data Architecture, Security Architecture, Appendix |
| POS/Invoicing BRD v1.0 | Platform-wide BIR-authorized POS/Invoicing, Site-level POS Server, Sales Invoice primary fiscal output, fiscal issuance before ExitAuthorization, fiscal reports, statutory entitlement and VAT privilege treatment, reprints/adjustments, POSLog/export posture, fiscal exception handling. | System Architecture, Trust Boundaries, Core Workflows, State Machines, Data Architecture, Failure Mode Architecture |

## 7. Open Questions That Must Remain Open in System Design

The System Design should carry these forward as explicit open items or design constraints rather than silently resolving them:

| Topic | Open question basis |
| --- | --- |
| WebPay URL slug registry | Core BRD OQ-001 and OQ-002. |
| Physical lot/cluster first-class modeling | Core BRD OQ-003. |
| Degraded tariff/projection freshness threshold | Core BRD OQ-004; APT-OQ-008; CON-OQ-003; MDR-OQ-005. |
| POS Server deployment and registration model | Core BRD OQ-005 and OQ-006; POS-OQ-001 to POS-OQ-003. |
| Continuity/BCP activation authority and workflow | Core BRD OQ-007; APT-OQ-007; CON-OQ-001, CON-OQ-002, CON-OQ-006; OC-OQ-003. |
| HCP connector push/pull topology and health model | Core BRD OQ-008 and OQ-010; OC-OQ-006; MDR-OQ-006. |
| Vendor acknowledgment retry policy | Core BRD OQ-009; CON-OQ-011. |
| Site Group user-facing terminology | Core BRD OQ-011. |
| Core BRD vs Management Dashboard detailed scope | Core BRD OQ-012; OC-OQ-010; MDR-OQ-001. |
| Terminal implementation architecture and hardware integration | APT-OQ-001 to APT-OQ-005, APT-OQ-015. |
| Offline evidence/payment/fiscal behavior | APT-OQ-006; CON-OQ-007 and CON-OQ-008; POS-AC-020. |
| Permission matrix and device trust mechanism | APT-OQ-009; OC-OQ-002 and OC-OQ-011; POS-OQ-019; MDR-OQ-002. |
| Manual release and fiscal exception policy | CON-OQ-009 and CON-OQ-010; OC-OQ-004 and OC-OQ-005. |
| Reconciliation SLA and labels | CON-OQ-012; MDR-OQ-011. |
| Evidence retention, redaction, and privacy controls | CON-OQ-014; OC-OQ-007; MDR-OQ-013 to MDR-OQ-015. |
| Tax/VAT treatment and Diplomat VAT privilege details | POS-OQ-008 and POS-OQ-009. |
| Fiscal numbering, counters, sequence gaps, X/Z scope | POS-OQ-004 to POS-OQ-007. |
| Digital Sales Invoice URL security model | POS-OQ-010. |
| ARTS POSLog and JSON schema mapping | POS-OQ-011 and POS-OQ-012. |
| BIR/accreditation sample package | POS-OQ-013. |
| Tamper-evident anchoring/recovery mechanism | POS-OQ-014. |

## 8. Items That Must Be Deferred to Companion Technical Designs

The System Design v1.3 may identify boundaries and ownership, but these items should remain deferred:

| Deferred item | Target later document / phase |
| --- | --- |
| POS Server internal architecture, sequence/counter mechanics, recovery mechanics, and fiscal storage internals | POS Server System Design and POS Server Database Design. |
| POS Server API operation details, endpoint names, DTO boundaries, error model, idempotency details | POS Server API Contract. |
| Assisted Payment Terminal final implementation stack, Android shell/WebView/PWA split, native bridge scope, scanner/camera/printer/cash drawer integration, kiosk lockdown, certificate/key storage | Assisted Payment Terminal System Design. |
| Continuity activation/deactivation technical workflow, degraded tariff algorithm, projection freshness enforcement mechanics, offline behavior, reconciliation workflow mechanics | Continuity System Design. |
| Operator Console implementation details, exact permission matrix, device trust mechanism, browser key binding/mTLS selection, supervisor workflow internals | Operator Console technical design if created later. |
| Management Dashboard BI/reporting technology, reporting stores, refresh intervals, export implementation, data mart design, exact aggregation rules | Management Dashboard/Reporting technical design or reporting data architecture. |
| Vendor PMS connector implementation topology, HCP connector push/pull mechanism, polling implementation, adapter runtime details | Vendor PMS Connector System Design / HikCentral Connector Profile. |
| Detailed BIR accreditation package, sample outputs, final fiscal wording/layouts, certification evidence package | BIR/accreditation submission pack and POS Server technical documents. |
| Test/UAT cases, detailed acceptance scripts, scenario fixtures | Test/UAT Pack. |
| Operational step-by-step procedures | Runbook Pack. |

## 9. Items That Must Be Deferred to Database/API/Engineering Phases

The following must not be invented in the System Design v1.3 core document:

| Deferred detail | Reason / boundary |
| --- | --- |
| Endpoint names and route paths | Deferred to API Contract Pack and companion API contracts. |
| DTO boundaries and request/response schemas | Deferred to API Contract Pack. |
| Database tables, columns, constraints, indexes, and migrations | Deferred to Database Design / Database Delta. |
| SQL routines, functions, procedures, and scripts | Deferred to Database/Engineering Pack. |
| Event payload fields, final event schemas, and serialization details | Deferred to Event/API/Engineering Pack. |
| Implementation classes, code namespaces, repository/service class names | Deferred to Engineering Pack and implementation. |
| Deployment scripts, environment templates, infrastructure scripts | Deferred to Deployment/Engineering Pack. |
| Final certificate model and key storage implementation | Deferred to Security Architecture detail and companion technical designs. |
| Detailed POSLog schema mapping and JSON schema validation | Deferred to POS Server API/Database/Accreditation work. |
| BIR accreditation package details and sample submissions | Deferred to accreditation pack. |
| Test/UAT case definitions and automated test names | Deferred to Test/UAT Pack. |
| Runbook procedures and operator step lists | Deferred to Runbook Pack. |

## 10. Suggested Acceptance Checklist for ExitPass System Design v1.3

| Checklist item | Acceptance signal |
| --- | --- |
| v1.2 controlled-successor posture retained | Top-level outline follows v1.2 sections or explicitly justifies controlled refinements. |
| Approved BRD baseline cited | All six approved BRDs plus approval baseline are cited as business inputs. |
| Authority model preserved | Vendor PMS/HCP, Central PMS, Payment Orchestrator, POS Server, WebPay, Operator Console, APT, dashboard, and gate responsibilities match the approval baseline. |
| Centralized WebPay covered | System Design explains centralized WebPay and Site Group/Site resolution without inventing slug registry internals. |
| Site Group/Site semantics covered | Lookup/payment scope and reporting/vendor/POS/operations boundary are distinguished. |
| Connector/projection model covered | VendorSystem, connector instance, AdapterMapping, HCP polling/projection, connector health, and projection freshness are described at system level. |
| Projection guardrails explicit | Projection is not payment finality, exit authorization, or financial truth. |
| Payment-to-exit chain covered | Normal chain includes resolve, tariff snapshot, payment finality, fiscal issuance, fiscal reference recording, ExitAuthorization, and gate consumption. |
| POS/Invoicing integrated | Site-level POS Server and fiscal issuance before ExitAuthorization are included without drafting POS Server technical design. |
| Assisted Payment Terminal covered | Cashier-Assisted and Continuity Terminal modes, terminal identity, device posture, statutory capture, payment/fiscal/status display, and constraints are included. |
| Operator Console covered | Non-payment operations/governance boundary, RBAC, evidence, fiscal exception review, continuity governance, manual release governance, and audit are included. |
| Management Dashboard covered | Operational visibility, financial truth separation, Site/Site Group views, reporting/export controls, and source labels are included. |
| Continuity covered | Activation/deactivation, degraded resolve, fail-closed rules, continuity terminal controls, manual release governance, reconciliation, and post-restoration review are included. |
| Security/privacy/RBAC covered | Role enforcement, evidence controls, device trust, audit logging, data protection, and open certificate/key decisions are surfaced. |
| Observability covered | Connector health, projection freshness, fiscal backlog, continuity state, manual release counts, reconciliation, payment uncertainty, and audit correlation are covered. |
| Open questions preserved | Open BRD questions remain explicit and are not silently closed. |
| Deferrals respected | No endpoint names, DTOs, database tables/columns, SQL routines, event payload schemas, implementation classes, deployment scripts, Test/UAT cases, or runbook steps are invented. |

## 11. Summary for System Design Lead

The future ExitPass System Design v1.3 should be a controlled successor to v1.2, not a new document family. The v1.2 outline is still usable, but each major section needs v1.3 traceability additions for centralized WebPay, Site Group/Site semantics, VendorSystem/AdapterMapping, HCP connector projection, platform-wide POS/Invoicing, Site POS Server, fiscal issuance before ExitAuthorization, Assisted Payment Terminal, Operator Console, Continuity, and Management Dashboard/Reporting.

The strongest invariant across all approved BRDs is authority separation:

- Vendor PMS/HCP remains normal authority for raw parking session lifecycle and tariff computation.
- Central PMS remains authority for payment-linked platform control state, payment finality, fiscal issuance reference recording, and ExitAuthorization.
- Payment Orchestrator reports verified provider outcomes and does not declare platform finality.
- POS Server remains fiscal issuance authority and does not issue ExitAuthorization.
- WebPay, Assisted Payment Terminal, Operator Console, Management Dashboard, and gates do not bypass Central PMS or POS Server authority.

The highest-risk areas for the System Design Lead are the areas where business requirements are approved but downstream detail remains intentionally open: WebPay slug registry, Site Group user-facing terminology, connector topology and freshness thresholds, continuity activation authority, POS Server deployment/registration, tax/VAT treatment, digital Sales Invoice URL security, final RBAC/device trust, reporting architecture, and fiscal/accreditation details. These should be carried as explicit open items or deferrals, not resolved by invention in the core System Design.
