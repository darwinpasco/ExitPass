# HikCentral Projection Scheduler Live UAT

This runbook validates the Central PMS vendor session projection scheduler against a live HikCentral environment. It is configuration and UAT only. It must not change payment, tariff, parking-session, or exit-authority behavior.

## Repository Findings

- Central PMS base appsettings do not contain HikCentral live configuration.
- `appsettings.Development.json` contains only local placeholder database/observability settings.
- Central PMS reads the live adapter from `CentralPms:VendorPms`.
- The scheduler/fallback options are read from `CentralPms:VendorSessionProjections`.
- The project does not currently define a Central PMS `UserSecretsId`, so environment variables or an approved secret store are the safest configuration path.
- The sync target table is `sessions.vendor_session_projection_sync_targets`.
- The projection read model is `sessions.vendor_session_projections`.

## Required Values

These values are required before live UAT can run:

| Value | Configuration key or table column | Secret | Source |
| --- | --- | --- | --- |
| HikCentral base URL | `CentralPms__VendorPms__HikCentral__BaseUrl` | No | Vendor/UAT environment |
| HikCentral AppKey | `CentralPms__VendorPms__HikCentral__AppKey` | Yes | Vendor/secret store |
| HikCentral AppSecret | `CentralPms__VendorPms__HikCentral__AppSecret` | Yes | Vendor/secret store |
| Adapter provider | `CentralPms__VendorPms__Provider=HikCentral` | No | UAT runtime config |
| Parking lot index code | `parking_lot_index_code` | No | HikCentral parking lot mapping |
| Parking lot name | `parking_lot_name` | No | HikCentral parking lot mapping |
| `site_id` | `sessions.vendor_session_projection_sync_targets.site_id` | No | Existing `sites.sites` row |
| `site_group_id` | `sessions.vendor_session_projection_sync_targets.site_group_id` | No | Existing `sites.site_groups` row |
| `vendor_system_id` | `sessions.vendor_session_projection_sync_targets.vendor_system_id` | No | Existing `integration.vendor_systems` row |
| Poll interval | `poll_interval_seconds` | No | UAT choice; suggested `300` |
| Lookback window | `lookback_window_minutes` | No | UAT choice; suggested `180` |
| Page size | `page_size` | No | UAT choice; suggested `100` |
| Scheduler flag | `CentralPms__VendorSessionProjections__SchedulerEnabled` | No | UAT runtime config |
| Degraded fallback flag | `CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled` | No | Keep `false` unless separately approved |

## Safe Environment Template

Use `docs/hikcentral-projection-live-uat.env.template` as the local template. Keep real values in a local shell, deployment secret store, or approved UAT configuration channel only.

Minimum UAT scheduler configuration:

```powershell
$env:CentralPms__VendorPms__Provider = "HikCentral"
$env:CentralPms__VendorPms__HikCentral__BaseUrl = "https://<hikcentral-host>"
$env:CentralPms__VendorPms__HikCentral__AppKey = "<secret>"
$env:CentralPms__VendorPms__HikCentral__AppSecret = "<secret>"
$env:CentralPms__VendorPms__HikCentral__UserId = "exitpass-adapter"

$env:CentralPms__VendorSessionProjections__SchedulerEnabled = "true"
$env:CentralPms__VendorSessionProjections__DefaultPollIntervalSeconds = "300"
$env:CentralPms__VendorSessionProjections__DefaultLookbackWindowMinutes = "180"
$env:CentralPms__VendorSessionProjections__DefaultPageSize = "100"
$env:CentralPms__VendorSessionProjections__MaxParallelSiteJobs = "2"
$env:CentralPms__VendorSessionProjections__StartupDelaySeconds = "30"
$env:CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled = "false"
```

Do not set `HIKCENTRAL_CONFIRM_PAYMENT_ENABLED=true` for this UAT. This projection validation does not require payment confirmation, fee finality, or gate commands.

## Seed One Scoped Target

