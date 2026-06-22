# HikCentral Real Sync Target Deployment Handoff

## Purpose

This handoff is the practical checklist for configuring HikCentral vendor session projection sync targets in a real environment and validating Operator Console health visibility.

Projection data is a latest-known continuity snapshot/read model only. It is not parking-session authority, tariff authority, payment authority, payment finality, or exit authorization authority.

## Scope

In scope:

- Discover real HikCentral parking lot index codes.
- Map ExitPass site, site group, and vendor system identifiers.
- Upsert one site-scoped and parking-lot-scoped sync target.
- Keep the target disabled until validation is complete.
- Enable exactly one intended target when ready.
- Designate exactly one scheduler-enabled Central PMS instance per environment.
- Verify the first projection sync.
- Validate the Operator Console Projection Health page.
- Restore a safe state if validation fails or must be paused.

Out of scope:

- Payment creation.
- Tariff calculation from projection data.
- Exit authorization.
- Vendor payment acknowledgment UAT script expansion.
- HikCentral credential rotation, except through the approved secret channel.
- Database schema changes.

## Pre-Deployment Prerequisites

Confirm before creating or enabling any sync target:

- Central PMS is deployed and connected to the target database.
- HikCentral is reachable from the Central PMS runtime network.
- HikCentral OpenAPI is enabled.
- HikCentral `AppKey` and `AppSecret` are available only through the approved secret channel.
- `CentralPms:VendorPms:Provider=HikCentral`.
- HikCentral `BaseUrl` is confirmed with the correct scheme and port.
- Projection tables exist:
  - `sessions.vendor_session_projections`
  - `sessions.vendor_session_projection_sync_targets`
- If the database predates the projection schema, apply `docs/sql/HikCentralProjectionSchemaPatch.sql` through the approved deployment process before target setup.
- Operator Console can reach Central PMS through the correct proxy or base URL.
- Projection health visibility RBAC exists through one of the accepted permissions:
  - `operator-console.vendor-projection-health.view`
  - mapped ops permission such as `ops.vendor-session-projection-health.view`

Do not store real credentials in docs, SQL helpers, screenshots, or logs.

## Real Identifier Discovery Checklist

Find and record the real environment values:

- `site_group_id`
- `site_id`
- `vendor_system_id`
- `vendor_code = HIKCENTRAL`
- `environment_code`
- HikCentral `parkingLotIndexCode`
- HikCentral `parkingLotName`

UAT identifiers are examples only. Do not reuse them in production unless they were intentionally seeded for that exact environment:

| Field | UAT example only |
| --- | --- |
| `site_id` | `c9000000-0000-0000-0000-000000000001` |
| `site_group_id` | `ce000000-0000-0000-0000-000000000001` |
| `vendor_system_id` | `31bde78a-5dfc-45c3-a1f3-e48abaf90927` |
| `parking_lot_index_code` | `1` |
| `parking_lot_name` | `TEST SITE` |

Recommended checks:

- Confirm the site and site group from the target environment's site reference data.
- Confirm the vendor system row maps to HikCentral and the correct environment.
- Confirm `vendor_code = HIKCENTRAL`.
- Confirm the environment code is the target environment, not UAT unless this is a UAT deployment.
- Confirm the parking lot directly from HikCentral, not from a previous runbook example.

## HikCentral Parking Lot Confirmation

Use HikCentral parking lot list before creating the sync target:

```http
POST /artemis/api/vehicle/v1/parkinglot/list
```

Expected useful response fields:

- `parkingLotIndexCode`
- `parkingLotName`
- `totalSpaceNum`
- `freeSpaceNum`

Example UAT response only:

```json
{
  "code": "0",
  "msg": "Success",
  "data": {
    "total": 1,
    "list": [
      {
        "parkingLotIndexCode": "1",
        "parkingLotName": "TEST SITE",
        "parentParkingLotIndexCode": "",
        "totalSpaceNum": 10000,
        "freeSpaceNum": 9963,
        "totalPermanentSpaceNum": -1,
        "freePermanentSpaceNum": -1,
        "maxParkingTime": -1
      }
    ]
  }
}
```

Record the real `parkingLotIndexCode` and `parkingLotName`. Do not infer production values from the UAT sample.

## Sync Target Setup Checklist

Use:

```text
docs/sql/HikCentralProjectionSyncTargetOps.sql
```

Checklist:

- List current sync targets.
- Verify no global or weakly scoped targets exist.
- Set SQL variables for the real `site_id`, `site_group_id`, `vendor_system_id`, `parking_lot_index_code`, and `parking_lot_name`.
- Upsert one target disabled by default.
- Review the returned target row before enablement.
- Confirm `enabled_flag = false` after upsert.
- Confirm `health_status = DISABLED` or the expected safe pre-run state.
- Enable the target only when the scheduler deployment plan is ready.
- Verify only the expected target is enabled.
- Keep `DegradedResolveFallbackEnabled=false`.
- Keep `SchedulerEnabled=false` until the designated scheduler instance is selected.

