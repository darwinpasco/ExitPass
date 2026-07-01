# ExitPass System Design v1.3 Orchestration Plan

## 1. Purpose

This orchestration plan prepares the workspace for drafting `docs/v1.3/ExitPass_System_Design_v1.3.md`.

The System Design v1.3 document must be produced as a controlled successor to ExitPass System Design v1.2. This plan defines the approved input baseline, specialist input-pack boundaries, integration rules, review gates, and validation commands that must be satisfied before the System Design Lead drafts the final System Design.

This plan does not draft the final System Design.

## 2. Approved Input BRD Baseline

The following documents are the approved v1.3 BRD baseline for the System Design v1.3 drafting effort:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Any contradiction, ambiguity, or apparent gap found in these inputs must be reported in the relevant specialist input pack. Specialist agents must not silently correct approved source documents.

## 3. v1.2 System Design Style/Outline Rule

ExitPass System Design v1.3 must use ExitPass System Design v1.2 from `D:\Docs\ExitPass\v1.2` as the writing-style and top-level outline baseline.

The v1.3 System Design must read like a controlled successor to v1.2, not a new document format. The System Design Lead must preserve the v1.2 document posture, section order, and engineering tone unless a v1.3 requirement clearly requires a controlled addition.

The v1.2 top-level outline baseline is:

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

The v1.3 System Design may refine section titles to reflect v1.3 scope, but it must not introduce a new document structure unless the change is explicitly justified in the final System Design drafting notes.

## 4. Agent Input-Pack List

Specialist agents will create the following input packs later:

- `docs/v1.3/system-design/input-packs/01_authority_model_review.md`
- `docs/v1.3/system-design/input-packs/02_traceability_map.md`
- `docs/v1.3/system-design/input-packs/03_workflow_and_state_input.md`
- `docs/v1.3/system-design/input-packs/04_security_trust_and_rbac_input.md`
- `docs/v1.3/system-design/input-packs/05_observability_reporting_and_operations_input.md`
- `docs/v1.3/system-design/input-packs/06_diagram_inventory_and_puml_inputs.md`
- `docs/v1.3/system-design/input-packs/07_scope_guard_and_consistency_review.md`

Each input pack must cite the approved BRD source sections it relies on and must separate confirmed requirements from assumptions, contradictions, and unresolved questions.

## 5. File Ownership Rules

- Specialist agents may create only their assigned input-pack file.
- Specialist agents must not edit the final System Design.
- Specialist agents must not edit approved BRDs.
- Specialist agents must not create API, database, or engineering implementation details.
- Specialist agents must not stage, delete, or overwrite files created by other agents.
- The System Design Lead integrates the final SDD only after all input packs exist.
- Any contradiction must be reported in the relevant input pack, not silently corrected in source documents.

## 6. System Design Lead Integration Rules

- The System Design Lead owns `docs/v1.3/ExitPass_System_Design_v1.3.md`.
- The System Design Lead must wait until all seven input packs exist before drafting the final System Design.
- The System Design Lead must reconcile the input packs against the approved BRD baseline before writing final content.
- The System Design Lead must preserve the v1.2 writing style, top-level outline, document control posture, and controlled-successor framing.
- The System Design Lead must keep design content at system-design level and must not expand into companion API, database, engineering pack, or implementation specifications.
- The System Design Lead must carry unresolved contradictions forward as explicit open items or review notes rather than resolving them by changing approved BRDs.
- The System Design Lead must ensure diagrams and PlantUML references are inventoried before they are integrated into the final System Design.

## 7. Review Gates

The System Design v1.3 drafting effort must pass these gates:

1. Orchestration workspace exists with this plan and the `input-packs` directory.
2. All seven specialist input packs exist in `docs/v1.3/system-design/input-packs/`.
3. Each input pack is limited to its assigned scope and does not alter approved BRDs, source code, schema, API contracts, or companion technical designs.
4. The System Design Lead reviews all input packs for contradictions, duplicate claims, missing traceability, and scope creep.
5. The System Design Lead confirms the v1.2 outline/style baseline before drafting the final System Design.
6. The final System Design is drafted only after the prior gates pass.
7. Validation commands are run after orchestration setup and again after final drafting.

## 8. Out-of-Scope Items

The following are out of scope for this orchestration setup:

- Drafting `docs/v1.3/ExitPass_System_Design_v1.3.md`.
- Modifying source code.
- Modifying database schema.
- Modifying API contracts.
- Creating DOCX files.
- Drafting companion technical designs.
- Drafting a Database Design, API Contract Pack, or Engineering Pack.
- Editing approved BRD baseline documents.
- Creating specialist input packs before specialist assignment.
- Staging or committing changes.

## 9. Validation Commands

Run these commands from `D:\SourceCodes\ExitPass` after orchestration setup:

```powershell
git status --short --untracked-files=all
git diff --check
```

Expected result:

- Only Markdown orchestration files under `docs/v1.3/system-design/` are added.
- No source code, database schema, API contract, BRD baseline, DOCX, or companion technical design files are modified.

## 10. Next Step

Assign specialist agents to create the seven input packs listed in this plan. The System Design Lead must stop before drafting the final System Design until all seven input packs exist and pass the review gates.
