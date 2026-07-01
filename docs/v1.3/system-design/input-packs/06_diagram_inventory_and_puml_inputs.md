# ExitPass System Design v1.3 Input Pack 06: Diagram Inventory and PlantUML Planning Inputs

## 1. Purpose

This input pack inventories the existing ExitPass v1.3 BRD diagrams and recommends the diagram set that should be considered for ExitPass System Design v1.3.

This file is planning input only. It does not create final diagrams, PlantUML source files, image exports, endpoint paths, database diagrams, schema details, or implementation-class diagrams.

The intent is to help the System Design Lead decide which diagrams belong in the final System Design document and how those diagrams should preserve the approved v1.3 authority model.

## 2. Source Documents and Diagram Folders Reviewed

Approved source documents reviewed:

| Source | Review focus |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 scope, authority model, Site Group/Site semantics, connector projection, normal payment-to-exit flow, POS/Invoicing anchor, continuity posture, audit/reporting requirements, open questions, and Appendix C diagram list. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Assisted Payment Terminal modes, terminal authority exclusions, cashier-assisted flow, continuity terminal flow, device/shift/accountability posture, and Appendix C diagram list. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Degraded operation principles, continuity states, activation/deactivation, degraded resolve, continuity terminal restrictions, manual release, reconciliation, and Appendix C diagram list. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console non-payment boundary, governance workflows, continuity activation review, fiscal exception review, manual release governance, RBAC/audit posture, and Appendix C diagram list. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Dashboard/reporting non-authority boundary, Site Group/Site reporting model, projection freshness, financial truth boundary, reporting domains, and Appendix C diagram list. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site-level POS Server fiscal model, payment finality to fiscal issuance to ExitAuthorization choreography, fiscal failure handling, channel/terminal fiscal routing, and Appendix C diagram list. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approved BRD baseline, preserved authority model, and downstream open confirmation items. |
| `docs/v1.3/system-design/ExitPass_System_Design_v1.3_Orchestration_Plan.md` | Specialist scope, file ownership, v1.2 outline/style baseline, and SDD integration rules. |
| `D:\Docs\ExitPass\v1.2` | v1.2 System Design file presence and orchestration-defined style/outline baseline for controlled successor posture. |

Diagram folders reviewed:

| Folder | Contents observed |
| --- | --- |
| `docs/v1.3/diagrams/` | Core ExitPass BRD diagrams under `brd/`. |
| `docs/v1.3/assisted-payment-terminal/diagrams/` | Assisted Payment Terminal BRD diagrams. |
| `docs/v1.3/continuity/diagrams/` | Continuity BRD diagrams. |
| `docs/v1.3/operator-console/diagrams/` | Operator Console BRD diagrams. |
| `docs/v1.3/management-dashboard-reporting/diagrams/` | Management Dashboard and Reporting BRD diagrams. |
| `docs/v1.3/pos-invoicing/diagrams/` | POS/Invoicing BRD diagrams. |

## 3. Existing v1.3 BRD Diagram Inventory

### Core ExitPass BRD v1.3 diagrams

| ID | Diagram | Existing files |
| --- | --- | --- |
| D-01 | ExitPass v1.3 Context Diagram | `.jpg` and `.puml` under `docs/v1.3/diagrams/brd/` |
| D-02 | Site Group, Site, Vendor System, and POS Server Relationship | `.jpg` and `.puml` under `docs/v1.3/diagrams/brd/` |
| D-03 | Normal Payment-to-Exit Flow with Fiscal Issuance | `.jpg` and `.puml` under `docs/v1.3/diagrams/brd/` |
| D-04 | Degraded Resolve and Continuity Flow | `.jpg` and `.puml` under `docs/v1.3/diagrams/brd/` |
| D-05 | Assisted Payment Terminal Operating Modes | `.jpg` and `.puml` under `docs/v1.3/diagrams/brd/` |

### Assisted Payment Terminal BRD diagrams

