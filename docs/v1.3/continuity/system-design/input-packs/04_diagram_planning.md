# Continuity System Design Diagram Planning Input Pack

Status: Specialist diagram planning input only

Branch: `docs/v1.3-continuity-system-design`

Assigned output: `docs/v1.3/continuity/system-design/input-packs/04_diagram_planning.md`

## 1. Purpose

This input pack recommends the diagram set for the future ExitPass Continuity System Design. It plans diagram coverage, purpose, component boundaries, authority labels, and diagram risks only.

This pack does not draft the final Continuity System Design, create final diagrams, create PlantUML files, create image files, define implementation details, or modify approved source documents.

The diagram plan must preserve the approved v1.3 continuity posture:

- Continuity is explicit, controlled degraded operation, not silent fallback.
- Continuity Terminal is disabled by default and available only under approved degraded/BCP scope.
- Central PMS owns degraded resolve decisioning, payment finality, fiscal reference recording, and ExitAuthorization.
- Vendor PMS/HCP remains normal raw session lifecycle and normal tariff authority.
- Vendor PMS Connector / HikCentral Connector reports vendor facts, health, freshness, and normalized outcomes only.
- Projection is operational visibility and controlled degraded support only.
- POS Server remains fiscal issuance authority.
- Fiscal issuance failure does not automatically authorize exit.
- Manual release is a governed exception, not normal ExitAuthorization.
- Operator Console is governance only.
- Management Dashboard and Reporting is visibility only.

## 2. Source Documents and Diagram Folders Reviewed

### Source documents reviewed

- `docs/v1.3/continuity/system-design/ExitPass_Continuity_System_Design_Orchestration_Plan.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`

### Diagram folders reviewed

- `docs/v1.3/continuity/diagrams/`
- `docs/v1.3/diagrams/system-design/`
- `docs/v1.3/assisted-payment-terminal/system-design/diagrams/`
- `docs/v1.3/operator-console/diagrams/`
- `docs/v1.3/pos-invoicing/diagrams/`
- `docs/v1.3/vendor-pms-connector/diagrams/`
- `docs/v1.3/hikcentral-connector/diagrams/`

## 3. Existing Relevant v1.3 Diagrams

### Continuity BRD diagrams

These diagrams are business context only and should not be copied as final System Design diagrams without revalidating authority labels and system-design scope.

| Existing diagram | Relevance to Continuity System Design planning |
| --- | --- |
| `D-01_Continuity_Context_Diagram` | Useful starting point for Continuity logical architecture and authority boundary labels. |
| `D-02_Continuity_Activation_and_Deactivation_Flow` | Useful business reference for activation, deactivation, scoped continuity, and post-restoration review. |
| `D-03_Vendor_PMS_HCP_Degraded_Resolve_Flow` | Useful reference for degraded resolve, projection freshness, and Central PMS decision ownership. |
| `D-04_Payment_Fiscal_Issuance_and_ExitAuthorization_Under_Continuity` | Useful reference for payment finality, POS Server fiscal issuance, Central PMS fiscal reference recording, and ExitAuthorization sequencing. |
| `D-05_Continuity_Terminal_Restricted_Operation_Flow` | Useful reference for Continuity Terminal activation, restricted workflows, and fail-closed behavior. |
| `D-06_Post_Restoration_Reconciliation_Flow` | Useful reference for post-restoration reconciliation and closure posture. |

### Core System Design diagrams

