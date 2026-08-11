# HikCentral Projection Safe Local Enablement

## Decision

The current implementation is prepared for one explicitly mapped `HikCentralLocal` target. Live polling remains operator-gated because this repository contains no approved local endpoint, credentials, database identity, Site mapping, Vendor System mapping, or parking-lot index.

Production configuration is unchanged. The WebPay statutory-discount walkthrough remains on its seeded `Production`-named fixture composition and does not load `appsettings.HikCentralLocal.json`.

## Safety Controls

- Only HTTP-success, application-code `0`, structurally complete pages can become successful projections.
- A genuine empty collection is a successful `ZERO_ROWS` result; transport, protocol, access, application, JSON, pagination, mapping, and persistence failures are not.
- Projection batches commit in one PostgreSQL transaction.
- One SHA-256-derived, target-scoped PostgreSQL advisory lock spans the projection operation and health update.
- New target rows default to disabled. Scheduler enablement does not enable target rows.
- Enabled targets must poll every 60 seconds.
- Freshness uses `last_success_at`, the completed-success timestamp. `CURRENT`, `DELAYED`, `STALE`, `NEVER_SYNCHRONIZED`, `FAILED`, `DISABLED`, and `LOCK_CONTENDED_DEFERRED` are distinct.
- Degraded lookup joins the projection row to its enabled target and derives age from target `last_success_at`; a refreshed row without a completed successful target cycle is not usable fallback data.
- The default degraded maximum is one minute. Degraded resolution remains disabled unless separately authorized.
- Errors store and log bounded classifications only. No upstream body, credential, signature, or personal record is emitted.
- Projection readiness exposes aggregates only, and operator health detail strips vendor-record, card, and plate identifiers.
- Manual synchronization requires `ops.vendor-session-projection-sync.execute` through the named `VendorSessionProjectionSyncOperator` policy.

## Database Preparation

Apply and validate through the governed database workflow:

```text
infra/db/patches/ExitPass_HikCentralProjectionSafety_v1.3.sql
infra/db/patches/validation/Validate_HikCentralProjectionSafety_v1.3.sql
```

Create or update the intended target with `docs/sql/HikCentralProjectionSyncTargetOps.sql`. Keep it disabled until the exact Site, Site Group, Vendor System, and parking-lot mapping are verified. Set its poll interval to `60`, disable every other target, then explicitly enable only the intended target.

## Presence-Only Activation Checks

In a dedicated PowerShell process, verify presence without printing values:

```powershell
$required = @(
  "ConnectionStrings__MainDatabase",
  "CentralPms__VendorPms__HikCentral__BaseUrl",
  "CentralPms__VendorPms__HikCentral__AppKey",
  "CentralPms__VendorPms__HikCentral__AppSecret"
)
$required | ForEach-Object {
  [pscustomobject]@{ Name = $_; Present = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, "Process")) }
}
```

Refuse activation unless the endpoint is confirmed non-Production, the database is loopback and approved for local development, exactly one target is enabled, no walkthrough state exists, and no other `HikCentralLocal` scheduler process is running.

## Controlled Startup

After those facts are independently confirmed and the operator authorizes live polling:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\v1.3\hikcentral\Start-HikCentralLocalProjection.ps1 `
  -ExpectedDatabaseName "<approved-local-database-name>" `
  -SiteId "<approved-test-site-id>" `
  -SiteGroupId "<approved-test-site-group-id>" `
  -VendorSystemId "<approved-local-vendor-system-id>" `
  -ParkingLotIndexCode "<approved-test-parking-lot-index>" `
  -AcknowledgeNonProductionEndpoint
```

The script uses only current `CentralPms__VendorPms__HikCentral__*` names, rejects legacy `HIKCENTRAL__*` variables, and does not print configuration values. It does not supply secrets.

## Runtime Evidence

Successful operation requires all of the following:

- startup validation passes before scheduler execution;
- readiness reports the required target as `CURRENT`;
- `last_attempt_at` and `last_success_at` advance after a completed cycle;
- a genuine zero-row cycle records success with zero committed records;
- failures preserve the prior `last_success_at` and expose only a bounded failure classification;
- lock contention increments its own counter without incrementing adapter failure count;
- projection records retain stable Site, Site Group, Vendor System, parking-lot, and stable upstream identity keys;
- no raw upstream body, credential, signature, or personal data appears in logs.

No authenticated HikCentral request was made during implementation or automated validation.
