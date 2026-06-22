# HikCentral Projection Production Controls

This runbook describes the controlled production configuration for HikCentral vendor session projection sync and guarded degraded resolve fallback.

Projection data is a latest-known continuity snapshot only. It is not parking-session authority, tariff authority, payment authority, payment finality, or exit authorization authority.

## Scope

Use this guide to:

- Configure Central PMS HikCentral projection settings safely per environment.
- Enable one centralized scheduler instance for many site-scoped sync targets.
- Create, enable, disable, and verify site-scoped sync targets.
- Keep degraded fallback disabled by default.
- Temporarily test degraded fallback under approval and restore safe settings afterward.
- Verify health, freshness, and authority boundaries.

Do not use this guide to create payment attempts, calculate tariffs from projections, issue exit authorization, or mutate HikCentral sessions.

## Safe Defaults

Central PMS safe defaults are:

| Setting | Default | Production control |
| --- | ---: | --- |
| `CentralPms__VendorSessionProjections__SchedulerEnabled` | `false` | Enable only in the single Central PMS instance assigned to run projection jobs. |
| `CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled` | `false` | Keep disabled unless a guarded fallback test or operations-approved degraded mode is active. |
| `DefaultPollIntervalSeconds` | `300` | Must be between `30` and `86400`. |
| `DefaultLookbackWindowMinutes` | `180` | Must be between `1` and `10080`. |
| `DefaultPageSize` | `100` | Must be between `1` and `500`. |
| `MaxParallelSiteJobs` | `2` | Must be between `1` and `16`. |
| `StartupDelaySeconds` | `30` | Must be between `0` and `3600`. |
| `SchedulerScanIntervalSeconds` | `30` | Must be between `15` and `3600`. |
| `MaxPagesPerRun` | `20` | Must be between `1` and `100`. |
| `MaxProjectionAgeMinutes` | `60` | Must be between `1` and `10080`. |
| `FailingFailureCountThreshold` | `3` | Must be between `1` and `100`. |

Central PMS validates these values at scheduler startup and before manual projection sync orchestration. Invalid values fail clearly with a `CentralPms:VendorSessionProjections` configuration error.

## Required Environment Variables

Configure these through environment variables, user secrets, or the approved deployment secret store. Do not commit real values.

```text
CentralPms__VendorPms__Provider=HikCentral
CentralPms__VendorPms__HikCentral__BaseUrl=https://<hikcentral-host-or-host:port>
CentralPms__VendorPms__HikCentral__AppKey=<secret>
CentralPms__VendorPms__HikCentral__AppSecret=<secret>
CentralPms__VendorPms__HikCentral__UserId=exitpass-adapter

CentralPms__VendorSessionProjections__SchedulerEnabled=false
CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled=false
CentralPms__VendorSessionProjections__DefaultPollIntervalSeconds=300
CentralPms__VendorSessionProjections__DefaultLookbackWindowMinutes=180
CentralPms__VendorSessionProjections__DefaultPageSize=100
CentralPms__VendorSessionProjections__MaxParallelSiteJobs=2
CentralPms__VendorSessionProjections__StartupDelaySeconds=30
CentralPms__VendorSessionProjections__SchedulerScanIntervalSeconds=30
CentralPms__VendorSessionProjections__MaxPagesPerRun=20
CentralPms__VendorSessionProjections__MaxProjectionAgeMinutes=60
CentralPms__VendorSessionProjections__FailingFailureCountThreshold=3
```

Production may use different numeric values within the accepted bounds. Degraded fallback should remain `false` unless separately approved.

## One Scheduler Instance

The architecture is one scheduler service, many site-scoped jobs.

Only one Central PMS instance per environment should run the scheduler loop. In scaled deployments:

- Set `CentralPms__VendorSessionProjections__SchedulerEnabled=true` on exactly one designated scheduler-capable Central PMS instance.
- Set `CentralPms__VendorSessionProjections__SchedulerEnabled=false` on all other Central PMS API instances.
- Keep all instances capable of serving the manual internal sync endpoint if internal auth allows it.
- Verify enabled sync targets are site scoped and parking-lot scoped before enabling the scheduler instance.