| ID | Diagram | Existing files |
| --- | --- | --- |
| D-01 | Assisted Payment Terminal Context Diagram | `.jpg` and `.puml` |
| D-02 | Assisted Payment Terminal Operating Modes | `.jpg` and `.puml` |
| D-03 | Cashier-Assisted Payment with Statutory Discount Validation Flow | `.jpg` and `.puml` |
| D-04 | Payment, Fiscal Issuance, and ExitAuthorization Authority Flow | `.jpg` and `.puml` |
| D-05 | Continuity Terminal Activation and Restricted Operation Flow | `.jpg` and `.puml` |
| D-06 | Android-first Hardened Terminal Posture Diagram | `.jpg` and `.puml` |

### Continuity BRD diagrams

| ID | Diagram | Existing files |
| --- | --- | --- |
| D-01 | Continuity Context Diagram | `.jpg` and `.puml` |
| D-02 | Continuity Activation and Deactivation Flow | `.jpg` and `.puml` |
| D-03 | Vendor PMS / HCP Degraded Resolve Flow | `.jpg` and `.puml` |
| D-04 | Payment, Fiscal Issuance, and ExitAuthorization Under Continuity | `.jpg` and `.puml` |
| D-05 | Continuity Terminal Restricted Operation Flow | `.jpg` and `.puml` |
| D-06 | Post-Restoration Reconciliation Flow | `.jpg` and `.puml` |

### Operator Console BRD diagrams

| ID | Diagram | Existing files |
| --- | --- | --- |
| D-01 | Operator Console Context Diagram | `.jpg` and `.puml` |
| D-02 | Operator Console Module Boundary Diagram | `.jpg` and `.puml` |
| D-03 | Statutory Discount Review and Evidence Workflow | `.jpg` and `.puml` |
| D-04 | Continuity Activation and Post-Restoration Review Governance Flow | `.jpg` and `.puml` |
| D-05 | Fiscal Issuance Exception Review Flow | `.jpg` and `.puml` |
| D-06 | Manual Release Governance Flow | `.jpg` and `.puml` |

### Management Dashboard and Reporting BRD diagrams

| ID | Diagram | Existing files |
| --- | --- | --- |
| D-01 | Management Dashboard Context Diagram | `.jpg` and `.puml` |
| D-02 | Operational Visibility vs Financial Truth Boundary Diagram | `.jpg` and `.puml` |
| D-03 | Site Group and Site Reporting View Diagram | `.jpg` and `.puml` |
| D-04 | Connector Health and Projection Freshness Dashboard Flow | `.jpg` and `.puml` |
| D-05 | Payment, Fiscal, and Reconciliation Reporting Flow | `.jpg` and `.puml` |
| D-06 | Continuity and Exception Reporting Flow | `.jpg` and `.puml` |

### POS/Invoicing BRD diagrams

| ID | Diagram | Existing files |
| --- | --- | --- |
| D-01 | POS/Invoicing Context Diagram | `.jpg` and `.puml` |
| D-02 | Site-level POS Server Model | `.jpg` and `.puml` |
| D-03 | Payment-to-Exit Fiscal Sequence | `.jpg` and `.puml` |
| D-04 | Channel / Terminal Fiscal Routing | `.jpg` and `.puml` |
| D-05 | Fiscal Output and Reporting Model | `.jpg` and `.puml` |
| D-06 | Fiscal Issuance Failure Exception Flow | `.jpg` and `.puml` |

Inventory summary:

- Existing v1.3 BRD diagram count reviewed: 35 diagrams.
- Each reviewed diagram set has both PlantUML source and JPEG export in the reviewed folders.
- These diagrams are BRD-level inputs. The SDD should not copy all diagrams mechanically. It should consolidate them into a smaller system-design-level set that explains architecture, authority, workflows, continuity, reporting boundaries, and event/audit posture.

## 4. Recommended ExitPass System Design v1.3 Diagram Set

The recommended SDD diagram set should include the following diagrams at minimum:

