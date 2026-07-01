# ExitPass Continuity System Design Orchestration Plan

Status: Orchestration setup only

Date: 2026-07-02

## 1. Purpose

This orchestration plan prepares the workspace for the future ExitPass Continuity System Design v1.0.

The plan defines source inputs, design scope, authority guardrails, continuity operating-state guardrails, specialist input-pack ownership, Lead integration rules, review gates, and validation commands. It does not draft the final Continuity System Design.

ExitPass Continuity is a controlled degraded-operation capability. It must remain explicit, audited, incident-tagged, reconciliation-tagged, time-bound, and subject to post-restoration review. It must not become a silent alternate operating mode.

## 2. Target Document

| Target | Path | Status |
| --- | --- | --- |
| Continuity System Design v1.0 | `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md` | To be drafted later by the Lead after specialist input packs exist. |
| Continuity System Design diagrams | `docs/v1.3/continuity/system-design/diagrams/` | To be created later by the Lead during final design drafting. |
| Specialist input packs | `docs/v1.3/continuity/system-design/input-packs/` | Folder prepared by this setup task; files are to be created later by specialists. |

## 3. Approved Baseline Inputs

The later Lead synthesis must use these approved v1.3 inputs:

| Source | Use |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 authority model, degraded operation boundaries, fiscal-before-exit rule, projection limits, and Continuity positioning. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System-level continuity architecture, fail-closed rules, trust boundaries, state ownership, observability, and downstream deferrals. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approved BRD baseline status and downstream open-question discipline. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Primary business source for Continuity scope, states, activation, degraded scenarios, Continuity Terminal restrictions, manual release, fiscal exceptions, reconciliation, and post-restoration review. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Business requirements for Continuity Terminal as restricted APT mode. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` | Technical posture for Continuity Terminal mode, terminal trust, restricted operation, degraded context display, and governance handoff. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Non-payment governance boundary, supervisor review, continuity activation/deactivation review, manual release governance, fiscal exception review, and post-restoration review. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Visibility/reporting boundary for continuity state, degraded visibility, fiscal exceptions, manual release counts, and reconciliation backlog. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal authority, fiscal issuance exception posture, offline fiscal restrictions, and Sales Invoice before normal ExitAuthorization. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Connector health, projection freshness, live resolve, fee calculation, vendor acknowledgment, and connector non-authority posture. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HCP-specific projection, one-minute polling baseline, fee calculation, `cardNum` uncertainty, and conditional `parkingfee/confirm` posture. |

Planning artifacts:

| Source | Use |
| --- | --- |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved decisions for continuity capability, degraded projection use, fiscal-before-exit, and authority boundaries. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Open questions for degraded freshness threshold, Continuity Terminal activation authority, connector health/freshness modeling, and downstream design details. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Impact map for degraded resolve, Continuity, fiscal exceptions, API/database/engineering deferrals, and Test/UAT implications. |

Business diagram input:

| Source | Use |
| --- | --- |
| `docs/v1.3/continuity/diagrams/` | Business-level diagram input only. These diagrams should inform final System Design diagram planning but must not be modified by specialists during input-pack work. |

## 4. Continuity System Design Scope

The future Continuity System Design should cover:

- Continuity capability architecture and component boundaries.
- Continuity activation and deactivation flow.
- Continuity operating states at conceptual level.
- Degraded-watch and degraded-active behavior.
- Vendor PMS/HCP unavailable or stale connector handling.
- Projection freshness, ambiguity, and sufficiency handling.
- Degraded resolve decisioning through Central PMS under approved policy.
- Degraded tariff basis handoff and approved tariff configuration posture.
- Continuity Terminal activation and restricted operation.
- Continuity-mode statutory discount restrictions.
- Payment uncertainty handling.
- Fiscal issuance failure, timeout, pending-exit, and fiscal exception handling.
- Vendor payment acknowledgment failure and reconciliation posture.
- Gate/exit issue and manual release governance handoff.
- Operator Console governance touchpoints.
- Management Dashboard and Reporting visibility touchpoints.
- Audit, incident, reconciliation, and post-restoration review posture.
- Fail-closed rules.
- Open questions and downstream API/database/engineering/UAT/runbook deferrals.

The design must remain a System Design. It must not become an API Contract, Database Design, Engineering Pack, Runbook Pack, UAT Pack, POS Server design, Operator Console design, Assisted Payment Terminal design, Vendor PMS Connector design, or HikCentral profile.

## 5. Authority Model Guardrails

The later final design and all specialist input packs must preserve these rules:

- Continuity is explicit controlled degraded operation, not silent fallback.
- Continuity Terminal is a restricted degraded/BCP mode of Assisted Payment Terminal.
- Continuity Terminal is disabled by default.
- Vendor PMS / HCP remains authority for raw parking session lifecycle and normal tariff computation in normal mode.
- Central PMS remains authority for payment-linked state, TariffSnapshot, payment finality, fiscal issuance reference recording, degraded resolve decision under approved policy, and ExitAuthorization.
- Central PMS owns degraded resolve decisioning under approved Continuity policy.
- Central PMS / Discount workflow owns statutory discount policy resolution, validation persistence, and payable-basis update.
- Vendor PMS Connector / HikCentral Connector reports vendor facts, health, projection freshness, and normalized outcomes, but does not approve degraded resolve.
- Projection data is operational visibility and controlled degraded support only.
- Projection is not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- POS Server remains resolved Site fiscal issuance authority.
- Fiscal issuance must succeed before normal ExitAuthorization unless a separately approved exception/manual-release policy applies.
- Fiscal issuance failure or timeout does not automatically authorize exit.
- Manual release is last-resort governed exception, not normal ExitAuthorization.
- Gate/exit execution consumes Central PMS authorization and must not bypass Central PMS.
- Operator Console is separate non-payment governance.
- Management Dashboard is visibility/reporting only.
- Continuity-origin activity requires audit, incident, reconciliation, and post-restoration review tagging.

## 6. Continuity Operating-State Guardrails

The later design should preserve these conceptual continuity states:

| Conceptual state | Meaning |
| --- | --- |
| Normal | No approved degraded operation is active for the scope. Normal Vendor PMS/HCP, Central PMS, POS Server, and gate authority model applies. |
| Degraded-watch | A dependency is degraded, stale, at risk, or under observation, but continuity workflows are not yet active. |
| Degraded-active | Approved degraded controls are active for a defined Site/Site Group/dependency scope. |
| Continuity Terminal active | Continuity Terminal mode is enabled for authorized terminals, users, Sites/Site Groups, and workflows within approved activation scope. |
| Restoration-in-progress | Affected dependency is returning to service, continuity-only workflows are being disabled or limited, and activity is being prepared for review. |
| Post-restoration review | Continuity-origin activity is under reconciliation, audit review, exception review, and operational closure checks. |
| Closed / reconciled | Continuity event is closed and required reconciliation and review are complete. |

These are design-level state concepts only. Exact state names, API statuses, database values, event payloads, workflow transitions, timers, alert thresholds, and runbook procedures remain deferred.

The design must not use these states to bypass Central PMS authority, POS Server fiscal authority, Operator Console governance, or reconciliation controls.

## 7. Specialist Input-Pack List

Specialist agents should create these files later, one file per assigned specialist:

| Input pack | Assigned focus | Expected output |
| --- | --- | --- |
| `docs/v1.3/continuity/system-design/input-packs/01_continuity_authority_scope_guard.md` | Continuity authority boundaries, source contradictions, non-authority scope, terminology normalization, and approved/deferred decisions. | Guardrail matrix, contradiction log, non-authority list, and open-question preservation notes. |
| `docs/v1.3/continuity/system-design/input-packs/02_degraded_workflow_and_state.md` | Degraded-watch/degraded-active workflows, activation/deactivation, projection freshness, degraded resolve, degraded tariff basis, Continuity Terminal state, vendor/connector/gate/payment uncertainty paths, and fail-closed behavior. | Workflow and state recommendations without endpoint, DTO, table, event, or runbook detail. |
| `docs/v1.3/continuity/system-design/input-packs/03_reconciliation_manual_release_fiscal_exception.md` | Fiscal issuance failure/timeout, pending-exit handling, manual release governance, vendor acknowledgment failure, reconciliation tagging, post-restoration review, audit evidence, and reporting handoff. | Exception/reconciliation design input without POS Server internals, final fiscal recovery mechanics, API contracts, or database design. |
| `docs/v1.3/continuity/system-design/input-packs/04_diagram_planning.md` | Existing Continuity BRD diagram review, recommended System Design diagram set, component list, authority labels, diagram risks, and PlantUML style guidance. | Diagram plan only; no final diagram files unless later authorized by Lead. |

## 8. File Ownership Rules

- Specialist agents may create only their assigned input-pack file.
- Specialist agents must not edit final documents.
- Specialist agents must not edit approved BRDs, ExitPass System Design, connector designs, Assisted Payment Terminal design, or diagrams.
- Specialist agents must not modify `docs/v1.3/continuity/diagrams/`.
- Specialist agents must not create API/database/engineering implementation details.
- Specialist agents must not create final Continuity System Design diagrams.
- Lead integrates the final document only after all input packs exist.
- Any contradiction must be reported in the relevant input pack, not silently corrected in approved sources.

## 9. Lead Integration Rules

The Lead integration pass shall:

- Verify that all four specialist input packs exist before drafting the final Continuity System Design.
- Preserve the approved v1.3 authority model.
- Keep Central PMS, Vendor PMS/HCP, Vendor PMS Connector/HikCentral Connector, POS Server, Assisted Payment Terminal, Operator Console, Management Dashboard, Payment Orchestrator, Gate/exit, Audit/Event, and Reconciliation boundaries distinct.
- Use Continuity BRD as the primary business source.
- Use ExitPass System Design v1.3 as the platform-level architecture authority.
- Use Assisted Payment Terminal System Design for Continuity Terminal technical boundary and terminal display/handoff posture.
- Use Vendor PMS Connector and HikCentral Connector designs for connector health, projection freshness, live resolve, fee calculation, and source-gap posture.
- Use POS/Invoicing BRD for fiscal authority and fiscal exception constraints.
- Use Operator Console BRD for governance, activation review, fiscal exception review, manual release review, and post-restoration review boundaries.
- Use Management Dashboard BRD for visibility/reporting and operational-versus-financial truth boundaries.
- Treat existing Continuity BRD diagrams as business context only.
- Carry unresolved decisions forward instead of inventing final API statuses, DTOs, database values, event payloads, thresholds, timers, retry policies, runbook steps, or UAT scripts.
- Create final System Design diagrams only during the later Lead synthesis task.

## 10. Out-of-Scope Items

This orchestration task and the specialist input packs must not:

- Draft the final Continuity System Design.
- Modify source code.
- Modify database schema.
- Modify API contracts.
- Create DOCX files.
- Modify approved BRDs.
- Modify ExitPass System Design v1.3.
- Modify Vendor PMS Connector System Design.
- Modify HikCentral Connector Profile.
- Modify Assisted Payment Terminal System Design.
- Draft Database/API/Engineering Pack.
- Draft Test/UAT Pack.
- Draft Runbook Pack.
- Create final System Design diagrams.
- Define endpoint paths, DTOs, database tables, database enum values, event payloads, queue names, retry counts, timers, alert thresholds, implementation classes, deployment scripts, UAT scripts, or runbook procedures.
- Approve offline fiscal issuance, offline payment, unmanaged degraded tariff basis, unmanaged manual release, or continuity behavior outside approved governance.

## 11. Review Gates

| Gate | Requirement |
| --- | --- |
| Gate 1: Workspace setup | Orchestration plan exists and `input-packs/` folder is prepared. |
| Gate 2: Specialist ownership | Each specialist creates only the assigned input-pack file. |
| Gate 3: Source alignment | Each input pack cites approved v1.3 sources and reports contradictions instead of editing approved documents. |
| Gate 4: Authority review | Input packs preserve Central PMS, Vendor PMS/HCP, POS Server, Operator Console, Management Dashboard, APT, connector, and gate/exit boundaries. |
| Gate 5: Continuity state review | Conceptual states remain business/design concepts and are not converted into database enums, API statuses, or runbook procedures. |
| Gate 6: Fail-closed review | Stale, ambiguous, insufficient, unknown, unsafe, or unapproved states fail closed or route to approved governance. |
| Gate 7: Reconciliation review | Continuity-origin activity remains incident-tagged, audit-tagged, reconciliation-tagged, and subject to post-restoration review. |
| Gate 8: Deferral review | Endpoint, DTO, database, event, retry, threshold, timer, runbook, and UAT details remain deferred. |
| Gate 9: Lead readiness | All four input packs exist and are internally consistent enough for Lead synthesis. |

## 12. Validation Commands

Run these commands after orchestration setup:

```powershell
git status --short --untracked-files=all
git diff --check
```

Expected result for this setup task:

- Only Markdown orchestration files/folders under `docs/v1.3/continuity/system-design/` are added.
- No source code changes.
- No database/schema changes.
- No API contract changes.
- No DOCX files.
- No approved BRD changes.
- No ExitPass System Design changes.
- No connector design changes.
- No Assisted Payment Terminal System Design changes.
- No final Continuity System Design draft.
- No diagram generation.
- No commit.

## 13. Next Step

Create the four specialist input-pack files in the assigned folder:

1. `01_continuity_authority_scope_guard.md`
2. `02_degraded_workflow_and_state.md`
3. `03_reconciliation_manual_release_fiscal_exception.md`
4. `04_diagram_planning.md`

After all four input packs exist and pass the review gates, the Lead may draft `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md` and create the System Design diagram set under `docs/v1.3/continuity/system-design/diagrams/`.