The sync target must be site-scoped and parking-lot-scoped. Do not create global sync targets.

## Scheduler Deployment Checklist

The architecture is one scheduler service, many site-scoped jobs.

Checklist:

- Select exactly one Central PMS instance per environment to run projection scheduler jobs.
- Set `CentralPms__VendorSessionProjections__SchedulerEnabled=true` only on that instance.
- Set `CentralPms__VendorSessionProjections__SchedulerEnabled=false` on all other Central PMS instances.
- Keep `CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled=false` by default.
- Review `MaxProjectionAgeMinutes` and confirm it is within the approved environment threshold.
- Review `MaxParallelSiteJobs`.
- Review `SchedulerScanIntervalSeconds`.
- Review target `poll_interval_seconds`, `lookback_window_minutes`, and `page_size`.
- Start the scheduler instance and watch the first run.

There is no distributed scheduler lock in the current implementation. Do not run multiple scheduler-enabled Central PMS instances for the same environment.

## First Sync Validation Checklist

During and after the first scheduler pass, confirm:

- Scheduler logs show the target was loaded.
- Logs include target scope:
  - `projection_sync_target_id`
  - `site_id`
  - `site_group_id`
  - `vendor_system_id`
  - `parking_lot_index_code`
- HikCentral passageway API returns `code=0`.
- `records_seen > 0` when passageway records exist for the lookback window.
- `records_projected >= 0`.
- `records_skipped` is reviewed if nonzero.
- `records_upserted` is reviewed.
- Target health becomes `HEALTHY`.
- `last_success_at` is populated.
- `failure_count = 0`.
- `last_error_code IS NULL`.
- `last_error_message IS NULL`.
- Logs do not contain `AppKey`, `AppSecret`, database passwords, raw payloads, or credential reference values.

If `records_seen > 0` and `records_projected = 0`, stop and compare the live HikCentral response shape with the projection normalizer expectations before enabling wider rollout.

## Database Verification Snippets

Replace placeholders before running. Do not paste real secrets into SQL.

### Target State

```sql
SELECT
    projection_sync_target_id,
    site_id,
    site_group_id,
    vendor_system_id,
    parking_lot_index_code,
    parking_lot_name,
    enabled_flag,
    poll_interval_seconds,
    lookback_window_minutes,
    page_size,
    health_status,
    failure_count,
    last_attempt_at,
    last_success_at,
    last_failure_at,
    last_error_code,
    last_error_message,
    updated_at
FROM sessions.vendor_session_projection_sync_targets
WHERE projection_sync_target_id = '<projection_sync_target_id>'::uuid;
```

### Projection Row Count By Parking Lot

```sql
SELECT
    parking_lot_index_code,
    count(*) AS projection_count,
    max(last_refreshed_at) AS latest_refreshed_at
FROM sessions.vendor_session_projections
WHERE site_id = '<site_id>'::uuid
  AND site_group_id = '<site_group_id>'::uuid
  AND vendor_system_id = '<vendor_system_id>'::uuid
  AND parking_lot_index_code = '<parking_lot_index_code>'
GROUP BY parking_lot_index_code;
```

### Active And Exited Projection Counts

```sql
SELECT
    projection_status,
    count(*) AS projection_count
FROM sessions.vendor_session_projections
WHERE site_id = '<site_id>'::uuid
  AND vendor_system_id = '<vendor_system_id>'::uuid
  AND parking_lot_index_code = '<parking_lot_index_code>'
GROUP BY projection_status
ORDER BY projection_status;
```

### Latest Refreshed Rows

```sql
SELECT
    vendor_session_projection_id,
    vendor_record_guid,
    card_num,
    plate_license,
    projection_status,
    enter_time,
    exit_time,
    last_refreshed_at,
    now() - last_refreshed_at AS projection_age
FROM sessions.vendor_session_projections
WHERE site_id = '<site_id>'::uuid
  AND vendor_system_id = '<vendor_system_id>'::uuid
  AND parking_lot_index_code = '<parking_lot_index_code>'
ORDER BY last_refreshed_at DESC
LIMIT 25;
```

### Stale / Freshness Check

```sql
SELECT
    projection_sync_target_id,
    enabled_flag,
    health_status,
    last_success_at,
    now() - last_success_at AS last_success_age,
    failure_count,
    last_error_code,
    last_error_message
FROM sessions.vendor_session_projection_sync_targets
WHERE site_id = '<site_id>'::uuid
  AND vendor_system_id = '<vendor_system_id>'::uuid
  AND parking_lot_index_code = '<parking_lot_index_code>';
```