| No. | Recommended SDD diagram | Primary purpose | Primary source drivers |
| --- | --- | --- | --- |
| 1 | ExitPass v1.3 Logical Architecture | Show major platform components and external systems at system-design level. | ExitPass BRD sections 3.7, 5.1, 7.1-7.9, 17; Approval Baseline section 4. |
| 2 | Authority Boundary Model | Show which component owns each authoritative decision or record. | ExitPass BRD sections 7.8-7.9, 11, 12, 13, 14; POS/Invoicing BRD section 12; Approval Baseline section 4. |
| 3 | Site Group / Site / VendorSystem / Connector Instance / POS Server Topology | Clarify customer lookup scope versus resolved operational/fiscal Site and related integrations. | ExitPass BRD sections 3.4, 5.1.2-5.1.4, 7.2-7.5, 9.2-9.4, 11; MDR BRD section 11; POS/Invoicing BRD section 10. |
| 4 | Normal Payment-to-Exit Sequence | Show normal customer/channel flow from lookup through payment, fiscal issuance, authorization, and gate consumption. | ExitPass BRD sections 8.3-8.4, 9.1-9.10, 12; POS/Invoicing BRD sections 18, 22; Assisted Payment Terminal BRD section 14.1. |
| 5 | Payment Finality to Fiscal Issuance to ExitAuthorization Sequence | Isolate the critical authority handoff after verified provider outcome. | ExitPass BRD sections 12.3-12.4, 13.6-13.7; POS/Invoicing BRD sections 9, 12, 18, 22, 30. |
| 6 | Vendor PMS Connector Projection and Freshness Flow | Show normal Vendor PMS/HCP polling, projection update, health/freshness labeling, and degraded-use constraints. | ExitPass BRD sections 7.4-7.5, 9.4, 9.6-9.7, 10.6-10.7, 13.2-13.4; Continuity BRD sections 12.1-12.2, 19-20; MDR BRD sections 12, 21, 31. |
| 7 | Degraded Resolve and Continuity Sequence | Show fail-closed degraded operation, continuity activation, projection use, fiscal/exit constraints, and reconciliation tagging. | ExitPass BRD sections 8.9, 13.1-13.11; Continuity BRD sections 9-12, 17, 19-25; Assisted Payment Terminal BRD section 14.2. |
| 8 | Assisted Payment Terminal Context and Modes | Show the terminal app family, cashier-assisted mode, continuity mode, and separation from Operator Console. | ExitPass BRD sections 7.7, 9.11-9.12, 13.10; Assisted Payment Terminal BRD sections 6-14; POS/Invoicing BRD sections 14-15. |
| 9 | Operator Console Governance Boundary | Show Operator Console as non-payment governance, review, evidence, continuity, fiscal exception, and manual release surface. | ExitPass BRD section 9.13; Operator Console BRD sections 6-15, 27-28; Continuity BRD section 14; POS/Invoicing BRD section 16. |
| 10 | Management Dashboard Source-of-Truth Boundary | Show dashboard/reporting as visibility only and distinguish projection-based operational visibility from canonical financial/fiscal truth. | ExitPass BRD sections 9.15, 14.4; MDR BRD sections 6-17, 21, 31; POS/Invoicing BRD section 17. |
| 11 | Audit, Event, and Outbox Conceptual Flow | Show event/audit categories, immutable traceability, outbox-style reliable event publication concept, and reporting/reconciliation consumers without database/schema detail. | ExitPass BRD sections 10.8, 11.4-11.5, 14, 15.3; Assisted Payment Terminal BRD section 23; Operator Console BRD section 27; MDR BRD sections 28-31; POS/Invoicing BRD section 28. |

## 5. Diagram Purpose and Intended SDD Section

