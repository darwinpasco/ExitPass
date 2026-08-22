# ExitPass Management Dashboard and Reporting Foundation Implementation v1.0

## Boundary

Phase 1 implements a read-only reporting facade inside Central PMS. This is a bounded deployment decision for the first API foundation, not a permanent decision against a future separately deployed reporting facade or BFF.

No Management Platform frontend, export, scheduled report, report builder, mutation, payment-finality decision, fiscal authority, settlement closure, gate control, or parking-session action is included.

## API

- `GET /v1/management-platform/dashboard/catalog`
  - policy: `ManagementPlatformDashboardCatalogRead`
  - permission: `reports.view`
- `GET /v1/management-platform/dashboard/operational-overview`
  - policy: `ManagementPlatformOperationalOverviewRead`
  - permission: `dashboard.view`
  - required query: `scopeType=SITE|SITE_GROUP` and non-empty `scopeReference`
- `GET /v1/management-platform/dashboard/payment-reconciliation-summary`
  - policy: `ManagementPlatformPaymentReconciliationSummaryRead`
  - permission: `reconciliation.view`
  - required query: explicit `scopeType`, `scopeReference`, `periodStart`, and `periodEnd`
  - contract: `management-platform-payment-reconciliation-reporting:v1`
- `GET /v1/management-platform/dashboard/fiscal-exception-summary`
  - policy: `ManagementPlatformFiscalExceptionSummaryRead`
  - permission: `sales-invoice-report.view`
  - required query: explicit `scopeType`, `scopeReference`, `periodStart`, and `periodEnd`
  - contract: `management-platform-fiscal-exception-reporting:v1`

Contract version: `management-platform-dashboard-reporting:v1`.

The catalog identifies `operational-overview`, `payment-reconciliation-summary`, and `fiscal-exception-summary` as `PARTIAL`. Management activity reporting remains `UNAVAILABLE`; it is not advertised as operational and returns no fabricated payload.

## Implemented Overview

The operational overview contains:

1. Canonical Site lifecycle and payment-enabled configuration aggregates from the Central PMS Site registry.
2. Scoped vendor projection-sync target health aggregates.
3. Scoped active-projection and stale-target aggregates.

Every section supplies source authority, availability, freshness, data-as-of timestamp where available, controlled warnings, and limitations. An unreadable projection source produces `UNAVAILABLE` sections with no numeric placeholder. No configured projection target produces `NOT_APPLICABLE`, not a success-looking zero result.

## Authorization

The H-006/I-020 server-issued Management Platform human session supplies the actor. Endpoint policy middleware verifies the dedicated permission. The reporting repository then verifies the live internal session, Management Platform audience, active account, credential version, authorization-epoch snapshot, expiry, and scope attached to the role assignment that grants `dashboard.view`.

Only explicit `SITE` and `SITE_GROUP` requests are supported. Missing scope never means GLOBAL. A Site Group grant may authorize an explicit Site in that group; a Site grant does not authorize the Site Group aggregate. Scope-not-found and scope-denied requests use one concealed response. Browser-authored identity, permission, role, Site, Site Group, or authorization-epoch headers are not authority.

## Feature Control

Typed options bind from `ManagementPlatform:DashboardReporting`. `Enabled` defaults to `false`; approved environments must enable it explicitly. `ProjectionStaleAfterMinutes` is startup-validated and defaults to 15 minutes. Production behavior outside this feature is unchanged.

Payment reporting has an additional typed, default-disabled gate at `ManagementPlatform:DashboardReporting:PaymentReconciliation:Enabled`. Both gates must be enabled. The report reads no external provider or POS Server service at request time.

Fiscal exception reporting has an additional typed, default-disabled gate at `ManagementPlatform:DashboardReporting:FiscalExceptions:Enabled`. The parent and report gates must both be enabled. The report reads Central PMS coordination references and recorded outcomes only; it does not query or synchronously call a Site POS Server.

## Audit

Catalog success, overview success, scope denial, query failure, and unavailable projection results write safe audit evidence. Stored metadata is limited to operation/report ID, actor/session references, effective scope reference, controlled result/source classification, correlation reference, and timestamp. Report payloads, credentials, secrets, raw provider diagnostics, and unnecessary personal data are excluded.

RBAC middleware continues to audit unauthenticated and permission-denied requests before endpoint execution.

## Failure Semantics

The established safe error envelope covers unauthenticated, forbidden, invalid scope, concealed scope, invalid/stale session, feature disabled, unavailable source, and unexpected failure. Raw exceptions are logged server-side with correlation and are never returned.

## Frontend Boundary

A later Management Platform task may consume only these same-origin routes and server-returned classifications. It must not infer GLOBAL scope, convert `UNAVAILABLE` to zero, reinterpret stale data as current, persist authority in browser storage, or add report/mutation behavior absent from the catalog.

## Acceptance Posture

This backend foundation is implemented but not end-to-end accepted. Frontend implementation, approved runtime enablement, responsive/accessibility validation, and complete browser acceptance remain follow-on work.
