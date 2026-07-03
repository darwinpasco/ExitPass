# ExitPass Management Dashboard and Reporting System Design SDD Review v1.0

## Document control

| Field | Value |
| --- | --- |
| Review target | `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_System_Design_SDD_v1.0.md` |
| Version | v1.0 |
| Branch | `docs/v1.3-management-dashboard-reporting-system-design` |
| Review type | Documentation/design boundary review |
| Status | ready_for_review |
| Last updated | 2026-07-03 |

## Scope reviewed

The review covered the Management Dashboard and Reporting SDD, including system boundary, runtime model, read-model/projection posture, dashboard/report domains, fiscal visibility projection posture, RBAC/export controls, audit/evidence handling, failure modes, implementation roadmap, acceptance criteria, traceability, and the seven MDR PlantUML/JPEG diagrams.

## Files inspected

| File | Purpose |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core authority, projection, reporting, fiscal, payment, exit, audit, and degraded-mode requirements. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System authority model and component responsibilities. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` | Operator Console boundary and dashboard handoff. |
| `docs/v1.3/operator-console/reviews/ExitPass_Operator_Console_System_Design_SDD_Review_v1.0.md` | Operator Console review posture. |
| `docs/v1.3/operator-console/diagrams/*` | Existing diagram convention and handoff references. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Management Dashboard requirements baseline. |
| `docs/v1.3/management-dashboard-reporting/diagrams/*` | Existing Management Dashboard rendered diagram convention. |
| `docs/v1.3/central-pms/engineering-pack/09-dashboard-visibility-plan.md` | Fiscal dashboard projection posture. |
| `docs/v1.3/central-pms/engineering-pack/08-operator-console-queues-plan.md` | Fiscal exception queue planning and handoff posture. |
| `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Post_Run_Review_v1.0.md` | Controlled UAT outcome and deferred dashboard/fiscal exception follow-up. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Vendor connector health/projection context. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HikCentral connector context. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` | Assisted terminal and statutory discount context. |
| `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md` | Continuity, manual release, degraded-mode, and reconciliation reporting context. |
| `docs/v1.3/pos-server/ExitPass_POS_Server_System_Design_v1.0.md` | POS Server fiscal authority and reporting context. |
| `docs/v1.3/pos-server-api/ExitPass_Central_PMS_to_POS_Server_Fiscal_Issuance_Integration_Contract_v1.0.md` | Central PMS to POS Server fiscal evidence boundary. |

## Boundary checks

| Boundary | Result |
| --- | --- |
| Central PMS owns payment finality, fiscal reference recording, and normal ExitAuthorization. | Passed |
| POS Server owns fiscal issuance and fiscal numbering only. | Passed |
| Payment Orchestrator owns provider interaction and verified provider evidence reporting only. | Passed |
| Vendor PMS remains raw parking-session lifecycle and tariff authority. | Passed |
| Gate Integration acts only through Central PMS authorization. | Passed |
| Operator Console remains operator workflow/governance surface only. | Passed |
| Management Dashboard owns visibility/reporting only. | Passed |

## Visibility-only authority check

Passed. The SDD defines Management Dashboard as a read-only projection/reporting layer. It consumes approved read models and projections and does not mutate source-of-truth records.

## Non-payment authority check

Passed. The SDD explicitly prohibits payment collection, confirmation, paid-state marking, refund, reversal, void, and provider interaction.

## Fiscal authority check

Passed. Fiscal visibility is read-only. POS Server fiscal evidence is shown only after Central PMS records it. Retry, readback, writeback, fiscal issuance, and closure mechanics are deferred to the future Fiscal Exception Queue / Readback / Retry design.

## Discount/compliance check

Passed. The dashboard reports Senior Citizen/PWD activity, compliance, duplicates, fraud signals, evidence policy posture, and override rates. It does not approve, reject, or override discounts.

## Reconciliation/continuity check

Passed. Reconciliation, settlement, continuity, manual release, degraded-mode, and post-restoration items are visibility/reporting only. The dashboard does not activate continuity, approve manual release, or close reconciliation exceptions.

## Reporting/export/privacy check

Passed. The SDD requires RBAC, Site/Site Group scope, redaction, explicit export permission, source labels, freshness labels, and audit logging for report view/export/evidence access.

## Projection/freshness/staleness check

Passed. The SDD requires source basis labels, last refreshed timestamps, stale/unavailable/partial data indicators, and clear separation between operational projection visibility and canonical financial/fiscal records.

## Diagram review

| Diagram | Source | Rendered JPEG | Review result |
| --- | --- | --- | --- |
| MDR-D01 Management Dashboard System Context | `diagrams/MDR-D01_Management_Dashboard_System_Context.puml` | `diagrams/MDR-D01_Management_Dashboard_System_Context.jpg` | Preserves backend-service-only interaction and no direct POS Server/provider/vendor/gate calls. |
| MDR-D02 Management Dashboard Authority Boundary | `diagrams/MDR-D02_Management_Dashboard_Authority_Boundary.puml` | `diagrams/MDR-D02_Management_Dashboard_Authority_Boundary.jpg` | Preserves visibility-only dashboard boundary. |
| MDR-D03 Dashboard Runtime Component Model | `diagrams/MDR-D03_Dashboard_Runtime_Component_Model.puml` | `diagrams/MDR-D03_Dashboard_Runtime_Component_Model.jpg` | Shows guarded dashboard modules and projection consumers. |
| MDR-D04 Data Source and Projection Model | `diagrams/MDR-D04_Data_Source_and_Projection_Model.puml` | `diagrams/MDR-D04_Data_Source_and_Projection_Model.jpg` | Shows read-model flow with source/freshness labels. |
| MDR-D05 Fiscal Visibility Projection and Exception Handoff | `diagrams/MDR-D05_Fiscal_Visibility_Projection_and_Exception_Handoff.puml` | `diagrams/MDR-D05_Fiscal_Visibility_Projection_and_Exception_Handoff.jpg` | Keeps fiscal retry/readback/writeback outside dashboard. |
| MDR-D06 Report Access RBAC Export Audit Sequence | `diagrams/MDR-D06_Report_Access_RBAC_Export_Audit_Sequence.puml` | `diagrams/MDR-D06_Report_Access_RBAC_Export_Audit_Sequence.jpg` | Shows RBAC, redaction, export permission, and audit paths. |
| MDR-D07 Operational Failure and Staleness Handling | `diagrams/MDR-D07_Operational_Failure_and_Staleness_Handling.puml` | `diagrams/MDR-D07_Operational_Failure_and_Staleness_Handling.jpg` | Shows stale/unavailable/partial data safety behavior. |

Diagram review result: passed.

## Fiscal Exception Queue handoff check

Passed. The SDD includes fiscal exception backlog and navigation/handoff only. Retry, readback, writeback, recovery, and closure remain deferred to the later Fiscal Exception Queue / Readback / Retry design.

## Operator Console handoff check

Passed. The SDD consumes Operator Console activity summaries and can link users to workflow surfaces, but it does not execute Operator Console actions or become discount/evidence approval authority.

## Gaps or open decisions

- Exact dashboard delivery technology and BFF adoption.
- Exact report catalog for the first implementation phase.
- Exact role matrix and report-scope assignments.
- Exact projection/read-model implementation and storage.
- Freshness thresholds and stale warning rules by domain.
- Export retention and storage governance.
- Exact evidence/statutory discount redaction rules.
- Fiscal Exception Queue read model and handoff route.
- Reconciliation source and allowed drilldown levels.
- External BI/export governance.

## Decision

Decision: ready_for_review.

The SDD is complete for design review. It preserves Management Dashboard as a visibility/reporting surface only and leaves authority-changing actions to their owning domains or later approved designs.