| Diagram | Target SDD section | Purpose |
| --- | --- | --- |
| ExitPass v1.3 Logical Architecture | System Architecture | Establish the controlled-successor v1.3 component view: Central PMS, Vendor PMS/HCP, connector instance, payment channels, Payment Orchestrator, Site POS Server, Assisted Payment Terminal, Operator Console, Management Dashboard, Audit/Event capability, and Gate/Exit system. |
| Authority Boundary Model | Trust Boundaries | Make the authority model explicit and reviewable. This should be the highest-value diagram for preventing downstream scope drift. |
| Site Group / Site / VendorSystem / Connector Instance / POS Server Topology | System Context or System Architecture | Explain how customer lookup/payment scope, resolved Site, vendor mapping, connector runtime, and Site POS Server routing relate without becoming a database entity diagram. |
| Normal Payment-to-Exit Sequence | Core Workflows | Present the end-to-end normal path from session discovery to gate consumption. This is the primary SDD workflow diagram. |
| Payment Finality to Fiscal Issuance to ExitAuthorization Sequence | Core Workflows or API Architecture overview | Focus the finality/fiscal/authorization handoff so SDD readers do not infer that payment channels, payment provider outcomes, or POS Server independently authorize exit. |
| Vendor PMS Connector Projection and Freshness Flow | Event Architecture or Observability | Show projection flow and freshness/health classification as operational visibility and degraded-support input only. |
| Degraded Resolve and Continuity Sequence | Failure Mode Architecture or Business Continuity | Show controlled degraded resolution, continuity activation, fail-closed behavior, supervisor involvement, audit/reconciliation tags, and post-restoration review. |
| Assisted Payment Terminal Context and Modes | System Context or Deployment Architecture | Place the terminal in the platform without turning it into a separate POS, fiscal, or authorization authority. |
| Operator Console Governance Boundary | Security Architecture or Trust Boundaries | Show governance workflows and non-payment/non-authority constraints for operator and supervisor actions. |
| Management Dashboard Source-of-Truth Boundary | Observability | Show the difference between operational visibility, financial truth, fiscal truth, audit records, and reporting/export access. |
| Audit, Event, and Outbox Conceptual Flow | Event Architecture or Observability | Provide a conceptual event/audit flow for traceability and reliable downstream consumption, while avoiding implementation queue/schema details. |

## 6. Key Components Per Diagram