| Existing diagram | Relevance to Continuity System Design planning |
| --- | --- |
| `D-01_ExitPass_v1.3_Logical_Architecture` | Baseline for component boundaries and cross-system topology. |
| `D-02_Authority_Boundary_Model` | Primary authority-label reference. |
| `D-03_Site_Group_Site_VendorSystem_Connector_POS_Topology` | Baseline for Site Group, Site, VendorSystem, connector, and POS Server routing boundaries. |
| `D-05_Payment_Finality_Fiscal_Issuance_ExitAuthorization_Sequence` | Baseline for payment/fiscal/exit authority sequencing. |
| `D-06_Vendor_PMS_Connector_Projection_Freshness_Flow` | Baseline for projection freshness and connector visibility. |
| `D-07_Degraded_Resolve_and_Continuity_Sequence` | Baseline for degraded resolve and continuity sequence planning. |
| `D-08_Assisted_Payment_Terminal_Context_and_Modes` | Baseline for Continuity Terminal as APT mode. |
| `D-09_Operator_Console_Governance_Boundary` | Baseline for governance-only console boundary. |
| `D-10_Management_Dashboard_Source_of_Truth_Boundary` | Baseline for visibility-only dashboard boundary. |
| `D-11_Audit_Event_Outbox_Conceptual_Flow` | Baseline for audit/event posture and non-authority event consumers. |

### Companion diagrams

| Diagram area | Existing diagrams most relevant to continuity planning |
| --- | --- |
| Assisted Payment Terminal | Terminal mode model, payment/fiscal/exit status display, fiscal issuance failure/pending exit flow, Continuity Terminal activation/restricted operation, manual release handoff, terminal observability/audit flow. |
| Operator Console | Continuity activation/post-restoration review governance, fiscal issuance exception review, manual release governance. |
| Management Dashboard and Reporting | Connector health/projection freshness dashboard flow, payment/fiscal/reconciliation reporting flow, continuity and exception reporting flow. |
| POS/Invoicing | Site-level POS Server model, payment-to-exit fiscal sequence, channel/terminal fiscal routing, fiscal issuance failure exception flow. |
| Vendor PMS Connector | Projection polling/freshness flow, vendor payment acknowledgment flow, degraded resolve handoff to Central PMS/Continuity, connector error normalization/health reporting. |
| HikCentral Connector | Passageway projection flow, ticket-only fee calculation uncertainty, conditional vendor payment acknowledgment, HCP connector health and stale projection flow. |

## 4. Recommended Continuity System Design Diagram Set

Recommend thirteen conceptual System Design diagrams:

| Proposed ID | Recommended diagram |
| --- | --- |
| CON-SD-D01 | Continuity logical architecture |
| CON-SD-D02 | Continuity operating-state model |
| CON-SD-D03 | Dependency degradation detection and degraded-watch flow |
| CON-SD-D04 | Continuity activation and scope-control flow |
| CON-SD-D05 | Vendor PMS / HCP degraded resolve decision flow |
| CON-SD-D06 | Projection freshness and degraded eligibility flow |
| CON-SD-D07 | Continuity Terminal restricted operation flow |
| CON-SD-D08 | Payment, fiscal issuance, and ExitAuthorization under continuity |
| CON-SD-D09 | Fiscal issuance failure / pending exit exception flow |
| CON-SD-D10 | Manual release governance flow |
| CON-SD-D11 | Vendor acknowledgment failure and reconciliation flow |
| CON-SD-D12 | Post-restoration reconciliation lifecycle |
| CON-SD-D13 | Continuity observability and audit event flow |

## 5. Diagram Purpose and Intended Section