### Card Lookup Check

```sql
SELECT
    vendor_session_projection_id,
    parking_lot_index_code,
    card_num,
    projection_status,
    enter_time,
    exit_time,
    last_refreshed_at
FROM sessions.vendor_session_projections
WHERE site_id = '<site_id>'::uuid
  AND vendor_system_id = '<vendor_system_id>'::uuid
  AND parking_lot_index_code = '<parking_lot_index_code>'
  AND card_num = '<card_num>'
ORDER BY last_refreshed_at DESC
LIMIT 10;
```

### No PaymentAttempt From Projection Data

Use the environment's approved payment audit query if available. At minimum, verify no new payment attempts were created during projection-only validation:

```sql
SELECT
    count(*) AS payment_attempts_created_during_window
FROM payments.payment_attempts
WHERE created_at >= '<validation_started_at_utc>'::timestamptz
  AND created_at <= '<validation_completed_at_utc>'::timestamptz;
```

Expected for projection-only validation: `0`, unless another approved payment test was intentionally running in the same window.

### No ExitAuthorization From Projection Data

Use the environment's approved exit authorization audit query if available. At minimum:

```sql
SELECT
    count(*) AS exit_authorizations_created_during_window
FROM exits.exit_authorizations
WHERE created_at >= '<validation_started_at_utc>'::timestamptz
  AND created_at <= '<validation_completed_at_utc>'::timestamptz;
```

Expected for projection-only validation: `0`, unless another approved exit authorization test was intentionally running in the same window.

## Operator Console Validation Checklist

Local development smoke:

```text
VITE_OPERATOR_CONSOLE_API_PROXY_TARGET=http://localhost:56065
```

Open:

```text
/operator-console/vendor-session-projections/health
```

Confirm:

- Projection summary cards are visible.
- Projection target table is visible.
- The real parking lot target is visible.
- Health status is visible.
- Stale/fresh indicator is visible.
- Latest projection records or counts are visible.
- Safe config visibility is visible:
  - `SchedulerEnabled`
  - `DegradedResolveFallbackEnabled`
  - `MaxProjectionAgeMinutes`
  - `MaxParallelSiteJobs`
  - `SchedulerScanIntervalSeconds`
- Non-authoritative projection warning is visible.
- No mutation controls are visible:
  - no sync-now button
  - no enable button
  - no disable button
  - no fallback toggle
  - no payment action
  - no tariff action
  - no exit authorization action
- No secrets are visible:
  - no `AppKey`
  - no `AppSecret`
  - no database password
  - no raw HikCentral payload

Raw PowerShell calls without local-dev RBAC headers should return `401 Unauthorized`. That is expected and confirms the ops endpoints are protected.

The global Operator readiness panel evaluates controlled actions such as `SESSION_LOOKUP`. It may show blocked in local smoke when site, site-group, device, shift, or production-trust fixture data is absent. That does not invalidate Projection Health page success if the projection-health data loads through RBAC.

## Degraded Fallback Guardrail

Do not enable degraded fallback during normal deployment.

Projection sync health and degraded resolve fallback are separate controls:

- Projection sync keeps the continuity read model fresh.
- Degraded fallback affects resolve behavior only during live vendor unavailability and only when explicitly enabled.

Rules:

- Keep `CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled=false`.
- Enable fallback only during an approved guarded test.
- Projection fallback must return non-authoritative HTTP 503 continuity visibility.
- Projection fallback must not return a normal resolved tariff response.
- Projection fallback must not create `PaymentAttempt`.
- Projection fallback must not calculate tariff.
- Projection fallback must not issue `ExitAuthorization`.
- Restore `DegradedResolveFallbackEnabled=false` immediately after the guarded test.

## Rollback / Safe Shutdown

To pause projection polling safely:

- Disable the specific sync target using `docs/sql/HikCentralProjectionSyncTargetOps.sql`.
- Set `CentralPms__VendorSessionProjections__SchedulerEnabled=false` on the designated scheduler instance if all scheduler activity should stop.
- Keep HikCentral `BaseUrl`, `AppKey`, and `AppSecret` unchanged unless rotating through the approved secret channel.
- Preserve projection rows unless there is a documented data correction reason.
- Do not delete projection data casually; it is continuity visibility evidence and helps support degraded operations.

After rollback:

- Verify target `enabled_flag=false`.
- Verify scheduler config is disabled where intended.
- Verify Operator Console shows the target as disabled.
- Verify degraded fallback remains disabled.

## Troubleshooting

### 401 On Ops Endpoints