| Diagram | Components to include | Relationships to show |
| --- | --- | --- |
| ExitPass v1.3 Logical Architecture | Parker/customer channels, WebPay/APM, Assisted Payment Terminal, Operator Console, Management Dashboard, Central PMS, Vendor PMS/HCP, connector instance, Payment Orchestrator, payment provider, resolved Site POS Server, Gate/Exit system, Audit/Event capability, reconciliation/reporting consumers. | Channels interact through approved platform flows; Central PMS coordinates control state; Vendor PMS/HCP remains normal session/tariff authority; Payment Orchestrator reports provider outcomes; POS Server issues fiscal records; gate consumes Central PMS authorization. |
| Authority Boundary Model | Authority domains for parking session lifecycle, normal tariff computation, projection/control state, payment finality, fiscal issuance, fiscal reference recording, ExitAuthorization, gate execution, discount policy, governance/review, reporting visibility. | Each authority domain maps to one owner or explicitly delegated workflow. Non-authority modules should be shown as consumers, submitters, or reviewers only. |
| Site Group / Site / VendorSystem / Connector Instance / POS Server Topology | Site Group/payment scope, one or more Sites, VendorSystem, connector instance, vendor object/session references, resolved Site POS Server, payment channels/terminals, reporting views. | Site Group supports customer lookup/payment scope; Site owns reporting, contract, Vendor PMS mapping, POS routing, and operations boundary; connector instance belongs to a VendorSystem/HCP instance; resolved Site determines POS Server routing. |
| Normal Payment-to-Exit Sequence | Customer or operator, channel, Central PMS, Vendor PMS/HCP, connector/projection where applicable, Payment Orchestrator, payment provider, Site POS Server, Gate/Exit system, audit/event records. | Lookup resolves Site and payable basis; payment provider outcome flows through Payment Orchestrator; Central PMS records finality; POS Server issues fiscal document; Central PMS records fiscal reference and issues ExitAuthorization; gate consumes authorization. |
| Payment Finality to Fiscal Issuance to ExitAuthorization Sequence | Payment Orchestrator, Central PMS, resolved Site POS Server, Gate/Exit consumer, Vendor PMS/HCP acknowledgement or update, audit/reconciliation workflow. | Payment Orchestrator reports verified outcome but does not declare platform finality; Central PMS declares finality; POS Server issues fiscal document; Central PMS records fiscal reference; Central PMS issues ExitAuthorization; gate consumes only Central PMS authorization. |
| Vendor PMS Connector Projection and Freshness Flow | Vendor PMS/HCP, connector instance, Central PMS projection/control state, integration health/freshness classifier, Management Dashboard, Operator Console/Continuity workflow, audit/event stream. | Connector polls or receives vendor data; Central PMS maintains projection and freshness status; dashboards display labels; continuity can use projection only under approved controls; projection does not become financial truth. |
| Degraded Resolve and Continuity Sequence | Channel/operator, Central PMS, Vendor PMS/HCP unavailable or stale, projection store/concept, Continuity workflow, Operator Console/supervisor, Assisted Payment Terminal in continuity mode, Site POS Server, Gate/Exit, reconciliation workflow, audit/event records. | Dependency degradation is recognized; continuity activation requires scope, approval, incident, audit, and reconciliation tags; degraded resolve uses allowed basis only; unsafe cases fail closed or route to review; manual release remains controlled and exceptional. |
| Assisted Payment Terminal Context and Modes | Cashier, parker, supervisor, Assisted Payment Terminal app family, cashier-assisted mode, continuity terminal mode, Central PMS, Discount workflow, Payment Orchestrator, Site POS Server, Operator Console, Gate/Exit. | Terminal captures cashier/device/shift context and submits to backend flows; cashier mode supports normal assisted payment; continuity mode is disabled by default; Operator Console handles review/governance; terminal does not declare finality, issue fiscal documents, or authorize exit. |
| Operator Console Governance Boundary | Site operator, supervisor, compliance auditor, support/technical ops, Operator Console, Central PMS, Discount workflow, Evidence Store/reference, Continuity workflow, Site POS Server fiscal exception context, manual emergency process, audit/reporting. | Console supports lookup, evidence review, continuity activation review, fiscal exception review, manual release governance, and audit. It does not collect payments, open gates, issue invoices, mutate fiscal records, or issue/consume ExitAuthorization. |
| Management Dashboard Source-of-Truth Boundary | Management Dashboard, operational projection/health source, Central PMS canonical records, Payment Orchestrator/provider outcome evidence, Site POS Server fiscal records, reconciliation records, audit/evidence records, user roles/scopes. | Operational reports use projection/health with freshness labels; financial/fiscal reports use canonical payment/fiscal/reconciliation records; exports are RBAC and scope controlled; dashboard remains visibility only. |
| Audit, Event, and Outbox Conceptual Flow | Business workflows, audit/event classification, conceptual outbox/reliable publication boundary, event consumers, Management Dashboard, reconciliation workflow, compliance/audit review, operational monitoring. | Workflows emit auditable business events; reliable publication feeds reporting/reconciliation/monitoring; events are classified by operational, payment, fiscal, authorization, connector, continuity, operator, reporting, and evidence categories; no schema or queue implementation should be shown. |

## 7. Authority Notes Per Diagram