There is no distributed scheduler lock in the current implementation. Do not run multiple scheduler-enabled Central PMS instances for the same environment unless a future advisory-lock control is added and tested.

## Sync Target Operations

Use:

```text
docs/sql/HikCentralProjectionSyncTargetOps.sql
```

The helper supports:

- Listing current targets.
- Verifying no global or weakly scoped target exists.
- Creating or updating one site-scoped target disabled by default.
- Enabling one expected target.
- Disabling one target.
- Verifying only expected targets are enabled.
- Checking target health and projection freshness.
- Checking duplicate stable identity behavior.

The UAT identifiers in the helper comments are examples only. Production target values must come from production site, site group, vendor system, and HikCentral parking lot mapping records.

## Confirm Parking Lot Mapping

Confirm the correct HikCentral parking lot before creating or enabling a target:

```http
POST /artemis/api/vehicle/v1/parkinglot/list
```

Record the returned `parkingLotIndexCode` and `parkingLotName`, then use those values in the sync target. Do not infer production `parking_lot_index_code` from UAT.

## Enable Scheduler Safely

1. Verify the projection tables exist:
   - `sessions.vendor_session_projections`
   - `sessions.vendor_session_projection_sync_targets`
2. Apply `docs/sql/HikCentralProjectionSchemaPatch.sql` only if the database predates the projection schema.
3. Confirm HikCentral `BaseUrl`, `AppKey`, and `AppSecret` are supplied through the approved secret channel.
4. Confirm the target site, site group, vendor system, and parking lot from source systems.
5. Upsert the target with `enabled_flag=false`.
6. Enable only the intended target.
7. Set `SchedulerEnabled=true` only on the designated Central PMS scheduler instance.
8. Leave `DegradedResolveFallbackEnabled=false`.
9. Watch target health and logs for the first scheduler pass.

Expected scheduler success logs include:

- `projection_sync_target_id`
- `site_id`
- `site_group_id`
- `vendor_system_id`
- `parking_lot_index_code`
- `records_seen`
- `records_projected`
- `records_skipped`
- `records_upserted`
- `pages_pulled`

Failure logs include the target scope plus `error_code` and `error_message`. Logs must not contain `AppKey`, `AppSecret`, database passwords, or raw sensitive payloads.

## Disable Scheduler Or A Target

To stop all scheduled projection work in one Central PMS process, set:

```text
CentralPms__VendorSessionProjections__SchedulerEnabled=false
```

To stop polling one parking lot target while leaving the scheduler available for other targets, disable the target row using `docs/sql/HikCentralProjectionSyncTargetOps.sql`.

## Guarded Degraded Fallback Test

Keep fallback disabled for normal operation:

```text
CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled=false
```

For an approved degraded fallback test only:

1. Ensure no payment creation or exit authorization path will be exercised from projection-only data.
2. Disable scheduler if the test requires intentional HikCentral unavailability:
   `SchedulerEnabled=false`.
3. Temporarily set `DegradedResolveFallbackEnabled=true`.
4. Set `MaxProjectionAgeMinutes` to the approved freshness threshold.
5. Simulate vendor unavailability in the approved environment only.
6. Confirm resolve returns a projection snapshot response with:
   - `source = projection_snapshot`
   - `projection_based = true`
   - `non_authoritative = true`
   - freshness metadata
   - parking-session authority = Vendor PMS
   - tariff authority = Vendor PMS
   - payment authority = ExitPass
7. Confirm no `PaymentAttempt` is created.
8. Confirm no tariff is calculated from projection data.
9. Confirm no `ExitAuthorization` is issued.
10. Restore `DegradedResolveFallbackEnabled=false` and the real HikCentral `BaseUrl`.

If forcing vendor unavailability could affect normal operations, do not run the fallback test. Document the planned test instead.

## Secret Rotation

Rotate HikCentral `AppKey` and `AppSecret` through the approved secret store or deployment environment variables.

Do not store secrets in:

- SQL helper files.
- appsettings files committed to Git.
- UAT runbooks.
- console logs.
- screenshots.

After rotation, restart the Central PMS process using the HikCentral adapter and run a scoped manual sync against one approved target.

## Base URL Checks

Validate the HikCentral base URL before rollout:

- Confirm the scheme is correct: `http` versus `https`.
- Confirm the port is correct for the HikCentral OpenAPI deployment.
- Confirm the host is reachable from the Central PMS runtime network.
- Do not include credentials or query strings in `BaseUrl`.
- Confirm the adapter can reach:
  - `POST /artemis/api/vehicle/v1/parkinglot/list`
  - `POST /artemis/api/vehicle/v1/parkinglot/passageway/record`

Common UAT issue: using the wrong HTTP/HTTPS scheme or wrong port causes vendor unavailability even when credentials are valid.

## Health And Freshness Verification

Operators and support users can inspect read-only projection health through protected ops endpoints:

```http
GET /v1/ops/vendor-session-projections/targets
GET /v1/ops/vendor-session-projections/targets/{projectionSyncTargetId}
GET /v1/ops/vendor-session-projections/summary
```

These endpoints require Central PMS ops/operator RBAC permission through the `VendorSessionProjectionHealthViewer` policy. Accepted permissions include:

- `ops.vendor-session-projection-health.view`
- `operator-console.vendor-projection-health.view`

The endpoints are visibility only. They do not enable targets, disable targets, trigger sync, change scheduler settings, calculate tariff, create payment attempts, mark tickets paid, or issue exit authorization.

### Operator Console Projection Health Page

Operator Console includes a read-only page for the same projection health view:

```text
/operator-console/vendor-session-projections/health
```

The page shows:

- aggregate target totals, health buckets, stale target count, and active/exited projection counts
- target rows with site, site group, parking lot, enabled state, health status, freshness, last success/failure, failure count, safe last error fields, and projection counts
- target detail with safe metadata, safe configuration visibility, and limited latest projected rows
- warnings when degraded fallback is enabled
- warnings when targets are stale or failing

Operators may infer whether projection sync appears healthy, stale, failing, disabled, or needing escalation. Operators must not infer tariff finality, payment finality, parking-session authority, paid state, or exit authorization from projection data.

Escalate to engineering or vendor support when the page shows failing targets, stale enabled targets, recurring HikCentral errors, unexpected zero projected rows after records are seen, or degraded fallback enabled outside an approved test window.

Use the SQL helper to inspect:

- target `enabled_flag`
- target `health_status`
- target `failure_count`
- `last_attempt_at`
- `last_success_at`
- `last_failure_at`
- `last_error_code`
- `last_error_message`
- projection `last_refreshed_at`
- projection freshness age

Projection fallback must not silently use stale projection data. `MaxProjectionAgeMinutes` controls the freshness threshold for degraded fallback.

### Health Status Meaning

| Status | Meaning | Operator action |
| --- | --- | --- |
| `HEALTHY` | Last scheduler/manual sync for the target succeeded. | Continue monitoring freshness and projection counts. |
| `DEGRADED` | Target has started failing or missing expected freshness but has not crossed the failing threshold. | Check `last_error_code`, `last_error_message`, HikCentral reachability, and DB connectivity. |
| `FAILING` | Target has repeated sync failures. | Escalate to engineering and, when vendor errors are present, HikCentral/vendor support. |
| `DISABLED` | Target is configured but not polling. | Confirm this is intentional before expecting fresh projection data. |
| `UNKNOWN` | Target has not established health yet. | Run approved manual sync or wait for the scheduler if the target is enabled. |

### Stale Meaning

A target is stale when it is enabled and its latest projection `last_refreshed_at` is missing or older than `CentralPms:VendorSessionProjections:MaxProjectionAgeMinutes`.

Stale projection data is continuity visibility only. Operators must not infer fee finality, payment finality, parking-session authority, or exit authorization from stale projection data.

### Safe Configuration Visibility

The summary and target responses expose only safe projection control flags:

- `SchedulerEnabled`
- `DegradedResolveFallbackEnabled`
- `MaxProjectionAgeMinutes`
- `MaxParallelSiteJobs`
- `SchedulerScanIntervalSeconds`

The endpoints do not expose HikCentral `AppKey`, `AppSecret`, database passwords, raw payloads, or raw credential reference values.

### Escalation Guidance

Escalate to engineering when:

- `health_status = FAILING`
- enabled targets are stale beyond the approved freshness window
- `last_error_code` indicates database write/read failure
- records are seen but projected counts unexpectedly drop to zero
- projection fallback appears during normal live vendor success

Escalate to HikCentral/vendor support when:

- errors indicate HikCentral authentication failure
- the parking lot list does not contain the expected `parkingLotIndexCode`
- passageway record responses change shape or omit expected fields
- HikCentral is unreachable from the Central PMS runtime network

## Authority Boundary Verification

For production readiness checks, verify:

- Normal live resolve uses HikCentral live vendor data when available.
- Projection fallback is not used when live vendor lookup succeeds.
- Projection fallback returns HTTP 503 continuity metadata when live vendor lookup is unavailable and fallback is enabled.
- Projection fallback does not create `PaymentAttempt`.
- Projection fallback does not calculate tariff.
- Projection fallback does not record payment finality.
- Projection fallback does not issue `ExitAuthorization`.
- Projection fallback does not mark tickets as paid.

## Troubleshooting

### Database Password Or Authentication Failure

Symptoms:

- Central PMS fails to start.
- Scheduler cannot list targets.
- Manual sync returns a database-related server error.

Checks:

- Confirm `ConnectionStrings__MainDatabase` is supplied by the secret store.
- Confirm the database user can read and write `sessions.vendor_session_projection_sync_targets` and `sessions.vendor_session_projections`.
- Confirm the database has the projection schema. Apply `docs/sql/HikCentralProjectionSchemaPatch.sql` only if the tables are missing.

### Wrong HikCentral Scheme Or Port

Symptoms:

- Vendor adapter reports connection refused, timeout, or unavailable.
- Fallback test returns vendor unavailable immediately.

Checks:

- Validate `CentralPms__VendorPms__HikCentral__BaseUrl`.
- Confirm whether the environment expects `http` or `https`.
- Confirm port and network firewall rules.

### Response Mapping Mismatch

Symptoms:

- HikCentral returns records but `records_projected=0` and `records_skipped>0`.

Checks:

- Compare live response fields with the projection normalizer expectations.
- Confirm fields such as `parkingLotInfo.parkingLotIndexCode`, `personInfo.cardNum`, `carInfo.EnterTime`, and `carInfo.ExitTime` are present.
- Confirm `plateLicense = Unknown` is treated as a placeholder and not a real plate identity.

### DateTimeOffset UTC Issue

Symptoms:

- Npgsql rejects `DateTimeOffset` values with non-zero offsets for `timestamptz`.

Checks:

- Confirm projection persistence converts HikCentral `+08:00` timestamps to UTC.
- Confirm live resolve persistence converts vendor parking and tariff timestamps to UTC.

### Stale Projection Fallback

Symptoms:

- Fallback finds a projection but returns normal vendor unavailable behavior.

Checks:

- Compare projection `last_refreshed_at` to `MaxProjectionAgeMinutes`.
- Confirm the projection status is usable for fallback.
- Do not widen freshness thresholds without operations approval.

### Internal Manual Sync 401

Symptoms:

- `POST /v1/internal/vendor-session-projections/sync` returns `401`.

Checks:

- Use the approved internal mTLS or internal service identity convention.
- Confirm Central PMS internal RBAC allows the caller.
- Confirm request includes the required internal headers for the environment.

## Related Files

- `docs/hikcentral-projection-live-uat.md`
- `docs/hikcentral-projection-resolve-uat-results.md`
- `docs/sql/HikCentralProjectionLiveUat.sql`
- `docs/sql/HikCentralProjectionSchemaPatch.sql`
- `docs/sql/HikCentralProjectionSyncTargetOps.sql`
