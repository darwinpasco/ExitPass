# ExitPass Management Dashboard and Reporting Phase-1 Read-Model Inventory v1.0

## Purpose

This inventory controls which Central PMS sources may support the first read-only Management Dashboard and Reporting API. It is authoritative only for this bounded phase-1 foundation. `UNAVAILABLE` never means zero, and an unavailable source does not authorize a placeholder metric.

## Classifications

- `AVAILABLE`: an authoritative source and safe scoped read model are implemented.
- `PARTIAL`: authoritative data exists, but only a bounded subset is safe and implemented.
- `UNAVAILABLE`: no approved phase-1 read model is exposed.
- `NOT_APPLICABLE`: the source is valid but has no configured records for the requested authorized scope.

## Inventory

| Source ID | Business meaning | Authority | Available fields | Scope | Currency timestamp | Privacy | Permission | Status | Phase-1 exposure and limitations |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `site-scope-registry` | Site and Site Group identity plus the reporting boundary | Central PMS `sites` domain | Scope reference/name, Site references, lifecycle status, payment-enabled posture | `SITE`, `SITE_GROUP` | Latest scope/Site update | Internal operational aggregate; names only | `dashboard.view` | `AVAILABLE` | Exposed as scoped aggregate counts. No address, private identity mapping, or GLOBAL scope is returned. |
| `site-operational-status` | Canonical configured Site lifecycle posture | Central PMS Site registry | Total, active, suspended, payment-enabled Site counts | `SITE`, `SITE_GROUP` | Latest Site update | Internal operational aggregate | `dashboard.view` | `AVAILABLE` | Configuration posture only; it does not prove connector, payment, fiscal, provider, or gate availability. |
| `connector-integration-health` | Vendor projection synchronization target health | Central PMS vendor-session projection target registry | Configured/enabled/healthy/degraded/failing target counts | `SITE`, `SITE_GROUP` | Latest attempt, success, or projection timestamp | Internal operational aggregate; raw provider errors excluded | `dashboard.view` | `PARTIAL` | Exposes aggregate target state only. Vendor diagnostics, endpoints, credentials, and latency are deferred. |
| `vendor-projection-freshness` | Currency of vendor parking-session projections | Central PMS vendor-session projection store | Active projection count and stale target count | `SITE`, `SITE_GROUP` | Latest projection refresh | Internal operational aggregate; no vehicle identifiers | `dashboard.view` | `PARTIAL` | Projection data is operational visibility, not parking, payment, fiscal, settlement, or exit truth. |
| `queue-continuity-exceptions` | Queue age, degraded modes, manual release, and continuity exceptions | No consolidated approved phase-1 source | None | Future `SITE`, `SITE_GROUP` | Not established | Potentially sensitive operational incidents | Future catalog decision | `UNAVAILABLE` | Catalogued only as a limitation; no result or zero is returned. |
| `active-session-vehicle-visibility` | Active sessions, vehicles, occupancy approximation, age, and long stay | Vendor projection has only a bounded active aggregate | Active projected-session count only | `SITE`, `SITE_GROUP` | Latest projection refresh | Vehicle identifiers prohibited in phase 1 | `dashboard.view` | `PARTIAL` | Only aggregate active projections are exposed. Vehicle, plate, ticket, age, long-stay, and occupancy views are deferred. |
| `payment-reconciliation-summary` | Payment attempts, outcomes, uncertainty, reconciliation, and settlement comparison | Canonical payment and reconciliation domains | None in this facade | Future `SITE`, `SITE_GROUP` | Not established | Financial and provider-sensitive | `reports.view` plus future report permission decision | `UNAVAILABLE` | Listed in the catalog as unavailable. No financial-finality or settlement claim is made. |
| `fiscal-exception-summary` | Sales Invoice status and fiscal exception visibility | Central PMS recorded fiscal evidence plus POS Server authority | None in this facade | Future `SITE`, `SITE_GROUP` | Not established | Fiscal-sensitive | `reports.view` plus future fiscal report permission decision | `UNAVAILABLE` | Listed in the catalog as unavailable. Central PMS does not issue or mutate fiscal documents through this API. |
| `management-activity-summary` | Audit-based management activity | Central audit evidence | None in this facade | Future authorized scope | Not established | Security-relevant audit metadata | `reports.view` plus future audit-report decision | `UNAVAILABLE` | Report access itself is audited, but audit-event reporting is deferred to a separately approved privacy-safe model. |
| `gate-exit-authority` | Exit authorization and gate command state | Gate/exit authority domains | None | Not a reporting authority in this slice | Not applicable | Safety-critical | Not applicable | `NOT_APPLICABLE` | Dashboard reporting remains non-authoritative and introduces no gate or exit action. |

## Phase-1 Decision

The operational overview exposes only `site-operational-status`, `connector-integration-health`, and the bounded aggregate from `vendor-projection-freshness`. Deferred rows remain visible in the catalog only when explicitly marked `UNAVAILABLE`. No separate Dashboard BFF is introduced in phase 1; the read-only facade is hosted in Central PMS.