| Diagram | Authority boundary notes |
| --- | --- |
| ExitPass v1.3 Logical Architecture | The diagram must visually center Central PMS as platform control authority, but it must not imply Central PMS owns Vendor PMS raw session lifecycle or POS Server fiscal issuance. |
| Authority Boundary Model | This diagram should explicitly show Vendor PMS/HCP, Central PMS, Payment Orchestrator, Site POS Server, Gate/Exit, Operator Console, Assisted Payment Terminal, and Management Dashboard as separate authority or non-authority roles. |
| Site Group / Site / VendorSystem / Connector Instance / POS Server Topology | Site Group is customer lookup/payment scope. Site is reporting, contract, Vendor PMS mapping, POS Server, and operational boundary. Do not merge the two concepts. |
| Normal Payment-to-Exit Sequence | Payment provider success is not platform finality until Central PMS records finality. ExitAuthorization follows fiscal issuance success or approved exception policy. |
| Payment Finality to Fiscal Issuance to ExitAuthorization Sequence | POS Server is fiscal authority only. It must never be shown issuing ExitAuthorization, opening gates, or declaring payment finality. |
| Vendor PMS Connector Projection and Freshness Flow | Projection and connector health are operational visibility and degraded-support inputs. They must not be shown as financial truth, settlement truth, or tariff authority in normal mode. |
| Degraded Resolve and Continuity Sequence | Continuity is fail-closed by default and requires explicit activation, scope, incident/BCP reference, audit tagging, and reconciliation. It must not look like an automatic alternate path. |
| Assisted Payment Terminal Context and Modes | Assisted Payment Terminal is payment-capable as a channel/terminal, but not an independent POS, payment-finality authority, fiscal authority, or ExitAuthorization authority. |
| Operator Console Governance Boundary | Operator Console is governance/review only. It may approve or review workflows where policy allows, but it must not become a payment app, POS issuer, gate controller, or Central PMS replacement. |
| Management Dashboard Source-of-Truth Boundary | Management Dashboard is read/report/export visibility. It must not activate continuity, approve discounts, mutate fiscal records, issue invoices, declare finality, or issue ExitAuthorization. |
| Audit, Event, and Outbox Conceptual Flow | Audit/event flows must preserve source authority. Event publication should not imply that downstream consumers can alter authoritative records. |

## 8. Diagram Risks to Avoid

| Risk | Affected diagrams | Avoidance guidance |
| --- | --- | --- |
| Treating BRD diagrams as final SDD diagrams without consolidation. | All | Reuse BRD diagrams as inputs, but redraw at SDD level around architecture, authority, and workflow clarity. |
| Blurring Site Group and Site. | Logical Architecture; Topology; Dashboard Boundary | Label Site Group as lookup/payment scope and Site as reporting/vendor/POS/operations boundary. |
| Making projection look authoritative. | Projection Flow; Dashboard Boundary; Degraded Resolve | Label projection/freshness as operational visibility and degraded-support input only. |
| Showing payment provider or Payment Orchestrator as finality authority. | Normal Sequence; Payment Finality Sequence | Show Payment Orchestrator reporting verified provider outcomes and Central PMS declaring platform finality. |
| Showing POS Server issuing ExitAuthorization. | Authority Boundary; Payment Finality Sequence; POS/Fiscal related diagrams | Keep POS Server as fiscal issuance authority only. Central PMS records fiscal reference and issues ExitAuthorization. |
| Showing Assisted Payment Terminal as a separate POS or policy authority. | Assisted Terminal Context; Normal Sequence; Degraded Resolve | Model terminal as a channel/terminal under platform controls and Site POS Server fiscal routing. |
| Showing Operator Console as a payment, gate, or fiscal execution surface. | Operator Console Boundary; Continuity; Audit/Event | Keep it as non-payment governance, review, approval, evidence, and audit surface. |
| Showing Management Dashboard as source of truth. | Dashboard Boundary; Audit/Event | Separate operational visibility from canonical payment, fiscal, reconciliation, and audit records. |
| Making continuity look automatic or always allowed. | Degraded Resolve; Assisted Terminal; Operator Console | Show approval, scope, incident reference, audit/reconciliation tags, and fail-closed branches. |
| Expanding into implementation detail. | All | Do not include endpoint paths, DTOs, database schemas, tables, columns, indexes, queue names, class diagrams, or retry algorithm internals. |
| Overloading one sequence diagram with every exception. | Normal Sequence; Degraded Resolve; Payment Finality Sequence | Keep normal, fiscal handoff, and degraded/continuity sequences separate. |
| Creating duplicate diagrams for every companion BRD. | All | Consolidate repeated companion diagrams into SDD diagrams that explain cross-domain architecture and authority. |