| Proposed ID | Purpose | Intended final design section |
| --- | --- | --- |
| CON-SD-D01 | Show continuity-capable architecture boundaries and non-authority labels across Central PMS, Vendor PMS/HCP, connectors, POS Server, payment channels, Continuity Terminal, Operator Console, Management Dashboard, gate/exit, audit, and reconciliation. | Architecture and component boundaries |
| CON-SD-D02 | Show conceptual continuity states from normal through closed/reconciled without converting them to API, database, or implementation statuses. | Continuity operating states |
| CON-SD-D03 | Show how dependency degradation becomes degraded-watch visibility without automatically activating continuity. | Degraded-watch and dependency monitoring |
| CON-SD-D04 | Show approved activation, explicit scope, incident/audit/reconciliation tagging, allowed workflow scope, deactivation, and post-restoration handoff. | Activation, deactivation, and scope control |
| CON-SD-D05 | Show Central PMS-owned degraded resolve decisioning when Vendor PMS/HCP live resolve is unavailable, degraded, stale, ambiguous, or insufficient. | Vendor PMS/HCP degraded operation |
| CON-SD-D06 | Show projection freshness and degraded eligibility checks as support inputs only, with fail-closed or governance handoff for unsafe cases. | Projection freshness and degraded eligibility |
| CON-SD-D07 | Show Continuity Terminal enablement and restricted operation under active scope, including display/handoff behavior and fail-closed limits. | Continuity Terminal |
| CON-SD-D08 | Show continuity payment, POS Server fiscal issuance, Central PMS fiscal reference recording, and Central PMS ExitAuthorization sequencing. | Payment, fiscal issuance, and exit under continuity |
| CON-SD-D09 | Show fiscal issuance failure, timeout, or pending state after payment finality, including blocked normal ExitAuthorization and governed exception routing. | Fiscal exceptions and pending exit handling |
| CON-SD-D10 | Show manual release as separately governed exception with approval, reason, incident/audit/reconciliation tags, and post-review. | Manual release governance |
| CON-SD-D11 | Show vendor acknowledgment failure, unknown outcome, backlog, retry/escalation posture, and reconciliation tagging without making acknowledgment payment finality. | Vendor acknowledgment and connector reconciliation |
| CON-SD-D12 | Show lifecycle from restoration-in-progress to post-restoration review and closed/reconciled status using canonical payment, fiscal, vendor, gate/manual release, continuity, and reconciliation records. | Restoration and reconciliation |
| CON-SD-D13 | Show continuity observability and audit/event flow as telemetry and durable traceability, not authority transfer. | Observability, audit, and reporting visibility |

## 6. Key Components Per Diagram

| Proposed ID | Key components to include |
| --- | --- |
| CON-SD-D01 | Central PMS, Vendor PMS/HCP, Vendor PMS Connector/HikCentral Connector, projection store/view, Payment Orchestrator, Site POS Server, WebPay/APM/Assisted Payment Terminal/Continuity Terminal, Operator Console, Management Dashboard, gate/exit execution, audit/event capability, reconciliation workflow. |
| CON-SD-D02 | Normal, Degraded-watch, Degraded-active, Continuity Terminal active, Restoration-in-progress, Post-restoration review, Closed/reconciled; show activation/deactivation and restoration handoffs conceptually. |
| CON-SD-D03 | Dependency health signals, connector health, projection freshness signal, POS Server status, payment/provider uncertainty signal, gate/exit status signal, Central PMS degraded-watch classification, Operator Console visibility, Management Dashboard visibility. |
| CON-SD-D04 | Approved governance workflow, Operator Console governance surface, Central PMS continuity policy evaluation, Site/Site Group/dependency scope, incident/BCP reference, allowed workflow scope, audit tag, reconciliation tag, Continuity Terminal availability flag, deactivation, post-restoration handoff. |
| CON-SD-D05 | Central PMS, Vendor PMS/HCP, connector boundary, live resolve unavailable/degraded result, projection/freshness input, approved continuity policy, approved degraded payable basis, blocked/ambiguous path, supervisor/manual review handoff. |
| CON-SD-D06 | Connector projection input, mapping status, freshness/staleness label, ambiguity/insufficiency checks, Central PMS degraded eligibility evaluation, fail-closed outcome, governance handoff, visibility to Operator Console and Management Dashboard. |
| CON-SD-D07 | Assisted Payment Terminal app family, Continuity Terminal mode, activation scope, authorized terminal/user/Site/Site Group, backend display state, restricted lookup/payment/fiscal/status display, supervisor/manual review handoff, fail-closed blocked states. |
| CON-SD-D08 | Continuity payment channel or Continuity Terminal, Central PMS payment finality, Payment Orchestrator verified outcome input, resolved Site POS Server fiscal issuance, Central PMS fiscal reference recording, Central PMS ExitAuthorization, gate/exit consumer. |
| CON-SD-D09 | Payment finality recorded, fiscal issuance requested, POS Server pending/failed/timed-out response, Central PMS blocks normal ExitAuthorization, customer/operator pending message, fiscal exception review, retry/escalation posture, manual release governance handoff where approved. |
| CON-SD-D10 | Operator Console or approved governance workflow, supervisor/operator actor, Central PMS context, fiscal/exit/gate exception context, approval/rejection, reason capture, incident/audit/reconciliation tags, gate/physical release execution outside normal ExitAuthorization, post-review. |
| CON-SD-D11 | Central PMS, connector boundary, Vendor PMS/HCP acknowledgment attempt, acknowledged/failed/unknown/conflicting outcome categories at conceptual level, retry/escalation posture, reconciliation queue/backlog visibility, audit/event capability. |
| CON-SD-D12 | Restoration signal, deactivation, continuity-origin activity set, canonical payment/provider records, POS fiscal records and fiscal references, vendor acknowledgment outcomes, gate outcomes, manual releases, continuity discount activity, reconciliation workflow, Operator Console review, Management Dashboard visibility, closure. |
| CON-SD-D13 | Central PMS audit classification, connector health/freshness events, Continuity Terminal activity events, Operator Console governance actions, POS fiscal outcome events, payment outcome events, gate/manual release outcome events, dashboard/report access audit, reconciliation consumers. |

