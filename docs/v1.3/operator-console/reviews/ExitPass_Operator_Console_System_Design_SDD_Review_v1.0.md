# ExitPass Operator Console System Design SDD Review v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Review document | ExitPass Operator Console System Design SDD Review |
| Reviewed document | `docs/v1.3/operator-console/ExitPass_Operator_Console_System_Design_SDD_v1.0.md` |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | `docs/v1.3-operator-console-system-design` |
| Decision | ready_for_review |

## 2. Scope Reviewed

The review covered the Operator Console as an internal ExitPass v1.3 operations and governance surface.

Reviewed scope includes:

- Authentication, RBAC, device trust, Site/Site Group assignment, and shift validation.
- Ticket scan and site-scoped session lookup.
- Read-only session, payment, ExitAuthorization, and fiscal context display.
- Senior Citizen and PWD statutory discount validation.
- Evidence capture, evidence minimization, privacy notice, and audit controls.
- Supervisor review and override.
- Audit and operational report access.
- Fiscal exception read-only visibility and handoff.
- Management Dashboard handoff.
- Non-goals and authority boundaries.

## 3. Files Inspected

| File | Review use |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 authority and acceptance baseline. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System architecture, authority boundaries, trust model, and observability. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator Console business requirements and non-payment boundary. |
| `docs/v1.3/system-design/input-packs/04_security_trust_and_rbac_input.md` | RBAC, device trust, evidence privacy, and audit posture. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` | Separation from payment-capable terminal workflows. |
| `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md` | Continuity/manual release/fiscal exception governance boundaries. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Reporting and dashboard handoff boundary. |
| `docs/v1.3/central-pms/engineering-pack/08-operator-console-queues-plan.md` | Future fiscal exception queue planning. |
| `docs/v1.3/central-pms/engineering-pack/09-dashboard-visibility-plan.md` | Dashboard fiscal visibility handoff posture. |
| `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Post_Run_Review_v1.0.md` | Closed controlled UAT lessons and follow-up boundaries. |
| `docs/v1.3/diagrams/system-design/D-09_Operator_Console_Governance_Boundary.puml` | Existing Operator Console governance boundary. |
| `docs/v1.3/operator-console/diagrams/*.puml` | Existing Operator Console diagram concepts. |

## 4. Boundary Checks

| Boundary | Result | Notes |
| --- | --- | --- |
| Central PMS owns payment finality | Pass | SDD only permits read-only payment status display. |
| Central PMS owns fiscal reference recording | Pass | SDD treats fiscal reference state as Central PMS-recorded context. |
| Central PMS owns normal ExitAuthorization | Pass | SDD explicitly prohibits Operator Console ExitAuthorization issuance. |
| POS Server owns fiscal issuance and numbering only | Pass | SDD prohibits POS Server command execution and direct POS Server access from the console. |
| Payment Orchestrator provider-only boundary | Pass | SDD prohibits direct provider interaction. |
| Vendor PMS session/tariff authority | Pass | SDD uses normalized Central PMS-approved lookup and does not allow tariff recalculation. |
| Gate Integration consumes Central PMS authorization | Pass | SDD prohibits gate opening from Operator Console. |
| Manual release is not normal ExitAuthorization | Pass | SDD repeats manual release governance boundary and defers policy detail. |

## 5. Non-Payment Authority Check

Result: Pass.

The SDD explicitly states that Operator Console does not:

- collect payment;
- manually confirm payment;
- reverse, refund, or void payment;
- interact directly with payment providers;
- declare platform payment finality;
- mutate payable basis directly.

Payment context is read-only and displayed only where needed for operator context.

## 6. Fiscal Authority Check

Result: Pass.

The SDD keeps fiscal visibility read-only. It does not design:

- fiscal issuance command execution;
- POS Server direct calls;
- fiscal retry;
- fiscal readback;
- fiscal writeback;
- fiscal exception closure mechanics.

Those items are deferred to the later Fiscal Exception Queue / Readback / Retry design.

## 7. Discount / Evidence / Privacy Check

Result: Pass.

The SDD covers:

- Senior Citizen and PWD workflows;
- privacy notice before evidence capture;
- structured metadata and evidence references;
- no unmanaged local raw ID image storage;
- no raw provider payloads, secrets, PAN/CVV, unmanaged customer PII, or uncontrolled evidence blobs;
- evidence access audit;
- supervisor review and reason-coded override.

Open details remain correctly deferred: final retention periods, redaction rules, duplicate/fraud scoring rules, and allowed evidence media by Site/jurisdiction.

## 8. Operations / Audit Check

Result: Pass.

The SDD covers:

- authentication and denial events;
- device trust validation;
- Site/Site Group and shift validation;
- lookup attempts and outcomes;
- discount case events;
- evidence access/export;
- supervisor override;
- fiscal status views;
- report access/export.

Audit failure is modeled as fail-closed for sensitive actions.

## 9. Management Dashboard Handoff Check

Result: Pass.

The SDD explicitly hands off aggregate operational metrics, fiscal exception trends, discount validation trends, connector/projection health, and financial/fiscal/reconciliation reports to Management Dashboard and Reporting.

It preserves the dashboard as visibility/reporting only and prevents dashboard source confusion.

## 10. Fiscal Exception Queue Handoff Check

Result: Pass.

The SDD includes a future-facing fiscal exception entry point while deferring:

- retry/readback decisions;
- POS Server GET readback mechanics;
- exception closure rules;
- manual release under fiscal exception;
- fiscal exception SLA and assignment;
- dashboard fiscal visibility projection store.

This matches the controlled UAT post-run closure note that Operator Console exception queues remain separate future work.

## 11. Gaps or Open Decisions

Open decisions are acceptable for this SDD level:

- Exact endpoint paths and DTOs.
- Whether implementation uses a dedicated Operator Console BFF/API layer.
- Exact permission matrix.
- Exact device trust mechanism.
- Exact shift service/source.
- Exact evidence retention/redaction/media policy.
- Exact statutory discount duplicate/fraud scoring rules.
- Exact fiscal exception queue mechanics.
- Exact Management Dashboard projection model.
- Exact local/device cache policy.

No gap blocks review because the SDD identifies these as downstream decisions and does not invent database tables, columns, endpoint contracts, or implementation classes.

## 12. Diagram Review

Diagrams reviewed:

| ID | Diagram | Result |
| --- | --- | --- |
| OC-D01 | `docs/v1.3/operator-console/diagrams/OC-D01_Operator_Console_System_Context.puml` | Pass |
| OC-D02 | `docs/v1.3/operator-console/diagrams/OC-D02_Operator_Console_Authority_Boundary.puml` | Pass |
| OC-D03 | `docs/v1.3/operator-console/diagrams/OC-D03_Operator_Console_Runtime_Component_Model.puml` | Pass |
| OC-D04 | `docs/v1.3/operator-console/diagrams/OC-D04_Login_Device_Site_Shift_Validation_Sequence.puml` | Pass |
| OC-D05 | `docs/v1.3/operator-console/diagrams/OC-D05_Ticket_Lookup_and_Statutory_Discount_Validation_Sequence.puml` | Pass |
| OC-D06 | `docs/v1.3/operator-console/diagrams/OC-D06_Fiscal_Status_Visibility_and_Exception_Handoff.puml` | Pass |
| OC-D07 | `docs/v1.3/operator-console/diagrams/OC-D07_Supervisor_Review_Evidence_Audit_Flow.puml` | Pass |

Rendered JPEG attachments reviewed:

- `docs/v1.3/operator-console/diagrams/OC-D01_Operator_Console_System_Context.jpg`
- `docs/v1.3/operator-console/diagrams/OC-D02_Operator_Console_Authority_Boundary.jpg`
- `docs/v1.3/operator-console/diagrams/OC-D03_Operator_Console_Runtime_Component_Model.jpg`
- `docs/v1.3/operator-console/diagrams/OC-D04_Login_Device_Site_Shift_Validation_Sequence.jpg`
- `docs/v1.3/operator-console/diagrams/OC-D05_Ticket_Lookup_and_Statutory_Discount_Validation_Sequence.jpg`
- `docs/v1.3/operator-console/diagrams/OC-D06_Fiscal_Status_Visibility_and_Exception_Handoff.jpg`
- `docs/v1.3/operator-console/diagrams/OC-D07_Supervisor_Review_Evidence_Audit_Flow.jpg`

Boundary confirmations:

- Non-payment boundary preserved.
- Non-fiscal authority boundary preserved.
- No direct POS Server call from Operator Console.
- No direct provider, Vendor PMS, or gate calls from Operator Console.
- Fiscal visibility remains read-only.
- Statutory discount validation remains backend-owned by Central PMS / Discount workflow.
- Evidence privacy and audit controls are represented.
- Supervisor override is shown as audited and non-mutating for payment finality, ExitAuthorization, gate behavior, and fiscal issuance.

Rendered JPEG files are attached to the SDD. The PlantUML source links are retained next to each rendered image.

## 13. Decision

Decision: ready_for_review.

Rationale:

- Required sections are present.
- Required PlantUML diagrams are present, rendered to JPEG, and embedded in the SDD.
- Authority boundaries are preserved.
- Operator Console remains an operations and governance surface, not payment/fiscal/exit/gate authority.
- Fiscal visibility is read-only.
- Future Fiscal Exception Queue and Management Dashboard designs have clear handoff points.
- No source code, SQL, migrations, generated artifacts, DOCX files, runtime configuration, or POS Server repository changes are required by this SDD.