Expected for unauthenticated calls. Use the approved ops/operator RBAC path. For local development, include the required Operator Console headers and `operator-console.vendor-projection-health.view` permission.

### Operator Console Proxy Target Wrong

Symptom: Operator Console loads but `/v1` API calls fail or hit the wrong local service.

Check:

```text
VITE_OPERATOR_CONSOLE_API_PROXY_TARGET=http://localhost:56065
```

The Vite default proxy target may be `localhost:8082`. Do not confuse the proxy setting with `VITE_CENTRAL_PMS_BASE_URL`.

### Wrong HikCentral HTTP/HTTPS Scheme

Symptom: connection refused, TLS error, timeout, or vendor unavailable.

Check whether the HikCentral OpenAPI endpoint expects `http` or `https` in `CentralPms__VendorPms__HikCentral__BaseUrl`.

### Wrong HikCentral Port

Symptom: HikCentral host resolves but OpenAPI calls fail.

Check the exact HikCentral OpenAPI port from the vendor deployment. Confirm the Central PMS runtime network can reach it.

### HikCentral Reachable But Raw Check Returns `code=64`

This commonly indicates an authentication/signature or permission problem for raw or unauthenticated checks. Confirm the call is made through the approved adapter/signing path and that the AppKey has permission for the parking APIs.

### Response Mapping Mismatch

Symptoms:

- `records_seen > 0`
- `records_projected = 0`
- `records_skipped > 0`

Check that live records include expected actual HikCentral fields:

- `parkingLotInfo.parkingLotIndexCode`
- `parkingLotInfo.parkingLotName`
- `passagewayInfo.passagewayIndexCode`
- `personInfo.cardNum`
- `carInfo.EnterTime`
- `carInfo.ExitTime`

### Npgsql DateTimeOffset UTC Issue

Symptom: Npgsql rejects non-UTC `DateTimeOffset` values for `timestamptz`.

Expected behavior: HikCentral `+08:00` timestamps must be converted to UTC before PostgreSQL binding.

### Stale Projection Fallback

Symptom: fallback finds a projection but does not provide usable continuity visibility.

Check:

- `last_refreshed_at`
- `MaxProjectionAgeMinutes`
- projection status
- target `last_success_at`

Do not widen freshness thresholds without operations approval.

### Scheduler `targets_loaded=0`

Check:

- target `enabled_flag`
- target site/vendor/parking-lot scope
- scheduler instance has database connectivity
- `SchedulerEnabled=true` only on the designated scheduler instance
- no environment mismatch between the database and Central PMS runtime

### `records_seen > 0` But `records_projected=0`

Check response mapping first. Also check required stable identity fields:

- `guid`, preferred
- `parkingLotIndexCode + cardNum + EnterTime`, fallback
- valid plate identity only as a non-ticket fallback when allowed by the current implementation

`plateLicense = Unknown` must not be treated as a real plate identity.

### Target Health `FAILING`

Review:

- `last_error_code`
- `last_error_message`
- HikCentral reachability
- HikCentral authentication
- database write permission
- timestamp UTC conversion errors
- response mapping errors

Escalate to engineering if the error is database, mapping, or timestamp related. Escalate to HikCentral/vendor support if the error is authentication, permission, or upstream availability related.

### Duplicate Scheduler Instances

Symptoms:

- duplicate logs for the same target at the same time
- unexpected scheduler load
- concurrent target updates from multiple Central PMS instances

Fix:

- Set `SchedulerEnabled=false` on all non-designated Central PMS instances.
- Leave exactly one scheduler-enabled instance per environment.
- Verify enabled targets are the expected site-scoped targets only.

## Final Deployment Sign-Off Checklist

Sign off only after confirming:

- Real identifiers confirmed.
- `parkingLotIndexCode` confirmed from HikCentral parking lot list.
- Target upserted disabled first.
- Target enabled intentionally.
- Exactly one scheduler instance enabled.
- Degraded fallback disabled.
- First sync successful.
- Target health is `HEALTHY`.
- Operator Console Projection Health page verified.
- No secrets in docs, logs, or screenshots.
- No `PaymentAttempt` created from projection validation.
- No `ExitAuthorization` issued from projection validation.
- Vendor PMS remains parking-session authority.
- Vendor PMS remains tariff authority.
- ExitPass remains payment authority.
- Projection remains non-authoritative continuity/read-model data.

## Cross References

- `docs/hikcentral-projection-production-controls.md`
- `docs/hikcentral-projection-resolve-uat-results.md`
- `docs/hikcentral-operator-console-projection-health-smoke.md`
- `docs/sql/HikCentralProjectionSyncTargetOps.sql`
- `docs/diagrams/hikcentral-normal-resolve-flow.puml`
- `docs/diagrams/hikcentral-degraded-projection-fallback-flow.puml`