Use `docs/sql/HikCentralProjectionLiveUat.sql`.

1. Run the preflight queries to identify the correct `site_id`, `site_group_id`, and `vendor_system_id`.
2. Replace the placeholders in the idempotent upsert.
3. Run only one parking-lot-scoped target for this UAT.
4. Confirm the target row is enabled and has the intended interval, lookback, and page size.

The upsert uses `ON CONFLICT (site_id, vendor_system_id, parking_lot_index_code)` and does not create a global sync target.

## Manual Sync

Start Central PMS with the live HikCentral adapter configuration and scheduler options. Then run one scoped manual sync:

```powershell
$body = @{
    siteId = "<site-id>"
    parkingLotIndexCode = "<parking-lot-index-code>"
    lookbackWindowMinutes = 180
    pageSize = 100
    force = $true
} | ConvertTo-Json

Invoke-RestMethod `
    -Method Post `
    -Uri "$env:EXITPASS_CENTRAL_PMS_BASE_URL/v1/internal/vendor-session-projections/sync" `
    -ContentType "application/json" `
    -Body $body
```

If internal mTLS is enabled in the UAT host, use the approved client certificate and internal-service calling convention for that environment.

Expected result fields:

- `succeeded = true`
- `recordsRead >= 0`
- `recordsUpserted >= 0`
- `recordsSkipped >= 0`
- `startedAt`
- `completedAt`
- target `siteId` and `parkingLotIndexCode`

If HikCentral returns zero records for the lookback window, widen the lookback window after confirming with the operator that this remains read-only and acceptable.

## Database Verification

Run the verification sections in `docs/sql/HikCentralProjectionLiveUat.sql` after the manual sync and again after a repeated sync.

Confirm:

- target exists and is enabled
- `last_attempt_at` is updated
- `last_success_at` is updated on success, or `last_failure_at` and error fields are populated on failure
- projection rows exist for the parking lot when HikCentral returns records
- `card_num` is populated where HikCentral supplies `personInfo.cardNum`
- active rows have `projection_status = 'ACTIVE'`
- exited rows have `projection_status = 'EXITED'`
- `last_refreshed_at` freshness age is visible
- duplicate stable identity query returns zero rows after repeated sync

## Ticket/Card UAT

Candidate card numbers from earlier testing:

- `3518855073102`
- `3518855085105`
- `3519278781100`
- `3519281044100`

Do not assume these are active. Use them only as lookup candidates. Validate with the SQL card lookup query in `docs/sql/HikCentralProjectionLiveUat.sql`.

## Scheduler UAT

After manual sync succeeds:

1. Keep `CentralPms__VendorSessionProjections__SchedulerEnabled=true`.
2. Confirm only one Central PMS scheduler instance is running for the UAT environment.
3. Wait at least one `poll_interval_seconds` plus one scheduler scan interval.
4. Re-run target health and projection freshness queries.

Expected:

- the same site/parking-lot target is refreshed
- target health stays `HEALTHY` after successful runs
- repeated runs update `last_seen_at`/`last_refreshed_at` without duplicate projection identities

## Degraded Resolve Fallback

Keep `CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled=false` during normal scheduler UAT.

Only test fallback if a separate safe window is approved. Safe fallback testing requires:

- live vendor lookup is intentionally unavailable in a controlled way
- fallback flag is temporarily enabled
- payment creation path is not exercised from projection-only data
- response is verified as `source = projection_snapshot`
- response is verified as `non_authoritative = true`
- no tariff is calculated
- no `PaymentAttempt` is created
- no `ExitAuthorization` is issued
- no ticket is marked paid

If forcing live vendor unavailability would affect normal operations, do not run fallback UAT. Document the planned test instead.

## Stop Conditions

Stop the UAT if:

- required live values are missing
- HikCentral credentials are accidentally printed in logs or terminal output
- the sync target would be global or not site scoped
- fallback appears during normal live lookup success
- projection data is used to calculate tariff, create payment, mark paid, or issue exit authorization
