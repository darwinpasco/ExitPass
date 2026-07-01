# Vendor PMS Connector Observability, Projection, and Operations Input Pack

## 1. Purpose

This input pack provides observability, projection freshness, health, and operations guidance for the later Vendor PMS Connector System Design and HikCentral Connector Profile.

This pack stays at companion technical-design input level. It identifies the observability domains, health and freshness signals, warning categories, dashboard visibility expectations, audit/correlation needs, and runbook implications that the Lead should preserve. It does not define final metric names, alert thresholds, dashboard wireframes, database/reporting schemas, monitoring stack, event payloads, implementation classes, or runbook procedures.

The core rule for this pack is that projection is operational visibility and controlled degraded support only. Projection is not tariff truth in normal mode, is not financial truth, is not fiscal truth, is not payment finality, and is not exit authority. Stale projection must be warning-labeled and must fail closed unless an approved degraded policy explicitly allows use.

## 2. Source Documents Reviewed

Primary source documents reviewed:

- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`

Optional source documents checked but not available in the input-pack directory at time of review:

- `docs/v1.3/vendor-pms-connector/input-packs/02_hikcentral_api_discovery.md`
- `docs/v1.3/vendor-pms-connector/input-packs/03_connector_workflow_and_state.md`

## 3. Observability Domains

The later connector design should preserve these observability domains without promoting any telemetry surface into authority:

| Domain | Design input |
| --- | --- |
| Connector health | Show whether the configured connector instance for a VendorSystem is operating, degraded, stale, unavailable, or otherwise unable to support normal live resolve or projection ingestion. Exact state model remains open. |
| Projection freshness | Show how current projected session data is for operational visibility and controlled degraded support. Exact freshness thresholds and stale warning labels remain open. |
| Vendor PMS / HCP availability | Show whether the external vendor dependency is reachable and whether live resolve, fee calculation, polling, and acknowledgment functions appear usable. |
| Polling execution | Surface last successful poll, poll latency, failed poll count, API error categories, polling gaps, and polling backlog at signal level only. |
| Projection data quality | Surface stale projection warnings, sessions not seen in latest poll, mapping ambiguity, duplicate/conflicting session candidates, insufficient projection data, and vendor object mapping health. |
| Degraded and continuity state | Surface degraded-watch visibility, degraded-active visibility, affected Site/Site Group, affected dependency, and continuity context where authorized. |
| Vendor acknowledgment status | Surface payment acknowledgment backlog and acknowledgment failure context as reconciliation and operations input, not payment finality. |
| Dashboard/reporting labels | Preserve source-of-truth labels, freshness labels, and authority-level labels so operational projection is not confused with financial, fiscal, or reconciliation records. |
| Audit and correlation | Preserve traceability across channel, Site Group, resolved Site, VendorSystem, vendor object reference, projection freshness where used, payment, fiscal, exit, continuity, manual release, and reconciliation context. |

## 4. Connector Health Signals

Connector health should be exposed to authorized operations users, Operator Console, and Management Dashboard surfaces as operational context.

Recommended signal areas:

- Connector instance reachability for the configured VendorSystem.
- Last successful poll.
- Recent poll outcome, including success, no data, partial data, failed call, timeout, authentication/authorization failure, rate-limit or throttling response, malformed/unexpected response, mapping failure, and vendor-side business error categories.
- Poll latency at descriptive signal level.
- Failed poll count over later-defined observation windows.
- Whether live resolve is available.
- Whether vendor fee calculation is available.
- Whether vendor payment acknowledgment appears available.
- Whether polling/projection is healthy enough for operational visibility.
- Whether projection is eligible for approved degraded use.
- Whether the connector is in normal, degraded-watch, degraded-active, restoration-in-progress, or post-restoration-review context, where those labels are provided by continuity/governance workflows.

The final connector health state model remains open. The design should avoid implying that connector health alone authorizes payment, fiscal issuance, degraded operation, or exit.

## 5. Projection Freshness Signals

Projection freshness must be visible wherever projection is shown or used for controlled degraded support.

Recommended freshness signal areas:

- Timestamp or age of the latest successful projection update.
- Poll coverage for the relevant VendorSystem, vendor object, Site, and Site Group context.
- Sessions projected from the latest poll.
- Sessions not seen in latest poll.
- Stale sessions or sessions whose last vendor observation is outside later-approved freshness rules.
- Projection records that are ambiguous, conflicting, incomplete, or insufficient for degraded resolve.
- Mapping ambiguity where a vendor-side object reference cannot be resolved to one unambiguous AdapterMapping and ExitPass Site.
- Freshness context attached to degraded resolve evaluation and continuity review.

Projection freshness labels must be clear that projection is operational visibility and controlled degraded support only. Projection is not tariff truth in normal mode, not financial truth, not fiscal truth, not payment finality, and not exit authority.

## 6. Vendor PMS / HCP Availability Signals

Vendor PMS / HCP availability should be separated from connector process health because the connector can be alive while the vendor dependency, specific API capability, or network path is degraded.

Recommended availability signal areas:

- Vendor unavailable or unreachable.
- Vendor reachable but live resolve unavailable.
- Vendor reachable but fee calculation unavailable.
- Vendor reachable but polling/passageway source unavailable or degraded.
- Vendor reachable but payment acknowledgment unavailable or backlogged.
- Vendor API authentication or authorization failure.
- Vendor API throttling, timeout, or transient network failure categories.
- Vendor API business response or data-shape errors that require support review.
- Vendor-side parking object unavailable, missing, disabled, or not mapped to the expected AdapterMapping.

Vendor PMS / HCP remains authority for raw session lifecycle and normal tariff computation. These availability signals provide context for operations, continuity, and reconciliation, but they do not transfer tariff, payment, fiscal, or exit authority to the connector.

## 7. Polling and Projection Metrics

The later design should define measurement areas but defer exact operational metric names and thresholds.

Required measurement areas:

- Last successful poll by connector instance, VendorSystem, and relevant vendor object scope.
- Poll latency for completed polling attempts.
- Failed poll count and failure categories.
- API error categories, including connectivity, timeout, authentication/authorization, throttling/rate-limit, invalid request, vendor business error, unexpected response, parse/validation failure, and mapping failure.
- Polling cycle coverage for mapped vendor objects.
- Projection update result, including created, updated, unchanged, skipped, ambiguous, insufficient, or rejected projection outcomes at conceptual level.
- Sessions projected and sessions not seen in latest poll.
- Stale projection count or stale projection exposure at conceptual level.
- Mapping ambiguity exposure by affected Site, Site Group, VendorSystem, vendor object type, and vendor object reference.
- Payment acknowledgment backlog and acknowledgment failure context.
- Live resolve unavailable, fee calculation unavailable, and vendor unavailable signals.

One-minute HCP passageway polling is the v1.3 business planning baseline for HikCentral projection. This pack does not define the final polling scheduler implementation, retry model, worker topology, queue mechanics, or exact scheduling behavior.

## 8. Stale / Ambiguous / Insufficient Projection Warnings

Stale, ambiguous, or insufficient projection must be warning-labeled wherever displayed or used as an input to degraded evaluation.

Warning categories the Lead should preserve:

- Stale projection.
- Connector stale or unavailable.
- Vendor unavailable.
- Sessions not seen in latest poll.
- Session projection incomplete or insufficient.
- Multiple possible sessions for a lookup.
- Mapping ambiguity between vendor-side object identity and ExitPass Site.
- Vendor object unmapped or inactive.
- Live resolve unavailable.
- Fee calculation unavailable.
- Payment acknowledgment backlog.
- Projection eligible only for approved degraded review, where policy allows.
- Projection not eligible for degraded use.

Stale projection must fail closed for degraded tariff computation and exit authorization unless an approved degraded policy explicitly allows controlled continuation. Projection must not be treated as approval for payment, tariff, discount, fiscal issuance, or exit.

## 9. Operational Dashboard / Operator Console Visibility

Operator Console visibility should focus on operational awareness, governance context, and support triage. It should expose connector health and projection freshness to authorized users without collecting payment, declaring payment finality, issuing fiscal documents, issuing ExitAuthorization, or opening gates.

Operator Console visibility should include:

- Connector health.
- Last successful poll.
- Projection freshness.
- Stale projection warnings.
- Vendor PMS / HCP availability status.
- Live resolve unavailable status where relevant.
- Fee calculation unavailable status where relevant.
- Degraded-watch and degraded-active visibility.
- Affected Site and Site Group.
- Restriction warnings when projection is stale, ambiguous, or insufficient.

Management Dashboard visibility should focus on operational, management, support, and reporting context. It should expose connector status, HCP/Vendor PMS availability, projection freshness, last successful poll, poll latency, failed poll count, sessions projected, sessions stale, sessions not seen in latest poll, mapping ambiguity, vendor acknowledgment backlog, degraded-watch visibility, degraded-active visibility, and continuity/reconciliation backlog where authorized.

Dashboard and reporting surfaces must include source-of-truth labels. Projection/freshness should be labeled as operational visibility, while financial and revenue reports must use canonical payment, provider, fiscal, fiscal reference, settlement, and reconciliation records. Projection/freshness should be exposed to Management Dashboard for operational awareness and controlled degraded context, not as financial truth.

Continuity and degraded controls should consume connector health and projection freshness signals to determine whether degraded-watch, degraded-active, restoration, or post-restoration review context should be shown or evaluated. The exact activation workflow and decision logic remain outside this pack.

## 10. Alert Categories

The later design should preserve these alert categories without defining exact alert thresholds, notification routing, or monitoring stack:

- Connector stale or unavailable.
- Failed polling.
- High poll latency.
- Vendor PMS / HCP unavailable.
- Live resolve unavailable.
- Fee calculation unavailable.
- Projection stale.
- Projection ambiguous.
- Projection insufficient for degraded use.
- Mapping ambiguity or unmapped vendor object.
- Sessions not seen in latest poll where operationally material.
- Payment acknowledgment backlog or acknowledgment failure.
- Degraded-watch entered or changed.
- Degraded-active entered or changed.
- Continuity activation/deactivation context.
- Reconciliation or post-restoration backlog related to connector status.

Alerts should identify affected scope at conceptual level, such as VendorSystem, connector instance, vendor object reference, Site, Site Group, dependency category, and continuity incident context where available. Exact alert thresholds and operational metric names remain open.

## 11. Audit and Correlation Requirements

Connector observability should support reconstruction and audit without defining final event payloads or database schema.

Correlation should preserve:

- Channel or workflow entry point where relevant.
- Site Group and resolved Site.
- VendorSystem.
- AdapterMapping context.
- Vendor object type and vendor object reference.
- Connector instance identity.
- Last successful poll and projection freshness context where projection was displayed or used.
- Poll outcome and API error category at audit/event classification level.
- Live resolve status.
- Fee calculation status.
- TariffSnapshot linkage where live or approved degraded fee basis is recorded by Central PMS.
- PaymentAttempt, ProviderOutcome, PaymentConfirmation, and platform payment finality context.
- Site POS Server fiscal issuance reference context.
- ExitAuthorization context.
- Vendor payment acknowledgment status and backlog context.
- Continuity activation, degraded-watch, degraded-active, manual release, incident, audit, and reconciliation tags where applicable.
- Dashboard/report/export access where connector or projection data is viewed or exported.

Reconciliation may use connector status, projection freshness, vendor acknowledgment status, and vendor availability as context. Reconciliation must not use projection as financial truth. Projection cannot close financial reconciliation.

## 12. Runbook Implications

Future runbook packs should cover connector and projection operations, but this input pack does not define runbook procedures.

Runbook areas implied by the reviewed sources:

- Connector stale or unavailable.
- Vendor PMS / HCP outage.
- Failed polling or sustained poll latency.
- Projection stale, ambiguous, or insufficient.
- Mapping ambiguity or unmapped vendor object.
- Live resolve unavailable.
- Fee calculation unavailable.
- Vendor payment acknowledgment backlog.
- Degraded-watch and degraded-active operations visibility.
- Continuity activation/deactivation support.
- Post-restoration review and reconciliation backlog.

Runbooks must preserve authority boundaries. They must not instruct operators to mark payments final, issue fiscal documents, authorize exit, activate continuity, close reconciliation, or bypass Central PMS outside approved workflows. Runbooks should use health and freshness signals as operational context, not as payment, fiscal, tariff, or exit authority.

## 13. Open Observability Questions

Open questions to preserve for later design:

- What are the exact projection freshness thresholds?
- What are the exact stale warning labels?
- What is the exact connector health state model?
- What is the exact polling scheduler implementation?
- What are the exact dashboard refresh intervals?
- What are the exact alert thresholds?
- What are the exact operational metric names?
- What monitoring stack will be used?
- What are the exact runbook procedures?
- What are the exact reconciliation SLA and status labels?
- What is the exact degraded tariff freshness threshold?
- What is the exact vendor acknowledgment retry/escalation policy?
- What exact API capability constraints apply after HikCentral API discovery is complete?
- What exact live resolve unavailable and fee calculation unavailable labels should be shown to operators and dashboards?
- What exact relationship should exist between connector health state, degraded-watch, degraded-active, and continuity activation workflows?

These questions should not be silently resolved in the final connector documents unless the Lead has an approved source or explicit decision.

## 14. Summary for Lead

The later Vendor PMS Connector System Design and HikCentral Connector Profile should carry forward a control-aware observability model:

- Connector health, projection freshness, Vendor PMS/HCP availability, polling execution, projection data quality, degraded state, vendor acknowledgment backlog, audit correlation, and dashboard source labeling are the key domains.
- Required health/freshness signals include last successful poll, poll latency, failed poll count, API error categories, stale projection warnings, sessions not seen in latest poll, mapping ambiguity, vendor unavailable, live resolve unavailable, fee calculation unavailable, payment acknowledgment backlog, degraded-watch visibility, and degraded-active visibility.
- One-minute HCP passageway polling is the business planning baseline, but final scheduling implementation remains open.
- Connector health should be exposed to Operator Console for operational awareness and support triage.
- Projection and freshness should be exposed to Management Dashboard for operational visibility, with source-of-truth and freshness labels.
- Continuity/degraded controls should consume health and freshness signals while preserving fail-closed behavior unless approved degraded policy allows controlled use.
- Reconciliation should use connector status and projection freshness as context only, not as financial truth.
- Projection is not tariff truth in normal mode, not financial truth, not fiscal truth, not payment finality, and not exit authority.
- This pack intentionally does not define exact metric names, alert thresholds, dashboard implementation, monitoring stack, runbook steps, database schema, event payloads, or implementation classes.