## 9. PlantUML Style Recommendations

Existing v1.3 BRD PlantUML conventions observed:

- Use simple PlantUML diagrams with `@startuml` / `@enduml`.
- Use `title` lines matching the diagram name.
- Use `skinparam shadowing false`.
- Use `skinparam defaultFontName Arial` or equivalent document-consistent font selection.
- Use `skinparam componentStyle rectangle` for component/context diagrams.
- Use `skinparam wrapWidth 200` and centered text alignment for dense component diagrams where needed.
- Use `actor`, `participant`, `rectangle`, and occasional `database` nodes at conceptual level.
- Use notes to document authority constraints and non-authority warnings.
- Use sequence diagrams for payment, fiscal, authorization, degraded, and reconciliation flows.

Recommended SDD PlantUML conventions:

| Area | Recommendation |
| --- | --- |
| Diagram posture | Keep diagrams at system-design level. Prefer conceptual components, actors, authority boundaries, and workflow messages. |
| Naming | Use SDD-specific diagram titles rather than reusing BRD diagram IDs directly. Existing BRD IDs can be cited in SDD notes or source comments if the Lead creates PlantUML later. |
| Authority emphasis | Add concise notes such as "does not declare payment finality", "visibility only", or "fiscal authority only" on diagrams where readers may infer wrong authority. |
| Color and grouping | Use restrained grouping for authority domains, channels, platform core, external systems, and reporting consumers. Avoid excessive color coding. |
| Sequence detail | Use business/action labels, not endpoint names. Avoid DTO names, retry algorithm details, database writes, or class-level operations. |
| Boundary labeling | Use explicit labels for "Authority", "Non-authority consumer", "Visibility only", "Approved governance workflow", and "External dependency" where helpful. |
| Traceability | Each generated diagram should cite source BRD sections in surrounding SDD text, not inside overcrowded diagrams. |
| File generation | The System Design Lead should generate final `.puml` and `.jpg` files only in the final SDD diagram-generation phase, not from this input pack. |

## 10. Diagram Generation Recommendation for System Design Lead

Recommended generation approach:

1. Create the 11 recommended SDD diagrams as the primary v1.3 SDD diagram set unless the System Design Lead intentionally consolidates or defers one with a documented reason.
2. Use the existing BRD `.puml` files as source references only. Do not copy them wholesale into the SDD without adapting them to SDD scope and v1.2-style section placement.
3. Generate final PlantUML and image exports only after all specialist input packs are reviewed for contradictions and scope overlap.
4. Keep diagram placement aligned to the v1.2 outline baseline from the orchestration plan:
   - System Overview / System Context: logical architecture and topology.
   - System Architecture / Trust Boundaries: authority boundary.
   - Core Workflows: normal payment-to-exit and finality/fiscal/authorization sequences.
   - Event Architecture / Observability: projection freshness and audit/event flow.
   - Failure Mode Architecture / Business Continuity: degraded resolve and continuity sequence.
   - Security Architecture / Operational Runbooks: Operator Console governance and terminal mode boundaries where relevant.
5. Prefer fewer high-value SDD diagrams over copying all 35 BRD diagrams. Companion BRD diagrams can remain source references for detailed domain-specific flows.
6. Avoid creating any diagram that looks like a database design, API contract, class model, endpoint map, or implementation architecture for POS Server, connector internals, terminal app internals, or dashboard implementation.

## 11. Open Diagram Questions

The following open items should be preserved as diagram annotations or surrounding SDD notes, not silently resolved in diagrams:

