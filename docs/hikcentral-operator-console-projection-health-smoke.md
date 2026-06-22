# HikCentral Operator Console Projection Health Smoke Test

Date: 2026-06-22

## Purpose

This document records the completed live smoke test for the Operator Console HikCentral Projection Health page.

The smoke test verified that the #301 read-only Central PMS projection health endpoints and the #302 Operator Console page can display live HikCentral projection health data without exposing secrets, adding mutation controls, or changing projection authority boundaries.

## Scope

Validated components:

- `GET /v1/ops/vendor-session-projections/summary`
- `GET /v1/ops/vendor-session-projections/targets`
- `GET /v1/ops/vendor-session-projections/targets/{projectionSyncTargetId}`
- Operator Console page `/operator-console/vendor-session-projections/health`

The test did not perform database writes, scheduler changes, degraded fallback changes, payment actions, tariff actions, parking-session authority changes, or exit authorization actions.

## Environment

Operator Console URL:

```text
http://localhost:5175/operator-console/vendor-session-projections/health
```

Central PMS URL:

```text
http://localhost:56065
```

Vite proxy target:

```text
VITE_OPERATOR_CONSOLE_API_PROXY_TARGET=http://localhost:56065
```

The Operator Console page loaded successfully after starting the UI with the Vite proxy target above.

## Prerequisites

- Central PMS API running at `http://localhost:56065`.
- Operator Console Vite app running at `http://localhost:5175`.
- Vite proxy configured with `VITE_OPERATOR_CONSOLE_API_PROXY_TARGET=http://localhost:56065`.
- Local development Operator Console RBAC headers available for protected ops endpoint checks.
- Projection health backend endpoints from #301 present.
- Operator Console Projection Health page from #302 present.

## Backend Endpoint Protection Check

Raw unauthenticated PowerShell calls to the #301 ops endpoints returned `401 Unauthorized`.

This is expected and confirms the projection health endpoints are protected and are not public/anonymous endpoints.

## Backend Local-Dev RBAC Header Check

PowerShell calls with local-development Operator Console RBAC headers succeeded.

Required local-development headers used:

| Header | Value |
| --- | --- |
| `X-Correlation-Id` | test correlation id |
| `X-Operator-User-Id` | `77000000-0000-0000-0000-000000000010` |
| `X-ExitPass-User-Id` | `77000000-0000-0000-0000-000000000010` |
| `X-ExitPass-Permissions` | `operator-console.vendor-projection-health.view` |
| `X-Operator-Device-Binding-Id` | `77000000-0000-0000-0000-000000000030` |
| `X-Operator-Shift-Id` | `77000000-0000-0000-0000-000000000050` |

Confirmed backend checks:

- `GET /v1/ops/vendor-session-projections/summary` passed with local-dev RBAC headers.
- `GET /v1/ops/vendor-session-projections/targets` passed with local-dev RBAC headers.

## Operator Console UI Smoke Result

The Operator Console page loaded successfully through the Vite proxy.

Confirmed visible on the page:

- ExitPass Operator Console shell loaded.
- Projection Health navigation item is visible.
- HikCentral Projection Health page loaded.
- Read-only boundary panel is visible.
- Projection summary cards are visible.
- Projection targets table is visible.
- `TEST SITE` target is visible.
- Target detail panel is visible.
- Safe config visibility is visible.
- Latest projected record counts are visible.
- Non-authoritative continuity visibility message is visible.

Confirmed not present:

- No sync-now button.
- No enable button.
- No disable button.
- No fallback toggle.
- No payment action.
- No tariff action.
- No exit authorization action.
- No `AppKey`.
- No `AppSecret`.
- No database password.
- No raw HikCentral payload.

## Vite Proxy Configuration Note

The live smoke test passed after correcting the Operator Console Vite proxy target:

```text
VITE_OPERATOR_CONSOLE_API_PROXY_TARGET=http://localhost:56065
```

Without this local-development proxy target, the Operator Console page cannot reach the local Central PMS API during the smoke test.

## Observed Live Values

Observed summary endpoint values:

| Field | Value |
| --- | ---: |
| `totalTargets` | `1` |
| `enabledTargets` | `0` |
| `disabledTargets` | `1` |
| `healthyTargets` | `1` |
| `degradedTargets` | `0` |
| `failingTargets` | `0` |
| `unknownTargets` | `0` |
| `staleTargets` | `0` |
| `targetsWithLastFailure` | `1` |
| `latestSuccessfulProjectionSyncAt` | `2026-06-22T02:58:33.907197+00:00` |
| `totalActiveProjections` | `13` |
| `totalExitedProjections` | `6` |
| `SchedulerEnabled` | `true` |
| `DegradedResolveFallbackEnabled` | `false` |
| `MaxProjectionAgeMinutes` | `30` |
| `MaxParallelSiteJobs` | `1` |
| `SchedulerScanIntervalSeconds` | `60` |

Observed UI values included:

| Field | Value |
| --- | ---: |
| Total targets | `1` |
| Enabled | `0` |
| Disabled | `1` |
| Healthy | `1` |
| Failing | `0` |
| Active projections | `13` |
| Exited projections | `6` |
| Scheduler enabled | `true` |
| Degraded fallback enabled | `false` |
| Max projection age minutes | `30` |
| Max parallel site jobs | `1` |
| Scheduler scan interval seconds | `60` |

## Boundary Confirmations

The smoke test confirmed:

- Projection health visibility is read-only.
- Projection data remains non-authoritative continuity visibility only.
- Vendor PMS remains parking-session authority.
- Vendor PMS remains tariff authority.
- ExitPass remains payment authority.
- No payment action was exposed.
- No tariff action was exposed.
- No exit authorization action was exposed.
- No scheduler enable/disable control was exposed.
- No sync trigger control was exposed.
- No degraded fallback toggle was exposed.
- No secrets were displayed.
- No raw HikCentral payloads were displayed.

## Known Limitations And Notes

The Operator readiness panel showed a blocked local readiness state during the smoke test.

That readiness state is separate from the Projection Health page and did not block the projection health smoke result. The Projection Health page itself loaded and displayed live backend data through the Vite proxy.

The target was disabled during the smoke result (`enabledTargets = 0`, `disabledTargets = 1`) while still reporting healthy historical projection sync state. This matches the safe post-UAT operating posture.

## Recommended Next Steps

- Keep the Vite proxy note available for local Operator Console smoke testing.
- Re-run the smoke test after any Operator Console routing, authorization, or projection health API changes.
- Add browser-level automated smoke coverage if the Operator Console test harness later supports authenticated local-dev RBAC headers.
- Continue keeping degraded fallback disabled unless separately approved for a guarded test or operations-approved degraded mode.