## 7. Authority Notes Per Diagram

| Proposed ID | Authority notes to label directly in the diagram |
| --- | --- |
| CON-SD-D01 | Label each authority owner: Central PMS for degraded decisioning/payment finality/ExitAuthorization; Vendor PMS/HCP for normal session/tariff; POS Server for fiscal issuance; Operator Console for governance; Management Dashboard for visibility. |
| CON-SD-D02 | State nodes are conceptual design states only; they are not database values, API statuses, event payloads, or implementation enums. |
| CON-SD-D03 | Degraded-watch is observation and visibility; it is not continuity activation and does not enable payment, fiscal, or exit bypass. |
| CON-SD-D04 | Activation must be explicit, scoped, approved where policy requires, tagged, time-bound, and reversible; no automatic fallback. |
| CON-SD-D05 | Connector reports facts and freshness only; Central PMS owns degraded resolve and approved degraded payable-basis decisioning. |
| CON-SD-D06 | Projection is operational visibility and degraded support only; stale, ambiguous, insufficient, unsafe, or unapproved projection fails closed or routes to governance. |
| CON-SD-D07 | Continuity Terminal is disabled by default; it displays backend state and submits allowed workflow input but does not declare finality, issue fiscal documents, approve discounts, issue ExitAuthorization, or open gates. |
| CON-SD-D08 | Payment Orchestrator reports verified provider outcomes; Central PMS declares platform payment finality; POS Server issues Sales Invoice; Central PMS records fiscal reference and issues ExitAuthorization only when eligible. |
| CON-SD-D09 | Fiscal issuance failure or timeout after payment does not reverse payment automatically and does not authorize exit automatically. |
| CON-SD-D10 | Manual release is a last-resort governed exception, not normal ExitAuthorization and not a way to rewrite payment, fiscal, projection, or vendor truth. |
| CON-SD-D11 | Vendor acknowledgment is not ExitPass payment finality; unknown or failed acknowledgment is reconciliation input, not exit authority. |
| CON-SD-D12 | Reconciliation closure belongs to approved reconciliation/post-restoration workflow; dashboard visibility does not close reconciliation. |
| CON-SD-D13 | Audit/events/observability communicate facts and visibility; they do not transfer authority to consumers or dashboards. |

## 8. Diagram Risks to Avoid

The final Continuity System Design diagrams should explicitly avoid these risks:

- Showing continuity as automatic fallback.
- Showing continuity as a normal alternate mode.
- Showing Continuity Terminal as always enabled.
- Showing a connector approving degraded resolve.
- Showing projection as a source of truth.
- Showing projection calculating tariff.
- Showing POS Server issuing ExitAuthorization.
- Showing manual release as normal ExitAuthorization.
- Showing Operator Console collecting payment.
- Showing Operator Console opening a gate.
- Showing Management Dashboard closing reconciliation.
- Including endpoint paths, DTOs, database tables, enum values, event payloads, implementation classes, timer values, thresholds, runbook steps, or secrets.
- Creating database diagrams, API route diagrams, implementation class diagrams, endpoint maps, or runbook flowcharts with procedural steps.
- Drawing HCP-specific details in a way that overrides the generic Vendor PMS Connector boundary or the unresolved HCP source gaps.
- Treating payment provider success, vendor paid state, fiscal display, dashboard status, or local terminal state as Central PMS payment finality.
- Treating fiscal exception, vendor acknowledgment failure, gate issue, or manual release as a reason to erase reconciliation obligations.

## 9. PlantUML Style Recommendations

These recommendations are for later Lead synthesis only. This input pack intentionally does not create PlantUML source files.

- Use clear diagram IDs such as `CON-SD-D01` through `CON-SD-D13`.
- Use conceptual `component`, `boundary`, `database`-style icons only for architecture-level components and stores/views; avoid database schema detail.
- Use separate visual groupings for authority owners, channels/terminals, integration boundaries, governance/reporting, and audit/reconciliation.
- Label authority directly on the owning component, for example `Central PMS\npayment finality + ExitAuthorization`.
- Label non-authority consumers directly, for example `Management Dashboard\nvisibility only`.
- Use notes for guardrails such as `Projection is operational visibility only` and `Continuity is explicit, scoped, audited`.
- Use `alt` or decision diamonds only for conceptual branch outcomes such as allowed, blocked, pending, failed closed, or governance handoff.
- Do not include endpoint paths, payload fields, database names, implementation classes, retry counts, timer values, threshold values, queue names, or secrets.
- Prefer concise line labels such as `reports health/freshness`, `requests fiscal issuance`, `records fiscal reference`, `issues ExitAuthorization`, `routes to governance`, and `tags for reconciliation`.
- Visually distinguish normal authority flow from degraded/continuity flow so continuity does not read as the default path.
- Use a consistent color or stereotype for non-authority visibility surfaces, but do not rely on color alone; include text labels.
- Keep HCP-specific diagrams subordinate to generic connector diagrams; HCP source gaps should appear as notes or blocked/unknown outcomes, not as approved behavior.

## 10. Summary for Lead

The future Continuity System Design should include thirteen conceptual diagrams covering architecture, operating states, degraded-watch detection, activation/scope control, degraded resolve, projection freshness, Continuity Terminal operation, payment/fiscal/exit sequencing, fiscal exceptions, manual release governance, vendor acknowledgment failure, reconciliation lifecycle, and observability/audit.

The strongest existing references are the Continuity BRD diagrams, core System Design authority and continuity diagrams, APT Continuity Terminal and fiscal exception diagrams, Operator Console governance diagrams, Management Dashboard visibility diagrams, POS fiscal sequence/failure diagrams, and connector projection/degraded handoff/acknowledgment diagrams.

The Lead should keep every diagram at System Design level. The diagrams should show components, authority boundaries, conceptual decisions, and handoffs only. They should not introduce endpoint maps, database diagrams, implementation classes, final state names, event payloads, thresholds, timer values, runbook steps, or generated final diagram artifacts.

Most important authority labels to preserve:

- Central PMS: degraded resolve decision, payment finality, fiscal reference recording, ExitAuthorization.
- Vendor PMS/HCP: normal raw session lifecycle and normal tariff computation.
- Vendor PMS Connector / HikCentral Connector: vendor facts, health, freshness, and normalized outcomes only.
- Projection: operational visibility and controlled degraded support only.
- POS Server: fiscal issuance authority only.
- Continuity Terminal: restricted APT mode, disabled by default, no finality/fiscal/exit authority.
- Operator Console: governance only, no payment or gate execution.
- Management Dashboard: visibility/reporting only, no reconciliation closure authority.
- Manual release: governed exception, not normal ExitAuthorization.