| Question | Source driver | Diagram impact |
| --- | --- | --- |
| Do WebPay URL slugs resolve to Site Group, Site, or both? | ExitPass BRD section 19.1 OQ-002. | Topology and normal payment-to-exit sequence should avoid hardcoding URL resolution beyond approved Site Group/Site semantics. |
| What is the exact degraded tariff freshness threshold? | ExitPass BRD section 19.1 OQ-004; Approval Baseline section 5. | Projection freshness and degraded resolve diagrams should show a freshness threshold concept without a numeric value. |
| What is the exact POS Server deployment and registration model? | ExitPass BRD section 19.1 OQ-005; Approval Baseline section 5. | Logical architecture and topology should show Site-level POS Server concept without deployment topology, registration protocol, or node count detail. |
| Is POS Server a module under Central PMS or a separate service? | ExitPass BRD section 19.1 OQ-006. | Diagrams should show authority separation while leaving deployment/module packaging open. |
| Who has exact BCP activation authority for Continuity Terminal? | ExitPass BRD section 19.1 OQ-007; Continuity BRD section 11; Approval Baseline section 5. | Continuity diagrams should show approved authority/supervisor role generically and avoid naming a final role hierarchy. |
| How should HCP connector health and projection freshness be modeled? | ExitPass BRD section 19.1 OQ-010; MDR BRD sections 21 and 31. | Projection/freshness diagrams should show health/freshness classification conceptually without internal status schema. |
| Should Site Group be user-facing as Payment Scope or Lookup Scope while retaining Site Group concept? | ExitPass BRD section 19.1 OQ-011. | Topology and dashboard diagrams should preserve both terms where useful and avoid final UI naming. |
| What exact MIN/PTU/serial/software/supplier/taxpayer identity assignment applies across Site, POS Server, channel, and terminal? | POS/Invoicing BRD section 10; Approval Baseline section 5. | POS-related SDD diagrams should show Site POS Server fiscal authority without encoding final BIR/accreditation identity assignment. |
| What is the Digital Sales Invoice URL security model? | POS/Invoicing BRD open downstream item in Approval Baseline section 5. | Payment/fiscal diagrams may show digital SI URL as fiscal output concept only, without token/security mechanics. |
| What dashboard implementation details are approved? | Approval Baseline section 5; MDR BRD sections 33-37. | Dashboard source-of-truth diagram should show sources and authority boundaries only, not BI tooling or storage implementation. |

## 12. Summary for System Design Lead

The existing v1.3 BRD set contains 35 source diagrams across the core BRD and companion BRDs. They provide strong coverage of context, Site/Site Group topology, normal payment-to-exit, fiscal sequencing, continuity, assisted terminal modes, Operator Console governance, dashboard source-of-truth boundaries, and POS/Invoicing flows.

For the SDD, the recommended set is 11 consolidated diagrams:

1. ExitPass v1.3 Logical Architecture
2. Authority Boundary Model
3. Site Group / Site / VendorSystem / Connector Instance / POS Server Topology
4. Normal Payment-to-Exit Sequence
5. Payment Finality to Fiscal Issuance to ExitAuthorization Sequence
6. Vendor PMS Connector Projection and Freshness Flow
7. Degraded Resolve and Continuity Sequence
8. Assisted Payment Terminal Context and Modes
9. Operator Console Governance Boundary
10. Management Dashboard Source-of-Truth Boundary
11. Audit, Event, and Outbox Conceptual Flow

The highest-risk diagramming issue is authority drift. Every diagram should preserve the approved model:

- Vendor PMS/HCP owns raw parking session lifecycle and normal tariff computation.
- Central PMS owns payment-linked platform control state, payment finality, fiscal issuance reference recording, and ExitAuthorization.
- Payment Orchestrator reports verified provider outcomes but does not declare platform payment finality.
- Site POS Server owns fiscal issuance and fiscal records but does not issue ExitAuthorization.
- Assisted Payment Terminal, Operator Console, Management Dashboard, WebPay, APM, and other channels/modules must not bypass Central PMS or POS Server authority.
- Gate/exit execution consumes Central PMS authorization and must not bypass Central PMS authority.

Final SDD diagrams should be generated later by the System Design Lead after all input packs are reviewed. This pack intentionally does not create `.puml` or `.jpg` files.
